import Foundation

/// Where a settings value comes from, highest precedence first (item 71, slice 1). The docs'
/// stack also has a command-line level (`--settings`) between managed and project local, and
/// shell variables that override some keys; Sanduhr can't see either for a session it didn't
/// start, so the page says so.
enum SettingsSource: Int, CaseIterable, Comparable, Sendable {
    case managed, projectLocal, projectShared, user

    static func < (a: SettingsSource, b: SettingsSource) -> Bool { a.rawValue < b.rawValue }

    var title: String {
        switch self {
        case .managed: "Managed"
        case .projectLocal: "Project local"
        case .projectShared: "Project shared"
        case .user: "User"
        }
    }

    var isProject: Bool { self == .projectLocal || self == .projectShared }
}

/// One settings file in the walk (or the managed files merged into one). The object is what
/// `JSONSerialization` read; it never leaves the background read, which turns it into masked rows.
struct SettingsLayer: @unchecked Sendable {
    let source: SettingsSource
    /// The file, for the row's "from …" line.
    let file: String
    let object: [String: Any]
    /// For a merged managed layer: the file that supplied each row key (`a.b`), when it isn't `file`.
    var fileFor: [String: String] = [:]
}

/// One key in effect, ready to show: the masked value, where it comes from and where it is
/// honored.
struct SettingRow: Identifiable, Equatable, Sendable {
    enum Value: Equatable, Sendable {
        case toggle(Bool)
        case text(String)
    }

    /// The key path from the top of the file (`["permissions", "allow"]`).
    let path: [String]
    /// Masked, through ConfigMask.
    let value: Value
    let source: SettingsSource
    let file: String
    /// Every source that added to a merged value, highest first; just `source` otherwise.
    var contributors: [SettingsSource] = []
    /// The docs' family (model and responses, permissions, env, …).
    var family: String
    /// Where the key is honored ("Everywhere", "User or managed only", …).
    var honored: String
    /// A value set at a level that ignores it, or a lint for this key.
    var warnings: [String] = []
    /// The strictest value won over a higher level's (`disableClaudeAiConnectors`, …).
    var strictWon = false
    /// Listed in the seat's `.seat/local-keys.json`: stripped before commit, this machine only.
    var machineOnly = false
    /// Projects whose own settings set this key too (never which).
    var setInProjects = 0
    /// The value as a work seat shows it: absolute paths outside the seat replaced (a project's
    /// folder can name it). Nil when that changes nothing.
    var workValue: Value?

    var id: String { path.joined(separator: "\u{1F}") }
    var key: String { path.joined(separator: ".") }

    func shown(work: Bool) -> Value { work ? (workValue ?? value) : value }
}

/// The precedence walker (item 71, slice 1), computed by Sanduhr: `/status` lists the files
/// that loaded, not which file supplied each key. Checked against the settings docs
/// (code.claude.com/docs/en/settings, settings-reference, managed-settings). Managed, then
/// project local, then project shared, then user; the highest level that sets a key wins, with
/// these rules:
///
/// - objects merge key by key (each key inside follows the same rules) and lists combine,
///   duplicates dropped, at any depth (`sandbox` lists, `autoMode` lists);
/// - `fallbackModel` and `modelPicker` are taken whole from the highest level; `availableModels`
///   from managed settings applies as-is, else it merges; `modelSettings` resolves one model at
///   a time; `extraKnownMarketplaces` one entry at a time, each whole;
/// - `enabledPlugins`, `env`, `permissions`, `hooks` and `autoMode` show one row per member;
/// - the strict keys take the strictest value from the levels the docs allow (the table in
///   "Exceptions to managed settings precedence");
/// - a value at a level that ignores it is left out, with a warning on the row, and with
///   `allowManagedPermissionRulesOnly` on, only managed permission rules count.
enum SettingsPrecedence {
    /// Values taken whole from the highest level that sets them.
    static let wholeValues: Set<String> = ["fallbackModel", "modelPicker"]
    /// Objects whose members resolve one by one, each member whole.
    static let perEntryWhole: Set<String> = ["modelSettings", "extraKnownMarketplaces", "managedMcpServers"]
    /// Objects shown as one row per member.
    static let perMember: Set<String> = ["enabledPlugins", "env", "permissions", "hooks", "autoMode"]

    enum Strict: Sendable {
        /// This boolean wins from the levels listed (nil: any level).
        case bool(Bool, from: Set<SettingsSource>?)
        /// The earliest in this order wins (the strictest), from the levels listed.
        case order([String], from: Set<SettingsSource>?)

