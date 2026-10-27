import CryptoKit
import Foundation

/// The widget pack format level this build understands, written in Windows' version numbering.
/// The catalog states `sanduhr_min` in those numbers and Windows' parser refuses any extra entry
/// key, so the Mac can't ask the catalog for a Mac field. A pack is installable when its
/// `sanduhr_min` is at or below this constant, never when compared with the app's own version.
/// It moves only when the Mac gains a newer format's features; a test pins it.
enum PackFormat {
    static let supported = "3.8.0"
}

/// One pack the catalog lists (FORMAT section 7): exactly the eight keys `build_catalog.py` writes.
struct WidgetCatalogEntry: Equatable {
    let id: String
    let name: String
    let kind: String
    let version: String
    let sanduhrMin: String
    let url: String
    let sha256: String
    let size: Int
}

/// A verified, parsed `catalog.json`. `generatedAtText` is the file's own string, kept for the
/// rollback marker so the comparison never depends on a round trip through a date type.
struct WidgetCatalogDocument: Equatable {
    let generatedAt: Date
    let generatedAtText: String
    let packs: [WidgetCatalogEntry]
}

/// The widget catalog's pure half (FORMAT section 7, spec 1): the pinned keys, signature
/// verification over the exact downloaded bytes, the strict catalog parser, the rollback check,
/// `sanduhr_min`, and the size and SHA-256 check on a downloaded zip. No disk, no network:
/// `WidgetCatalogClient` fetches, this decides. A port of Windows' WidgetCatalog.cs.
///
/// Client rules, in order, stopping at the first failure: (1) verify the signature against the
/// embedded keys before parsing anything, (2) reject a rollback by `generated_at`, (3) check
/// `sanduhr_min`, then `size` and `sha256` before opening the zip, (4) extract safely
/// (`WidgetZip`), (5) re-validate locally (`PackValidator`, strict), (6) install atomically.
enum WidgetCatalog {
    /// The catalog's public keys: DER SubjectPublicKeyInfo, base64, ECDSA P-256. A list, for rotation
    /// (FORMAT 7, Key rotation): a catalog verifies when any key here verifies either signature file.
    /// Key A, the same bytes as Windows' `PinnedKeys`; the private half lives only in the
    /// `sanduhr-widgets` repository secret `SIGNING_KEY`.
    static let pinnedKeys = [
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEd+Og3mp7woxwUKyFRcqIcFjG2NOFB0jLYme8qLCMYmx7jRCzae6co0SkuQRFN2MoKPAuw5v3kzQLOFFP/nKNkA==",
    ]

    /// Where the newest catalog and its signatures are fetched from: the latest release's assets.
    static let latestBase = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/latest/download/"
    /// Every pack URL a catalog may name starts with this (the tag and file name follow).
    static let downloadPrefix = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/"

    static let catalogFile = "catalog.json"
    static let signatureFile = "catalog.json.sig"
    static let secondSignatureFile = "catalog.json.sig2"

    /// A catalog is a few hundred bytes a pack; 1 MB is thousands of packs.
    static let maxCatalogBytes = 1024 * 1024
    /// A signature file is 88 base64 characters and a newline.
    static let maxSignatureFileBytes = 1024
    static let signatureBytes = 64
    static let maxPacks = 1000

    /// A pack zip: the 10 MB of files FORMAT 3 allows plus room for 67 entries' zip headers.
    static let maxZipBytes = PackValidator.maxPackBytes + 256 * 1024

    /// The hosts a catalog fetch may be redirected to: GitHub, and the hosts GitHub serves release
    /// assets from. Anything else (and anything but https) stops the fetch.
    static let allowedHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"]

    private static let catalogKeys = ["generated_at", "packs", "schema_version"]
    private static let entryKeys = ["id", "kind", "name", "sanduhr_min", "sha256", "size", "url", "version"]

    // MARK: - Rule 1: the signature

    /// A signature file's 64 bytes, or nil. The file is base64 (standard alphabet, padded) on one
    /// line; surrounding whitespace is stripped first. Anything else (another character, a second
    /// line, a length other than 64 decoded bytes) is not a signature.
    static func decodeSignature(_ file: [UInt8]) -> [UInt8]? {
        guard !file.isEmpty, file.count <= maxSignatureFileBytes else { return nil }
        guard !file.contains(where: { $0 > 0x7e }) else { return nil }
        var text = file[...]
        let space: Set<UInt8> = [0x20, 0x09, 0x0D, 0x0A]
        while let f = text.first, space.contains(f) { text = text.dropFirst() }
        while let l = text.last, space.contains(l) { text = text.dropLast() }
        guard !text.isEmpty else { return nil }
        for c in text {
            let ok = (c >= 0x41 && c <= 0x5A) || (c >= 0x61 && c <= 0x7A) || (c >= 0x30 && c <= 0x39) || c == 0x2B || c == 0x2F || c == 0x3D
            if !ok { return nil }
        }
        guard let decoded = Data(base64Encoded: Data(text)), decoded.count == signatureBytes else { return nil }
        return [UInt8](decoded)
    }

