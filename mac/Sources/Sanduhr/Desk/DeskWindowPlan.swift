import CoreGraphics
import Foundation

/// One Desk window's screen geometry: the screen it covers, its size, the menu bar strip on top, the
/// camera notch (window coordinates, top-left origin) and the Dock's reach. The main window's lives on
/// `DeskModel` (`deskSize`, `topInset`, `notchRect`, `dockInsets`), where the notch, OLED care and the
/// debug hooks read it; every other window carries its own and hands it to its DeskView (and the
/// companion roams it when placed there, CompanionRoom).
struct DeskScreenGeometry: Equatable {
    var screen: DeskScreenInfo
    var deskSize: CGSize
    var topInset: CGFloat
    var notchRect: CGRect?
    var dockInsets: DockInsets

    /// The geometry for a screen: the size of its frame, the menu bar as the frame's top above the
    /// visible frame's (what `buildWindow` always set), the notch and the Dock as given.
    static func of(_ screen: DeskScreenInfo, visibleFrame: CGRect, notch: CGRect?,
                   dock: DockInsets = DockInsets()) -> DeskScreenGeometry {
        DeskScreenGeometry(screen: screen, deskSize: screen.frame.size,
                           topInset: screen.frame.maxY - visibleFrame.maxY,
                           notchRect: notch, dockInsets: dock)
    }

    /// The geometry for a display besides the main one: its own notch and, when the Dock shows on it,
    /// the Dock's reservation (`DockGeometry.alwaysShown`, read from that screen's frame against its
    /// visible frame, so a screen the Dock is not on gets none). The auto-hiding Dock's slide is
    /// followed on the main display only.
    static func extra(_ screen: DeskScreenInfo, visibleFrame: CGRect, notch: CGRect?,
                      dockPrefs: DockPrefs) -> DeskScreenGeometry {
        of(screen, visibleFrame: visibleFrame, notch: notch,
           dock: DockGeometry.alwaysShown(dockPrefs, frame: screen.frame, visible: visibleFrame))
    }
}

/// The Desk windows to build (spec, Drawing): one on the main display always, and one on each other
/// display some piece draws on. Pure, from the arrangement and the connected screens; DeskController
/// builds what it says and DeskView draws each window's share.
struct DeskWindowPlan: Equatable {
    struct Window: Equatable {
        var screen: DeskScreenInfo
        /// The menu bar display's window: the notch, the wings, the pill, the click roles and the
        /// pieces whose display is not connected.
        var isMain: Bool
    }

    var windows: [Window]

    /// Pieces drawn once, on one window: the companion (its driver is one, CompanionDriver) roams
    /// the window of the display it is placed on, and All displays draws it on the main one (2.45).
    static let drawnOnce: Set<String> = ["companion"]

    /// The display a piece's copies follow: its own, except that a piece drawn once on All
    /// displays is on the main one.
    static func display(of piece: DeskPlacement) -> DeskDisplay {
        drawnOnce.contains(piece.widget) && piece.monitor == .all ? .main : piece.monitor
    }

    /// The one window a piece drawn once draws in, among the connected screens: its display's
    /// window when that display is connected, else (main, All, a display not connected) the main
    /// window. nil before the screens are known.
    static func window(drawing piece: DeskPlacement, connected: [DeskScreenInfo]) -> Window? {
        guard let main = DeskScreens.main(connected) else { return nil }
        let shown = display(of: piece)
        let screen = connected.first { $0 != main && DeskScreens.showsOn(shown, screen: $0, connected: connected) }
        return screen.map { Window(screen: $0, isMain: false) } ?? Window(screen: main, isMain: true)
    }

    /// While arranging (2.44) every connected display with an id has a window, so a piece can be
    /// dragged onto a display that has none yet; Done or Cancel plans from the layout again.
    static func plan(_ arrangement: DeskArrangement, screens: [DeskScreenInfo], arranging: Bool = false) -> DeskWindowPlan {
        let main = DeskScreens.main(screens)
        if arranging, let main {
            let all = screens.filter { $0 == main || $0.id != nil }
            return DeskWindowPlan(windows: all.map { Window(screen: $0, isMain: $0 == main) })
        }
        var spread = arrangement
        spread.pieces = arrangement.pieces.map { p in
            var q = p
            q.monitor = display(of: p)
            return q
        }
        let windows = DeskScreens.screensWithPieces(spread, screens).map { Window(screen: $0, isMain: $0 == main) }
        return DeskWindowPlan(windows: windows)
    }

    var main: Window? { windows.first { $0.isMain } }

    /// The screens of the windows besides the main one: what a layout change compares to decide
    /// whether windows come or go.
    var extraScreens: [DeskScreenInfo] { windows.filter { !$0.isMain }.map(\.screen) }

    /// Whether `window` draws `piece`: where its display shows (DeskScreens.showsOn: All on every
    /// window, a display that is not connected on the main one), a piece drawn once on All on main.
    static func draws(_ piece: DeskPlacement, on window: Window, connected: [DeskScreenInfo]) -> Bool {
        DeskScreens.showsOn(display(of: piece), screen: window.screen, connected: connected)
    }

    /// The stacks `window` draws, by anchor, top to bottom: `displayStacks` filtered to the displays
    /// that show on this window and merged by anchor, in layout order (two displays' `tl` are two
    /// stacks, each on its own window; a display that is not connected joins the main window's).
    static func stacks(_ arrangement: DeskArrangement, on window: Window, connected: [DeskScreenInfo],
                       showMeetings: Bool = true, showClaude: Bool = true) -> [DeskAnchor: [DeskPlacement]] {
        var out: [DeskAnchor: [DeskPlacement]] = [:]
        for p in arrangement.shown(showMeetings: showMeetings, showClaude: showClaude)
        where draws(p, on: window, connected: connected) {
            out[p.anchor, default: []].append(p)
        }
        return out
    }

    /// A window's top-left corner from the main window's top-left corner, in top-left-origin points:
    /// what a frame reported in that window's coordinates moves by to land in the main window's, so
    /// one hit test (DeskHitTest, `pointerInWindow()`) covers every window.
    static func offset(of frame: CGRect, from main: CGRect) -> CGPoint {
        CGPoint(x: frame.minX - main.minX, y: main.maxY - frame.maxY)
    }
}

/// What a DeskView on a window besides the main one needs: its geometry and its offset from the main
/// window (DeskWindowPlan.offset).
struct DeskExtraWindow: Equatable {
    var geometry: DeskScreenGeometry
    var offset: CGPoint

    var plan: DeskWindowPlan.Window { DeskWindowPlan.Window(screen: geometry.screen, isMain: false) }
    /// The key its frames are stored under (DeskWindowElements.key, DeskModel.windowFrames).
    var key: DeskDisplay { DeskWindowElements.key(plan) }
}
