import Foundation

/// The embedded "Sign in to Claude" decisions (item 62), kept apart from WebKit so they test
/// without a browser. Ported from Windows `Sanduhr.Core.ClaudeSignIn`: the window loads
/// claude.ai's own login, and once a `sessionKey` cookie appears in its throwaway cookie store,
/// that key becomes the account's credential. `cf_clearance` is not kept: Cloudflare binds it to
/// the browser that earned it, and the window is Safari while the API calls are not.
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
    /// How long a loaded Google page may sit with nothing in it before the panel covers it.
    static let blankGoogleWait: TimeInterval = 4

    /// The tail WebKit appends to its user agent so the window reads as the Safari it is. A web view
    /// that claimed to be Chrome failed Cloudflare's human check every time (2026-10-05): the check
    /// compares the claimed browser with the engine's behavior. `safariVersion` is the installed
    /// Safari's (`CFBundleShortVersionString`); nil or empty falls back to `fallbackSafari`.
    static let fallbackSafari = "18.0"
    static func applicationName(safariVersion: String?) -> String {
        let v = safariVersion?.trimmingCharacters(in: .whitespaces) ?? ""
        let version = v.isEmpty || !v.allSatisfy({ $0.isNumber || $0 == "." }) ? fallbackSafari : v
        return "Version/\(version) Safari/605.1.15"
    }

    /// The installed Safari's version, read from its bundle.
    static func installedSafariVersion() -> String? {
        Bundle(url: URL(fileURLWithPath: "/Applications/Safari.app"))?
            .object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
    }

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

    /// Google's sign-in pages. claude.ai sends an account made with Google here even from Continue
    /// with email. Sign-in finishes here when Google's popup gets a window of its own; the window
    /// covers a Google page with the browser-and-paste steps only when it sits blank.
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

    /// Where a page's `window.open` goes. A popup on a sign-in host, or one with no address yet
    /// (a script may open it blank and set the location after), gets a window of its own, so it
    /// keeps `window.opener` and can hand Google's answer back to claude.ai. Loaded in the main
    /// view instead, it lost the opener and Google's page went white (Windows 4.0 found it).
    /// Anything else opens in the default browser.
    static func popupStaysInWindow(_ url: URL?) -> Bool {
        guard let url, !url.absoluteString.isEmpty else { return true }
        return staysInWindow(url)
    }

    /// A link or popup that leaves the window may go to the default browser only as a web address.
    /// Any other scheme (`file:`, an app's own scheme) is dropped, so a page can't open files or
    /// launch apps through the sign-in window.
    static func opensInBrowser(_ url: URL?) -> Bool {
        guard let scheme = url?.scheme?.lowercased(), url?.host?.isEmpty == false else { return false }
        return scheme == "https" || scheme == "http"
    }

    /// A loaded page with no text in its body: nothing a person can use, so the window offers
    /// the browser and a pasted key over it.
    static func pageLooksBlank(bodyTextLength: Int?) -> Bool {
        (bodyTextLength ?? 0) == 0
    }
}
