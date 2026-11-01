import Foundation

/// What `SpriteMotionProposal.decode` made of a request file: a proposal, a refusal with reasons, or nothing to answer.
struct SpriteMotionRequest {
    let kind: WidgetRequestKind
    let id: String?
    let spriteId: String?
    /// The fields Claude sent, as sent (animations, fps, scale, moods, triggers, roam).
    let motion: PackObject?
    let note: String?
    /// The installed pack's files with `sprite.json` carrying the new motion; it passed the pack validator (strict).
    /// The patch version is not bumped and nothing is installed: that is Apply's.
    let files: [String: Data]?
    let reasons: [DeskReason]

    static let discard = SpriteMotionRequest(kind: .discard, id: nil, spriteId: nil, motion: nil, note: nil, files: nil, reasons: [])
}

/// `propose_sprite_motion` (spec 2.39.0 section 19), the app's half of the file: the request the MCP server drops as
/// `sprite-motion-request.json` and the answer in `sprite-motion-result.json`. Claude maps a made-here sprite's motion
/// from its frame size and cell count (`SpriteMotionState`) and sends `sprite.json` fields only. Same statuses, size
/// limit and ten-minute window as `WidgetProposal`.
///
/// `decode` trusts nothing the server checked: the fields are only the six `sprite.json` ones, the id names a sprite
/// made here that is installed (never a catalog pack), and the installed pack with the new fields in its
/// `sprite.json` must pass `PackValidator` in strict mode, which also checks every cell index against the real sheet.
/// Pure apart from reading the installed pack through the store.
enum SpriteMotionProposal {
    static let requestFile = "sprite-motion-request.json"
    static let resultFile = "sprite-motion-result.json"
    static let schemaVersion = 1
    static let maxAge: TimeInterval = WidgetProposal.maxAge
    static let maxRequestBytes = 256 * 1024

    /// The `sprite.json` fields a proposal may carry. Nothing else: no image, no pixels, no sheet, no stages.
    static let fields = ["animations", "fps", "scale", "moods", "triggers", "roam"]
    /// The tables sent whole; fps, scale and roam left out stay as they are.
    static let required = ["animations", "moods", "triggers"]

    /// Reads a request. Unreadable, without an id, or older than `maxAge` is dropped; anything wrong with it is
    /// refused with reasons; a good one is a proposal.
    static func decode(_ data: Data, now: Date, store: WidgetStore) -> SpriteMotionRequest {
        if data.count > maxRequestBytes { return .discard }
        var bytes = [UInt8](data)
        if bytes.starts(with: [0xEF, 0xBB, 0xBF]) { bytes.removeFirst(3) }
        guard let scalars = PackJson.decodeUTF8(bytes), case .success(let root) = PackJson.parse(scalars),
              let o = root.object else { return .discard }
        guard let id = o["id"]?.string, id.utf16.count > 0, id.utf16.count <= 64 else { return .discard }
        guard let stampText = o["requested_at"]?.string, let at = ISO8601.parse(stampText) else {
            return refused(id, [DeskReason("requested_at", "is missing or not a date")])
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard let sv = o["schema_version"]?.number, sv.isInt, sv.value == Double(schemaVersion) else {
            return refused(id, [DeskReason("schema_version", "this Sanduhr reads sprite motion requests of schema 1")])
        }
        var note = o["note"]?.string
        if let n = note, n.utf16.count > 200 { note = String(decoding: Array(n.utf16.prefix(200)), as: UTF16.self) }

        guard let spriteId = o["sprite_id"]?.string, WidgetStore.isSafeId(spriteId),
              let row = store.load().find(spriteId), row.madeHere else {
            return refused(id, [DeskReason("sprite_id", "is not a sprite made here that is installed; a catalog sprite cannot be changed")])
        }
        guard let motion = o["sprite"]?.object, motion.count > 0 else {
            return refused(id, [DeskReason("sprite", "must be an object of sprite.json fields")])
        }
        let foreign = motion.keys.filter { !fields.contains($0) }
        if !foreign.isEmpty {
            let shown = foreign.prefix(5).map { String($0.prefix(24)) }.joined(separator: ", ")
            return refused(id, [DeskReason("sprite", "unknown field(s): \(shown). A motion is animations, fps, scale, moods, triggers and roam; the art is never changed here.")])
        }
        let missing = required.filter { motion[$0] == nil }
        if !missing.isEmpty {
            return refused(id, [DeskReason("sprite", "\(missing.joined(separator: ", ")) must be sent whole")])
        }

        guard var files = store.readFiles(spriteId), let spriteData = files["sprite.json"],
              let spriteScalars = PackJson.decodeUTF8([UInt8](spriteData)),
              case .success(let spriteValue) = PackJson.parse(spriteScalars), var sprite = spriteValue.object else {
            return refused(id, [DeskReason("sprite_id", "this sprite's files can't be read")])
        }
        for key in fields {
            if let v = motion[key] { sprite = WidgetJson.setting(sprite, key, v) }
        }
        files["sprite.json"] = Data((WidgetJson.indented(.object(sprite)) + "\n").utf8)
        let verdict = PackValidator.validate(files, folderName: spriteId, strict: true)
        guard verdict.ok else {
            return refused(id, verdict.errors.prefix(24).map { DeskReason("sprite", $0) })
        }
        return SpriteMotionRequest(kind: .proposal, id: id, spriteId: spriteId, motion: motion, note: note, files: files, reasons: [])
    }

    private static func refused(_ id: String, _ reasons: [DeskReason]) -> SpriteMotionRequest {
        SpriteMotionRequest(kind: .refused, id: id, spriteId: nil, motion: nil, note: nil, files: nil, reasons: reasons)
    }

    /// The result file's contents: the same shape as `widget-result.json`, with the sprite's id as `pack_id`.
    static func resultJson(id: String, answer: WidgetAnswer, now: Date) -> Data {
        WidgetProposal.resultJson(id: id, answer: answer, now: now)
    }
}
