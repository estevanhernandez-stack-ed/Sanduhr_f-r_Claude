import Foundation

/// A fetch could not complete: offline, a timeout, an HTTP error, a redirect off the allowed
/// hosts, or a body over its size limit. The message is for the log only.
struct WidgetFetchError: Error, Equatable {
    let message: String
}

/// Fetches one URL's body, at most `maxBytes`. Returns nil when the server says the file is not
/// there (404), which is how an absent `catalog.json.sig2` reads. Throws `WidgetFetchError` for
/// everything else that is not a body. `WidgetDownloader` is the only code that touches the network.
typealias WidgetFetch = (_ url: String, _ maxBytes: Int) async throws -> Data?

/// Where the rollback marker lives: the newest catalog `generated_at` this Mac has accepted.
/// The widget store (`widgets.json`) implements it; tests use a stand-in.
protocol WidgetCatalogMarker: AnyObject {
    func newestCatalogSeen() -> Date?
    func rememberCatalog(_ generatedAtText: String)
}

/// A catalog refresh: the verified catalog, or the sentence Settings shows.
struct WidgetCatalogRefresh {
    let catalog: WidgetCatalogDocument?
    let error: String?
}

/// A pack that passed rules 3 to 5: ready for the store's atomic install.
struct WidgetDownloadedPack {
    let entry: WidgetCatalogEntry
    let files: [String: Data]
    let manifest: PackManifest
}

/// The catalog client's steps in FORMAT 7's order, over an injected `WidgetFetch` (the app's HTTPS
/// or a test's dictionary). Settings calls `refresh()` when Settings ▸ Widgets opens and
/// `download(_:)` on Install or Update; nothing here runs on its own. A port of Windows'
/// WidgetCatalogClient.cs; the install itself (rule 6) is the widget store's, which takes a
/// `WidgetDownloadedPack`.
final class WidgetCatalogClient {
    static let offline = "Sanduhr couldn't reach the widget catalog. Check your connection and try again."
    static let unverified = "The widget catalog couldn't be verified."
    static let unreadable = "The widget catalog couldn't be read."
    static let rolledBack = "The widget catalog is older than one Sanduhr has already seen, so it wasn't used."
    static let downloadFailed = "Sanduhr couldn't download the widget. Check your connection and try again."
    static let downloadMismatch = "The download didn't match the catalog, so it wasn't installed."
    static let notInCatalog = "That widget isn't in the verified catalog."

    private let fetch: WidgetFetch
    private let marker: WidgetCatalogMarker?
    private let keys: [String]
    private let base: String
    private let supported: String

    /// The last catalog that verified and parsed in this session, or nil.
    private(set) var current: WidgetCatalogDocument?

    /// - Parameters:
    ///   - fetch: The network, or a test's stand-in.
    ///   - marker: Holds the rollback marker; nil keeps no memory (a test, or a fresh store).
    ///   - pinnedKeys: The keys a catalog must verify against.
    ///   - baseUrl: Where catalog.json and its signatures are; defaults to the latest release.
    ///   - supported: The pack-format level, `PackFormat.supported`.
    init(fetch: @escaping WidgetFetch, marker: WidgetCatalogMarker? = nil, pinnedKeys: [String] = WidgetCatalog.pinnedKeys,
         baseUrl: String = WidgetCatalog.latestBase, supported: String = PackFormat.supported) {
        self.fetch = fetch
        self.marker = marker
        self.keys = pinnedKeys
        self.base = baseUrl
        self.supported = supported
    }

