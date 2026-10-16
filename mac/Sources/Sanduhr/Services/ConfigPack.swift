import CryptoKit
import Foundation

/// A config pack (item 76, slice C, `docs/config-transfer-spec.md`): what a personal seat holds,
/// described rather than copied. Plain files travel (CLAUDE.md, agents, skills, commands, output
/// styles, kept mods); plugins travel as their marketplace source; MCP servers with `${VAR}` in
/// place of every env and header value; chosen settings keys; env by name only. The home folder
/// is written as `~`, any other machine path keeps its row from travelling, and every file is
/// scanned for secrets before it can be chosen. Reading only: nothing in the seat is written.
enum ConfigPack {
    /// One row of the export sheet.
    struct Item: Identifiable, Equatable {
        enum Kind: String, CaseIterable {
            case instructions, agent, skill, command, outputStyle, keptMod, plugin, mcpServer, setting, env

            var title: String {
                switch self {
                case .instructions: "Instructions"
                case .agent: "Agents"
                case .skill: "Skills"
                case .command: "Commands"
                case .outputStyle: "Output styles"
                case .keptMod: "Kept mods"
                case .plugin: "Plugins"
                case .mcpServer: "MCP servers"
                case .setting: "Settings"
                case .env: "Environment variables"
                }
            }
        }

        let kind: Kind
        let name: String
        /// What it is, in a few words: a file count, a marketplace, a server's transport.
        var detail = ""
        /// The files it brings: path in the pack, and the text written there (home folder as `~`).
        var files: [File] = []
        /// What goes into the manifest for it (plugins, servers, settings, env names), as JSON.
        var json: Data?
        /// Why it can't travel: a path only this Mac has, or a line that looks like a secret.
        var blocked: String?
        /// Checked when the sheet opens.
        var onByDefault = true
        /// The seat's watch words it holds (a work seat's, `WatchWords`): shown, and unchecked.
        var watched: [String] = []

        var id: String { kind.rawValue + "/" + name }
        var canTravel: Bool { blocked == nil }
    }

    struct File: Equatable {
        var path: String
        var text: String
    }

    static let maxFileBytes = ModPackReader.maxFileBytes
    /// Settings keys a pack may carry: the ones Claude Code documents as shareable, and the wall
    /// switches. Plugins travel as rows of their own, so `enabledPlugins` isn't one of them.
    static let settingsKeys = ["permissions", "model", "outputStyle", "hooks", "extraKnownMarketplaces",
                               "syncClaudeAiSkills", "syncClaudeAiPlugins", "disableClaudeAiConnectors",
                               "cleanupPeriodDays"]
    /// Env names that are this Mac's alone and never listed.
    static let machineEnv: Set<String> = ["CLAUDE_CODE_PLUGIN_DIRS", "CLAUDE_CONFIG_DIR"]
    /// Absolute paths every Mac has: a command under these still travels.
    static let systemPrefixes = ["/usr/", "/bin/", "/sbin/", "/System/", "/Library/Apple/", "/opt/homebrew/"]

    // MARK: The scan

    /// The first line (from 1) of `text` that looks like a secret, nil when none does: what the
    /// one mask matches (`ConfigMask.maskString`), or a long high-entropy word.
    static func secretLine(in text: String) -> Int? {
        for (i, line) in text.components(separatedBy: "\n").enumerated() {
            if ConfigMask.maskString(line) != line || line.split(whereSeparator: { !isTokenChar($0) }).contains(where: { looksRandom(String($0)) }) {
                return i + 1
            }
        }
        return nil
    }

    private static func isTokenChar(_ c: Character) -> Bool {
        c.isASCII && (c.isLetter || c.isNumber || "_-+/=".contains(c))
    }

    /// 32 characters or more, letters and digits mixed, and more varied than hex can be.
    static func looksRandom(_ word: String) -> Bool {
        guard word.count >= 32, word.contains(where: \.isNumber), word.contains(where: \.isLetter) else { return false }
        var counts: [Character: Int] = [:]
        for c in word { counts[c, default: 0] += 1 }
        let n = Double(word.count)
        let entropy = counts.values.reduce(0.0) { acc, k in let p = Double(k) / n; return acc - p * log2(p) }
        return entropy > 4.2
    }

    // MARK: Paths

