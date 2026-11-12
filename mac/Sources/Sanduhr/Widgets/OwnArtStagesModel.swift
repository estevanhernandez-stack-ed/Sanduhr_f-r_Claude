import CoreGraphics
import Foundation
import ImageIO
import Observation

/// "Use my own art..." on a sprite with stages (spec 2.43.0 section 24): one column per stage in growth order, the
/// stage's current first cell, the user's pick and the check's sentence. No AppKit: the view turns the PNG bytes
/// into pictures, and the open panel is only the source of bytes. Pure over the pack's files; nothing is sent.
@MainActor @Observable
final class OwnArtStagesModel {
    /// One stage's column.
    struct Column: Identifiable, Equatable {
        let stage: CompanionStage
        /// The current sheet's first cell as PNG bytes; nil when the sheet can't be read.
        let current: Data?
        /// The picked PNG, if any.
        var picked: Data?
        /// The picked PNG's first cell as PNG bytes.
        var preview: Data?
        /// The check's sentence on the pick; nil when there is no pick or it passed.
        var error: String?

        var id: String { stage.id }
        var name: String { stage.name }
        var growthText: String { "From \(Int((stage.minGrowth * 100).rounded()))% growth" }
        var isValidPick: Bool { picked != nil && error == nil }
    }

    private(set) var columns: [Column]
    private let maxFrame: Int
    private let applyArt: ([String: Data]) -> Bool
    /// True once Apply ran and the reinstall went in.
    private(set) var applied = false

    /// Nil when the files aren't a sprite Sanduhr can read. `apply` is the one install path
    /// (`WidgetsModel.useOwnArt(id:art:)`).
    init?(files: [String: Data], apply: @escaping ([String: Data]) -> Bool) {
        guard let sprite = WidgetOwnArt.sprite(files) else { return nil }
        maxFrame = sprite.maxFrame
        applyArt = apply
        // `CompanionSprite.parse` keeps the file's order, and the validator makes it ascending by `min_growth`;
        // a stable sort holds ties in file order all the same.
        let ordered = sprite.stages.enumerated().sorted {
            $0.element.minGrowth != $1.element.minGrowth ? $0.element.minGrowth < $1.element.minGrowth : $0.offset < $1.offset
        }.map(\.element)
        columns = ordered.map { Column(stage: $0, current: Self.currentFirstCell(files, $0), picked: nil, preview: nil, error: nil) }
    }

    /// A sprite with two or more stages opens the sheet; a one-stage sprite keeps the single panel.
    static func opensSheet(stageCount: Int) -> Bool { stageCount >= 2 }

    /// Keeps `data` for the stage and checks it. An unknown stage is ignored.
    func pick(_ stageId: String, _ data: Data) {
        guard let i = columns.firstIndex(where: { $0.stage.id == stageId }) else { return }
        columns[i].picked = data
        columns[i].error = WidgetOwnArt.checkImage(data, stage: columns[i].stage, maxFrame: maxFrame)
        columns[i].preview = columns[i].error == nil
            ? Self.firstCell(png: data, frameWidth: columns[i].stage.frameWidth, frameHeight: columns[i].stage.frameHeight)
            : nil
    }

    /// Drops the stage's pick, its preview and its sentence.
    func clear(_ stageId: String) {
        guard let i = columns.firstIndex(where: { $0.stage.id == stageId }) else { return }
        columns[i].picked = nil
        columns[i].preview = nil
        columns[i].error = nil
    }

    /// At least one valid pick, and no column holding a failed pick.
    var canApply: Bool {
        columns.contains { $0.isValidPick } && !columns.contains { $0.picked != nil && $0.error != nil }
    }

    /// Stage id to the PNG, for the valid picks only.
    var art: [String: Data] {
        var out: [String: Data] = [:]
        for c in columns where c.isValidPick { out[c.stage.id] = c.picked }
        return out
    }

    /// Hands every valid pick to the install closure once. Nothing runs unless `canApply`.
    @discardableResult
    func apply() -> Bool {
        guard canApply else { return false }
        applied = applyArt(art)
        return applied
    }

    // MARK: First cells

    private static func currentFirstCell(_ files: [String: Data], _ stage: CompanionStage) -> Data? {
        guard let sheet = files[stage.sheet] else { return nil }
        if stage.isPixelSheet {
            guard case .success(let image) = PixelSheet.rasterize(sheet), let png = image.pngData() else { return nil }
            return firstCell(png: png, frameWidth: image.frameWidth, frameHeight: image.frameHeight)
        }
        return firstCell(png: sheet, frameWidth: stage.frameWidth, frameHeight: stage.frameHeight)
    }

    /// The top-left `frameWidth` x `frameHeight` cell of a PNG, as PNG bytes; nil when it can't be decoded or is smaller.
    static func firstCell(png: Data, frameWidth: Int, frameHeight: Int) -> Data? {
        guard let source = CGImageSourceCreateWithData(png as CFData, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil),
              image.width >= frameWidth, image.height >= frameHeight,
              let cell = image.cropping(to: CGRect(x: 0, y: 0, width: frameWidth, height: frameHeight))
        else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out, "public.png" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, cell, nil)
        return CGImageDestinationFinalize(dest) ? out as Data : nil
    }
}