    /// Rules 1 and 2: fetch catalog.json and its signature files, verify before parsing, parse
    /// strictly, refuse a rollback, and remember the newest `generated_at`.
    func refresh() async -> WidgetCatalogRefresh {
        let catalog: Data?, sig: Data?
        do {
            catalog = try await fetch(base + WidgetCatalog.catalogFile, WidgetCatalog.maxCatalogBytes)
            sig = try await fetch(base + WidgetCatalog.signatureFile, WidgetCatalog.maxSignatureFileBytes)
        } catch {
            return WidgetCatalogRefresh(catalog: nil, error: Self.offline)
        }
        guard let catalog else { return WidgetCatalogRefresh(catalog: nil, error: Self.offline) }
        // Present only during a key rotation; any failure to get it just means there is no second signature.
        let sig2 = try? await fetch(base + WidgetCatalog.secondSignatureFile, WidgetCatalog.maxSignatureFileBytes)

        // Rule 1: nothing is parsed until a pinned key verifies the exact bytes.
        let bytes = [UInt8](catalog)
        guard WidgetCatalog.verifySignature(bytes, [sig.map { [UInt8]($0) }, sig2.map { [UInt8]($0) }], keys) else {
            return WidgetCatalogRefresh(catalog: nil, error: Self.unverified)
        }
        guard case .success(let doc) = WidgetCatalog.parse(bytes) else {
            return WidgetCatalogRefresh(catalog: nil, error: Self.unreadable)
        }
        // Rule 2.
        if WidgetCatalog.isRollback(doc.generatedAt, newestSeen: marker?.newestCatalogSeen()) {
            return WidgetCatalogRefresh(catalog: nil, error: Self.rolledBack)
        }
        marker?.rememberCatalog(doc.generatedAtText)
        current = doc
        return WidgetCatalogRefresh(catalog: doc, error: nil)
    }

    /// Rules 3 to 5 for one pack of `current`: `sanduhr_min`, download, size and SHA-256 before the
    /// zip is opened, safe extraction, strict local re-validation. The failure is the sentence to show.
    func download(_ entry: WidgetCatalogEntry) async -> Result<WidgetDownloadedPack, WidgetCatalogError> {
        func fail(_ m: String) -> Result<WidgetDownloadedPack, WidgetCatalogError> { .failure(WidgetCatalogError(message: m)) }
        // Only a pack of the catalog that verified in this session.
        guard let current, current.packs.contains(entry) else { return fail(Self.notInCatalog) }
        guard WidgetCatalog.meetsMinimum(entry.sanduhrMin, supported: supported) else {
            return fail("\(entry.name) needs a newer Sanduhr (it asks for widget format \(entry.sanduhrMin)).")
        }

        let zip: Data?
        do {
            zip = try await fetch(entry.url, entry.size)
        } catch {
            return fail(Self.downloadFailed)
        }
        guard let zip else { return fail(Self.downloadFailed) }
        if WidgetCatalog.checkDownload(entry, [UInt8](zip)) != nil { return fail(Self.downloadMismatch) }

        let files: [String: Data]
        switch WidgetZip.read(zip) {
        case .failure(let e): return fail("\(entry.name) wasn't installed: its zip isn't safe to open (\(e.message)).")
        case .success(let f): files = f
        }

        let verdict = PackValidator.validate(files, folderName: entry.id, strict: true)
        guard verdict.errors.isEmpty, let manifest = verdict.manifest else {
            return fail("\(entry.name) wasn't installed: it didn't pass Sanduhr's checks (\(verdict.errors.first ?? "unknown")).")
        }
        if manifest.id != entry.id || manifest.version != entry.version || manifest.kind != entry.kind {
            return fail("\(entry.name) wasn't installed: its files don't match the catalog.")
        }
        guard WidgetCatalog.meetsMinimum(manifest.sanduhrMin, supported: supported) else {
            return fail("\(entry.name) needs a newer Sanduhr (it asks for widget format \(manifest.sanduhrMin)).")
        }
        return .success(WidgetDownloadedPack(entry: entry, files: files, manifest: manifest))
    }
}