    /// Imports a pinned key. Nil unless it is a DER SubjectPublicKeyInfo for a NIST P-256 key with
    /// no trailing bytes (CryptoKit refuses other curves and RSA itself; the length pins the rest).
    static func importKey(_ spkiBase64: String) -> P256.Signing.PublicKey? {
        guard let der = Data(base64Encoded: spkiBase64), der.count == 91 else { return nil }
        return try? P256.Signing.PublicKey(derRepresentation: der)
    }

    /// True when any of `signatureFiles` (the raw `.sig` and `.sig2` bytes; a missing file is nil)
    /// is a valid ECDSA P-256 / SHA-256 signature, IEEE P1363 form, over exactly `catalog`, by any
    /// of `pinnedKeys`. The keys are a parameter so tests sign with a throwaway key; the app passes
    /// `pinnedKeys`. Never throws.
    static func verifySignature(_ catalog: [UInt8], _ signatureFiles: [[UInt8]?], _ keys: [String]) -> Bool {
        guard !keys.isEmpty else { return false }
        let sigs = signatureFiles.compactMap { $0 }.compactMap { decodeSignature($0) }
        guard !sigs.isEmpty else { return false }
        let data = Data(catalog)
        for spki in keys {
            guard let key = importKey(spki) else { continue }
            for sig in sigs {
                if let parsed = try? P256.Signing.ECDSASignature(rawRepresentation: sig),
                   key.isValidSignature(parsed, for: data) {
                    return true
                }
            }
        }
        return false
    }

    // MARK: - After the signature: the strict parse

    /// Parses a catalog whose signature already verified. Strict, as FORMAT 7 writes it: UTF-8
    /// without a byte-order mark, no duplicate keys, exactly the documented keys, `schema_version`
    /// 1, `generated_at` as `YYYY-MM-DDTHH:MM:SSZ`, packs sorted by id with no id twice, and every
    /// pack's URL under this repository's releases, ending in `<id>-<version>.zip`.
    static func parse(_ bytes: [UInt8]) -> Result<WidgetCatalogDocument, WidgetCatalogError> {
        func fail(_ m: String) -> Result<WidgetCatalogDocument, WidgetCatalogError> { .failure(WidgetCatalogError(message: m)) }
        if bytes.isEmpty { return fail("the catalog is empty") }
        if bytes.count > maxCatalogBytes { return fail("the catalog is larger than 1 MB") }
        if bytes.count >= 3, bytes[0] == 0xEF, bytes[1] == 0xBB, bytes[2] == 0xBF {
            return fail("the catalog starts with a byte-order mark")
        }
        guard let scalars = PackJson.decodeUTF8(bytes) else { return fail("the catalog is not UTF-8") }
        let root: PackValue
        switch PackJson.parse(scalars) {
        case .failure(let e): return fail("the catalog is not valid JSON (" + e.message + ")")
        case .success(let v): root = v
        }
        guard let o = root.object else { return fail("the catalog is not a JSON object") }
        guard sameKeys(o, catalogKeys) else {
            return fail("the catalog's keys must be exactly " + catalogKeys.joined(separator: ", "))
        }
        guard let schema = o["schema_version"]?.number, schema.isInt, schema.value == 1 else {
            return fail("the catalog's schema_version must be 1")
        }
        guard let stamp = o["generated_at"]?.string, let generatedAt = parseStamp(stamp) else {
            return fail("the catalog's generated_at must be a UTC time written YYYY-MM-DDTHH:MM:SSZ")
        }
        guard let list = o["packs"]?.array else { return fail("the catalog's packs must be an array") }
        if list.count > maxPacks { return fail("the catalog lists more than \(maxPacks) packs") }
        var packs: [WidgetCatalogEntry] = []
        for (i, item) in list.enumerated() {
            switch parseEntry(item) {
            case .failure(let e):
                return fail("catalog pack \(i): \(e.message)")
            case .success(let entry):
                if let last = packs.last, !ordinalLess(last.id, entry.id) {
                    return fail("catalog pack \(i): packs must be sorted by id, each id once ('\(entry.id)' after '\(last.id)')")
                }
                packs.append(entry)
            }
        }
        return .success(WidgetCatalogDocument(generatedAt: generatedAt, generatedAtText: stamp, packs: packs))
    }

