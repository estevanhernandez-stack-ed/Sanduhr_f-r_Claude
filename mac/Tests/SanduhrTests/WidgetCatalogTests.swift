import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// Test-side helpers for the catalog: a throwaway signing key, signature files as `sign_catalog.sh`
/// writes them, and catalogs as `build_catalog.py` writes them. Ported from Windows' WidgetTestKit.cs.
enum CatalogKit {
    static func realCatalog() -> [UInt8] {
        [UInt8](FileManager.default.contents(atPath: WidgetTestKit.fixtures + "/widget-zips/catalog.json")!)
    }

    static func realZip(_ name: String) -> Data {
        FileManager.default.contents(atPath: WidgetTestKit.fixtures + "/widget-zips/" + name)!
    }

    static func spki(_ key: P256.Signing.PrivateKey) -> String {
        key.publicKey.derRepresentation.base64EncodedString()
    }

    /// A signature file: P1363 base64 and one LF.
    static func sigFile(_ key: P256.Signing.PrivateKey, _ catalog: [UInt8]) -> [UInt8] {
        let sig = try! key.signature(for: Data(catalog))
        return Array((sig.rawRepresentation.base64EncodedString() + "\n").utf8)
    }

    static func sha256(_ bytes: [UInt8]) -> String {
        SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
    }

    static func entry(_ id: String, _ name: String, _ kind: String, _ version: String, _ zip: [UInt8], min: String = "3.8.0") -> WidgetCatalogEntry {
        WidgetCatalogEntry(id: id, name: name, kind: kind, version: version, sanduhrMin: min,
                           url: "\(WidgetCatalog.downloadPrefix)v\(version)/\(id)-\(version).zip",
                           sha256: sha256(zip), size: zip.count)
    }

    /// A catalog.json the way build_catalog.py writes it: sorted keys, two-space indent, one LF at the end.
    static func catalog(_ generatedAt: String, _ packs: [WidgetCatalogEntry] = []) -> [UInt8] {
        var s = "{\n  \"generated_at\": \"\(generatedAt)\",\n  \"packs\": ["
        for (i, p) in packs.enumerated() {
            s += i == 0 ? "\n" : ",\n"
            s += "    {\n"
            s += "      \"id\": \"\(p.id)\",\n      \"kind\": \"\(p.kind)\",\n      \"name\": \"\(p.name)\",\n"
            s += "      \"sanduhr_min\": \"\(p.sanduhrMin)\",\n      \"sha256\": \"\(p.sha256)\",\n      \"size\": \(p.size),\n"
            s += "      \"url\": \"\(p.url)\",\n      \"version\": \"\(p.version)\"\n    }"
        }
        s += packs.isEmpty ? "],\n" : "\n  ],\n"
        s += "  \"schema_version\": 1\n}\n"
        return Array(s.utf8)
    }

    /// The catalog with `from` replaced by `to`; fails the test when `from` isn't there.
    static func edit(_ catalog: [UInt8], _ from: String, _ to: String) -> [UInt8] {
        let text = String(decoding: catalog, as: UTF8.self)
        #expect(text.contains(from), "the catalog has no \(from)")
        return Array(text.replacingOccurrences(of: from, with: to).utf8)
    }
}

/// FORMAT 7 rules 1 to 3: the pinned key, the signature, the strict parse, rollback, sanduhr_min, size and hash.
@Suite("Widget catalog")
struct WidgetCatalogTests {
    // MARK: - The pinned key

    @Test func thePinnedKeyImportsAsA256BitP256Key() {
        #expect(!WidgetCatalog.pinnedKeys.isEmpty)
        for spki in WidgetCatalog.pinnedKeys {
            #expect(Data(base64Encoded: spki)?.count == 91)
            #expect(WidgetCatalog.importKey(spki) != nil)
        }
    }

