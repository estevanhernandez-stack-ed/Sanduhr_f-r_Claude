import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// A `URLProtocol` that answers from a table and records what it was asked. No request leaves the process.
final class StubProtocol: URLProtocol {
    struct Reply {
        var status = 200
        var headers: [String: String] = [:]
        var body = Data()
    }

    private static let lock = NSLock()
    nonisolated(unsafe) private static var replies: [String: Reply] = [:]
    nonisolated(unsafe) private static var seen: [URLRequest] = []

    static func reset(_ table: [String: Reply]) {
        lock.lock(); defer { lock.unlock() }
        replies = table
        seen = []
    }

    static var requests: [URLRequest] {
        lock.lock(); defer { lock.unlock() }
        return seen
    }

    static func session() -> URLSession {
        let c = WidgetDownloader.configuration()
        c.protocolClasses = [StubProtocol.self]
        return URLSession(configuration: c)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func stopLoading() {}

    override func startLoading() {
        Self.lock.lock()
        Self.seen.append(request)
        let reply = Self.replies[request.url!.absoluteString]
        Self.lock.unlock()
        guard let reply else {
            client?.urlProtocol(self, didFailWithError: URLError(.cannotFindHost))
            return
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: reply.status, httpVersion: "HTTP/1.1", headerFields: reply.headers)!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: reply.body)
        client?.urlProtocolDidFinishLoading(self)
    }
}

private final class Marker: WidgetCatalogMarker {
    var newest: Date?
    var remembered: [String] = []
    func newestCatalogSeen() -> Date? { newest }
    func rememberCatalog(_ generatedAtText: String) { remembered.append(generatedAtText) }
}

/// The network half, against a stub: redirect rules, the size cap, headers. Serialized because the
/// stub's table is shared.
@Suite("Widget downloader", .serialized)
struct WidgetDownloaderTests {
    private static let start = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/latest/download/catalog.json"
    private static let hop1 = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/a.zip"
    private static let asset = "https://objects.githubusercontent.com/asset/a"
    private static let body = Data("hello".utf8)

    private func downloader(_ table: [String: StubProtocol.Reply]) -> WidgetDownloader {
        StubProtocol.reset(table)
        return WidgetDownloader(session: StubProtocol.session())
    }

    private func redirect(to url: String) -> StubProtocol.Reply {
        StubProtocol.Reply(status: 302, headers: ["Location": url])
    }

    private func refusal(_ d: WidgetDownloader, _ url: String = start, _ max: Int = 1000) async -> String? {
        do {
            _ = try await d.fetch(url, max)
            return nil
        } catch let e as WidgetFetchError {
            return e.message
        } catch {
            return "other: \(error)"
        }
    }

    @Test func aPlainBodyComesBack() async throws {
        let d = downloader([Self.start: .init(body: Self.body)])
        #expect(try await d.fetch(Self.start, 1000) == Self.body)
    }

    @Test func aRedirectChainToGitHubsAssetHostsIsFollowed() async throws {
        let d = downloader([
            Self.start: redirect(to: Self.hop1),
            Self.hop1: redirect(to: Self.asset),
            Self.asset: .init(body: Self.body),
        ])
        #expect(try await d.fetch(Self.start, 1000) == Self.body)
        #expect(StubProtocol.requests.count == 3)
    }

    @Test func aRelativeLocationResolvesAgainstTheCurrentUrl() async throws {
        let d = downloader([
            Self.start: redirect(to: "/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/a.zip"),
            Self.hop1: .init(body: Self.body),
        ])
        #expect(try await d.fetch(Self.start, 1000) == Self.body)
    }

    @Test(arguments: [
        "https://evil.example/a",
        "http://github.com/a",
        "https://github.com:8443/a",
        "https://raw.githubusercontent.com/a",
    ])
    func aRedirectOffTheAllowedHostsStopsTheFetch(target: String) async {
        let d = downloader([Self.start: redirect(to: target), target: .init(body: Self.body)])
        #expect(await refusal(d) != nil)
        #expect(StubProtocol.requests.count == 1, "the off-list URL was requested")
    }

