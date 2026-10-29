import Foundation
import Observation

/// The Desk settings a design can change (spec 8), read from and written to the `.desk` defaults suite: the layout
/// (anchors, sizes, tilt, style), the font, the ink, the plate and the shadow. The Mac subset of Windows'
/// `DeskDesignSettings`: no Launcher, no theme, no monitor, no weather nickname. Margins, sizes of the clock and
/// whether the Desk is on are not part of a design and are never touched.
struct DeskDesignLook: Equatable {
    var arrangement: DeskArrangement
    /// The saved `font` value: nil is the default family, "" the system font, anything else a family name.
    var font: String?
    /// The saved `inkColor`: one to four colors without the `#`, comma separated.
    var ink: String
    var plate: Bool
    var shadow: Bool

    static let defaultInk = "ffffff"

    /// The look the Desk has in `defaults` (the values it draws from when nothing is previewed).
    static func read(_ defaults: DefaultsStore) -> DeskDesignLook {
        DeskDesignLook(arrangement: DeskArrangement(defaults.object(forKey: DeskArrangeMode.layoutKey) as? String ?? DeskLayout.standard),
                 font: defaults.object(forKey: DeskFont.key) as? String,
                 ink: defaults.object(forKey: DeskLook.inkKey) as? String ?? defaultInk,
                 plate: defaults.object(forKey: DeskPlate.key) as? Bool ?? DeskPlate.defaultOn,
                 shadow: defaults.object(forKey: DeskLook.shadowKey) as? Bool ?? true)
    }

    /// The family the Desk draws in for this look (EsteFont Pro for a font never chosen or no longer installed).
    func family(available: (String) -> Bool = DeskFont.isAvailable) -> String {
        DeskFont.resolve(saved: font, available: available)
    }

    /// The ink as stops with `#`, for the review's lines and the state file's shape: unreadable is white.
    var inkWords: [String] {
        let stops = ink.split(separator: ",").compactMap { DeskDesign.hex(String($0)) }.prefix(4)
        return (stops.isEmpty ? [Self.defaultInk] : Array(stops)).map { "#" + $0 }
    }
}

/// Turns a design into the Desk's settings, and says what it changes. Pure: the preview and Apply both go through
/// `merged`, so what is previewed is what is applied (Windows' `DeskDesignApply` and `DeskDesign.Merge/Diff`).
enum DeskDesignApply {
    /// The look a design asks for, over `current`. A field the design leaves nil keeps its current value.
    static func merged(_ current: DeskDesignLook, _ design: DeskDesign) -> DeskDesignLook {
        var next = current
        if let layout = design.layout { next.arrangement = mergeLayout(current.arrangement, layout) }
        if let font = design.font { next.font = fontSetting(font) }
        if let ink = design.ink, !ink.isEmpty { next.ink = ink.compactMap { DeskDesign.hex($0) }.prefix(4).joined(separator: ",") }
        if let plate = design.plate { next.plate = plate }
        if let shadow = design.shadow { next.shadow = shadow }
        return next
    }

    /// The layout merged piece by piece (Windows' `MergeLayout`): a hidden piece goes; an anchor places the piece
    /// (a moved piece keeps its size); a piece shown with no anchor takes its standard one; size, tilt (the stack's
    /// slant) and style follow.
    static func mergeLayout(_ current: DeskArrangement, _ pieces: [DeskPieceLayout]) -> DeskArrangement {
        var arr = current
        for p in pieces {
            guard DeskArrangement.known.contains(p.piece) else { continue }
            if p.hidden == true { arr.hide(p.piece); continue }
            if let a = p.anchor { arr.place(p.piece, at: a) } else if p.hidden == false, arr.placement(p.piece) == nil { arr.show(p.piece) }
            if let s = p.size { arr.setScale(p.piece, s) }
            if let t = p.tilt { arr.setTilt(p.piece, t) }
            if let st = p.style { arr.setStyle(p.piece, st) }
        }
        return arr
    }

    /// The `font` value for a design's font: "" stays the system font, anything else the family name.
    static func fontSetting(_ font: DeskFontChoice) -> String { font.family }

