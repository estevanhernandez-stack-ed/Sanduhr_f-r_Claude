import Darwin
import Foundation

/// Everything By Seat shows for one seat (item 71, slice 1b), read from files and ready to
/// draw: every value masked, no raw settings object kept. Building one runs no CLI, no git, no
/// mod or server, and writes nothing.
struct SeatReport: Equatable, Sendable {
    let seat: String
    var settings = SettingsPart()
    var instructions = InstructionsPart()
    var extensions = ExtensionsPart()
    var history = HistoryPart()
    var housekeeping: [Leftover] = []
    var repo = RepoPart()
    /// MCP servers across every source, in precedence order.
    var mcp: [SeatMCPRow] = []
    var routes: [Route] = []
    /// This seat against each other seat: masked values, names only across the wall (the view
    /// decides with SeatWall).
    var drift: [Drift] = []

    // MARK: Settings

    struct SettingsPart: Equatable, Sendable {
        /// settings.json doesn't parse: why and where, never its text.
        var unreadable: String?
        var rows: [SettingRow] = []
        var lints: [String] = []
        /// Managed settings files found (the file and its drop-in folder).
        var managedFiles = 0
        var warningCount: Int { rows.reduce(0) { $0 + $1.warnings.count } + lints.count }
    }

    // MARK: Instructions

    struct InstructionNode: Identifiable, Equatable, Sendable {
        enum Kind: String, Sendable { case managed, seat, rule, imported }
        let id: Int
        let kind: Kind
        /// The path as shown (`~/…`), or a note for a managed `claudeMd` key.
        let title: String
        let depth: Int
        var lines = 0
        var bytes = 0
        /// A rule's `paths` gate.
        var gate: String?
        var outsideSeat = false
        var missing = false
        /// Larger than the 1 MB read cap: its counts cover the first megabyte.
        var truncated = false
    }

    struct Exclude: Equatable, Sendable {
        let pattern: String
        /// CLAUDE.md files on this Mac it matches (ancestors of the seat's projects and the home folder).
        let matches: Int
    }

    struct MemoryIndex: Equatable, Sendable {
        /// The project's folder name under `projects/`; the view hides it for a work seat.
        let project: String
        let lines: Int
        let bytes: Int
        let missingLinks: Int
        var overLimit: Bool { lines > SeatReports.memoryLineLimit || bytes > SeatReports.memoryByteLimit }
    }

    struct InstructionsPart: Equatable, Sendable {
        var nodes: [InstructionNode] = []
        var excludes: [Exclude] = []
        /// `~/.claude/CLAUDE.md` would load as an ancestor file into this seat's sessions.
        var leak: String?
        var memory: [MemoryIndex] = []
        var unresolved: Int { nodes.filter(\.missing).count }
    }

    // MARK: Extensions

    struct ExtensionRow: Identifiable, Equatable, Sendable {
        let kind: CatalogKind
        let name: String
        var description: String
        let scope: SeatEntry.Scope
        /// Shown only on a seat's own view; the view hides project names for a work seat.
        var project: String?
        var shadow: String?
        var id: String { kind.rawValue + ":" + name + ":" + scope.rawValue + ":" + (project ?? "") }
    }

    struct PluginRow: Identifiable, Equatable, Sendable {
        let name: String
        let key: String
        let enabled: Bool
        let installed: Bool
        /// The key is in the seat's `enabledPlugins`.
        let declared: Bool
        var id: String { key }
    }

    struct HookRow: Identifiable, Equatable, Sendable {
        let id: Int
        let event: String
        let matcher: String
        /// Masked, with the home folder as `~`.
        let command: String
        let timeout: Int?
        /// "Settings" or "Plugin <name>".
        let source: String
        /// The command as a work seat shows it: paths outside the seat replaced.
        let workCommand: String
    }

    struct ExtensionsPart: Equatable, Sendable {
        var rows: [ExtensionRow] = []
        var skillOverrides: [String] = []
        var plugins: [PluginRow] = []
        var parity: [String] = []
        var hooks: [HookRow] = []
    }

    // MARK: History

