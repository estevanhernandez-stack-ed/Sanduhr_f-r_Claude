import CoreServices
import Foundation

// MARK: The folders and the disk

/// Where each agent's turn files live. Injected, so tests point every one at a temp folder.
struct AgentHomes: Equatable {
    /// Codex homes (`CodexHomes`): each one's `sessions/**/rollout-*.jsonl` is followed.
    var codex: [URL]
    /// `~/.gemini/antigravity-cli`: its `brain/<conversation>/.system_generated/logs/transcript.jsonl`.
    var antigravity: URL
    /// Sanduhr's support folder, where Claude Code's hook writes `cc-signal-*.json`.
    var signals: URL

    /// This Mac's folders, for the app.
    static func live() -> AgentHomes {
        AgentHomes(codex: CodexHomes.live(), antigravity: AntigravityBurnReader.liveHome(), signals: HandoffFiles.support)
    }
}

/// What a Darwin post from Claude Code's hook found: signal files (the 2.47 hook, which takes the
/// new path), or none (an older hook, or a file the 100-file cap dropped), which takes the legacy glow.
enum ClaudeCodePostResult: Equatable, Sendable {
    case hadFiles
    case bare
}

extension ClaudeCodeSignalFile {
    static let namePrefix = "cc-signal-"

    /// `cc-signal-<unix ms>-<random>.json`.
    static func isSignalName(_ name: String) -> Bool {
        name.hasPrefix(namePrefix) && name.hasSuffix(".json")
    }

    /// The hook's temporary name before the rename: `.cc-signal-….json.tmp` (or without the dot,
    /// Windows' form). Removed once older than `maxAge`; a younger one may be mid-rename.
    static func isTempName(_ name: String) -> Bool {
        (name.hasPrefix(namePrefix) || name.hasPrefix("." + namePrefix)) && name.hasSuffix(".tmp")
    }
}

/// What a folder stream reports.
enum FolderChange: Equatable, Sendable {
    /// These files (or folders) changed.
    case paths([String])
    /// Events were dropped or coalesced: read the whole folder again.
    case rescan
    /// The stream is gone: the folder falls back to the poll.
    case failed
}

/// A live watch on one folder. FSEvents in the app, a fake in tests.
protocol FolderStream: AnyObject {
    func stop()
}

struct TurnFileInfo: Equatable {
    var size: UInt64
    var modified: Date
    var created: Date
}

struct TurnFile: Equatable {
    var url: URL
    var info: TurnFileInfo
}

/// Every way the watcher touches the disk, streams included.
protocol TurnFileSystem {
    func isFolder(_ url: URL) -> Bool
    /// A regular file's size and dates; nil for anything else or a missing file.
    func info(_ url: URL) -> TurnFileInfo?
    /// Regular files under `folder` whose name passes `named`.
    func files(under folder: URL, recursive: Bool, named: (String) -> Bool) -> [TurnFile]
    /// The names of the folders in `folder`.
    func folders(in folder: URL) -> [String]
    /// Up to `length` bytes from `offset`.
    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data
    func remove(_ url: URL)
    /// Antigravity's conversation id to `workspace_uris`, or nil when unreadable.
    func antigravitySummaries(home: URL) -> [String: String]?
    /// A stream on `folder`, nil when one could not start.
    func stream(_ folder: URL, _ onChange: @escaping (FolderChange) -> Void) -> FolderStream?
}

/// The real disk. The stream and the summaries lookup are injectable, so tests keep the real file
/// reads over temp folders and swap only FSEvents and SQLite.
struct TurnDisk: TurnFileSystem {
    var makeStream: (URL, @escaping (FolderChange) -> Void) -> FolderStream? = { FSEventsFolderStream.start($0, $1) }
    var summaries: (URL) -> [String: String]? = {
        AntigravitySummaries.read(dbPath: AntigravityBurnReader(home: $0).summariesPath)
    }

    private static let keys: [URLResourceKey] = [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey, .creationDateKey]

