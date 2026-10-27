import Foundation

/// The kind-specific rules of `PackValidator`: themes, palettes, effects, pixel sheets and sprites.
extension PackValidator {
    // MARK: - Theme

    static let themeOptionalKeys = [
        "glass_alpha", "border_alpha", "border_tint", "accent_bloom", "inner_highlight", "card_corner_radius",
        "breath_period_ms", "opts_out_of_mica", "bg_grid", "monospace_font", "monospace_fallback",
    ]

    /// A catalog theme (FORMAT 5.1): the Windows ThemeLint's schema and design rules (see the type
    /// comment for why this is not `ThemeLint.lint`) plus what FORMAT adds for the catalog: unknown
    /// keys, `accent_bloom: null`, a non-boolean `opts_out_of_mica` or `bg_grid`, and a non-string
    /// font name. As in the reference, the design warnings are measured only when there is no error.
    static func lintTheme(_ data: PackValue) -> (errors: [String], warnings: [String]) {
        guard let o = data.object else { return (["theme must be a JSON object"], []) }
        var errs: [String] = []
        for k in o.keys where k != "name" && !ThemeLint.colorFields.contains(k) && !themeOptionalKeys.contains(k) {
            errs.append("unknown key \(PackJson.pyStr(k)) (catalog themes allow only the documented fields)")
        }
        if let ab = o["accent_bloom"], ab.isNull {
            errs.append("accent_bloom must be an object {\"blur\": 3-8, \"alpha\": 0.25-0.65}")
        }
        for key in ["opts_out_of_mica", "bg_grid"] where o.has(key) && o[key]?.bool == nil {
            errs.append("\(key) must be true or false")
        }
        for key in ["monospace_font", "monospace_fallback"] where o.has(key) && o[key]?.string == nil {
            errs.append("\(key) must be a string")
        }

        var core: [String] = []
        let colors = themeSchema(o, into: &core)
        let glassAlpha = themeDials(o, into: &core)
        errs += core
        if !errs.isEmpty { return (errs, []) }
        let optsOut = o["opts_out_of_mica"]?.bool ?? false
        return ([], themeDesignRules(o, colors: colors, glassAlpha: glassAlpha, optsOut: optsOut))
    }

    private static func describeTheme(_ v: PackValue) -> String {
        switch v {
        case .null: return "null"
        case .string(let s): return PackJson.pyStr(s)
        case .number(let n): return n.raw
        case .bool(let b): return b ? "true" : "false"
        case .object: return "an object"
        case .array: return "an array"
        }
    }

    private static func themeSchema(_ o: PackObject, into errs: inout [String]) -> [String: ThemeLint.RGB] {
        if let name = o["name"]?.string {
            if name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                errs.append("name must not be empty.")
            } else if name.utf16.count > ThemeLint.maxNameLength {
                errs.append("name is \(name.utf16.count) characters; the strip shows 12, \(ThemeLint.maxNameLength) is the limit.")
            }
        } else {
            errs.append("name is required (1 to 12 characters, Title Case).")
        }

