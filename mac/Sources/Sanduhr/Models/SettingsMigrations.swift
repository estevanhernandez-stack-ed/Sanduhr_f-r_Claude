import Foundation

/// Settings v2's one-time moves (item 72, slice 2), run at launch before the Desk starts. Each one
/// is idempotent: a second run finds nothing left to move and writes nothing, so an existing
/// install's choices read the same after the update however often it launches.
///
/// - Show the Claude meters on the desktop (`showClaude`) is retired: off, it hides both Claude
///   meters pieces in the layout string, then the key goes. The Desk page's places are its one home.
/// - Option+J and Option+S (`hotKeys`) become two switches, each on where the old one worked
///   (the old switch on and the Desk on).
/// - Camera and mic's two controls become one placement (AVPlace.migrate).
enum SettingsMigrations {
    /// The retired General switch.
    static let showClaudeKey = "showClaude"
    static let layoutKey = "layout"

    /// Runs every move on the Desk suite. Returns whether any wrote something.
    @discardableResult
    static func run(_ desk: DefaultsStore) -> Bool {
        let a = claudeMetersSwitch(desk)
        let b = SanduhrHotKeys.migrate(desk)
        let c = AVPlace.migrate(desk)
        return a || b || c
    }

    /// Show the Claude meters on the desktop, off: both Claude meters pieces set to Hidden, the
    /// rest of the layout (places, order, sizes) as it was. On or off, the key then goes, so the
    /// next run is a no-op and a piece placed again later stays placed.
    @discardableResult
    static func claudeMetersSwitch(_ d: DefaultsStore) -> Bool {
        guard let on = d.object(forKey: showClaudeKey) as? Bool else { return false }
        if !on {
            var a = DeskArrangement(d.object(forKey: layoutKey) as? String ?? DeskLayout.standard)
            a.place("claude", at: nil)
            a.place("meters", at: nil)
            d.set(a.string, forKey: layoutKey)
        }
        d.set(nil, forKey: showClaudeKey)
        return true
    }
}

/// General, Shortcuts (slice 2): Option+J joins the next meeting and Option+S opens Settings, one
/// switch each, both registered whenever Sanduhr runs (not only with the Desk). 2.10.0 had one
/// switch for both (`hotKeys`, on by default) that worked only while the Desk ran, so each new
/// switch starts on only where both held: the old switch on (or never set) and the Desk on. An
/// install with the Desk off had no working shortcut, and starts with both off, so Option+S and
/// Option+J keep typing ß and ∆ there (spec open question 4, decided 2026-10-08). A brand-new
/// install takes the same rule: DeskFirstRun switches the Desk on before the migration runs, so
/// both start on. The old key stays, so an older build reads what it wrote.
///
///   defaults write com.626labs.sanduhr.desk hotKeyJoin -bool false
///   defaults write com.626labs.sanduhr.desk hotKeySettings -bool false
enum SanduhrHotKeys {
    enum Shortcut: CaseIterable {
        case join, settings

        var key: String {
            switch self {
            case .join: "hotKeyJoin"
            case .settings: "hotKeySettings"
            }
        }

        /// The switch's name on General.
        var title: String {
            switch self {
            case .join: "Option+J joins the next meeting"
            case .settings: "Option+S opens Settings"
            }
        }
    }

    /// 2.10.0's one switch for both.
    static let legacyKey = "hotKeys"

    /// General, Shortcuts' caption.
    static let caption = "Both work in every app whenever Sanduhr runs, with or without the Desk. While one is on, Option+S no longer types ß, or Option+J ∆."

    /// The old switch as it read: on unless switched off.
    static func legacy(in d: DefaultsStore) -> Bool { d.object(forKey: legacyKey) as? Bool ?? true }

    /// What each new switch starts as: on where the old shortcuts worked, the old switch on and
    /// the Desk on (desk key `deskEnabled`).
    static func seed(in d: DefaultsStore) -> Bool { legacy(in: d) && d.bool(forKey: DeskController.enabledKey) }

    /// A shortcut's switch; before the migration it reads as its seed.
    static func isOn(_ s: Shortcut, in d: DefaultsStore) -> Bool {
        d.object(forKey: s.key) as? Bool ?? seed(in: d)
    }

    /// Seeds each switch that has no value (seed(in:)). Returns whether it wrote anything; a
    /// second run finds both set and writes nothing, whatever the Desk does after.
    @discardableResult
    static func migrate(_ d: DefaultsStore) -> Bool {
        var changed = false
        let start = seed(in: d)
        for s in Shortcut.allCases where !(d.object(forKey: s.key) is Bool) {
            d.set(start, forKey: s.key)
            changed = true
        }
        return changed
    }
}
