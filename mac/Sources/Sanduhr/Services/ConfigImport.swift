import CryptoKit
import Foundation

/// Opening a config pack into a seat (item 76, slice D, `docs/config-transfer-spec.md`). Each
/// row of the pack is compared with the seat: what the seat lacks can be brought over, what it
/// has the same is shown, what it has differently stays the seat's own (this slice never
/// replaces anything, so no receipt holds an old value). Files, settings keys and env values the
/// user types are written by receipts, grouped as one import with one Undo Import. Kept mods go
/// to New Mod as proposals. Plugins and MCP servers go in through Claude Code's CLI (slice E)
/// when `claude` is found and no session runs in the seat; otherwise they are shown as the
/// commands to run.
enum ConfigImport {
    /// Whether plugin and MCP rows can be run here, or only shown.
    enum CLIState: Equatable, Sendable {
        case ready
        /// No `claude` found: the commands are shown to copy.
        case missing
        /// Claude Code runs in the seat: nothing is installed under a live session.
        case running
    }

    struct Row: Identifiable {
        enum Status: Equatable {
            /// The seat lacks it: it can be brought over.
            case new
            /// The seat has the same.
            case same
            /// The seat has its own, different: kept.
            case differs
            /// An env name the seat lacks: brought over once a value is typed.
            case needsValue
            /// A kept mod: reviewed in New Mod before anything runs.
            case review
            /// A plugin or MCP server: the command to run, shown for copying.
            case command(String)
            /// A plugin or MCP server Sanduhr runs `claude` for once chosen: the command, shown.
            case install(String)
            /// It can't be brought over, and why.
            case refused(String)
        }

        let kind: ConfigPack.Item.Kind
        let name: String
        var detail = ""
        var status: Status
        /// Files to write, by path inside the seat.
        var files: [(path: String, data: Data)] = []
        /// Of `files`, the ones written runnable (a carried plugin's scripts).
        var executable: Set<String> = []
        /// A carried plugin written without `claude` to hand: the command that installs it.
        var command: String?
        /// A settings key's value as it will be written (home folder put back for `~/`).
        var value: Any?
        /// What `claude` runs for a plugin or MCP server row.
        var job: Job?

        var id: String { kind.rawValue + "/" + name }
        var choosable: Bool {
            switch status {
            case .new, .needsValue, .review, .install: true
            default: false
            }
        }
    }

    /// A plugin or MCP server to put in through the CLI. Only names and the JSON that travels.
    enum Job: Equatable, Sendable {
        /// `claude plugin install <id>`, after `marketplace add <source>` when the seat lacks `marketplace`.
        case plugin(id: String, marketplace: String, source: String)
        /// `claude mcp add-json -s user <name> <json>`; `sha256` is the config's canonical hash.
        case mcp(name: String, json: String, sha256: String)
    }

    // MARK: The carry list (slice I)

    /// What the pack itself said it left behind, minus env names that are rows here.
    static func packCarry(_ pack: ModPackReader.Unpacked, rows: [Row]) -> [ConfigPack.Carry] {
        let envRows = Set(rows.filter { $0.kind == .env }.map(\.name))
        return (pack.manifest["carry"] as? [[String: Any]] ?? []).compactMap { e in
            guard let kind = e["kind"] as? String, let name = e["name"] as? String, let why = e["why"] as? String else { return nil }
            if kind == "Values", envRows.contains(name), (e["why"] as? String)?.hasPrefix("An environment variable") == true { return nil }
            return ConfigPack.Carry(kind: kind, name: name, why: why)
        }
    }

    /// After an import: the rows it didn't bring, each with what's left to do by hand.
    static func leftOver(_ rows: [Row], chosen: Set<String>, env values: [String: String]) -> [ConfigPack.Carry] {
        rows.compactMap { r in
            let why: String
            switch r.status {
            case .refused(let reason): why = reason
            case .command(let cmd): why = "Run it yourself: " + cmd
            case .needsValue where chosen.contains(r.id) && (values[r.name] ?? r.value as? String ?? "").isEmpty:
                why = "No value was typed: set it in settings.json by hand."
            case .new where chosen.contains(r.id) && r.command != nil:
                why = "Its files are written; install it yourself: " + (r.command ?? "")
            case .new, .needsValue, .review, .install:
                guard !chosen.contains(r.id) else { return nil }
                why = "Not brought over."
            case .same, .differs:
                return nil
            }
            return ConfigPack.Carry(kind: r.kind.title, name: r.name, why: why)
        }
    }

