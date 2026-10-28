import Foundation

/// The fields of a pack.json that passed validation.
struct PackManifest: Equatable {
    let id: String
    let name: String
    let version: String
    let author: String
    let license: String
    let kind: String
    let sanduhrMin: String
    let description: String
    let files: [String]
    let preview: String?
}

/// The verdict on one widget pack. `errors` are precise sentences, one per finding, the same
/// sentences `sanduhr-widgets/tools/validate.py` prints. `warnings` are the theme design rules; in
/// strict mode (the catalog's) they are errors instead and `warnings` is empty. `manifest` is set
/// only when the pack passed.
struct PackValidation {
    let errors: [String]
    let warnings: [String]
    let manifest: PackManifest?

    var ok: Bool { errors.isEmpty }
}

/// Sanduhr's Swift port of the widget pack validator (`sanduhr-widgets` `tools/validate.py`,
/// docs/FORMAT.md, format v1 with sprite.json v1 and v2), line by line from the Windows port
/// (windows-dotnet/src/Sanduhr.Core/PackValidator.cs). One validator, three callers: the catalog's
/// CI (the Python), the app on every install, and the MCP server on every propose. The app never
/// runs Python.
///
/// Pure: the input is the pack in memory (relative POSIX path to bytes, `pack.json` included) and
/// the output is a `PackValidation`. No disk, no network, no process. Whoever turns a zip or a
/// folder into the map refuses links and non-regular entries first (the map cannot hold one).
///
/// Messages match the Python byte for byte, except where a rule runs through the theme lint
/// (schema and design messages, which extend the Python's sentence with the fix; the Python's
/// text is a prefix of each) and a malformed JSON document (the reason text, which comes from each
/// parser). Where the Python accepts what FORMAT forbids, this port follows FORMAT ("if this file
/// and a tool disagree, the file wins"): a literal `null` where an object belongs is an error, and
/// anchors do not accept a trailing newline.
///
/// The theme rules are the Windows ThemeLint's, not `ThemeLint.lint`'s: the Mac lint also reads
/// Mac-only fields (`title_ink`, `ghost_alpha`, `description`) that the catalog's format does not
/// have, and words a few messages its own way. The color math is shared (`ThemeLint.luminance`
/// and friends are identical on both sides).
enum PackValidator {
    // MARK: - Format constants (FORMAT.md)

    static let kinds = ["theme", "palette", "effects", "font", "sprite"]

    /// SPDX id to the phrase of which LICENSE must carry one (whitespace collapsed, lowercase), in FORMAT's order.
    static let licenses: [(id: String, phrase: String)] = [
        ("MIT", "permission is hereby granted"),
        ("CC-BY-4.0", "attribution 4.0 international"),
        ("CC0-1.0", "cc0 1.0 universal"),
        ("Apache-2.0", "apache license"),
        ("OFL-1.1", "sil open font license"),
        ("BSD-3-Clause", "redistribution and use in source and binary forms"),
        ("ISC", "permission to use, copy, modify, and/or distribute"),
    ]

    static let fontLicenses = ["OFL-1.1", "Apache-2.0"]
    static let allowedExt = [".json", ".png", ".ttf", ".otf"]
    static let bannedExt = [
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".js", ".mjs", ".py", ".html", ".htm",
        ".svg", ".jar", ".msi", ".lnk", ".vbs", ".com", ".scr",
    ]
    static let bannedSnippets = ["<script", "javascript:", "<iframe", "<!doctype html"]
    static let execSignatures: [(sig: [UInt8], what: String)] = [
        (Array("MZ".utf8), "a Windows executable"),
        ([0x7F] + Array("ELF".utf8), "an ELF executable"),
        (Array("#!".utf8), "a script (shebang)"),
    ]
    static let trademarkSubstrings = ["claude", "anthropic"]
    static let trademarkWords = ["opus", "sonnet", "haiku", "fable"]

    static let maxImageBytes = 2 * 1024 * 1024
    static let maxImageSide = 4096
    static let maxFontBytes = 5 * 1024 * 1024
    static let maxPackBytes = 10 * 1024 * 1024
    static let maxFiles = 64
    static let maxLicenseBytes = 100 * 1024

    static let manifestRequired = ["schema_version", "id", "name", "version", "author", "license", "kind", "sanduhr_min", "description", "files"]
    static let manifestOptional = ["preview"]