    struct HistoryPart: Equatable, Sendable {
        /// `cleanupPeriodDays` in effect, nil when unset (Claude Code's default, 30, applies).
        var cleanupDays: Int?
        var desktopDays: Int?
        var transcriptBytes: Int64 = 0
        /// Bytes of transcripts written in the last 30 days.
        var recentBytes: Int64 = 0
        var sessions = 0
        var projects = 0
        var oldest: Date?
        var historyBytes: Int64?
        /// Subagent folders whose session transcript is gone.
        var orphanSidecars = 0
        /// The largest projects by transcript size: folder name and bytes (hidden for a work seat).
        var largest: [Sized] = []
        /// The walk stopped at `SeatReports.transcriptLimit` entries: the sizes are partial.
        var partial = false
        var warns: Bool { cleanupDays == nil }
    }

    struct Sized: Equatable, Sendable {
        let name: String
        let bytes: Int64
    }

    // MARK: Housekeeping

    struct Leftover: Identifiable, Equatable, Sendable {
        /// What it is ("A Sanduhr backup", "A stub .claude.json", …).
        let kind: String
        /// Its name (a file or folder name, never a project's).
        let name: String
        /// Who left it: Sanduhr, Claude Code, a config tool, a seat migration.
        let origin: String
        var bytes: Int64 = 0
        var modified: Date?
        /// What Claude Code does with it if left alone.
        let ifLeft: String
        var id: String { kind + ":" + name }
    }

    // MARK: Seat repo

    enum InstallModel: String, Sendable {
        case gitClone, copyInstalled, plain

        var title: String {
            switch self {
            case .gitClone: "Git clone"
            case .copyInstalled: "Copy-installed from a repo elsewhere"
            case .plain: "Plain folder (unsynced)"
            }
        }
    }

    enum SyncState: Equatable, Sendable {
        /// No `.seat/state.json`.
        case none
        /// The file isn't the shape Sanduhr knows.
        case unknownSchema
        case read(SyncRead)
    }

    struct SyncRead: Equatable, Sendable {
        var lastRun: String?
        var phase: String?
        var paused: Bool?
        /// The last report's lines, masked; the view shows only how many for a work seat.
        var report: [String] = []
        var applySteps = 0
    }

    enum Bucket: String, CaseIterable, Sendable {
        case synced, machineOnly, never

        var title: String {
            switch self {
            case .synced: "Synced"
            case .machineOnly: "Machine-only by design"
            case .never: "Never"
            }
        }
    }

    struct RepoPart: Equatable, Sendable {
        var model = InstallModel.plain
        var branch: String?
        /// The repo's origin against the seat identity file: nil when either is missing. Never shown.
        var remoteMatches: Bool?
        var sync = SyncState.none
        /// `.seat/disabled` is there: the seat's sync is switched off.
        var killSwitch = false
        var buckets: [Bucket: [String]] = [:]
    }

    // MARK: Routing and drift

    struct Route: Equatable, Sendable {
        /// The project folder; the view hides it for a work seat.
        let project: String
        /// The seat `CLAUDE_CONFIG_DIR` names in its `.vscode/settings.json`, standardized.
        let target: String
    }

    struct DriftKey: Equatable, Sendable {
        let key: String
        /// Masked; shown only between two personal seats.
        let mine: String?
        let theirs: String?
    }

    struct Drift: Equatable, Sendable {
        let other: String
        var keys: [DriftKey] = []
        /// `kind:name` held here and not there, and the other way.
        var onlyHere: [String] = []
        var onlyThere: [String] = []
        /// One side's settings.json is unreadable, so keys weren't compared.
        var settingsUnreadable = false
        var count: Int { keys.count + onlyHere.count + onlyThere.count }
    }

    /// One-line counts for state.yaml and the collapsed summaries.
    var counts: SeatCounts {
        SeatCounts(settings: settings.rows.count, warnings: settings.warningCount, unreadable: settings.unreadable != nil,
                   instructions: instructions.nodes.count, extensions: extensions.rows.count + extensions.plugins.count,
                   parity: extensions.parity.count, hooks: extensions.hooks.count, mcp: mcp.count,
                   historyWarning: history.warns, leftovers: housekeeping.count,
                   drift: drift.reduce(0) { $0 + $1.count })
    }
}