    // MARK: Planning

    /// The rows of `pack` against `seat`. Reads the seat; writes nothing.
    static func plan(_ pack: ModPackReader.Unpacked, seat: String, home: String = NSHomeDirectory(), cli: CLIState = .missing) -> [Row] {
        let root = AccountData.normalized(seat)
        let fm = FileManager.default
        var rows: [Row] = []

        // Files under files/: one row per CLAUDE.md, agent, command, output style; one per skill folder.
        var groups: [String: (kind: ConfigPack.Item.Kind, name: String, files: [(String, Data)])] = [:]
        for (path, data) in pack.files where path.hasPrefix("files/") {
            let rel = String(path.dropFirst("files/".count))
            let parts = rel.split(separator: "/").map(String.init)
            let key: String
            let kind: ConfigPack.Item.Kind
            let name: String
            switch parts.first {
            case "CLAUDE.md" where parts.count == 1: (key, kind, name) = ("CLAUDE.md", .instructions, "CLAUDE.md")
            case "agents": (key, kind, name) = (rel, .agent, stem(parts.last ?? rel))
            case "commands": (key, kind, name) = (rel, .command, stem(parts.last ?? rel))
            case "output-styles": (key, kind, name) = (rel, .outputStyle, stem(parts.last ?? rel))
            case "skills" where parts.count >= 3: (key, kind, name) = ("skills/" + parts[1], .skill, parts[1])
            default: continue   // nothing else under files/ is a seat's: left out
            }
            groups[key, default: (kind, name, [])].files.append((rel, data))
        }
        for (_, g) in groups.sorted(by: { $0.key < $1.key }) {
            var row = Row(kind: g.kind, name: g.name, status: .new)
            row.files = g.files.sorted { $0.0 < $1.0 }.map { (path: $0.0, data: $0.1) }
            let existing = row.files.map { fm.contents(atPath: (root as NSString).appendingPathComponent($0.path)) }
            let folderThere = g.kind == .skill && fm.fileExists(atPath: (root as NSString).appendingPathComponent("skills/" + g.name))
            if zip(existing, row.files).allSatisfy({ $0.0 == $0.1.data }) {
                row.status = .same
            } else if folderThere || existing.contains(where: { $0 != nil }) {
                row.status = .differs
            }
            let bytes = row.files.reduce(0) { $0 + $1.data.count }
            row.detail = "\(row.files.count) \(row.files.count == 1 ? "file" : "files"), " + ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
            rows.append(row)
        }

        // Settings keys: only ones the pack may carry, the home folder put back.
        let settings = readObject((root as NSString).appendingPathComponent("settings.json"))
        for (key, packed) in (pack.manifest["settings"] as? [String: Any] ?? [:]).sorted(by: { $0.key < $1.key }) {
            guard ConfigPack.settingsKeys.contains(key) else { continue }
            let local = ConfigPack.walk(packed, rewrite: { atHome($0, home: home) }, check: { _ in nil }).0
            var row = Row(kind: .setting, name: key, status: .new)
            row.value = local
            if settings == nil, fm.fileExists(atPath: (root as NSString).appendingPathComponent("settings.json")) {
                row.status = .refused("This folder's settings.json isn't JSON Sanduhr can edit.")
            } else if let mine = settings?[key] {
                row.status = (mine as AnyObject).isEqual(local) ? .same : .differs
            } else if Self.value(local) == nil {
                row.status = .refused("Its value holds a number Sanduhr doesn't write.")
            }
            row.detail = summary(local)
            rows.append(row)
        }

        // Env names: the seat's own are kept; a missing one waits for a value, filled in already for a
        // switch whose value travelled (checked again here: a pack is only data).
        let env = settings?["env"] as? [String: Any] ?? [:]
        for e in pack.manifest["env"] as? [[String: Any]] ?? [] {
            guard let name = e["name"] as? String, isEnvName(name), !ConfigPack.machineEnv.contains(name) else { continue }
            let travelled = ConfigPack.switchValue(name, e["value"])
            var row = Row(kind: .env, name: name, detail: env[name] != nil ? "set in this folder already" : travelled != nil ? "its value came with the pack" : "type its value",
                          status: env[name] != nil ? .same : .needsValue)
            row.value = travelled
            rows.append(row)
        }

        // Kept mods: reviewed as proposals.
        for m in pack.manifest["kept_mods"] as? [[String: Any]] ?? [] {
            guard let name = m["name"] as? String, KeptMods.isName(name) else { continue }
            let n = pack.files.keys.filter { $0.hasPrefix("mods/\(name)/") }.count
            rows.append(Row(kind: .keptMod, name: name, detail: (m["version"] as? String).map { "\($0), " } ?? "" + "\(n) files, reviewed in New Mod", status: .review))
        }

        // Plugins and MCP servers: run through `claude` when it can be, else the command to copy.
        let prefix = configPrefix(root, home: home)
        let installed = Set(((readObject((root as NSString).appendingPathComponent("plugins/installed_plugins.json"))?["plugins"]) as? [String: Any] ?? [:]).keys)
        let servers = readObject(ClaudeCodeFolders.configFile(for: root, home: home))?["mcpServers"] as? [String: Any] ?? [:]
        func status(_ cmd: String) -> Row.Status {
            switch cli {
            case .ready: .install(cmd)
            case .missing: .command(cmd)
            case .running: .refused("Claude Code is running in this folder. Quit it, then open the pack again to install this.")
            }
        }
        for p in pack.manifest["plugins"] as? [[String: Any]] ?? [] where p["carried"] as? Bool == true {
            guard let name = p["name"] as? String, ConfigPack.isPluginName(name) else { continue }
            rows.append(carriedRow(pack, name: name, executable: p["executable"] as? [String] ?? [], root: root, prefix: prefix,
                                   installed: installed, cli: cli))
        }
        for p in pack.manifest["plugins"] as? [[String: Any]] ?? [] where p["carried"] as? Bool != true {
            guard let name = p["name"] as? String, let market = p["marketplace"] as? [String: Any],
                  let mname = market["name"] as? String, let source = market["source"] as? [String: Any] else { continue }
            guard let arg = marketplaceArgument(source) else {
                rows.append(Row(kind: .plugin, name: name, status: .refused("Its marketplace's source isn't one Sanduhr can name.")))
                continue
            }
            let id = name + "@" + mname
            let cmd = "\(prefix)claude plugin marketplace add \(shellQuoted(arg)) && \(prefix)claude plugin install \(shellQuoted(id)) --scope user"
            var row = Row(kind: .plugin, name: name, detail: "from \(mname)", status: installed.contains(id) ? .same : status(cmd))
            row.job = .plugin(id: id, marketplace: mname, source: arg)
            rows.append(row)
        }
        for s in pack.manifest["mcp"] as? [[String: Any]] ?? [] {
            guard let name = s["name"] as? String, let config = s["config"] as? [String: Any],
                  let json = try? JSONSerialization.data(withJSONObject: config, options: [.sortedKeys, .withoutEscapingSlashes]),
                  let text = String(data: json, encoding: .utf8) else { continue }
            let refs = Set((config["env"] as? [String: String] ?? [:]).keys).union((config["headers"] as? [String: String] ?? [:]).values.map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "${}")) })
            let cmd = "\(prefix)claude mcp add-json -s user \(shellQuoted(name)) \(shellQuoted(text))"
            let st: Row.Status = servers[name].map { canonicalSHA($0) == canonicalSHA(config) ? .same : .differs } ?? status(cmd)
            var row = Row(kind: .mcpServer, name: name, detail: refs.isEmpty ? (config["type"] as? String ?? "") : "needs " + refs.sorted().joined(separator: ", ") + " set where Claude Code starts", status: st)
            row.job = .mcp(name: name, json: text, sha256: canonicalSHA(config))
            rows.append(row)
        }
        return rows
    }

    /// Where a carried plugin is written in the seat: a one-plugin marketplace of its own.
    static let carriedFolder = "carried-plugins"

    /// A plugin carried as files (slice J): written under `carried-plugins/<name>/` as a one-plugin
    /// marketplace (`carried-<name>`, source `./plugin`), then added and installed through `claude`.
    /// The row names every hook command the plugin runs.
    private static func carriedRow(_ pack: ModPackReader.Unpacked, name: String, executable: [String], root: String, prefix: String,
                                   installed: Set<String>, cli: CLIState) -> Row {
        let market = "carried-" + name
        let id = name + "@" + market
        let base = "\(carriedFolder)/\(name)"
        let from = "plugins/\(name)/"
        var files = pack.files.filter { $0.key.hasPrefix(from) }.sorted { $0.key < $1.key }
            .map { (path: base + "/plugin/" + String($0.key.dropFirst(from.count)), data: $0.value) }
        let manifest: [String: Any] = ["name": market, "owner": ["name": "Carried with Sanduhr"],
                                       "plugins": [["name": name, "source": "./plugin"]]]
        if let data = try? JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]) {
            files.append((path: base + "/.claude-plugin/marketplace.json", data: data + Data([0x0A])))
        }
        let folder = (root as NSString).appendingPathComponent(base)
        let cmd = "\(prefix)claude plugin marketplace add \(shellQuoted(folder)) && \(prefix)claude plugin install \(shellQuoted(id)) --scope user"
        let hooks = hookCommands(pack, plugin: from)
        var detail = "from a folder, \(files.count - 1) \(files.count == 2 ? "file" : "files")"
        if !hooks.isEmpty { detail += "; runs " + hooks.joined(separator: ", ") }
        var row = Row(kind: .plugin, name: name, detail: detail, status: .new)
        row.files = files
        row.executable = Set(executable.filter { !$0.contains("..") }.map { base + "/plugin/" + $0 })
        row.job = .plugin(id: id, marketplace: market, source: folder)
        if files.count < 2 {
            row.status = .refused("The pack holds none of its files.")
        } else if installed.contains(id) {
            row.status = .same
        } else if installed.contains(where: { $0.hasPrefix(name + "@") }) {
            row.status = .differs
        } else if FileManager.default.fileExists(atPath: folder) {
            row.status = .refused("\(base) is in this folder already.")
        } else {
            switch cli {
            case .ready: row.status = .install(cmd)
            case .running: row.status = .refused("Claude Code is running in this folder. Quit it, then open the pack again to install this.")
            case .missing:
                // Written, then installed by hand: the files are what the command needs.
                row.detail += ". Then run: " + cmd
                row.command = cmd
            }
        }
        return row
    }

    /// The commands a carried plugin's hooks run, from `hooks/hooks.json` and the manifest's own
    /// `hooks`, each once, `${CLAUDE_PLUGIN_ROOT}` shortened.
    static func hookCommands(_ pack: ModPackReader.Unpacked, plugin from: String) -> [String] {
        var out: [String] = []
        func walk(_ v: Any) {
            switch v {
            case let o as [String: Any]:
                if let c = o["command"] as? String {
                    let short = c.replacingOccurrences(of: "${CLAUDE_PLUGIN_ROOT}/", with: "")
                    if !out.contains(short) { out.append(short) }
                }
                o.values.forEach(walk)
            case let a as [Any]: a.forEach(walk)
            default: break
            }
        }
        for path in [from + "hooks/hooks.json", from + ".claude-plugin/plugin.json"] {
            guard let data = pack.files[path], let doc = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { continue }
            walk(path.hasSuffix("plugin.json") ? doc["hooks"] ?? [:] : doc)
        }
        return out
    }

    private static func stem(_ file: String) -> String { (file as NSString).deletingPathExtension }

    /// `~/` put back as the home folder, in a settings value.
    static func atHome(_ s: String, home: String) -> String {
        let h = AccountData.normalized(home)
        if s == "~" { return h }
        return s.replacingOccurrences(of: "~/", with: h + "/")
    }

    static func isEnvName(_ s: String) -> Bool { s.range(of: #"^[A-Za-z_][A-Za-z0-9_]{0,127}$"#, options: .regularExpression) != nil }

    /// `CLAUDE_CONFIG_DIR=… ` for a folder other than Claude Code's default.
    static func configPrefix(_ seat: String, home: String) -> String {
        let fallback = AccountData.normalized((home as NSString).appendingPathComponent(".claude"))
        return seat == fallback ? "" : "CLAUDE_CONFIG_DIR=\(shellQuoted(ConfigPack.homeRelative(seat, home: home).replacingOccurrences(of: "~/", with: "$HOME/"))) "
    }

    /// What `claude plugin marketplace add` takes for a source: `owner/repo`, a git or https URL.
    static func marketplaceArgument(_ source: [String: Any]) -> String? {
        switch source["source"] as? String {
        case "github": (source["repo"] as? String).flatMap { $0.range(of: #"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"#, options: .regularExpression) != nil ? $0 : nil }
        case "git", "url": (source["url"] as? String).flatMap { $0.hasPrefix("https://") || $0.hasPrefix("git@") ? $0 : nil }
        default: nil
        }
    }

    /// Single-quoted for zsh, `$HOME` left to expand when it leads the string.
    static func shellQuoted(_ s: String) -> String {
        if s.hasPrefix("$HOME/") { return "\"$HOME\"" + shellQuoted(String(s.dropFirst(5))) }
        return "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    private static func summary(_ value: Any?) -> String {
        switch value {
        case let o as [String: Any]: "\(o.count) \(o.count == 1 ? "entry" : "entries")"
        case let a as [Any]: "\(a.count) \(a.count == 1 ? "entry" : "entries")"
        case let n as NSNumber where CFGetTypeID(n) == CFBooleanGetTypeID(): n.boolValue ? "on" : "off"
        case let v?: "\(v)"
        case nil: ""
        }
    }

    private static func readObject(_ path: String) -> [String: Any]? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        return try? JSONEdit.root(data)
    }

    /// A JSON value as JSONEdit writes it; nil for numbers it doesn't (fractions) or null.
    static func value(_ any: Any?) -> JSONEdit.Value? {
        switch any {
        case let s as String: return .string(s)
        case let n as NSNumber:
            if CFGetTypeID(n) == CFBooleanGetTypeID() { return .bool(n.boolValue) }
            let d = n.doubleValue
            return d == d.rounded() && abs(d) < 1e15 ? .int(n.intValue) : nil
        case let a as [Any]:
            var out: [JSONEdit.Value] = []
            for v in a { guard let w = value(v) else { return nil }; out.append(w) }
            return .array(out)
        case let o as [String: Any]:
            var pairs: [JSONEdit.Pair] = []
            for k in o.keys.sorted() { guard let w = value(o[k]) else { return nil }; pairs.append(JSONEdit.Pair(k, w)) }
            return .object(pairs)
        default: return nil
        }
    }

    // MARK: Writing

    /// One write an import made, enough to undo it and nothing more: never a value it replaced
    /// (it replaces none) and never an env value.
    struct Step: Codable, Equatable {
        enum Kind: String, Codable { case file, folder, setting, env, settingsFile, envObject, marketplace, plugin, mcpServer, cliKey }

        /// Written through `claude`, and taken back the same way.
        var throughCLI: Bool { [.marketplace, .plugin, .mcpServer].contains(kind) }
        var kind: Kind
        /// A file or folder path, a settings key, an env name, a marketplace, a plugin id or a server name.
        var name: String
        /// A file's SHA-256, or the canonical JSON SHA-256 of a settings value: undo leaves it
        /// when it changed since.
        var sha256: String?
        /// What sat between the braces before the member was the only one.
        var emptyInner: String?
    }

    /// One import: where, when, from which pack, and its steps in order.
    struct Record: Codable, Equatable, Identifiable {
        var id: String
        var seat: String
        var label: String
        var packSHA256: String
        var at: Date
        var steps: [Step]
    }

    enum Failure: Error, Equatable {
        /// settings.json changed while Sanduhr wrote it, or isn't JSON it can edit.
        case settingsChanged
        case writeFailed(String)

        var message: String {
            switch self {
            case .settingsChanged: "This folder's settings.json changed while Sanduhr wrote it, or isn't JSON Sanduhr can edit. Nothing in it was changed; try again."
            case .writeFailed(let path): "Sanduhr couldn't write \(path). What it wrote before that is in the import, and Undo Import takes it back."
            }
        }
    }

    /// Writes the chosen rows into `seat`: files first, then one settings write for every key and
    /// env value. Returns the record; on a failure partway, the record of what was written.
    static func apply(_ rows: [Row], env values: [String: String], seat: String, label: String, packSHA256: String,
                      installer: IntegrationInstaller = .standard, now: Date = Date()) -> (Record, Failure?) {
        let root = AccountData.normalized(seat)
        let fm = FileManager.default
        var record = Record(id: UUID().uuidString.lowercased(), seat: root, label: label, packSHA256: packSHA256, at: now, steps: [])

        // Files for what's new, and a carried plugin's before `claude` installs it.
        for row in rows where !row.files.isEmpty && (row.status == .new || { if case .install = row.status { true } else { false } }()) {
            for f in row.files {
                let target = (root as NSString).appendingPathComponent(f.path)
                // Made since the plan: the seat's own now, left alone.
                if fm.fileExists(atPath: target) { continue }
                var missing: [String] = []
                var dir = (target as NSString).deletingLastPathComponent
                while dir.count > root.count, !fm.fileExists(atPath: dir) {
                    missing.append(dir)
                    dir = (dir as NSString).deletingLastPathComponent
                }
                do {
                    for d in missing.reversed() {
                        try fm.createDirectory(atPath: d, withIntermediateDirectories: false)
                        record.steps.append(Step(kind: .folder, name: d))
                    }
                    guard fm.createFile(atPath: target, contents: f.data, attributes: [.posixPermissions: row.executable.contains(f.path) ? 0o755 : 0o644]) else {
                        throw CocoaError(.fileWriteUnknown)
                    }
                    record.steps.append(Step(kind: .file, name: target, sha256: sha(f.data)))
                } catch {
                    return (record, .writeFailed(target))
                }
            }
        }

        let settingsRows = rows.filter { $0.kind == .setting && $0.status == .new }
        // A typed value wins over one that came with the pack.
        let envValue = { (row: Row) in values[row.name] ?? row.value as? String ?? "" }
        let envRows = rows.filter { $0.kind == .env && $0.status == .needsValue && !envValue($0).isEmpty }
        guard !settingsRows.isEmpty || !envRows.isEmpty else { return (record, nil) }
        let file = (root as NSString).appendingPathComponent("settings.json")
        for _ in 0..<3 {
            // nil: the folder has no settings.json yet, and the import makes one.
            let original: [UInt8]?
            do { original = try installer.read(file) } catch { return (record, .settingsChanged) }
            var b = original ?? Array("{\n}\n".utf8)
            var steps: [Step] = original == nil ? [Step(kind: .settingsFile, name: file)] : []
            do {
                guard (try? JSONEdit.root(Data(b))) != nil else { return (record, .settingsChanged) }
                for row in settingsRows {
                    guard let v = value(row.value), try JSONEdit.topObject(b).member(row.name) == nil else { continue }
                    let r = JSONEdit.set(b, in: try JSONEdit.topObject(b), key: row.name, value: v)
                    b = r.bytes
                    steps.append(Step(kind: .setting, name: row.name, sha256: canonicalSHA(v.plain),
                                      emptyInner: r.emptyInner.map { String(decoding: $0, as: UTF8.self) }))
                }
                if !envRows.isEmpty {
                    let top = try JSONEdit.topObject(b)
                    if top.member("env") == nil {
                        let r = JSONEdit.set(b, in: top, key: "env", value: .object([]))
                        b = r.bytes
                        steps.append(Step(kind: .envObject, name: "env", emptyInner: r.emptyInner.map { String(decoding: $0, as: UTF8.self) }))
                    }
                    for row in envRows {
                        guard let m = try JSONEdit.topObject(b).member("env") else { break }
                        let obj = try JSONEdit.object(b, at: m.valueStart)
                        guard obj.member(row.name) == nil else { continue }
                        let r = JSONEdit.set(b, in: obj, key: row.name, value: .string(envValue(row)))
                        b = r.bytes
                        steps.append(Step(kind: .env, name: row.name, emptyInner: r.emptyInner.map { String(decoding: $0, as: UTF8.self) }))
                    }
                }
            } catch {
                return (record, .settingsChanged)
            }
            if (try? installer.commit(file, original: original, bytes: b, backup: false)) == true {
                record.steps += steps
                return (record, nil)
            }
        }
        return (record, .settingsChanged)
    }

    // MARK: Through the CLI (slice E)

    /// Runs `jobs` in `seat` in order: a plugin's marketplace is added first when the seat lacks it.
    /// Returns the steps that wrote something, and a line per job that failed. Blocking: run off
    /// the main thread.
    static func run(_ jobs: [Job], seat: String, cli: ClaudeCLI) -> (steps: [Step], failures: [String]) {
        var steps: [Step] = [], failures: [String] = []
        let settingsFile = (AccountData.normalized(seat) as NSString).appendingPathComponent("settings.json")
        let keysBefore = Set(readObject(settingsFile)?.keys.map { $0 } ?? [])

        var markets: Set<String>?
        for job in jobs {
            switch job {
            case .plugin(let id, let marketplace, let source):
                if markets == nil {
                    let r = cli.run(.marketplaceList, seat: seat)
                    let list = (try? JSONSerialization.jsonObject(with: Data(r.out.utf8))) as? [[String: Any]]
                    markets = Set((list ?? []).compactMap { $0["name"] as? String })
                }
                if markets?.contains(marketplace) != true {
                    let r = cli.run(.marketplaceAdd(source: source), seat: seat)
                    guard r.ok else { failures.append("\(id): adding its marketplace failed: \(r.reason)"); continue }
                    let added = r.json?["marketplace"] as? String ?? marketplace
                    markets?.insert(added)
                    steps.append(Step(kind: .marketplace, name: added))
                }
                let r = cli.run(.pluginInstall(id: id), seat: seat)
                guard r.ok, r.json?["outcome"] as? String == "ok" else { failures.append("\(id): \(r.reason)"); continue }
                if r.json?["installedVersion"] == nil { steps.append(Step(kind: .plugin, name: id)) }
            case .mcp(let name, let json, let sha256):
                let r = cli.run(.mcpAddJSON(name: name, json: json), seat: seat)
                guard r.ok else { failures.append("MCP server \(name): \(r.reason)"); continue }
                steps.append(Step(kind: .mcpServer, name: name, sha256: sha256))
            }
        }
        // Keys claude made for them, recorded so Undo can drop them once they're empty again.
        let made = Set(readObject(settingsFile)?.keys.map { $0 } ?? []).subtracting(keysBefore).intersection(cliKeys)
        if !steps.isEmpty { steps.insert(contentsOf: made.sorted().map { Step(kind: .cliKey, name: $0) }, at: 0) }
        return (steps, failures)
    }

    /// The settings keys `claude plugin` writes into a folder's settings.json.
    static let cliKeys: Set<String> = ["enabledPlugins", "extraKnownMarketplaces"]

    /// Takes back an import's CLI steps, last first, through `claude`: a server changed since, or
    /// a marketplace another plugin now comes from, is left and named. Blocking: run off the main
    /// thread. Without `cli`, each is left and named.
    static func undoCLI(_ record: Record, cli: ClaudeCLI?, home: String = NSHomeDirectory()) -> [String] {
        let steps = record.steps.filter(\.throughCLI)
        guard !steps.isEmpty else { return [] }
        guard let cli else { return steps.map { "\($0.name), since claude wasn't found to take it back" } }
        var left: [String] = []
        for step in steps.reversed() {
            switch step.kind {
            case .plugin:
                let r = cli.run(.pluginUninstall(id: step.name), seat: record.seat)
                if !r.ok, !r.reason.localizedCaseInsensitiveContains("not installed") { left.append("plugin \(step.name): \(r.reason)") }
            case .marketplace:
                let r = cli.run(.pluginList, seat: record.seat)
                let ids = ((try? JSONSerialization.jsonObject(with: Data(r.out.utf8))) as? [[String: Any]] ?? []).compactMap { $0["id"] as? String }
                if ids.contains(where: { $0.hasSuffix("@" + step.name) }) {
                    left.append("marketplace \(step.name), which a plugin now comes from")
                    continue
                }
                let rm = cli.run(.marketplaceRemove(name: step.name), seat: record.seat)
                if !rm.ok { left.append("marketplace \(step.name): \(rm.reason)") }
            case .mcpServer:
                let servers = readObject(ClaudeCodeFolders.configFile(for: record.seat, home: home))?["mcpServers"] as? [String: Any] ?? [:]
                guard let current = servers[step.name] else { continue }
                guard canonicalSHA(current) == step.sha256 else { left.append("MCP server \(step.name), changed since"); continue }
                let r = cli.run(.mcpRemove(name: step.name), seat: record.seat)
                if !r.ok { left.append("MCP server \(step.name): \(r.reason)") }
            default: break
            }
        }
        return left
    }

    /// Takes an import back, last step first. A file, value or folder that changed since is left,
    /// and named in what this returns.
    static func undo(_ record: Record, installer: IntegrationInstaller = .standard) -> [String] {
        let fm = FileManager.default
        var left: [String] = []
        let file = (record.seat as NSString).appendingPathComponent("settings.json")
        let settingSteps = record.steps.filter { [.setting, .env, .envObject, .settingsFile, .cliKey].contains($0.kind) }
        if !settingSteps.isEmpty {
            var done = false
            for _ in 0..<3 where !done {
                guard let original = try? installer.read(file) else { left.append("settings.json is gone"); break }
                var b = original
                var stepLeft: [String] = []
                var deleteFile = false
                for step in settingSteps.reversed() {
                    guard let top = try? JSONEdit.topObject(b) else { break }
                    switch step.kind {
                    case .setting:
                        guard let m = top.member(step.name) else { continue }
                        let current = try? JSONSerialization.jsonObject(with: Data(JSONEdit.text(b, m)), options: [.fragmentsAllowed])
                        guard let current, canonicalSHA(current) == step.sha256 else { stepLeft.append("settings key \(step.name), changed since"); continue }
                        b = JSONEdit.remove(b, in: top, key: step.name, emptyInner: step.emptyInner.map { Array($0.utf8) })
                    case .env:
                        guard let m = top.member("env"), let obj = try? JSONEdit.object(b, at: m.valueStart), obj.member(step.name) != nil else { continue }
                        b = JSONEdit.remove(b, in: obj, key: step.name, emptyInner: step.emptyInner.map { Array($0.utf8) })
                    case .envObject:
                        guard let m = top.member("env"), let obj = try? JSONEdit.object(b, at: m.valueStart) else { continue }
                        if obj.members.isEmpty {
                            b = JSONEdit.remove(b, in: top, key: "env", emptyInner: step.emptyInner.map { Array($0.utf8) })
                        }
                    case .cliKey:
                        // Left by claude's install: dropped only once it holds nothing again.
                        guard let m = top.member(step.name), let obj = try? JSONEdit.object(b, at: m.valueStart), obj.members.isEmpty else { continue }
                        b = JSONEdit.remove(b, in: top, key: step.name)
                    case .settingsFile:
                        deleteFile = (try? JSONEdit.topObject(b))?.members.isEmpty == true
                        if !deleteFile { stepLeft.append("settings.json, which now holds other settings") }
                    default: break
                    }
                }
                if (try? installer.commit(file, original: original, bytes: deleteFile ? nil : b, backup: false)) == true {
                    left += stepLeft
                    done = true
                }
            }
            if !done { left.append("settings.json, which kept changing") }
        }
        for step in record.steps.reversed() {
            switch step.kind {
            case .file:
                guard let data = fm.contents(atPath: step.name) else { continue }
                if sha(data) == step.sha256 { try? fm.removeItem(atPath: step.name) } else { left.append(ConfigPack.homeRelative(step.name, home: NSHomeDirectory()) + ", changed since") }
            case .folder:
                if (try? fm.contentsOfDirectory(atPath: step.name))?.isEmpty == true { try? fm.removeItem(atPath: step.name) }
            default: break
            }
        }
        return left
    }

    static func sha(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }

    static func canonicalSHA(_ value: Any) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys, .fragmentsAllowed, .withoutEscapingSlashes])) ?? Data()
        return sha(data)
    }

    // MARK: The record of imports

    /// `imports.json` in Sanduhr's folder: every import not yet undone.
    struct Log {
        let url: URL
        static var standard: Log { Log(url: HistoryStore.Files.standard.dir.appendingPathComponent("imports.json")) }

        func list() -> [Record] {
            guard let data = FileManager.default.contents(atPath: url.path) else { return [] }
            let d = JSONDecoder()
            d.dateDecodingStrategy = .iso8601
            return ReceiptLog.decode(Record.self, from: data, decoder: d).records
        }

        func save(_ records: [Record]) {
            let e = JSONEncoder()
            e.dateEncodingStrategy = .iso8601
            e.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
            if let data = try? e.encode(records) { HandoffFiles.writeOwnerOnly(data, to: url) }
        }

        func add(_ r: Record) { save(list() + [r]) }
        func remove(_ id: String) { save(list().filter { $0.id != id }) }
    }
}
