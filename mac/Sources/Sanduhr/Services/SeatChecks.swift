import Foundation

/// By Seat's Extensions section (item 71, slice 1b): agents, skills, commands and output styles
/// with shadowing notes, `skillOverrides`, plugins enabled, installed and declared side by
/// side with the parity checks, and every hook from settings and plugins, read-only.
enum SeatExtensionsCheck {
    static func read(_ scan: SeatScan, settings: [String: Any], home: String) -> SeatReport.ExtensionsPart {
        var part = SeatReport.ExtensionsPart()
        for (kind, entries) in [(CatalogKind.agent, scan.agents), (.skill, scan.skills), (.command, scan.commands),
                                (.outputStyle, scan.outputStyles)] {
            for e in entries {
                var row = SeatReport.ExtensionRow(kind: kind, name: e.name, description: e.description, scope: e.scope,
                                                  project: e.project)
                row.shadow = shadow(kind, e, among: entries)
                part.rows.append(row)
            }
        }
        if let overrides = settings["skillOverrides"] as? [String: Any] {
            part.skillOverrides = overrides.keys.sorted().map { "\($0): \(ConfigMask.display(overrides[$0] as Any, at: ["skillOverrides", $0]))" }
        }
        let declared = Set((settings["enabledPlugins"] as? [String: Any] ?? [:]).keys)
        for item in scan.plugins {
            let key = item.key ?? item.name
            part.plugins.append(SeatReport.PluginRow(name: item.name, key: key,
                                                     enabled: item.state == .on || item.state == .session,
                                                     installed: item.path != nil && item.state != .missing,
                                                     declared: declared.contains(key)))
        }
        part.parity = parity(scan)
        part.hooks = hooks(settings: settings, scan: scan, home: home)
        return part
    }

    /// Which entry wins when two scopes hold the same name: a project's agent beats the seat's
    /// in that project; the seat's skill beats a project's.
    static func shadow(_ kind: CatalogKind, _ e: SeatEntry, among entries: [SeatEntry]) -> String? {
        let others = entries.filter { $0.name == e.name && $0.scope != e.scope }
        guard !others.isEmpty else { return nil }
        switch kind {
        case .agent:
            return e.scope == .project ? nil : "A project's agent of this name wins in that project."
        case .skill:
            return e.scope == .project ? "The seat's skill of this name wins over this one." : nil
        default:
            return "Also defined at another scope."
        }
    }

    /// The plugin parity checks: on but not installed here, on twice from two marketplaces, a
    /// marketplace the seat never added, a directory marketplace whose folder isn't on this Mac,
    /// and an inline mod hiding a same-named marketplace plugin (which `claude plugin list`
    /// still shows as on).
    static func parity(_ scan: SeatScan) -> [String] {
        var out: [String] = []
        let markets = Set(scan.marketplaces.map(\.name))
        for item in scan.plugins {
            let synced: Bool = { if case .installed("synced") = item.origin { return true }; return false }()
            if item.state == .missing {
                out.append("\(item.name): its folder isn't on this Mac.")
            } else if item.state == .on, item.path == nil, !synced {
                out.append("\(item.name): on in settings, not installed on this Mac.")
            }
        }
        var onByName: [String: Set<String>] = [:]
        for item in scan.plugins where item.state == .on {
            if case .installed(let m) = item.origin, m != "synced" { onByName[item.name, default: []].insert(m) }
        }
        for (name, ms) in onByName.sorted(by: { $0.key < $1.key }) where ms.count > 1 {
            out.append("\(name): on from \(ms.count) marketplaces (\(ms.sorted().joined(separator: ", "))).")
        }
        var unknown = Set<String>()
        for item in scan.plugins {
            if case .installed(let m) = item.origin, m != "synced", !markets.contains(m) { unknown.insert(m) }
        }
        for m in unknown.sorted() { out.append("Marketplace \(m) isn't added in this seat (no source declared).") }
        for m in scan.marketplaces {
            if m.source == "no source declared" { out.append("Marketplace \(m.name) has no source.") }
            if m.source == "directory", !m.present { out.append("Marketplace \(m.name) is a folder that isn't on this Mac.") }
        }
        let marketNames = Set(scan.plugins.filter { if case .installed = $0.origin { return true }; return false }.map(\.name))
        for item in scan.plugins where item.origin == .pluginDirs && marketNames.contains(item.name) && item.state == .on {
            out.append("\(item.name): the inline folder hides the marketplace plugin of the same name, which claude plugin list still shows as on.")
        }
        return out
    }