    /// `s` with the home folder written as `~`.
    static func homeRelative(_ s: String, home: String) -> String {
        let h = AccountData.normalized(home)
        return s.replacingOccurrences(of: h + "/", with: "~/").replacingOccurrences(of: h, with: "~")
    }

    /// The first absolute path in `s` outside the home folder that another Mac may not have.
    static func machinePath(in s: String, home: String) -> String? {
        let h = AccountData.normalized(home)
        for word in s.split(whereSeparator: { $0 == " " || $0 == "\"" || $0 == "'" || $0 == "=" || $0 == "(" || $0 == ")" || $0 == ":" }) {
            let w = String(word)
            guard w.hasPrefix("/"), w.count > 1, !w.hasPrefix("//") else { continue }
            if w == h || w.hasPrefix(h + "/") { continue }
            if systemPrefixes.contains(where: { w.hasPrefix($0) }) { continue }
            return w
        }
        return nil
    }

    /// Walks a JSON value: every string goes through `rewrite`; the first reason `check` gives
    /// for any string stops the walk and is returned.
    static func walk(_ value: Any, rewrite: (String) -> String, check: (String) -> String?) -> (Any, String?) {
        switch value {
        case let s as String:
            let r = rewrite(s)
            return (r, check(r))
        case let a as [Any]:
            var out: [Any] = []
            for v in a {
                let (w, why) = walk(v, rewrite: rewrite, check: check)
                if let why { return (out, why) }
                out.append(w)
            }
            return (out, nil)
        case let o as [String: Any]:
            var out: [String: Any] = [:]
            for (k, v) in o {
                let (w, why) = walk(v, rewrite: rewrite, check: check)
                if let why { return (out, why) }
                out[k] = w
            }
            return (out, nil)
        default:
            return (value, nil)
        }
    }

    // MARK: Planning

