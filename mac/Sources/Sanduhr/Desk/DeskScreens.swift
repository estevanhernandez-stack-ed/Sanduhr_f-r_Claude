import AppKit
import CoreGraphics
import Foundation

/// One connected display as the Desk sees it: its UUID string (nil when macOS gave none), its frame
/// in macOS screen coordinates (bottom-left origin, y up, the menu bar display at 0,0), whether it
/// is the menu bar display, and the name `DeskScreens.named` gives it.
struct DeskScreenInfo: Equatable {
    var id: String?
    var frame: CGRect
    var primary: Bool
    var name: String = ""
}

/// Displays, their names and where a piece draws (spec, Desk pieces per display). Everything here is
/// pure except `current()`, the adapter that reads `NSScreen`. Ported from Windows'
/// `DeskMonitorNames` and `DeskMonitors.ShowsOn`.
enum DeskScreens {
    /// The main display: the one flagged primary, else the one at 0,0, else the first.
    static func main(_ screens: [DeskScreenInfo]) -> DeskScreenInfo? {
        guard let i = mainIndex(screens) else { return nil }
        return screens[i]
    }

    private static func mainIndex(_ screens: [DeskScreenInfo]) -> Int? {
        if screens.isEmpty { return nil }
        if let i = screens.firstIndex(where: { $0.primary }) { return i }
        if let i = screens.firstIndex(where: { $0.frame.origin == .zero }) { return i }
        return 0
    }

    /// "1920×1080": the size in points, rounded, with the multiplication sign.
    static func size(_ frame: CGRect) -> String {
        "\(Int(frame.width.rounded()))×\(Int(frame.height.rounded()))"
    }

    /// The screens with their names filled in (the input's own names are ignored). The main one is
    /// "W×H, main". Another is named for where it sits against the main one: "right of main",
    /// "left of main", "below main" or "above main" (macOS is y-up, so a frame with a larger y is
    /// above). One that overlaps the main one (mirrored or odd) is named by its position, "at X,Y",
    /// where X is its left edge and Y its top edge in top-left-origin points relative to the main
    /// screen's top-left corner (what Windows reports as left, top, with its primary at 0,0).
    /// Two with the same name get " (2)", " (3)" in screen order: left to right, then top to bottom.
    /// The order of the result is the input's.
    static func named(_ screens: [DeskScreenInfo]) -> [DeskScreenInfo] {
        guard let mi = mainIndex(screens) else { return screens }
        let main = screens[mi].frame
        // Left to right, then top to bottom (a larger maxY is higher on screen), then input order.
        let order = screens.indices.sorted { a, b in
            let fa = screens[a].frame, fb = screens[b].frame
            if fa.minX != fb.minX { return fa.minX < fb.minX }
            if fa.maxY != fb.maxY { return fa.maxY > fb.maxY }
            return a < b
        }
        var names = [Int: String]()
        var used = [String: Int]()
        for i in order {
            let f = screens[i].frame
            let base = i == mi ? "\(size(f)), main" : "\(size(f)), \(whereIs(f, main))"
            used[base, default: 0] += 1
            let n = used[base]!
            names[i] = n == 1 ? base : "\(base) (\(n))"
        }
        return screens.indices.map { i in
            var s = screens[i]
            s.name = names[i] ?? ""
            return s
        }
    }

    private static func whereIs(_ f: CGRect, _ main: CGRect) -> String {
        if f.minX >= main.maxX { return "right of main" }
        if f.maxX <= main.minX { return "left of main" }
        if f.maxY <= main.minY { return "below main" }
        if f.minY >= main.maxY { return "above main" }
        let left = Int((f.minX - main.minX).rounded())
        let top = Int((main.maxY - f.maxY).rounded())
        return "at \(left),\(top)"
    }

    private static func same(_ a: DeskScreenInfo, _ b: DeskScreenInfo) -> Bool {
        a.id == b.id && a.frame == b.frame && a.primary == b.primary
    }

    /// Whether a piece placed on `display` draws on `screen`. All: always. Main: on the main screen.
    /// An id: on the screen with that id (case-insensitive); while no connected screen has that id,
    /// on the main screen, so nothing disappears. Resolution only: the stored layout is never rewritten.
    static func showsOn(_ display: DeskDisplay, screen: DeskScreenInfo, connected: [DeskScreenInfo]) -> Bool {
        switch display {
        case .all:
            return true
        case .main:
            return main(connected).map { same($0, screen) } ?? screen.primary
        case .id(let wanted):
            let isConnected = connected.contains { $0.id?.caseInsensitiveCompare(wanted) == .orderedSame }
            if isConnected {
                return screen.id?.caseInsensitiveCompare(wanted) == .orderedSame
            }
            return main(connected).map { same($0, screen) } ?? screen.primary
        }
    }

    /// The screens that need a Desk window: the main one always, plus each other screen some placed
    /// piece shows on. In the input's order.
    static func screensWithPieces(_ arrangement: DeskArrangement, _ screens: [DeskScreenInfo]) -> [DeskScreenInfo] {
        guard let mi = mainIndex(screens) else { return [] }
        return screens.indices.compactMap { i in
            if i == mi { return screens[i] }
            let shows = arrangement.pieces.contains {
                showsOn($0.monitor, screen: screens[i], connected: screens)
            }
            return shows ? screens[i] : nil
        }
    }

    /// The connected displays, named. The primary one is the menu bar display (`NSScreen.screens.first`),
    /// not `NSScreen.main`, which follows the key window.
    @MainActor static func current() -> [DeskScreenInfo] {
        let screens = NSScreen.screens
        let infos = screens.enumerated().map { i, s in
            DeskScreenInfo(id: id(of: s), frame: s.frame, primary: i == 0)
        }
        return named(infos)
    }

    /// A screen's display UUID string, nil when macOS gives none.
    static func id(of screen: NSScreen) -> String? {
        guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { return nil }
        let displayID = CGDirectDisplayID(number.uint32Value)
        guard let uuid = CGDisplayCreateUUIDFromDisplayID(displayID)?.takeRetainedValue(),
              let text = CFUUIDCreateString(nil, uuid) as String? else { return nil }
        return text
    }
}