        var from: Set<SettingsSource>? {
            switch self {
            case .bool(_, let f), .order(_, let f): f
            }
        }
    }

    static let projects: Set<SettingsSource> = [.projectShared, .projectLocal]

    /// The docs' table of exceptions to managed settings precedence.
    static let strict: [String: Strict] = [
        "disableClaudeAiConnectors": .bool(true, from: nil),
        "maxEffortLevel": .order(["low", "medium", "high", "xhigh", "max"], from: nil),
        "permissions.blockReadsOutsideWorkingDirectories": .bool(true, from: nil),
        "enableArtifact": .bool(false, from: nil),
        "isolatePeerMachines": .bool(true, from: nil),
        "syncClaudeAiSkills": .bool(false, from: [.managed, .projectLocal, .user]),
        "syncClaudeAiPlugins": .bool(false, from: [.managed, .projectLocal, .user]),
        "useAutoModeDuringPlan": .bool(false, from: [.managed, .projectLocal, .user]),
        "autoMode.classifyAllShell": .bool(true, from: [.user]),
        "remoteControlAtStartup": .bool(false, from: projects),
        "crossSessionInbound": .order(["refuse", "hold", "accept"], from: projects),
    ]

    /// Keys honored only in user or managed settings.
    static let userOrManagedOnly: Set<String> = ["pluginConfigs", "appendPlugins", "modelPicker", "autoMode",
                                                 "desktopSessionCleanupPeriodDays"]
    /// Keys a shared project file can't set, either way.
    static let notFromSharedProject: Set<String> = ["syncClaudeAiSkills", "syncClaudeAiPlugins", "useAutoModeDuringPlan"]

    /// The scope badge: where a key is honored, by the docs' Scope column.
    static func honored(_ path: [String]) -> String {
        guard let top = path.first else { return "Everywhere" }
        if top == "mcpServers" { return "Not read from settings files" }
        if userOrManagedOnly.contains(top) { return "User or managed only" }
        if notFromSharedProject.contains(top) { return "User, local or managed" }
        if path == ["permissions", "defaultMode"] { return "Everywhere (auto and bypass: user or managed)" }
        if path.count == 2, path[0] == "env", envIgnoredInProject(path[1], value: nil) { return "User or managed only" }
        if top == "allowManagedPermissionRulesOnly" { return "Managed only" }
        return "Everywhere"
    }

    /// Variables Claude Code drops from a project's or local `env` (the docs' list): where it
    /// keeps its files, Windows machine variables, session-content export, the telemetry
    /// exporters (except values that turn them off), start-up and sync, dialog timers, and
    /// attachments.
    static func envIgnoredInProject(_ name: String, value: Any?) -> Bool {
        let fixed: Set<String> = ["CLAUDE_CONFIG_DIR", "CLAUDE_CODE_TMPDIR", "HOME", "TMPDIR", "TMP", "TEMP",
                                  "SystemRoot", "ComSpec", "ProgramData", "LOCALAPPDATA", "PATHEXT", "PSModulePath",
                                  "OTEL_LOG_RAW_API_BODIES", "ENABLE_BETA_TRACING_DETAILED", "BETA_TRACING_ENDPOINT",
                                  "CLAUDE_CODE_ENABLE_TELEMETRY", "CLAUDE_CODE_ENHANCED_TELEMETRY_BETA",
                                  "ENABLE_ENHANCED_TELEMETRY_BETA", "OTEL_LOG_ASSISTANT_RESPONSES",
                                  "OTEL_EXPORTER_PROMETHEUS_HOST", "OTEL_EXPORTER_PROMETHEUS_PORT",
                                  "CLAUDE_CODE_PROCESS_WRAPPER", "CLAUDE_CODE_SYNC_SKILLS", "CLAUDE_CODE_SYNC_PLUGINS",
                                  "CLAUDE_CODE_PLUGIN_CACHE_DIR", "CLAUDE_CODE_PLUGIN_SEED_DIR",
                                  "CLAUDE_CODE_USER_DIALOG_TIMEOUT_MS", "CLAUDE_AFK_TIMEOUT_MS", "CLAUDE_AFK_COUNTDOWN_MS",
                                  "CLAUDE_CODE_DISABLE_ATTACHMENTS"]
        if fixed.contains(name) || name.hasPrefix("XDG_") || name.hasPrefix("ProgramFiles") { return true }
        let text = (value as? String)?.lowercased()
        if ["OTEL_LOGS_EXPORTER", "OTEL_METRICS_EXPORTER", "OTEL_TRACES_EXPORTER"].contains(name) {
            return text != "none"
        }
        if ["OTEL_LOG_USER_PROMPTS", "OTEL_LOG_TOOL_CONTENT", "OTEL_LOG_TOOL_DETAILS"].contains(name) {
            return !["0", "false", "off", "no"].contains(text ?? "")
        }
        if name.hasPrefix("OTEL_EXPORTER_OTLP_") {
            return ["_ENDPOINT", "_HEADERS", "_PROTOCOL", "_CERTIFICATE", "_CLIENT_KEY", "_INSECURE"].contains { name.hasSuffix($0) }
        }
        return false
    }

