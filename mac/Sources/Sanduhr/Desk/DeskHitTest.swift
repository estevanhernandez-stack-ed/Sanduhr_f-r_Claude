import Foundation
import CoreGraphics

/// One interactive piece of the Desk as the clicks and state.yaml's `desk_frames` see it: its
/// kind, a key (a limit's raw value, a meeting row's index; never a title or a label) and its
/// frame in the Desk window's coordinates (top-left origin, as `pointerInWindow()` gives them).
struct DeskElement: Equatable {
    enum Kind: String, CaseIterable {
        case meetingRow = "meeting_row"
        case note
        case account
        case meterRow = "meter_row"
        case meters
        /// The meeting list as a whole: it holds the rows and the note, and takes no click itself.
        case meetings
    }

    var kind: Kind
    /// The tier's raw value for a meter row, the row's index ("0", "1") for a meeting row, nil
    /// for the rest.
    var key: String?
    var frame: CGRect
    /// Takes a click. A meeting row without a join link is drawn but takes none, and the
    /// meetings block never does.
    var clickable = true
}

/// Which Desk element is under a point (item 41). One pure answer for the mouse-through switch,
/// left clicks and two-finger clicks, so they can never disagree about what the pointer is over.
enum DeskHitTest {
    /// First match wins, in this order: the small targets sit inside or beside the big ones.
    static let priority: [DeskElement.Kind] = [.meetingRow, .note, .account, .meterRow, .meters]

    /// How far past its frame each kind still takes the click, in points (horizontal, vertical):
    /// a little slack, so the gaps between letters and the line above or below still count.
    static func slack(_ kind: DeskElement.Kind) -> CGSize {
        switch kind {
        case .meetingRow, .note, .meterRow: return CGSize(width: 8, height: 4)
        case .account: return CGSize(width: 4, height: 2)
        case .meters, .meetings: return CGSize(width: 8, height: 6)
        }
    }

    /// The element's click area: its frame and the slack. Empty for an empty frame (not drawn,
    /// or its frame never arrived), so it never takes a click.
    static func area(_ element: DeskElement) -> CGRect {
        guard !element.frame.isEmpty else { return .null }
        let s = slack(element.kind)
        return element.frame.insetBy(dx: -s.width, dy: -s.height)
    }

    /// The clickable element under `point`, by `priority`; nil where Desk lets the mouse through.
    static func element(at point: CGPoint, in elements: [DeskElement]) -> DeskElement? {
        for kind in priority {
            if let hit = elements.first(where: { $0.kind == kind && $0.clickable && area($0).contains(point) }) {
                return hit
            }
        }
        return nil
    }

    /// True when the hit is the meters or one of their rows: the two-finger limit menu.
    static func isMeters(_ element: DeskElement?) -> Bool {
        element?.kind == .meters || element?.kind == .meterRow
    }
}

/// What DeskView draws, worked out from the same settings and model state it reads, so a piece
/// that is drawn but never reported its frame shows up as an empty frame instead of not at all.
enum DeskElements {
    /// One meeting row as the elements need it: its id (to find its frame) and whether it has a
    /// join link. Never its title.
    struct Row: Equatable {
        var id: String
        var hasLink: Bool
    }

    /// The model's side: what there is to draw, and the frames the view reported.
    struct Input {
        /// The widgets the layout places (DeskLayout.placed).
        var placed: Set<String> = []
        var meterTiers: [Tier] = []
        var signInNeeded = false
        var switchNote = false
        /// Two or more accounts: the claude line draws the label as its own element.
        var hasAccount = false
        var calendarNote = false
        var rows: [Row] = []
        var metersFrame: CGRect = .zero
        var meterRowFrames: [Tier: CGRect] = [:]
        var accountFrame: CGRect = .zero
        var noteFrame: CGRect = .zero
        var meetingsFrame: CGRect = .zero
        var rowFrames: [String: CGRect] = [:]
    }