/// A seat's counts, never a name: By Seat's summaries and state.yaml's `mods_config.by_seat`.
struct SeatCounts: Equatable, Sendable {
    var settings = 0
    var warnings = 0
    var unreadable = false
    var instructions = 0
    var extensions = 0
    var parity = 0
    var hooks = 0
    var mcp = 0
    var historyWarning = false
    var leftovers = 0
    var drift = 0
    var running = false
}

/// Builds every seat's report in one background pass (drift needs them all).
enum SeatReports {
    static let memoryLineLimit = 200
    static let memoryByteLimit = 25 * 1024
    /// Import hops followed from a CLAUDE.md, as Claude Code does.
    static let importDepth = 4
    /// Transcript files measured per seat, so a huge history stays quick.
    static let transcriptLimit = 200_000

    struct Input: Sendable {
        let seat: String
        let isDefault: Bool
        let scan: SeatScan
    }

    /// The managed files, merged into one layer, and a lint per file that doesn't parse.
    struct Managed {
        var layer: SettingsLayer?
        var count = 0
        var lints: [String] = []
        var layers: [SettingsLayer] { layer.map { [$0] } ?? [] }
    }

    /// `configs` is each seat's `.claude.json` placement as the page already read it; the
    /// file's contents come from the scan (`SeatScan.configKeys`, `caseTwinProjects`), so it is
    /// parsed once per load.
    static func build(_ inputs: [Input], home: String,
                      managedFolder: String = (ModOverrides.managedFile as NSString).deletingLastPathComponent,
                      configs: [String: [ClaudeCodeFolders.ConfigCandidate]] = [:],
                      now: Date = Date(), fileManager fm: FileManager = .default) -> [SeatReport] {
        let managed = managedSettings(managedFolder, fm)
        var raws: [[String: Any]?] = []
        var reports: [SeatReport] = []
        for input in inputs {
            let (report, raw) = build(input, home: home, managed: managed, managedFolder: managedFolder,
                                      config: configs[input.seat], now: now, fm)
            reports.append(report)
            raws.append(raw)
        }
        for i in reports.indices {
            reports[i].drift = reports.indices.filter { $0 != i }.map { j in
                SeatDriftCheck.drift(mine: raws[i], theirs: raws[j], myScan: inputs[i].scan, theirScan: inputs[j].scan,
                                     other: reports[j].seat)
            }
        }
        return reports
    }

    /// One seat's report and its raw settings object (for drift, never kept; nil when the file
    /// is unreadable).
    static func build(_ input: Input, home: String, managed: Managed, managedFolder: String,
                      config: [ClaudeCodeFolders.ConfigCandidate]?, now: Date, _ fm: FileManager) -> (SeatReport, [String: Any]?) {
        let seat = input.seat
        var report = SeatReport(seat: seat)
        let settingsFile = (seat as NSString).appendingPathComponent("settings.json")
        let settings = SeatScanner.readObject(settingsFile, fm, cap: SeatScanner.Cap.settings).object ?? [:]
        let user = SettingsLayer(source: .user, file: settingsFile, object: settings)
        report.settings = settingsPart(input, user: user, managed: managed, home: home, fm)
        report.instructions = SeatInstructions.read(seat: seat, isDefault: input.isDefault, home: home,
                                                     projects: input.scan.projects, managedFolder: managedFolder,
                                                     excludes: list("claudeMdExcludes", managed.layers + [user]),
                                                     managedClaudeMd: managed.layer?.object["claudeMd"] as? String, fm)
        report.extensions = SeatExtensionsCheck.read(input.scan, settings: settings, seat: seat, home: home)
        report.mcp = SeatMCPView.rows(input.scan.mcpServers, approvals: SeatMCPView.MCPApprovals(settings: settings))
        report.history = history(seat, rows: report.settings.rows, now: now, fm)
        report.housekeeping = SeatHousekeeping.read(seat: seat, isDefault: input.isDefault, home: home,
                                                    config: config ?? ClaudeCodeFolders.configCandidates(for: seat, home: home, fileManager: fm),
                                                    scan: input.scan, fm)
        report.repo = SeatRepo.read(seat, fm)
        report.routes = SeatRepo.routes(input.scan.projects, home: home, fm)
        return (report, input.scan.settingsUnreadable == nil ? settings : nil)
    }

