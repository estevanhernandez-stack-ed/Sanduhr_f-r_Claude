import Foundation

/// "Just the Desk" (BACKLOG #4, 2026-10-11): Sanduhr for someone without a Claude account. Chosen
/// on the welcome sheet, it uses the Desk on its own: the clock, meetings, the message, now
/// playing and watchers, with no sign-in asked for. The sheet stops coming back, the widget follows
/// its When the widget shows choice instead of staying up for sign-in, and the Desk and notch drop
/// their sign-in line (SignInReminder). The Claude pieces stay placed and draw nothing until there
/// are numbers.
///
/// Signing in later (Settings, Accounts) ends it at the first successful fetch: the Claude pieces
/// fill in, and the sign-in reminder comes back on if Just the Desk was what turned it off.
enum JustTheDesk {
    /// Standard defaults: chosen and not yet ended by a sign-in.
    static let key = "justTheDesk"
    /// Standard defaults: Just the Desk turned the sign-in reminder off, so a sign-in turns it back
    /// on. Absent when the reminder was already off: that was the person's own choice.
    static let silencedKey = "justTheDeskSilencedReminder"

    static let buttonTitle = "Just the Desk"
    static let caption = "No Claude account? Use the Desk on its own: a clock, your meetings and a message on the desktop. Sign in any time from Settings, Accounts."

    static func isOn(in store: DefaultsStore = UserDefaults.standard) -> Bool {
        store.object(forKey: key) as? Bool ?? false
    }

    /// The welcome sheet's Just the Desk: on, the sign-in reminder off (remembering whether this
    /// turned it off), and the Desk on. The caller hides the sheet and applies the widget's choice.
    static func choose(standard: DefaultsStore = UserDefaults.standard, desk: DefaultsStore = UserDefaults.desk) {
        standard.set(true, forKey: key)
        if SignInReminder.isOn(in: desk) {
            SignInReminder.set(false, in: desk)
            standard.set(true, forKey: silencedKey)
        }
        desk.set(true, forKey: DeskController.enabledKey)
    }

    /// The first successful fetch after signing in. Ends Just the Desk and turns the sign-in
    /// reminder back on when Just the Desk had turned it off. True when it was on.
    @discardableResult
    static func endBySignIn(standard: DefaultsStore = UserDefaults.standard, desk: DefaultsStore = UserDefaults.desk) -> Bool {
        guard isOn(in: standard) else { return false }
        if standard.object(forKey: silencedKey) as? Bool == true { SignInReminder.set(true, in: desk) }
        standard.set(nil, forKey: key)
        standard.set(nil, forKey: silencedKey)
        return true
    }

    /// Whether the welcome sheet shows on its own: no session key, and Just the Desk not chosen.
    static func showsWelcome(hasSessionKey: Bool, in store: DefaultsStore = UserDefaults.standard) -> Bool {
        !hasSessionKey && !isOn(in: store)
    }

    /// The widget rule's "has a session key": with Just the Desk on, the widget never waits for a
    /// sign-in and follows its choice as a signed-in Mac's would.
    static func signInSettled(awaitingSignIn: Bool, in store: DefaultsStore = UserDefaults.standard) -> Bool {
        !awaitingSignIn || isOn(in: store)
    }
}
