import CoreGraphics
import Foundation
import ImageIO
import Observation

/// A Claude-made sprite drawn from memory: its parsed `sprite.json` and every stage's pixel sheet rasterized with
/// `PixelSheet` (the same call `WidgetStore.install` makes) and decoded from the PNG it encodes, so what the Desk
/// draws is what an installed copy would draw. Nothing is written anywhere: the pack is not in the widgets folder
/// and the saved companion choice is not read or changed.
struct CompanionPreview {
    typealias Sheet = (image: CGImage, width: Int, height: Int)

    let packId: String
    let sprite: CompanionSprite
    private let sheets: [String: Sheet]

    /// The preview of a sprite proposal; nil for another kind, an unreadable `sprite.json`, a stage that is not a pixel
    /// sheet (a Claude-made pack holds JSON only, so none is) or a sheet that does not rasterize.
    static func make(_ pack: WidgetProposalPack) -> CompanionPreview? {
        guard pack.kind == "sprite", let bytes = pack.files["sprite.json"], let sprite = CompanionSprite.parse(bytes) else { return nil }
        var sheets: [String: Sheet] = [:]
        for stage in sprite.stages {
            guard stage.isPixelSheet, let data = pack.files[stage.sheet],
                  case .success(let raster) = PixelSheet.rasterize(data),
                  let png = raster.pngData(),
                  let src = CGImageSourceCreateWithData(png as CFData, nil),
                  let image = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
            sheets[stage.id] = (image, image.width, image.height)
        }
        return CompanionPreview(packId: pack.id, sprite: sprite, sheets: sheets)
    }

    func sheet(for stage: CompanionStage) -> Sheet? { sheets[stage.id] }
}

/// What the Desk shows while a widget proposal is previewed: the sprite as the companion, or the palette as the ink.
/// Never both (a pack is one kind).
struct WidgetDeskPreview {
    var companion: CompanionPreview?
    /// The Desk ink spec (`"ff2a6d,d16ba5,05d9e8"`), as `inkColor` stores it.
    var ink: String?
}

/// The Desk side of a review, behind a seam so the review's tests need no Desk window.
@MainActor
protocol WidgetPreviewHost: AnyObject {
    /// The Desk is on, so a preview would be seen.
    var deskRunning: Bool { get }
    /// The Desk is taken (its layout is being arranged), so a proposal waits: Windows' `DeskBusy`.
    var deskBusy: Bool { get }
    /// Shows `preview` on the Desk, replacing a preview already up.
    func show(_ preview: WidgetDeskPreview)
    /// Puts the Desk back as it was.
    func end()
}

/// The Desk ink while a palette is previewed: `DeskView` and the ink modifier read it before the saved `inkColor`, so
/// the saved choice is never written. Nil at rest. In memory only.
@MainActor
@Observable
final class DeskInkPreview {
    static let shared = DeskInkPreview()
    var spec: String?

    /// The ink to draw: the preview's, else the saved one.
    func resolved(saved: String) -> String { spec ?? saved }
}

/// The running app's side: the sprite goes to the Desk model's companion driver (placed for the preview when the
/// piece is hidden, DeskView adds it), the palette to `DeskInkPreview`.
@MainActor
final class DeskWidgetPreviewHost: WidgetPreviewHost {
    private let controller: DeskController

    init(controller: DeskController = .shared) { self.controller = controller }

    var deskRunning: Bool { controller.running }
    /// Arranging, or a Desk design waits for the user's review (it previews with the same ink).
    var deskBusy: Bool { controller.model.arrange.active || DeskDesignProposalHandoff.shared.pending != nil }

    func show(_ preview: WidgetDeskPreview) {
        // One preview at a time: a Desk design's ink is the same ink.
        DeskDesignProposalHandoff.shared.endPreview()
        let model = controller.model
        model.setWidgetPreview(companion: preview.companion)
        DeskInkPreview.shared.spec = preview.ink
    }

    func end() {
        controller.model.setWidgetPreview(companion: nil)
        DeskInkPreview.shared.spec = nil
    }
}