    /// `managed-settings.json`, then the drop-ins in `managed-settings.d/` by name, merged into
    /// one managed layer as Claude Code merges them. A file that doesn't parse is a lint: Claude
    /// Code refuses to start with it.
    static func managedSettings(_ folder: String, _ fm: FileManager) -> Managed {
        var files: [(path: String, object: [String: Any])] = []
        var out = Managed()
        func read(_ path: String) {
            switch SeatScanner.readObject(path, fm, cap: SeatScanner.Cap.settings) {
            case .object(let o): files.append((path, o))
            case .unreadable(let why):
                out.lints.append("\((path as NSString).lastPathComponent) (managed) \(why): Claude Code won't start with this file.")
            case .missing: break
            }
        }
        read((folder as NSString).appendingPathComponent("managed-settings.json"))
        let drop = (folder as NSString).appendingPathComponent("managed-settings.d")
        for name in SeatScanner.visible(drop, fm) where name.hasSuffix(".json") {
            read((drop as NSString).appendingPathComponent(name))
        }
        out.count = files.count
        out.layer = SettingsPrecedence.mergeManaged(files)
        return out
    }

    /// A list key merged over `layers` (strings only).
    static func list(_ key: String, _ layers: [SettingsLayer]) -> [String] {
        var out: [String] = []
        for layer in layers.sorted(by: { $0.source < $1.source }) {
            for case let s as String in layer.object[key] as? [Any] ?? [] where !out.contains(s) { out.append(s) }
        }
        return out
    }

    /// The `.seat/local-keys.json` keys: an array of names, or an object's keys.
    static func machineOnlyKeys(_ seat: String, _ fm: FileManager) -> Set<String> {
        let file = (seat as NSString).appendingPathComponent(".seat/local-keys.json")
        guard case .data(let data) = SeatScanner.readFile(file, cap: SeatScanner.Cap.small, fm),
              let v = try? JSONSerialization.jsonObject(with: data) else { return [] }
        if let a = v as? [Any] { return Set(a.compactMap { $0 as? String }) }
        if let o = v as? [String: Any] {
            if let keys = o["keys"] as? [Any] { return Set(keys.compactMap { $0 as? String }) }
            return Set(o.keys)
        }
        return []
    }

    /// The seat-wide view: managed and the seat's own settings walked; each project's files only
    /// counted (never named), with values they set where they're ignored.
    static func settingsPart(_ input: SeatReports.Input, user: SettingsLayer, managed: Managed,
                             home: String, _ fm: FileManager) -> SeatReport.SettingsPart {
        var part = SeatReport.SettingsPart()
        part.unreadable = input.scan.settingsUnreadable
        part.managedFiles = managed.count
        part.rows = SettingsPrecedence.walk(managed.layers + [user], machineOnly: machineOnlyKeys(input.seat, fm),
                                            seat: input.seat, home: home)
        var setIn: [String: Int] = [:]
        var ignoredIn: [String: Int] = [:]
        for project in input.scan.projects {
            let dot = (project as NSString).appendingPathComponent(".claude")
            var seen = Set<String>(), ignored = Set<String>()
            for (file, source) in [("settings.local.json", SettingsSource.projectLocal), ("settings.json", .projectShared)] {
                guard let o = SeatScanner.readObject((dot as NSString).appendingPathComponent(file), fm,
                                                     cap: SeatScanner.Cap.settings).object else { continue }
                for path in SettingsPrecedence.rowPaths(o) {
                    let key = path.joined(separator: ".")
                    seen.insert(key)
                    if let v = SettingsPrecedence.value(at: path, in: o),
                       SettingsPrecedence.ignored(path, value: v, source: source) != nil { ignored.insert(key) }
                }
            }
            for k in seen { setIn[k, default: 0] += 1 }
            for k in ignored { ignoredIn[k, default: 0] += 1 }
        }
        for i in part.rows.indices {
            part.rows[i].setInProjects = setIn[part.rows[i].key] ?? 0
            if let n = ignoredIn[part.rows[i].key] {
                part.rows[i].warnings.append("Set in \(n) project file\(n == 1 ? "" : "s") that \(n == 1 ? "ignores" : "ignore") it.")
            }
        }
        part.lints = managed.lints + SettingsPrecedence.lints(settings: user.object, configKeys: input.scan.configKeys,
                                                              seat: input.seat, isDefault: input.isDefault, fileManager: fm)
        return part
    }