    /// Every row the export sheet offers for `seat`. Reads the seat's files and Sanduhr's kept
    /// copies; writes nothing.
    static func plan(seat: String, scan: SeatScan, keptOn: [KeptMod], kept: KeptMods,
                     home: String = NSHomeDirectory()) -> [Item] {
        let root = AccountData.normalized(seat)
        var items: [Item] = []

        // Instructions: off by default, it's often the most personal file in a seat.
        let claudeMd = (root as NSString).appendingPathComponent("CLAUDE.md")
        if FileManager.default.fileExists(atPath: claudeMd) {
            var item = fileItem(.instructions, name: "CLAUDE.md", sources: [(claudeMd, "files/CLAUDE.md")], home: home)
            item.onByDefault = false
            items.append(item)
        }

        // The seat's own agents, commands, output styles and skills (not synced, not a project's).
        for (kind, entries) in [(Item.Kind.agent, scan.agents), (.command, scan.commands), (.outputStyle, scan.outputStyles)] {
            for e in entries where e.scope == .seat && e.path.hasPrefix(root + "/") {
                let rel = String(e.path.dropFirst(root.count + 1))
                items.append(fileItem(kind, name: e.name, sources: [(e.path, "files/" + rel)], home: home))
            }
        }
        for e in scan.skills where e.scope == .seat && e.path.hasPrefix(root + "/") {
            let dir = (e.path as NSString).lastPathComponent == "SKILL.md" ? (e.path as NSString).deletingLastPathComponent : e.path
            let sources = folderFiles(dir).map { ($0, "files/" + String($0.dropFirst(root.count + 1))) }
            var item = fileItem(.skill, name: e.name, sources: sources, home: home)
            if sources.isEmpty { item.blocked = "Its folder couldn't be read, or holds a link." }
            items.append(item)
        }

        // Kept mods on in this seat: their files as kept, under mods/<name>/.
        for mod in keptOn {
            let dir = kept.path(mod)
            let rels = (try? KeptMods.files(in: URL(fileURLWithPath: dir))) ?? []
            var item = fileItem(.keptMod, name: mod.name, sources: rels.map { ((dir as NSString).appendingPathComponent($0), "mods/\(mod.name)/\($0)") }, home: home)
            item.detail = (mod.version.map { $0 + ", " } ?? "") + item.detail
            var doc: [String: Any] = ["name": mod.name, "stamp": mod.stamp]
            if let v = mod.version { doc["version"] = v }
            item.json = try? JSONSerialization.data(withJSONObject: doc)
            if rels.isEmpty { item.blocked = "Sanduhr's kept copy couldn't be read." }
            items.append(item)
        }

        // Plugins from marketplaces, by their marketplace's source.
        let known = readObject((root as NSString).appendingPathComponent("plugins/known_marketplaces.json")) ?? [:]
        for p in scan.plugins {
            guard case .installed(let market) = p.origin, !["inline", "synced", "skills-dir"].contains(market) else { continue }
            var item = Item(kind: .plugin, name: p.name, detail: "from \(market)")
            let source = (known[market] as? [String: Any])?["source"] as? [String: Any]
            let kind = source?["source"] as? String
            if source == nil {
                item.blocked = "Sanduhr can't tell where \(market) comes from."
            } else if kind == "directory" || kind == "file" {
                item.blocked = "\(market) is a folder on this Mac."
            }
            item.json = try? JSONSerialization.data(withJSONObject: [
                "name": p.name, "marketplace": ["name": market, "source": source ?? [:]],
                "scope": "user", "enabled": p.state == .on,
            ] as [String: Any])
            item.onByDefault = p.state == .on
            items.append(item)
        }

        // MCP servers from the seat's .claude.json, user scope, values turned into references.
        let config = readObject(ClaudeCodeFolders.configFile(for: root, home: home)) ?? [:]
        for (name, value) in (config["mcpServers"] as? [String: Any] ?? [:]).sorted(by: { $0.key < $1.key }) {
            guard let server = value as? [String: Any] else { continue }
            if name == "sanduhr" || "\(server)".contains("sanduhr_mcp.py") { continue }   // the importing Sanduhr installs its own
            items.append(mcpItem(name: name, server: server, home: home))
        }

        // Settings keys, and env names.
        let settings = readObject((root as NSString).appendingPathComponent("settings.json")) ?? [:]
        for key in settingsKeys {
            guard var value = settings[key] else { continue }
            if key == "hooks" { value = withoutSanduhrHooks(value) }
            if key == "extraKnownMarketplaces", let o = value as? [String: Any] {
                value = o.filter { (($0.value as? [String: Any])?["source"] as? [String: Any])?["source"] as? String != "directory" }
            }
            if let o = value as? [String: Any], o.isEmpty { continue }
            var item = Item(kind: .setting, name: key)
            let (clean, why) = walk(value, rewrite: { homeRelative($0, home: home) }, check: { s in
                if ConfigMask.maskString(s) != s { return "It holds a value that looks like a secret." }
                return machinePath(in: s, home: home).map { "It names \($0), a path on this Mac only." }
            })
            item.blocked = why
            item.detail = summary(clean)
            item.json = try? JSONSerialization.data(withJSONObject: [key: clean])
            items.append(item)
        }
        let names = (settings["env"] as? [String: Any] ?? [:]).keys.filter { !machineEnv.contains($0) }.sorted()
        if !names.isEmpty {
            var item = Item(kind: .env, name: "env", detail: "\(names.count) \(names.count == 1 ? "name" : "names"), values typed in on import: " + names.joined(separator: ", "))
            item.json = try? JSONSerialization.data(withJSONObject: names)
            items.append(item)
        }
        return items
    }

    /// A row made of files: each read as text, the home folder written as `~`, and scanned.
    private static func fileItem(_ kind: Item.Kind, name: String, sources: [(String, String)], home: String) -> Item {
        var item = Item(kind: kind, name: name)
        var bytes = 0
        for (source, packPath) in sources {
            guard let data = FileManager.default.contents(atPath: source), data.count <= maxFileBytes,
                  let text = String(data: data, encoding: .utf8) else {
                item.blocked = "\((packPath as NSString).lastPathComponent) isn't a text file under \(maxFileBytes >> 20) MB."
                break
            }
            // The scan reads what would travel: the home folder already written as ~.
            let travels = homeRelative(text, home: home)
            if let line = secretLine(in: travels) {
                item.blocked = "\(String(packPath.split(separator: "/").dropFirst().joined(separator: "/"))) line \(line) looks like a secret."
                break
            }
            bytes += data.count
            item.files.append(File(path: packPath, text: travels))
        }
        item.detail = "\(sources.count) \(sources.count == 1 ? "file" : "files"), " + ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
        return item
    }