    static let requiredAnimations = ["walk", "sit", "nap", "pop", "cheer", "worry", "focus", "sleep", "celebrate"]
    static let moodKeys = ["content", "focused", "worried", "exhausted", "celebrate"]
    static let triggerKeys = ["needs_you", "job_passed", "near_limit", "idle"]
    static let roamEdges = ["bottom", "left", "right"]
    static let effectTypes = ["glow", "sweep", "shimmer", "pulse"]

    static let pixelKeys = ["palette", "frame_w", "frame_h", "frames"]
    /// "." is transparent, the rest index the palette.
    static let pixelChars = Array(".0123456789abcdef".unicodeScalars)
    static let pixelMaxColors = 16
    static let pixelFrameMin = 8
    static let pixelFrameMax = 32
    static let pixelMaxFrames = 256
    static let pixelMaxBytes = 2 * 1024 * 1024
    /// Per sheet, so one bad sheet does not flood the report.
    static let pixelMaxErrors = 12

    // MARK: - Entry points

    /// Validate a pack given as its files: relative POSIX path (`pack.json`, `LICENSE`,
    /// `sprites/egg.png`) to bytes. `folderName` is the folder the pack lives in or will be
    /// installed to (the catalog entry's id); the pack's `id` must equal it, and nil skips that one
    /// check. `strict` turns theme design warnings into errors, as the catalog's CI does.
    static func validate(_ files: [String: Data]?, folderName: String? = nil, strict: Bool = false) -> PackValidation {
        var bytes: [String: [UInt8]]?
        if let files { bytes = files.mapValues { [UInt8]($0) } }
        return validate(bytes: bytes, folderName: folderName, strict: strict)
    }

    /// Validate a pack given as its `pack.json` and the rest of its files (which must not repeat it).
    static func validate(packJson: Data, otherFiles: [String: Data], folderName: String? = nil, strict: Bool = false) -> PackValidation {
        if otherFiles["pack.json"] != nil {
            return PackValidation(errors: ["pack.json is given twice"], warnings: [], manifest: nil)
        }
        var all = otherFiles
        all["pack.json"] = packJson
        return validate(all, folderName: folderName, strict: strict)
    }

    static func validate(bytes: [String: [UInt8]]?, folderName: String?, strict: Bool) -> PackValidation {
        let report = Report()
        let manifest = run(report, bytes, folderName)
        var errors = report.errors
        var warnings = report.warnings
        if strict {
            errors += warnings
            warnings = []
        }
        return PackValidation(errors: errors, warnings: warnings, manifest: errors.isEmpty ? manifest : nil)
    }

    // MARK: - One pack

    final class Report {
        var errors: [String] = []
        var warnings: [String] = []
        func err(_ m: String) { errors.append(m) }
        func warn(_ m: String) { warnings.append(m) }
    }

    /// A JSON file as a rule sees it: not in the pack, unreadable (its error is already
    /// reported), or its value (which may be a JSON null for a field, never for a file).
    enum Loaded {
        case absent
        case unreadable
        case value(PackValue)
    }

    /// The pack's files and the JSON parsed out of them so far, so each file is read (and its
    /// parse error reported) once.
    final class Pack {
        let report: Report
        let onDisk: [String: [UInt8]]
        private var parsed: [String: Loaded] = [:]

        init(report: Report, onDisk: [String: [UInt8]]) {
            self.report = report
            self.onDisk = onDisk
        }

        func load(_ rel: String) -> Loaded {
            guard let bytes = onDisk[rel] else { return .absent }
            if let hit = parsed[rel] { return hit }
            let v = PackValidator.loadJson(report, bytes, rel)
            parsed[rel] = v
            return v
        }
    }