    // MARK: History

    static func history(_ seat: String, rows: [SettingRow], now: Date, _ fm: FileManager) -> SeatReport.HistoryPart {
        var part = SeatReport.HistoryPart()
        func number(_ key: String) -> Int? {
            guard let row = rows.first(where: { $0.path == [key] }), case .text(let t) = row.value else { return nil }
            return Int(t)
        }
        part.cleanupDays = number("cleanupPeriodDays")
        part.desktopDays = number("desktopSessionCleanupPeriodDays")
        let history = (seat as NSString).appendingPathComponent("history.jsonl")
        part.historyBytes = (try? fm.attributesOfItem(atPath: history))?[.size] as? Int64
        let projectsDir = (seat as NSString).appendingPathComponent("projects")
        let cutoff = now.addingTimeInterval(-30 * 86_400)
        var sizes: [SeatReport.Sized] = []
        var measured = 0
        for project in SeatScanner.visible(projectsDir, fm) {
            let dir = (projectsDir as NSString).appendingPathComponent(project)
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: dir, isDirectory: &isDir), isDir.boolValue else { continue }
            part.projects += 1
            var bytes: Int64 = 0
            let top = SeatScanner.visible(dir, fm)
            let sessions = Set(top.filter { $0.hasSuffix(".jsonl") }.map { String($0.dropLast(6)) })
            part.sessions += sessions.count
            part.orphanSidecars += top.filter { isSessionID($0) && !sessions.contains($0) && isFolder((dir as NSString).appendingPathComponent($0), fm) }.count
            guard let e = fm.enumerator(at: URL(fileURLWithPath: dir), includingPropertiesForKeys: [.fileSizeKey, .contentModificationDateKey, .isRegularFileKey]) else { continue }
            while let url = e.nextObject() as? URL {
                // Every entry visited counts toward the cap, not just transcripts.
                measured += 1
                guard measured <= transcriptLimit else {
                    part.partial = true
                    break
                }
                guard url.pathExtension == "jsonl",
                      let v = try? url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey, .isRegularFileKey]),
                      v.isRegularFile == true else { continue }
                let size = Int64(v.fileSize ?? 0)
                bytes += size
                if let m = v.contentModificationDate {
                    if m >= cutoff { part.recentBytes += size }
                    if part.oldest == nil || m < part.oldest! { part.oldest = m }
                }
            }
            part.transcriptBytes += bytes
            sizes.append(SeatReport.Sized(name: project, bytes: bytes))
        }
        part.largest = Array(sizes.sorted { $0.bytes > $1.bytes }.prefix(5))
        return part
    }

    /// A session id: 36 characters of hex digits and dashes.
    static func isSessionID(_ s: String) -> Bool {
        s.count == 36 && s.allSatisfy { $0.isHexDigit || $0 == "-" }
    }

    static func isFolder(_ path: String, _ fm: FileManager) -> Bool {
        var isDir: ObjCBool = false
        return fm.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }
}

/// The Instructions section (item 71, slice 1b): the CLAUDE.md stack a session in the seat
/// loads, with `@` imports resolved up to four hops, rules with their `paths` gates,
/// `claudeMdExcludes`, the ancestor leak and auto memory's index sizes.
enum SeatInstructions {
    /// The most read from one CLAUDE.md, rule or MEMORY.md.
    static let readCap = 1 << 20

    /// The first `readCap` bytes of a regular file.
    static func head(_ path: String, _ fm: FileManager) -> Data {
        guard SeatScanner.isRegular(path, fm), let h = FileHandle(forReadingAtPath: path) else { return Data() }
        defer { try? h.close() }
        return (try? h.read(upToCount: readCap)) ?? Data()
    }

