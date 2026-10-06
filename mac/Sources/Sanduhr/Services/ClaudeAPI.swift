import Foundation

/// Talks to the unofficial `claude.ai/api` endpoints the Python widget uses.
/// Mirrors `ClaudeAPI` in sanduhr.py:135-152.
///
/// Cloudflare note: the Python version uses `cloudscraper` to solve CF's JS
/// challenge. URLSession can't do that, but an already-authenticated
/// `sessionKey` usually skips the challenge. If CF still blocks, the user
/// can paste their `cf_clearance` cookie in settings and we'll include it.
actor ClaudeAPI {
    enum APIError: Error, LocalizedError {
        case noOrganizations
        case cloudflareChallenge
        case http(Int)
        case invalidResponse
        case unauthorized

        var errorDescription: String? {
            switch self {
            case .noOrganizations:     return "No organizations found on this account"
            case .cloudflareChallenge: return "Cloudflare challenge — paste your cf_clearance cookie"
            case .http(let c):         return "HTTP \(c)"
            case .invalidResponse:     return "Unexpected response from claude.ai"
            case .unauthorized:        return "Session expired — sign in again"
            }
        }
    }

    private static let base = URL(string: "https://claude.ai/api")!
    /// Plausible macOS-Chrome UA to satisfy Cloudflare heuristics.
    /// Also the sign-in window's (ClaudeSignIn): Cloudflare binds cf_clearance to the user agent that
    /// earned it, so a clearance captured there only works when replayed under the same one.
    static let userAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

    private let sessionKey: String
    private let cfClearance: String?
    private var orgID: String?
    private let session: URLSession

    init(sessionKey: String, cfClearance: String?) {
        self.sessionKey = sessionKey
        self.cfClearance = cfClearance
        let cfg = URLSessionConfiguration.ephemeral
        cfg.httpCookieStorage = nil
        cfg.httpShouldSetCookies = false
        cfg.timeoutIntervalForRequest = 15
        self.session = URLSession(configuration: cfg)
    }

    // MARK: Public

    /// Two-step fetch (orgs → usage), mirroring sanduhr.py:145-152.
    func getUsage() async throws -> UsageResponse {
        let org = try await organizationID()
        let url = Self.base
            .appendingPathComponent("organizations")
            .appendingPathComponent(org)
            .appendingPathComponent("usage")
        return try await getJSON(url)
    }

    /// The organization this client tracks (`Organization.tracked`), fetched once per client. The
    /// Data section compares it in memory with a Claude Code folder's; never stored or logged.
    func organizationID() async throws -> String {
        if let orgID { return orgID }
        let url = Self.base.appendingPathComponent("organizations")
        let orgs: [Organization] = try await getJSON(url)
        guard let org = Organization.tracked(in: orgs) else { throw APIError.noOrganizations }
        orgID = org.uuid
        return org.uuid
    }

    // MARK: Internal

    private func getJSON<T: Decodable>(_ url: URL) async throws -> T {
        var req = URLRequest(url: url)
        req.httpMethod = "GET"
        req.setValue("application/json",  forHTTPHeaderField: "Accept")
        req.setValue(Self.userAgent,       forHTTPHeaderField: "User-Agent")
        req.setValue("https://claude.ai/", forHTTPHeaderField: "Referer")
        req.setValue(cookieHeader(),       forHTTPHeaderField: "Cookie")
        // Sec-Fetch hints help with CF heuristics.
        req.setValue("same-origin",  forHTTPHeaderField: "Sec-Fetch-Site")
        req.setValue("cors",         forHTTPHeaderField: "Sec-Fetch-Mode")
        req.setValue("empty",        forHTTPHeaderField: "Sec-Fetch-Dest")

        let (data, response) = try await session.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw APIError.invalidResponse }

        switch http.statusCode {
        case 200..<300:
            do { return try JSONDecoder().decode(T.self, from: data) }
            catch { throw APIError.invalidResponse }
        case 401, 403:
            if looksLikeCloudflareChallenge(data: data, response: http) {
                throw APIError.cloudflareChallenge
            }
            throw APIError.unauthorized
        default:
            throw APIError.http(http.statusCode)
        }
    }

    private func cookieHeader() -> String {
        var parts = ["sessionKey=\(sessionKey)"]
        if let cf = cfClearance, !cf.isEmpty { parts.append("cf_clearance=\(cf)") }
        return parts.joined(separator: "; ")
    }

    private func looksLikeCloudflareChallenge(data: Data, response: HTTPURLResponse) -> Bool {
        if let server = response.value(forHTTPHeaderField: "Server"),
           server.lowercased().contains("cloudflare"),
           let body = String(data: data, encoding: .utf8),
           body.contains("Just a moment") || body.contains("cf-chl") || body.contains("challenge-platform")
        {
            return true
        }
        return false
    }
}