    private static func run(_ rep: Report, _ input: [String: [UInt8]]?, _ folderName: String?) -> PackManifest? {
        guard let input else {
            rep.err("no files were given")
            return nil
        }
        let onDisk = input
        let names = ordinalSorted(Array(onDisk.keys))

        let total = onDisk.values.reduce(0) { $0 + $1.count }
        if total > maxPackBytes {
            rep.err("pack is \(PackJson.pyF(Double(total) / 1048576.0, 1)) MB, limit is 10 MB")
        }
        if onDisk.count > maxFiles + 3 {
            rep.err("pack has \(onDisk.count) files, limit is \(maxFiles)")
        }
        for rel in names where !safeRelPath(rel) {
            rep.err("\(rel): unsafe file name (segments must match [A-Za-z0-9][A-Za-z0-9._-]*)")
        }

        // -- manifest
        guard onDisk["pack.json"] != nil else {
            rep.err("pack.json is missing")
            return nil
        }
        let pack = Pack(report: rep, onDisk: onDisk)
        let loaded = pack.load("pack.json")
        guard case .value(let parsedManifest) = loaded else { return nil }
        guard let manifest = parsedManifest.object else {
            rep.err("pack.json: must be a JSON object")
            return nil
        }
        for k in manifest.keys where !manifestRequired.contains(k) && !manifestOptional.contains(k) {
            rep.err("pack.json: unknown key \(PackJson.pyStr(k))")
        }
        for k in manifestRequired where !manifest.has(k) {
            rep.err("pack.json: missing required key \(PackJson.pyStr(k))")
        }

        func textField(_ key: String, _ lo: Int, _ hi: Int) -> String? {
            guard let value = manifest[key] else { return nil }
            guard let v = value.string, PackJson.pyLen(v) >= lo, PackJson.pyLen(v) <= hi else {
                rep.err("pack.json: \(key) must be a string of \(lo) to \(hi) characters")
                return nil
            }
            if hasControlCharacter(v) {
                rep.err("pack.json: \(key) contains control characters")
                return nil
            }
            return v
        }

        if manifest.has("schema_version"), !PackJson.isOne(manifest["schema_version"]) {
            rep.err("pack.json: schema_version must be 1")
        }
        let pid = textField("id", 3, 40)
        if let pid {
            if !isID(pid) {
                rep.err("pack.json: id \(PackJson.pyStr(pid)) must be lowercase kebab-case ([a-z][a-z0-9]*(-[a-z0-9]+)*)")
            } else if let folderName, !sameScalars(pid, folderName) {
                rep.err("pack.json: id \(PackJson.pyStr(pid)) does not match the folder name \(PackJson.pyStr(folderName))")
            }
        }
        let name = textField("name", 1, 40)
        let version = textField("version", 1, 32)
        if let version, !isSemver(version) {
            rep.err("pack.json: version \(PackJson.pyStr(version)) must be MAJOR.MINOR.PATCH")
        }
        let author = textField("author", 1, 80)
        let smin = textField("sanduhr_min", 1, 32)
        if let smin, !isSemver(smin) {
            rep.err("pack.json: sanduhr_min \(PackJson.pyStr(smin)) must be MAJOR.MINOR.PATCH")
        }
        let desc = textField("description", 1, 200)

        var kind = manifest["kind"]?.string
        if manifest.has("kind"), kind == nil || !kinds.contains(kind!) {
            rep.err("pack.json: kind must be one of " + kinds.joined(separator: ", "))
            kind = nil
        }

        var lic = manifest["license"]?.string
        if manifest.has("license"), lic == nil || !licenses.contains(where: { $0.id == lic! }) {
            rep.err("pack.json: license \(PackJson.pyRepr(manifest["license"])) is not allowed; use one of "
                + licenses.map(\.id).joined(separator: ", "))
            lic = nil
        }
        if kind == "font", let lic, !fontLicenses.contains(lic) {
            rep.err("pack.json: font packs must use \(fontLicenses.joined(separator: " or ")) (got \(lic))")
        }

        // trademark scan over the manifest strings and every file name
        for (where_, text) in [("id", pid), ("name", name), ("description", desc), ("author", author)] {
            if let text, !text.isEmpty { checkTrademark(rep, "pack.json " + where_, text) }
        }
        for rel in names { checkTrademark(rep, "file name " + PackJson.pyStr(rel), rel) }

        let filesField = manifest["files"]
        var listed: [String] = []
        if manifest.has("files") {
            if let fl = filesField?.array, fl.count >= 1, fl.count <= maxFiles {
                for p in fl {
                    guard let ps = p.string else {
                        rep.err("pack.json: files entries must be strings")
                        continue
                    }
                    if !safeRelPath(ps) {
                        rep.err("pack.json: files entry \(PackJson.pyStr(ps)) is not a safe relative path")
                    } else if ps == "pack.json" || ps == "LICENSE" {
                        rep.err("pack.json: files must not list \(ps)")
                    } else {
                        listed.append(ps)
                    }
                }
                if Set(listed).count != listed.count {
                    rep.err("pack.json: files has duplicate entries")
                }
            } else {
                rep.err("pack.json: files must be an array of 1 to \(maxFiles) paths")
            }
        }

        var preview: String?
        if manifest.has("preview") {
            if let pv = manifest["preview"]?.string, safeRelPath(pv), pv.hasSuffixBytes(".png") {
                preview = pv
                if listed.contains(pv) {
                    rep.err("pack.json: preview is declared in `preview`; do not repeat it in files")
                }
            } else {
                rep.err("pack.json: preview must be a safe relative .png path")
            }
        }

        // -- exact file listing
        let declared = Set(listed)
        var exempt: Set<String> = ["pack.json", "LICENSE"]
        if let preview { exempt.insert(preview) }
        for rel in names where !declared.contains(rel) && !exempt.contains(rel) {
            rep.err("\(rel): file is not listed in pack.json files")
        }
        for rel in ordinalSorted(Array(declared)) where onDisk[rel] == nil {
            rep.err("\(rel): listed in pack.json files but missing from the pack")
        }
        if let preview, onDisk[preview] == nil {
            rep.err("\(preview): preview file is missing")
        }

        // -- per-file content checks
        for rel in names {
            guard let raw = onDisk[rel] else { continue }
            var ext: String
            if rel == "pack.json" || rel == "LICENSE" {
                ext = ""
            } else {
                ext = splitExt(rel)
                if !ext.utf8.elementsEqual(ext.lowercased().utf8) {
                    rep.err("\(rel): file extension must be lowercase")
                }
                ext = ext.lowercased()
                if bannedExt.contains(ext) {
                    rep.err("\(rel): \(ext) files are banned (packs are data only: no scripts, executables, HTML or SVG)")
                    continue
                }
                if !allowedExt.contains(ext) {
                    rep.err("\(rel): file type \(PackJson.pyStr(ext.isEmpty ? "none" : ext)) is not allowed (allowed: \(allowedExt.joined(separator: ", ")))")
                    continue
                }
            }
            for (sig, what) in execSignatures where raw.starts(with: sig) {
                rep.err("\(rel): content looks like \(what)")
            }
            for snip in bannedSnippets where containsAsciiLower(raw, snip) {
                rep.err("\(rel): contains \(PackJson.pyStr(snip)); active content is banned")
            }
            if ext == ".png" {
                if rel == preview { _ = checkPng(rep, rel, raw) }   // sprite sheets are checked with their frame grid
            } else if ext == ".ttf" || ext == ".otf" {
                checkFont(rep, rel, raw)
            }
        }

        // -- LICENSE
        if let licBytes = onDisk["LICENSE"] {
            if licBytes.isEmpty {
                rep.err("LICENSE is empty")
            } else if licBytes.count > maxLicenseBytes {
                rep.err("LICENSE is larger than 100 KB")
            } else if let scalars = PackJson.decodeUTF8(licBytes) {
                if let lic {
                    var text = String.UnicodeScalarView()
                    text.append(contentsOf: scalars)
                    let flat = pyWhitespaceWords(String(text).lowercased()).joined(separator: " ")
                    let phrase = licenses.first(where: { $0.id == lic })?.phrase ?? ""
                    if !containsBytes(Array(flat.utf8), Array(phrase.utf8)) {
                        rep.err("LICENSE text does not look like \(lic) (expected the standard license text)")
                    }
                }
            } else {
                rep.err("LICENSE is not valid UTF-8")
            }
        } else {
            rep.err("LICENSE is missing; every pack carries its own license text")
        }

        // -- kind-specific
        guard let kind else { return nil }

        // trademark scan over every string in every JSON asset
        for rel in names where rel != "pack.json" && rel.utf8.hasSuffixBytes(".json") {
            guard case .value(let data) = pack.load(rel) else { continue }
            if let s = firstTrademarkString(data) {
                checkTrademark(rep, "\(rel) value \(PackJson.pyStr(PackJson.pySlice(s, 40)))", s)
            }
        }

        switch kind {
        case "theme":
            if listed.count < 1 || listed.count > 8 {
                rep.err("pack.json: theme packs list 1 to 8 theme files (got \(listed.count))")
            }
            for rel in listed {
                if !isThemeFile(rel) {
                    rep.err("\(rel): theme files must be themes/<kebab-name>.json")
                    continue
                }
                guard case .value(let data) = pack.load(rel) else { continue }
                let (errs, warns) = lintTheme(data)
                for m in errs { rep.err("\(rel): \(m)") }
                for m in warns { rep.warn("\(rel): \(m)") }
            }
        case "palette":
            if listed != ["palettes.json"] {
                rep.err("pack.json: palette packs must list exactly [\"palettes.json\"]")
            }
            if case .value(let data) = pack.load("palettes.json") { checkPalettes(rep, data) }
        case "effects":
            if listed != ["effects.json"] {
                rep.err("pack.json: effects packs must list exactly [\"effects.json\"]")
            }
            if case .value(let data) = pack.load("effects.json") { checkEffects(rep, data) }
        case "font":
            if listed.count < 1 || listed.count > 4 {
                rep.err("pack.json: font packs list 1 to 4 font files (got \(listed.count))")
            }
            for rel in listed where splitExt(rel) != ".ttf" && splitExt(rel) != ".otf" {
                rep.err("\(rel): font packs may list only .ttf and .otf files")
            }
        default:   // sprite
            switch pack.load("sprite.json") {
            case .absent: rep.err("sprite.json is missing")
            case .unreadable: break
            case .value(let data): checkSprite(pack, data, filesField)
            }
        }

        guard rep.errors.isEmpty, let pid, let name, let version, let author, let lic, let smin, let desc else { return nil }
        return PackManifest(id: pid, name: name, version: version, author: author, license: lic, kind: kind,
                            sanduhrMin: smin, description: desc, files: listed, preview: preview)
    }

