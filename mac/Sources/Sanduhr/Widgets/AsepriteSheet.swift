import Foundation

/// "Import an Aseprite sheet" (spec 2.43.0 section 23): the JSON Aseprite writes beside a sheet (`File > Export Sprite
/// Sheet`, JSON Data, as a hash or an array) read into a cell size, tags and an fps. Pure: JSON bytes in, a plan or one
/// sentence out. No AppKit, no file IO; the sheet's own bytes are read by `AsepriteImport`.
enum AsepriteSheet {
    /// What the JSON says before the sheet's size is known.
    struct Parsed: Equatable {
        var imageName: String
        var metaWidth: Int?
        var cellWidth: Int
        var cellHeight: Int
        /// Each frame's rect origin in file order, in pixels.
        var origins: [(x: Int, y: Int)]
        /// Each frame's duration in ms (100 when the file gives none), in file order.
        var durations: [Int]
        var tags: [Tag]

        static func == (a: Parsed, b: Parsed) -> Bool {
            a.imageName == b.imageName && a.cellWidth == b.cellWidth && a.cellHeight == b.cellHeight
                && a.durations == b.durations && a.tags == b.tags
                && a.origins.map { [$0.x, $0.y] } == b.origins.map { [$0.x, $0.y] }
        }
    }

    struct Tag: Equatable {
        var name: String
        var from: Int
        var to: Int
        var direction: String
    }

    /// The cells to tag and the numbers the sprite takes.
    struct Plan: Equatable {
        var cellWidth: Int
        var cellHeight: Int
        var tags: [String: [Int]]
        /// Tag names that are no action, in file order.
        var ignored: [String]
        var fps: Int
        var frameCount: Int
    }

    static let notAseprite = "That isn't an Aseprite sheet's JSON."
    static let notAGrid = "The frames aren't one grid of same-size cells. Export with Trim and Rotate off."
    static let folderInName = "The sheet's name has a folder in it. Sanduhr reads only a sheet beside the JSON."
    static let defaultDurationMs = 100
    static let maxBytes = 1024 * 1024

    static func missing(_ name: String) -> String { "The sheet \(name) isn't next to the JSON." }

    // MARK: Parse

    /// The JSON's content, or the one sentence why not. Everything that doesn't need the sheet's size is checked here.
    static func parse(_ data: Data) -> Swift.Result<Parsed, AsepriteError> {
        func fail(_ s: String) -> Swift.Result<Parsed, AsepriteError> { .failure(AsepriteError(message: s)) }
        guard data.count <= maxBytes else { return fail(notAseprite) }
        let bytes = data.starts(with: [0xEF, 0xBB, 0xBF]) ? [UInt8](data.dropFirst(3)) : [UInt8](data)
        guard let scalars = PackJson.decodeUTF8(bytes), case .success(let root) = PackJson.parse(scalars),
              let top = root.object, let meta = top["meta"]?.object, let image = meta["image"]?.string,
              let framesNode = top["frames"] else { return fail(notAseprite) }

        var entries: [PackObject] = []
        if let list = framesNode.array {
            for node in list { guard let o = node.object else { return fail(notAseprite) }; entries.append(o) }
        } else if let hash = framesNode.object {
            for key in hash.keys { guard let o = hash[key]?.object else { return fail(notAseprite) }; entries.append(o) }
        } else { return fail(notAseprite) }
        if entries.isEmpty { return fail(notAseprite) }

        var rects: [(x: Int, y: Int, w: Int, h: Int)] = []
        var durations: [Int] = []
        var gridBroken = false
        for entry in entries {
            guard let rect = entry["frame"]?.object, let x = int(rect["x"]), let y = int(rect["y"]),
                  let w = int(rect["w"]), let h = int(rect["h"]), x >= 0, y >= 0, w > 0, h > 0 else { return fail(notAseprite) }
            if entry["rotated"]?.bool == true || entry["trimmed"]?.bool == true { gridBroken = true }
            rects.append((x, y, w, h))
            durations.append(int(entry["duration"]).flatMap { $0 > 0 ? $0 : nil } ?? defaultDurationMs)
        }

        var tags: [Tag] = []
        if let list = meta["frameTags"] {
            guard let nodes = list.array else { return fail(notAseprite) }
            for node in nodes {
                guard let o = node.object, let name = o["name"]?.string, let from = int(o["from"]), let to = int(o["to"]),
                      from >= 0, to >= from, to < rects.count else { return fail(notAseprite) }
                tags.append(Tag(name: name, from: from, to: to, direction: o["direction"]?.string ?? "forward"))
            }
        }

        // The sheet's name is checked before the grid: it is what the caller reads next.
        if image.isEmpty || image == "." || image == ".." || image.contains("/") || image.contains("\\") || image.contains("\0") {
            return fail(folderInName)
        }

        let w = rects[0].w, h = rects[0].h
        if gridBroken || rects.contains(where: { $0.w != w || $0.h != h || $0.x % w != 0 || $0.y % h != 0 }) {
            return fail(notAGrid)
        }
        let range = WidgetNewSprite.cellMin...WidgetNewSprite.cellMax
        if !range.contains(w) || !range.contains(h) {
            return fail("That sheet's frames are \(w)x\(h). A sprite's cells are \(WidgetNewSprite.cellMin) to \(WidgetNewSprite.cellMax) pixels on a side.")
        }
        var metaWidth: Int?
        if let size = meta["size"]?.object { metaWidth = int(size["w"]) }
        return .success(Parsed(imageName: image, metaWidth: metaWidth, cellWidth: w, cellHeight: h,
                               origins: rects.map { ($0.x, $0.y) }, durations: durations, tags: tags))
    }

