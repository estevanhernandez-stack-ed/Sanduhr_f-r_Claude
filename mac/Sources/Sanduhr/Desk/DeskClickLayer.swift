import AppKit
import CoreGraphics

/// The click layer for "Keep the Desk under desktop icons" (DeskLayer). Under the icons the Desk
/// window sits below Finder's desktop window, which covers the whole screen, so no click could
/// reach it. While that choice is on, a second window takes the Desk's old place just below the
/// app windows: transparent, the Desk's size, and empty but for one faint plate over each small
/// piece that does something on a click (DeskHitTest's areas: meeting rows with a link, the
/// calendar note, the account name, watchers, now playing and the camera strip). The plates give
/// the window server a drawn pixel to deliver the click to (DeskPointerMenu.hitPlateOpacity),
/// and the same monitors and hit test as the Desk window's turn the click into the same action.
/// The big pieces whose click only opens a menu (the meters, the clock, the message, the claude
/// line) get no plate: a desktop full of files has files under them, and those stay the
/// Finder's. So everywhere else the layer lets the mouse through; only a file under a small
/// piece's click area yields it to the piece.
enum DeskClickLayer {
    /// The layer's level: where the Desk sits when it is not under the icons.
    static var level: NSWindow.Level { DeskLayer.level(underIcons: false) }

    /// The kinds the layer serves: the small pieces a click acts on. The meters, the clock, the
    /// message and the claude line (a click there only holds the mouse for a menu) stay with the
    /// files; the account name inside the claude line keeps its click.
    static let kinds: Set<DeskElement.Kind> = [.meetingRow, .note, .account, .watcher, .nowPlaying, .nowPlayingNext, .avIndicators]

    /// The elements the layer answers for: the clickable ones of its kinds. The one list for the
    /// plates, the mouse-through switch and the click, so they can never disagree.
    static func elements(in all: [DeskElement]) -> [DeskElement] {
        all.filter { $0.clickable && kinds.contains($0.kind) }
    }

    /// The plates: one click area (DeskHitTest.area) per element the layer serves that has a
    /// frame. An element without a frame yet takes no click in the Desk window either.
    static func areas(in all: [DeskElement]) -> [CGRect] {
        elements(in: all).filter { $0.fold == nil }.map(DeskHitTest.area).filter { !$0.isNull && !$0.isEmpty }
    }

    /// The plates of elements in a slanted stack (the fold): the same areas, sheared with the stack.
    static func slantedAreas(in all: [DeskElement]) -> [DeskQuad] {
        elements(in: all).filter { $0.fold != nil }.compactMap(DeskHitTest.quad)
    }

    /// True when the layer stands in for the Desk window: under the icons, and not arranging.
    /// Arrange mode (item 60) takes the whole Desk window, which comes up to this level for it.
    static func active(underIcons: Bool, arranging: Bool) -> Bool {
        underIcons && !arranging
    }
}

/// The click layer's content: the plates, drawn in the Desk window's coordinates (top-left
/// origin, as DeskHitTest and `pointerInWindow()` use them), so the areas are set as they come.
final class DeskClickLayerView: NSView {
    var areas: [CGRect] = [] {
        didSet { if areas != oldValue { needsDisplay = true } }
    }

    /// The plates of pieces in a slanted stack (the fold), drawn as the slanted quads they click as.
    var slanted: [DeskQuad] = [] {
        didSet { if slanted != oldValue { needsDisplay = true } }
    }

    override var isFlipped: Bool { true }

    /// Takes the first click instead of spending it on activating Sanduhr, so one click joins
    /// (as FirstClickHostingView does for the Desk window).
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(DeskPointerMenu.hitPlateOpacity).setFill()
        for area in areas where area.intersects(dirtyRect) {
            area.fill()
        }
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        for quad in slanted where quad.bounds.intersects(dirtyRect) {
            ctx.addPath(quad.path)
            ctx.fillPath()
        }
    }
}