    /// Hooks from the seat's settings and each loaded plugin's `hooks/hooks.json`, as Claude
    /// Code merges them (all run), commands masked and shortened.
    static func hooks(settings: [String: Any], scan: SeatScan, home: String) -> [SeatReport.HookRow] {
        var out: [SeatReport.HookRow] = []
        func add(_ hooks: [String: Any], source: String) {
            for event in hooks.keys.sorted() {
                for case let group as [String: Any] in hooks[event] as? [Any] ?? [] {
                    let matcher = group["matcher"] as? String ?? ""
                    for case let h as [String: Any] in group["hooks"] as? [Any] ?? [] {
                        let raw = h["command"] as? String ?? h["url"] as? String ?? (h["type"] as? String ?? "")
                        let shown = ConfigMask.maskString(raw).replacingOccurrences(of: home, with: "~")
                        out.append(SeatReport.HookRow(id: out.count, event: event, matcher: ConfigMask.maskString(matcher),
                                                      command: shown, timeout: (h["timeout"] as? NSNumber)?.intValue, source: source))
                    }
                }
            }
        }
        add(settings["hooks"] as? [String: Any] ?? [:], source: "Settings")
        for item in scan.plugins where item.state == .on || item.state == .session {
            guard let path = item.path,
                  let o = SeatScanner.readObject((path as NSString).appendingPathComponent("hooks/hooks.json")).object,
                  let hooks = o["hooks"] as? [String: Any] else { continue }
            add(hooks, source: "Plugin \(item.name)")
        }
        return out
    }
}

/// By Seat's MCP section rows (item 71, slice 1b): every source in precedence order, the
/// collisions, approval and the recommended route.
struct SeatMCPRow: Identifiable, Equatable, Sendable {
    let server: SeatMCPServer
    let index: Int
    /// A higher scope holds the same name.
    var shadowedBy: SeatMCPServer.Scope?
    /// Other names with the same endpoint.
    var sameEndpoint: [String] = []
    var approval: String?
    var route: String?
    var id: Int { index }
}

enum SeatMCPView {
    /// The rows for a seat's servers: managed, local, project, user, then plugin.
    static func rows(_ servers: [SeatMCPServer], approvals: MCPApprovals) -> [SeatMCPRow] {
        let order = SeatMCPServer.Scope.allCases
        let sorted = servers.enumerated().sorted { a, b in
            let ia = order.firstIndex(of: a.element.scope) ?? 0, ib = order.firstIndex(of: b.element.scope) ?? 0
            return ia != ib ? ia < ib : a.offset < b.offset
        }.map(\.element)
        let named: Set<SeatMCPServer.Scope> = [.local, .project, .user]
        return sorted.enumerated().map { i, s in
            var row = SeatMCPRow(server: s, index: i)
            if named.contains(s.scope) {
                row.shadowedBy = sorted.first { $0.name == s.name && named.contains($0.scope)
                    && (order.firstIndex(of: $0.scope) ?? 0) < (order.firstIndex(of: s.scope) ?? 0) }?.scope
            }
            row.sameEndpoint = Array(Set(sorted.filter { $0.name != s.name && !$0.target.isEmpty && $0.target == s.target }.map(\.name))).sorted()
            row.approval = s.scope == .project ? approvals.state(s.name) : nil
            row.route = route(s)
            return row
        }
    }