    private static func mcpItem(name: String, server: [String: Any], home: String) -> Item {
        var item = Item(kind: .mcpServer, name: name)
        var out: [String: Any] = [:]
        let transport = server["type"] as? String ?? (server["url"] != nil ? "http" : "stdio")
        out["type"] = transport
        var why: String?
        for key in ["command", "url"] {
            guard let s = server[key] as? String else { continue }
            let r = homeRelative(s, home: home).replacingOccurrences(of: "~/", with: "${HOME}/")
            if ConfigMask.maskString(r) != r { why = "Its \(key) holds a value that looks like a secret." }
            if key == "command", let p = machinePath(in: r, home: home) { why = why ?? "It runs \(p), a path on this Mac only." }
            out[key] = r
        }
        if let args = server["args"] as? [Any] {
            let strings = args.map { homeRelative("\($0)", home: home).replacingOccurrences(of: "~/", with: "${HOME}/") }
            if ConfigMask.mask(strings) as? [String] != strings { why = why ?? "Its arguments hold a value that looks like a secret." }
            if let p = strings.lazy.compactMap({ machinePath(in: $0, home: home) }).first { why = why ?? "It names \(p), a path on this Mac only." }
            out["args"] = strings
        }
        // Every env and header value becomes a reference, filled in on the other side.
        if let env = server["env"] as? [String: Any], !env.isEmpty {
            out["env"] = Dictionary(uniqueKeysWithValues: env.keys.map { ($0, "${\($0)}") })
        }
        if let headers = server["headers"] as? [String: Any], !headers.isEmpty {
            out["headers"] = Dictionary(uniqueKeysWithValues: headers.keys.map { ($0, "${\(envName($0))}") })
        }
        item.blocked = why
        item.detail = transport + ((out["env"] as? [String: Any]).map { ", \($0.count) env" } ?? "") + ((out["headers"] as? [String: Any]).map { ", \($0.count) headers" } ?? "")
        item.json = try? JSONSerialization.data(withJSONObject: ["name": name, "config": out] as [String: Any])
        return item
    }

    /// A header's name as an environment variable's: `X-Api-Key` → `X_API_KEY`.
    static func envName(_ header: String) -> String {
        String(header.uppercased().map { $0.isLetter || $0.isNumber ? $0 : "_" })
    }

    /// Hooks without Sanduhr's own: the importing Sanduhr installs those itself.
    static func withoutSanduhrHooks(_ value: Any) -> Any {
        guard let events = value as? [String: Any] else { return value }
        var out: [String: Any] = [:]
        for (event, groups) in events {
            let kept = (groups as? [Any] ?? []).compactMap { g -> Any? in
                guard var group = g as? [String: Any], let hooks = group["hooks"] as? [Any] else { return g }
                let rest = hooks.filter { !"\($0)".lowercased().contains("sanduhr") }
                if rest.isEmpty { return nil }
                group["hooks"] = rest
                return group
            }
            if !kept.isEmpty { out[event] = kept }
        }
        return out
    }

    private static func summary(_ value: Any) -> String {
        switch value {
        case let o as [String: Any]: "\(o.count) \(o.count == 1 ? "entry" : "entries")"
        case let a as [Any]: "\(a.count) \(a.count == 1 ? "entry" : "entries")"
        case let b as Bool: b ? "on" : "off"
        default: "\(value)"
        }
    }

    /// Every plain file under `dir`, sorted; empty when a link is there (a skill isn't copied
    /// through one) or it can't be read.
    private static func folderFiles(_ dir: String) -> [String] {
        guard let walk = FileManager.default.enumerator(atPath: dir) else { return [] }
        var out: [String] = []
        while let rel = walk.nextObject() as? String {
            guard let type = walk.fileAttributes?[.type] as? FileAttributeType else { continue }
            if type == .typeSymbolicLink { return [] }
            if type == .typeRegular, !ModPackReader.isLitter(rel) { out.append((dir as NSString).appendingPathComponent(rel)) }
            if out.count > ModPackReader.maxEntries { return [] }
        }
        return out.sorted()
    }

    private static func readObject(_ path: String) -> [String: Any]? {
        guard let data = FileManager.default.contents(atPath: path), data.count < 8 << 20 else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }

    // MARK: Writing

    static func fileName(label: String) -> String {
        let safe = label.lowercased().map { $0.isLetter || $0.isNumber ? $0 : "-" }
        return String(safe).split(separator: "-").joined(separator: "-") + "-config." + ModPack.fileExtension
    }

