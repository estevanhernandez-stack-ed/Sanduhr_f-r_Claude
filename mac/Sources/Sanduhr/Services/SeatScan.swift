import Foundation

/// An agent, skill, command or output style a seat (or one of its projects) provides (item 71,
/// slice 1), read from the file's frontmatter. Nothing it names is run.
struct SeatEntry: Equatable, Sendable {
    enum Scope: String, Sendable {
        /// The seat's own folder (`agents/`, `skills/`, `commands/`, `output-styles/`).
        case seat
        /// `skills/synced/`: arrives from the claude.ai account and is overwritten on the next sync.
        case synced
        /// A project's `.claude/` folder.
        case project
    }

    let name: String
    var description: String
    let scope: Scope
    /// The file it was read from. Only the seat's own view shows it, never the Catalog.
    let path: String
    /// The project folder for `.project`, nil otherwise. Never shown across the wall.
    var project: String?
    /// A skill's `when_to_use`, which Claude Code appends to the description in its skill listing.
    var whenToUse = ""
    /// `disable-model-invocation: true`: Claude Code keeps it out of the listing Claude reads.
    var modelHidden = false
}

/// What one plugin folder brings (item 71, slice 1c): its skills, agents, commands and output
/// styles from the default folders, and how many MCP servers it declares. Read from frontmatter;
/// nothing it names is run.
struct PluginContents: Equatable, Sendable {
    var skills: [SeatEntry] = []
    var agents: [SeatEntry] = []
    var commands: [SeatEntry] = []
    var outputStyles: [SeatEntry] = []
    var mcpServers = 0

    var isEmpty: Bool { skills.isEmpty && agents.isEmpty && commands.isEmpty && outputStyles.isEmpty && mcpServers == 0 }
}

/// The settings that shape Claude Code's skill listing (item 71, slice 1c), from the seat's
/// `settings.json` with managed settings over it. A shell's environment can't be seen.
struct ListingSettings: Equatable, Sendable {
    /// `skillListingBudgetFraction`: the share of the context window the listing may use.
    var budgetFraction = 0.01
    /// `SLASH_COMMAND_TOOL_CHAR_BUDGET` in the settings' `env`: a fixed budget in characters.
    var budgetChars: Int?
    /// `skillListingMaxDescChars`: each entry's description and `when_to_use`, cut to this.
    var maxDescChars = 1_536
    /// `skillOverrides`: skill name to `on`, `name-only`, `user-invocable-only` or `off`.
    /// Plugin skills ignore it.
    var overrides: [String: String] = [:]

    static func read(_ layers: [[String: Any]]) -> ListingSettings {
        var out = ListingSettings()
        for o in layers {
            if let f = (o["skillListingBudgetFraction"] as? NSNumber)?.doubleValue, f > 0 { out.budgetFraction = f }
            if let n = (o["skillListingMaxDescChars"] as? NSNumber)?.intValue, n > 0 { out.maxDescChars = n }
            if let env = o["env"] as? [String: Any], let v = env["SLASH_COMMAND_TOOL_CHAR_BUDGET"] {
                let n = (v as? NSNumber)?.intValue ?? Int((v as? String ?? "").trimmingCharacters(in: .whitespaces))
                if let n, n > 0 { out.budgetChars = n }
            }
            for (k, v) in o["skillOverrides"] as? [String: Any] ?? [:] {
                if let v = v as? String { out.overrides[k] = v }
            }
        }
        return out
    }
}

/// An MCP server a seat knows of, by name, with what it runs and the names (never values) of
/// its environment and headers.
struct SeatMCPServer: Equatable, Sendable {
    enum Scope: String, CaseIterable, Sendable {
        /// `managed-mcp.json` beside the managed settings: above every other source.
        case managed
        /// A project's entry in `.claude.json` (`projects.<path>.mcpServers`).
        case local
        /// A project's `.mcp.json`.
        case project
        /// `.claude.json`'s top-level `mcpServers`.
        case user
        /// A plugin's `.mcp.json` or manifest `mcpServers`.
        case plugin

        var title: String {
            switch self {
            case .managed: "Managed"
            case .local: "Local (this project, .claude.json)"
            case .project: "Project (.mcp.json)"
            case .user: "User (.claude.json)"
            case .plugin: "Plugin"
            }
        }
    }