    /// Every element DeskView draws, block before its rows, frames as reported (.zero when none
    /// arrived). Meter rows are keyed by tier, meeting rows by index.
    static func build(_ input: Input) -> [DeskElement] {
        var out: [DeskElement] = []
        if input.placed.contains("claude"), input.hasAccount {
            out.append(DeskElement(kind: .account, frame: input.accountFrame))
        }
        // The meters block draws for rows, the sign-in line, or a switch's note that has no
        // claude line to sit on (DeskView.meters).
        let noteOnMeters = input.switchNote && !input.placed.contains("claude")
        if input.placed.contains("meters"), !input.meterTiers.isEmpty || input.signInNeeded || noteOnMeters {
            out.append(DeskElement(kind: .meters, frame: input.metersFrame))
            for tier in input.meterTiers {
                out.append(DeskElement(kind: .meterRow, key: tier.rawValue,
                                       frame: input.meterRowFrames[tier] ?? .zero))
            }
        }
        if input.placed.contains("meetings") {
            out.append(DeskElement(kind: .meetings, frame: input.meetingsFrame, clickable: false))
            if input.calendarNote {
                out.append(DeskElement(kind: .note, frame: input.noteFrame))
            } else {
                for (i, row) in input.rows.enumerated() {
                    out.append(DeskElement(kind: .meetingRow, key: String(i),
                                           frame: input.rowFrames[row.id] ?? .zero, clickable: row.hasLink))
                }
            }
        }
        return out
    }
}

/// Checks the Desk's reported geometry (state.yaml's `desk_frames_ok`): every drawn element has
/// a non-empty frame inside the Desk window, the rows lie within their block, and no two click
/// areas of different kinds overlap. The first problem found is the answer, worded with kinds
/// and keys only.
enum DeskFrameCheck {
    /// Rounding room, in points: SwiftUI frames land on fractions.
    static let tolerance: CGFloat = 0.5

    /// The block a row must lie within.
    static func parent(_ kind: DeskElement.Kind) -> DeskElement.Kind? {
        switch kind {
        case .meterRow: return .meters
        case .meetingRow, .note: return .meetings
        default: return nil
        }
    }

    /// nil when the geometry holds, else the first problem ("meters frame empty").
    static func problem(_ elements: [DeskElement], window: CGSize) -> String? {
        let bounds = CGRect(origin: .zero, size: window).insetBy(dx: -tolerance, dy: -tolerance)
        for e in elements {
            if e.frame.isEmpty { return "\(name(e)) frame empty" }
            if !bounds.contains(e.frame) { return "\(name(e)) off the Desk window" }
        }
        for e in elements {
            guard let p = parent(e.kind), let block = elements.first(where: { $0.kind == p }) else { continue }
            if !block.frame.insetBy(dx: -tolerance, dy: -tolerance).contains(e.frame) {
                return "\(name(e)) outside \(p.rawValue)"
            }
        }
        let clickable = elements.filter(\.clickable)
        for (i, a) in clickable.enumerated() {
            for b in clickable[(i + 1)...] where a.kind != b.kind && !nested(a.kind, b.kind) {
                if overlaps(DeskHitTest.area(a), DeskHitTest.area(b)) {
                    return "\(name(a)) overlaps \(name(b))"
                }
            }
        }
        return nil
    }

    /// A row and its own block share their area by design.
    private static func nested(_ a: DeskElement.Kind, _ b: DeskElement.Kind) -> Bool {
        parent(a) == b || parent(b) == a
    }

    /// More than the tolerance in both directions: click areas that only touch do not overlap.
    static func overlaps(_ a: CGRect, _ b: CGRect) -> Bool {
        let both = a.intersection(b)
        return !both.isNull && both.width > tolerance && both.height > tolerance
    }

    /// "meters", "meter_row five_hour", "meeting_row 0".
    static func name(_ e: DeskElement) -> String {
        e.key.map { "\(e.kind.rawValue) \($0)" } ?? e.kind.rawValue
    }
}

/// When Desk watches the pointer closely (item 42). The window takes the mouse only over a click
/// area (DeskHitTest), and normally learns where the pointer is from mouse-moved events. Those
/// can miss the moment the pointer arrives (the last event fell just short, the layout moved
/// under a still pointer, the frames arrived after launch), and a two-finger click then reaches
/// the Finder. So while the pointer is within `reach` of a Desk block, Desk also checks it on a
/// short timer; anywhere else nothing polls.
enum DeskPointerWatch {
    /// How far around each block the close watch starts, in points.
    static let reach: CGFloat = 48
    /// The close watch's tick: well under the time from arriving to clicking.
    static let interval: TimeInterval = 0.05

    /// True when `point` is within `reach` of any of `frames` (the Desk blocks as reported:
    /// meters, the account name, the calendar note, the meeting list). Empty frames never count.
    static func near(_ point: CGPoint, frames: [CGRect]) -> Bool {
        frames.contains { !$0.isEmpty && $0.insetBy(dx: -reach, dy: -reach).contains(point) }
    }
}