    // MARK: - Small helpers

    /// Parse a UTF-8 JSON file, or report why it could not be read (a JSON null included).
    static func loadJson(_ rep: Report, _ raw: [UInt8], _ rel: String) -> Loaded {
        if raw.starts(with: [0xEF, 0xBB, 0xBF]) {
            rep.err("\(rel): has a UTF-8 byte-order mark; JSON must be plain UTF-8")
            return .unreadable
        }
        guard let scalars = PackJson.decodeUTF8(raw) else {
            rep.err("\(rel): not valid UTF-8")
            return .unreadable
        }
        switch PackJson.parse(scalars) {
        case .failure(let e):
            rep.err("\(rel): invalid JSON (\(e.message))")
            return .unreadable
        case .success(let value):
            // Null means "no usable file" to every caller, so a literal null is an error here, as in the reference.
            if value.isNull {
                rep.err("\(rel): must be a JSON object, got null")
                return .unreadable
            }
            return .value(value)
        }
    }

    /// Strings in code point order (Python's `sorted`, .NET's ordinal order for everything a pack can name).
    static func ordinalSorted(_ items: [String]) -> [String] {
        items.sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
    }

    /// Equal by code points, not Swift's canonical equivalence.
    static func sameScalars(_ a: String, _ b: String) -> Bool { a.unicodeScalars.elementsEqual(b.unicodeScalars) }