    let name: String
    let scope: Scope
    /// `stdio`, `http`, `sse` or what the entry says.
    var transport: String
    /// The command (masked) or the URL's host.
    var target: String
    var envNames: [String] = []
    var headerNames: [String] = []
    /// An env or header value is written out rather than a `${VAR}` reference (the value itself is
    /// never kept).
    var literalValues = false
    /// The project folder (local and project scopes) or the plugin's name. Never shown across the wall.
    var owner: String?
}

/// A marketplace a seat added, and the plugins its cached `marketplace.json` lists. Read from
/// files only: no network and no refresh.
struct SeatMarketplace: Equatable, Sendable {
    struct Listed: Equatable, Sendable {
        let name: String
        var description = ""
        var version: String?
    }

    let name: String
    /// Where it comes from, in words (`github owner/repo`, `directory`, `git`), masked.
    var source: String
    /// Its folder exists here (`directory` sources can be on one machine only).
    var present: Bool
    /// The cached `marketplace.json` was found and read.
    var cached: Bool
    var plugins: [Listed] = []
}

/// What one seat holds, read from its files (item 71, slice 1): the plugins and mods
/// `ModInventory` finds, the marketplaces it added, plugins enabled in its projects, its
/// agents, skills, commands and output styles, and its MCP servers. Files only: no CLI, no
/// network, no mod or server started, nothing written. Project folders are the ones its
/// `.claude.json` lists, at most `ModOverrides.projectLimit`.
struct SeatScan: Equatable, Sendable {
    let seat: String
    /// Its settings.json is there but doesn't parse: why, with the position, never its text.
    var settingsUnreadable: String?
    /// Files of the seat that couldn't be read (too large, not JSON, not a regular file), by
    /// name: the Catalog says so and points at By Seat.
    var unreadableFiles: [String] = []
    var plugins: [ModItem] = []
    var marketplaces: [SeatMarketplace] = []
    /// Plugin keys (`name@market`) a project turns on, resolved as Claude Code does (the
    /// project's `settings.local.json` over its `settings.json`, key by key), with the projects.
    var projectPlugins: [String: Set<String>] = [:]
    var agents: [SeatEntry] = []
    var skills: [SeatEntry] = []
    var commands: [SeatEntry] = []
    var outputStyles: [SeatEntry] = []
    var mcpServers: [SeatMCPServer] = []
    /// The project folders read.
    var projects: [String] = []
    /// A folder walk hit its limit: what's listed is partial.
    var partial = false
    /// The top-level keys of the seat's `.claude.json` (By Seat's lints read them, so the file is
    /// parsed once per load), and how many of its project keys differ from another only in case.
    var configKeys: [String] = []
    var caseTwinProjects = 0
    /// Each plugin's contents by its item id (`ModItem.id`), for the plugins with a folder here.
    var pluginContents: [String: PluginContents] = [:]
    var listing = ListingSettings()
}

enum SeatScanner {
    /// The most entries read per folder kind, so a huge folder stays quick.
    static let entryLimit = 500
    /// Folder entries visited per seat across every walk, and how deep a walk goes.
    static let visitLimit = 5_000
    static let depthLimit = 3

    /// Size caps: a file over its cap reads as unreadable ("is larger than …"), never as empty.
    enum Cap {
        static let claudeJSON = 32 << 20
        static let manifest = 4 << 20
        static let settings = 2 << 20
        static let small = 1 << 20
    }

    /// The visits left for one seat's walks.
    final class Budget {
        var left: Int
        var hit = false
        init(_ n: Int = SeatScanner.visitLimit) { left = n }

        /// Takes one visit; false once the budget is spent.
        func take() -> Bool {
            guard left > 0 else {
                hit = true
                return false
            }
            left -= 1
            return true
        }
    }