    /// Why a value at `path` in a file of `source` is ignored, nil when it is honored.
    static func ignored(_ path: [String], value: Any, source: SettingsSource, managedRulesOnly: Bool = false) -> String? {
        guard let top = path.first else { return nil }
        if top == "mcpServers" {
            return "mcpServers in a settings file is ignored: servers live in .claude.json or .mcp.json."
        }
        if managedRulesOnly, source != .managed, path.count == 2, top == "permissions", ["allow", "ask", "deny"].contains(path[1]) {
            return "allowManagedPermissionRulesOnly is on: only managed settings supply permission rules."
        }
        guard source.isProject else { return nil }
        if userOrManagedOnly.contains(top) {
            return "\(top) is read from user or managed settings only; the \(source.title.lowercased()) value is ignored."
        }
        if path == ["permissions", "defaultMode"], let mode = value as? String, mode == "auto" || mode == "bypassPermissions" {
            return "defaultMode \(mode) in a project file is ignored."
        }
        if path.count == 2, top == "env", envIgnoredInProject(path[1], value: value) {
            return "\(path[1]) in a project's env is ignored."
        }
        if notFromSharedProject.contains(top), source == .projectShared {
            return "\(top) in a shared project file is ignored; set it in the seat's own settings.json or settings.local.json."
        }
        if top == "remoteControlAtStartup", IntegrationInstaller.boolValue(value) == true {
            return "remoteControlAtStartup: true in a project file is ignored."
        }
        return nil
    }

    /// The docs' families, for grouping rows.
    static func family(_ top: String) -> String {
        switch top {
        case "model", "fallbackModel", "availableModels", "modelSettings", "maxEffortLevel", "effortLevel",
             "alwaysThinkingEnabled", "modelPicker", "outputStyle": "Model and responses"
        case "permissions", "autoMode", "additionalDirectories", "allowManagedPermissionRulesOnly", "useAutoModeDuringPlan": "Permissions"
        case "env": "Environment"
        case "hooks", "disableAllHooks": "Hooks"
        case "enabledPlugins", "extraKnownMarketplaces", "pluginConfigs", "appendPlugins", "skillOverrides",
             "strictKnownMarketplaces": "Plugins"
        case "enableAllProjectMcpServers", "enabledMcpjsonServers", "disabledMcpjsonServers", "allowedMcpServers",
             "deniedMcpServers", "disableClaudeAiConnectors", "mcpServers", "managedMcpServers": "MCP approval"
        case "claudeMdExcludes", "autoMemoryEnabled", "memory": "Memory"
        case "includeCoAuthoredBy", "attribution": "Attribution"
        case "sandbox": "Sandbox"
        case "statusLine", "theme", "spinnerTipsEnabled", "spinnerVerbs", "prefersReducedMotion", "enableArtifact",
             "disableArtifact": "UI"
        case "cleanupPeriodDays", "desktopSessionCleanupPeriodDays", "syncClaudeAiSkills", "syncClaudeAiPlugins",
             "isolatePeerMachines", "crossSessionInbound", "remoteControlAtStartup": "Privacy and history"
        case "autoUpdates", "autoUpdatesChannel": "Updates"
        default: "Other"
        }
    }

    // MARK: Managed files