    static func hasControlCharacter(_ s: String) -> Bool {
        s.unicodeScalars.contains { $0.value < 0x20 || $0.value == 0x7F }
    }

    /// Python's `str.split()`: runs of Unicode whitespace (including the information separators
    /// U+001C to U+001F) separate words.
    static func pyWhitespaceWords(_ s: String) -> [String] {
        var words: [String] = []
        var current = String.UnicodeScalarView()
        for c in s.unicodeScalars {
            if c.properties.isWhitespace || (0x1C...0x1F).contains(c.value) {
                if !current.isEmpty { words.append(String(current)); current = String.UnicodeScalarView() }
            } else {
                current.append(c)
            }
        }
        if !current.isEmpty { words.append(String(current)) }
        return words
    }

    /// Python's `os.path.splitext(p)[1]` on a POSIX path: the last dot of the last segment, not
    /// counting leading dots (`.hidden` has no extension).
    static func splitExt(_ p: String) -> String {
        let b = Array(p.utf8)
        let sep = b.lastIndex(of: UInt8(ascii: "/")) ?? -1
        guard let dot = b.lastIndex(of: UInt8(ascii: ".")), dot > sep else { return "" }
        for i in (sep + 1)..<dot where b[i] != UInt8(ascii: ".") {
            return String(decoding: b[dot...], as: UTF8.self)
        }
        return ""
    }

    /// Whether `raw`, lowercased as ASCII (`bytes.lower`), contains the ASCII `needle` (already lowercase).
    static func containsAsciiLower(_ raw: [UInt8], _ needle: String) -> Bool {
        let n = Array(needle.utf8)
        let scan = min(raw.count, maxPackBytes)
        guard n.count <= scan else { return false }
        var i = 0
        while i + n.count <= scan {
            var j = 0
            while j < n.count {
                var b = raw[i + j]
                if b >= 0x41 && b <= 0x5A { b += 32 }
                if b != n[j] { break }
                j += 1
            }
            if j == n.count { return true }
            i += 1
        }
        return false
    }

    static func containsBytes(_ hay: [UInt8], _ needle: [UInt8]) -> Bool {
        guard !needle.isEmpty else { return true }
        guard needle.count <= hay.count else { return false }
        for i in 0...(hay.count - needle.count) where hay[i] == needle[0] && Array(hay[i..<i + needle.count]) == needle {
            return true
        }
        return false
    }