    @Test func theProductionKeyIsKeyA() {
        #expect(WidgetCatalog.pinnedKeys[0]
            == "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEd+Og3mp7woxwUKyFRcqIcFjG2NOFB0jLYme8qLCMYmx7jRCzae6co0SkuQRFN2MoKPAuw5v3kzQLOFFP/nKNkA==")
    }

    @Test func aKeyOnAnotherCurveOrNotAKeyIsRefused() {
        #expect(WidgetCatalog.importKey(P384.Signing.PrivateKey().publicKey.derRepresentation.base64EncodedString()) == nil)
        #expect(WidgetCatalog.importKey("not base64!") == nil)
        #expect(WidgetCatalog.importKey(Data(repeating: 7, count: 91).base64EncodedString()) == nil)
        let der = P256.Signing.PrivateKey().publicKey.derRepresentation
        #expect(WidgetCatalog.importKey((der + [0]).base64EncodedString()) == nil)   // trailing bytes
        #expect(WidgetCatalog.importKey(der.dropLast().base64EncodedString()) == nil)
    }

    // MARK: - Rule 1: the signature

    @Test func aValidSignatureVerifies() {
        let key = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        #expect(WidgetCatalog.verifySignature(cat, [CatalogKit.sigFile(key, cat), nil], [CatalogKit.spki(key)]))
    }

    @Test func aTamperedCatalogFails() {
        let key = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        let sig = CatalogKit.sigFile(key, cat)
        var tampered = cat
        tampered[tampered.count - 2] ^= 1
        #expect(!WidgetCatalog.verifySignature(tampered, [sig], [CatalogKit.spki(key)]))
        // One more byte at the end is a different file too.
        #expect(!WidgetCatalog.verifySignature(cat + [0x0A], [sig], [CatalogKit.spki(key)]))
    }

    @Test func aSignatureByAnotherKeyFails() {
        let signer = P256.Signing.PrivateKey()
        let pinned = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        #expect(!WidgetCatalog.verifySignature(cat, [CatalogKit.sigFile(signer, cat)], [CatalogKit.spki(pinned)]))
        // And the production key does not verify a test key's signature.
        #expect(!WidgetCatalog.verifySignature(cat, [CatalogKit.sigFile(signer, cat)], WidgetCatalog.pinnedKeys))
    }

    @Test(arguments: [63, 65, 0])
    func aSignatureOfTheWrongLengthIsInvalid(length: Int) {
        let bytes = (0..<length).map { _ in UInt8.random(in: 0...255) }
        #expect(WidgetCatalog.decodeSignature(Array((Data(bytes).base64EncodedString() + "\n").utf8)) == nil)
    }

    @Test func aDerSignatureIsNotAP1363Signature() throws {
        let key = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        let der = try key.signature(for: Data(cat)).derRepresentation
        #expect(!WidgetCatalog.verifySignature(cat, [Array((der.base64EncodedString() + "\n").utf8)], [CatalogKit.spki(key)]))
    }

    @Test func signatureFileWhitespaceIsTrimmedButASecondLineOrOddCharactersAreNot() {
        let key = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        var b64 = String(decoding: CatalogKit.sigFile(key, cat), as: UTF8.self)
        b64.removeLast()
        let spki = CatalogKit.spki(key)
        #expect(WidgetCatalog.verifySignature(cat, [Array(b64.utf8)], [spki]))
        #expect(WidgetCatalog.verifySignature(cat, [Array(("  " + b64 + " \r\n\n").utf8)], [spki]))
        #expect(!WidgetCatalog.verifySignature(cat, [Array((b64.prefix(40) + "\n" + b64.dropFirst(40)).utf8)], [spki]))
        #expect(WidgetCatalog.decodeSignature(Array(("-" + b64.dropFirst()).utf8)) == nil)   // URL-safe alphabet
        #expect(WidgetCatalog.decodeSignature(Array(("é" + b64).utf8)) == nil)
    }

    @Test func sig2AloneVerifiesAndABadSigBesideAGoodSig2StillVerifies() {
        let oldKey = P256.Signing.PrivateKey()
        let newKey = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        let pinned = [CatalogKit.spki(oldKey), CatalogKit.spki(newKey)]
        #expect(WidgetCatalog.verifySignature(cat, [nil, CatalogKit.sigFile(newKey, cat)], pinned))
        #expect(WidgetCatalog.verifySignature(cat, [Array("garbage\n".utf8), CatalogKit.sigFile(newKey, cat)], pinned))
        // A client that pins only the old key reads only .sig (rotation step 2).
        #expect(WidgetCatalog.verifySignature(cat, [CatalogKit.sigFile(oldKey, cat), CatalogKit.sigFile(newKey, cat)], [CatalogKit.spki(oldKey)]))
        #expect(!WidgetCatalog.verifySignature(cat, [nil, nil], pinned))
        #expect(!WidgetCatalog.verifySignature(cat, [CatalogKit.sigFile(newKey, cat)], []))
    }

    // MARK: - The strict parse

    @Test func theRealCatalogParses() throws {
        let doc = try WidgetCatalog.parse(CatalogKit.realCatalog()).get()
        #expect(doc.generatedAtText == "2026-10-22T02:23:13Z")
        #expect(doc.generatedAt == Date(timeIntervalSince1970: 1_792_635_793))
        #expect(doc.packs.map(\.id) == ["hourglass", "oled-neon", "oled-neon-effects", "oled-neon-palettes"])
        let h = doc.packs[0]
        #expect(h.name == "Hourglass")
        #expect(h.kind == "sprite")
        #expect(h.version == "1.0.0")
        #expect(h.sanduhrMin == "3.8.0")
        #expect(h.size == 34521)
        #expect(h.sha256 == "551c75b56192b045a02b307aafffdac24b5965d4626c1e5b37fb14182c48fd67")
    }

    private static let hourglassUrl = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip"
    private static let hourglassSha = "551c75b56192b045a02b307aafffdac24b5965d4626c1e5b37fb14182c48fd67"

    @Test(arguments: [
        ("\"schema_version\": 1", "\"schema_version\": 2"),
        ("\"schema_version\": 1", "\"schema_version\": 1.0"),
        ("\"schema_version\": 1\n", "\"schema_version\": 1,\n  \"extra\": true\n"),
        ("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-10-22 02:23:13\""),
        ("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-13-22T02:23:13Z\""),
        ("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-02-30T02:23:13Z\""),
        ("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-10-22T02:23:13+00:00\""),
        ("\"kind\": \"sprite\"", "\"kind\": \"script\""),
        ("\"id\": \"hourglass\"", "\"id\": \"Hourglass\""),
        ("\"version\": \"1.0.0\"\n    },\n    {\n      \"id\": \"oled-neon\"", "\"version\": \"1.0\"\n    },\n    {\n      \"id\": \"oled-neon\""),
        ("\"sanduhr_min\": \"3.8.0\"", "\"sanduhr_min\": \"3.8\""),
        ("\"size\": 34521", "\"size\": 0"),
        ("\"size\": 34521", "\"size\": 34521.0"),
        ("\"size\": 34521", "\"size\": \"34521\""),
        ("\"size\": 34521", "\"size\": 99999999999"),
        (hourglassSha, hourglassSha.uppercased()),
        (hourglassSha, "551c75b5"),
        (hourglassUrl, "https://evil.example/hourglass-1.0.0.zip"),
        (hourglassUrl, "http://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip"),
        (hourglassUrl, "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/oled-neon-1.0.0.zip"),
        (hourglassUrl, "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/../../x/hourglass-1.0.0.zip"),
        (hourglassUrl, "https://github.com/626Labs-LLC/other-repo/releases/download/v1.0.0/hourglass-1.0.0.zip"),
        ("\"name\": \"Hourglass\"", "\"name\": \"Hourglass\",\n      \"name\": \"Twice\""),
        ("\"name\": \"Hourglass\"", "\"name\": \"Hour\\u0007glass\""),
        ("\"name\": \"Hourglass\"", "\"name\": \"\""),
        ("\"id\": \"oled-neon\"", "\"id\": \"aaa-first\""),   // out of order
        ("\"id\": \"oled-neon-effects\"", "\"id\": \"oled-neon\""),   // twice
    ])
    func aMalformedCatalogIsRefused(from: String, to: String) {
        let bytes = CatalogKit.edit(CatalogKit.realCatalog(), from, to)
        switch WidgetCatalog.parse(bytes) {
        case .success: Issue.record("the catalog was accepted after \(from) became \(to)")
        case .failure(let e): #expect(!e.message.isEmpty)
        }
    }

    @Test func aByteOrderMarkBadUtf8OrNonObjectIsRefused() {
        func refused(_ bytes: [UInt8]) -> Bool {
            if case .failure = WidgetCatalog.parse(bytes) { return true }
            return false
        }
        let real = CatalogKit.realCatalog()
        #expect(refused([0xEF, 0xBB, 0xBF] + real))
        #expect(refused(Array(real.dropLast(2)) + [0xC3, 0x0A]))
        #expect(refused(Array("[]".utf8)))
        #expect(refused([]))
        #expect(refused([UInt8](repeating: 0, count: WidgetCatalog.maxCatalogBytes + 1)))
    }

    @Test func moreThanAThousandPacksIsRefused() {
        var s = "{\n  \"generated_at\": \"2026-10-22T00:00:00Z\",\n  \"packs\": ["
        s += (0..<1001).map { _ in "{}" }.joined(separator: ",")
        s += "],\n  \"schema_version\": 1\n}\n"
        guard case .failure(let e) = WidgetCatalog.parse(Array(s.utf8)) else { Issue.record("accepted"); return }
        #expect(e.message.contains("1000"))
    }

    @Test func anEmptyPackListIsACatalog() throws {
        let doc = try WidgetCatalog.parse(CatalogKit.catalog("2026-10-22T00:00:00Z")).get()
        #expect(doc.packs.isEmpty)
    }

    // MARK: - Rule 2: rollback

    @Test func olderThanTheNewestSeenIsARollbackAndTheSameOneIsNot() {
        let seen = Date(timeIntervalSince1970: 1_792_635_793)
        #expect(WidgetCatalog.isRollback(seen.addingTimeInterval(-1), newestSeen: seen))
        #expect(!WidgetCatalog.isRollback(seen, newestSeen: seen))
        #expect(!WidgetCatalog.isRollback(seen.addingTimeInterval(86400), newestSeen: seen))
        #expect(!WidgetCatalog.isRollback(seen, newestSeen: nil))
    }

    // MARK: - Rule 3: sanduhr_min, size, sha256

    @Test(arguments: [
        ("3.8.0", "3.8.0", true),
        ("3.8.0", "3.7.2", false),
        ("3.8.0", "3.10.0", true),
        ("3.8.1", "3.8.0", false),
        ("4.0.0", "3.99.99", false),
        ("3.10.0", "3.9.0", false),
        ("3.8", "9.9.9", false),
        ("v3.8.0", "9.9.9", false),
        ("03.8.0", "9.9.9", false),
    ])
    func sanduhrMinComparesNumerically(min: String, supported: String, met: Bool) {
        #expect(WidgetCatalog.meetsMinimum(min, supported: supported) == met)
    }

    @Test func theFormatLevelIsPinnedAndEveryFixturePackMeetsIt() throws {
        #expect(PackFormat.supported == "3.8.0")
        let doc = try WidgetCatalog.parse(CatalogKit.realCatalog()).get()
        #expect(!doc.packs.isEmpty)
        for p in doc.packs { #expect(WidgetCatalog.meetsMinimum(p.sanduhrMin), "\(p.id) needs \(p.sanduhrMin)") }
        // The app's own version never enters into it.
        #expect(!WidgetCatalog.meetsMinimum("3.8.1"))
    }

    @Test func sizeAndHashAreCheckedBeforeTheZipIsOpened() {
        let zip = Array("pretend zip".utf8)
        let entry = CatalogKit.entry("good-palette", "P", "palette", "1.0.0", zip)
        #expect(WidgetCatalog.checkDownload(entry, zip) == nil)
        #expect(WidgetCatalog.checkDownload(entry, zip + [0])?.contains("bytes") == true)
        var flipped = zip
        flipped[0] ^= 1
        #expect(WidgetCatalog.checkDownload(entry, flipped)?.contains("SHA-256") == true)
    }

    @Test(arguments: [
        ("https://github.com/626Labs-LLC/sanduhr-widgets/releases/latest/download/catalog.json", true),
        ("https://objects.githubusercontent.com/github-production-release-asset/x", true),
        ("https://release-assets.githubusercontent.com/github-production-release-asset/x", true),
        ("http://github.com/x", false),
        ("https://github.com:8443/x", false),
        ("https://user:pw@github.com/x", false),
        ("https://github.com.evil.example/x", false),
        ("https://raw.githubusercontent.com/x", false),
    ])
    func redirectsMayLandOnlyOnGitHubOverHttps(url: String, allowed: Bool) {
        #expect(WidgetCatalog.isAllowedUrl(URL(string: url)) == allowed)
    }

    @Test func theZipReaderUsesTheCatalogsLimit() {
        #expect(WidgetZip.maxZipBytes == WidgetCatalog.maxZipBytes)
        #expect(WidgetCatalog.maxZipBytes == 10 * 1024 * 1024 + 256 * 1024)
    }
}
