import Foundation
import Testing
@testable import Sanduhr

/// Mac agent notifications, item 3: the activity watcher, ported from Windows'
/// `AgentActivityWatcherTests`. Temp folders stand in for the Codex and Antigravity homes and
/// Sanduhr's folder; a fake stream stands in for FSEvents and a fake ticker for the timer, so no real
/// FSEvents stream, timer, `~/.codex`, `~/.gemini` or `~/Library` is touched.

/// Streams handed out per folder. A folder in `failing` gets no stream, as when FSEvents refuses one.
final class FakeFolderStreams {
    final class Stream: FolderStream {
        let folder: URL
        let handler: (FolderChange) -> Void
        var stopped = false
        init(folder: URL, handler: @escaping (FolderChange) -> Void) { self.folder = folder; self.handler = handler }
        func stop() { stopped = true }
    }

    var failing: Set<String> = []
    private(set) var made: [Stream] = []

    func make(_ folder: URL, _ handler: @escaping (FolderChange) -> Void) -> FolderStream? {
        guard !failing.contains(folder.path) else { return nil }
        let s = Stream(folder: folder, handler: handler)
        made.append(s)
        return s
    }

    var live: [Stream] { made.filter { !$0.stopped } }

    /// What FSEvents would say for `paths`: only live streams hear it.
    func send(_ change: FolderChange, to folder: URL) {
        for s in live where s.folder.path == folder.path { s.handler(change) }
    }
}

/// The real disk, spied: every read's file and range.
final class SpyTurnDisk: TurnFileSystem {
    var disk: TurnDisk
    var reads: [(path: String, offset: UInt64, length: Int)] = []
    init(_ disk: TurnDisk) { self.disk = disk }
    func isFolder(_ url: URL) -> Bool { disk.isFolder(url) }
    func info(_ url: URL) -> TurnFileInfo? { disk.info(url) }
    func files(under folder: URL, recursive: Bool, named: (String) -> Bool) -> [TurnFile] {
        disk.files(under: folder, recursive: recursive, named: named)
    }
    func folders(in folder: URL) -> [String] { disk.folders(in: folder) }
    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data {
        reads.append((url.path, offset, length))
        return try disk.read(url, offset: offset, length: length)
    }
    func remove(_ url: URL) { disk.remove(url) }
    func antigravitySummaries(home: URL) -> [String: String]? { disk.antigravitySummaries(home: home) }
    func stream(_ folder: URL, _ onChange: @escaping (FolderChange) -> Void) -> FolderStream? {
        disk.stream(folder, onChange)
    }
}

final class WatchFixture {
    let root: URL
    var now = Date().addingTimeInterval(-2)   // a little behind the disk, so a new file is newer
    let ticker = FakeTicker()
    let streams = FakeFolderStreams()
    var summaries: [String: String]? = nil
    var events: [AgentTurnEvent] = []
    private(set) lazy var disk: SpyTurnDisk = {
        var d = TurnDisk()
        d.makeStream = { [weak self] folder, handler in self?.streams.make(folder, handler) }
        d.summaries = { [weak self] _ in self?.summaries }
        return SpyTurnDisk(d)
    }()
    private(set) lazy var watcher: AgentActivityWatcher = AgentActivityWatcher(
        homes: AgentHomes(codex: [codexHome], antigravity: agyHome, signals: signals),
        files: disk, clock: { [weak self] in self?.now ?? Date() }, ticker: ticker,
        events: { [weak self] in self?.events.append($0) })

    var codexHome: URL { root.appendingPathComponent("codex", isDirectory: true) }
    var sessions: URL { codexHome.appendingPathComponent("sessions/2026/11/17", isDirectory: true) }
    var agyHome: URL { root.appendingPathComponent("agy", isDirectory: true) }
    var signals: URL { root.appendingPathComponent("support", isDirectory: true) }