    /// The recommended route (G7): local stdio servers at user scope, project servers with
    /// `${VAR}` placeholders, remote services as claude.ai connectors where one exists.
    static func route(_ s: SeatMCPServer) -> String? {
        let remote = s.transport != "stdio"
        switch s.scope {
        case .local where !remote: return "A local stdio server is usually best at user scope."
        case .project where s.literalValues: return "Use ${VAR} placeholders in .mcp.json instead of written-out values."
        case .user where remote, .local where remote: return "A remote service: a claude.ai connector may cover it."
        default: return nil
        }
    }

    /// The approval settings for project (`.mcp.json`) servers.
    struct MCPApprovals: Equatable, Sendable {
        var all = false
        var enabled: Set<String> = []
        var disabled: Set<String> = []

        init(all: Bool = false, enabled: Set<String> = [], disabled: Set<String> = []) {
            self.all = all
            self.enabled = enabled
            self.disabled = disabled
        }

        init(settings: [String: Any]) {
            all = IntegrationInstaller.boolValue(settings["enableAllProjectMcpServers"]) == true
            enabled = Set((settings["enabledMcpjsonServers"] as? [Any] ?? []).compactMap { $0 as? String })
            disabled = Set((settings["disabledMcpjsonServers"] as? [Any] ?? []).compactMap { $0 as? String })
        }

        func state(_ name: String) -> String {
            if disabled.contains(name) { return "Turned off" }
            if all || enabled.contains(name) { return "Approved" }
            return "Asks on first use"
        }
    }
}

/// By Seat's Housekeeping section (item 71, slice 1b): leftovers with age, size and origin,
/// Sanduhr's own first, and duplicate project keys and case-twin memory folders (counted).
enum SeatHousekeeping {
    static let kept = "Claude Code leaves it: kept until you delete it."

    static func read(seat: String, isDefault: Bool, home: String, config: [String: Any], scan: SeatScan,
                     _ fm: FileManager) -> [SeatReport.Leftover] {
        var out: [SeatReport.Leftover] = []
        func item(_ path: String, kind: String, origin: String, ifLeft: String) {
            let a = try? fm.attributesOfItem(atPath: path)
            out.append(SeatReport.Leftover(kind: kind, name: (path as NSString).lastPathComponent, origin: origin,
                                           bytes: (a?[.size] as? Int64) ?? 0, modified: a?[.modificationDate] as? Date,
                                           ifLeft: ifLeft))
        }
        let names = SeatScanner.visible(seat, fm) + ((try? fm.contentsOfDirectory(atPath: seat)) ?? []).filter { $0.hasPrefix(".") }
        let unique = Array(Set(names)).sorted()
        var sanduhrBackups = unique.filter { $0.hasSuffix(IntegrationInstaller.backupSuffix) }.map { (seat as NSString).appendingPathComponent($0) }
        if isDefault {
            let outer = (home as NSString).appendingPathComponent(ClaudeCodeFolders.configName + IntegrationInstaller.backupSuffix)
            if fm.fileExists(atPath: outer) { sanduhrBackups.insert(outer, at: 0) }
        }
        for p in sanduhrBackups {
            item(p, kind: "A Sanduhr backup", origin: "Sanduhr",
                 ifLeft: "Sanduhr deletes it only when the file returns byte for byte; otherwise it stays.")
        }
        for name in unique where name.contains(".tmp.") {
            item((seat as NSString).appendingPathComponent(name), kind: "An orphaned temp file", origin: "An interrupted write", ifLeft: kept)
        }
        for name in unique where (name.hasPrefix(".claude.json.backup") || name.contains(".bak")) && !name.hasSuffix(IntegrationInstaller.backupSuffix) {
            item((seat as NSString).appendingPathComponent(name), kind: "A stray backup outside backups/", origin: "A config tool", ifLeft: kept)
        }
        if unique.contains("bridge-spawn") {
            item((seat as NSString).appendingPathComponent("bridge-spawn"), kind: "A bridge-spawn folder", origin: "claude rc", ifLeft: kept)
        }
        // Copies a config tool or a seat migration left beside the seat.
        let seatName = (seat as NSString).lastPathComponent
        for name in ((try? fm.contentsOfDirectory(atPath: home)) ?? []).sorted() where ClaudeCodeFolders.isBackup(name) {
            let mine = name.hasPrefix(ClaudeCodeFolders.backupPrefix) ? isDefault : name.hasPrefix(seatName + ClaudeCodeFolders.backupMarker)
            guard mine else { continue }
            item((home as NSString).appendingPathComponent(name), kind: "A copy of the seat",
                 origin: name.hasPrefix(ClaudeCodeFolders.backupPrefix) ? "A seat migration" : "A config tool", ifLeft: kept)
        }
        let candidates = ClaudeCodeFolders.configCandidates(for: seat, home: home, fileManager: fm)
        if candidates.filter(\.exists).count > 1, let stub = candidates.first(where: { $0.exists && !$0.inUse && !$0.signedIn }) {
            item(stub.path, kind: "A stub .claude.json beside the live one", origin: "A start with CLAUDE_CONFIG_DIR set to this folder",
                 ifLeft: "Read only when CLAUDE_CONFIG_DIR names this folder; kept until you delete it.")
        }
        let projects = (config["projects"] as? [String: Any] ?? [:]).keys
        let twins = Dictionary(grouping: projects, by: { $0.lowercased() }).values.filter { $0.count > 1 }
        if !twins.isEmpty {
            out.append(SeatReport.Leftover(kind: "Project keys that differ only in case", name: "\(twins.count) in .claude.json",
                                           origin: "Claude Code", ifLeft: "Trust, MCP servers and allowed tools may sit on either copy."))
        }
        let memory = SeatScanner.visible((seat as NSString).appendingPathComponent("projects"), fm)
        let memoryTwins = Dictionary(grouping: memory, by: { $0.lowercased() }).values.filter { $0.count > 1 }
        if !memoryTwins.isEmpty {
            out.append(SeatReport.Leftover(kind: "Project folders that differ only in case", name: "\(memoryTwins.count) under projects/",
                                           origin: "Claude Code", ifLeft: "Each keeps its own transcripts and memory."))
        }
        return out
    }
}