    /// `config` is the seat's `.claude.json` placement, already read by the caller, so the
    /// file is parsed once per load.
    static func scan(seat: String, home: String = NSHomeDirectory(), config candidates: [ClaudeCodeFolders.ConfigCandidate]? = nil,
                     fileManager fm: FileManager = .default,
                     managedFolder: String = (ModOverrides.managedFile as NSString).deletingLastPathComponent) -> SeatScan {
        let root = AccountData.normalized(seat)
        var out = SeatScan(seat: root)
        let budget = Budget()
        func read(_ path: String, cap: Int, name: String) -> [String: Any]? {
            switch readObject(path, fm, cap: cap) {
            case .object(let o): return o
            case .unreadable:
                if !out.unreadableFiles.contains(name) { out.unreadableFiles.append(name) }
                return nil
            case .missing: return nil
            }
        }
        let settingsFile = (root as NSString).appendingPathComponent("settings.json")
        var settings: [String: Any] = [:]
        switch readObject(settingsFile, fm, cap: Cap.settings) {
        case .object(let o): settings = o
        case .unreadable(let why):
            out.settingsUnreadable = why
            out.unreadableFiles.append("settings.json")
        case .missing: break
        }
        out.plugins = ModInventory.scan(folder: root, home: home)
        out.plugins += syncedFolders(root, settings: settings, known: Set(out.plugins.compactMap(\.key)), fm)
        let managed = readObject((managedFolder as NSString).appendingPathComponent("managed-settings.json"), fm, cap: Cap.settings).object ?? [:]
        applyManaged(managed["enabledPlugins"] as? [String: Any] ?? [:], to: &out.plugins, folder: root)
        out.marketplaces = marketplaces(root, settings: settings, fm) { name in
            if !out.unreadableFiles.contains(name) { out.unreadableFiles.append(name) }
        }
        if case .unreadable = readObject((root as NSString).appendingPathComponent("plugins/installed_plugins.json"), fm, cap: Cap.manifest) {
            out.unreadableFiles.append("plugins/installed_plugins.json")
        }

        let places = candidates ?? ClaudeCodeFolders.configCandidates(for: root, home: home, fileManager: fm)
        let configPath = places.first(where: \.inUse)?.path ?? (root as NSString).appendingPathComponent(ClaudeCodeFolders.configName)
        let config = read(configPath, cap: Cap.claudeJSON, name: ".claude.json") ?? [:]
        let projectTable = config["projects"] as? [String: Any] ?? [:]
        out.projects = projectTable.keys.sorted().prefix(ModOverrides.projectLimit).map { $0 }
        out.configKeys = config.keys.sorted()
        out.caseTwinProjects = Dictionary(grouping: projectTable.keys, by: { $0.lowercased() }).values.filter { $0.count > 1 }.count

        out.agents = entries(in: (root as NSString).appendingPathComponent("agents"), kind: .agent, scope: .seat, fm, budget)
        out.commands = entries(in: (root as NSString).appendingPathComponent("commands"), kind: .command, scope: .seat, fm, budget)
        out.outputStyles = entries(in: (root as NSString).appendingPathComponent("output-styles"), kind: .outputStyle, scope: .seat, fm, budget)
        let skillsDir = (root as NSString).appendingPathComponent("skills")
        out.skills = entries(in: skillsDir, kind: .skill, scope: .seat, fm, budget)
            + entries(in: (skillsDir as NSString).appendingPathComponent("synced"), kind: .skill, scope: .synced, fm, budget)

        out.mcpServers = mcpServers(in: config, scope: .user, owner: nil)
        out.mcpServers += managedServers(managedFolder, fm)
        for item in out.plugins where item.state != .missing {
            guard let path = item.path else { continue }
            let servers = pluginServers(path, name: item.name, fm)
            out.mcpServers += servers
            out.pluginContents[item.id] = contents(of: path, servers: servers.count, fm, budget)
        }
        out.listing = ListingSettings.read([settings, managed])

        // Each project: its own extensions, its MCP servers and the plugins it turns on.
        for project in out.projects {
            let dot = (project as NSString).appendingPathComponent(".claude")
            func tag(_ list: [SeatEntry]) -> [SeatEntry] {
                list.map { var e = $0; e.project = project; return e }
            }
            out.agents += tag(entries(in: (dot as NSString).appendingPathComponent("agents"), kind: .agent, scope: .project, fm, budget))
            out.commands += tag(entries(in: (dot as NSString).appendingPathComponent("commands"), kind: .command, scope: .project, fm, budget))
            out.outputStyles += tag(entries(in: (dot as NSString).appendingPathComponent("output-styles"), kind: .outputStyle, scope: .project, fm, budget))
            out.skills += tag(entries(in: (dot as NSString).appendingPathComponent("skills"), kind: .skill, scope: .project, fm, budget))
            if let entry = projectTable[project] as? [String: Any] {
                out.mcpServers += mcpServers(in: entry, scope: .local, owner: project)
            }
            if let mcp = readObject((project as NSString).appendingPathComponent(".mcp.json"), fm, cap: Cap.small).object {
                out.mcpServers += mcpServers(in: mcp, scope: .project, owner: project)
            }
            let shared = readObject((dot as NSString).appendingPathComponent("settings.json"), fm, cap: Cap.settings).object?["enabledPlugins"] as? [String: Any] ?? [:]
            let local = readObject((dot as NSString).appendingPathComponent("settings.local.json"), fm, cap: Cap.settings).object?["enabledPlugins"] as? [String: Any] ?? [:]
            for (key, on) in projectEnabled(shared: shared, local: local, managed: managed["enabledPlugins"] as? [String: Any] ?? [:]) where on {
                out.projectPlugins[key, default: []].insert(project)
            }
        }
        out.partial = budget.hit
        return out
    }