/// The widget catalog's network half: the one place the app fetches `catalog.json`, its signatures
/// and pack zips, from `github.com/626Labs-LLC/sanduhr-widgets/releases`. It runs only when
/// Settings ▸ Widgets opens and when the user installs or updates a pack; nothing polls. Never in
/// the MCP process.
///
/// No cookies, no cache, no identifiers; the user agent is just "Sanduhr". Redirects are followed
/// by hand, at most five, and only over https to GitHub and the hosts GitHub serves release
/// assets from (`WidgetCatalog.allowedHosts`). Every body has a size cap (1 MB for the catalog,
/// 1 KB for a signature, the catalog's size for a zip) enforced while reading, and every request a
/// timeout. Verification, parsing and install are `WidgetCatalogClient`'s; this only moves bytes.
final class WidgetDownloader {
    static let maxRedirects = 5
    static let smallFileTimeout: TimeInterval = 20
    static let zipTimeout: TimeInterval = 90
    static let userAgent = "Sanduhr"

    private let session: URLSession

    /// - Parameter session: Tests pass one whose configuration carries a `URLProtocol` stub; the
    ///   app passes none and gets an ephemeral session with no cookies and no cache.
    init(session: URLSession? = nil) {
        self.session = session ?? URLSession(configuration: Self.configuration())
    }

    static func configuration() -> URLSessionConfiguration {
        let c = URLSessionConfiguration.ephemeral
        c.httpCookieStorage = nil
        c.httpShouldSetCookies = false
        c.httpCookieAcceptPolicy = .never
        c.urlCache = nil
        c.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        c.httpAdditionalHeaders = ["User-Agent": userAgent]
        return c
    }

    /// Hands a 3xx back as a response instead of following it, so the hop is checked by hand.
    private final class RedirectRefuser: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
            completionHandler(nil)
        }
    }

    /// One body, at most `maxBytes`; nil on 404. See `WidgetFetch`.
    func fetch(_ url: String, _ maxBytes: Int) async throws -> Data? {
        let limit = maxBytes > WidgetCatalog.maxCatalogBytes ? Self.zipTimeout : Self.smallFileTimeout
        guard let start = URL(string: url) else { throw WidgetFetchError(message: "not a URL") }
        do {
            return try await withThrowingTaskGroup(of: Data?.self) { group in
                group.addTask { try await self.follow(start, maxBytes, timeout: limit) }
                group.addTask {
                    try await Task.sleep(nanoseconds: UInt64(limit * 1_000_000_000))
                    throw WidgetFetchError(message: "the request timed out")
                }
                defer { group.cancelAll() }
                return try await group.next() ?? nil
            }
        } catch let e as WidgetFetchError {
            throw e
        } catch is CancellationError {
            throw CancellationError()
        } catch let e as URLError where e.code == .cancelled {
            throw CancellationError()
        } catch {
            throw WidgetFetchError(message: "the request failed")
        }
    }

    private func follow(_ first: URL, _ maxBytes: Int, timeout: TimeInterval) async throws -> Data? {
        var url = first
        for _ in 0...Self.maxRedirects {
            guard WidgetCatalog.isAllowedUrl(url) else {
                throw WidgetFetchError(message: "refused a URL outside GitHub's release hosts")
            }
            var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalAndRemoteCacheData, timeoutInterval: timeout)
            request.httpShouldHandleCookies = false
            request.setValue(Self.userAgent, forHTTPHeaderField: "User-Agent")
            let (bytes, response) = try await session.bytes(for: request, delegate: RedirectRefuser())
            guard let http = response as? HTTPURLResponse else { throw WidgetFetchError(message: "not an HTTP response") }
            let code = http.statusCode
            if [301, 302, 303, 307, 308].contains(code) {
                guard let location = http.value(forHTTPHeaderField: "Location"),
                      let next = URL(string: location, relativeTo: url)?.absoluteURL else {
                    throw WidgetFetchError(message: "a redirect without a location")
                }
                url = next
                continue
            }
            if code == 404 { return nil }
            guard (200..<300).contains(code) else { throw WidgetFetchError(message: "HTTP \(code)") }
            if http.expectedContentLength > Int64(maxBytes) { throw WidgetFetchError(message: "the file is larger than allowed") }
            var body = Data()
            for try await byte in bytes {
                if body.count >= maxBytes { throw WidgetFetchError(message: "the file is larger than allowed") }
                body.append(byte)
            }
            return body
        }
        throw WidgetFetchError(message: "too many redirects")
    }
}