    // MARK: - The format's patterns (the reference's regular expressions, by hand, ASCII only)

    private static func isLower(_ b: UInt8) -> Bool { b >= 0x61 && b <= 0x7A }
    private static func isDigit(_ b: UInt8) -> Bool { b >= 0x30 && b <= 0x39 }
    private static func isAlnum(_ b: UInt8) -> Bool { isLower(b) || isDigit(b) || (b >= 0x41 && b <= 0x5A) }
    private static func isLowerAlnum(_ b: UInt8) -> Bool { isLower(b) || isDigit(b) }

    /// `^[a-z][a-z0-9]*(-[a-z0-9]+)*\z`.
    static func isID(_ s: String) -> Bool {
        let b = Array(s.utf8)
        guard let first = b.first, isLower(first) else { return false }
        var afterDash = false
        for c in b.dropFirst() {
            if c == UInt8(ascii: "-") {
                if afterDash { return false }
                afterDash = true
            } else if isLowerAlnum(c) {
                afterDash = false
            } else {
                return false
            }
        }
        return !afterDash
    }

    /// `^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z`.
    static func isSemver(_ s: String) -> Bool {
        let parts = Array(s.utf8).split(separator: UInt8(ascii: "."), omittingEmptySubsequences: false)
        guard parts.count == 3 else { return false }
        return parts.allSatisfy { p in
            guard let f = p.first, p.allSatisfy(isDigit) else { return false }
            return p.count == 1 || f != 0x30
        }
    }

    /// `^#[0-9a-fA-F]{6}\z`.
    static func isHexColor(_ s: String) -> Bool {
        let b = Array(s.utf8)
        guard b.count == 7, b[0] == UInt8(ascii: "#") else { return false }
        return b.dropFirst().allSatisfy { isDigit($0) || ($0 >= 0x61 && $0 <= 0x66) || ($0 >= 0x41 && $0 <= 0x46) }
    }

    static func isHex(_ v: PackValue?) -> Bool {
        guard let s = v?.string else { return false }
        return isHexColor(s)
    }

    /// `^[a-z0-9][a-z0-9-]{1,23}\z`.
    static func isPresetName(_ s: String) -> Bool {
        let b = Array(s.utf8)
        guard (2...24).contains(b.count), isLowerAlnum(b[0]) else { return false }
        return b.dropFirst().allSatisfy { isLowerAlnum($0) || $0 == UInt8(ascii: "-") }
    }

    /// `^[a-z][a-z0-9_]{1,23}\z`.
    static func isAnimationName(_ s: String) -> Bool {
        let b = Array(s.utf8)
        guard (2...24).contains(b.count), isLower(b[0]) else { return false }
        return b.dropFirst().allSatisfy { isLowerAlnum($0) || $0 == UInt8(ascii: "_") }
    }

    /// `^themes/[a-z0-9]+(-[a-z0-9]+)*\.json\z`.
    static func isThemeFile(_ s: String) -> Bool {
        let b = Array(s.utf8)
        let head = Array("themes/".utf8), tail = Array(".json".utf8)
        guard b.count > head.count + tail.count, b.starts(with: head), b.suffix(tail.count).elementsEqual(tail) else { return false }
        let core = b[head.count..<(b.count - tail.count)]
        return core.split(separator: UInt8(ascii: "-"), omittingEmptySubsequences: false)
            .allSatisfy { !$0.isEmpty && $0.allSatisfy(isLowerAlnum) }
    }

    /// True for a relative POSIX path with safe segments (`[A-Za-z0-9][A-Za-z0-9._-]*` each).
    static func safeRelPath(_ p: String?) -> Bool {
        guard let p, !p.isEmpty, PackJson.pyLen(p) <= 128 else { return false }
        let b = Array(p.utf8)
        if b.first == UInt8(ascii: "/") || b.contains(UInt8(ascii: "\\")) || b.contains(UInt8(ascii: ":")) { return false }
        return b.split(separator: UInt8(ascii: "/"), omittingEmptySubsequences: false).allSatisfy { seg in
            guard let f = seg.first, isAlnum(f) else { return false }
            return seg.allSatisfy { isAlnum($0) || $0 == UInt8(ascii: ".") || $0 == UInt8(ascii: "_") || $0 == UInt8(ascii: "-") }
        }
    }

    // MARK: - Trademarks