    init() {
        root = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-agent-watch-\(UUID().uuidString)", isDirectory: true)
        for dir in [sessions, agyHome.appendingPathComponent("brain", isDirectory: true), signals] {
            try! FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        }
    }

    deinit { try? FileManager.default.removeItem(at: root) }

    func kinds() -> [AgentTurnEvent.Kind] { events.map(\.kind) }

    // MARK: Lines

    static func iso(_ d: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.string(from: d)
    }

    static func json(_ o: [String: Any]) -> String {
        String(data: try! JSONSerialization.data(withJSONObject: o, options: [.sortedKeys]), encoding: .utf8)! + "\n"
    }

    func codexLine(_ at: Date, _ type: String, _ payload: [String: Any]) -> String {
        Self.json(["timestamp": Self.iso(at), "type": type, "payload": payload])
    }

    func meta(parent: String? = nil, cwd: String = "/work/alpha") -> String {
        var p: [String: Any] = ["id": "s", "cwd": cwd]
        if let parent { p["parent_thread_id"] = parent }
        return codexLine(now, "session_meta", p)
    }

    func started(_ at: Date) -> String { codexLine(at, "event_msg", ["type": "task_started", "turn_id": "t"]) }

    func complete(_ at: Date, ms: Int? = 124_000) -> String {
        var p: [String: Any] = ["type": "task_complete", "turn_id": "t"]
        if let ms { p["duration_ms"] = ms }
        return codexLine(at, "event_msg", p)
    }

    func tokenCount(_ at: Date) -> String { codexLine(at, "event_msg", ["type": "token_count"]) }

    func step(_ at: Date, _ type: String, toolCalls: Bool = false) -> String {
        var o: [String: Any] = ["type": type, "status": "DONE", "created_at": Self.iso(at)]
        if toolCalls { o["tool_calls"] = [1] }
        return Self.json(o)
    }

    // MARK: Files

    @discardableResult
    func append(_ url: URL, _ text: String) -> URL { append(url, Data(text.utf8)) }

    @discardableResult
    func append(_ url: URL, _ data: Data) -> URL {
        let fm = FileManager.default
        try? fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if !fm.fileExists(atPath: url.path) { fm.createFile(atPath: url.path, contents: nil) }
        let h = try! FileHandle(forWritingTo: url)
        try! h.seekToEnd()
        try! h.write(contentsOf: data)
        try! h.close()
        return url
    }

    func rolloutURL(_ name: String) -> URL { sessions.appendingPathComponent(name + ".jsonl") }

    @discardableResult
    func rollout(_ name: String, _ text: String) -> URL { append(rolloutURL(name), text) }

    func transcriptURL(_ conversation: String) -> URL {
        agyHome.appendingPathComponent("brain/\(conversation)/.system_generated/logs/transcript.jsonl")
    }

    @discardableResult
    func transcript(_ conversation: String, _ text: String) -> URL { append(transcriptURL(conversation), text) }

    func age(_ url: URL, by seconds: TimeInterval) {
        let d = now.addingTimeInterval(-seconds)
        try! FileManager.default.setAttributes([.modificationDate: d, .creationDate: d], ofItemAtPath: url.path)
    }

    /// Tells the fake stream on `home` that `urls` changed, as FSEvents would.
    func changed(_ urls: URL..., home: URL? = nil) {
        streams.send(.paths(urls.map(\.path)), to: home ?? codexHome)
    }

    func signal(_ event: String, session: String, at: Date? = nil, cwd: String = "/work/alpha",
                name: String? = nil, raw: String? = nil) {
        let ms = Int64(((at ?? now).timeIntervalSince1970 * 1000).rounded())
        let body = raw ?? Self.json(["v": 1, "event": event, "cwd": cwd, "session": session, "at": ms])
        let file = name ?? "cc-signal-\(ms)-\(UUID().uuidString.prefix(6)).json"
        FileManager.default.createFile(atPath: signals.appendingPathComponent(file).path, contents: Data(body.utf8))
    }

