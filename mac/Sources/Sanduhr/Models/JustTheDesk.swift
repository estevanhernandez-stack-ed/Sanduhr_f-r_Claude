import Foundation

/// "Just the Desk" (BACKLOG #4, 2.15.0 to 2.40.0): Sanduhr for someone without a Claude account,
/// chosen on the welcome sheet. Set up Sanduhr (2.41.0) folds it in: the screen's "desktop on, usage
/// off" is the same state, and the welcome sheet's button and everything that chose, ended or settled
/// around it are gone. What stays is the reader: the two keys an older install wrote, which the
/// setup migration reads once (`SetupMigration`) to turn an upgrade into a stored setup.
enum JustTheDesk {
    /// Standard defaults: chosen and not yet ended by a sign-in, as an older build left it.
    static let key = "justTheDesk"
    /// Standard defaults: Just the Desk turned the sign-in reminder off, so the migration turns it
    /// back on. Absent when the reminder was already off: that was the person's own choice.
    static let silencedKey = "justTheDeskSilencedReminder"

    static func isOn(in store: DefaultsStore = UserDefaults.standard) -> Bool {
        store.object(forKey: key) as? Bool ?? false
    }
}