    func isFolder(_ url: URL) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue
    }

    private static func info(_ v: URLResourceValues) -> TurnFileInfo? {
        guard v.isRegularFile == true, let size = v.fileSize, let modified = v.contentModificationDate else { return nil }
        return TurnFileInfo(size: UInt64(size), modified: modified, created: v.creationDate ?? modified)
    }

    func info(_ url: URL) -> TurnFileInfo? {
        // resourceValues caches per URL instance; a fresh URL reads the disk.
        let fresh = URL(fileURLWithPath: url.path)
        guard let v = try? fresh.resourceValues(forKeys: Set(Self.keys)) else { return nil }
        return Self.info(v)
    }

    func files(under folder: URL, recursive: Bool, named: (String) -> Bool) -> [TurnFile] {
        let fm = FileManager.default
        var urls: [URL] = []
        if recursive {
            guard let walker = fm.enumerator(at: folder, includingPropertiesForKeys: Self.keys, options: [],
                                             errorHandler: { _, _ in true }) else { return [] }
            for case let url as URL in walker where named(url.lastPathComponent) { urls.append(url) }
        } else {
            urls = ((try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: Self.keys)) ?? [])
                .filter { named($0.lastPathComponent) }
        }
        return urls.compactMap { url in
            guard let v = try? url.resourceValues(forKeys: Set(Self.keys)), let i = Self.info(v) else { return nil }
            return TurnFile(url: url, info: i)
        }
    }

    func folders(in folder: URL) -> [String] {
        let urls = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isDirectoryKey])) ?? []
        return urls.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true }
            .map(\.lastPathComponent)
    }

    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        try handle.seek(toOffset: offset)
        return try handle.read(upToCount: length) ?? Data()
    }

    func remove(_ url: URL) { try? FileManager.default.removeItem(at: url) }

    func antigravitySummaries(home: URL) -> [String: String]? { summaries(home) }

    func stream(_ folder: URL, _ onChange: @escaping (FolderChange) -> Void) -> FolderStream? {
        makeStream(folder, onChange)
    }
}

/// A thin FSEvents wrapper (as `DrawerWatcher`): file-level events on the main queue, coalesced by
/// FSEvents' latency. Dropped or must-rescan events become `.rescan`. No timer, no polling: a quiet
/// folder costs nothing.
final class FSEventsFolderStream: FolderStream {
    private var stream: FSEventStreamRef?
    private let handler: (FolderChange) -> Void

    private init(handler: @escaping (FolderChange) -> Void) { self.handler = handler }

    deinit { stop() }

    static func start(_ folder: URL, _ handler: @escaping (FolderChange) -> Void,
                      queue: DispatchQueue = .main, latency: CFTimeInterval = 0.5) -> FSEventsFolderStream? {
        let s = FSEventsFolderStream(handler: handler)
        return s.begin(folder.path, queue: queue, latency: latency) ? s : nil
    }

    private static let rescanFlags = FSEventStreamEventFlags(kFSEventStreamEventFlagMustScanSubDirs
        | kFSEventStreamEventFlagUserDropped | kFSEventStreamEventFlagKernelDropped)

    private func begin(_ path: String, queue: DispatchQueue, latency: CFTimeInterval) -> Bool {
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(),
                                           retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, info, count, eventPaths, flags, _ in
            guard let info else { return }
            let me = Unmanaged<FSEventsFolderStream>.fromOpaque(info).takeUnretainedValue()
            if (0..<count).contains(where: { flags[$0] & FSEventsFolderStream.rescanFlags != 0 }) {
                me.handler(.rescan)
                return
            }
            let list = unsafeBitCast(eventPaths, to: NSArray.self) as? [String] ?? []
            me.handler(.paths(Array(list.prefix(count))))
        }
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagFileEvents
                                             | kFSEventStreamCreateFlagNoDefer)
        guard let s = FSEventStreamCreate(nil, callback, &context, [path] as CFArray,
                                          FSEventStreamEventId(kFSEventStreamEventIdSinceNow), latency, flags) else { return false }
        FSEventStreamSetDispatchQueue(s, queue)
        guard FSEventStreamStart(s) else {
            FSEventStreamInvalidate(s)
            FSEventStreamRelease(s)
            return false
        }
        stream = s
        return true
    }

    func stop() {
        guard let s = stream else { return }
        FSEventStreamStop(s)
        FSEventStreamInvalidate(s)
        FSEventStreamRelease(s)
        stream = nil
    }
}