    /// Writes what differs between `current` and `next` into `defaults`: only the settings a design changed, so
    /// nothing else in the suite is touched.
    static func write(_ next: DeskDesignLook, over current: DeskDesignLook, to defaults: DefaultsStore) {
        if next.arrangement.string != current.arrangement.string {
            defaults.set(next.arrangement.string, forKey: DeskArrangeMode.layoutKey)
        }
        if next.font != current.font, let font = next.font { defaults.set(font, forKey: DeskFont.key) }
        if next.ink != current.ink { defaults.set(next.ink, forKey: DeskLook.inkKey) }
        if next.plate != current.plate { defaults.set(next.plate, forKey: DeskPlate.key) }
        if next.shadow != current.shadow { defaults.set(next.shadow, forKey: DeskLook.shadowKey) }
    }

    // MARK: - Put my Desk back

    /// The Desk suite key holding the values the last applied design replaced (JSON text).
    static let beforeKey = "deskDesignBefore"

    /// The suite keys a design writes, in the order `write` checks them.
    static func changedKeys(_ next: DeskDesignLook, over current: DeskDesignLook) -> [String] {
        var keys: [String] = []
        if next.arrangement.string != current.arrangement.string { keys.append(DeskArrangeMode.layoutKey) }
        if next.font != current.font, next.font != nil { keys.append(DeskFont.key) }
        if next.ink != current.ink { keys.append(DeskLook.inkKey) }
        if next.plate != current.plate { keys.append(DeskPlate.key) }
        if next.shadow != current.shadow { keys.append(DeskLook.shadowKey) }
        return keys
    }

