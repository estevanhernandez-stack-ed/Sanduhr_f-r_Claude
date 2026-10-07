import Foundation

/// What a mod draws in Claude Code, found by reading its source (item 64).
enum ModSurface: String, CaseIterable, Sendable {
    case band, pane, status, toast, commands

    var title: String {
        switch self {
        case .band: "band above the prompt"
        case .pane: "pane"
        case .status: "status entry"
        case .toast: "toasts"
        case .commands: "slash commands"
        }
    }
}

/// What a mod's code can reach beyond drawing, found by reading its source (item 64).
enum ModCapability: String, CaseIterable, Sendable {
    case process, http, files

    var title: String {
        switch self {
        case .process: "runs programs"
        case .http: "uses the network"
        case .files: "reads or writes files"
        }
    }
}

/// What the static scan found a plugin draws and uses, in a fixed order.
struct ModTouches: Equatable, Sendable {
    var surfaces: [ModSurface] = []
    var capabilities: [ModCapability] = []
    /// The slash commands it serves, without the slash, sorted.
    var commands: [String] = []

    var isEmpty: Bool { surfaces.isEmpty && capabilities.isEmpty }
}

/// One mod or plugin a Claude Code folder loads, as the Mods page lists it (item 64, slice 1).
struct ModItem: Identifiable, Equatable, Sendable {
    enum Origin: Equatable, Sendable {
        /// Listed in the folder's `env.CLAUDE_CODE_PLUGIN_DIRS` (an `@inline` plugin).
        case pluginDirs
        /// In `plugins/installed_plugins.json` or `enabledPlugins` under `<name>@<marketplace>`.
        case installed(marketplace: String)
        /// A plugin folder under the Claude Code folder's `skills/` (`<name>@skills-dir`).
        case skillsDir
        /// Made in one session, under `dev-mods/<session>/`.
        case devMods(session: String)
    }

    enum State: String, Sendable {
        /// Loads in new sessions.
        case on
        /// Turned off in `enabledPlugins` (or never turned on).
        case off
        /// A dev mod: loads only in the session that made it.
        case session
        /// The folder it names isn't there.
        case missing

        var title: String {
            switch self {
            case .on: "On"
            case .off: "Off"
            case .session: "Its session only"
            case .missing: "Missing"
            }
        }
    }

    /// The Claude Code folder that loads it.
    let folder: String
    /// The manifest's name, else the plugin key's or the folder's.
    let name: String
    var version: String?
    var description = ""
    /// Its folder on disk; nil when Sanduhr has no path for it (an enabled key without a record).
    var path: String?
    let origin: Origin
    /// Its `enabledPlugins` key (`name@inline`, `name@skills-dir`, `name@market`), nil for dev mods.
    var key: String?
    /// It has a hooks module (`hooks/hooks.json` lists `modules`); a plain plugin otherwise.
    var isMod = false
    var state: State = .on
    var touches = ModTouches()

    var id: String { folder + "\n" + (path ?? key ?? name) }

    /// Where it loads from, in words.
    var originTitle: String {
        switch origin {
        case .pluginDirs: "Plugin folder list (CLAUDE_CODE_PLUGIN_DIRS)"
        case .installed(let market): market == "synced" ? "Synced from claude.ai" : "Installed from \(market)"
        case .skillsDir: "Skills folder"
        case .devMods(let session): "Made in session \(session.prefix(8))"
        }
    }
}

/// The Mods page's counts, for its summary card and state.yaml's `mods_page`.
struct ModCounts: Equatable, Sendable {
    var folders = 0
    var mods = 0
    var plugins = 0
    var on = 0
    var missing = 0

    init(folders: Int = 0, mods: Int = 0, plugins: Int = 0, on: Int = 0, missing: Int = 0) {
        self.folders = folders
        self.mods = mods
        self.plugins = plugins
        self.on = on
        self.missing = missing
    }

    init(_ inventory: [ModFolderInventory]) {
        let items = inventory.flatMap(\.items)
        folders = inventory.count
        mods = items.filter(\.isMod).count
        plugins = items.count - mods
        on = items.filter { $0.state == .on || $0.state == .session }.count
        missing = items.filter { $0.state == .missing }.count
    }
}

/// One Claude Code folder and what it loads.
struct ModFolderInventory: Identifiable, Equatable, Sendable {
    let folder: String
    var items: [ModItem]
    var id: String { folder }
}

