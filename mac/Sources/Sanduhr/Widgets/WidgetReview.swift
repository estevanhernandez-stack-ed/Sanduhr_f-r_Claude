import Foundation

/// The words of the widget review (Windows' WidgetReviewCopy, with the Mac's own "on this Mac").
enum WidgetReviewCopy {
    static let barName = "Review Claude's widget"
    static let changesTitle = "What this widget is"
    static let makeCompanion = "Make it the companion"
    static let previewOnDesk = "Preview on the Desk"
    static let localOnly = "Installs on Apply as a widget made here, on this Mac only. Nothing is sent anywhere."
    /// Shown instead of the preview button's effect when the Desk is off (the preview needs it).
    static let deskOff = "Turn the Desk on to preview it."
    /// Effects packs have nothing to draw on the Desk (the companion and the ink are all a preview shows).
    static let noPreview = "Effects packs don't draw yet, so there is nothing to preview."

    static func heading(_ name: String) -> String { "Claude suggests a widget · \(name)" }
}

/// What the review banner shows about the proposal under review: its name, Claude's note, "What this widget is",
/// the files and their license, and which choices it offers. A port of Windows' `DeskReviewModel` as
/// `WidgetReview.Model` builds it.
struct WidgetReviewModel: Equatable {
    let id: String
    let packId: String
    let kind: String
    let name: String
    let heading: String
    /// Claude's note, or the pack's own description when there is none.
    let note: String?
    let changesTitle: String
    /// "What this widget is", then the files line, then `WidgetReviewCopy.localOnly` (Windows' order).
    let changes: [String]
    /// The pack's files, in ordinal order.
    let files: [String]
    let license: String
    /// "Make it the companion" for a sprite; nil for any other kind.
    let option: String?
    /// The Desk is off, so a preview would not be seen (Windows' `DeskOff`).
    let deskOff: Bool
    /// A sprite or a palette can be previewed; an effects pack has nothing to draw.
    let canPreview: Bool
    /// The pending proposal's state: shown on the Desk now.
    let previewing: Bool
}

/// What the review says about a Claude-made pack (spec 3.8 section 5: "What this widget is" and the file list), and what
/// the Desk previews it with. Pure: reads the pack's own JSON, which already passed the validator. A port of Windows'
/// `WidgetReview`.
enum WidgetReview {
    static func model(id: String, pack: WidgetProposalPack, note: String?, deskOn: Bool, previewing: Bool = false) -> WidgetReviewModel {
        var lines = describe(pack)
        let files = WidgetProposal.ordinalSorted(Array(pack.files.keys))
        lines.append("Files: " + files.joined(separator: ", ") + " (\(pack.manifest.license))")
        lines.append(WidgetReviewCopy.localOnly)
        let hasNote = note.map { $0.unicodeScalars.contains { !$0.properties.isWhitespace } } ?? false
        return WidgetReviewModel(
            id: id, packId: pack.id, kind: pack.kind, name: pack.name, heading: WidgetReviewCopy.heading(pack.name),
            note: hasNote ? note : pack.manifest.description, changesTitle: WidgetReviewCopy.changesTitle,
            changes: lines, files: files, license: pack.manifest.license,
            option: pack.kind == "sprite" ? WidgetReviewCopy.makeCompanion : nil,
            deskOff: !deskOn, canPreview: pack.kind != "effects", previewing: previewing)
    }

    /// "What this widget is": the kind and its parts (a sprite's stages, frames and colors; the palettes and their
    /// stops; the effects and their timing).
    static func describe(_ pack: WidgetProposalPack) -> [String] {
        var lines: [String] = []
        switch pack.kind {
        case "sprite": describeSprite(pack, &lines)
        case "palette": describePalettes(pack, &lines)
        case "effects": describeEffects(pack, &lines)
        default: break
        }
        return lines
    }