    /// Saves the stored values of the keys a design is about to change (an absent key is recorded as absent), with the
    /// design's name and the time. Replaces an earlier snapshot; a design that changes nothing leaves it alone.
    static func recordBefore(_ next: DeskDesignLook, over current: DeskDesignLook, name: String?, now: Date, in store: DefaultsStore) {
        let keys = changedKeys(next, over: current)
        guard !keys.isEmpty else { return }
        var values: [String: Any] = [:]
        for k in keys { values[k] = store.object(forKey: k) ?? NSNull() }
        let root: [String: Any] = ["name": name ?? "", "at": ISO8601.stamp(now), "keys": values]
        guard let data = try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys]) else { return }
        store.set(String(decoding: data, as: UTF8.self), forKey: beforeKey)
    }

    /// What `Put my Desk back` would restore.
    struct Undo {
        let name: String?
        let at: Date?
        fileprivate let values: [String: Any]

        /// The snapshot in `store`, nil with none (or one that cannot be read).
        static func read(_ store: DefaultsStore) -> Undo? {
            guard let text = store.object(forKey: DeskDesignApply.beforeKey) as? String,
                  let obj = try? JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any],
                  let keys = obj["keys"] as? [String: Any] else { return nil }
            let name = (obj["name"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            return Undo(name: name, at: (obj["at"] as? String).flatMap(ISO8601.parse), values: keys)
        }

        /// Writes the saved values back (removing keys that were absent) and clears the snapshot; nothing else.
        func putBack(in store: DefaultsStore) {
            let known = [DeskArrangeMode.layoutKey, DeskFont.key, DeskLook.inkKey, DeskPlate.key, DeskLook.shadowKey]
            for (k, v) in values where known.contains(k) { store.set(v is NSNull ? nil : v, forKey: k) }
            store.set(nil, forKey: DeskDesignApply.beforeKey)
        }

        /// The caption under the button.
        func caption(now: Date = Date()) -> String {
            let f = RelativeDateTimeFormatter()
            f.unitsStyle = .full
            let when = at.map { f.localizedString(for: $0, relativeTo: now) } ?? "earlier"
            return "Undo \"\(name ?? "a design")\" from Claude, applied \(when)."
        }
    }

    /// Keep it: drops the snapshot and changes nothing else.
    static func keepIt(in store: DefaultsStore) { store.set(nil, forKey: beforeKey) }

    // MARK: - What changes

    /// The "what changed" lines for the banner: one plain sentence per difference, pieces first (in the Desk's
    /// order), then the font, ink, plate and shadow. Windows' `Diff`, with this Mac's own names.
    static func changes(from a: DeskDesignLook, to b: DeskDesignLook, available: (String) -> Bool = DeskFont.isAvailable) -> [String] {
        var lines: [String] = []
        for key in DeskArrangement.known {
            let name = DeskLayout.widgets.first { $0.key == key }?.name ?? key
            let was = a.arrangement.placement(key)
            guard let now = b.arrangement.placement(key) else {
                if was != nil { lines.append("\(name): hidden") }
                continue
            }
            let tilt = b.arrangement.tilt(of: key) ?? 0
            let style = b.arrangement.style(of: key)
            if let was {
                if was.anchor != now.anchor {
                    lines.append("\(name): moved from \(was.anchor.name.lowercased()) to \(now.anchor.name.lowercased())")
                }
                if abs(was.scale - now.scale) > 1e-9 { lines.append("\(name): size \(percent(was.scale)) to \(percent(now.scale))") }
                let wasTilt = a.arrangement.tilt(of: key) ?? 0
                if abs(wasTilt - tilt) > 1e-9 { lines.append("\(name): slant \(degrees(wasTilt)) to \(degrees(tilt))") }
                if DeskMeterStyles.supports(key), style != a.arrangement.style(of: key) {
                    lines.append("\(name): \(DeskMeterStyles.word(style))")
                }
            } else {
                var line = "\(name): shown at \(now.anchor.name.lowercased())"
                if abs(now.scale - 1) > 1e-9 { line += ", size \(percent(now.scale))" }
                if tilt != 0 { line += ", slanted \(degrees(tilt))" }
                if DeskMeterStyles.supports(key), style != .bar { line += ", \(DeskMeterStyles.word(style))" }
                lines.append(line)
            }
        }
        let fa = a.family(available: available)
        if let asked = b.font, asked.lowercased() != fa.lowercased() { lines.append("Font: \(fontName(fa)) to \(fontName(asked))") }
        if a.inkWords != b.inkWords { lines.append("Ink: \(a.inkWords.joined(separator: ", ")) to \(b.inkWords.joined(separator: ", "))") }
        if a.plate != b.plate { lines.append("\(DeskPlate.title): \(b.plate ? "on" : "off")") }
        if a.shadow != b.shadow { lines.append("\(DeskInkShadow.title): \(b.shadow ? "on" : "off")") }
        return lines
    }

    private static func percent(_ scale: Double) -> String { "\(Int((scale * 100).rounded()))%" }

    private static func degrees(_ tilt: Double) -> String { "\(Int(tilt.rounded()))\u{00B0}" }

    private static func fontName(_ family: String) -> String { family.isEmpty ? "the system font" : family }

    // MARK: - The font a preview draws in

    /// The family to draw a preview in when the design's font is not on this Mac: the nearest look that is, so the
    /// preview still reads. The design keeps the family it asked for; only the preview substitutes.
    static func previewFont(_ family: String, installed: (String) -> Bool) -> String {
        if family.isEmpty || BundledFonts.bundled(family) != nil || installed(family) { return family }
        let lower = family.lowercased()
        let want: [String]
        if ["mono", "code", "consol", "courier", "typewriter"].contains(where: lower.contains) {
            want = ["Menlo", "Monaco", "Courier New"]
        } else if ["script", "hand", "brush", "marker", "pen"].contains(where: lower.contains) {
            want = [DeskFont.defaultFamily]
        } else if (lower.contains("serif") && !lower.contains("sans")) || ["times", "georgia", "garamond", "playfair", "book"].contains(where: lower.contains) {
            want = ["Georgia", "Times New Roman", "Times"]
        } else {
            want = ["Helvetica Neue", "Helvetica"]
        }
        return want.first { BundledFonts.bundled($0) != nil || installed($0) } ?? DeskFont.defaultFamily
    }
}

/// What the Desk draws while a Desk design is previewed: the layout, the font, the plate and the shadow, read before
/// the saved values (the ink goes through `DeskInkPreview`, which the notch and the message already follow). The
/// saved settings are never written; each field is nil at rest. In memory only.
@MainActor
@Observable
final class DeskDesignPreview {
    static let shared = DeskDesignPreview()

    var layout: DeskArrangement?
    /// The `font` value to draw (a stand-in family when the design's is not installed).
    var font: String?
    var plate: Bool?
    var shadow: Bool?

    var isShowing: Bool { layout != nil || font != nil || plate != nil || shadow != nil }

    /// The layout string to draw from: the preview's, else `saved`.
    func layoutString(saved: String) -> String { layout?.string ?? saved }

    func clear() {
        if layout != nil { layout = nil }
        if font != nil { font = nil }
        if plate != nil { plate = nil }
        if shadow != nil { shadow = nil }
    }
}