// MARK: The watcher

/// Follows every agent's turn files (agent notifications design, sections 1 and 2; Windows
/// `AgentActivityWatcher`) and raises one `AgentTurnEvent` per change, in file order:
/// - Codex's plain rollouts and the Antigravity CLI's transcripts, through one FSEvents stream per
///   followed home, read from in-memory offsets with a torn last line kept for the next read. A
///   stream that won't start, or fails, puts its folder on the 30-second poll.
/// - Claude Code's signal files, read and deleted on each Darwin post (`claudeCodePosted`).
///
/// Nothing replays: when an agent is switched on, a file changed in the last 10 minutes is read from
/// up to its last 4 MB to learn whether a turn is open, raising nothing; older files start at their
/// end. Claude Code's waiting signal files get the same silent first pass.
///
/// Idle cost: no timer runs unless a folder is polling or an Antigravity end is pending; then one
/// ticker runs (30 s, or 5 s while an end is armed). Main-thread confined: the app calls it on the
/// main thread and the real streams deliver there.
final class AgentActivityWatcher {
    static let pollEvery: TimeInterval = 30
    /// The ticker's interval while an Antigravity end is pending: it lands within 5 s of its 90.
    static let dueEvery: TimeInterval = 5
    static let replayWindow: TimeInterval = 600
    static let replayTailBytes: UInt64 = 4 * 1024 * 1024
    /// A longer line is skipped whole: turn markers are small, and tool output can be huge.
    static let maxLineBytes = 1024 * 1024
    /// An open turn's file reports activity at most this often.
    static let keepAliveEvery: TimeInterval = 60
    /// Antigravity's summaries are read again for an unknown conversation at most this often.
    static let lookupEvery: TimeInterval = 60
    private static let chunkBytes = 256 * 1024
    private static let keepCapacity = 64 * 1024

    struct FollowedFolder: Equatable {
        var agent: AgentSource
        var folder: URL
        var polling: Bool
    }

    private final class Tail {
        let agent: AgentSource
        let url: URL
        let session: String
        let root: String
        var offset: UInt64 = 0
        var partial = Data()
        var oversized = false
        /// A Codex rollout first seen past its start: read its first line (the sub-agent mark and
        /// folder) before the next new bytes.
        var needsMeta = false
        var lines = 0
        var activeAt: Date?
        var project: String?
        var codex: CodexTurnReader?
        var antigravity: AntigravityTurnReader?

        init(agent: AgentSource, url: URL, session: String, root: String) {
            self.agent = agent
            self.url = url
            self.session = session
            self.root = root
            reset(offset: 0)
        }

        var turnOpen: Bool {
            if let c = codex { return !c.isChild && c.turnOpen }
            return antigravity?.turnOpen ?? false
        }

        func clearPartial() {
            partial.removeAll(keepingCapacity: partial.count <= AgentActivityWatcher.keepCapacity)
        }

        func reset(offset: UInt64) {
            self.offset = offset
            clearPartial()
            oversized = false
            codex = agent == .codex ? CodexTurnReader() : nil
            antigravity = agent == .antigravity ? AntigravityTurnReader() : nil
            needsMeta = agent == .codex && offset > 0
        }
    }

    private struct Root {
        let agent: AgentSource
        let home: URL
        /// The home as listed, and as FSEvents may report it (symlinks resolved).
        let listed: String
        let resolved: String
        var stream: FolderStream?
        var polling: Bool
    }

    private let homes: AgentHomes
    private let files: TurnFileSystem
    private let clock: () -> Date
    private let ticker: AudioBarsTicker
    private let events: (AgentTurnEvent) -> Void