    @Test func aStartingUrlOffTheListIsNeverRequested() async {
        let d = downloader([:])
        #expect(await refusal(d, "https://evil.example/catalog.json") != nil)
        #expect(await refusal(d, "http://github.com/x") != nil)
        #expect(StubProtocol.requests.isEmpty)
    }

    @Test func fiveRedirectsAreFollowedAndASixthStops() async throws {
        func chain(_ hops: Int) -> [String: StubProtocol.Reply] {
            var t: [String: StubProtocol.Reply] = [:]
            func u(_ i: Int) -> String { i == 0 ? Self.start : "https://github.com/hop\(i)" }
            for i in 0..<hops { t[u(i)] = redirect(to: u(i + 1)) }
            t[u(hops)] = .init(body: Self.body)
            return t
        }
        #expect(try await downloader(chain(5)).fetch(Self.start, 1000) == Self.body)
        let message = await refusal(downloader(chain(6)))
        #expect(message == "too many redirects")
    }

    @Test func aRedirectWithoutALocationStopsTheFetch() async {
        let d = downloader([Self.start: .init(status: 302)])
        #expect(await refusal(d) == "a redirect without a location")
    }

    @Test func aMissingFileIsNilAndOtherErrorsThrow() async throws {
        let d = downloader([Self.start: .init(status: 404), Self.hop1: .init(status: 500)])
        #expect(try await d.fetch(Self.start, 1000) == nil)
        #expect(await refusal(d, Self.hop1) == "HTTP 500")
    }

    @Test func aBodyOverTheCapIsRefusedWhileStreaming() async throws {
        // No Content-Length: the cap is enforced as bytes arrive.
        let d = downloader([Self.start: .init(body: Data(repeating: 1, count: 11))])
        #expect(await refusal(d, Self.start, 10) == "the file is larger than allowed")
        #expect(try await d.fetch(Self.start, 11)?.count == 11)
    }

    @Test func aDeclaredLengthOverTheCapIsRefusedBeforeReading() async {
        let d = downloader([Self.start: .init(headers: ["Content-Length": "5000"], body: Data(repeating: 1, count: 5000))])
        #expect(await refusal(d, Self.start, 4999) == "the file is larger than allowed")
    }

    @Test func requestsCarryTheSanduhrAgentAndNoCookies() async throws {
        let d = downloader([Self.start: .init(body: Self.body)])
        _ = try await d.fetch(Self.start, 1000)
        let r = try #require(StubProtocol.requests.first)
        #expect(r.value(forHTTPHeaderField: "User-Agent") == "Sanduhr")
        #expect(r.value(forHTTPHeaderField: "Cookie") == nil)
        #expect(r.httpShouldHandleCookies == false)
    }

    @Test func theSessionIsEphemeralWithNoCookiesOrCache() {
        let c = WidgetDownloader.configuration()
        #expect(c.httpCookieStorage == nil)
        #expect(c.httpShouldSetCookies == false)
        #expect(c.urlCache == nil)
        #expect(c.httpAdditionalHeaders?["User-Agent"] as? String == "Sanduhr")
        #expect(WidgetDownloader.maxRedirects == 5)
        #expect(WidgetDownloader.smallFileTimeout == 20)
        #expect(WidgetDownloader.zipTimeout == 90)
    }
}

/// The client's steps in order, over a dictionary of bytes (no network at all).
@Suite("Widget catalog client")
struct WidgetCatalogClientTests {
    private let base = "https://test.invalid/"

    private func client(_ files: [String: Data], key: P256.Signing.PrivateKey, marker: Marker? = nil) -> WidgetCatalogClient {
        WidgetCatalogClient(fetch: { url, _ in
            if url.hasPrefix("fail:") { throw WidgetFetchError(message: "down") }
            return files[url]
        }, marker: marker, pinnedKeys: [CatalogKit.spki(key)], baseUrl: base)
    }