/// By Seat's Seat section (item 71, slice 1b): the install model, the branch and whether the
/// origin matches the seat identity file (read from `.git/` files, git is never run), the
/// seat's last sync from `.seat/state.json` ("unknown schema" on a mismatch), the kill switch,
/// and the never-list as three buckets read from the seat's own `.gitignore`.
enum SeatRepo {
    static func read(_ seat: String, _ fm: FileManager = .default) -> SeatReport.RepoPart {
        var part = SeatReport.RepoPart()
        let git = (seat as NSString).appendingPathComponent(".git")
        let dotSeat = (seat as NSString).appendingPathComponent(".seat")
        if fm.fileExists(atPath: git) {
            part.model = .gitClone
        } else if SeatReports.isFolder(dotSeat, fm) {
            part.model = .copyInstalled
        }
        if let head = fm.contents(atPath: (git as NSString).appendingPathComponent("HEAD")).map({ String(decoding: $0, as: UTF8.self) }) {
            let t = head.trimmingCharacters(in: .whitespacesAndNewlines)
            part.branch = t.hasPrefix("ref: refs/heads/") ? String(t.dropFirst("ref: refs/heads/".count)) : "detached"
        }
        let origin = fm.contents(atPath: (git as NSString).appendingPathComponent("config")).flatMap { originURL(String(decoding: $0, as: UTF8.self)) }
        let identity = SeatScanner.readObject((dotSeat as NSString).appendingPathComponent("seat.json"), fm).object
        let declared = identity.flatMap { o in ["remote", "repo", "origin", "url"].lazy.compactMap { o[$0] as? String }.first }
        if let origin, let declared { part.remoteMatches = normalizedRemote(origin) == normalizedRemote(declared) }
        part.sync = syncState((dotSeat as NSString).appendingPathComponent("state.json"), fm)
        part.killSwitch = fm.fileExists(atPath: (dotSeat as NSString).appendingPathComponent("disabled"))
        if let data = fm.contents(atPath: (seat as NSString).appendingPathComponent(".gitignore")) {
            part.buckets = buckets(String(decoding: data, as: UTF8.self))
        }
        return part
    }

