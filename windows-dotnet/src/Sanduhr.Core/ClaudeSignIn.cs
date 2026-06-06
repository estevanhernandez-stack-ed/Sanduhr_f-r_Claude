namespace Sanduhr.Core;

/// <summary>
/// The captured cookie pair pulled off claude.ai's cookie jar after a successful
/// embedded sign-in. <see cref="SessionKey"/> is the credential the app tracks
/// with; <see cref="CfClearance"/> is present only when Cloudflare issued a
/// clearance cookie. <see cref="HasSession"/> is the truth signal for "the user
/// is signed in" — mirrors ROROROblox's "wait for the auth cookie, then resolve."
/// </summary>
public sealed record CapturedCookies(string? SessionKey, string? CfClearance)
{
    public bool HasSession => !string.IsNullOrEmpty(SessionKey);
}

/// <summary>
/// Pure (WPF-free, WebView2-free) logic for the embedded "Sign in to Claude"
/// flow — the testable seam lifted from ROROROblox's CookieCapture split. The
/// App's <c>SignInWindow</c> hosts the WebView2 and feeds raw nav URLs + the
/// CookieManager's cookie list into these functions; all the decisions
/// ("is this a signed-in page?", "did we capture a sessionKey?") live here so
/// they unit-test without a browser.
/// </summary>
public static class ClaudeSignIn
{
    /// <summary>Where the embedded window starts the user.</summary>
    public const string LoginUrl = "https://claude.ai/login";

    /// <summary>The origin passed to <c>CookieManager.GetCookiesAsync</c>.</summary>
    public const string CookieOrigin = "https://claude.ai";

    /// <summary>The session credential cookie claude.ai sets after auth.</summary>
    public const string SessionKeyCookie = "sessionKey";

    /// <summary>The Cloudflare clearance cookie, when present.</summary>
    public const string CfClearanceCookie = "cf_clearance";

    /// <summary>
    /// Canonical desktop-Chrome User-Agent used by BOTH the embedded sign-in
    /// WebView2 and <c>ClaudeApiClient</c>. Two reasons they must match:
    /// (1) Cloudflare binds <c>cf_clearance</c> to the UA that obtained it — a
    /// clearance captured in the sign-in window is only valid when the API client
    /// replays it under an identical UA. (2) WebView2's default UA carries Edge /
    /// embedded-webview markers that Google's sign-in refuses; a clean desktop-Chrome
    /// UA is the standard mitigation for OAuth inside a WebView2.
    /// </summary>
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    /// True when <paramref name="url"/> is an https claude.ai (or *.claude.ai)
    /// page. Gates capture so we never fire on embedded analytics, captcha
    /// iframes, OAuth provider hosts (accounts.google.com), or telemetry.
    /// </summary>
    public static bool IsClaudeUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return IsClaudeHost(uri.Host);
    }

    /// <summary>
    /// True for an auth surface that does NOT mean "signed in": the login page,
    /// OAuth round-trips, magic-link landing, and logout. We require leaving these
    /// before accepting a sessionKey so a stale cookie mid-refresh can't trigger a
    /// premature capture.
    /// </summary>
    public static bool IsLoginUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!IsClaudeHost(uri.Host))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
            return false; // claude.ai/ is the signed-in app root, not a login page

        return StartsWithSegment(path, "/login")
            || StartsWithSegment(path, "/auth")
            || StartsWithSegment(path, "/oauth")
            || StartsWithSegment(path, "/magic-link")
            || StartsWithSegment(path, "/logout")
            || StartsWithSegment(path, "/sign-in")
            || StartsWithSegment(path, "/signin");
    }

    /// <summary>
    /// True when the URL is a claude.ai page that is NOT an auth surface —
    /// i.e. a place the user only reaches once authenticated
    /// (claude.ai/, claude.ai/new, claude.ai/chats, claude.ai/recents…). This is
    /// the navigation gate; the cookie presence (<see cref="CapturedCookies.HasSession"/>)
    /// is the actual truth signal.
    /// </summary>
    public static bool IsSignedInUrl(string? url) => IsClaudeUrl(url) && !IsLoginUrl(url);

    /// <summary>
    /// Pull the sessionKey (+ cf_clearance if present) out of a name→value cookie
    /// map. The App builds the map from <c>CoreWebView2Cookie</c> entries returned
    /// by <c>CookieManager.GetCookiesAsync(CookieOrigin)</c>. Whitespace-only
    /// values are treated as absent.
    /// </summary>
    public static CapturedCookies Extract(IReadOnlyDictionary<string, string?> cookiesByName)
    {
        if (cookiesByName is null)
            throw new ArgumentNullException(nameof(cookiesByName));

        cookiesByName.TryGetValue(SessionKeyCookie, out var session);
        cookiesByName.TryGetValue(CfClearanceCookie, out var clearance);

        return new CapturedCookies(
            string.IsNullOrWhiteSpace(session) ? null : session,
            string.IsNullOrWhiteSpace(clearance) ? null : clearance);
    }

    private static bool IsClaudeHost(string? host) =>
        host is not null
        && (host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".claude.ai", StringComparison.OrdinalIgnoreCase));

    private static bool StartsWithSegment(string path, string segment) =>
        path.Equals(segment, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase);
}
