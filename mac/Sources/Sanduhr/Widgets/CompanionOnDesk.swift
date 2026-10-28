import Foundation

/// Whether the companion can show: the Desk is on and the `companion` piece is placed. A sprite
/// installed from Settings, Widgets shows nothing until both hold, so the page says which is missing
/// and links to the row on Settings, Desk where it is set.
enum CompanionOnDesk {
    enum State: Equatable { case on, deskOff, notPlaced }

    static func state(deskOn: Bool, layout: String) -> State {
        if DeskArrangement(layout).placement("companion") == nil { return .notPlaced }
        return deskOn ? .on : .deskOff
    }

    /// The sentence over the link, nil when the companion shows.
    static func prompt(_ state: State, name: String?) -> String? {
        let it = name.map { "\($0) is installed. " } ?? ""
        switch state {
        case .on: return nil
        case .notPlaced: return it + "Place the Companion piece on the Desk to see it."
        case .deskOff: return it + "Turn on the Desk to see your companion."
        }
    }

    /// Where the link lands on Settings, Desk: the Companion's place, or the Desk's switch.
    static func anchor(_ state: State) -> String {
        state == .deskOff ? SettingsAnchor.desk : SettingsAnchor.companionPlace
    }
}
