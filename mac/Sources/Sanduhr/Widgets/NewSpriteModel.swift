import Foundation
import Observation

/// The state behind the "New sprite from an image..." sheet (spec 2.39.0 section 18): the picked sheet, the cell
/// size, which cells are tagged to which action, the name and the scale. The rules on all of it live in
/// `WidgetNewSprite`; this holds the choices, answers what the sheet shows, and builds the `Input`. Installing goes
/// through `install`, so a test passes a fake and the app passes `WidgetsModel.installNewSprite`. The image is only
/// held here; nothing is sent anywhere.
@MainActor
@Observable
final class NewSpriteModel {
    let png: Data
    /// The sheet's pixel size, or zeros when `imageError` is set.
    let imageWidth: Int
    let imageHeight: Int
    /// One sentence when the picked file fails the pack image rules, nil otherwise.
    let imageError: String?

    private(set) var cellWidth: Int
    private(set) var cellHeight: Int
    /// The action a click on a cell tags.
    var selectedAction = "walk"
    /// Action to its cells, in click order.
    private(set) var tags: [String: [Int]] = [:]
    var name = ""
    var scale = WidgetNewSprite.defaultScale
    /// The reasons the last Make failed, in sentences. Empty before one and after a success.
    private(set) var errors: [String] = []
    /// True once the sprite was installed; the sheet closes on it.
    private(set) var made = false

    /// Installs the made `Input`; returns the sentences why not (empty when it went in).
    private let install: (WidgetNewSprite.Input) -> [String]

    init(png: Data, install: @escaping (WidgetNewSprite.Input) -> [String]) {
        self.png = png
        self.install = install
        let size = WidgetOwnArt.imageSize(png)
        imageWidth = size.width
        imageHeight = size.height
        imageError = size.error
        let start = Self.startingCell(size.width, size.height)
        cellWidth = start
        cellHeight = start
    }

    // MARK: Cells

    /// The cell size the sheet opens at: the largest of 16, 32, 24, 8 that divides both sides, else 16.
    static func startingCell(_ w: Int, _ h: Int) -> Int {
        guard w > 0, h > 0 else { return 16 }
        return [16, 32, 24, 8].first { w % $0 == 0 && h % $0 == 0 } ?? 16
    }

    var columns: Int { imageWidth / max(cellWidth, 1) }
    var rows: Int { imageHeight / max(cellHeight, 1) }
    var cellCount: Int { columns * rows }
    /// Whether the sheet is a whole number of cells at this size. When not, Make says so.
    var gridFits: Bool { imageWidth % max(cellWidth, 1) == 0 && imageHeight % max(cellHeight, 1) == 0 }

    /// Sets the cell size (each side clamped to 8...64). Tags past the new last cell are dropped.
    func setCell(width: Int, height: Int) {
        cellWidth = min(max(width, WidgetNewSprite.cellMin), WidgetNewSprite.cellMax)
        cellHeight = min(max(height, WidgetNewSprite.cellMin), WidgetNewSprite.cellMax)
        dropOutOfRangeTags()
        errors = []
    }

    private func dropOutOfRangeTags() {
        for (action, cells) in tags {
            let kept = cells.filter { $0 >= 0 && $0 < cellCount }
            tags[action] = kept.isEmpty ? nil : kept
        }
    }

    // MARK: Tags

    /// A click on `cell`: tags it to the selected action (at the end of the order), or untags it when it already is.
    func tap(_ cell: Int) {
        guard cell >= 0, cell < cellCount else { return }
        var cells = tags[selectedAction] ?? []
        if let at = cells.firstIndex(of: cell) { cells.remove(at: at) } else { cells.append(cell) }
        tags[selectedAction] = cells.isEmpty ? nil : cells
        errors = []
    }

    func cells(of action: String) -> [Int] { tags[action] ?? [] }

    /// The 1-based place of `cell` in `action`'s order (walk plays in click order), nil when it is not tagged.
    func order(of cell: Int, in action: String) -> Int? {
        cells(of: action).firstIndex(of: cell).map { $0 + 1 }
    }

    /// The actions `cell` is tagged to.
    func actions(of cell: Int) -> [String] { WidgetNewSprite.actions.filter { cells(of: $0).contains(cell) } }

    /// The actions not ready, each with what happens: nap, cheer and worry with no cells play sit; walk (under two
    /// cells) and sit (none) are needed.
    var untagged: [(action: String, note: String)] {
        WidgetNewSprite.actions.compactMap { action in
            guard cells(of: action).count < (action == "walk" ? 2 : 1) else { return nil }
            switch action {
            case "walk": return (action, "needs two or more cells")
            case "sit": return (action, "needs a cell")
            default: return (action, "uses sit")
            }
        }
    }

    // MARK: Make

    var input: WidgetNewSprite.Input {
        WidgetNewSprite.Input(png: png, cellWidth: cellWidth, cellHeight: cellHeight, name: name, scale: scale, tags: tags)
    }

    /// Make: installs through `install`; on failure the sentences are in `errors` and nothing changed.
    func make() {
        errors = install(input)
        made = errors.isEmpty
    }
}