    static func read(seat: String, isDefault: Bool, home: String, projects: [String], managedFolder: String,
                     excludes: [String], managedClaudeMd: String?, _ fm: FileManager) -> SeatReport.InstructionsPart {
        var part = SeatReport.InstructionsPart()
        var nodes: [SeatReport.InstructionNode] = []
        var visited = Set<String>()
        func display(_ p: String) -> String { ClaudeCodeFolders.Folder(path: p).display(home: home) }
        func add(_ path: String, kind: SeatReport.InstructionNode.Kind, depth: Int, gate: String? = nil) {
            let p = AccountData.normalized(path)
            var node = SeatReport.InstructionNode(id: nodes.count, kind: kind, title: display(p), depth: depth, gate: gate)
            node.outsideSeat = kind == .imported && !(p == seat || p.hasPrefix(seat + "/"))
            let data: Data
            switch SeatScanner.readFile(p, cap: SeatInstructions.readCap, fm) {
            case .data(let d): data = d
            case .missing:
                node.missing = true
                nodes.append(node)
                return
            case .refused:
                // Not a regular file: nothing Claude Code would load. Too large: counted from its
                // first megabyte and marked.
                guard SeatScanner.isRegular(p, fm) else {
                    node.missing = true
                    nodes.append(node)
                    return
                }
                node.truncated = true
                data = SeatInstructions.head(p, fm)
            }
            let text = String(decoding: data, as: UTF8.self)
            node.bytes = data.count
            node.lines = text.isEmpty ? 0 : text.components(separatedBy: "\n").count - (text.hasSuffix("\n") ? 1 : 0)
            nodes.append(node)
            guard !visited.contains(p), depth < SeatReports.importDepth else { return }
            visited.insert(p)
            for target in imports(text, from: (p as NSString).deletingLastPathComponent, home: home) {
                add(target, kind: .imported, depth: depth + 1)
            }
        }
        let managedFile = (managedFolder as NSString).appendingPathComponent("CLAUDE.md")
        if fm.fileExists(atPath: managedFile) { add(managedFile, kind: .managed, depth: 0) }
        if let text = managedClaudeMd {
            nodes.append(SeatReport.InstructionNode(id: nodes.count, kind: .managed, title: "claudeMd in managed settings",
                                                    depth: 0, lines: text.components(separatedBy: "\n").count,
                                                    bytes: text.utf8.count))
        }
        let own = (seat as NSString).appendingPathComponent("CLAUDE.md")
        if fm.fileExists(atPath: own) { add(own, kind: .seat, depth: 0) }
        let rules = (seat as NSString).appendingPathComponent("rules")
        if let e = fm.enumerator(atPath: rules) {
            var files: [String] = []
            while let rel = e.nextObject() as? String {
                if rel.hasSuffix(".md"), !rel.split(separator: "/").contains(where: { $0.hasPrefix(".") }) { files.append(rel) }
            }
            for rel in files.sorted() {
                let file = (rules as NSString).appendingPathComponent(rel)
                let gate = SeatScanner.frontmatter(file, fm)["paths"]
                add(file, kind: .rule, depth: 0, gate: gate.flatMap { $0.isEmpty ? nil : $0 })
            }
        }
        part.nodes = nodes

        // Files claudeMdExcludes can match here: the home folder's and each project's.
        var candidates = [(home as NSString).appendingPathComponent("CLAUDE.md"),
                          (home as NSString).appendingPathComponent(".claude/CLAUDE.md")]
        for p in projects {
            for name in ["CLAUDE.md", ".claude/CLAUDE.md", "CLAUDE.local.md"] {
                candidates.append((p as NSString).appendingPathComponent(name))
            }
        }
        let present = candidates.filter { fm.fileExists(atPath: $0) }
        part.excludes = excludes.map { pattern in
            SeatReport.Exclude(pattern: pattern, matches: present.filter { matches(pattern, $0, home: home) }.count)
        }
        let homeClaude = (home as NSString).appendingPathComponent(".claude/CLAUDE.md")
        if !isDefault, fm.fileExists(atPath: homeClaude), !excludes.contains(where: { matches($0, homeClaude, home: home) }) {
            part.leak = "~/.claude/CLAUDE.md loads as an ancestor file into this folder's sessions started under your home folder. Add it to this folder's claudeMdExcludes to keep it out."
        }
        part.memory = memory(seat, fm)
        return part
    }