        var colors: [String: ThemeLint.RGB] = [:]
        for field in ThemeLint.colorFields {
            guard let raw = o[field], !raw.isNull else {
                errs.append("\(field) is required (a #rrggbb color).")
                continue
            }
            guard let rgb = ThemeLint.parseHex(raw.string) else {
                errs.append("\(field) must be a #rrggbb color (six hex digits), got \(describeTheme(raw)).")
                continue
            }
            colors[field] = rgb
        }
        return colors
    }

    /// The dials' checks; returns glass_alpha (or its default) for the contrast rule.
    private static func themeDials(_ o: PackObject, into errs: inout [String]) -> Double {
        let glassAlpha = checkRange(o, "glass_alpha", 0, 1, 0.80, &errs)
        _ = checkRange(o, "border_alpha", 0, 1, 0.40, &errs)
        checkOptionalHex(o, "border_tint", &errs)
        if let ab = o["accent_bloom"], !ab.isNull {
            if let abo = ab.object {
                _ = checkRange(abo, "blur", 0, 20, 4, &errs, label: "accent_bloom.blur")
                _ = checkRange(abo, "alpha", 0, 1, 0.45, &errs, label: "accent_bloom.alpha")
            } else {
                errs.append("accent_bloom must be an object {\"blur\": 3-8, \"alpha\": 0.25-0.65}.")
            }
        }
        if let ih = o["inner_highlight"], !ih.isNull {
            if let iho = ih.object {
                if let c = iho["color"], !c.isNull {
                    checkOptionalHex(iho, "color", &errs, label: "inner_highlight.color")
                } else {
                    errs.append("inner_highlight needs a color (#rrggbb) or the whole field set to null.")
                }
                _ = checkRange(iho, "alpha", 0, 1, 0.20, &errs, label: "inner_highlight.alpha")
            } else {
                errs.append("inner_highlight must be {\"color\": \"#rrggbb\", \"alpha\": 0.15-0.30} or null.")
            }
        }
        _ = checkRange(o, "card_corner_radius", 0, 24, 10, &errs)
        _ = checkRange(o, "breath_period_ms", 500, 20000, 4000, &errs)
        return glassAlpha
    }

    /// Range-check a numeric dial when present; returns the value (or the default when absent or
    /// invalid) so later rules can use it.
    private static func checkRange(_ o: PackObject, _ key: String, _ lo: Double, _ hi: Double, _ fallback: Double,
                                   _ errs: inout [String], label: String? = nil) -> Double {
        let label = label ?? key
        guard let raw = o[key], !raw.isNull else { return fallback }
        guard let n = raw.number else {
            errs.append("\(label) must be a number between \(ThemeLint.fmtTrim(lo, 2)) and \(ThemeLint.fmtTrim(hi, 2)), got \(describeTheme(raw)).")
            return fallback
        }
        let value = n.value
        if value.isNaN || value < lo || value > hi {
            errs.append("\(label) must be between \(ThemeLint.fmtTrim(lo, 2)) and \(ThemeLint.fmtTrim(hi, 2)), got \(ThemeLint.fmtTrim(value, 3)).")
            return fallback
        }
        return value
    }

    private static func checkOptionalHex(_ o: PackObject, _ key: String, _ errs: inout [String], label: String? = nil) {
        let label = label ?? key
        guard let raw = o[key], !raw.isNull else { return }
        if ThemeLint.parseHex(raw.string) == nil {
            errs.append("\(label) must be a #rrggbb color or null, got \(describeTheme(raw)).")
        }
    }

    /// The design rules, as measurements (the Windows ThemeLint's, with the same thresholds).
    private static func themeDesignRules(_ o: PackObject, colors c: [String: ThemeLint.RGB], glassAlpha: Double,
                                         optsOut: Bool) -> [String] {
        var warns: [String] = []
        for field in ["bg", "glass", "glass_on_mica"] {
            let l = ThemeLint.luminance(c[field]!)
            if l > ThemeLint.darkBaseMaxLuminance {
                warns.append("\(field) is light (luminance \(ThemeLint.fmt(l, 2)), keep it under \(ThemeLint.fmt(ThemeLint.darkBaseMaxLuminance, 2))); translucent glass layering does not work on light surfaces.")
            }
        }

        let card = ThemeLint.composite(c["glass_on_mica"]!, optsOut ? 1.0 : glassAlpha, ThemeLint.desktop)
        let cardL = ThemeLint.luminance(card)
        let textRatio = ThemeLint.contrast(ThemeLint.luminance(c["text"]!), cardL)
        if textRatio < ThemeLint.textContrastMin {
            warns.append("text reads at \(ThemeLint.fmt(textRatio, 1)):1 on the card (needs \(ThemeLint.fmt(ThemeLint.textContrastMin, 1)):1); pick a brighter text or a darker glass_on_mica.")
        }
        let secondaryRatio = ThemeLint.contrast(ThemeLint.luminance(c["text_secondary"]!), cardL)
        if secondaryRatio < ThemeLint.textSecondaryContrastMin {
            warns.append("text_secondary reads at \(ThemeLint.fmt(secondaryRatio, 1)):1 on the card (needs \(ThemeLint.fmt(ThemeLint.textSecondaryContrastMin, 1)):1).")
        }

        let ramp = ["text", "text_secondary", "text_dim", "text_muted"]
        for i in 1..<ramp.count where ThemeLint.luminance(c[ramp[i]]!) >= ThemeLint.luminance(c[ramp[i - 1]]!) {
            warns.append("\(ramp[i]) is not darker than \(ramp[i - 1]); the text ramp is one hue at decreasing luminance.")
        }
        for i in 1..<ramp.count {
            if let d = ThemeLint.hueDistance(c["text"]!, c[ramp[i]]!), d > ThemeLint.hueToleranceDegrees {
                warns.append("\(ramp[i]) is a different hue from text (\(ThemeLint.fmt(d, 0)) degrees apart); keep the ramp monochrome.")
            }
        }

        // The marker stands out by luminance OR by hue (Ember's amber tick on the green fill is a
        // hue win, not a luminance one).
        let green = ThemeLint.parseHex(ThemeLint.barFillGreen)!
        let marker = c["pace_marker"]!
        func hidden(on fill: ThemeLint.RGB) -> Bool {
            ThemeLint.contrast(ThemeLint.luminance(marker), ThemeLint.luminance(fill)) < ThemeLint.paceMarkerContrastMin
                && (ThemeLint.hueDistance(marker, fill) ?? 0) < ThemeLint.paceMarkerHueMin
        }
        let onGreen = hidden(on: green)
        if onGreen || hidden(on: c["bar_bg"]!) {
            warns.append("pace_marker disappears on the \(onGreen ? "green bar fill" : "empty bar"); it should stand out on every fill by brightness or by a complementary hue.")
        }

        if let sd = ThemeLint.hueDistance(c["accent"]!, c["sparkline"]!), sd > ThemeLint.hueToleranceDegrees {
            warns.append("sparkline is a different hue from accent (\(ThemeLint.fmt(sd, 0)) degrees apart); one accent hue reads as one brand.")
        }
        if let tint = ThemeLint.parseHex(o["border_tint"]?.string), let td = ThemeLint.hueDistance(c["accent"]!, tint),
           td > ThemeLint.hueToleranceDegrees {
            warns.append("border_tint is a different hue from accent (\(ThemeLint.fmt(td, 0)) degrees apart); tint borders with the accent or leave it null.")
        }
        if optsOut && glassAlpha < 1.0 {
            warns.append("opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise.")
        }
        return warns
    }

    // MARK: - Palettes and effects

    private static func unknownKeys(_ o: PackObject, allowed: [String]) -> [String] {
        ordinalSorted(o.keys.filter { !allowed.contains($0) })
    }

    static func checkPalettes(_ rep: Report, _ data: PackValue) {
        let rel = "palettes.json"
        guard let o = data.object else {
            rep.err("\(rel): must be a JSON object")
            return
        }
        let extra = unknownKeys(o, allowed: ["schema_version", "palettes"])
        if !extra.isEmpty { rep.err("\(rel): unknown keys \(PackJson.pyList(extra))") }
        if !PackJson.isOne(o["schema_version"]) { rep.err("\(rel): schema_version must be 1") }
        guard let items = o["palettes"]?.array, items.count >= 1, items.count <= 64 else {
            rep.err("\(rel): palettes must be an array of 1 to 64 entries")
            return
        }
        var seen = Set<String>()
        for (i, item) in items.enumerated() {
            let where_ = "\(rel): palettes[\(i)]"
            guard let p = item.object else {
                rep.err("\(where_) must be an object")
                continue
            }
            let pextra = unknownKeys(p, allowed: ["name", "colors", "mood"])
            if !pextra.isEmpty { rep.err("\(where_) has unknown keys \(PackJson.pyList(pextra))") }
            if let n = p["name"]?.string, isPresetName(n) {
                if !seen.insert(n).inserted { rep.err("\(where_): duplicate palette name \(PackJson.pyStr(n))") }
            } else {
                rep.err("\(where_): name must be 2 to 24 lowercase letters, digits or hyphens")
            }
            if let cols = p["colors"]?.array, cols.count >= 1, cols.count <= 4 {
                for c in cols where !isHex(c) { rep.err("\(where_): \(PackJson.pyRepr(c)) is not a #rrggbb color") }
            } else {
                rep.err("\(where_): colors must be 1 to 4 #rrggbb stops")
            }
            if p.has("mood") {
                if let mood = p["mood"]?.string, PackJson.pyLen(mood) <= 24 {} else {
                    rep.err("\(where_): mood must be a string of at most 24 characters")
                }
            }
        }
    }

    static func checkEffects(_ rep: Report, _ data: PackValue) {
        let rel = "effects.json"
        guard let o = data.object else {
            rep.err("\(rel): must be a JSON object")
            return
        }
        let extra = unknownKeys(o, allowed: ["schema_version", "effects"])
        if !extra.isEmpty { rep.err("\(rel): unknown keys \(PackJson.pyList(extra))") }
        if !PackJson.isOne(o["schema_version"]) { rep.err("\(rel): schema_version must be 1") }
        guard let items = o["effects"]?.array, items.count >= 1, items.count <= 64 else {
            rep.err("\(rel): effects must be an array of 1 to 64 presets")
            return
        }
        var seen = Set<String>()
        for (i, item) in items.enumerated() {
            let where_ = "\(rel): effects[\(i)]"
            guard let e = item.object else {
                rep.err("\(where_) must be an object")
                continue
            }
            let eextra = unknownKeys(e, allowed: ["name", "type", "intensity", "period_ms", "color"])
            if !eextra.isEmpty { rep.err("\(where_) has unknown keys \(PackJson.pyList(eextra))") }
            if let n = e["name"]?.string, isPresetName(n) {
                if !seen.insert(n).inserted { rep.err("\(where_): duplicate preset name \(PackJson.pyStr(n))") }
            } else {
                rep.err("\(where_): name must be 2 to 24 lowercase letters, digits or hyphens")
            }
            if let t = e["type"]?.string, effectTypes.contains(t) {} else {
                rep.err("\(where_): type must be one of \(effectTypes.joined(separator: ", "))")
            }
            if let n = e["intensity"]?.number, PackJson.isNum(e["intensity"]), n.value >= 0, n.value <= 1 {} else {
                rep.err("\(where_): intensity must be a number from 0 to 1")
            }
            if let n = e["period_ms"]?.number, PackJson.isInt(e["period_ms"]), n.value >= 400, n.value <= 10000 {} else {
                rep.err("\(where_): period_ms must be an integer from 400 to 10000")
            }
            if let c = e["color"], c.string == "ink" || isHex(c) {} else {
                rep.err("\(where_): color must be a #rrggbb color or \"ink\"")
            }
        }
    }

    // MARK: - Pixel sheets

    /// Check a sprite v2 pixel sheet (FORMAT 5.5) drawn into a `w` x `h` cell. `maxIndex` is the
    /// highest frame index the animations use (-1 for none). Returns the messages, capped at
    /// `pixelMaxErrors` plus one line saying more were cut.
    static func checkPixelSheet(_ rel: String, _ data: PackValue, _ w: Int, _ h: Int, maxIndex: Double, label: String) -> [String] {
        var errors: [String] = []
        func err(_ msg: String) { errors.append("\(rel): \(msg)") }

        guard let o = data.object else {
            return ["\(rel): a pixel sheet must be a JSON object with \(pixelKeys.joined(separator: ", "))"]
        }
        let extra = unknownKeys(o, allowed: pixelKeys)
        if !extra.isEmpty {
            err("unknown keys \(PackJson.pyList(extra)) (a pixel sheet has exactly \(pixelKeys.joined(separator: ", ")))")
        }
        for k in pixelKeys where !o.has(k) { err("missing required key \(PackJson.pyStr(k))") }

        var ncolors: Int?
        if o.has("palette") {
            if let pal = o["palette"]?.array, pal.count >= 1, pal.count <= pixelMaxColors {
                ncolors = pal.count
                for (i, c) in pal.enumerated() {
                    if !isHex(c) {
                        err("palette[\(i)] \(PackJson.pyRepr(c)) is not a #rrggbb color")
                    } else if let s = c.string, s.lowercased() == "#ffffff" {
                        err("palette[\(i)] is pure white (\(s)), which sprites may not use (it burns on "
                            + "OLED panels); use an off-white such as #f0f0f0")
                    }
                }
            } else {
                let got = o["palette"]?.array.map { " (got \($0.count))" } ?? ""
                err("palette must be 1 to \(pixelMaxColors) #rrggbb colors\(got)")
            }
        }

        let fwv = o["frame_w"], fhv = o["frame_h"]
        let sizeOk = pixelSize(fwv) && pixelSize(fhv)
        let fw: Int? = sizeOk ? Int(fwv!.number!.value) : nil
        let fh: Int? = sizeOk ? Int(fhv!.number!.value) : nil
        if o.has("frame_w") && o.has("frame_h") {
            if !sizeOk {
                err("frame_w and frame_h must be integers from \(pixelFrameMin) to \(pixelFrameMax), got "
                    + "\(PackJson.pyRepr(fwv)) x \(PackJson.pyRepr(fhv)) (pixel sheets are sized for a companion)")
            } else if fw != w || fh != h {
                err("frame size \(fw!)x\(fh!) does not match the \(label) frame size \(w)x\(h)")
            }
        }

        if o.has("frames") {
            if let frames = o["frames"]?.array, frames.count >= 1, frames.count <= pixelMaxFrames {
                if maxIndex >= Double(frames.count) {
                    err("animations use frame index \(formatIndex(maxIndex)) but the pixel sheet has only \(frames.count) "
                        + "frame\(frames.count == 1 ? "" : "s") (\(label))")
                }
                for (fi, f) in frames.enumerated() {
                    guard let frame = f.array, frame.allSatisfy({ $0.string != nil }) else {
                        err("frames[\(fi)] must be an array of strings, one per pixel row")
                        continue
                    }
                    if let fh, frame.count != fh { err("frames[\(fi)] has \(frame.count) rows, expected \(fh) (frame_h)") }
                    for (ri, r) in frame.enumerated() {
                        let row = r.string ?? ""
                        let rowLen = PackJson.pyLen(row)
                        if let fw, rowLen != fw { err("frames[\(fi)][\(ri)] is \(rowLen) characters, expected \(fw) (frame_w)") }
                        var ci = 0
                        for scalar in row.unicodeScalars {
                            let c = scalar.value
                            if c > 0x7F || !pixelChars.contains(scalar) {
                                err("frames[\(fi)][\(ri)][\(ci)] is \(PackJson.pyStr(String(scalar))); a pixel is '.' "
                                    + "(transparent) or 0-9, a-f (a palette index, lowercase)")
                                break
                            }
                            if c != 0x2E, let ncolors, pixelIndex(c) >= ncolors {
                                err("frames[\(fi)][\(ri)][\(ci)] is palette index \(PackJson.pyStr(String(scalar))) (\(pixelIndex(c))) "
                                    + "but the palette has only \(ncolors) color\(ncolors == 1 ? "" : "s")")
                                break
                            }
                            ci += 1
                        }
                    }
                    if errors.count > pixelMaxErrors { break }
                }
            } else {
                err("frames must be an array of 1 to \(pixelMaxFrames) frames")
            }
        }

        if errors.count <= pixelMaxErrors { return errors }
        return Array(errors.prefix(pixelMaxErrors)) + ["\(rel): more errors in this pixel sheet are not shown; fix the ones above first"]
    }

    private static func pixelSize(_ v: PackValue?) -> Bool {
        guard PackJson.isInt(v), let n = v?.number else { return false }
        return n.value >= Double(pixelFrameMin) && n.value <= Double(pixelFrameMax)
    }

    /// The palette index a pixel character names ('0'-'9', 'a'-'f').
    static func pixelIndex(_ c: UInt32) -> Int { c <= 0x39 ? Int(c) - 0x30 : Int(c) - 0x61 + 10 }

    private static func formatIndex(_ v: Double) -> String { String(format: "%.0f", v) }

    private static func spriteSize(_ v: PackValue?) -> Bool {
        guard PackJson.isInt(v), let n = v?.number else { return false }
        return n.value >= 8 && n.value <= 64
    }

    // MARK: - Sprites

    private struct SheetRef {
        let path: String
        let w: Int
        let h: Int
        let label: String
    }

    static func checkSprite(_ pack: Pack, _ data: PackValue, _ filesField: PackValue?) {
        let rep = pack.report
        let rel = "sprite.json"
        guard let o = data.object else {
            rep.err("\(rel): must be a JSON object")
            return
        }
        let allowed = ["schema_version", "frame_w", "frame_h", "scale", "fps", "sheet", "stages", "animations", "moods", "triggers", "roam"]
        let extra = unknownKeys(o, allowed: allowed)
        if !extra.isEmpty { rep.err("\(rel): unknown keys \(PackJson.pyList(extra))") }
        var sv: Int?
        if PackJson.isInt(o["schema_version"]), let n = o["schema_version"]?.number, n.value == 1 || n.value == 2 {
            sv = Int(n.value)
        } else {
            rep.err("\(rel): schema_version must be 1 or 2 (2 allows .json pixel sheets)")
        }

        func sheetPathError(_ path: PackValue?) -> String? {
            guard let p = path?.string, safeRelPath(p) else { return "must be a safe relative path" }
            if p.utf8.hasSuffix(".png") { return nil }
            if p.utf8.hasSuffix(".json") {
                if p == "sprite.json" { return "must not be sprite.json itself" }
                if sv == 2 { return nil }
                return "\(PackJson.pyStr(p)) is a .json pixel sheet, which needs sprite.json schema_version 2 "
                    + "(schema_version 1 sheets are .png only)"
            }
            return "must be a .png sheet" + (sv == 2 ? " or a .json pixel sheet" : "")
        }

        var fw: Int?, fh: Int?
        if !spriteSize(o["frame_w"]) || !spriteSize(o["frame_h"]) {
            rep.err("\(rel): frame_w and frame_h must be integers from 8 to 64")
        } else {
            fw = Int(o["frame_w"]!.number!.value)
            fh = Int(o["frame_h"]!.number!.value)
        }
        if !(PackJson.isInt(o["scale"]) && o["scale"]!.number!.value >= 1 && o["scale"]!.number!.value <= 4) {
            rep.err("\(rel): scale must be an integer from 1 to 4")
        }
        if !(PackJson.isInt(o["fps"]) && o["fps"]!.number!.value >= 1 && o["fps"]!.number!.value <= 24) {
            rep.err("\(rel): fps must be an integer from 1 to 24")
        }

        // animations
        var maxIndex = -1.0
        var animNames = Set<String>()
        if let anims = o["animations"]?.object {
            if anims.count > 32 { rep.err("\(rel): more than 32 animations") }
            for name in anims.keys {
                if !isAnimationName(name) {
                    rep.err("\(rel): animation name \(PackJson.pyStr(name)) must match [a-z][a-z0-9_]{1,23}")
                }
                animNames.insert(name)
                if let frames = anims[name]?.array, frames.count >= 1, frames.count <= 64,
                   frames.allSatisfy({ PackJson.isInt($0) && ($0.number?.value ?? -1) >= 0 }) {
                    maxIndex = max(maxIndex, frames.map { $0.number!.value }.max()!)
                } else {
                    rep.err("\(rel): animation \(PackJson.pyStr(name)) must be 1 to 64 non-negative integer frame indexes")
                }
            }
            for need in requiredAnimations where !animNames.contains(need) {
                rep.err("\(rel): required animation \(PackJson.pyStr(need)) is missing")
            }
        } else {
            rep.err("\(rel): animations must be an object")
        }

        // moods and triggers
        for (key, keys) in [("moods", moodKeys), ("triggers", triggerKeys)] {
            guard let m = o[key]?.object else {
                rep.err("\(rel): \(key) must be an object with keys \(keys.joined(separator: ", "))")
                continue
            }
            for k in keys {
                if let v = m[k] {
                    if let an = v.string, animNames.contains(an) {} else {
                        rep.err("\(rel): \(key).\(k) names animation \(PackJson.pyRepr(v)), which is not defined")
                    }
                } else {
                    rep.err("\(rel): \(key) is missing \(PackJson.pyStr(k))")
                }
            }
            for k in m.keys where !keys.contains(k) {
                rep.err("\(rel): \(key) has unknown key \(PackJson.pyStr(k))")
            }
        }

        // roam
        if let roam = o["roam"]?.object {
            let rextra = unknownKeys(roam, allowed: ["edges", "speed_px_s"])
            if !rextra.isEmpty { rep.err("\(rel): roam has unknown keys \(PackJson.pyList(rextra))") }
            var edgesOk = false
            if let edges = roam["edges"]?.array, !edges.isEmpty {
                let names = edges.compactMap(\.string)
                edgesOk = names.count == edges.count && names.allSatisfy(roamEdges.contains) && Set(names).count == names.count
            }
            if !edgesOk {
                rep.err("\(rel): roam.edges must be a non-empty unique subset of \(roamEdges.joined(separator: ", "))")
            }
            if !(PackJson.isNum(roam["speed_px_s"]) && roam["speed_px_s"]!.number!.value >= 5 && roam["speed_px_s"]!.number!.value <= 120) {
                rep.err("\(rel): roam.speed_px_s must be a number from 5 to 120")
            }
        } else {
            rep.err("\(rel): roam must be an object")
        }

        // sheets and stages
        var sheets: [SheetRef] = []
        let hasSheet = o.has("sheet"), hasStages = o.has("stages")
        if hasSheet == hasStages {
            rep.err("\(rel): give exactly one of `sheet` (single sheet) or `stages`")
        } else if hasSheet {
            if let why = sheetPathError(o["sheet"]) {
                rep.err("\(rel): sheet \(why)")
            } else if let fw, let fh, let s = o["sheet"]?.string {
                sheets.append(SheetRef(path: s, w: fw, h: fh, label: "sheet"))
            }
        } else {
            var stages: [PackValue] = []
            if let list = o["stages"]?.array, list.count >= 1, list.count <= 6 {
                stages = list
            } else {
                rep.err("\(rel): stages must be 1 to 6 entries")
            }
            var ids = Set<String>(), used = Set<String>()
            var prev: Double?
            for (i, item) in stages.enumerated() {
                let where_ = "\(rel): stages[\(i)]"
                guard let st = item.object else {
                    rep.err("\(where_) must be an object")
                    continue
                }
                let sextra = unknownKeys(st, allowed: ["id", "name", "min_growth", "sheet", "frame_w", "frame_h"])
                if !sextra.isEmpty { rep.err("\(where_) has unknown keys \(PackJson.pyList(sextra))") }
                let sid = st["id"]
                if let sids = sid?.string, isID(sids), PackJson.pyLen(sids) >= 2, PackJson.pyLen(sids) <= 20 {
                    if !ids.insert(sids).inserted { rep.err("\(where_): duplicate stage id \(PackJson.pyStr(sids))") }
                } else {
                    rep.err("\(where_): id must be kebab-case, 2 to 20 characters")
                }
                if let nm = st["name"]?.string, PackJson.pyLen(nm) >= 1, PackJson.pyLen(nm) <= 24 {} else {
                    rep.err("\(where_): name must be 1 to 24 characters")
                }
                if PackJson.isNum(st["min_growth"]), let mg = st["min_growth"]?.number?.value, mg >= 0, mg <= 1 {
                    if i == 0 && mg != 0 { rep.err("\(where_): the first stage must have min_growth 0") }
                    if let prev, mg <= prev {
                        rep.err("\(where_): min_growth \(PackJson.pyG(mg)) must be greater than the previous stage's \(PackJson.pyG(prev))")
                    }
                    prev = mg
                } else {
                    rep.err("\(where_): min_growth must be a number from 0 to 1")
                }
                var sw: Int?, sh: Int?
                if !spriteSize(st["frame_w"]) || !spriteSize(st["frame_h"]) {
                    rep.err("\(where_): frame_w and frame_h must be integers from 8 to 64")
                } else {
                    sw = Int(st["frame_w"]!.number!.value)
                    sh = Int(st["frame_h"]!.number!.value)
                    if i == 0, let fw, let fh, sw != fw || sh != fh {
                        rep.err("\(where_): stage 0 frame size \(sw!)x\(sh!) must equal the top-level frame_w x frame_h \(fw)x\(fh)")
                    }
                }
                let sp = st["sheet"]
                if let why = sheetPathError(sp) {
                    rep.err("\(where_): sheet \(why)")
                } else if let path = sp?.string {
                    if !used.insert(path).inserted {
                        rep.err("\(where_): sheet \(PackJson.pyStr(path)) is used by another stage")
                    } else if let sw, let sh {
                        sheets.append(SheetRef(path: path, w: sw, h: sh, label: "stage " + PackJson.pyRepr(sid)))
                    }
                }
            }
        }

        var expectedFiles: Set<String> = ["sprite.json"]
        for s in sheets { expectedFiles.insert(s.path) }
        var listed = Set<String>()
        for p in filesField?.array ?? [] { if let s = p.string { listed.insert(s) } }
        if !sheets.isEmpty && listed != expectedFiles {
            let missing = ordinalSorted(Array(expectedFiles.subtracting(listed)))
            let surplus = ordinalSorted(Array(listed.subtracting(expectedFiles)))
            if !missing.isEmpty { rep.err("pack.json: files must list " + missing.joined(separator: ", ")) }
            if !surplus.isEmpty {
                rep.err("pack.json: sprite packs may list only sprite.json and the sheets it references; remove "
                    + surplus.joined(separator: ", "))
            }
        }

        for s in sheets {
            guard let bytes = pack.onDisk[s.path] else { continue }   // reported by the exact-listing check
            if s.path.utf8.hasSuffix(".json") {
                if bytes.count > pixelMaxBytes {
                    rep.err("\(s.path): pixel sheet is \(PackJson.pyF(Double(bytes.count) / 1048576.0, 1)) MB, limit is 2 MB")
                    continue
                }
                if case .value(let sheet) = pack.load(s.path) {
                    for m in checkPixelSheet(s.path, sheet, s.w, s.h, maxIndex: maxIndex, label: s.label) { rep.err(m) }
                }
                continue
            }
            guard let (pw, ph) = checkPng(rep, s.path, bytes) else { continue }
            if pw % s.w != 0 || ph % s.h != 0 {
                rep.err("\(s.path): sheet is \(pw)x\(ph), not a whole number of \(s.w)x\(s.h) frames (\(s.label))")
                continue
            }
            let cells = (pw / s.w) * (ph / s.h)
            if maxIndex >= Double(cells) {
                rep.err("\(s.path): animations use frame index \(formatIndex(maxIndex)) but the sheet has only \(cells) frames (\(s.label))")
            }
        }
    }
}

private extension String.UTF8View {
    func hasSuffix(_ suffix: String) -> Bool {
        let s = Array(suffix.utf8)
        return count >= s.count && Array(self).suffix(s.count).elementsEqual(s)
    }
}