/// The mods and plugins a Claude Code folder loads (item 64, slice 1), found by reading files
/// only: its settings.json (`env.CLAUDE_CODE_PLUGIN_DIRS`, `enabledPlugins`), its
/// `plugins/installed_plugins.json`, its `skills/` and `dev-mods/` folders, and each plugin's
/// manifest, hooks.json and source as text. No mod code runs, the `claude` CLI isn't called and
/// nothing is written. Extends ModStatusEntries' scan.
enum ModInventory {
    static func scan(folder: String, home: String = NSHomeDirectory()) -> [ModItem] {
        let root = URL(fileURLWithPath: AccountData.normalized(folder))
        let settings = readJSON(root.appendingPathComponent("settings.json")) ?? [:]
        let enabled = settings["enabledPlugins"] as? [String: Any] ?? [:]
        var out: [ModItem] = []
        func add(_ item: ModItem) {
            if let p = item.path, out.contains(where: { $0.path == p }) { return }
            out.append(item)
        }
        for path in ModStatusEntries.pluginDirs(settings, home: home) {
            add(item(root.path, path: path, origin: .pluginDirs, keySuffix: "inline", enabled: enabled))
        }
        let records = ModStatusEntries.installedRecords(root)
        let keys = Set(records.keys).union(enabled.keys.filter(isMarketplaceKey))
        for key in keys.sorted() {
            let market = String(key[key.index(after: key.lastIndex(of: "@") ?? key.startIndex)...])
            let on = (enabled[key] as? Bool) == true
            guard let record = records[key] else {
                // Turned on or off here, but no files for it in this folder (synced or gone).
                let name = String(key.prefix(upTo: key.lastIndex(of: "@") ?? key.endIndex))
                add(ModItem(folder: root.path, name: name, origin: .installed(marketplace: market), key: key,
                            state: on ? .on : .off))
                continue
            }
            var found = item(root.path, path: record.path, origin: .installed(marketplace: market), keySuffix: nil,
                             enabled: enabled, fallbackName: String(key.prefix(upTo: key.lastIndex(of: "@") ?? key.endIndex)))
            found.key = key
            if found.version == nil { found.version = record.version }
            if found.state != .missing { found.state = on ? .on : .off }
            add(found)
        }
        for dir in subfolders(root.appendingPathComponent("skills"))
        where FileManager.default.fileExists(atPath: dir.appendingPathComponent(".claude-plugin/plugin.json").path) {
            add(item(root.path, path: dir.path, origin: .skillsDir, keySuffix: "skills-dir", enabled: enabled))
        }
        for session in subfolders(root.appendingPathComponent("dev-mods")) {
            for dir in pluginFolders(in: session) {
                var found = item(root.path, path: dir.path, origin: .devMods(session: session.lastPathComponent),
                                 keySuffix: nil, enabled: enabled)
                found.state = .session
                add(found)
            }
        }
        return out
    }

    /// Every folder's inventory, in the order given; folders with nothing are kept (the page
    /// says so).
    static func scan(folders: [String], home: String = NSHomeDirectory()) -> [ModFolderInventory] {
        folders.map { ModFolderInventory(folder: AccountData.normalized($0), items: scan(folder: $0, home: home)) }
    }

    /// A marketplace plugin's key: `name@market`, not the `@inline`/`@skills-dir` switches.
    static func isMarketplaceKey(_ key: String) -> Bool {
        guard let at = key.lastIndex(of: "@"), at != key.startIndex else { return false }
        let market = key[key.index(after: at)...]
        return !market.isEmpty && market != "inline" && market != "skills-dir"
    }