    /// The manifest for the chosen rows. Names no machine, user or seat; `fromWork` stamps it
    /// `"from": "work"` so an import elsewhere can say where it came from.
    static func manifest(_ items: [Item], label: String, version: String, now: Date, fromWork: Bool = false,
                         extra: [File] = []) -> [String: Any] {
        var files: [[String: Any]] = []
        var settings: [String: Any] = [:]
        var plugins: [Any] = []
        var mcp: [Any] = []
        var mods: [Any] = []
        var env: [Any] = []
        for item in items {
            for f in item.files {
                let data = Data(f.text.utf8)
                files.append(["path": f.path, "bytes": data.count, "sha256": SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()])
            }
            let doc = item.json.flatMap { try? JSONSerialization.jsonObject(with: $0) }
            switch item.kind {
            case .setting: (doc as? [String: Any])?.forEach { settings[$0.key] = $0.value }
            case .plugin: if let doc { plugins.append(doc) }
            case .mcpServer: if let doc { mcp.append(doc) }
            case .keptMod: if let doc { mods.append(doc) }
            case .env: env = (doc as? [String] ?? []).map { ["name": $0, "needs_value": true] }
            default: break
            }
        }
        // any-agent/ is listed like every file, so a reader verifies it; import reads only files/.
        for f in extra {
            let data = Data(f.text.utf8)
            files.append(["path": f.path, "bytes": data.count, "sha256": SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()])
        }
        var doc: [String: Any] = ["schema": 1, "kind": "config", "created_at": HandoffFiles.stamp(now), "created_by": "Sanduhr " + version,
                                  "label": label, "files": files, "settings": settings, "env": env, "plugins": plugins, "mcp": mcp,
                                  "kept_mods": mods]
        if fromWork { doc["from"] = "work" }
        return doc
    }

    /// `any-agent/` for the chosen rows (item 76, slice H), or nil when the pack would then pass
    /// a reader's caps (256 entries, 20 MB), counting the folders `ditto` writes.
    static func anyAgentFiles(_ chosen: [Item]) -> [File]? {
        let items = chosen.filter(\.canTravel)
        let extra = AnyAgent.files(items)
        let all = items.flatMap(\.files) + extra
        let bytes = all.reduce(0) { $0 + $1.text.utf8.count }
        guard AnyAgent.entryCount(all.map(\.path) + [ModPack.manifestName]) <= ModPackReader.maxEntries,
              bytes <= ModPackReader.maxBytes - (1 << 20) else { return nil }
        return extra
    }

    /// Writes the chosen rows (blocked ones are never written) as a pack at `destination`. Built
    /// in a temporary folder, zipped with `ditto`, moved into place.
    static func write(_ chosen: [Item], label: String, to destination: URL, version: String = ModPack.appVersion,
                      now: Date = Date(), fromWork: Bool = false, anyAgent: Bool = false) throws {
        let items = chosen.filter(\.canTravel)
        let extra = anyAgent ? anyAgentFiles(items) ?? [] : []
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("sanduhr-config-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: work) }
        let tree = work.appendingPathComponent("pack", isDirectory: true)
        do {
            try fm.createDirectory(at: tree, withIntermediateDirectories: true)
            for f in items.flatMap(\.files) + extra {
                let url = tree.appendingPathComponent(f.path)
                try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(f.text.utf8).write(to: url)
            }
            let doc = manifest(items, label: label, version: version, now: now, fromWork: fromWork, extra: extra)
            var data = try JSONSerialization.data(withJSONObject: doc, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
            data.append(0x0A)
            try data.write(to: tree.appendingPathComponent(ModPack.manifestName))
        } catch {
            throw ModPack.Failure.writeFailed
        }
        let zip = work.appendingPathComponent("pack.zip")
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        p.arguments = ["-c", "-k", "--norsrc", "--noextattr", "--noacl", tree.path, zip.path]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { throw ModPack.Failure.writeFailed }
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw ModPack.Failure.writeFailed }
        do {
            if fm.fileExists(atPath: destination.path) {
                _ = try fm.replaceItemAt(destination, withItemAt: zip)
            } else {
                try fm.moveItem(at: zip, to: destination)
            }
        } catch {
            throw ModPack.Failure.writeFailed
        }
    }
}