    private static func parseEntry(_ value: PackValue) -> Result<WidgetCatalogEntry, WidgetCatalogError> {
        func fail(_ m: String) -> Result<WidgetCatalogEntry, WidgetCatalogError> { .failure(WidgetCatalogError(message: m)) }
        guard let e = value.object else { return fail("not a JSON object") }
        guard sameKeys(e, entryKeys) else { return fail("keys must be exactly " + entryKeys.joined(separator: ", ")) }
        var strings: [String: String] = [:]
        for k in entryKeys where k != "size" {
            guard let s = e[k]?.string else { return fail("\(k) must be a string") }
            if s.unicodeScalars.contains(where: { $0.value < 0x20 || $0.value == 0x7F }) {
                return fail("\(k) has a control character")
            }
            strings[k] = s
        }
        let id = strings["id"]!, name = strings["name"]!, kind = strings["kind"]!, version = strings["version"]!
        let min = strings["sanduhr_min"]!, url = strings["url"]!, sha = strings["sha256"]!
        if id.utf16.count < 3 || id.utf16.count > 40 || !isId(id) {
            return fail("id must be lowercase kebab-case, 3 to 40 characters")
        }
        if name.utf16.count < 1 || name.utf16.count > 40 { return fail("name must be 1 to 40 characters") }
        if !PackValidator.kinds.contains(kind) {
            return fail("kind must be one of " + PackValidator.kinds.joined(separator: ", "))
        }
        if semver(version) == nil { return fail("version must be MAJOR.MINOR.PATCH") }
        if semver(min) == nil { return fail("sanduhr_min must be MAJOR.MINOR.PATCH") }
        if !isSha256(sha) { return fail("sha256 must be 64 lowercase hex characters") }
        guard let n = e["size"]?.number, n.isInt, n.isFinite, n.value >= 1, n.value <= Double(maxZipBytes) else {
            return fail("size must be an integer from 1 to \(maxZipBytes)")
        }
        if !isPackUrl(url, id: id, version: version) {
            return fail("url must be \(downloadPrefix)<tag>/\(id)-\(version).zip")
        }
        return .success(WidgetCatalogEntry(id: id, name: name, kind: kind, version: version, sanduhrMin: min,
                                           url: url, sha256: sha, size: Int(n.value)))
    }

    /// A pack URL: this repository's release download, one tag segment, then exactly `id-version.zip`.
    static func isPackUrl(_ url: String, id: String, version: String) -> Bool {
        guard url.hasPrefix(downloadPrefix) else { return false }
        let rest = url.dropFirst(downloadPrefix.count)
        guard let slash = rest.firstIndex(of: "/"), slash != rest.startIndex else { return false }
        let tag = rest[..<slash], file = rest[rest.index(after: slash)...]
        return isTag(tag) && tag != "." && tag != ".." && file == "\(id)-\(version).zip"
    }

    /// True for an https URL whose host may serve the catalog or a pack (a redirect target included).
    static func isAllowedUrl(_ url: URL?) -> Bool {
        guard let url, url.scheme?.lowercased() == "https", url.port == nil || url.port == 443,
              url.user == nil, url.password == nil, let host = url.host?.lowercased() else { return false }
        return allowedHosts.contains(host)
    }

    private static func sameKeys(_ o: PackObject, _ keys: [String]) -> Bool {
        o.count == keys.count && keys.allSatisfy { o.has($0) }
    }

    /// Ordinal (UTF-8 byte) order, as `string.CompareOrdinal` sorts the ASCII ids.
    private static func ordinalLess(_ a: String, _ b: String) -> Bool {
        a.utf8.lexicographicallyPrecedes(b.utf8)
    }

    private static func isLower(_ c: UInt8) -> Bool { c >= 0x61 && c <= 0x7A }
    private static func isDigit(_ c: UInt8) -> Bool { c >= 0x30 && c <= 0x39 }

    /// `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`
    private static func isId(_ s: String) -> Bool {
        let b = Array(s.utf8)
        guard let first = b.first, isLower(first) else { return false }
        let segments = b.split(separator: 0x2D, omittingEmptySubsequences: false)
        return segments.allSatisfy { !$0.isEmpty && $0.allSatisfy { isLower($0) || isDigit($0) } }
    }

    /// `^[0-9a-f]{64}$`
    private static func isSha256(_ s: String) -> Bool {
        s.utf8.count == 64 && s.utf8.allSatisfy { isDigit($0) || ($0 >= 0x61 && $0 <= 0x66) }
    }

    /// `^[A-Za-z0-9][A-Za-z0-9._-]*$`
    private static func isTag(_ s: Substring) -> Bool {
        let b = Array(s.utf8)
        guard let first = b.first, isLower(first) || isDigit(first) || (first >= 0x41 && first <= 0x5A) else { return false }
        return b.allSatisfy { isLower($0) || isDigit($0) || ($0 >= 0x41 && $0 <= 0x5A) || $0 == 0x2E || $0 == 0x5F || $0 == 0x2D }
    }