    /// `[remote "origin"]`'s url in a git config file.
    static func originURL(_ config: String) -> String? {
        var inOrigin = false
        for raw in config.components(separatedBy: "\n") {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("[") { inOrigin = line == "[remote \"origin\"]"; continue }
            if inOrigin, line.hasPrefix("url"), let eq = line.firstIndex(of: "=") {
                return line[line.index(after: eq)...].trimmingCharacters(in: .whitespaces)
            }
        }
        return nil
    }

    /// A remote as `host/owner/repo`, lowercased: scheme, user, `.git` and `git@host:` dropped.
    static func normalizedRemote(_ s: String) -> String {
        var t = s.trimmingCharacters(in: .whitespaces).lowercased()
        if let r = t.range(of: "://") { t = String(t[r.upperBound...]) }
        if let at = t.firstIndex(of: "@"), !t[..<at].contains("/") { t = String(t[t.index(after: at)...]) }
        t = t.replacingOccurrences(of: ":", with: "/")
        while t.hasSuffix("/") { t.removeLast() }
        if t.hasSuffix(".git") { t.removeLast(4) }
        return t
    }

    /// `.seat/state.json`: the fields Sanduhr knows in either spelling (`lastRun`/`last_run`,
    /// `phase`, `paused`, `report`/`lastReport`/`last_report`, `apply`/`applied`/`applySteps`/
    /// `apply_steps`), "unknown schema" when none of them is there or the file isn't an object.
    static func syncState(_ path: String, _ fm: FileManager = .default) -> SeatReport.SyncState {
        switch SeatScanner.readObject(path, fm) {
        case .missing: return .none
        case .unreadable: return .unknownSchema
        case .object(let o):
            func pick(_ keys: [String]) -> Any? { keys.lazy.compactMap { o[$0] }.first }
            let lastRun = pick(["lastRun", "last_run"])
            let phase = pick(["phase"])
            let paused = pick(["paused"])
            let report = pick(["report", "lastReport", "last_report"])
            let apply = pick(["apply", "applied", "applySteps", "apply_steps"])
            guard lastRun != nil || phase != nil || paused != nil || report != nil || apply != nil else { return .unknownSchema }
            var read = SeatReport.SyncRead()
            read.lastRun = lastRun.map { ConfigMask.maskString(String(describing: $0)) }
            read.phase = phase.map { ConfigMask.maskString(String(describing: $0)) }
            read.paused = IntegrationInstaller.boolValue(paused)
            if let lines = report as? [Any] {
                read.report = lines.compactMap { $0 as? String }.map(ConfigMask.maskString)
            } else if let text = report as? String {
                read.report = text.components(separatedBy: "\n").filter { !$0.isEmpty }.map(ConfigMask.maskString)
            }
            read.applySteps = (apply as? [Any])?.count ?? (apply as? [String: Any])?.count ?? 0
            return .read(read)
        }
    }

    /// Words that put an ignored pattern in the never bucket.
    static let neverWords = ["credential", ".env", "secret", "token", "key", ".pem", "log", "password", "auth"]

    /// The seat's `.gitignore` as three buckets: `!pattern` lines are synced (the allow-list),
    /// ignored patterns naming credentials, env files, keys or logs are never, the rest
    /// machine-only by design. Comments and blank lines are skipped.
    static func buckets(_ gitignore: String) -> [SeatReport.Bucket: [String]] {
        var out: [SeatReport.Bucket: [String]] = [:]
        for raw in gitignore.components(separatedBy: "\n") {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard !line.isEmpty, !line.hasPrefix("#") else { continue }
            if line.hasPrefix("!") {
                out[.synced, default: []].append(String(line.dropFirst()))
            } else if neverWords.contains(where: { line.lowercased().contains($0) }) {
                out[.never, default: []].append(line)
            } else {
                out[.machineOnly, default: []].append(line)
            }
        }
        return out
    }

