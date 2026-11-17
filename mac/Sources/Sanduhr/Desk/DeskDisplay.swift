import Foundation

/// Which display a Desk piece draws on: the main one (the default, and what every layout written
/// before 2.44 meant), every display, or one display by its UUID string. The layout word carries it
/// as an `@<display>` suffix (`DeskArrangement`).
enum DeskDisplay: Equatable, Hashable {
    case main
    case all
    case id(String)

    /// The longest display id a layout word may carry.
    static let maxIdLength = 300

    /// The suffix text after `@`: "all", the id, nil for main.
    var suffix: String? {
        switch self {
        case .main: return nil
        case .all: return "all"
        case .id(let s): return s
        }
    }

    /// The display for the text after `@`. "all" is every display; "main", an empty text and any id
    /// this build refuses (a space, `:` or `@`, or longer than `maxIdLength`) read as main.
    static func parse(_ text: String) -> DeskDisplay {
        if text.isEmpty || text == "main" { return .main }
        if text == "all" { return .all }
        guard text.count <= maxIdLength,
              !text.contains(where: { $0 == " " || $0 == ":" || $0 == "@" || $0.isWhitespace }) else { return .main }
        return .id(text)
    }
}

/// One stack's key: the display and the anchor. Two displays' `tl` stacks are two stacks.
struct DeskStackKey: Hashable {
    var display: DeskDisplay
    var anchor: DeskAnchor
}
