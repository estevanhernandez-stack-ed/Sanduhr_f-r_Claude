import CoreGraphics
import Foundation

/// The frames one Desk window's views reported (onDeskFrame), in the main window's coordinates
/// (top-left origin; another window's are moved by its offset, DeskFrameReporting), so one pointer
/// reading (`pointerInWindow()`) judges every window. A piece on All displays draws on every window
/// and reports into each window's own frames, so every copy takes clicks (2.45). The main window's
/// frames are what the model's frame properties read and write; a Mac with one display has only
/// those, exactly as before 2.45.
struct DeskFrames: Equatable {
    var metersFrame: CGRect = .zero
    var meterRowFrames: [Tier: CGRect] = [:]
    var accountFrame: CGRect = .zero
    var noteFrame: CGRect = .zero
    var meetingsFrame: CGRect = .zero
    var rowFrames: [String: CGRect] = [:]
    var nowPlayingFrame: CGRect = .zero
    var stripFrame: CGRect = .zero
    var stripNextFrame: CGRect = .zero
    var watcherRowFrames: [String: CGRect] = [:]
    var watchersFrame: CGRect = .zero
    var stripWatcherFrame: CGRect = .zero
    var stripAVFrame: CGRect = .zero
    var clockFrame: CGRect = .zero
    var messageFrame: CGRect = .zero
    var claudeLineFrame: CGRect = .zero
    /// Each slanted stack this window draws: its tilt and top left, by anchor, in the same main
    /// window coordinates (DeskFoldStack moves another window's by its offset). Two windows' stacks
    /// at one anchor keep their own (2.45).
    var folds: [DeskAnchor: DeskFold] = [:]

    /// The blocks the close pointer watch looks around (DeskPointerWatch): the meters, the account
    /// name, the calendar note, the meeting list, now playing, the strip's pieces and the watchers,
    /// and the clock, the message and the claude line while they take clicks.
    func watchedBlocks(piecesTakeClicks: Bool) -> [CGRect] {
        [metersFrame, accountFrame, noteFrame, meetingsFrame, nowPlayingFrame, stripFrame, stripNextFrame,
         watchersFrame, stripWatcherFrame, stripAVFrame]
            + (piecesTakeClicks ? [clockFrame, messageFrame, claudeLineFrame] : [])
    }
}

extension DeskElements.Input {
    /// The input with one window's frames in place of whatever it had.
    mutating func take(_ f: DeskFrames) {
        metersFrame = f.metersFrame
        meterRowFrames = f.meterRowFrames
        accountFrame = f.accountFrame
        noteFrame = f.noteFrame
        meetingsFrame = f.meetingsFrame
        rowFrames = f.rowFrames
        nowPlayingFrame = f.nowPlayingFrame
        stripFrame = f.stripFrame
        stripNextFrame = f.stripNextFrame
        watcherRowFrames = f.watcherRowFrames
        stripWatcherFrame = f.stripWatcherFrame
        stripAVFrame = f.stripAVFrame
        clockFrame = f.clockFrame
        messageFrame = f.messageFrame
        claudeLineFrame = f.claudeLineFrame
    }
}

/// Which Desk window's elements: the pieces it draws with the frames it reported (2.45). Pure, from
/// the input the model builds once, the layout and the connected screens; DeskController judges a
/// click against the elements of the window under the pointer and gives each click layer its own.
enum DeskWindowElements {
    /// The key a window's frames are stored under: `.main` for the main window, else its display's
    /// id (the same `.id` Arrange names the window by). A screen without an id is keyed by its frame,
    /// so two such screens never share frames.
    static func key(_ screen: DeskScreenInfo, isMain: Bool) -> DeskDisplay {
        if isMain { return .main }
        return .id(screen.id ?? "frame \(screen.frame.minX),\(screen.frame.minY),\(screen.frame.width),\(screen.frame.height)")
    }

    static func key(_ window: DeskWindowPlan.Window) -> DeskDisplay { key(window.screen, isMain: window.isMain) }