    /// The trademark the text carries, or nil: `claude` and `anthropic` anywhere, and `opus`,
    /// `sonnet`, `haiku`, `fable` as whole words, case-insensitively (FORMAT rule 6).
    static func findTrademark(_ text: String) -> String? {
        let lower = Array(text.lowercased().unicodeScalars)
        func alnum(_ s: Unicode.Scalar) -> Bool { (0x61...0x7A).contains(s.value) || (0x30...0x39).contains(s.value) }
        func index(of word: String, from: Int) -> Int? {
            let w = Array(word.unicodeScalars)
            guard w.count <= lower.count else { return nil }
            var i = from
            while i + w.count <= lower.count {
                if Array(lower[i..<i + w.count]) == w { return i }
                i += 1
            }
            return nil
        }
        for w in trademarkSubstrings where index(of: w, from: 0) != nil { return w }
        for w in trademarkWords {
            var at = 0
            while let hit = index(of: w, from: at) {
                let end = hit + w.unicodeScalars.count
                let before = hit > 0 && alnum(lower[hit - 1])
                let after = end < lower.count && alnum(lower[end])
                if !before && !after { return w }
                at = hit + 1
            }
        }
        return nil
    }

    static func checkTrademark(_ rep: Report, _ where_: String, _ text: String) {
        if let hit = findTrademark(text) {
            rep.err("trademark: \(where_) contains \(PackJson.pyStr(hit)). Third-party marks are not allowed in packs "
                + "(no Anthropic or Claude marks, no model names as branding); see FORMAT.md rule 6")
        }
    }

    /// The first string (keys included, in document order) that carries a trademark.
    static func firstTrademarkString(_ v: PackValue) -> String? {
        switch v {
        case .string(let s):
            return findTrademark(s) == nil ? nil : s
        case .object(let o):
            for k in o.keys {
                if findTrademark(k) != nil { return k }
                if let child = o[k], let hit = firstTrademarkString(child) { return hit }
            }
            return nil
        case .array(let items):
            for item in items {
                if let hit = firstTrademarkString(item) { return hit }
            }
            return nil
        default:
            return nil
        }
    }

    // MARK: - PNG

    static let pngSignature: [UInt8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]

    static func channels(_ colorType: Int) -> Int {
        switch colorType {
        case 0: return 1
        case 2: return 3
        case 3: return 1
        case 4: return 2
        case 6: return 4
        default: return 0
        }
    }

    static func validDepth(_ colorType: Int, _ depth: Int) -> Bool {
        switch colorType {
        case 0: return [1, 2, 4, 8, 16].contains(depth)
        case 2: return [8, 16].contains(depth)
        case 3: return [1, 2, 4, 8].contains(depth)
        case 4, 6: return [8, 16].contains(depth)
        default: return false
        }
    }

    struct PngInfo {
        let width: Int
        let height: Int
        let depth: Int
        let colorType: Int
        let interlace: Int
        let idat: [UInt8]
    }

    private static func readU32(_ d: [UInt8], _ at: Int) -> Int {
        (Int(d[at]) << 24) | (Int(d[at + 1]) << 16) | (Int(d[at + 2]) << 8) | Int(d[at + 3])
    }