    private var roots: [String: Root] = [:]
    private var rootOrder: [String] = []
    private var tails: [String: Tail] = [:]
    private var firstPass: [AgentSource: Date] = [:]
    private var claudeOn = false
    private var signalsPrimed = false
    private var claude = ClaudeCodeTurns()
    private var pending: [AgentTurnEvent] = []
    private var tickerInterval: TimeInterval?
    private var lastPoll: Date?
    private var agyProjects: [String: String] = [:]
    private var agyLooked: Set<String> = []
    private var agyLookupAt: Date?

    init(homes: AgentHomes, files: TurnFileSystem, clock: @escaping () -> Date, ticker: AudioBarsTicker,
         events: @escaping (AgentTurnEvent) -> Void) {
        self.homes = homes
        self.files = files
        self.clock = clock
        self.ticker = ticker
        self.events = events
    }

    deinit { stop() }

    /// The folders followed, and whether each is on FSEvents or the poll (for the smoke state).
    var followedFolders: [FollowedFolder] {
        rootOrder.compactMap { roots[$0] }.map { FollowedFolder(agent: $0.agent, folder: $0.home, polling: $0.polling) }
    }

    /// Follows what is on and lets go of what is off. Calling it again with the same switches does
    /// nothing; an agent switched back on starts over, with a silent first pass.
    func update(codexOn: Bool, antigravityOn: Bool, claudeOn on: Bool) {
        let now = clock()
        set(.codex, codexOn, now)
        set(.antigravity, antigravityOn, now)
        if on && !claudeOn {
            claudeOn = true
            claude = ClaudeCodeTurns()
            signalsPrimed = false
            _ = readSignals(now)
        } else if !on {
            claudeOn = false
            signalsPrimed = false
        }
        flush()
        rearm(now)
    }

    /// A Darwin post from Claude Code's hook: reads and deletes every waiting signal file, and says
    /// whether there were any. Stale, oversized and malformed files are deleted unread, and so is
    /// every file while Claude Code is off; a temporary file older than 10 minutes is removed.
    @discardableResult
    func claudeCodePosted() -> ClaudeCodePostResult {
        let now = clock()
        let had = readSignals(now)
        flush()
        return had ? .hadFiles : .bare
    }

    /// Stops every stream and the ticker and forgets all state. `update` starts again from scratch.
    func stop() {
        for key in rootOrder { roots[key]?.stream?.stop() }
        roots = [:]
        rootOrder = []
        tails = [:]
        firstPass = [:]
        claudeOn = false
        signalsPrimed = false
        pending = []
        ticker.stop()
        tickerInterval = nil
    }

    // MARK: Following

    private func set(_ agent: AgentSource, _ on: Bool, _ now: Date) {
        let following = firstPass[agent] != nil
        if on && !following { follow(agent, now) }
        if !on && following { unfollow(agent) }
    }

    private func follow(_ agent: AgentSource, _ now: Date) {
        firstPass[agent] = now
        let list = agent == .codex ? homes.codex : [homes.antigravity]
        for home in list where files.isFolder(home) {
            let key = home.standardizedFileURL.path
            guard roots[key] == nil else { continue }
            let resolved = Self.realPath(key)
            let stream = files.stream(home) { [weak self] change in self?.changed(key, change) }
            roots[key] = Root(agent: agent, home: home, listed: key, resolved: resolved, stream: stream, polling: stream == nil)
            rootOrder.append(key)
            if stream == nil { lastPoll = now }
            scan(key, now, prime: true)
        }
    }

    private func unfollow(_ agent: AgentSource) {
        for key in rootOrder where roots[key]?.agent == agent {
            roots[key]?.stream?.stop()
            roots[key] = nil
        }
        rootOrder.removeAll { roots[$0] == nil }
        tails = tails.filter { $0.value.agent != agent }
        firstPass[agent] = nil
    }

