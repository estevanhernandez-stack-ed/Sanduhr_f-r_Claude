import Foundation

/// What `WidgetOwnArt.apply` made: the pack's new files and manifest, or the reasons in sentences for Settings.
struct OwnArtResult {
    let files: [String: Data]?
    let manifest: PackManifest?
    let errors: [String]

    var ok: Bool { files != nil && manifest != nil }

    static func failed(_ errors: String...) -> OwnArtResult { OwnArtResult(files: nil, manifest: nil, errors: errors) }
}

/// "Use my own art..." (spec 3.8 section 6), a port of Windows' WidgetOwnArt.cs: the user's own PNG sprite
/// sheets put into a sprite pack made here, one per stage. Each image is checked with the pack image rules
/// first (`checkImage`: a PNG, at most 2 MB and 4096 pixels on a side, a whole number of the stage's cells and
/// a cell for every frame the animations use), then copied into the pack as `art/<stage>.png`, the sheet it
/// replaces is dropped, `pack.json`'s files and patch version follow, and the whole pack is validated again
/// (`PackValidator`, strict: the chunk stream and CRCs included). The caller installs it as a local widget: a
/// user's image makes the pack local-only, and nothing here sends it anywhere.
///
/// Pure: bytes in, bytes and sentences out. Claude never supplies these images; only the user's file picker does.
enum WidgetOwnArt {
    static let artFolder = "art"