    /// The item for the plugin folder at `path`, read from disk.
    private static func item(_ folder: String, path: String, origin: ModItem.Origin, keySuffix: String?,
                             enabled: [String: Any], fallbackName: String? = nil) -> ModItem {
        let dir = URL(fileURLWithPath: path)
        var isDir: ObjCBool = false
        let exists = FileManager.default.fileExists(atPath: dir.path, isDirectory: &isDir) && isDir.boolValue
        let manifest = exists ? ModStatusEntries.manifest(dir) : [:]
        let name = (manifest["name"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? fallbackName ?? dir.lastPathComponent
        let key = keySuffix.map { "\(name)@\($0)" }
        var item = ModItem(folder: folder, name: name, path: dir.path, origin: origin, key: key)
        guard exists else {
            item.state = .missing
            return item
        }
        item.version = manifest["version"] as? String
        item.description = manifest["description"] as? String ?? ""
        item.isMod = ModStatusEntries.isMod(dir)
        item.touches = touches(dir, isMod: item.isMod)
        if let key, (enabled[key] as? Bool) == false { item.state = .off }
        return item
    }

    /// The visible subfolders of `dir`, by name.
    static func subfolders(_ dir: URL) -> [URL] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []
        return names.filter { !$0.hasPrefix(".") }.sorted().compactMap { name in
            let url = dir.appendingPathComponent(name)
            var isDir: ObjCBool = false
            return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue ? url : nil
        }
    }

    /// A session's dev mods: the session folder itself when it is a plugin, else each subfolder
    /// that is one (a manifest or a hooks.json).
    static func pluginFolders(in session: URL) -> [URL] {
        func isPlugin(_ d: URL) -> Bool {
            let fm = FileManager.default
            return fm.fileExists(atPath: d.appendingPathComponent(".claude-plugin/plugin.json").path)
                || fm.fileExists(atPath: d.appendingPathComponent("hooks/hooks.json").path)
        }
        if isPlugin(session) { return [session] }
        return subfolders(session).filter(isPlugin)
    }

    // MARK: The static scan

    /// What the plugin at `dir` draws and uses. A mod's source is read as text; any plugin's
    /// `commands/*.md` are slash commands, its settings-style hooks (`hooks.json`'s `hooks`) run
    /// programs or call URLs, and an MCP server (`.mcp.json`, manifest `mcpServers`) is a program.
    static func touches(_ dir: URL, isMod: Bool) -> ModTouches {
        var surfaces = Set<ModSurface>()
        var capabilities = Set<ModCapability>()
        var commands = Set(commandFiles(dir))
        if isMod {
            ModStatusEntries.forEachSource(dir) { _, text in
                surfaces.formUnion(self.surfaces(in: text))
                capabilities.formUnion(self.capabilities(in: text))
                commands.formUnion(commandNames(in: text))
                return false
            }
        }
        if let hooks = FileManager.default.contents(atPath: dir.appendingPathComponent("hooks/hooks.json").path) {
            let text = String(decoding: hooks, as: UTF8.self)
            if matches(#""type"\s*:\s*"command""#, text) { capabilities.insert(.process) }
            if matches(#""type"\s*:\s*"http""#, text) { capabilities.insert(.http) }
        }
        if FileManager.default.fileExists(atPath: dir.appendingPathComponent(".mcp.json").path)
            || ModStatusEntries.manifest(dir)["mcpServers"] != nil {
            capabilities.insert(.process)
        }
        if !commands.isEmpty { surfaces.insert(.commands) }
        return ModTouches(surfaces: ModSurface.allCases.filter(surfaces.contains),
                          capabilities: ModCapability.allCases.filter(capabilities.contains),
                          commands: commands.sorted())
    }

    /// The surfaces a mod's source text draws.
    static func surfaces(in text: String) -> Set<ModSurface> {
        var out = Set<ModSurface>()
        if text.contains("AbovePrompt") { out.insert(.band) }
        if matches(#"component\s*:\s*['"`]Pane['"`]"#, text) || text.contains("ui.open") { out.insert(.pane) }
        if text.contains("ui.status") { out.insert(.status) }
        if text.contains("ui.toast") { out.insert(.toast) }
        if !commandNames(in: text).isEmpty { out.insert(.commands) }
        return out
    }

    /// The capabilities a mod's source text uses through `$`.
    static func capabilities(in text: String) -> Set<ModCapability> {
        var out = Set<ModCapability>()
        if matches(#"\bprocess\.(run|spawn)\b"#, text) { out.insert(.process) }
        if matches(#"\bhttp\.fetch\b"#, text) { out.insert(.http) }
        if matches(#"\bfs\.(read|write|list|stat|exists|remove|rename|mkdir|watch)\b"#, text) { out.insert(.files) }
        return out
    }

    /// The slash commands a mod's source serves: `on('command.run', { command: 'x' }, …)` and
    /// `$.command.register({ name: 'x', … })`.
    static func commandNames(in text: String) -> Set<String> {
        var out = Set<String>()
        for pattern in [#"command\.run['"`]\s*,\s*\{[^}]*?command\s*:\s*['"`]([^'"`]+)['"`]"#,
                        #"command\.register\(\s*\{[^}]*?name\s*:\s*['"`]([^'"`]+)['"`]"#] {
            guard let re = try? NSRegularExpression(pattern: pattern) else { continue }
            let ns = text as NSString
            for m in re.matches(in: text, range: NSRange(location: 0, length: ns.length)) where m.numberOfRanges > 1 {
                out.insert(ns.substring(with: m.range(at: 1)))
            }
        }
        return out
    }

    /// A plugin's `commands/*.md`, by name.
    static func commandFiles(_ dir: URL) -> [String] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: dir.appendingPathComponent("commands").path)) ?? []
        return names.filter { $0.lowercased().hasSuffix(".md") }.map { String($0.dropLast(3)) }.sorted()
    }

    static func matches(_ pattern: String, _ text: String) -> Bool {
        text.range(of: pattern, options: .regularExpression) != nil
    }

    private static func readJSON(_ url: URL) -> [String: Any]? {
        guard let data = FileManager.default.contents(atPath: url.path) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }
}