    /// Which seat each project routes to: `claudeCode.environmentVariables`'
    /// `CLAUDE_CONFIG_DIR` in its `.vscode/settings.json` (a list of `{name, value}`, or an
    /// object). Projects without one aren't listed.
    static func routes(_ projects: [String], home: String, _ fm: FileManager = .default) -> [SeatReport.Route] {
        projects.compactMap { project in
            let file = (project as NSString).appendingPathComponent(".vscode/settings.json")
            guard let o = SeatScanner.readObject(file, fm).object else { return nil }
            let vars = o["claudeCode.environmentVariables"]
            var value: String?
            if let list = vars as? [Any] {
                value = list.compactMap { $0 as? [String: Any] }.first { $0["name"] as? String == "CLAUDE_CONFIG_DIR" }?["value"] as? String
            } else if let dict = vars as? [String: Any] {
                value = dict["CLAUDE_CONFIG_DIR"] as? String
            }
            guard let value, !value.isEmpty else { return nil }
            return SeatReport.Route(project: project, target: ClaudeProcesses.seat(configDir: value, home: home))
        }
    }
}

/// Drift between two seats (item 71, slice 1b): settings keys that differ (with masked values,
/// shown only between two personal seats) and extensions one holds and the other doesn't.
enum SeatDriftCheck {
    static func drift(mine: [String: Any], theirs: [String: Any], myScan: SeatScan, theirScan: SeatScan,
                      other: String) -> SeatReport.Drift {
        var d = SeatReport.Drift(other: other)
        let paths = Set(SettingsPrecedence.rowPaths(mine)).union(SettingsPrecedence.rowPaths(theirs))
        for path in paths.sorted(by: { $0.joined(separator: ".") < $1.joined(separator: ".") }) {
            let a = SettingsPrecedence.value(at: path, in: mine), b = SettingsPrecedence.value(at: path, in: theirs)
            if let a, let b, (a as? NSObject)?.isEqual(b) == true { continue }
            d.keys.append(SeatReport.DriftKey(key: path.joined(separator: "."),
                                              mine: a.map { ConfigMask.display($0, at: path) },
                                              theirs: b.map { ConfigMask.display($0, at: path) }))
        }
        let here = names(myScan), there = names(theirScan)
        d.onlyHere = here.subtracting(there).sorted()
        d.onlyThere = there.subtracting(here).sorted()
        return d
    }

    /// `kind:name` for what a seat holds at its own scope (projects left out).
    static func names(_ s: SeatScan) -> Set<String> {
        var out = Set<String>()
        for p in s.plugins { out.insert("plugin:" + p.name) }
        for (k, list) in [("agent", s.agents), ("skill", s.skills), ("command", s.commands), ("output style", s.outputStyles)] {
            for e in list where e.scope != .project { out.insert(k + ":" + e.name) }
        }
        for m in s.mcpServers where m.scope == .user { out.insert("mcp:" + m.name) }
        return out
    }
}

/// Modification dates of every file under some folders, to prove a read changed nothing
/// (item 71, slice 1b). Capped, so a huge seat stays quick.
enum SeatMtimes {
    static func snapshot(_ folders: [String], limit: Int = 50_000, fileManager fm: FileManager = .default) -> [String: Date] {
        var out: [String: Date] = [:]
        for folder in folders {
            out[folder] = (try? fm.attributesOfItem(atPath: folder))?[.modificationDate] as? Date
            guard let e = fm.enumerator(atPath: folder) else { continue }
            while let rel = e.nextObject() as? String, out.count < limit {
                let p = (folder as NSString).appendingPathComponent(rel)
                out[p] = (try? fm.attributesOfItem(atPath: p))?[.modificationDate] as? Date
            }
        }
        return out
    }
}
