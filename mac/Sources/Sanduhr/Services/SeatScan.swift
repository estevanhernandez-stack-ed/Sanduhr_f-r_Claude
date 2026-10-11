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
    var plugins: [ModItem] = []
    var marketplaces: [SeatMarketplace] = []
    /// Plugin keys (`name@market`) turned on in a project's `.claude/settings.json` or
    /// `settings.local.json`, or installed at project scope, with the projects that do.
    var projectPlugins: [String: Set<String>] = [:]
    var agents: [SeatEntry] = []
    var skills: [SeatEntry] = []
    var commands: [SeatEntry] = []
    var outputStyles: [SeatEntry] = []
    var mcpServers: [SeatMCPServer] = []
    /// The project folders read.
    var projects: [String] = []
}

enum SeatScanner {
    /// The most entries read per folder kind, so a huge folder stays quick.
    static let entryLimit = 500

    static func scan(seat: String, home: String = NSHomeDirectory(), fileManager fm: FileManager = .default,
                     managedFolder: String = (ModOverrides.managedFile as NSString).deletingLastPathComponent) -> SeatScan {
        let root = AccountData.normalized(seat)
        var out = SeatScan(seat: root)
        let settingsFile = (root as NSString).appendingPathComponent("settings.json")
        let settings: [String: Any]
        switch readObject(settingsFile, fm) {
        case .object(let o): settings = o
        case .unreadable(let why):
            settings = [:]
            out.settingsUnreadable = why
        case .missing: settings = [:]
        }
        out.plugins = ModInventory.scan(folder: root, home: home)
        out.plugins += syncedFolders(root, settings: settings, known: Set(out.plugins.compactMap(\.key)), fm)
        out.marketplaces = marketplaces(root, settings: settings, fm)

        let config = readObject(ClaudeCodeFolders.configFile(for: root, home: home, fileManager: fm), fm).object ?? [:]
        let projectTable = config["projects"] as? [String: Any] ?? [:]
        out.projects = projectTable.keys.sorted().prefix(ModOverrides.projectLimit).map { $0 }

        out.agents = entries(in: (root as NSString).appendingPathComponent("agents"), kind: .agent, scope: .seat, fm)
        out.commands = entries(in: (root as NSString).appendingPathComponent("commands"), kind: .command, scope: .seat, fm)
        out.outputStyles = entries(in: (root as NSString).appendingPathComponent("output-styles"), kind: .outputStyle, scope: .seat, fm)
        let skillsDir = (root as NSString).appendingPathComponent("skills")
        out.skills = entries(in: skillsDir, kind: .skill, scope: .seat, fm)
            + entries(in: (skillsDir as NSString).appendingPathComponent("synced"), kind: .skill, scope: .synced, fm)

        out.mcpServers = mcpServers(in: config, scope: .user, owner: nil)
        out.mcpServers += managedServers(managedFolder, fm)
        for item in out.plugins where item.state != .missing {
            guard let path = item.path else { continue }
            out.mcpServers += pluginServers(path, name: item.name, fm)
        }

        // Each project: its own extensions, its MCP servers and the plugins it turns on.
        for project in out.projects {
            let dot = (project as NSString).appendingPathComponent(".claude")
            func tag(_ list: [SeatEntry]) -> [SeatEntry] {
                list.map { var e = $0; e.project = project; return e }
            }
            out.agents += tag(entries(in: (dot as NSString).appendingPathComponent("agents"), kind: .agent, scope: .project, fm))
            out.commands += tag(entries(in: (dot as NSString).appendingPathComponent("commands"), kind: .command, scope: .project, fm))
            out.outputStyles += tag(entries(in: (dot as NSString).appendingPathComponent("output-styles"), kind: .outputStyle, scope: .project, fm))
            out.skills += tag(entries(in: (dot as NSString).appendingPathComponent("skills"), kind: .skill, scope: .project, fm))
            if let entry = projectTable[project] as? [String: Any] {
                out.mcpServers += mcpServers(in: entry, scope: .local, owner: project)
            }
            if let mcp = readObject((project as NSString).appendingPathComponent(".mcp.json"), fm).object {
                out.mcpServers += mcpServers(in: mcp, scope: .project, owner: project)
            }
            for file in ["settings.json", "settings.local.json"] {
                let enabled = readObject((dot as NSString).appendingPathComponent(file), fm).object?["enabledPlugins"] as? [String: Any] ?? [:]
                for (key, value) in enabled where IntegrationInstaller.boolValue(value) == true {
                    out.projectPlugins[key, default: []].insert(project)
                }
            }
        }
        for (key, project) in projectScopedRecords(root, fm) {
            out.projectPlugins[key, default: []].insert(project)
        }
        return out
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

    /// A JSON object file: missing, unreadable (with where it stops parsing, never the text) or
    /// read. A file whose top level isn't an object is unreadable too.
    static func readObject(_ path: String, _ fm: FileManager = .default) -> Read {
        guard let data = fm.contents(atPath: path) else { return .missing }
        do {
            let value = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
            guard let o = value as? [String: Any] else { return .unreadable("has no JSON object at its top level") }
            return .object(o)
        } catch {
            return .unreadable(parseError(error))
        }
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
    /// file name). Commands: `*.md`, a subfolder's as `folder:name`. Skills: each subfolder with
    /// a `SKILL.md` (never a plugin folder, which ModInventory lists, nor `synced/` itself).
    static func entries(in dir: String, kind: EntryKind, scope: SeatEntry.Scope, _ fm: FileManager) -> [SeatEntry] {
        var out: [SeatEntry] = []
        if kind == .skill {
            for name in visible(dir, fm) where name != "synced" || scope == .synced {
                let folder = (dir as NSString).appendingPathComponent(name)
                guard !fm.fileExists(atPath: (folder as NSString).appendingPathComponent(".claude-plugin/plugin.json")) else { continue }
                let file = (folder as NSString).appendingPathComponent("SKILL.md")
                guard fm.fileExists(atPath: file) else { continue }
                let front = frontmatter(file, fm)
                out.append(SeatEntry(name: nonEmpty(front["name"]) ?? name, description: front["description"] ?? "",
                                     scope: scope, path: file))
                if out.count >= entryLimit { break }
            }
            return out
        }
        guard let e = fm.enumerator(atPath: dir) else { return [] }
        while let rel = e.nextObject() as? String {
            let parts = rel.split(separator: "/").map(String.init)
            if parts.contains(where: { $0.hasPrefix(".") }) { continue }
            guard rel.lowercased().hasSuffix(".md") else { continue }
            let file = (dir as NSString).appendingPathComponent(rel)
            let front = frontmatter(file, fm)
            let stem = String(rel.dropLast(3))
            let fallback = kind == .command ? stem.replacingOccurrences(of: "/", with: ":") : (stem as NSString).lastPathComponent
            let name = kind == .command ? fallback : (nonEmpty(front["name"]) ?? fallback)
            out.append(SeatEntry(name: name, description: front["description"] ?? "", scope: scope, path: file))
            if out.count >= entryLimit { break }
        }
        return out.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
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
        guard let h = FileHandle(forReadingAtPath: path) else { return [:] }
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

    /// Install records at project or local scope (`installed_plugins.json` version 2): the key
    /// and the project it was installed for.
    static func projectScopedRecords(_ root: String, _ fm: FileManager) -> [(String, String)] {
        let file = (root as NSString).appendingPathComponent("plugins/installed_plugins.json")
        guard let plugins = readObject(file, fm).object?["plugins"] as? [String: Any] else { return [] }
        var out: [(String, String)] = []
        for (key, value) in plugins {
            for record in value as? [Any] ?? [] {
                guard let r = record as? [String: Any], let scope = r["scope"] as? String,
                      scope == "project" || scope == "local", let project = r["projectPath"] as? String else { continue }
                out.append((key, AccountData.normalized(project)))
            }
        }
        return out
    }

    /// The marketplaces the seat added (`plugins/known_marketplaces.json`) and the ones its
    /// settings declare (`extraKnownMarketplaces`), each with its cached `marketplace.json`.
    static func marketplaces(_ root: String, settings: [String: Any], _ fm: FileManager) -> [SeatMarketplace] {
        let known = readObject((root as NSString).appendingPathComponent("plugins/known_marketplaces.json"), fm).object ?? [:]
        let declared = settings["extraKnownMarketplaces"] as? [String: Any] ?? [:]
        var out: [SeatMarketplace] = []
        for name in Set(known.keys).union(declared.keys).sorted() {
            let entry = known[name] as? [String: Any] ?? declared[name] as? [String: Any] ?? [:]
            let location = (entry["installLocation"] as? String)
                ?? (root as NSString).appendingPathComponent("plugins/marketplaces/\(name)")
            var isDir: ObjCBool = false
            let present = fm.fileExists(atPath: location, isDirectory: &isDir) && isDir.boolValue
            var m = SeatMarketplace(name: name, source: sourceTitle(entry["source"]), present: present, cached: false)
            let manifest = readObject((location as NSString).appendingPathComponent(".claude-plugin/marketplace.json"), fm).object
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
        return SeatMCPServer(name: name, scope: scope, transport: transport, target: target,
                             envNames: (entry["env"] as? [String: Any] ?? [:]).keys.sorted(),
                             headerNames: (entry["headers"] as? [String: Any] ?? [:]).keys.sorted(),
                             owner: owner)
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
