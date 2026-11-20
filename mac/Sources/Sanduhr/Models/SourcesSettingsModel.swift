import Foundation
import Observation

extension Notification.Name {
    /// Posted when a source's color, Primary or Antigravity switch changed: the widget and the menu bar redraw.
    static let sanduhrSourcesDidChange = Notification.Name("sanduhrSourcesDidChange")
}

/// Every string the Sources group shows (Settings, Usage; agent sources design, section 2). Windows'
/// words (`SettingsNames.Sources*`), with "Mac" for "PC".
enum SourcesCopy {
    static let title = "Sources"
    static let intro = "The AI plans Sanduhr tracks. Codex is read from its own logs on this Mac; nothing is sent anywhere."
    static let codexCaption = "Reads ~/.codex. Activity and limits, no sign-in."
    static let antigravityCaption = "Activity only, from the Antigravity CLI. Sanduhr can't see Antigravity's limits or the IDE's usage."
    /// Under a row whose folder is absent or empty.
    static let nothingFound = "Nothing found yet."
    static let primary = "Primary"
    static let primaryGroup = "Primary source"
    static let primaryCaption = "The menu bar and the combined bars' fill follow the primary."
    static let color = "Color"
    static let reset = "Reset"
    static let colorRefused = "Use a color like #2FB5A3."
    /// Nominative use, in the wording of Windows' `AntigravityNotice`; shown where Antigravity is named.
    static let antigravityTrademark = "Antigravity and Gemini are trademarks of Google LLC. Sanduhr is not affiliated with or endorsed by Google."

    static func trackName(_ source: AgentSource) -> String { "Track \(source.displayName)" }
    static func primaryName(_ source: AgentSource) -> String { "\(source.displayName) primary" }
    static func colorName(_ source: AgentSource) -> String { "\(source.displayName) color" }
    static func resetName(_ source: AgentSource) -> String { "Reset \(source.displayName) color" }
}

/// Whether a source has anything on this Mac to read. Pure: they take the home to look in.
enum SourcesProbe {
    /// `~/.codex` (or `CODEX_HOME`) is a folder.
    static func codexFound(home: URL, environment: [String: String] = [:], fileManager: FileManager = .default) -> Bool {
        CodexInstall.isInstalled(home: home, environment: environment, fileManager: fileManager)
    }

    /// `~/.gemini/antigravity-cli` is a folder with something in it. An empty one reads as nothing yet.
    static func antigravityFound(home: URL, fileManager: FileManager = .default) -> Bool {
        let folder = home.appendingPathComponent(".gemini", isDirectory: true)
            .appendingPathComponent("antigravity-cli", isDirectory: true)
        var isDir: ObjCBool = false
        guard fileManager.fileExists(atPath: folder.path, isDirectory: &isDir), isDir.boolValue else { return false }
        return ((try? fileManager.contentsOfDirectory(atPath: folder.path)) ?? []).isEmpty == false
    }
}

enum SourcesColor {
    /// `#RRGGBB` from 0...1 channels (a color well's sRGB), clamped.
    static func hex(red: Double, green: Double, blue: Double) -> String {
        func byte(_ v: Double) -> Int { Int((min(1, max(0, v)) * 255).rounded()) }
        return String(format: "#%02X%02X%02X", byte(red), byte(green), byte(blue))
    }
}

/// One row of the Sources group.
struct SourceRow: Equatable, Identifiable {
    let source: AgentSource
    var id: AgentSource { source }
    var name: String { source.displayName }
    let isOn: Bool
    /// A source with nothing found is greyed; one left on from before can still be turned off.
    let canToggle: Bool
    let color: String
    let isDefaultColor: Bool
    let isPrimary: Bool
    /// Primary is chosen among the sources with limits that are on.
    let canBePrimary: Bool
    let caption: String?
    /// "Nothing found yet." while the source's folder is absent or empty.
    let nothingFound: String?
    let trademark: String?
}

/// Settings, Usage, Sources (agent sources design, section 2). Claude's and Codex's switches are the
/// setup's `usage` (decision D6), written through `SetupService` exactly as Set up Sanduhr's Continue
/// writes them, so the consequences are the same (usage switch, Desk reload, the lines setup says).
/// Antigravity, activity only, has its own key. Colors and Primary are `AgentSourceSettings` keys.
@MainActor
@Observable
final class SourcesSettingsModel {
    private(set) var rows: [SourceRow] = []
    /// What the last change said: a refusal, or the line setup says for what was turned off.
    private(set) var message: String?
    /// A color box's refusal, per source.
    private(set) var colorMessage: [AgentSource: String] = [:]

    /// A change landed in the stored settings: the app redraws the widget and the menu bar.
    @ObservationIgnored var onChanged: () -> Void = {}
    /// A setup change was applied: the app starts or stops what follows (`AppDelegate.afterSetup`).
    @ObservationIgnored var onApplied: (SetupResult) -> Void = { _ in }

    @ObservationIgnored private let service: SetupService
    @ObservationIgnored private let store: DefaultsStore
    @ObservationIgnored private let loadSetup: () -> SetupSettings?
    @ObservationIgnored private let home: URL
    @ObservationIgnored private let environment: [String: String]