    private func changed(_ key: String, _ change: FolderChange) {
        guard var root = roots[key] else { return }
        let now = clock()
        switch change {
        case .paths(let paths):
            for p in paths {
                if let (path, session) = classify(p, root: root) { process(root.agent, path, session, key, now, prime: false) }
            }
        case .rescan:
            scan(key, now, prime: false)
        case .failed:
            root.stream?.stop()
            root.stream = nil
            root.polling = true
            roots[key] = root
            lastPoll = now
            scan(key, now, prime: false)
        }
        flush()
        rearm(now)
    }

    private func tick() {
        let now = clock()
        if roots.values.contains(where: \.polling),
           lastPoll.map({ now.timeIntervalSince($0) >= Self.pollEvery * 0.9 }) ?? true {
            lastPoll = now
            for key in rootOrder where roots[key]?.polling == true { scan(key, now, prime: false) }
        }
        for tail in tails.values.sorted(by: { $0.url.path < $1.url.path }) {
            guard var r = tail.antigravity, let due = r.dueAt, due <= now else { continue }
            let out = r.tick(now: now)
            tail.antigravity = r
            pending += out
        }
        flush()
        rearm(now)
    }

    /// One ticker, only while something needs it.
    private func rearm(_ now: Date) {
        let armed = tails.values.contains { $0.antigravity?.dueAt != nil }
        let polling = roots.values.contains(where: \.polling)
        let want: TimeInterval? = armed ? Self.dueEvery : polling ? Self.pollEvery : nil
        if want == tickerInterval, want == nil || ticker.running { return }
        tickerInterval = want
        if let want {
            ticker.start(interval: want) { [weak self] in self?.tick() }
        } else {
            ticker.stop()
        }
    }

    private func flush() {
        let out = pending
        pending = []
        out.forEach(events)
    }

    // MARK: Files

    /// The canonical path and session of a followed file under `root`, nil for anything else
    /// (a compressed rollout, Codex's history, `transcript_full.jsonl`, a folder).
    private func classify(_ raw: String, root: Root) -> (String, String)? {
        var path = raw
        if root.resolved != root.listed, path.hasPrefix(root.resolved + "/") {
            path = root.listed + path.dropFirst(root.resolved.count)
        }
        switch root.agent {
        case .codex:
            let base = root.listed + "/sessions/"
            guard path.hasPrefix(base) else { return nil }
            let name = (path as NSString).lastPathComponent
            guard Self.isPlainRollout(name) else { return nil }
            return (path, CodexBurnReader.sessionID(of: name))
        case .antigravity:
            let base = root.listed + "/brain/"
            guard path.hasPrefix(base) else { return nil }
            let parts = path.dropFirst(base.count).split(separator: "/", omittingEmptySubsequences: false)
            guard parts.count == 4, !parts[0].isEmpty, parts[1] == ".system_generated", parts[2] == "logs",
                  parts[3] == Substring(AntigravityBurnReader.transcriptName) else { return nil }
            return (path, String(parts[0]))
        case .claude:
            return nil
        }
    }

    /// The path with every symlink resolved, as FSEvents reports it (`/var` is `/private/var`).
    /// Foundation's `resolvingSymlinksInPath` strips `/private` again, so this asks `realpath`.
    static func realPath(_ path: String) -> String {
        guard let p = realpath(path, nil) else { return path }
        defer { free(p) }
        return String(cString: p)
    }

    private static func isPlainRollout(_ name: String) -> Bool {
        let lower = name.lowercased()
        return lower.hasPrefix("rollout-") && lower.hasSuffix(".jsonl")
    }

    /// Every followed file under the root; one that is gone takes its open turn with it.
    private func scan(_ key: String, _ now: Date, prime: Bool) {
        guard let root = roots[key] else { return }
        var seen = Set<String>()
        for (path, session) in list(root) {
            seen.insert(path)
            process(root.agent, path, session, key, now, prime: prime)
        }
        tails = tails.filter { $0.value.root != key || seen.contains($0.key) }
    }

