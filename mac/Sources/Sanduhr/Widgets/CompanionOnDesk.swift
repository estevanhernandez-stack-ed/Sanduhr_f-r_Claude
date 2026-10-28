import Foundation

/// Whether the companion can show: the Desk is on and the `companion` piece is placed. A sprite
/// installed from Settings ▸ Widgets shows nothing until both hold, so the page asks for Turn On,
/// which makes both writes.
enum CompanionOnDesk {
    enum State: Equatable { case on, deskOff, notPlaced }

    static func state(deskOn: Bool, layout: String) -> State {
        if DeskArrangement(layout).placement("companion") == nil { return .notPlaced }
        return deskOn ? .on : .deskOff
    }

    /// `layout` with the companion at its standard place; a placed companion stays where it is.
    static func placed(in layout: String) -> String {
        var a = DeskArrangement(layout)
        a.show("companion")
        return a.string
    }

    /// The sentence over Turn On.
    static func prompt(_ state: State, name: String?) -> String? {
        let it = name.map { "\($0) is installed. " } ?? ""
        switch state {
        case .on: return nil
        case .notPlaced: return it + "Turn it on to put your companion on the Desk."
        case .deskOff: return it + "Turn it on to show the Desk, where your companion lives."
        }
    }
}