    var signalFolder: [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: signals.path)) ?? []).sorted()
    }
}

@Suite("Agent activity watcher")
struct AgentActivityWatcherTests {
    let fx = WatchFixture()

    // MARK: Codex

    @Test func historyIsNotReplayedAndGrowthGivesEvents() {
        let url = fx.rollout("rollout-a", fx.meta() + fx.started(fx.now.addingTimeInterval(-540))
                             + fx.complete(fx.now.addingTimeInterval(-420)))
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.events.isEmpty)

        fx.rollout("rollout-a", fx.started(fx.now) + fx.complete(fx.now.addingTimeInterval(124)))
        fx.changed(url)
        #expect(fx.kinds() == [.started, .finished])
        #expect(fx.events.allSatisfy { $0.agent == .codex && $0.session == "rollout-a" && $0.project == "alpha" })
        #expect(fx.events.last?.duration == 124)
    }

    /// Review Focus 3: launched mid-turn, the turn's start is in the replay window, so the finish
    /// after launch notifies with the right duration, and nothing before launch notifies.
    @Test func aTurnRunningAtLaunchFinishesWithItsReplayedStart() {
        let url = fx.rollout("rollout-a", fx.meta() + fx.started(fx.now.addingTimeInterval(-500))
                             + fx.complete(fx.now.addingTimeInterval(-400))
                             + fx.started(fx.now.addingTimeInterval(-180)))
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.events.isEmpty)

        fx.rollout("rollout-a", fx.complete(fx.now, ms: nil))
        fx.changed(url)
        #expect(fx.kinds() == [.finished])
        #expect(fx.events.first?.duration == 180)
    }

    @Test func aFileChangedLongerAgoStartsAtItsEnd() {
        let url = fx.rollout("rollout-a", fx.meta() + fx.started(fx.now.addingTimeInterval(-1500)))
        fx.age(url, by: 1200)
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.disk.reads.isEmpty)

        fx.rollout("rollout-a", fx.complete(fx.now, ms: nil))
        fx.changed(url)
        #expect(fx.kinds() == [.finished])
        #expect(fx.events.first?.duration == nil)   // its start was never read
        #expect(fx.events.first?.project == "alpha")   // the meta line is read once it grows
    }

    @Test func replayReadsAtMostTheLastFourMegabytes() {
        let filler = String(repeating: "x", count: 1000)
        var big = fx.meta()
        for _ in 0..<5200 { big += WatchFixture.json(["type": "response_item", "pad": filler]) }
        let url = fx.rollout("rollout-big", big + fx.started(fx.now.addingTimeInterval(-60)))
        let size = UInt64(try! FileManager.default.attributesOfItem(atPath: url.path)[.size] as! Int)
        #expect(size > 5 * 1024 * 1024)
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        // The meta line from the start, then nothing before the last 4 MB.
        let skipped = AgentActivityWatcher.maxLineBytes..<Int(size - AgentActivityWatcher.replayTailBytes)
        for r in fx.disk.reads {
            let range = Int(r.offset)..<(Int(r.offset) + r.length)
            #expect(!range.overlaps(skipped))
        }
        #expect(fx.events.isEmpty)

        fx.rollout("rollout-big", fx.complete(fx.now, ms: nil))
        fx.changed(url)
        #expect(fx.events.first?.duration == 60)   // the open turn was learned from the tail
    }

    @Test func aRolloutCreatedAfterLaunchIsReadFromItsStart() {
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        let url = fx.rollout("rollout-new", fx.meta() + fx.started(fx.now) + fx.complete(fx.now.addingTimeInterval(124)))
        fx.changed(url)
        #expect(fx.kinds() == [.started, .finished])
    }

    @Test func aLineWrittenInTwoHalvesIsReadOnceItIsWhole() {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        let line = fx.started(fx.now)
        let cut = line.index(line.startIndex, offsetBy: 20)
        fx.rollout("rollout-a", String(line[..<cut]))
        fx.changed(url)
        #expect(fx.events.isEmpty)
        fx.rollout("rollout-a", String(line[cut...]))
        fx.changed(url)
        #expect(fx.kinds() == [.started])
    }

    @Test func aLineOverAMegabyteIsSkippedAndTheNextIsRead() {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        let huge = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"pad\":\""
            + String(repeating: "y", count: AgentActivityWatcher.maxLineBytes + 10) + "\"}}\n"
        fx.rollout("rollout-a", huge + fx.started(fx.now))
        fx.changed(url)
        #expect(fx.kinds() == [.started])
    }

    @Test func compressedRolloutsAreIgnored() {
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        let zst = fx.append(fx.sessions.appendingPathComponent("rollout-z.jsonl.zst"),
                            fx.meta() + fx.started(fx.now) + fx.complete(fx.now))
        fx.changed(zst)
        fx.streams.send(.rescan, to: fx.codexHome)
        #expect(fx.events.isEmpty)
        #expect(!fx.disk.reads.contains { $0.path.hasSuffix(".zst") })
    }

    @Test func aRolloutThatShrinksOrDisappearsMidTurnRaisesNothing() {
        let a = fx.rollout("rollout-a", fx.meta())
        let b = fx.rollout("rollout-b", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.started(fx.now))
        fx.rollout("rollout-b", fx.started(fx.now))
        fx.changed(a, b)
        #expect(fx.kinds() == [.started, .started])
        fx.events.removeAll()

        try! Data(fx.meta().utf8).write(to: a)            // rewritten shorter
        try! FileManager.default.removeItem(at: b)         // compressed away
        fx.changed(a, b)
        #expect(fx.events.isEmpty)
        fx.rollout("rollout-a", fx.complete(fx.now, ms: nil))
        fx.changed(a)
        #expect(fx.kinds() == [.finished])
        #expect(fx.events.first?.duration == nil)         // the turn was dropped with the rewrite
    }

    @Test func aSubAgentsRolloutIsSilentKeepAliveIncluded() {
        let url = fx.rollout("rollout-child", fx.meta(parent: "p"))
        fx.age(url, by: 1200)   // its meta is read lazily
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-child", fx.started(fx.now) + fx.tokenCount(fx.now) + fx.complete(fx.now))
        fx.changed(url)
        fx.now = fx.now.addingTimeInterval(120)
        fx.rollout("rollout-child", fx.started(fx.now) + fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.events.isEmpty)
    }

    @Test func aRescanReadsEveryFollowedFile() {
        let a = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.started(fx.now))
        fx.streams.send(.rescan, to: fx.codexHome)
        #expect(fx.kinds() == [.started])
        _ = a
    }

    @Test func aPathInItsResolvedFormIsTheSameFile() throws {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.started(fx.now))
        let resolved = AgentActivityWatcher.realPath(url.path)
        try #require(resolved != url.path)   // the temp folder sits behind /var -> /private/var
        fx.streams.send(.paths([resolved]), to: fx.codexHome)
        fx.changed(url)
        #expect(fx.kinds() == [.started])   // read once, not twice from two offsets
    }

    @Test func pathsOutsideTheSessionsFolderAreIgnored() {
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        let other = fx.append(fx.codexHome.appendingPathComponent("rollout-x.jsonl"), fx.meta() + fx.started(fx.now))
        let history = fx.append(fx.codexHome.appendingPathComponent("history.jsonl"), "{}\n")
        fx.changed(other, history)
        #expect(fx.events.isEmpty)
        #expect(fx.disk.reads.isEmpty)
    }

    // MARK: Keep-alive

    @Test func linesForAnOpenTurnKeepItAliveAtMostOnceAMinute() {
        let url = fx.rollout("rollout-a", fx.meta() + fx.started(fx.now.addingTimeInterval(-30)))
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.events == [AgentTurnEvent(agent: .codex, kind: .started, project: "alpha", session: "rollout-a",
                                             at: fx.now, duration: nil)])
        fx.now = fx.now.addingTimeInterval(30)
        fx.rollout("rollout-a", fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.events.count == 1)
        fx.now = fx.now.addingTimeInterval(31)
        fx.rollout("rollout-a", fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.events.count == 2)
    }

    @Test func linesWithNoTurnOpenKeepNothingAlive() {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.events.isEmpty)
    }

    @Test func aTurnEventCountsAsThatMinutesKeepAlive() {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.rollout("rollout-a", fx.started(fx.now) + fx.tokenCount(fx.now))
        fx.changed(url)
        fx.now = fx.now.addingTimeInterval(20)
        fx.rollout("rollout-a", fx.tokenCount(fx.now))
        fx.changed(url)
        #expect(fx.kinds() == [.started])
    }

    // MARK: Streams, the poll and the timer

    @Test func nothingFollowedMeansNoStreamAndNoTimer() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: false)
        #expect(fx.streams.made.isEmpty)
        #expect(!fx.ticker.running)
        #expect(fx.watcher.followedFolders.isEmpty)
    }

    @Test func aQuietFollowedFolderRunsNoTimer() {
        fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: true, claudeOn: true)
        #expect(fx.streams.live.map(\.folder.path).sorted() == [fx.agyHome.path, fx.codexHome.path].sorted())
        #expect(!fx.ticker.running)
        #expect(fx.watcher.followedFolders.allSatisfy { !$0.polling })
    }

    @Test func aStreamThatWillNotStartFallsBackToThePoll() {
        fx.streams.failing = [fx.codexHome.path]
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.ticker.running)
        #expect(fx.ticker.interval == AgentActivityWatcher.pollEvery)
        #expect(fx.watcher.followedFolders.map(\.polling) == [true])

        fx.rollout("rollout-a", fx.started(fx.now))
        fx.now = fx.now.addingTimeInterval(30)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started])
        _ = url
    }

    @Test func aStreamErrorFallsBackToThePoll() {
        fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(!fx.ticker.running)
        fx.rollout("rollout-a", fx.started(fx.now))
        fx.streams.send(.failed, to: fx.codexHome)
        #expect(fx.kinds() == [.started])   // the fall back reads at once
        #expect(fx.streams.live.isEmpty)
        #expect(fx.ticker.running && fx.ticker.interval == AgentActivityWatcher.pollEvery)

        fx.rollout("rollout-a", fx.complete(fx.now))
        fx.now = fx.now.addingTimeInterval(30)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started, .finished])
    }

    @Test func anAgentSwitchedOffStopsFollowingAndComingBackReplaysNothing() {
        let url = fx.rollout("rollout-a", fx.meta())
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: false)
        #expect(fx.streams.live.isEmpty)
        #expect(fx.watcher.followedFolders.isEmpty)

        fx.rollout("rollout-a", fx.started(fx.now))
        fx.changed(url)
        #expect(fx.events.isEmpty)
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.events.isEmpty)   // what was written while off is history
        fx.rollout("rollout-a", fx.complete(fx.now))
        fx.changed(url)
        #expect(fx.kinds() == [.finished])
    }

    @Test func updateTwiceKeepsOneStream() {
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.streams.made.count == 1)
    }

    @Test func aMissingHomeIsNotFollowed() throws {
        try FileManager.default.removeItem(at: fx.agyHome)
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(fx.streams.made.isEmpty)
        #expect(!fx.ticker.running)
    }

    @Test func stopEndsEveryStreamAndTheTimer() {
        fx.streams.failing = [fx.agyHome.path]
        fx.watcher.update(codexOn: true, antigravityOn: true, claudeOn: true)
        #expect(fx.ticker.running)
        fx.watcher.stop()
        #expect(fx.streams.live.isEmpty)
        #expect(!fx.ticker.running)
        #expect(fx.watcher.followedFolders.isEmpty)
    }

    // MARK: Antigravity

    @Test func anAntigravityTurnFinishesNinetySecondsAfterItsLastStep() {
        fx.summaries = ["conv-1": "[\"file:///work/proj-a\"]"]
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        let url = fx.transcript("conv-1", fx.step(fx.now, "USER_INPUT"))
        fx.changed(url, home: fx.agyHome)
        #expect(fx.kinds() == [.started])
        #expect(fx.events.first?.project == "proj-a")
        #expect(!fx.ticker.running)

        fx.transcript("conv-1", fx.step(fx.now.addingTimeInterval(124), "PLANNER_RESPONSE"))
        fx.now = fx.now.addingTimeInterval(125)
        fx.changed(url, home: fx.agyHome)
        #expect(fx.kinds() == [.started, .started])   // the step, two minutes on, is that minute's keep-alive
        #expect(fx.ticker.running)   // a deadline is armed, so the one timer runs
        #expect(fx.ticker.interval == AgentActivityWatcher.dueEvery)

        fx.now = fx.now.addingTimeInterval(60)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started, .started])
        fx.now = fx.now.addingTimeInterval(31)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started, .started, .finished])
        #expect(fx.events.last?.duration == 124)
        #expect(!fx.ticker.running)   // nothing armed: the timer is gone
    }

    /// Review Focus 3 for Antigravity: the `USER_INPUT` before launch is in the replay window.
    @Test func anAntigravityTurnRunningAtLaunchFinishesWithItsDuration() {
        fx.transcript("conv-1", fx.step(fx.now.addingTimeInterval(-180), "USER_INPUT"))
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(fx.events.isEmpty)
        let url = fx.transcript("conv-1", fx.step(fx.now, "PLANNER_RESPONSE"))
        fx.changed(url, home: fx.agyHome)
        fx.now = fx.now.addingTimeInterval(95)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started, .finished])   // the step's keep-alive, then the end
        #expect(fx.events.last?.duration == 180)
    }

    @Test func anAntigravityConversationThatEndedBeforeLaunchRaisesNothingLater() {
        fx.transcript("conv-1", fx.step(fx.now.addingTimeInterval(-60), "USER_INPUT")
                      + fx.step(fx.now.addingTimeInterval(-30), "PLANNER_RESPONSE"))
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(!fx.ticker.running)   // settled at launch
        fx.now = fx.now.addingTimeInterval(200)
        fx.ticker.tick?()
        #expect(fx.events.isEmpty)
    }

    @Test func aNewLineCancelsThePendingEnd() {
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        let url = fx.transcript("conv-1", fx.step(fx.now, "USER_INPUT") + fx.step(fx.now, "PLANNER_RESPONSE"))
        fx.changed(url, home: fx.agyHome)
        #expect(fx.ticker.running)
        fx.now = fx.now.addingTimeInterval(80)
        fx.transcript("conv-1", fx.step(fx.now, "PLANNER_RESPONSE", toolCalls: true))
        fx.changed(url, home: fx.agyHome)
        #expect(!fx.ticker.running)
        fx.now = fx.now.addingTimeInterval(100)
        fx.ticker.tick?()
        #expect(fx.kinds() == [.started, .started])   // the start, then the tool step's keep-alive
    }

    @Test func withAntigravityOffItsFilesAreNotRead() {
        fx.transcript("conv-1", fx.step(fx.now, "USER_INPUT"))
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(!fx.disk.reads.contains { $0.path.contains("/agy/") })
        #expect(!fx.streams.made.contains { $0.folder.path == fx.agyHome.path })
    }

    // MARK: Claude Code's signal files

    @Test func aSignalBecomesAnEventAndItsFileGoes() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.signal("prompt", session: "s1")
        #expect(fx.watcher.claudeCodePosted() == .hadFiles)
        #expect(fx.kinds() == [.started])
        #expect(fx.events.first?.project == "alpha")
        #expect(fx.signalFolder.isEmpty)
    }

    @Test func aPromptThenADoneGivesTheTurnsLength() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.signal("prompt", session: "s1", at: fx.now.addingTimeInterval(-124))
        fx.watcher.claudeCodePosted()
        fx.signal("done", session: "s1")
        fx.watcher.claudeCodePosted()
        #expect(fx.kinds() == [.started, .finished])
        #expect(fx.events.last?.duration == 124)
    }

    @Test func twoSessionsInTheSameMillisecondBothArrive() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.signal("prompt", session: "s1")
        fx.signal("prompt", session: "s2")
        fx.watcher.claudeCodePosted()
        #expect(Set(fx.events.map(\.session)) == ["s1", "s2"])
        #expect(fx.signalFolder.isEmpty)
    }

    @Test func aStaleMalformedOrOversizedSignalIsDeletedUnread() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.signal("prompt", session: "old", at: fx.now.addingTimeInterval(-700))
        fx.signal("prompt", session: "bad", raw: "{not json")
        fx.signal("prompt", session: "big", raw: "{\"pad\":\"" + String(repeating: "z", count: 5000) + "\"}")
        #expect(fx.watcher.claudeCodePosted() == .hadFiles)
        #expect(fx.events.isEmpty)
        #expect(fx.signalFolder.isEmpty)
        #expect(!fx.disk.reads.contains { $0.path.contains("cc-signal-") && $0.length > ClaudeCodeSignalFile.maxBytes + 1 })
    }

    @Test func aLeftoverTempFileOlderThanTheSignalAgeGoesAndAFreshOneStays() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        let old = fx.signals.appendingPathComponent(".cc-signal-1-a.json.tmp")
        let fresh = fx.signals.appendingPathComponent(".cc-signal-2-b.json.tmp")
        for u in [old, fresh] { FileManager.default.createFile(atPath: u.path, contents: Data("{".utf8)) }
        fx.age(old, by: 700)
        #expect(fx.watcher.claudeCodePosted() == .bare)
        #expect(fx.signalFolder == [".cc-signal-2-b.json.tmp"])
        #expect(fx.events.isEmpty)
    }

    @Test func aBarePostIsBare() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        FileManager.default.createFile(atPath: fx.signals.appendingPathComponent("watchers.json").path, contents: Data("{}".utf8))
        #expect(fx.watcher.claudeCodePosted() == .bare)
        #expect(fx.signalFolder == ["watchers.json"])   // nothing else in the folder is touched
    }

    @Test func signalsWaitingAtLaunchRaiseNothingButStillTimeALaterDone() {
        fx.signal("prompt", session: "s1", at: fx.now.addingTimeInterval(-90))
        fx.signal("waiting", session: "s2")
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        #expect(fx.events.isEmpty)
        #expect(fx.signalFolder.isEmpty)
        fx.signal("done", session: "s1")
        fx.watcher.claudeCodePosted()
        #expect(fx.kinds() == [.finished])
        #expect(fx.events.first?.duration == 90)
    }

    @Test func withClaudeCodeOffASignalIsDeletedUnreadAndRaisesNothing() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: false)
        fx.signal("prompt", session: "s1")
        #expect(fx.watcher.claudeCodePosted() == .hadFiles)   // a new hook's post: not the legacy glow
        #expect(fx.events.isEmpty)
        #expect(fx.signalFolder.isEmpty)
        #expect(!fx.disk.reads.contains { $0.path.contains("cc-signal-") })
    }

    @Test func claudeCodeBackOnReplaysWhatWaited() {
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: false)
        fx.signal("prompt", session: "s1")
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        #expect(fx.events.isEmpty)
        #expect(fx.signalFolder.isEmpty)
    }
}
