import Foundation

/// One entry in a piece's Display picker. `tag` is the picker's selection value.
struct DeskDisplayChoice: Equatable, Hashable {
    enum Kind: Equatable, Hashable {
        case main
        case all
        case screen(String)
        case notConnected
    }

    var kind: Kind
    var title: String

    var tag: String {
        switch kind {
        case .main: return "main"
        case .all: return "all"
        case .screen(let id): return "id:" + id
        case .notConnected: return "not-connected"
        }
    }
}

/// One stack in Settings' Order list: a display (nil title when only the main display is in play)
/// and an anchor, with the pieces top to bottom.
struct DeskOrderGroup: Equatable {
    var display: DeskDisplay
    var anchor: DeskAnchor
    /// The display's header, nil on a one-display Desk whose pieces are all on the main display.
    var displayTitle: String?
    var pieces: [DeskPlacement]
}

/// Settings, Desk, Layout: the Display picker's choices and writes, and the Order list's grouping,
/// kept pure so tests drive them without views (spec, Settings).
enum DeskDisplayChoices {
    static let mainTitle = "Main display"
    static let allTitle = "All displays"
    static let notConnectedTitle = "A display that is not connected (on the main display for now)"
    static let notConnectedOrderTitle = "A display that is not connected"

    private static func same(_ a: String?, _ b: String) -> Bool {
        a?.caseInsensitiveCompare(b) == .orderedSame
    }

    private static func isConnected(_ id: String, _ screens: [DeskScreenInfo]) -> Bool {
        screens.contains { same($0.id, id) }
    }

    private static func isMainID(_ id: String, _ screens: [DeskScreenInfo]) -> Bool {
        guard let m = DeskScreens.main(screens) else { return false }
        return same(m.id, id)
    }

    /// The choices for a piece: Main, All, each connected non-main display by name, and, when the
    /// piece sits on an id that is not connected, the stranded choice last. A piece drawn once
    /// (DeskWindowPlan.drawnOnce, the companion) has no All: it lives on one display.
    static func choices(for placement: DeskPlacement, screens: [DeskScreenInfo]) -> [DeskDisplayChoice] {
        var out = [DeskDisplayChoice(kind: .main, title: mainTitle)]
        if !DeskWindowPlan.drawnOnce.contains(placement.widget) {
            out.append(DeskDisplayChoice(kind: .all, title: allTitle))
        }
        let main = DeskScreens.main(screens)
        for s in screens {
            guard let id = s.id, !(main.map { same($0.id, id) } ?? false) else { continue }
            out.append(DeskDisplayChoice(kind: .screen(id), title: s.name))
        }
        if case .id(let id) = placement.monitor, !isConnected(id, screens) {
            out.append(DeskDisplayChoice(kind: .notConnected, title: notConnectedTitle))
        }
        return out
    }

    /// The choice the piece is on. The main display's own id reads as Main; an id nothing
    /// connected carries is the stranded choice; a companion on All (a layout written by hand or a
    /// design) reads as Main, where it draws.
    static func selection(for placement: DeskPlacement, screens: [DeskScreenInfo]) -> DeskDisplayChoice {
        let all = choices(for: placement, screens: screens)
        let kind: DeskDisplayChoice.Kind
        switch placement.monitor {
        case .main: kind = .main
        case .all: kind = .all
        case .id(let id):
            if isMainID(id, screens) { kind = .main }
            else if let s = screens.first(where: { same($0.id, id) }), let sid = s.id { kind = .screen(sid) }
            else { kind = .notConnected }
        }
        return all.first { $0.kind == kind } ?? all[0]
    }

    /// The display a choice writes. Nil for the stranded choice, which writes nothing.
    static func display(for choice: DeskDisplayChoice, screens: [DeskScreenInfo]) -> DeskDisplay? {
        switch choice.kind {
        case .main: return .main
        case .all: return .all
        case .notConnected: return nil
        case .screen(let id): return isMainID(id, screens) ? .main : .id(id)
        }
    }

    /// Applies a pick through `DeskArrangement.setDisplay`.
    static func apply(_ choice: DeskDisplayChoice, widget: String, to arrangement: inout DeskArrangement,
                      screens: [DeskScreenInfo]) {
        guard let d = display(for: choice, screens: screens) else { return }
        arrangement.setDisplay(widget, d)
    }

    /// Whether the Display pickers show: more than one display is connected, or a placed piece is
    /// on an id that is not connected (so it can be brought back).
    static func shows(_ arrangement: DeskArrangement, screens: [DeskScreenInfo]) -> Bool {
        if screens.count > 1 { return true }
        return arrangement.pieces.contains {
            if case .id(let id) = $0.monitor { return !isConnected(id, screens) && !isMainID(id, screens) }
            return false
        }
    }

    /// The Order list's stacks: every placed piece, by display (main, all, each connected display
    /// in screen order, then ids not connected in first-seen order), then by anchor.
    static func orderGroups(_ arrangement: DeskArrangement, screens: [DeskScreenInfo]) -> [DeskOrderGroup] {
        var displays: [DeskDisplay] = [.main, .all]
        let main = DeskScreens.main(screens)
        for s in screens {
            if let id = s.id, !(main.map { same($0.id, id) } ?? false) { displays.append(.id(id)) }
        }
        for p in arrangement.pieces where !displays.contains(p.monitor) {
            if case .id(let id) = p.monitor, isMainID(id, screens) { continue }
            displays.append(p.monitor)
        }
        let many = shows(arrangement, screens: screens)
        var out: [DeskOrderGroup] = []
        for d in displays {
            let sourceDisplays = [d] + arrangement.pieces.compactMap { p -> DeskDisplay? in
                if d == .main, case .id(let id) = p.monitor, isMainID(id, screens) { return p.monitor }
                return nil
            }
            for anchor in DeskAnchor.allCases {
                var pieces: [DeskPlacement] = []
                for sd in Set(sourceDisplays) { pieces += arrangement.stack(anchor, on: sd) }
                pieces = arrangement.pieces.filter { p in pieces.contains { $0.widget == p.widget } }
                if pieces.isEmpty { continue }
                out.append(DeskOrderGroup(display: d, anchor: anchor,
                                          displayTitle: (many || d != .main) ? title(for: d, screens: screens) : nil,
                                          pieces: pieces))
            }
        }
        return out
    }

    private static func title(for display: DeskDisplay, screens: [DeskScreenInfo]) -> String {
        switch display {
        case .main: return mainTitle
        case .all: return allTitle
        case .id(let id):
            return screens.first { same($0.id, id) }?.name ?? notConnectedOrderTitle
        }
    }
}
