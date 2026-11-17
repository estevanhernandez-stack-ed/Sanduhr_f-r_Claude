import CoreGraphics
import Foundation

/// One Desk window's screen geometry: the screen it covers, its size, the menu bar strip on top, the
/// camera notch (window coordinates, top-left origin) and the Dock's reach. The main window's lives on
/// `DeskModel` (`deskSize`, `topInset`, `notchRect`, `dockInsets`), where the notch, the companion, OLED
/// care and the debug hooks read it; every other window carries its own and hands it to its DeskView.
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

    /// Pieces that stay on the main display whatever their display says: the companion roams the
    /// main window only (its driver is one, CompanionDriver).
    static let mainOnly: Set<String> = ["companion"]

    /// While arranging (2.44) every connected display with an id has a window, so a piece can be
    /// dragged onto a display that has none yet; Done or Cancel plans from the layout again.
    static func plan(_ arrangement: DeskArrangement, screens: [DeskScreenInfo], arranging: Bool = false) -> DeskWindowPlan {
        let main = DeskScreens.main(screens)
        if arranging, let main {
            let all = screens.filter { $0 == main || $0.id != nil }
            return DeskWindowPlan(windows: all.map { Window(screen: $0, isMain: $0 == main) })
        }
        var spread = arrangement
        spread.pieces = arrangement.pieces.filter { !mainOnly.contains($0.widget) }
        let windows = DeskScreens.screensWithPieces(spread, screens).map { Window(screen: $0, isMain: $0 == main) }
        return DeskWindowPlan(windows: windows)
    }

    var main: Window? { windows.first { $0.isMain } }

    /// The screens of the windows besides the main one: what a layout change compares to decide
    /// whether windows come or go.
    var extraScreens: [DeskScreenInfo] { windows.filter { !$0.isMain }.map(\.screen) }

    /// Whether `window` draws `piece`: where its display shows (DeskScreens.showsOn: All on every
    /// window, a display that is not connected on the main one), the main-only pieces on main.
    static func draws(_ piece: DeskPlacement, on window: Window, connected: [DeskScreenInfo]) -> Bool {
        if mainOnly.contains(piece.widget) { return window.isMain }
        return DeskScreens.showsOn(piece.monitor, screen: window.screen, connected: connected)
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
}
