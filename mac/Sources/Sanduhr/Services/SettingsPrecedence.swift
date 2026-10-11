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

/// One settings file in the walk. The object is what `JSONSerialization` read; it never leaves
/// the background read, which turns it into masked rows.
struct SettingsLayer: @unchecked Sendable {
    let source: SettingsSource
    /// The file, for the row's "from …" line.
    let file: String
    let object: [String: Any]
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
    /// Every source that added to a merged list, highest first; just `source` otherwise.
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

    var id: String { path.joined(separator: "\u{1F}") }
    var key: String { path.joined(separator: ".") }
}

/// The precedence walker (item 71, slice 1), computed by Sanduhr: `/status` lists the files
/// that loaded, not which file supplied each key. Managed, then project local, then project
/// shared, then user; the highest level that sets a key wins, except:
///
/// - arrays merge across levels (highest first, duplicates dropped), except `fallbackModel`,
///   `modelPicker`, `availableModels` and `modelSettings`, which override;
/// - `enabledPlugins`, `env`, `permissions` and `hooks` resolve per member (per plugin id, per
///   variable, per permission list, per hook event);
/// - the strict-wins keys take the strictest value from any level that may set them
///   (`disableClaudeAiConnectors`, `maxEffortLevel`, `permissions.blockReadsOutsideWorkingDirectories`,
///   `enableArtifact`, `isolatePeerMachines`, `syncClaudeAiSkills`, `syncClaudeAiPlugins`);
/// - a value at a level that ignores it is left out, with a warning on the row.
enum SettingsPrecedence {
    /// Arrays that override instead of merging.
    static let overridingLists: Set<String> = ["fallbackModel", "modelPicker", "availableModels", "modelSettings"]
    /// Objects whose members resolve one by one.
    static let perMember: Set<String> = ["enabledPlugins", "env", "permissions", "hooks"]

    enum Strict: Sendable {
        /// This boolean wins wherever it is set.
        case bool(Bool)
        /// The earliest in this order wins (the lowest cap).
        case order([String])
    }

    static let strict: [String: Strict] = [
        "disableClaudeAiConnectors": .bool(true),
        "maxEffortLevel": .order(["low", "medium", "high", "xhigh", "max"]),
        "permissions.blockReadsOutsideWorkingDirectories": .bool(true),
        "enableArtifact": .bool(false),
        "isolatePeerMachines": .bool(true),
        "syncClaudeAiSkills": .bool(false),
        "syncClaudeAiPlugins": .bool(false),
    ]

    /// Keys honored only in user or managed settings.
    static let userOrManagedOnly: Set<String> = ["pluginConfigs", "appendPlugins", "modelPicker", "autoMode",
                                                 "desktopSessionCleanupPeriodDays"]

    /// The scope badge: where a key is honored, by the docs' Scope column.
    static func honored(_ path: [String]) -> String {
        guard let top = path.first else { return "Everywhere" }
        if top == "mcpServers" { return "Not read from settings files" }
        if userOrManagedOnly.contains(top) { return "User or managed only" }
        if top == "syncClaudeAiSkills" || top == "syncClaudeAiPlugins" { return "User, local or managed" }
        if path == ["permissions", "defaultMode"] { return "Everywhere (auto and bypass: user or managed)" }
        if path == ["env", "CLAUDE_CONFIG_DIR"] { return "User or managed only" }
        return "Everywhere"
    }

    /// Why a value at `path` in a file of `source` is ignored, nil when it is honored.
    static func ignored(_ path: [String], value: Any, source: SettingsSource) -> String? {
        guard let top = path.first else { return nil }
        if top == "mcpServers" {
            return "mcpServers in a settings file is ignored: servers live in .claude.json or .mcp.json."
        }
        guard source.isProject else { return nil }
        if userOrManagedOnly.contains(top) {
            return "\(top) is read from user or managed settings only; the \(source.title.lowercased()) value is ignored."
        }
        if path == ["permissions", "defaultMode"], let mode = value as? String, mode == "auto" || mode == "bypassPermissions" {
            return "defaultMode \(mode) in a project file is ignored."
        }
        if path == ["env", "CLAUDE_CONFIG_DIR"] {
            return "CLAUDE_CONFIG_DIR in a project's env is ignored."
        }
        if (top == "syncClaudeAiSkills" || top == "syncClaudeAiPlugins"), source == .projectShared,
           IntegrationInstaller.boolValue(value) == false {
            return "\(top): false in a shared project file is ignored; set it in the seat's own settings.json."
        }
        return nil
    }

