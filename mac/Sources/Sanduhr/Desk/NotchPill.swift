import Foundation
import CoreGraphics

/// "Top-center island on screens without a notch" (Settings, Desk, Notch; issue #119). A Mac
/// mini, an iMac, an external display or a clamshell MacBook has no camera notch, so the island
/// never draws there: `DeskController.buildWingsWindow` returns before it starts. On, the Desk
/// draws the island's wing text as a pill over the middle of the menu bar instead, as tall as
/// the bar, centered on the screen, with the island's click and two-finger menu. Off by default;
/// a notched Mac keeps its island and never sees the pill.
///
/// The middle of the menu bar is not free space the way the notch is: app menus grow from the
/// left and status items from the right, and on a small screen the two can meet under the pill.
/// Measured on a no-notch Mac 2026-10-19 (`docs/notes/no-notch-measurements.md`): with Xcode's
/// menus and a full row of status items on a 1680-pt screen, the pill cleared the menus by 16 pt
/// and a 0.30 cap is the widest that always clears the status items. So the rule: the pill is
/// never wider than `maxWidthFraction` of the screen, and it hides while the pointer is in the
/// menu bar, so whatever sits under it can always be reached (the hide is what keeps a wide app
/// menu reachable; the cap keeps the status items clear).
/// It also hides while the menu bar is hidden (full screen, auto-hide), when there is no bar to
/// sit on. Main screen only in this cut, like the Desk window itself; every notch-less screen is
/// a later item.
///
///   defaults write com.626labs.sanduhr.desk notchPill -bool true      (turn it on; off by default)
///   defaults write com.626labs.sanduhr.desk notchPillForce -bool true (debug: draw it on a notched Mac too)
enum NotchPill {
    /// Desk suite key (com.626labs.sanduhr.desk).
    static let key = "notchPill"
    static let defaultOn = false

    static let title = "Top-center island on screens without a notch"
    static let caption = "Draws the island's wing text as a pill over the middle of the menu bar on a screen with no camera notch. It hides while the pointer is in the menu bar and while the menu bar is hidden, as in full screen."

    /// The widest the pill gets, as a fraction of the screen's width, so app menus and status
    /// items keep room on either side.
    static let maxWidthFraction: CGFloat = 0.30

    /// Debug key in the same suite: the smoke hook writes it so a notched Mac can show the pill
    /// for the smoke tools. Nothing here writes it.
    static let debugForceKey = "notchPillForce"

    static func isOn(in store: DefaultsStore) -> Bool {
        store.object(forKey: key) as? Bool ?? defaultOn
    }

    static func set(_ on: Bool, in store: DefaultsStore) {
        store.set(on, forKey: key)
    }

    /// True when the debug override asks for the pill regardless of the notch.
    static func forced(in store: DefaultsStore) -> Bool {
        store.object(forKey: debugForceKey) as? Bool ?? false
    }

    /// Whether the Desk builds the pill's window at all (DeskController.buildPillWindow): with the
    /// switch on, on a screen without a notch, or on any screen under the debug override. A notched
    /// Mac without the override never builds one, so nothing there changes.
    static func builds(on: Bool, hasNotch: Bool, forced: Bool) -> Bool {
        on && (!hasNotch || forced)
    }

    /// The pill window's frame in screen (AppKit, bottom-left origin) coordinates: as tall as the
    /// menu bar, at the top of `screen`, centered on it, `contentWidth` wide up to
    /// `maxWidthFraction` of the screen and never narrower than it is tall (a round pill at the
    /// least). Nil while the menu bar is hidden, when `visibleFrame` reaches the top of the screen.
    static func frame(screen: CGRect, visibleFrame: CGRect, contentWidth: CGFloat) -> CGRect? {
        guard visibleFrame.maxY < screen.maxY else { return nil }
        let height = screen.maxY - visibleFrame.maxY
        let width = max(min(contentWidth, screen.width * maxWidthFraction), height)
        return CGRect(x: screen.midX - width / 2, y: visibleFrame.maxY, width: width, height: height)
    }

    /// Whether the pill shows now: only with the switch on, on the main screen, where there is no
    /// notch (the island draws there instead), while the menu bar is visible and the pointer is
    /// not in it.
    static func shows(on: Bool, hasNotch: Bool, isMainScreen: Bool, menuBarVisible: Bool, pointerInMenuBar: Bool) -> Bool {
        on && !hasNotch && isMainScreen && menuBarVisible && !pointerInMenuBar
    }

    /// True when `point` (AppKit coordinates) is in the menu bar: within `barHeight` of the top
    /// of `screen`, the top edge included and the bar's bottom edge excluded, and inside the
    /// screen's width.
    static func pointerInMenuBar(_ point: CGPoint, screen: CGRect, barHeight: CGFloat) -> Bool {
        guard barHeight > 0, point.x >= screen.minX, point.x < screen.maxX else { return false }
        return point.y <= screen.maxY && point.y > screen.maxY - barHeight
    }
}