    /// A project's `enabledPlugins` as Claude Code resolves it: managed over the project's
    /// `settings.local.json` over its `settings.json`, key by key. An install record at project
    /// scope says the plugin is installed there, not that it is on, so it isn't counted.
    static func projectEnabled(shared: [String: Any], local: [String: Any], managed: [String: Any]) -> [String: Bool] {
        var out: [String: Bool] = [:]
        for layer in [shared, local, managed] {
            for (key, value) in layer {
                if let b = IntegrationInstaller.boolValue(value) { out[key] = b }
            }
        }
        return out
    }

    /// Managed `enabledPlugins` win over the seat's own: a managed true turns an installed plugin
    /// on (or declares it), a managed false turns it off.
    static func applyManaged(_ managed: [String: Any], to items: inout [ModItem], folder: String) {
        for (key, value) in managed.sorted(by: { $0.key < $1.key }) {
            guard let on = IntegrationInstaller.boolValue(value) else { continue }
            if let i = items.firstIndex(where: { $0.key == key }) {
                if items[i].state != .missing && items[i].state != .session { items[i].state = on ? .on : .off }
            } else if on, ModInventory.isMarketplaceKey(key) {
                let name = ConfigCatalog.pluginName(key)
                items.append(ModItem(folder: folder, name: name, path: nil,
                                     origin: .installed(marketplace: ConfigCatalog.market(key)), key: key, state: .on))
            }
        }
    }

    // MARK: Files

    enum Read {
        case missing
        case unreadable(String)
        case object([String: Any])

        var object: [String: Any]? {
            if case .object(let o) = self { return o }
            return nil
        }
    }

    /// What a bounded read of a file found.
    enum Bytes {
        case missing
        /// Not a regular file (a folder, a FIFO, a socket, a device), or larger than the cap.
        case refused(String)
        case data(Data)
    }

    /// A regular file's bytes, at most `cap` of them. A FIFO, socket or device is never opened.
    static func readFile(_ path: String, cap: Int, _ fm: FileManager = .default) -> Bytes {
        guard let a = try? fm.attributesOfItem(atPath: path) else { return .missing }
        guard (a[.type] as? FileAttributeType) == .typeRegular else { return .refused("isn't a regular file") }
        let size = (a[.size] as? NSNumber)?.intValue ?? 0
        guard size <= cap else { return .refused("is larger than \(max(1, cap >> 20)) MB, so Sanduhr doesn't read it") }
        guard let h = FileHandle(forReadingAtPath: path) else { return .missing }
        defer { try? h.close() }
        return .data((try? h.read(upToCount: cap)) ?? Data())
    }

    /// A JSON object file: missing, unreadable (with where it stops parsing, never the text) or
    /// read. A file whose top level isn't an object, that holds a comment or a trailing comma
    /// (Claude Code's strict JSON refuses both), that isn't a regular file or that is over `cap`
    /// is unreadable too.
    static func readObject(_ path: String, _ fm: FileManager = .default, cap: Int = Cap.small) -> Read {
        let data: Data
        switch readFile(path, cap: cap, fm) {
        case .missing: return .missing
        case .refused(let why): return .unreadable(why)
        case .data(let d): data = d
        }
        do {
            let value = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
            guard let o = value as? [String: Any] else { return .unreadable("has no JSON object at its top level") }
            if let problem = strictProblem(data) { return .unreadable(problem) }
            return .object(o)
        } catch {
            return .unreadable(strictProblem(data) ?? parseError(error))
        }
    }