    init(service: SetupService, store: DefaultsStore = UserDefaults.standard,
         loadSetup: @escaping () -> SetupSettings? = { SetupStore.load() },
         home: URL = FileManager.default.homeDirectoryForCurrentUser,
         environment: [String: String] = ProcessInfo.processInfo.environment) {
        self.service = service
        self.store = store
        self.loadSetup = loadSetup
        self.home = home
        self.environment = environment
        reload()
    }

    /// Rebuilds the rows from the stored setup, the stored settings and the folders.
    func reload() {
        let setup = loadSetup()
        let settings = AgentSourceSettings.load(from: store)
        let primary = settings.primary(setup: setup)
        let found: (AgentSource) -> Bool = { [home, environment] source in
            switch source {
            case .claude: true
            case .codex: SourcesProbe.codexFound(home: home, environment: environment)
            case .antigravity: SourcesProbe.antigravityFound(home: home)
            }
        }
        rows = AgentSource.allCases.map { source in
            let on = settings.isOn(source, setup: setup)
            let color = settings.color(of: source)
            return SourceRow(
                source: source, isOn: on, canToggle: found(source) || on,
                color: color, isDefaultColor: color.caseInsensitiveCompare(source.defaultColor) == .orderedSame,
                isPrimary: source.hasLimits && source == primary,
                canBePrimary: source.hasLimits && on,
                caption: Self.caption(source), nothingFound: found(source) ? nil : SourcesCopy.nothingFound,
                trademark: Self.trademark(source))
        }
    }

    private static func caption(_ source: AgentSource) -> String? {
        switch source {
        case .claude: nil
        case .codex: SourcesCopy.codexCaption
        case .antigravity: SourcesCopy.antigravityCaption
        }
    }

    private static func trademark(_ source: AgentSource) -> String? {
        switch source {
        case .claude: nil
        case .codex: SetupCopy.codexTrademark
        case .antigravity: SourcesCopy.antigravityTrademark
        }
    }

    // MARK: Switches

    func setOn(_ source: AgentSource, _ on: Bool) {
        message = nil
        switch source {
        case .antigravity:
            let wasOn = AgentSourceSettings.load(from: store).antigravityOn
            store.set(on, forKey: AgentSourceSettings.antigravityOnKey)
            // Switched on again: a record erased while it was on starts again.
            if on, !wasOn { VaultAgentStop.clear(.antigravity, in: store) }
        case .claude, .codex:
            guard applySetup(source, on) else { reload(); return }
        }
        reload()
        onChanged()
    }

    /// Claude and Codex: the refusal first, then `SetupService.apply`, the one path setup uses.
    /// False when nothing was changed (refused or not saved).
    private func applySetup(_ source: AgentSource, _ on: Bool) -> Bool {
        var choices = service.current()
        let setup = loadSetup()
        let settings = AgentSourceSettings.load(from: store)
        // No stored setup reads as Claude on, so a first Codex tick must not turn it off.
        if setup == nil { choices.claudeUsage = true }
        if !on, let refusal = AgentSourceRules.refusalTurningOff(
            source, isOn: { settings.isOn($0, setup: setup) }, deskRuns: choices.deskRuns) {
            message = refusal
            return false
        }
        if source == .claude { choices.claudeUsage = on } else { choices.codexUsage = on }
        // What turning it off does is previewed by setup before Continue ("Your account stays saved"),
        // and said here after the change; what apply itself says follows.
        let said = service.preview(choices)
        let result = service.apply(choices)
        // Nothing changed when the save failed or setup would be left with nothing: say only that.
        let unchanged = result.lines.contains(SetupCopy.saveFailed) || result.lines.contains(SetupCopy.pickOne)
        let lines = (unchanged ? [] : said) + result.lines.filter { $0 != SetupCopy.drawersFirstOnLine }
        message = lines.isEmpty ? nil : lines.joined(separator: " ")
        if unchanged { return false }
        onApplied(result)
        return true
    }

    // MARK: Primary and color

    func setPrimary(_ source: AgentSource) {
        let setup = loadSetup()
        guard let next = AgentSourceSettings.load(from: store).settingPrimary(source, setup: setup) else {
            reload()
            return
        }
        store.set(next.savedPrimary.rawValue, forKey: AgentSourceSettings.primaryKey)
        message = nil
        reload()
        onChanged()
    }

    /// True when the color was saved; false when it was refused (the saved one stays, and the
    /// source's `colorMessage` says why).
    @discardableResult
    func setColor(_ source: AgentSource, _ text: String) -> Bool {
        guard let next = AgentSourceSettings.load(from: store).settingColor(source, to: text),
              let saved = next.colors[source] else {
            colorMessage[source] = SourcesCopy.colorRefused
            reload()
            return false
        }
        colorMessage[source] = nil
        store.set(saved, forKey: AgentSourceSettings.colorKey(source))
        reload()
        onChanged()
        return true
    }

    func resetColor(_ source: AgentSource) {
        colorMessage[source] = nil
        store.set(nil, forKey: AgentSourceSettings.colorKey(source))
        reload()
        onChanged()
    }
}