    // MARK: Plan

    /// The plan for a sheet of `width` x `height` pixels (from the PNG itself): each frame's row-major cell, the
    /// action tags and the fps. Refuses a sheet the frames don't tile.
    static func plan(_ parsed: Parsed, sheetWidth: Int, sheetHeight: Int) -> Swift.Result<Plan, AsepriteError> {
        let w = parsed.cellWidth, h = parsed.cellHeight
        guard sheetWidth > 0, sheetHeight > 0, sheetWidth % w == 0, sheetHeight % h == 0 else {
            return .failure(AsepriteError(message: notAGrid))
        }
        let columns = sheetWidth / w
        var cells: [Int] = []
        for o in parsed.origins {
            guard o.x + w <= sheetWidth, o.y + h <= sheetHeight else { return .failure(AsepriteError(message: notAGrid)) }
            cells.append(o.y / h * columns + o.x / w)
        }

        var tags: [String: [Int]] = [:]
        var ignored: [String] = []
        for tag in parsed.tags {
            let action = tag.name.lowercased()
            guard WidgetNewSprite.actions.contains(action), tags[action] == nil else { ignored.append(tag.name); continue }
            tags[action] = frames(tag).map { cells[$0] }
        }
        if tags.isEmpty {
            tags["walk"] = cells
            tags["sit"] = [cells[0]]
        }
        let fps = SpriteFrames.fps(delays: parsed.durations.map { Double($0) / 1000 })
        return .success(Plan(cellWidth: w, cellHeight: h, tags: tags, ignored: ignored, fps: fps, frameCount: cells.count))
    }

    /// A tag's frame indices in play order, by its direction.
    static func frames(_ tag: Tag) -> [Int] {
        let forward = Array(tag.from...tag.to)
        let inner = tag.to - tag.from >= 2 ? Array((tag.from + 1)..<tag.to) : []
        switch tag.direction {
        case "reverse": return forward.reversed()
        case "pingpong": return forward + inner.reversed()
        case "pingpong_reverse": return forward.reversed() + inner
        default: return forward
        }
    }

    private static func int(_ v: PackValue?) -> Int? {
        guard let n = v?.number, n.value.isFinite, n.value == n.value.rounded(), abs(n.value) < 1_000_000 else { return nil }
        return Int(n.value)
    }
}

struct AsepriteError: Error, Equatable {
    let message: String
}
