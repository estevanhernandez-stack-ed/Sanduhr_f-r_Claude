import Foundation

/// The words of the motion review (spec 2.39.0 section 19), beside `WidgetReviewCopy`.
enum SpriteMotionReviewCopy {
    static let barName = "Review Claude's sprite motion"
    static let changesTitle = "What changes"
    static let unchangedArt = "The art is not touched, only how it moves. Applies on this Mac only. Nothing is sent anywhere."
    static let previewOnDesk = WidgetReviewCopy.previewOnDesk
    static let endPreview = WidgetReviewCopy.endPreview
    static let apply = WidgetReviewCopy.apply
    static let decline = "Decline"
    static let gone = "This sprite is not installed any more, so the motion was not applied."

    static func heading(_ name: String) -> String { "Claude suggests a motion · \(name)" }
}

/// What the motion banner shows for the pending motion.
struct SpriteMotionReviewModel: Equatable {
    let id: String
    let spriteId: String
    let name: String
    let heading: String
    let note: String?
    let changesTitle: String
    let changes: [String]
    let deskOff: Bool
    let previewing: Bool

    var previewHint: String? { deskOff && !previewing ? WidgetReviewCopy.deskOff : nil }
}

/// The pure half of the review: the short summary of what a motion changes, the patch bump and the apply itself.
enum SpriteMotionReview {
    /// The lines under "What changes": fps and scale (old to new when they differ), then the animations' lengths.
    /// `old` is the installed `sprite.json` (nil when it can't be read), `new` the merged one.
    static func changes(old: Data?, new: Data) -> [String] {
        guard let after = CompanionSprite.parse(new) else { return ["A new motion for this sprite."] }
        let before = old.flatMap { CompanionSprite.parse($0) }
        var lines: [String] = []
        lines.append(line("Speed", "\(before?.fps ?? after.fps)", "\(after.fps)", unit: " fps", same: before == nil || before?.fps == after.fps))
        lines.append(line("Size", "\(before?.scale ?? after.scale)", "\(after.scale)", unit: "x", same: before == nil || before?.scale == after.scale))
        let names = after.animations.keys.sorted()
        let shown = names.prefix(10).map { name -> String in
            let n = after.animations[name]?.count ?? 0
            return "\(name) \(n) frame\(n == 1 ? "" : "s")"
        }
        var anim = "\(names.count) animation\(names.count == 1 ? "" : "s"): " + shown.joined(separator: ", ")
        if names.count > 10 { anim += ", and \(names.count - 10) more" }
        lines.append(anim)
        return lines
    }

    private static func line(_ label: String, _ was: String, _ now: String, unit: String, same: Bool) -> String {
        same ? "\(label): \(now)\(unit) (as it was)" : "\(label): \(was)\(unit) to \(now)\(unit)"
    }

    /// "1.0.3" becomes "1.0.4". Nil for a version that isn't three whole numbers.
    static func bumpedPatch(_ version: String) -> String? {
        let parts = version.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3, let major = Int(parts[0]), let minor = Int(parts[1]), let patch = Int(parts[2]),
              major >= 0, minor >= 0, patch >= 0, patch < Int.max else { return nil }
        return "\(major).\(minor).\(patch + 1)"
    }

    /// Apply: the installed pack read again now (the user may have changed it since the proposal came), the motion's
    /// fields merged into its `sprite.json`, the patch version bumped in `pack.json`, the whole pack validated strict
    /// and reinstalled as the user's own (`.local`). The art files are the installed bytes, never rewritten.
    static func apply(spriteId: String, motion: PackObject, store: SpriteMotionStoring) -> WidgetAnswer {
        func failed(_ why: String) -> WidgetAnswer {
            WidgetAnswer(WidgetAnswer.rejected, reason: "install_failed", reasons: [DeskReason("sprite", why)], packId: spriteId)
        }
        guard store.isMadeHere(spriteId), var files = store.files(spriteId) else { return failed(SpriteMotionReviewCopy.gone) }
        guard let spriteData = files["sprite.json"], var sprite = object(spriteData),
              let packData = files["pack.json"], var manifest = object(packData) else {
            return failed("This sprite's files can't be read, so the motion was not applied.")
        }
        for key in SpriteMotionProposal.fields {
            if let v = motion[key] { sprite = WidgetJson.setting(sprite, key, v) }
        }
        guard let version = manifest["version"]?.string, let bumped = bumpedPatch(version) else {
            return failed("This sprite's version isn't a number like 1.0.0, so the motion was not applied.")
        }
        manifest = WidgetJson.setting(manifest, "version", .string(bumped))
        files["sprite.json"] = Data((WidgetJson.indented(.object(sprite)) + "\n").utf8)
        files["pack.json"] = Data((WidgetJson.indented(.object(manifest)) + "\n").utf8)
        let verdict = PackValidator.validate(files, folderName: spriteId, strict: true)
        guard verdict.ok, let checked = verdict.manifest else {
            return failed("The motion no longer fits this sprite: " + (verdict.errors.first ?? "it did not pass the check") + " Nothing was changed.")
        }
        let result = store.installLocal(files, checked)
        guard result.ok else { return failed(result.error ?? "could not be installed") }
        return WidgetAnswer(WidgetAnswer.applied, packId: spriteId)
    }

    private static func object(_ data: Data) -> PackObject? {
        guard let scalars = PackJson.decodeUTF8([UInt8](data)), case .success(let v) = PackJson.parse(scalars) else { return nil }
        return v.object
    }
}

/// The store as the motion review needs it: read a made-here sprite, put it back. A seam so the decision logic
/// runs against a fake.
protocol SpriteMotionStoring {
    func isMadeHere(_ id: String) -> Bool
    func files(_ id: String) -> [String: Data]?
    func installLocal(_ files: [String: Data], _ manifest: PackManifest) -> WidgetStoreResult
}

extension WidgetStore: SpriteMotionStoring {
    func isMadeHere(_ id: String) -> Bool { load().find(id)?.madeHere ?? false }
    func files(_ id: String) -> [String: Data]? { readFiles(id) }
    func installLocal(_ files: [String: Data], _ manifest: PackManifest) -> WidgetStoreResult {
        install(files, manifest, source: .local, replaceMadeHere: true)
    }
}