    /// MAJOR.MINOR.PATCH with no leading zeros, as numbers (a part too big for an Int is `Int.max`),
    /// or nil.
    private static func semver(_ s: String?) -> (Int, Int, Int)? {
        guard let s else { return nil }
        let parts = s.utf8.split(separator: 0x2E, omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        var out: [Int] = []
        for p in parts {
            guard !p.isEmpty, p.allSatisfy(isDigit), p.count == 1 || p.first != 0x30 else { return nil }
            out.append(Int(String(decoding: p, as: UTF8.self)) ?? Int.max)
        }
        return (out[0], out[1], out[2])
    }

    /// `YYYY-MM-DDTHH:MM:SSZ` as UTC, or nil.
    static func parseStamp(_ stamp: String?) -> Date? {
        guard let stamp else { return nil }
        let b = Array(stamp.utf8)
        guard b.count == 20 else { return nil }
        for (i, c) in b.enumerated() {
            switch i {
            case 4, 7: if c != 0x2D { return nil }
            case 10: if c != 0x54 { return nil }
            case 13, 16: if c != 0x3A { return nil }
            case 19: if c != 0x5A { return nil }
            default: if !isDigit(c) { return nil }
            }
        }
        func num(_ from: Int, _ to: Int) -> Int { b[from..<to].reduce(0) { $0 * 10 + Int($1 - 0x30) } }
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let parts = DateComponents(calendar: cal, timeZone: cal.timeZone, year: num(0, 4), month: num(5, 7), day: num(8, 10),
                                   hour: num(11, 13), minute: num(14, 16), second: num(17, 19))
        guard parts.year! >= 1, parts.hour! <= 23, parts.minute! <= 59, parts.second! <= 59,
              let date = cal.date(from: parts) else { return nil }
        // Calendar is lenient (month 13, February 30 roll over): the date must read back the same.
        let back = cal.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        guard back.year == parts.year, back.month == parts.month, back.day == parts.day,
              back.hour == parts.hour, back.minute == parts.minute, back.second == parts.second else { return nil }
        return date
    }

    // MARK: - Rule 2: rollback

    /// True when `generatedAt` is older than the newest catalog already seen. The same catalog
    /// again is not a rollback.
    static func isRollback(_ generatedAt: Date, newestSeen: Date?) -> Bool {
        guard let newestSeen else { return false }
        return generatedAt < newestSeen
    }

    // MARK: - Rule 3: sanduhr_min, size, sha256

    /// True when `supported` (a pack-format level, `PackFormat.supported`) is at least `sanduhrMin`.
    /// A minimum that is not MAJOR.MINOR.PATCH is never met.
    static func meetsMinimum(_ sanduhrMin: String, supported: String = PackFormat.supported) -> Bool {
        guard let need = semver(sanduhrMin), let have = semver(supported) else { return false }
        return compare(have, need) >= 0
    }

    private static func compare(_ a: (Int, Int, Int), _ b: (Int, Int, Int)) -> Int {
        if a.0 != b.0 { return a.0 < b.0 ? -1 : 1 }
        if a.1 != b.1 { return a.1 < b.1 ? -1 : 1 }
        if a.2 != b.2 { return a.2 < b.2 ? -1 : 1 }
        return 0
    }

    /// Compares two MAJOR.MINOR.PATCH versions; a malformed one sorts first.
    static func compareVersions(_ a: String?, _ b: String?) -> Int {
        compare(semver(a) ?? (-1, -1, -1), semver(b) ?? (-1, -1, -1))
    }

    /// Checks a downloaded zip against its catalog entry: the exact size, then the SHA-256 (compared
    /// in constant time). Nil when both match, otherwise the reason. Run before the zip is opened.
    static func checkDownload(_ entry: WidgetCatalogEntry, _ zip: [UInt8]) -> String? {
        guard zip.count == entry.size else {
            return "the download is \(zip.count) bytes, the catalog says \(entry.size)"
        }
        let hex = SHA256.hash(data: zip).map { String(format: "%02x", $0) }.joined()
        return constantTimeEqual(Array(hex.utf8), Array(entry.sha256.utf8))
            ? nil
            : "the download's SHA-256 does not match the catalog"
    }

    private static func constantTimeEqual(_ a: [UInt8], _ b: [UInt8]) -> Bool {
        guard a.count == b.count else { return false }
        var diff: UInt8 = 0
        for i in 0..<a.count { diff |= a[i] ^ b[i] }
        return diff == 0
    }
}

/// A catalog the parser refused, with the reason for the log.
struct WidgetCatalogError: Error, Equatable {
    let message: String
}
