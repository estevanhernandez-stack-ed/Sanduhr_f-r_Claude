import Foundation
import Observation

/// Claude's suggested themes (item 55), the file half. The MCP server's `propose_theme` drops
/// theme-request.json into Sanduhr's Application Support folder; this picks it up (the shared
/// HandoffWatch, the same watch as the Desk messages' handoff), checks it again (ThemeProposal,
/// ThemeLint) and either saves it into the themes folder and applies it when asked, when
/// Settings, Themes' "Let Claude change themes directly" is on, or holds it as the suggestion
/// Settings, Themes shows with a preview card, Save, Save and Apply, and Dismiss. Each step
/// answers in theme-result.json, which the server reads: `pending_approval` at once, then
/// `saved`, `applied` or `rejected` when the user decides.
///
/// A suggestion waits in memory: quitting Sanduhr drops it. A newer request replaces an older one
/// still waiting. The request file is removed as soon as it is read. A built-in's key is refused;
/// a user theme is never overwritten (ThemeProposal.placement). Nothing here logs a theme's name
/// or description.
@MainActor
@Observable
final class ThemeProposalHandoff {
    static let shared = ThemeProposalHandoff()

    /// Settings, Widget, Themes: "Let Claude change themes directly", off by default.
    nonisolated static let directKey = "themeClaudeDirect"

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the request and result files.
        var support: URL
        /// The user themes folder.
        var themes: URL

        static var standard: Paths {
            Paths(support: HandoffFiles.support, themes: UserThemes.appSupportThemesDir())
        }

        var request: URL { support.appendingPathComponent(ThemeProposal.requestFile) }
        var result: URL { support.appendingPathComponent(ThemeProposal.resultFile) }
        func file(_ key: String) -> URL { themes.appendingPathComponent(key + ".json") }
    }

    /// The suggestion waiting for the user, if any.
    private(set) var pending: ThemeProposal?

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let direct: () -> Bool
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private let reserved: Set<String>
    /// The theme in use (the result's previous_key).
    @ObservationIgnored var activeKey: () -> String = { "" }
    /// A theme file was written under this key (the app reloads the registry).
    @ObservationIgnored var onSaved: ((String) -> Void)?
    /// Apply the theme with this key (after `onSaved`).
    @ObservationIgnored var onApply: ((String) -> Void)?
    /// A suggestion arrived and waits (the banner decision is the caller's).
    @ObservationIgnored var onSuggestion: ((ThemeProposal) -> Void)?
    /// A suggestion was saved, dismissed or replaced by a newer one (its notification can go).
    @ObservationIgnored var onDecided: ((ThemeProposal) -> Void)?
    @ObservationIgnored private var watching = false

    init(paths: Paths = .standard,
         direct: @escaping () -> Bool = { UserDefaults.standard.bool(forKey: ThemeProposalHandoff.directKey) },
         now: @escaping () -> Date = Date.init,
         reserved: Set<String> = ThemeProposal.reservedKeys) {
        self.paths = paths
        self.direct = direct
        self.now = now
        self.reserved = reserved
    }

    /// Watches Sanduhr's folder (the shared HandoffWatch) and takes a request already waiting.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    /// Reads and removes a waiting request, if there is one.
    func check() {
        guard let data = HandoffFiles.take(paths.request, maxBytes: ThemeProposal.maxRequestBytes) else { return }
        receive(data)
    }

    /// One request's bytes, as read from the file.
    func receive(_ data: Data) {
        switch ThemeProposal.decode(data, now: now(), reserved: reserved) {
        case .discard:
            return
        case .refused(let id, let outcome):
            writeResult(id: id, outcome)
        case .proposal(let p):
            if direct() {
                save(p, apply: p.apply)
            } else {
                if let old = pending { onDecided?(old) }
                pending = p
                var waiting = ThemeProposal.Outcome(status: .pendingApproval, key: placement(p)?.key, name: p.name,
                                                    findings: p.findings)
                waiting.remedy = "The user decides in Sanduhr's Settings, Themes; the theme is not saved yet."
                writeResult(id: p.id, waiting)
                onSuggestion?(p)
            }
        }
    }

    /// Where the waiting (or a given) theme would be saved, now.
    func placement(_ p: ThemeProposal) -> ThemeProposal.Placement? {
        ThemeProposal.placement(for: p, reserved: reserved) { [paths] key in
            try? Data(contentsOf: paths.file(key))
        }
    }

    /// Save, or Save and Apply: the waiting suggestion goes into the themes folder.
    func approve(apply: Bool) {
        guard let p = pending else { return }
        pending = nil
        onDecided?(p)
        save(p, apply: apply)
    }

    /// Dismiss: the suggestion is dropped and the server's caller hears so.
    func dismiss() {
        guard let p = pending else { return }
        pending = nil
        onDecided?(p)
        writeResult(id: p.id, .refusal("dismissed", "The user dismissed the theme; nothing was saved.", findings: p.findings))
    }

    private func save(_ p: ThemeProposal, apply: Bool) {
        guard let place = placement(p) else {
            writeResult(id: p.id, .refusal("name_taken", "The themes folder has a different theme under '\(p.key)' and its next 98 keys; pass another save_as.", findings: p.findings))
            return
        }
        let url = paths.file(place.key)
        if !place.sameAsExisting {
            do {
                try FileManager.default.createDirectory(at: paths.themes, withIntermediateDirectories: true)
                try p.file.write(to: url, options: .atomic)
            } catch {
                writeResult(id: p.id, ThemeProposal.Outcome(
                    status: .error, reason: "save_failed",
                    remedy: "Could not write the theme file (\(type(of: error))).", findings: p.findings))
                return
            }
        }
        let previous = activeKey()
        onSaved?(place.key)
        if apply { onApply?(place.key) }
        writeResult(id: p.id, ThemeProposal.Outcome(
            status: apply ? .applied : .saved, key: place.key, name: p.name, previousKey: previous,
            savedPath: url.path, renamedFrom: place.renamedFrom, findings: p.findings))
    }

    private func writeResult(id: String, _ outcome: ThemeProposal.Outcome) {
        HandoffFiles.writeOwnerOnly(ThemeProposal.resultJSON(id: id, outcome, now: now()), to: paths.result)
    }
}

extension ThemeProposalHandoff {
    /// The app's wiring: a saved theme reloads the registry (the widget's Theme menu and the
    /// gallery list it at once), Apply goes through `selectTheme(id:)` as a click in the gallery
    /// does, and a suggestion may post a notification (Notifier).
    func startForApp(vm: UsageViewModel) {
        activeKey = { [weak vm] in vm?.theme.id ?? "" }
        onSaved = { [weak vm] _ in
            UserThemes.reload()
            vm?.userThemesTick &+= 1
        }
        onApply = { [weak vm] key in vm?.selectTheme(id: key) }
        onSuggestion = { Notifier.shared.suggestTheme($0) }
        onDecided = { Notifier.shared.clearThemeSuggestion($0) }
        start()
    }
}