    /// `managed-settings.json` then each `managed-settings.d/*.json` in name order, merged into
    /// one managed layer as the docs describe: a later file's single value replaces an earlier
    /// one, lists combine without duplicates, nested blocks merge key by key, `fallbackModel` and
    /// `modelPicker` are replaced whole, and `extraKnownMarketplaces` and `managedMcpServers` one
    /// entry at a time, whole. Each row key remembers the last file that set it.
    static func mergeManaged(_ files: [(path: String, object: [String: Any])]) -> SettingsLayer? {
        guard let first = files.first else { return nil }
        var merged: [String: Any] = [:]
        var fileFor: [String: String] = [:]
        for (path, object) in files {
            for (key, value) in object {
                if let old = merged[key] {
                    if wholeValues.contains(key) {
                        merged[key] = value
                    } else if perEntryWhole.contains(key), var o = old as? [String: Any], let n = value as? [String: Any] {
                        for (k, v) in n { o[k] = v }
                        merged[key] = o
                    } else {
                        merged[key] = combine(later: value, earlier: old)
                    }
                } else {
                    merged[key] = value
                }
            }
            for rowPath in rowPaths(object) { fileFor[rowPath.joined(separator: ".")] = path }
        }
        return SettingsLayer(source: .managed, file: first.path, object: merged, fileFor: fileFor)
    }

    /// Two values for one key where `later` takes precedence: objects key by key, lists combined
    /// (`later`'s first), anything else `later`.
    static func combine(later: Any, earlier: Any) -> Any {
        if let a = later as? [String: Any], let b = earlier as? [String: Any] {
            var out = b
            for (k, v) in a { out[k] = b[k].map { combine(later: v, earlier: $0) } ?? v }
            return out
        }
        if let a = later as? [Any], let b = earlier as? [Any] {
            var out = a
            for e in b where !out.contains(where: { same($0, e) }) { out.append(e) }
            return out
        }
        return later
    }

    // MARK: The walk

    /// The rows for one context, `layers` in any order (one managed layer at most; merge the
    /// managed files first). Each row's value is masked, and `workValue` has paths outside
    /// `seat` replaced.
    static func walk(_ layers: [SettingsLayer], machineOnly: Set<String> = [], seat: String? = nil,
                     home: String = NSHomeDirectory()) -> [SettingRow] {
        let sorted = layers.sorted { $0.source < $1.source }
        let rulesOnly = sorted.contains {
            $0.source == .managed && IntegrationInstaller.boolValue($0.object["allowManagedPermissionRulesOnly"]) == true
        }
        var paths: [[String]] = []
        for layer in sorted {
            for path in rowPaths(layer.object) where !paths.contains(path) { paths.append(path) }
        }
        var rows: [SettingRow] = []
        for path in paths {
            var found: [(SettingsLayer, Any)] = []
            var warnings: [String] = []
            for layer in sorted {
                guard let v = value(at: path, in: layer.object) else { continue }
                if let why = ignored(path, value: v, source: layer.source, managedRulesOnly: rulesOnly) {
                    warnings.append(why)
                    continue
                }
                found.append((layer, v))
            }
            let context = RowContext(machineOnly: machineOnly, seat: seat, home: home)
            guard !found.isEmpty else {
                // Set only where it is ignored: shown so the person sees it does nothing.
                if let layer = sorted.first(where: { value(at: path, in: $0.object) != nil }),
                   let v = value(at: path, in: layer.object) {
                    var row = row(path, v, layer, warnings: warnings, context)
                    row.warnings.insert("Not in effect.", at: 0)
                    rows.append(row)
                }
                continue
            }
            rows.append(resolve(path, found: found, layers: sorted, warnings: warnings, context))
        }
        return rows
    }

    struct RowContext {
        let machineOnly: Set<String>
        let seat: String?
        let home: String
    }