    /// The window a key names among the connected screens; nil for a display no longer connected
    /// (its window is going) and for `.main` before the screens are known.
    static func window(_ key: DeskDisplay, connected: [DeskScreenInfo]) -> DeskWindowPlan.Window? {
        guard let main = DeskScreens.main(connected) else { return nil }
        if key == .main { return DeskWindowPlan.Window(screen: main, isMain: true) }
        return connected.first { $0 != main && self.key($0, isMain: false) == key }
            .map { DeskWindowPlan.Window(screen: $0, isMain: false) }
    }

    /// The input as `window` sees it: only the pieces it draws (DeskWindowPlan.draws), with its own
    /// frames. The main window leaves out just the pieces that draw elsewhere, so a widget the layout
    /// places but the arrangement does not name stays, as before 2.45; another window keeps only the
    /// pieces it draws, and never the strip under the camera, which is the main window's (NotchView).
    /// No window (the screens not known yet): the main window's frames and every piece.
    static func input(_ shared: DeskElements.Input, frames: DeskFrames, arrangement: DeskArrangement,
                      window: DeskWindowPlan.Window?, connected: [DeskScreenInfo]) -> DeskElements.Input {
        var input = shared
        input.take(frames)
        guard let window else { return input }
        let here = Set(arrangement.pieces.filter { DeskWindowPlan.draws($0, on: window, connected: connected) }.map(\.widget))
        if window.isMain {
            let elsewhere = Set(arrangement.pieces.map(\.widget)).subtracting(here)
            input.placed.subtract(elsewhere)
        } else {
            input.placed.formIntersection(here)
            input.nowPlayingStrip = false
            input.nowPlayingStripNext = false
            input.watcherStrip = false
            input.avStrip = false
        }
        // The rows and the now playing line follow their pieces.
        if !input.placed.contains(WatcherPlacement.widget) { input.watcherRows = [] }
        if !input.placed.contains(NowPlayingPlacement.widget) { input.nowPlayingLine = false }
        return input
    }

    /// `window`'s elements, built from `input(...)` and folded with its own stacks' slants (its
    /// frames' folds) over the pieces it draws, so two windows' stacks at one anchor never mix (2.45).
    /// The main window (or no window: the screens not known yet) folds over the main window's pieces
    /// exactly as before 2.45 (DeskModel.mainWindowPieces).
    static func elements(_ shared: DeskElements.Input, frames: DeskFrames, arrangement: DeskArrangement,
                         window: DeskWindowPlan.Window?, connected: [DeskScreenInfo]) -> [DeskElement] {
        let built = DeskElements.build(input(shared, frames: frames, arrangement: arrangement,
                                             window: window, connected: connected))
        let extra = window.map { !$0.isMain } ?? false
        let folding = extra ? pieces(arrangement, on: window, connected: connected)
            : DeskModel.mainWindowPieces(arrangement, screens: connected)
        return DeskElements.folded(built, arrangement: folding, folds: frames.folds)
    }

    /// The meters' frame to put the widget beside (Show Widget): the copy on `preferring`, else the
    /// main window's, else the first window's that has one (by key, so the answer is stable); .zero
    /// when no window draws the meters.
    static func metersFrame(_ windows: [DeskDisplay: DeskFrames], preferring key: DeskDisplay) -> CGRect {
        for k in [key, .main] {
            if let f = windows[k]?.metersFrame, !f.isEmpty { return f }
        }
        let rest = windows.filter { !$0.value.metersFrame.isEmpty }.sorted { ($0.key.suffix ?? "") < ($1.key.suffix ?? "") }
        return rest.first?.value.metersFrame ?? .zero
    }

    /// The pieces `window` draws, for folding its elements with its stacks' slants (DeskElements.folded).
    static func pieces(_ arrangement: DeskArrangement, on window: DeskWindowPlan.Window?,
                       connected: [DeskScreenInfo]) -> DeskArrangement {
        guard let window else { return arrangement }
        var out = arrangement
        out.pieces = arrangement.pieces.filter { DeskWindowPlan.draws($0, on: window, connected: connected) }
        return out
    }
}
