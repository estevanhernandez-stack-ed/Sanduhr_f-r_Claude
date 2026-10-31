import Foundation

/// A sprite made here, as Claude may know it: its id, name, frame size and cell count. Never its image, a pixel, a
/// path or the current animation tables (spec 2.39.0 section 19: "Claude maps the motion, never sees the art").
struct SpriteMotionSprite: Equatable, Sendable {
    let id: String
    let name: String
    let frameWidth: Int
    let frameHeight: Int
    /// Cells in the sheet, row-major from 0; for a sprite with several stages, the fewest any stage has (an index
    /// must be valid on every stage).
    let cells: Int

    /// The entry in `desk-state.json`'s `sprites` list (and so in `get_desk`).
    var json: [String: Any] {
        ["id": id, "name": name, "frame_w": frameWidth, "frame_h": frameHeight, "cells": cells]
    }
}

/// The made-here sprites `desk-state.json` lists for `propose_sprite_motion`. The cell count is worked out here, from
/// the sheet's pixel size and the frame size (a PNG's header is all that is read), so the MCP server never opens an
/// image. Pure apart from reading the widgets folder.
enum SpriteMotionState {
    /// As many sprites as the file lists.
    static let maxSprites = 64
    static let maxNameChars = 40

    /// Every sprite made here (a Claude-made or local pack of kind sprite) that reads as a sprite, by id. A pack whose
    /// files cannot be read, or whose sheet is not a whole number of cells, is left out.
    static func sprites(store: WidgetStore) -> [SpriteMotionSprite] {
        var found: [SpriteMotionSprite] = []
        for row in store.load().packs where row.madeHere && WidgetStore.isSafeId(row.id) {
            guard let sprite = sprite(row.id, store: store) else { continue }
            found.append(sprite)
        }
        found.sort { $0.id < $1.id }
        return Array(found.prefix(maxSprites))
    }

    static func sprite(_ id: String, store: WidgetStore) -> SpriteMotionSprite? {
        guard let info = store.readInfo(id), info.kind == "sprite" else { return nil }
        let dir = store.packDir(id)
        guard let data = try? Data(contentsOf: dir.appendingPathComponent("sprite.json")),
              let sprite = CompanionSprite.parse(data), let first = sprite.stages.first else { return nil }
        var fewest = Int.max
        for stage in sprite.stages {
            guard let n = cells(stage, in: dir) else { return nil }
            fewest = min(fewest, n)
        }
        guard fewest >= 1, fewest < Int.max else { return nil }
        return SpriteMotionSprite(id: id, name: String(info.name.prefix(maxNameChars)),
                                  frameWidth: first.frameWidth, frameHeight: first.frameHeight, cells: fewest)
    }

    /// Cells in one stage's sheet: the frames of a pixel sheet, or the PNG's size over the stage's frame size.
    private static func cells(_ stage: CompanionStage, in dir: URL) -> Int? {
        let url = dir.appendingPathComponent(stage.sheet)
        if stage.isPixelSheet {
            guard let data = try? Data(contentsOf: url), let scalars = PackJson.decodeUTF8([UInt8](data)),
                  case .success(let value) = PackJson.parse(scalars), let frames = value.object?["frames"]?.array else { return nil }
            return frames.count
        }
        // Only the header: the image is not read.
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let head = try? handle.read(upToCount: 33) else { return nil }
        let size = WidgetOwnArt.imageSize(head)
        guard size.error == nil, stage.frameWidth > 0, stage.frameHeight > 0,
              size.width % stage.frameWidth == 0, size.height % stage.frameHeight == 0 else { return nil }
        return size.width / stage.frameWidth * (size.height / stage.frameHeight)
    }
}
