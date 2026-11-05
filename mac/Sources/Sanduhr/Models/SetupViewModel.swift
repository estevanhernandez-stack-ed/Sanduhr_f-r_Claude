import Foundation
import Observation

/// Set up Sanduhr's window model (setup spec, section 10): three ticks, the lines a change says,
/// Continue and Not now or Cancel. No AppKit, so it tests on its own. Nothing here draws a name, a
/// path or a usage number: the lines are fixed copy, and the drawers count is a number of files.
@MainActor
@Observable
final class SetupViewModel {
    /// A first run (no setup saved yet): the default ticks, and Not now. Otherwise the ticks show
    /// what is on now, and the dismiss button says Cancel.
    let firstRun: Bool

    var desktop: Bool { didSet { if desktop != oldValue { refresh() } } }
    var drawers: Bool { didSet { if drawers != oldValue { refresh() } } }
    var claudeUsage: Bool {
        didSet {
            guard claudeUsage != oldValue else { return }
            if !settingUsage { usageTouched = true }
            refresh()
        }
    }

    /// What unticking does, said before Continue applies it. Empty on a first run: nothing is on yet.
    private(set) var changeLines: [String] = []
    /// Why Continue is off, or nil.
    private(set) var blocked: String?
    /// What Continue said that is worth reading (the window then shows them and a Done button), or
    /// the save failure (the window stays as it was, with the line).
    private(set) var resultLines: [String] = []
    /// Continue applied and has something to say: the window shows `resultLines` until Done.
    private(set) var said = false
    /// Continue saved the choices. Closing any other way saves nothing.
    private(set) var saved = false

    /// Continue applied the choices (the caller starts what follows: the widget, the tour).
    var onApplied: (SetupResult) -> Void = { _ in }
    /// The window should close: Not now, Cancel, or Continue with nothing to say.
    var onClose: () -> Void = {}
    /// Design it with an AI chat: the window sets it; nil hides the button.
    var onDesignWithChat: (() -> Void)?

    private let service: SetupService
    private let hasAccount: () -> Bool
    private var usageTouched = false
    private var settingUsage = false

    /// `hasAccount`: an account added while the screen was up (a first run) means usage is wanted,
    /// so it is ticked when the window opens, and again at Continue if that tick was never touched.
    init(service: SetupService, firstRun: Bool, hasAccount: @escaping () -> Bool = { false }) {
        self.service = service
        self.hasAccount = hasAccount
        self.firstRun = firstRun
        let now = firstRun ? SetupChoices.firstRun : service.current()
        desktop = now.desktop
        drawers = now.drawers
        claudeUsage = now.claudeUsage || (firstRun && hasAccount())
        refresh()
    }

    var dismissLabel: String { firstRun ? SetupCopy.notNow : SetupCopy.cancel }
    var canContinue: Bool { blocked == nil && !said }
    var choices: SetupChoices { SetupChoices(desktop: desktop, drawers: drawers, claudeUsage: claudeUsage) }

    /// The trademark line shows whenever Claude is named on the screen: usage ticked, or a change
    /// line that says Claude (the add-ons line names Claude Code with usage unticked).
    var showTrademark: Bool { claudeUsage || changeLines.contains { $0.contains("Claude") } }

    private func refresh() {
        blocked = SetupChange.blocked(choices)
        changeLines = firstRun ? [] : service.preview(choices)
        resultLines = []
    }

    func continueTapped() {
        guard canContinue else { return }
        if firstRun, !usageTouched, !claudeUsage, hasAccount() {
            // Signed in while the screen was up: that sign-in said yes to usage.
            settingUsage = true
            claudeUsage = true
            settingUsage = false
        }
        let result = service.apply(choices)
        if result.lines.contains(SetupCopy.saveFailed) {
            // Nothing was saved: stay open with the line, so the person can try again or back out.
            resultLines = result.lines
            return
        }
        saved = true
        onApplied(result)
        let notes = result.lines.filter { $0 != SetupCopy.drawersFirstOnLine }
        if notes.isEmpty {
            onClose()
        } else {
            resultLines = notes
            said = true
        }
    }

    /// Not now, Cancel, the close button and Done after the notes: nothing more is saved.
    func dismiss() { onClose() }
}