    /// One row's value from the levels that set it, `found` highest first.
    private static func resolve(_ path: [String], found: [(SettingsLayer, Any)], layers: [SettingsLayer],
                                warnings: [String], _ context: RowContext) -> SettingRow {
        var warnings = warnings
        let key = path.joined(separator: ".")
        let top = path[0]
        let first = found[0]
        var chosen = first
        var merged: Any = first.1
        var contributors = [first.0.source]
        var strictWon = false
        if let rule = strict[key] {
            var candidates = found
            if key == "enableArtifact" {
                // disableArtifact: true from any level turns the Artifact tool off too.
                for layer in layers where IntegrationInstaller.boolValue(layer.object["disableArtifact"]) == true {
                    candidates.append((layer, false))
                }
            }
            let best = strictest(candidates, rule)
            strictWon = best.0.source != first.0.source || !same(best.1, first.1)
            chosen = best
            merged = best.1
            contributors = [best.0.source]
            if let from = rule.from, !from.isDisjoint(with: projects) {
                for (layer, v) in found where layer.source.isProject && !(layer.source == best.0.source && same(v, best.1)) {
                    warnings.append("The \(layer.source.title.lowercased()) value isn't stricter, so it's ignored.")
                }
            }
        } else if wholeValues.contains(top) {
            // Taken whole from the highest level.
        } else if top == "availableModels", let managed = found.first(where: { $0.0.source == .managed }) {
            chosen = managed
            merged = managed.1
            contributors = [.managed]
        } else if perEntryWhole.contains(top), found.allSatisfy({ $0.1 is [String: Any] }) {
            var out: [String: Any] = [:]
            for (_, v) in found.reversed() { for (k, e) in v as? [String: Any] ?? [:] { out[k] = e } }
            merged = out
            contributors = found.map(\.0.source)
        } else if found.allSatisfy({ $0.1 is [String: Any] }) || found.allSatisfy({ $0.1 is [Any] }), found.count > 1 {
            merged = found.dropFirst().reduce(first.1) { combine(later: $0, earlier: $1.1) }
            contributors = found.map(\.0.source)
        }
        var r = row(path, merged, chosen.0, warnings: warnings, context)
        var seen: [SettingsSource] = []
        for s in contributors where !seen.contains(s) { seen.append(s) }
        r.contributors = seen
        r.strictWon = strictWon
        return r
    }

    private static func row(_ path: [String], _ value: Any, _ layer: SettingsLayer, warnings: [String],
                            _ context: RowContext) -> SettingRow {
        func shown(_ v: Any) -> SettingRow.Value {
            if isBool(v), let b = v as? Bool, !ConfigMask.isSecret(path) { return .toggle(b) }
            return .text(ConfigMask.display(v, at: path))
        }
        let plain = shown(value)
        var work: SettingRow.Value?
        if let seat = context.seat {
            let redacted = shown(WallMask.redactPaths(value, seat: seat, home: context.home))
            if redacted != plain { work = redacted }
        }
        return SettingRow(path: path, value: plain, source: layer.source,
                          file: layer.fileFor[path.joined(separator: ".")] ?? layer.file,
                          contributors: [layer.source], family: family(path[0]), honored: honored(path),
                          warnings: warnings, machineOnly: context.machineOnly.contains(path[0]), workValue: work)
    }

    /// A JSON boolean, not a number that bridges to one.
    static func isBool(_ v: Any) -> Bool {
        guard let n = v as? NSNumber else { return false }
        return CFGetTypeID(n) == CFBooleanGetTypeID()
    }

    /// The row paths a settings object has: its keys, one level deeper for the per-member keys.
    static func rowPaths(_ object: [String: Any]) -> [[String]] {
        var out: [[String]] = []
        for key in object.keys.sorted() {
            if perMember.contains(key), let o = object[key] as? [String: Any], !o.isEmpty {
                for sub in o.keys.sorted() { out.append([key, sub]) }
            } else {
                out.append([key])
            }
        }
        return out
    }

    static func value(at path: [String], in object: [String: Any]) -> Any? {
        var current: Any = object
        for segment in path {
            guard let o = current as? [String: Any], let next = o[segment] else { return nil }
            current = next
        }
        return current
    }

    /// The strictest of the values the rule lets count, the precedence winner (`found[0]`)
    /// always among them.
    private static func strictest(_ found: [(SettingsLayer, Any)], _ rule: Strict) -> (SettingsLayer, Any) {
        let winner = found[0]
        let allowed = found.filter { rule.from?.contains($0.0.source) ?? true }
        switch rule {
        case .bool(let wins, _):
            if IntegrationInstaller.boolValue(winner.1) == wins { return winner }
            return allowed.first { IntegrationInstaller.boolValue($0.1) == wins } ?? winner
        case .order(let order, _):
            func rank(_ v: Any) -> Int { order.firstIndex(of: v as? String ?? "") ?? order.count }
            return ([winner] + allowed).min { rank($0.1) < rank($1.1) } ?? winner
        }
    }

    static func same(_ a: Any, _ b: Any) -> Bool {
        (a as? NSObject)?.isEqual(b) ?? false
    }

    // MARK: Lints

