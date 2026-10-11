import Foundation

/// "Just the Desk" (BACKLOG #4, 2026-10-11): Sanduhr for someone without a Claude account. Chosen
/// on the welcome sheet, it uses the Desk on its own: the clock, meetings, the message, now
/// playing and watchers, with no sign-in asked for. The sheet stops coming back, the widget follows
/// its When the widget shows choice instead of staying up for sign-in, and the Desk and notch drop
/// their sign-in line (SignInReminder). The Claude pieces stay placed and draw nothing until there
/// are numbers.
///
/// Saving a key later (Settings, Accounts, or the sheet) ends it at once, before any fetch: a key
/// that then fails gets the usual sign-in flow (the widget up for it, the Desk's sign-in line), and
/// one that works fills the Claude pieces in. The sign-in reminder comes back on if Just the Desk
/// was what turned it off.
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

    /// A session key exists now. Ends Just the Desk and turns the sign-in reminder back on when
    /// Just the Desk had turned it off. True when it was on.
    @discardableResult
    static func endBySignIn(standard: DefaultsStore = UserDefaults.standard, desk: DefaultsStore = UserDefaults.desk) -> Bool {
        guard isOn(in: standard) else { return false }
        if standard.object(forKey: silencedKey) as? Bool == true { SignInReminder.set(true, in: desk) }
        standard.set(nil, forKey: key)
        standard.set(nil, forKey: silencedKey)
        return true
    }

    /// Whether the welcome sheet shows on its own: no session key, Just the Desk not chosen, and
    /// not put off for this launch with Not Now (WelcomeNotNow).
    static func showsWelcome(hasSessionKey: Bool, notNow: Bool = false,
                             in store: DefaultsStore = UserDefaults.standard) -> Bool {
        !hasSessionKey && !notNow && !isOn(in: store)
    }

    /// The widget rule's "has a session key": with Just the Desk on and no key, the widget never
    /// waits for a sign-in and follows its choice as a signed-in Mac's would. Once a key exists it
    /// waits for that key's first fetch like any other (SignInGate).
    static func signInSettled(awaitingSignIn: Bool, hasSessionKey: Bool,
                              in store: DefaultsStore = UserDefaults.standard) -> Bool {
        !awaitingSignIn || (isOn(in: store) && !hasSessionKey)
    }
}