    /// What Foundation's parser lets through and Claude Code's strict JSON doesn't: a comment or
    /// a trailing comma outside strings, with where it is ("isn't valid JSON (a trailing comma
    /// around line 1, column 15)"), columns counted in characters. Nil for strict JSON.
    static func strictProblem(_ data: Data) -> String? {
        data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) -> String? in
            let b = raw.bindMemory(to: UInt8.self)
            var i = 0, line = 1, column = 1
            var inString = false
            var lastComma: (line: Int, column: Int)?
            func at(_ l: Int, _ c: Int) -> String { "around line \(l), column \(c)" }
            while i < b.count {
                let c = b[i]
                if inString {
                    if c == UInt8(ascii: "\\") {
                        i += 2
                        column += 2
                        continue
                    }
                    if c == UInt8(ascii: "\"") { inString = false }
                } else {
                    switch c {
                    case UInt8(ascii: "\""):
                        inString = true
                        lastComma = nil
                    case UInt8(ascii: "/") where i + 1 < b.count && (b[i + 1] == UInt8(ascii: "/") || b[i + 1] == UInt8(ascii: "*")):
                        return "isn't valid JSON (a comment \(at(line, column)))"
                    case UInt8(ascii: ","):
                        lastComma = (line, column)
                    case UInt8(ascii: "}"), UInt8(ascii: "]"):
                        if let comma = lastComma { return "isn't valid JSON (a trailing comma \(at(comma.line, comma.column)))" }
                    case UInt8(ascii: " "), UInt8(ascii: "\t"), UInt8(ascii: "\r"), UInt8(ascii: "\n"):
                        break
                    default:
                        lastComma = nil
                    }
                }
                if c == UInt8(ascii: "\n") {
                    line += 1
                    column = 1
                } else if c & 0xC0 != 0x80 {
                    // A UTF-8 continuation byte isn't a new character.
                    column += 1
                }
                i += 1
            }
            return nil
        }
    }

    /// A JSONC file (VS Code's `.vscode/settings.json`): comments and trailing commas allowed,
    /// stripped outside strings before parsing. Claude Code's own files stay strict.
    static func lenientObject(_ path: String, _ fm: FileManager = .default, cap: Int = Cap.small) -> [String: Any]? {
        guard case .data(let data) = readFile(path, cap: cap, fm) else { return nil }
        let clean = stripJSONC(data)
        return (try? JSONSerialization.jsonObject(with: clean)) as? [String: Any]
    }

    /// `data` without `//` and `/* */` comments and without commas right before `}` or `]`,
    /// strings left as they are.
    static func stripJSONC(_ data: Data) -> Data {
        let b = [UInt8](data)
        var out: [UInt8] = []
        out.reserveCapacity(b.count)
        var i = 0
        var inString = false
        while i < b.count {
            let c = b[i]
            if inString {
                out.append(c)
                if c == UInt8(ascii: "\\"), i + 1 < b.count {
                    out.append(b[i + 1])
                    i += 2
                    continue
                }
                if c == UInt8(ascii: "\"") { inString = false }
                i += 1
                continue
            }
            if c == UInt8(ascii: "/"), i + 1 < b.count, b[i + 1] == UInt8(ascii: "/") {
                while i < b.count, b[i] != UInt8(ascii: "\n") { i += 1 }
                continue
            }
            if c == UInt8(ascii: "/"), i + 1 < b.count, b[i + 1] == UInt8(ascii: "*") {
                i += 2
                while i + 1 < b.count, !(b[i] == UInt8(ascii: "*") && b[i + 1] == UInt8(ascii: "/")) { i += 1 }
                i += 2
                continue
            }
            if c == UInt8(ascii: "}") || c == UInt8(ascii: "]") {
                // Drop a comma left before this close, past any whitespace.
                var j = out.count - 1
                while j >= 0, [UInt8(ascii: " "), UInt8(ascii: "\t"), UInt8(ascii: "\r"), UInt8(ascii: "\n")].contains(out[j]) { j -= 1 }
                if j >= 0, out[j] == UInt8(ascii: ",") { out.remove(at: j) }
            }
            if c == UInt8(ascii: "\"") { inString = true }
            out.append(c)
            i += 1
        }
        return Data(out)
    }

    /// "isn't valid JSON (around line 3, column 5)": the position only. Foundation's message can
    /// quote a character of the file, so nothing else of it is kept.
    static func parseError(_ error: Error) -> String {
        let text = ((error as NSError).userInfo["NSDebugDescription"] as? String) ?? ""
        if let r = text.range(of: #"line \d+, column \d+"#, options: .regularExpression) {
            return "isn't valid JSON (around \(text[r]))"
        }
        if let r = text.range(of: #"character \d+"#, options: .regularExpression) {
            return "isn't valid JSON (around \(text[r]))"
        }
        return "isn't valid JSON"
    }

    // MARK: Agents, skills, commands, output styles

    enum EntryKind { case agent, skill, command, outputStyle }

    /// The entries in one folder. Agents and output styles: `*.md` files (subfolders too, by
    /// file name). Commands: `*.md`, a subfolder's as `folder:name` (each `/` of the path under
    /// `commands/` becomes `:`, as the slash-commands docs name them: `frontend/component.md` is
    /// `/frontend:component`). Skills: each subfolder with a `SKILL.md` (never a plugin folder,
    /// which ModInventory lists, nor `synced/` itself).
    ///
    /// The walk is bounded: hidden folders (`.git`) and `node_modules` are skipped whole, it goes
    /// at most `depthLimit` folders down, and every entry it visits spends the seat's `budget`.
    /// Symlinked subfolders aren't followed (the enumerator's default), so a link loop can't
    /// trap it; a command or agent kept behind a folder link isn't listed.
    static func entries(in dir: String, kind: EntryKind, scope: SeatEntry.Scope, _ fm: FileManager,
                        _ budget: Budget = Budget()) -> [SeatEntry] {
        var out: [SeatEntry] = []
        if kind == .skill {
            for name in visible(dir, fm) where name != "synced" || scope == .synced {
                guard budget.take() else { break }
                let folder = (dir as NSString).appendingPathComponent(name)
                guard !fm.fileExists(atPath: (folder as NSString).appendingPathComponent(".claude-plugin/plugin.json")) else { continue }
                let file = (folder as NSString).appendingPathComponent("SKILL.md")
                guard isRegular(file, fm) else { continue }
                let front = frontmatter(file, fm)
                out.append(SeatEntry(name: nonEmpty(front["name"]) ?? name, description: front["description"] ?? "",
                                     scope: scope, path: file, whenToUse: front["when_to_use"] ?? "",
                                     modelHidden: isTrue(front["disable-model-invocation"])))
                if out.count >= entryLimit { break }
            }
            return out
        }
        let base = URL(fileURLWithPath: dir)
        guard let e = fm.enumerator(at: base, includingPropertiesForKeys: [.isRegularFileKey, .isDirectoryKey],
                                    options: [.skipsHiddenFiles]) else { return [] }
        while let url = e.nextObject() as? URL {
            guard budget.take() else { break }
            let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .isDirectoryKey])
            if values?.isDirectory == true {
                if url.lastPathComponent == "node_modules" || e.level > depthLimit { e.skipDescendants() }
                continue
            }
            guard values?.isRegularFile == true, url.pathExtension.lowercased() == "md" else { continue }
            let rel = String(url.standardizedFileURL.path.dropFirst(base.standardizedFileURL.path.count + 1))
            let front = frontmatter(url.path, fm)
            let stem = String(rel.dropLast(3))
            let fallback = kind == .command ? stem.replacingOccurrences(of: "/", with: ":") : (stem as NSString).lastPathComponent
            let name = kind == .command ? fallback : (nonEmpty(front["name"]) ?? fallback)
            out.append(SeatEntry(name: name, description: front["description"] ?? "", scope: scope, path: url.path,
                                 whenToUse: front["when_to_use"] ?? "", modelHidden: isTrue(front["disable-model-invocation"])))
            if out.count >= entryLimit { break }
        }
        return out.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    /// A plugin folder's skills, agents, commands and output styles from their default folders,
    /// on the seat's walk budget. A manifest that points elsewhere isn't followed.
    static func contents(of path: String, servers: Int, _ fm: FileManager, _ budget: Budget = Budget()) -> PluginContents {
        func at(_ sub: String) -> String { (path as NSString).appendingPathComponent(sub) }
        return PluginContents(skills: entries(in: at("skills"), kind: .skill, scope: .seat, fm, budget),
                              agents: entries(in: at("agents"), kind: .agent, scope: .seat, fm, budget),
                              commands: entries(in: at("commands"), kind: .command, scope: .seat, fm, budget),
                              outputStyles: entries(in: at("output-styles"), kind: .outputStyle, scope: .seat, fm, budget),
                              mcpServers: servers)
    }

    static func isTrue(_ s: String?) -> Bool { s?.trimmingCharacters(in: .whitespaces).lowercased() == "true" }

    static func isRegular(_ path: String, _ fm: FileManager) -> Bool {
        ((try? fm.attributesOfItem(atPath: path))?[.type] as? FileAttributeType) == .typeRegular
    }

    static func visible(_ dir: String, _ fm: FileManager) -> [String] {
        ((try? fm.contentsOfDirectory(atPath: dir)) ?? []).filter { !$0.hasPrefix(".") }.sorted()
    }

    static func nonEmpty(_ s: String?) -> String? {
        guard let s, !s.isEmpty else { return nil }
        return s
    }

    /// The `key: value` lines of a Markdown file's YAML frontmatter (first 16 KB), quotes
    /// dropped; a folded or literal block (`|`, `>`) joins its indented lines with spaces.
    static func frontmatter(_ path: String, _ fm: FileManager = .default) -> [String: String] {
        guard isRegular(path, fm), let h = FileHandle(forReadingAtPath: path) else { return [:] }
        defer { try? h.close() }
        let data = (try? h.read(upToCount: 16 * 1024)) ?? Data()
        return frontmatter(text: String(decoding: data, as: UTF8.self))
    }

    static func frontmatter(text: String) -> [String: String] {
        var lines = text.components(separatedBy: "\n").map { $0.hasSuffix("\r") ? String($0.dropLast()) : $0 }
        guard lines.first?.trimmingCharacters(in: .whitespaces) == "---" else { return [:] }
        lines.removeFirst()
        var out: [String: String] = [:]
        var block: String?
        for line in lines {
            if line.trimmingCharacters(in: .whitespaces) == "---" { break }
            if let key = block {
                if line.hasPrefix(" ") || line.hasPrefix("\t") || line.isEmpty {
                    let t = line.trimmingCharacters(in: .whitespaces)
                    let so = out[key] ?? ""
                    if !t.isEmpty { out[key] = so.isEmpty ? t : so + " " + t }
                    continue
                }
                block = nil
            }
            guard !line.hasPrefix(" "), let colon = line.firstIndex(of: ":") else { continue }
            let key = line[..<colon].trimmingCharacters(in: .whitespaces)
            var value = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            if ["|", ">", "|-", ">-", "|+", ">+"].contains(value) {
                block = key
                out[key] = ""
                continue
            }
            if value.count >= 2, let f = value.first, f == "\"" || f == "'", value.last == f {
                value = String(value.dropFirst().dropLast())
            }
            out[key] = value
        }
        return out
    }

    // MARK: Plugins

    /// Plugins claude.ai syncs into the seat that no `enabledPlugins` key or install record
    /// names: each plugin folder under `plugins/synced/` or `plugins/cache/synced/` (where a
    /// synced plugin's files are kept is unverified; a seat without either folder lists none),
    /// as `name@synced`, on unless `enabledPlugins` turns it off.
    static func syncedFolders(_ root: String, settings: [String: Any], known: Set<String>, _ fm: FileManager) -> [ModItem] {
        let enabled = settings["enabledPlugins"] as? [String: Any] ?? [:]
        var out: [ModItem] = []
        for base in ["plugins/synced", "plugins/cache/synced"] {
            let dir = (root as NSString).appendingPathComponent(base)
            for name in visible(dir, fm) {
                let folder = (dir as NSString).appendingPathComponent(name)
                let manifest = ModStatusEntries.manifest(URL(fileURLWithPath: folder))
                let plugin = nonEmpty(manifest["name"] as? String) ?? name
                let key = "\(plugin)@synced"
                guard !known.contains(key), !out.contains(where: { $0.key == key }),
                      fm.fileExists(atPath: (folder as NSString).appendingPathComponent(".claude-plugin/plugin.json")) else { continue }
                var item = ModItem(folder: root, name: plugin, path: folder, origin: .installed(marketplace: "synced"), key: key)
                item.version = manifest["version"] as? String
                item.description = manifest["description"] as? String ?? ""
                item.isMod = ModStatusEntries.isMod(URL(fileURLWithPath: folder))
                item.state = IntegrationInstaller.boolValue(enabled[key]) == false ? .off : .on
                out.append(item)
            }
        }
        return out
    }

    /// The marketplaces the seat added (`plugins/known_marketplaces.json`) and the ones its
    /// settings declare (`extraKnownMarketplaces`), each with its cached `marketplace.json`.
    static func marketplaces(_ root: String, settings: [String: Any], _ fm: FileManager,
                             unreadable: (String) -> Void = { _ in }) -> [SeatMarketplace] {
        let knownRead = readObject((root as NSString).appendingPathComponent("plugins/known_marketplaces.json"), fm, cap: Cap.small)
        if case .unreadable = knownRead { unreadable("plugins/known_marketplaces.json") }
        let known = knownRead.object ?? [:]
        let declared = settings["extraKnownMarketplaces"] as? [String: Any] ?? [:]
        var out: [SeatMarketplace] = []
        for name in Set(known.keys).union(declared.keys).sorted() {
            let entry = known[name] as? [String: Any] ?? declared[name] as? [String: Any] ?? [:]
            let location = (entry["installLocation"] as? String)
                ?? (root as NSString).appendingPathComponent("plugins/marketplaces/\(name)")
            var isDir: ObjCBool = false
            let present = fm.fileExists(atPath: location, isDirectory: &isDir) && isDir.boolValue
            var m = SeatMarketplace(name: name, source: sourceTitle(entry["source"]), present: present, cached: false)
            let manifestRead = readObject((location as NSString).appendingPathComponent(".claude-plugin/marketplace.json"), fm, cap: Cap.manifest)
            if case .unreadable = manifestRead { unreadable("the \(name) marketplace's marketplace.json") }
            let manifest = manifestRead.object
            if let manifest {
                m.cached = true
                for p in manifest["plugins"] as? [Any] ?? [] {
                    guard let o = p as? [String: Any], let n = nonEmpty(o["name"] as? String) else { continue }
                    m.plugins.append(SeatMarketplace.Listed(name: n, description: o["description"] as? String ?? "",
                                                            version: o["version"] as? String))
                }
            }
            out.append(m)
        }
        return out
    }

    /// A marketplace source in words, masked: `github owner/repo`, `git <url>`, `directory`, `url`.
    static func sourceTitle(_ source: Any?) -> String {
        guard let s = source as? [String: Any] else { return "no source declared" }
        let kind = s["source"] as? String ?? "unknown"
        switch kind {
        case "github": return "github " + ConfigMask.maskString(s["repo"] as? String ?? "")
        case "git", "url": return kind + " " + ConfigMask.maskString(s["url"] as? String ?? "")
        default: return kind
        }
    }

    // MARK: MCP servers

    /// The servers under `mcpServers` in a `.claude.json`, project entry or `.mcp.json`.
    static func mcpServers(in object: [String: Any], scope: SeatMCPServer.Scope, owner: String?) -> [SeatMCPServer] {
        let servers = object["mcpServers"] as? [String: Any] ?? [:]
        return servers.keys.sorted().compactMap { name in
            guard let entry = servers[name] as? [String: Any] else { return nil }
            return server(name, entry, scope: scope, owner: owner)
        }
    }

    static func server(_ name: String, _ entry: [String: Any], scope: SeatMCPServer.Scope, owner: String?) -> SeatMCPServer {
        let url = entry["url"] as? String
        let transport = entry["type"] as? String ?? (url == nil ? "stdio" : "http")
        let target: String
        if let url {
            target = URL(string: url)?.host ?? ConfigMask.maskString(url)
        } else {
            let command = entry["command"] as? String ?? ""
            let args = (ConfigMask.mask(entry["args"] as? [Any] ?? [], at: ["args"]) as? [Any] ?? []).compactMap { $0 as? String }
            target = ([ConfigMask.maskString(command)] + args).joined(separator: " ")
        }
        let values = Array((entry["env"] as? [String: Any] ?? [:]).values) + Array((entry["headers"] as? [String: Any] ?? [:]).values)
        var s = SeatMCPServer(name: name, scope: scope, transport: transport, target: target,
                              envNames: (entry["env"] as? [String: Any] ?? [:]).keys.sorted(),
                              headerNames: (entry["headers"] as? [String: Any] ?? [:]).keys.sorted(),
                              owner: owner)
        s.literalValues = values.contains { ($0 as? String).map { !$0.contains("${") } ?? false }
        return s
    }

    static func managedServers(_ folder: String, _ fm: FileManager) -> [SeatMCPServer] {
        guard let o = readObject((folder as NSString).appendingPathComponent("managed-mcp.json"), fm).object else { return [] }
        return mcpServers(in: o, scope: .managed, owner: nil)
    }

    /// A plugin's servers: its `.mcp.json` and its manifest's `mcpServers` when that is an object.
    static func pluginServers(_ path: String, name: String, _ fm: FileManager) -> [SeatMCPServer] {
        var out: [SeatMCPServer] = []
        if let o = readObject((path as NSString).appendingPathComponent(".mcp.json"), fm).object {
            out += mcpServers(in: o, scope: .plugin, owner: name)
        }
        let manifest = ModStatusEntries.manifest(URL(fileURLWithPath: path))
        if manifest["mcpServers"] is [String: Any] {
            out += mcpServers(in: manifest, scope: .plugin, owner: name)
        }
        return out
    }
}