    /// The docs' families, for grouping rows.
    static func family(_ top: String) -> String {
        switch top {
        case "model", "fallbackModel", "availableModels", "modelSettings", "maxEffortLevel", "effortLevel",
             "alwaysThinkingEnabled", "modelPicker", "outputStyle": "Model and responses"
        case "permissions", "autoMode", "additionalDirectories": "Permissions"
        case "env": "Environment"
        case "hooks", "disableAllHooks": "Hooks"
        case "enabledPlugins", "extraKnownMarketplaces", "pluginConfigs", "appendPlugins", "skillOverrides",
             "strictKnownMarketplaces": "Plugins"
        case "enableAllProjectMcpServers", "enabledMcpjsonServers", "disabledMcpjsonServers", "allowedMcpServers",
             "deniedMcpServers", "disableClaudeAiConnectors", "mcpServers": "MCP approval"
        case "claudeMdExcludes", "autoMemoryEnabled", "memory": "Memory"
        case "includeCoAuthoredBy", "attribution": "Attribution"
        case "sandbox": "Sandbox"
        case "statusLine", "theme", "spinnerTipsEnabled", "spinnerVerbs", "prefersReducedMotion", "enableArtifact": "UI"
        case "cleanupPeriodDays", "desktopSessionCleanupPeriodDays", "syncClaudeAiSkills", "syncClaudeAiPlugins",
             "isolatePeerMachines": "Privacy and history"
        case "autoUpdates", "autoUpdatesChannel": "Updates"
        default: "Other"
        }
    }

    /// The rows for one context, `layers` in any order. Each row's value is masked.
    static func walk(_ layers: [SettingsLayer], machineOnly: Set<String> = []) -> [SettingRow] {
        let sorted = layers.sorted { $0.source < $1.source }
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
                if let why = ignored(path, value: v, source: layer.source) {
                    warnings.append(why)
                    continue
                }
                found.append((layer, v))
            }
            guard let first = found.first else {
                // Set only where it is ignored: shown so the person sees it does nothing.
                if let layer = sorted.first(where: { value(at: path, in: $0.object) != nil }),
                   let v = value(at: path, in: layer.object) {
                    var row = row(path, v, layer, warnings: warnings, machineOnly: machineOnly)
                    row.warnings.insert("Not in effect.", at: 0)
                    rows.append(row)
                }
                continue
            }
            var chosen = first
            var strictWon = false
            var merged: Any = first.1
            var contributors = [first.0.source]
            if let rule = strict[path.joined(separator: ".")], let best = strictest(found, rule) {
                strictWon = best.0.source != first.0.source
                chosen = best
                merged = best.1
                contributors = [best.0.source]
            } else if first.1 is [Any], !overridingLists.contains(path[0]) {
                var list: [Any] = []
                contributors = []
                for (layer, v) in found {
                    guard let a = v as? [Any] else { continue }
                    var added = false
                    for e in a where !list.contains(where: { same($0, e) }) {
                        list.append(e)
                        added = true
                    }
                    if added || contributors.isEmpty { contributors.append(layer.source) }
                }
                merged = list
            }
            var r = row(path, merged, chosen.0, warnings: warnings, machineOnly: machineOnly)
            r.contributors = contributors
            r.strictWon = strictWon
            rows.append(r)
        }
        return rows
    }

    private static func row(_ path: [String], _ value: Any, _ layer: SettingsLayer, warnings: [String],
                            machineOnly: Set<String>) -> SettingRow {
        let shown: SettingRow.Value
        if isBool(value), let b = value as? Bool, !ConfigMask.isSecret(path) {
            shown = .toggle(b)
        } else {
            shown = .text(ConfigMask.display(value, at: path))
        }
        return SettingRow(path: path, value: shown, source: layer.source, file: layer.file,
                          contributors: [layer.source], family: family(path[0]), honored: honored(path),
                          warnings: warnings, machineOnly: machineOnly.contains(path[0]))
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

    private static func strictest(_ found: [(SettingsLayer, Any)], _ rule: Strict) -> (SettingsLayer, Any)? {
        switch rule {
        case .bool(let wins):
            return found.first { IntegrationInstaller.boolValue($0.1) == wins } ?? found.first
        case .order(let order):
            return found.min { a, b in
                (order.firstIndex(of: a.1 as? String ?? "") ?? order.count) < (order.firstIndex(of: b.1 as? String ?? "") ?? order.count)
            }
        }
    }

    private static func same(_ a: Any, _ b: Any) -> Bool {
        (a as? NSObject)?.isEqual(b) ?? false
    }

    // MARK: Lints

    /// Known dead or misplaced config in a seat's own files: `Write(path)` allow rules, a
    /// `settings.local.json` in the seat folder, Windows paths on a Mac, `.claude.json` keys that
    /// belong in settings, and a statusline whose command isn't on this Mac.
    static func lints(settings: [String: Any], config: [String: Any], seat: String, isDefault: Bool,
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
        let misplaced = ["permissions", "hooks", "env"].filter { config[$0] != nil }
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