    private func list(_ root: Root) -> [(String, String)] {
        switch root.agent {
        case .codex:
            let sessions = root.home.appendingPathComponent("sessions", isDirectory: true)
            return files.files(under: sessions, recursive: true, named: Self.isPlainRollout)
                .compactMap { classify($0.url.standardizedFileURL.path, root: root) }
                .sorted { $0.0 < $1.0 }
        case .antigravity:
            let brain = root.home.appendingPathComponent("brain", isDirectory: true)
            return files.folders(in: brain).sorted().map {
                (root.listed + "/brain/\($0)/.system_generated/logs/" + AntigravityBurnReader.transcriptName, $0)
            }
        case .claude:
            return []
        }
    }

    private func process(_ agent: AgentSource, _ path: String, _ session: String, _ root: String, _ now: Date, prime: Bool) {
        let url = URL(fileURLWithPath: path)
        guard let info = files.info(url) else {
            tails[path] = nil   // gone (compressed, deleted): no ending is invented
            return
        }
        if let tail = tails[path] {
            if info.size < tail.offset { tail.reset(offset: info.size) }   // rewritten shorter: go on from its end
            else if info.size > tail.offset { readNew(tail, info, now) }
            return
        }
        let tail = Tail(agent: agent, url: url, session: session, root: root)
        tails[path] = tail
        // A file that was there before this agent's first pass is history: read for state, raise nothing.
        if prime || info.created < (firstPass[agent] ?? now) { primeTail(tail, info, now) }
        else { readNew(tail, info, now) }
    }

    private func primeTail(_ tail: Tail, _ info: TurnFileInfo, _ now: Date) {
        let recent = now.timeIntervalSince(info.modified) <= Self.replayWindow
        guard recent else {
            tail.offset = info.size
            tail.needsMeta = tail.codex != nil && info.size > 0
            return
        }
        if tail.codex != nil { readMeta(tail, size: info.size, now) }
        let start = info.size > Self.replayTailBytes ? info.size - Self.replayTailBytes : 0
        let (consumed, _) = feed(tail, from: start, count: info.size - start, skipFirstPartial: start > 0, now)
        tail.offset = start + consumed
        tail.antigravity?.settle()
    }

    private func readMeta(_ tail: Tail, size: UInt64, _ now: Date) {
        tail.clearPartial()
        tail.oversized = false
        _ = feed(tail, from: 0, count: min(size, UInt64(Self.maxLineBytes) + 1), skipFirstPartial: false, now, firstLineOnly: true)
        tail.clearPartial()
        tail.oversized = false
        tail.needsMeta = false
    }

    private func readNew(_ tail: Tail, _ info: TurnFileInfo, _ now: Date) {
        if tail.needsMeta { readMeta(tail, size: info.size, now) }
        let before = tail.lines
        let start = tail.offset
        let (consumed, out) = feed(tail, from: start, count: info.size - start, skipFirstPartial: false, now)
        tail.offset = start + consumed
        pending += out
        // New lines while a turn is open: the agent is still at it, so its card must not lose touch.
        // An event in the same read counts as that minute's report.
        guard tail.lines > before, tail.turnOpen else { return }
        if !out.isEmpty {
            tail.activeAt = now
        } else if tail.activeAt.map({ now.timeIntervalSince($0) >= Self.keepAliveEvery }) ?? true {
            tail.activeAt = now
            pending.append(AgentTurnEvent(agent: tail.agent, kind: .started, project: tail.project,
                                          session: tail.session, at: now, duration: nil))
        }
    }

    /// Feeds every complete line in the next `count` bytes to the tail's reader and returns the bytes
    /// read and the events. A line with no newline yet stays in the tail. A read that fails stops
    /// there; the offset moves only by what was read.
    private func feed(_ tail: Tail, from start: UInt64, count: UInt64, skipFirstPartial: Bool, _ now: Date,
                      firstLineOnly: Bool = false) -> (UInt64, [AgentTurnEvent]) {
        var out: [AgentTurnEvent] = []
        var consumed: UInt64 = 0
        var skipping = skipFirstPartial
        let folder = tail.antigravity == nil ? nil : antigravityFolder(tail.session, now)
        while consumed < count {
            let n = Int(min(UInt64(Self.chunkBytes), count - consumed))
            guard let data = try? files.read(tail.url, offset: start + consumed, length: n), !data.isEmpty else { break }
            consumed += UInt64(data.count)
            var lineStart = data.startIndex
            while let nl = data[lineStart...].firstIndex(of: 10) {
                if skipping {
                    skipping = false
                } else {
                    collect(tail, data[lineStart..<nl])
                    out += endLine(tail, now, folder)
                    if firstLineOnly { return (consumed, out) }
                }
                lineStart = data.index(after: nl)
            }
            if !skipping { collect(tail, data[lineStart..<data.endIndex]) }
        }
        return (consumed, out)
    }