    /// Parse a PNG's chunk stream (signature, CRCs, IHDR first, IEND last, nothing after it).
    /// Returns the reason for anything malformed.
    static func parsePng(_ data: [UInt8]) -> Result<PngInfo, PngFailure> {
        guard data.count >= 8, Array(data[0..<8]) == pngSignature else { return .failure(PngFailure("not a PNG (bad signature)")) }
        var pos = 8
        var first = true, seenIend = false
        var idat: [UInt8] = []
        var width = 0, height = 0, depth = 0, ctype = 0, comp = 0, filt = 0, interlace = 0
        while pos < data.count {
            if pos + 8 > data.count { return .failure(PngFailure("truncated chunk header")) }
            let length = readU32(data, pos)
            let tag = String(String.UnicodeScalarView(data[pos + 4..<pos + 8].map { Unicode.Scalar($0) }))
            let end = pos + 8 + length + 4
            if end > data.count { return .failure(PngFailure("truncated \(tag) chunk")) }
            let crc = UInt32(truncatingIfNeeded: readU32(data, end - 4))
            if crc32(data, pos + 4, 4 + length) != crc { return .failure(PngFailure("bad CRC in \(tag) chunk")) }
            let body = pos + 8
            if first {
                if tag != "IHDR" || length != 13 { return .failure(PngFailure("first chunk must be a 13-byte IHDR")) }
                width = readU32(data, body)
                height = readU32(data, body + 4)
                depth = Int(data[body + 8])
                ctype = Int(data[body + 9])
                comp = Int(data[body + 10])
                filt = Int(data[body + 11])
                interlace = Int(data[body + 12])
                first = false
            } else if tag == "IHDR" {
                return .failure(PngFailure("duplicate IHDR"))
            } else if tag == "IDAT" {
                idat.append(contentsOf: data[body..<body + length])
            } else if tag == "IEND" {
                seenIend = true
                pos = end
                break
            }
            pos = end
        }
        if first { return .failure(PngFailure("no IHDR chunk")) }
        if !seenIend { return .failure(PngFailure("missing IEND chunk")) }
        if pos != data.count { return .failure(PngFailure("trailing data after IEND")) }
        if width == 0 || height == 0 { return .failure(PngFailure("zero-sized image")) }
        if channels(ctype) == 0 || !validDepth(ctype, depth) {
            return .failure(PngFailure("invalid color type \(ctype) / bit depth \(depth)"))
        }
        if comp != 0 || filt != 0 || (interlace != 0 && interlace != 1) {
            return .failure(PngFailure("unsupported compression, filter or interlace method"))
        }
        if idat.isEmpty { return .failure(PngFailure("no IDAT chunk")) }
        return .success(PngInfo(width: width, height: height, depth: depth, colorType: ctype, interlace: interlace, idat: idat))
    }

    struct PngFailure: Error {
        let message: String
        init(_ message: String) { self.message = message }
    }

    private static let crcTable: [UInt32] = (0..<256).map { n -> UInt32 in
        var c = UInt32(n)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    /// The CRC-32 PNG chunks carry (zlib's crc32).
    static func crc32(_ data: [UInt8], _ offset: Int, _ count: Int) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        for i in offset..<offset + count { c = crcTable[Int((c ^ UInt32(data[i])) & 0xFF)] ^ (c >> 8) }
        return c ^ 0xFFFF_FFFF
    }

    /// Full PNG check. Returns (w, h), or nil after reporting errors.
    static func checkPng(_ rep: Report, _ rel: String, _ data: [UInt8]) -> (w: Int, h: Int)? {
        if data.count > maxImageBytes {
            rep.err("\(rel): image is \(PackJson.pyF(Double(data.count) / 1048576.0, 1)) MB, limit is 2 MB")
            return nil
        }
        let png: PngInfo
        switch parsePng(data) {
        case .failure(let why):
            rep.err("\(rel): \(why.message)")
            return nil
        case .success(let info):
            png = info
        }
        let w = png.width, h = png.height
        if w > maxImageSide || h > maxImageSide {
            rep.err("\(rel): image is \(w)x\(h), limit is \(maxImageSide) pixels on a side")
            return nil
        }
        if png.interlace == 0 {
            let bits = png.depth * channels(png.colorType)
            let expected = h * (1 + (w * bits + 7) / 8)
            let got: Int
            do {
                got = try PackInflate.inflatedLength(png.idat, limit: expected + 1)
            } catch let e as PackInflate.Corrupt {
                rep.err("\(rel): corrupt image data (\(e.message))")
                return nil
            } catch {
                rep.err("\(rel): corrupt image data (\(error))")
                return nil
            }
            if got != expected {
                rep.err("\(rel): image data is \(got) bytes, expected \(expected) for \(w)x\(h)")
                return nil
            }
        }
        return (w, h)
    }

    static func checkFont(_ rep: Report, _ rel: String, _ data: [UInt8]) {
        if data.count > maxFontBytes {
            rep.err("\(rel): font is \(PackJson.pyF(Double(data.count) / 1048576.0, 1)) MB, limit is 5 MB")
            return
        }
        let magic = Array(data.prefix(4))
        if magic == Array("ttcf".utf8) {
            rep.err("\(rel): font collections (.ttc) are not allowed")
        } else if !(magic == [0, 1, 0, 0] || magic == Array("true".utf8) || magic == Array("OTTO".utf8)) {
            rep.err("\(rel): not a TrueType/OpenType font (bad magic number)")
        }
    }
}

private extension String {
    func hasSuffixBytes(_ suffix: String) -> Bool { utf8.hasSuffixBytes(suffix) }
}

private extension String.UTF8View {
    func hasSuffixBytes(_ suffix: String) -> Bool {
        let s = Array(suffix.utf8)
        return count >= s.count && Array(self).suffix(s.count).elementsEqual(s)
    }
}