    private func signed(_ catalog: [UInt8], _ key: P256.Signing.PrivateKey) -> [String: Data] {
        [base + "catalog.json": Data(catalog), base + "catalog.json.sig": Data(CatalogKit.sigFile(key, catalog))]
    }

    @Test func aSignedCatalogRefreshesAndRemembersItsStamp() async {
        let key = P256.Signing.PrivateKey()
        let marker = Marker()
        let c = client(signed(CatalogKit.realCatalog(), key), key: key, marker: marker)
        let r = await c.refresh()
        #expect(r.error == nil)
        #expect(r.catalog?.packs.count == 4)
        #expect(marker.remembered == ["2026-10-22T02:23:13Z"])
        #expect(c.current == r.catalog)
    }

    @Test func aTamperedCatalogIsNotParsed() async {
        let key = P256.Signing.PrivateKey()
        var files = signed(CatalogKit.realCatalog(), key)
        files[base + "catalog.json"] = Data(CatalogKit.edit(CatalogKit.realCatalog(), "\"size\": 34521", "\"size\": 34522"))
        let c = client(files, key: key)
        let r = await c.refresh()
        #expect(r.error == WidgetCatalogClient.unverified)
        #expect(c.current == nil)
    }

    @Test func aMissingCatalogOrSignatureIsOfflineOrUnverified() async {
        let key = P256.Signing.PrivateKey()
        #expect(await client([:], key: key).refresh().error == WidgetCatalogClient.offline)
        var files = signed(CatalogKit.realCatalog(), key)
        files[base + "catalog.json.sig"] = nil
        #expect(await client(files, key: key).refresh().error == WidgetCatalogClient.unverified)
        let down = WidgetCatalogClient(fetch: { _, _ in throw WidgetFetchError(message: "down") },
                                       pinnedKeys: [CatalogKit.spki(key)], baseUrl: base)
        #expect(await down.refresh().error == WidgetCatalogClient.offline)
    }

    @Test func aSignedButMalformedCatalogIsUnreadable() async {
        let key = P256.Signing.PrivateKey()
        let bad = CatalogKit.edit(CatalogKit.realCatalog(), "\"schema_version\": 1", "\"schema_version\": 2")
        #expect(await client(signed(bad, key), key: key).refresh().error == WidgetCatalogClient.unreadable)
    }

    @Test func aCatalogOlderThanTheNewestSeenIsRefusedAndTheSameOneIsNot() async {
        let key = P256.Signing.PrivateKey()
        let marker = Marker()
        let files = signed(CatalogKit.realCatalog(), key)
        marker.newest = WidgetCatalog.parseStamp("2026-10-23T00:00:00Z")
        #expect(await client(files, key: key, marker: marker).refresh().error == WidgetCatalogClient.rolledBack)
        #expect(marker.remembered.isEmpty)
        marker.newest = WidgetCatalog.parseStamp("2026-10-22T02:23:13Z")
        #expect(await client(files, key: key, marker: marker).refresh().error == nil)
    }

    @Test func sig2AloneVerifiesDuringARotation() async {
        let oldKey = P256.Signing.PrivateKey()
        let newKey = P256.Signing.PrivateKey()
        let cat = CatalogKit.realCatalog()
        let files = [base + "catalog.json": Data(cat), base + "catalog.json.sig": Data(CatalogKit.sigFile(oldKey, cat)),
                     base + "catalog.json.sig2": Data(CatalogKit.sigFile(newKey, cat))]
        let c = WidgetCatalogClient(fetch: { url, _ in files[url] }, pinnedKeys: [CatalogKit.spki(newKey)], baseUrl: base)
        #expect(await c.refresh().error == nil)
    }

    // MARK: - Install steps 3 to 5, with the real hourglass zip

    private func refreshed(_ catalog: [UInt8], zips: [String: Data]) async -> (WidgetCatalogClient, WidgetCatalogDocument) {
        let key = P256.Signing.PrivateKey()
        var files = signed(catalog, key)
        for (url, zip) in zips { files[url] = zip }
        let c = client(files, key: key)
        let doc = await c.refresh().catalog!
        return (c, doc)
    }