    /// Known dead or misplaced config in a seat's own files: `Write(path)` allow rules, a
    /// `settings.local.json` in the seat folder, Windows paths on a Mac, `.claude.json` keys that
    /// belong in settings, and a statusline whose command isn't on this Mac.
    static func lints(settings: [String: Any], configKeys: [String], seat: String, isDefault: Bool,
                      fileManager fm: FileManager = .default) -> [String] {
        var out: [String] = []
        let allow = (value(at: ["permissions", "allow"], in: settings) as? [Any] ?? []).compactMap { $0 as? String }
        let writes = allow.filter { $0.hasPrefix("Write(") }
        if !writes.isEmpty {
            out.append("\(writes.count) Write(path) allow rule\(writes.count == 1 ? "" : "s") never match: only Edit(path) rules do.")
        }
        if fm.fileExists(atPath: (seat as NSString).appendingPathComponent("settings.local.json")) {
            out.append("settings.local.json in the seat folder isn't read as user settings, so its hooks and permissions do nothing"
                       + (isDefault ? " (except in a session started in the home folder, where it may read as that folder's project file)." : "."))
        }
        let windows = windowsPaths(settings, path: [])
        if !windows.isEmpty {
            out.append("Windows paths in \(windows.sorted().joined(separator: ", ")): this Mac can't use them.")
        }
        let misplaced = ["permissions", "hooks", "env"].filter(configKeys.contains)
        if !misplaced.isEmpty {
            out.append("\(misplaced.joined(separator: ", ")) in .claude.json \(misplaced.count == 1 ? "is" : "are") ignored: \(misplaced.count == 1 ? "it belongs" : "they belong") in settings.json.")
        }
        if let command = value(at: ["statusLine", "command"], in: settings) as? String,
           let exe = firstPath(command), !fm.fileExists(atPath: exe) {
            out.append("The statusline command names a file that isn't on this Mac: written on another machine?")
        }
        return out
    }

    /// The top-level keys whose strings hold a Windows absolute path (`C:\…`).
    static func windowsPaths(_ value: Any, path: [String]) -> Set<String> {
        if let s = value as? String {
            return s.range(of: #"(^|[\s"'])[A-Za-z]:\\"#, options: .regularExpression) != nil ? [path.first ?? ""] : []
        }
        if let o = value as? [String: Any] {
            return o.reduce(into: Set<String>()) { $0.formUnion(windowsPaths($1.value, path: path + [$1.key])) }
        }
        if let a = value as? [Any] {
            return a.reduce(into: Set<String>()) { $0.formUnion(windowsPaths($1, path: path)) }
        }
        return []
    }

    /// The first word of a command when it is an absolute path (quotes dropped).
    static func firstPath(_ command: String) -> String? {
        let trimmed = command.trimmingCharacters(in: .whitespaces)
        let word: String
        if let q = trimmed.first, q == "\"" || q == "'" {
            word = String(trimmed.dropFirst().prefix { $0 != q })
        } else {
            word = String(trimmed.prefix { !$0.isWhitespace })
        }
        return word.hasPrefix("/") ? word : nil
    }
}

/// The wall's path mask (item 71, slice 1b): on a work seat's view, an absolute path outside the
/// seat (a project folder in a permission rule, `additionalDirectories`, a hook command) is
/// replaced, because a folder's name can name the work. Paths inside the seat stay.
enum WallMask {
    static let placeholder = "‹path outside the seat›"

    static func redactPaths(_ value: Any, seat: String, home: String) -> Any {
        if let s = value as? String { return redact(s, seat: seat, home: home) }
        if let o = value as? [String: Any] { return o.mapValues { redactPaths($0, seat: seat, home: home) } }
        if let a = value as? [Any] { return a.map { redactPaths($0, seat: seat, home: home) } }
        return value
    }

    /// `s` with each absolute path (`/…`, `//…`, `~/…`) outside `seat` replaced.
    static func redact(_ s: String, seat: String, home: String) -> String {
        guard let re = try? NSRegularExpression(pattern: #"(?<![\w.])(~/|/)[^\s"'()\[\],;]*"#) else { return s }
        let ns = s as NSString
        var out = ""
        var last = 0
        for m in re.matches(in: s, range: NSRange(location: 0, length: ns.length)) {
            let token = ns.substring(with: m.range)
            var path = token.hasPrefix("~/") ? (home as NSString).appendingPathComponent(String(token.dropFirst(2))) : token
            while path.hasPrefix("//") { path.removeFirst() }
            let inside = path == seat || path.hasPrefix(seat + "/") || token == "/" || token == "~/"
            guard !inside else { continue }
            out += ns.substring(with: NSRange(location: last, length: m.range.location - last)) + placeholder
            last = m.range.location + m.range.length
        }
        return last == 0 ? s : out + ns.substring(from: last)
    }
}