    /// The `@path` imports in a CLAUDE.md, outside code fences and inline code, resolved from
    /// `dir`: `~/` from the home folder, `/` absolute, anything else relative. A bare `@name`
    /// (no slash and no dot) is a mention, not an import.
    static func imports(_ text: String, from dir: String, home: String) -> [String] {
        var out: [String] = []
        var fenced = false
        for raw in text.components(separatedBy: "\n") {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("```") || line.hasPrefix("~~~") {
                fenced.toggle()
                continue
            }
            guard !fenced else { continue }
            // Inline code spans don't import.
            let plain = line.replacingOccurrences(of: #"`[^`]*`"#, with: "", options: .regularExpression)
            // `@path` runs to the first unescaped space (`\ ` keeps one in the path); a quoted path
            // isn't an import, and a bare `@README` is one (code.claude.com/docs/en/memory).
            guard let re = try? NSRegularExpression(pattern: #"(?:^|\s)@((?:\\ |[^\s"'])+)"#) else { continue }
            let ns = plain as NSString
            for m in re.matches(in: plain, range: NSRange(location: 0, length: ns.length)) {
                var token = ns.substring(with: m.range(at: 1)).replacingOccurrences(of: "\\ ", with: " ")
                while let last = token.last, ",;:)]!?".contains(last) { token.removeLast() }
                // A sentence's full stop ends the path, not the file name.
                while token.hasSuffix("."), token != ".", token != "..", !token.hasSuffix("/.."), !token.hasSuffix("/.") {
                    token.removeLast()
                }
                guard !token.isEmpty else { continue }
                let resolved: String
                if token.hasPrefix("~/") {
                    resolved = (home as NSString).appendingPathComponent(String(token.dropFirst(2)))
                } else if token.hasPrefix("/") {
                    resolved = token
                } else {
                    resolved = (dir as NSString).appendingPathComponent(token)
                }
                let p = AccountData.normalized(resolved)
                if !out.contains(p) { out.append(p) }
            }
        }
        return out
    }

    /// A `claudeMdExcludes` glob against a file, `~/` expanded.
    static func matches(_ pattern: String, _ path: String, home: String) -> Bool {
        let p = pattern.hasPrefix("~/") ? (home as NSString).appendingPathComponent(String(pattern.dropFirst(2))) : pattern
        return fnmatch(p, path, 0) == 0
    }

    /// Each project's auto memory index (`projects/<folder>/memory/MEMORY.md`): its size against
    /// the 200-line and 25 KB load limit, and links to files that aren't there.
    static func memory(_ seat: String, _ fm: FileManager) -> [SeatReport.MemoryIndex] {
        let projects = (seat as NSString).appendingPathComponent("projects")
        var out: [SeatReport.MemoryIndex] = []
        for name in SeatScanner.visible(projects, fm) {
            let dir = (projects as NSString).appendingPathComponent(name + "/memory")
            let file = (dir as NSString).appendingPathComponent("MEMORY.md")
            guard SeatScanner.isRegular(file, fm) else { continue }
            let data = head(file, fm)
            let text = String(decoding: data, as: UTF8.self)
            var missing = 0
            if let re = try? NSRegularExpression(pattern: #"\]\(([^)#\s]+)\)"#) {
                let ns = text as NSString
                for m in re.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
                    let target = ns.substring(with: m.range(at: 1))
                    guard !target.contains("://") else { continue }
                    let path = target.hasPrefix("/") ? target : (dir as NSString).appendingPathComponent(target)
                    if !fm.fileExists(atPath: path) { missing += 1 }
                }
            }
            let lines = text.isEmpty ? 0 : text.components(separatedBy: "\n").count - (text.hasSuffix("\n") ? 1 : 0)
            out.append(SeatReport.MemoryIndex(project: name, lines: lines, bytes: data.count, missingLinks: missing))
        }
        return out
    }
}