    private func collect(_ tail: Tail, _ bytes: Data) {
        guard !bytes.isEmpty, !tail.oversized else { return }
        if tail.partial.count + bytes.count > Self.maxLineBytes {
            tail.oversized = true
            tail.partial = Data()
            return
        }
        tail.partial.append(bytes)
    }

    private func endLine(_ tail: Tail, _ now: Date, _ folder: String?) -> [AgentTurnEvent] {
        tail.lines += 1
        if tail.oversized {
            tail.oversized = false
            tail.antigravity?.activity()   // still a line: the turn is going on
            return []
        }
        var line = tail.partial
        tail.clearPartial()
        while let last = line.last, last == 13 || last == 32 || last == 9 { line.removeLast() }
        guard !line.isEmpty else { return [] }
        var out: [AgentTurnEvent] = []
        if var c = tail.codex {
            out = c.read(lines: [line], file: tail.url.lastPathComponent)
            tail.codex = c
        } else if var a = tail.antigravity {
            out = a.read(lines: [line], conversation: tail.session, project: folder, now: now)
            tail.antigravity = a
        }
        if let p = out.last?.project { tail.project = p }
        return out
    }

    /// The conversation's folder from Antigravity's summaries. The database is read for a
    /// conversation not seen before, and otherwise at most once a minute.
    private func antigravityFolder(_ conversation: String, _ now: Date) -> String? {
        if let hit = agyProjects[conversation] { return hit }
        let since = agyLookupAt.map { now.timeIntervalSince($0) } ?? .infinity
        let due = since >= Self.lookupEvery || (!agyLooked.contains(conversation) && since >= 1)
        guard due else { return nil }
        agyLookupAt = now
        agyLooked.insert(conversation)
        for (id, uris) in files.antigravitySummaries(home: homes.antigravity) ?? [:] {
            if let path = AntigravityBurnReader.projectPath(ofWorkspaceURIs: uris) { agyProjects[id] = path }
        }
        return agyProjects[conversation]
    }

    // MARK: Claude Code's signal files

    /// Reads, applies and deletes every waiting signal, oldest first. The first pass after Claude
    /// Code is switched on records turn starts and raises nothing. True when any signal file was there.
    private func readSignals(_ now: Date) -> Bool {
        let all = files.files(under: homes.signals, recursive: false) {
            ClaudeCodeSignalFile.isSignalName($0) || ClaudeCodeSignalFile.isTempName($0)
        }
        let signals = all.filter { ClaudeCodeSignalFile.isSignalName($0.url.lastPathComponent) }
            .sorted { $0.url.lastPathComponent < $1.url.lastPathComponent }
        let replay = !signalsPrimed
        if claudeOn { signalsPrimed = true }
        for f in signals {
            var data: Data?
            if claudeOn, f.info.size <= UInt64(ClaudeCodeSignalFile.maxBytes) {
                data = try? files.read(f.url, offset: 0, length: ClaudeCodeSignalFile.maxBytes + 1)
            }
            files.remove(f.url)
            if let data, let s = ClaudeCodeSignalFile.parse(data, now: now), let e = claude.apply(s, replay: replay) {
                pending.append(e)
            }
        }
        for t in all where ClaudeCodeSignalFile.isTempName(t.url.lastPathComponent)
            && now.timeIntervalSince(t.info.modified) > ClaudeCodeSignalFile.maxAge {
            files.remove(t.url)
        }
        return !signals.isEmpty
    }
}
