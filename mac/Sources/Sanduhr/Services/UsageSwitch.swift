import Foundation

/// Claude usage on or off (setup spec, section 11). Off stops every Claude fetch, drops the shown
/// numbers and deletes snapshot.json (and, through `pauseSides`, every per-seat `snapshot.<ref>.json`
/// and `seats.json`), so readers find no snapshot: the statusline prints nothing and
/// the MCP's `get_usage` answers no_data (missing). The account, its Keychain item, its history and every setting stay:
/// nothing here signs out, removes or edits an account (that is `UsageViewModel.signOut`, which this
/// type never reaches). On fetches at once when a key exists.
///
/// The on/off bit lives in the stored setup (`SetupStore`): a Mac with no setup yet reads as on, so an
/// upgrade never loses its meters before the migration has run. The effects are closures, so a test
/// drives the whole switch with fakes: no client, no timer, no Keychain, no file.
@MainActor
final class UsageSwitch {
    struct Effects {
        /// Stops the timers, drops the client and the shown numbers.
        var stopFetching: () -> Void = {}
        /// Removes snapshot.json.
        var deleteSnapshot: () -> Void = {}
        var setStatus: (UsageViewModel.StatusMessage) -> Void = { _ in }
        /// A saved session key exists (existence only, never its value).
        var hasKey: () -> Bool = { false }
        /// Builds the client from the saved key, fetches at once and starts the timers. False when
        /// there is no key to build it from.
        var beginFetching: () -> Bool = { false }
        /// Pauses (true) or resumes (false) what fetches on the side: account following and the
        /// per-seat meters, which remove their files while paused and write them again on resume.
        var pauseSides: (Bool) -> Void = { _ in }
        /// Tells the surfaces (menu bar, Desk, notch) to redraw.
        var announce: () -> Void = {}
    }

    private let defaults: DefaultsStore
    var effects: Effects

    init(defaults: DefaultsStore = UserDefaults.standard, effects: Effects = Effects()) {
        self.defaults = defaults
        self.effects = effects
    }

    /// Claude usage is on. No stored setup reads as on.
    var isOn: Bool { SetupStore.claudeUsageOn(from: defaults) }

    /// A launch: true when the normal start goes ahead. Off, nothing fetches and no snapshot is
    /// written; a stale one is removed so a reader never sees numbers Sanduhr stopped updating.
    func launch() -> Bool {
        guard !isOn else { return true }
        hold()
        return false
    }

    /// Turns Claude usage on or off and saves it with the stored setup. `fetchNow` false is for a
    /// caller that connects and fetches right after (a saved key): turning on then saves, resumes
    /// the side fetchers and shows "connecting", and the caller does the rest.
    func set(_ on: Bool, fetchNow: Bool = true) {
        guard on != isOn else { return }
        let before = SetupStore.load(from: defaults)
        let setup = (before ?? SetupChoices.firstRun.toSettings(before: nil)).withClaude(on)
        SetupStore.save(setup, to: defaults)
        enter(on, fetchNow: fetchNow)
    }

    /// The stored setup already says usage is on or off (the setup service saved it with the rest
    /// of the screen's choices): runs what that means, without the early return `set` has for an
    /// unchanged value.
    func applySaved(fetchNow: Bool = true) {
        enter(isOn, fetchNow: fetchNow)
    }

    private func enter(_ on: Bool, fetchNow: Bool) {
        if !on { hold(); return }
        effects.pauseSides(false)
        effects.setStatus(.connecting)
        if fetchNow, !effects.beginFetching() { effects.setStatus(.notSignedIn) }
        effects.announce()
    }

    /// Off: the sides pause, fetching stops, the status reads `usageOff`, the snapshot goes.
    private func hold() {
        effects.pauseSides(true)
        effects.stopFetching()
        effects.setStatus(.usageOff)
        effects.deleteSnapshot()
        effects.announce()
    }
}