    @Test func theRealHourglassZipDownloadsAndPassesTheChecks() async throws {
        let zip = CatalogKit.realZip("hourglass-1.0.0.zip")
        let (c, doc) = await refreshed(CatalogKit.realCatalog(), zips: ["https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip": zip])
        let pack = try await c.download(doc.packs[0]).get()
        #expect(pack.manifest.id == "hourglass")
        #expect(pack.files["pack.json"] != nil)
    }

    @Test func aPackNotInTheVerifiedCatalogIsRefused() async {
        let (c, doc) = await refreshed(CatalogKit.realCatalog(), zips: [:])
        var other = doc.packs[0]
        other = WidgetCatalogEntry(id: other.id, name: other.name, kind: other.kind, version: other.version, sanduhrMin: other.sanduhrMin,
                                   url: other.url, sha256: String(repeating: "0", count: 64), size: other.size)
        guard case .failure(let e) = await c.download(other) else { Issue.record("accepted"); return }
        #expect(e.message == WidgetCatalogClient.notInCatalog)
    }

    @Test func aPackThatNeedsANewerFormatIsNotDownloaded() async {
        let zip = Array("pretend zip".utf8)
        let entry = CatalogKit.entry("future-thing", "Future", "palette", "1.0.0", zip, min: "3.9.0")
        let counted = Counter()
        let key = P256.Signing.PrivateKey()
        let cat = CatalogKit.catalog("2026-10-22T00:00:00Z", [entry])
        let files = signed(cat, key)
        let c = WidgetCatalogClient(fetch: { url, _ in
            if url == entry.url { counted.bump() }
            return files[url]
        }, pinnedKeys: [CatalogKit.spki(key)], baseUrl: base)
        _ = await c.refresh()
        guard case .failure(let e) = await c.download(entry) else { Issue.record("accepted"); return }
        #expect(e.message.contains("newer Sanduhr"))
        #expect(counted.count == 0)
    }

    @Test func aDownloadThatDoesNotMatchSizeOrHashIsRefusedBeforeTheZipOpens() async {
        let zip = CatalogKit.realZip("hourglass-1.0.0.zip")
        var flipped = zip
        flipped[100] ^= 1
        let url = "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip"
        for bad in [flipped, zip + [0], Data()] {
            let (c, doc) = await refreshed(CatalogKit.realCatalog(), zips: [url: bad])
            guard case .failure(let e) = await c.download(doc.packs[0]) else { Issue.record("accepted"); return }
            #expect(e.message == WidgetCatalogClient.downloadMismatch)
        }
        let (c, doc) = await refreshed(CatalogKit.realCatalog(), zips: [:])
        guard case .failure(let e) = await c.download(doc.packs[0]) else { Issue.record("accepted"); return }
        #expect(e.message == WidgetCatalogClient.downloadFailed)
    }

    @Test func aZipWhoseManifestDisagreesWithTheCatalogIsRefused() async {
        // oled-neon's zip served under hourglass's entry, with a matching size and hash.
        let zip = CatalogKit.realZip("oled-neon-palettes-1.0.0.zip")
        let entry = CatalogKit.entry("hourglass", "Hourglass", "sprite", "1.0.0", [UInt8](zip))
        let key = P256.Signing.PrivateKey()
        var files = signed(CatalogKit.catalog("2026-10-22T00:00:00Z", [entry]), key)
        files[entry.url] = zip
        let c = client(files, key: key)
        _ = await c.refresh()
        guard case .failure(let e) = await c.download(entry) else { Issue.record("accepted"); return }
        #expect(e.message.contains("wasn't installed"))
    }
}

private final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var n = 0
    var count: Int { lock.lock(); defer { lock.unlock() }; return n }
    func bump() { lock.lock(); n += 1; lock.unlock() }
}