    private static func describePalettes(_ pack: WidgetProposalPack, _ lines: inout [String]) {
        guard let palettes = json(pack, "palettes.json")?.object?["palettes"]?.array else { return }
        lines.append(palettes.count == 1 ? "A palette for the Desk ink: 1 palette." : "A palette pack for the Desk ink: \(palettes.count) palettes.")
        for p in palettes.compactMap(\.object).prefix(8) {
            let colors = p["colors"]?.array ?? []
            let n = colors.count
            let mood = (p["mood"]?.string).flatMap { $0.isEmpty ? nil : ", \($0)" } ?? ""
            let stops = colors.compactMap(\.string).joined(separator: " ")
            lines.append("\(p["name"]?.string ?? ""): \(n) color\(n == 1 ? "" : "s") (\(stops))\(mood)")
        }
        if palettes.count > 8 { lines.append("and \(palettes.count - 8) more") }
        lines.append("The Desk shows the first palette as its ink while you preview it.")
    }

    private static func describeEffects(_ pack: WidgetProposalPack, _ lines: inout [String]) {
        guard let effects = json(pack, "effects.json")?.object?["effects"]?.array else { return }
        lines.append(effects.count == 1 ? "An effect preset: 1 effect." : "Effect presets: \(effects.count) effects.")
        for e in effects.compactMap(\.object).prefix(8) {
            let intensity = e["intensity"]?.number?.value ?? 0
            let period = (e["period_ms"]?.number?.value ?? 0) / 1000.0
            let color = e["color"]?.string == "ink" ? "the ink" : (e["color"]?.string ?? "")
            lines.append("\(e["name"]?.string ?? ""): \(e["type"]?.string ?? ""), \(String(format: "%.0f", intensity * 100))% intensity, every \(trim(period)) s, in \(color)")
        }
        if effects.count > 8 { lines.append("and \(effects.count - 8) more") }
    }

    /// `0.#`: one decimal at most, none when whole.
    private static func trim(_ v: Double) -> String {
        let s = String(format: "%.1f", v)
        return s.hasSuffix(".0") ? String(s.dropLast(2)) : s
    }

    private static func describeSprite(_ pack: WidgetProposalPack, _ lines: inout [String]) {
        guard let bytes = pack.files["sprite.json"], let sprite = CompanionSprite.parse(bytes) else {
            lines.append("A Desk companion.")
            return
        }
        lines.append(sprite.stages.count == 1
            ? "A Desk companion: 1 stage, \(sprite.animations.count) animations at \(sprite.fps) fps, drawn at \(sprite.scale)x."
            : "A Desk companion: \(sprite.stages.count) stages that grow with your coding days, \(sprite.animations.count) animations at \(sprite.fps) fps, drawn at \(sprite.scale)x.")
        for stage in sprite.stages {
            let label = sprite.stages.count == 1 ? "Art" : stage.name
            if stage.isPixelSheet, let sheet = json(pack, stage.sheet)?.object {
                let frames = sheet["frames"]?.array?.count ?? 0
                let colors = sheet["palette"]?.array?.count ?? 0
                lines.append("\(label): \(frames) frame\(frames == 1 ? "" : "s") of \(stage.frameWidth)x\(stage.frameHeight) pixels, \(colors) color\(colors == 1 ? "" : "s")")
            } else {
                lines.append("\(label): \(stage.frameWidth)x\(stage.frameHeight) pixel frames")
            }
        }
        let edges = sprite.edges.map(\.rawValue).joined(separator: ", ")
        lines.append("Roams the \(edges) edge\(sprite.edges.count == 1 ? "" : "s") at \(String(format: "%.0f", sprite.speedPxPerSecond)) px a second when roaming is on.")
    }

    /// The ink a palette pack previews with: its first palette's stops as the Desk ink spec (`"ff2a6d,d16ba5,05d9e8"`).
    /// Nil for another kind or a pack without one.
    static func previewInk(_ pack: WidgetProposalPack) -> String? {
        guard pack.kind == "palette", let list = json(pack, "palettes.json")?.object?["palettes"]?.array,
              let first = list.compactMap(\.object).first, let colors = first["colors"]?.array else { return nil }
        let hexes = colors.compactMap(\.string).compactMap(PackInkPresets.normalize)
        return hexes.isEmpty ? nil : hexes.joined(separator: ",")
    }

    private static func json(_ pack: WidgetProposalPack, _ rel: String) -> PackValue? {
        guard let bytes = pack.files[rel], let scalars = PackJson.decodeUTF8([UInt8](bytes)),
              case .success(let v) = PackJson.parse(scalars) else { return nil }
        return v
    }
}
