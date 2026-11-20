import Foundation

/// The agents Sanduhr can track (agent sources design, section 1). Not `UsageSource`, which already
/// means the Claude Code activity source on the Usage page. Claude and Codex have limits (widget
/// sections, Primary, Desk meters, setup ticks); Antigravity is activity only: local burn in the
/// Vault, never limits, never Primary. Colors evoke each brand without copying its hex.
enum AgentSource: String, CaseIterable, Hashable {
    case claude, codex, antigravity

    /// The sources that have limits, in widget order.
    static let withLimits: [AgentSource] = allCases.filter(\.hasLimits)

    var displayName: String {
        switch self {
        case .claude: "Claude"
        case .codex: "Codex"
        case .antigravity: "Antigravity"
        }
    }

    /// The footer's short label ("Codex +3K · Agy +1K").
    var shortName: String {
        switch self {
        case .claude: "CC"
        case .codex: "Codex"
        case .antigravity: "Agy"
        }
    }

    var hasLimits: Bool { self != .antigravity }

    /// Default colors, pinned by `SourceContrastTests`. Antigravity's is a periwinkle that evokes a
    /// blue without copying Google's.
    var defaultColor: String {
        switch self {
        case .claude: "#E0875A"
        case .codex: "#2FB5A3"
        case .antigravity: "#7B8CF0"
        }
    }
}

/// Per-source display settings: a color each, Primary, and Antigravity's activity switch, in the app
/// defaults. Whether Claude and Codex are on is the setup's `usage` (one switch, two screens: Set up
/// Sanduhr and the Sources group read and write the same entry), so it is read from there, never
/// copied here.
///
/// Keys (standard defaults): `sources.primary`, `sources.<id>.color` (`#RRGGBB`),
/// `sources.antigravity.on`. A color that is absent or not `#RRGGBB` reads as the default.
struct AgentSourceSettings: Equatable {
    static let primaryKey = "sources.primary"
    static let antigravityOnKey = "sources.antigravity.on"
    static func colorKey(_ source: AgentSource) -> String { "sources.\(source.rawValue).color" }

    /// The saved Primary; `primary(setup:)` is the one the app draws with.
    var savedPrimary: AgentSource = .claude
    var colors: [AgentSource: String] = [:]
    var antigravityOn = false

    static let `default` = AgentSourceSettings()

    /// Whether `text` is a color Sanduhr stores: `#RRGGBB`.
    static func isColor(_ text: String?) -> Bool { ThemeLint.parseHex(text) != nil }

    func color(of source: AgentSource) -> String {
        if let c = colors[source], Self.isColor(c) { return c }
        return source.defaultColor
    }

    /// Claude is on unless the setup says otherwise (no setup reads as on, as `SetupStore.claudeUsageOn`
    /// does); Codex is off unless the setup ticked it; Antigravity follows its own switch.
    func isOn(_ source: AgentSource, setup: SetupSettings?) -> Bool {
        switch source {
        case .claude: return setup.map { $0.usage[SetupSettings.claude] ?? true } ?? true
        case .codex: return setup?.codexUsage ?? false
        case .antigravity: return antigravityOn
        }
    }

    /// The saved Primary if it has limits and is on, else the first source with limits that is on,
    /// else Claude (so a Mac with only Claude on, or nothing on, reads as it always did).
    func primary(setup: SetupSettings?) -> AgentSource {
        if savedPrimary.hasLimits, isOn(savedPrimary, setup: setup) { return savedPrimary }
        return AgentSource.withLimits.first { isOn($0, setup: setup) } ?? .claude
    }

    /// Settings with `source` as Primary; nil when it can't lead: Antigravity has no limits, and a
    /// source that is off leads nothing.
    func settingPrimary(_ source: AgentSource, setup: SetupSettings?) -> AgentSourceSettings? {
        guard source.hasLimits, isOn(source, setup: setup) else { return nil }
        var copy = self
        copy.savedPrimary = source
        return copy
    }

    /// Settings with `source`'s color set; nil for a value that is not `#RRGGBB` (the saved one stays).
    func settingColor(_ source: AgentSource, to text: String) -> AgentSourceSettings? {
        let trimmed = text.trimmingCharacters(in: .whitespaces)
        guard Self.isColor(trimmed) else { return nil }
        var copy = self
        copy.colors[source] = trimmed
        return copy
    }

    /// Settings with `source`'s color back at its default.
    func resettingColor(_ source: AgentSource) -> AgentSourceSettings {
        var copy = self
        copy.colors[source] = nil
        return copy
    }

    // MARK: Stored form

    /// Entry by entry: damage in one value never loses the others.
    static func load(from store: DefaultsStore = UserDefaults.standard) -> AgentSourceSettings {
        var s = AgentSourceSettings()
        if let raw = store.object(forKey: primaryKey) as? String, let p = AgentSource(rawValue: raw), p.hasLimits {
            s.savedPrimary = p
        }
        for source in AgentSource.allCases {
            if let c = store.object(forKey: colorKey(source)) as? String, isColor(c) { s.colors[source] = c }
        }
        if let n = store.object(forKey: antigravityOnKey) as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() {
            s.antigravityOn = n.boolValue
        }
        return s
    }

    /// Every key is written (a saved key is a saved choice); a color at its default has no key.
    func save(to store: DefaultsStore = UserDefaults.standard) {
        store.set(savedPrimary.rawValue, forKey: Self.primaryKey)
        store.set(antigravityOn, forKey: Self.antigravityOnKey)
        for source in AgentSource.allCases {
            store.set(colors[source], forKey: Self.colorKey(source))
        }
    }
}

/// The rules for switching sources off.
enum AgentSourceRules {
    /// Keep one on, or turn on the Desk first. Windows' wording (SettingsNames.SourcesKeepOne).
    static let keepOne = "Keep one on, or turn on the Desk first."

    /// Why `source` can't be turned off, or nil when it can. Turning off the last source with limits
    /// while the Desk is off leaves Sanduhr nothing to show, the rule Set up Sanduhr enforces with
    /// "Pick at least one". Antigravity is activity only and is never refused.
    /// - Parameters:
    ///   - isOn: which sources are on now.
    ///   - deskRuns: the Desk runs (desktop or drawers on).
    static func refusalTurningOff(_ source: AgentSource, isOn: (AgentSource) -> Bool, deskRuns: Bool) -> String? {
        guard source.hasLimits, !deskRuns else { return nil }
        let othersOn = AgentSource.withLimits.contains { $0 != source && isOn($0) }
        return othersOn ? nil : keepOne
    }
}

/// Whether Codex is installed on this Mac, from the folder it keeps its files in.
enum CodexInstall {
    static let folderName = ".codex"

    /// `~/.codex` is a folder under `home`, or `CODEX_HOME` (from the app's own environment) names one.
    /// Pure and injectable: tests hand it a temp home and an environment.
    static func isInstalled(home: URL, environment: [String: String] = [:],
                            fileManager: FileManager = .default) -> Bool {
        var candidates = [home.appendingPathComponent(folderName, isDirectory: true)]
        if let custom = environment["CODEX_HOME"], !custom.isEmpty {
            candidates.append(URL(fileURLWithPath: custom, isDirectory: true))
        }
        return candidates.contains { url in
            var isDir: ObjCBool = false
            return fileManager.fileExists(atPath: url.path, isDirectory: &isDir) && isDir.boolValue
        }
    }
}