    private static let pngSignature: [UInt8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
    private static let ihdr: [UInt8] = Array("IHDR".utf8)

    /// The stages of the pack's sprite (one, `CompanionSprite.singleStageId`, for a single-sheet sprite), or nil
    /// when it is not a sprite pack Sanduhr can read.
    static func sprite(_ files: [String: Data]) -> CompanionSprite? {
        files["sprite.json"].flatMap { CompanionSprite.parse($0) }
    }

    /// The path a stage's own art is copied to.
    static func artPath(_ stageId: String) -> String { "\(artFolder)/\(stageId).png" }

    /// The pack image rules every picked PNG meets, whatever it is for: a PNG, at most 2 MB and 4096 pixels on a
    /// side. The pixel size, or one sentence. The chunk stream and the CRCs are checked again with the whole pack.
    static func imageSize(_ data: Data) -> (width: Int, height: Int, error: String?) {
        let png = [UInt8](data)
        if png.count > PackValidator.maxImageBytes {
            return (0, 0, "That image is larger than 2 MB. A sprite sheet is at most 2 MB.")
        }
        if png.count < 33 || Array(png[0..<8]) != pngSignature || Array(png[12..<16]) != ihdr {
            return (0, 0, "That file isn't a PNG image. A sprite sheet is a PNG.")
        }
        let w = bigEndian32(png, 16), h = bigEndian32(png, 20)
        if w == 0 || h == 0 || w > PackValidator.maxImageSide || h > PackValidator.maxImageSide {
            return (w, h, "That image is \(w)x\(h). A sprite sheet is at most \(PackValidator.maxImageSide) pixels on a side.")
        }
        return (w, h, nil)
    }

    /// The pack image rules on one picked file, before it is copied: nil when it may be used, else one sentence.
    static func checkImage(_ data: Data, stage: CompanionStage, maxFrame: Int) -> String? {
        let size = imageSize(data)
        if let why = size.error { return why }
        let w = size.width, h = size.height
        if w % stage.frameWidth != 0 || h % stage.frameHeight != 0 {
            return "That image is \(w)x\(h), which isn't a whole number of \(stage.frameWidth)x\(stage.frameHeight) cells (the size of \(stage.name)'s frames)."
        }
        let cells = w / stage.frameWidth * (h / stage.frameHeight)
        if maxFrame >= cells {
            return "That sheet has \(cells) cell\(cells == 1 ? "" : "s"), but the animations use frame \(maxFrame), so it needs at least \(maxFrame + 1)."
        }
        return nil
    }

    /// The pack with `art` (stage id to the user's PNG) in place of those stages' sheets, validated as a whole.
    /// `packId` is the installed folder's name. Stages not in `art` keep their sheets.
    static func apply(_ files: [String: Data], packId: String, art: [String: Data]) -> OwnArtResult {
        if art.isEmpty { return .failed("No image was chosen, so the widget is unchanged.") }
        guard let sprite = sprite(files) else { return .failed("This widget isn't a sprite Sanduhr can read.") }
        // The C# walks the dictionary in insertion order; a Swift dictionary has none, so the stages go by id.
        for stageId in WidgetProposal.ordinalSorted(Array(art.keys)) {
            guard let stage = sprite.stages.first(where: { $0.id == stageId }) else { return .failed("The sprite has no stage \(stageId).") }
            if let why = checkImage(art[stageId]!, stage: stage, maxFrame: sprite.maxFrame) { return .failed(stage.name + ": " + why) }
        }

        guard let spriteData = files["sprite.json"], let manifestData = files["pack.json"],
              var spriteDoc = parseObject(spriteData), var manifest = parseObject(manifestData)
        else { return .failed("This widget's files can't be read.") }

        var next = files
        if let stages = spriteDoc["stages"]?.array {
            var updated: [PackValue] = []
            for node in stages {
                if let st = node.object, let sid = st["id"]?.string, let png = art[sid] {
                    updated.append(.object(WidgetJson.setting(st, "sheet", .string(artPath(sid)))))
                    next[artPath(sid)] = png
                } else {
                    updated.append(node)
                }
            }
            spriteDoc = WidgetJson.setting(spriteDoc, "stages", .array(updated))
        } else if let single = art[CompanionSprite.singleStageId] {
            spriteDoc = WidgetJson.setting(spriteDoc, "sheet", .string(artPath("sprite")))
            next[artPath("sprite")] = single
        }

        // The sheets sprite.json names now; a sheet no stage uses any more leaves the pack.
        var referenced: Set<String> = ["sprite.json"]
        if let top = spriteDoc["sheet"]?.string { referenced.insert(top) }
        for node in spriteDoc["stages"]?.array ?? [] {
            if let s = node.object?["sheet"]?.string { referenced.insert(s) }
        }
        let preview = manifest["preview"]?.string
        for rel in Array(next.keys) where rel != "pack.json" && rel != "LICENSE" && rel != preview && !referenced.contains(rel) {
            next.removeValue(forKey: rel)
        }

        let listed = WidgetProposal.ordinalSorted(Array(referenced)).map { PackValue.string($0) }
        manifest = WidgetJson.setting(manifest, "files", .array(listed))
        if let version = manifest["version"]?.string {
            manifest = WidgetJson.setting(manifest, "version", .string(bumpPatch(version)))
        }

        next["sprite.json"] = Data((WidgetJson.indented(.object(spriteDoc)) + "\n").utf8)
        next["pack.json"] = Data((WidgetJson.indented(.object(manifest)) + "\n").utf8)

        let verdict = PackValidator.validate(next, folderName: packId, strict: true)
        if verdict.ok, let m = verdict.manifest { return OwnArtResult(files: next, manifest: m, errors: []) }
        return OwnArtResult(files: nil, manifest: nil, errors: verdict.errors)
    }

    /// "1.0.0" to "1.0.1"; anything else stays as it was (the validator then says why).
    static func bumpPatch(_ version: String) -> String {
        let parts = version.split(separator: ".", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 3, let patch = Int32(parts[2].trimmingCharacters(in: .whitespaces)), patch < Int32.max else { return version }
        return "\(parts[0]).\(parts[1]).\(Int(patch) + 1)"
    }

    private static func bigEndian32(_ b: [UInt8], _ at: Int) -> Int {
        Int(b[at]) << 24 | Int(b[at + 1]) << 16 | Int(b[at + 2]) << 8 | Int(b[at + 3])
    }

    private static func parseObject(_ data: Data) -> PackObject? {
        guard let scalars = PackJson.decodeUTF8([UInt8](data)), case .success(let v) = PackJson.parse(scalars) else { return nil }
        return v.object
    }
}
