import Foundation

/// The embedded "Sign in to Claude" decisions (item 62), kept apart from WebKit so they test
/// without a browser. Ported from Windows `Sanduhr.Core.ClaudeSignIn`: the window loads
/// claude.ai's own login, and once a `sessionKey` cookie appears in its throwaway cookie store,
/// that key (and `cf_clearance`, when Cloudflare set one) becomes the account's credential.
///
/// Cookie presence is the signal, not the page: claude.ai signs in on another Anthropic host and
/// sets the cookie from script without a navigation the window could watch, so the window checks
/// the store on every navigation and on a short poll. The store starts empty for every sign-in,
/// so a cookie in it can only come from this sign-in.
enum ClaudeSignIn {
    static let loginURL = URL(string: "https://claude.ai/login")!
    static let sessionKeyCookie = "sessionKey"
    static let cfClearanceCookie = "cf_clearance"
    /// How often the window looks for the cookie between navigations.
    static let pollInterval: TimeInterval = 1.5
    /// A first page that hasn't loaded by then shows the error and the paste way out.
    static let loadTimeout: TimeInterval = 30

    /// What a finished sign-in hands back. Never logged; saved to the Keychain and dropped.
    struct Captured: Equatable, Sendable {
        let sessionKey: String
        let cfClearance: String?
    }

    /// A cookie as the window reads it from `WKHTTPCookieStore`.
    struct Cookie: Equatable, Sendable {
        let name: String
        let value: String
        let domain: String
    }

    /// The session from claude.ai's cookies, or nil while there is none. Cookies for other hosts
    /// are ignored; of duplicates (`claude.ai` and `.claude.ai`), the first non-empty value wins.
    static func capture(_ cookies: [Cookie]) -> Captured? {
        func value(_ name: String) -> String? {
            cookies.lazy
                .filter { $0.name == name && isClaudeHost($0.domain) }
                .map { $0.value.trimmingCharacters(in: .whitespacesAndNewlines) }
                .first { !$0.isEmpty }
        }
        guard let key = value(sessionKeyCookie) else { return nil }
        return Captured(sessionKey: key, cfClearance: value(cfClearanceCookie))
    }

    /// claude.ai or a subdomain. A cookie domain may start with a dot.
    static func isClaudeHost(_ host: String?) -> Bool {
        guard var h = host?.lowercased(), !h.isEmpty else { return false }
        if h.hasPrefix(".") { h.removeFirst() }
        return h == "claude.ai" || h.hasSuffix(".claude.ai")
    }

    /// Google's sign-in pages. Google refuses sign-in inside an app's web view, so the window
    /// shows its notice here: go back and continue with email instead (a Claude account made with
    /// Google can sign in by email code).
    static func isGoogleSignIn(_ url: URL?) -> Bool {
        guard let host = url?.host?.lowercased() else { return false }
        return host == "accounts.google.com" || host.hasPrefix("accounts.google.")
            || host == "accounts.youtube.com"
    }

    /// Hosts a sign-in window may show. Anything else (a link in the page to the docs, a help
    /// article) opens in the default browser instead, so the window stays a sign-in window.
    static func staysInWindow(_ url: URL?) -> Bool {
        guard let url, let scheme = url.scheme?.lowercased() else { return false }
        if scheme == "about" || scheme == "blob" || scheme == "data" { return true }
        guard scheme == "https", let host = url.host?.lowercased() else { return false }
        return isClaudeHost(host) || host == "anthropic.com" || host.hasSuffix(".anthropic.com")
            || isGoogleSignIn(url) || host.hasSuffix(".google.com") || host.hasSuffix(".gstatic.com")
            || host == "challenges.cloudflare.com" || host.hasSuffix(".cloudflare.com")
            || host.hasSuffix(".apple.com")
    }
}
