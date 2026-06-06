using System.Net;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

// ---------------------------------------------------------------------------
// Typed error hierarchy — 1:1 with api.py's APIError / SessionExpired /
// CloudflareBlocked / NetworkError. The fetcher lets these propagate; the App
// catches them and marshals targeted feedback to the UI thread (parity with
// the Python fetcher's (kind, message) signal split).
// ---------------------------------------------------------------------------

/// <summary>Base class for all claude.ai API errors (port of <c>api.APIError</c>).</summary>
public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
    public ApiException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>401, or 403 without a Cloudflare challenge — the session key needs rotation (<c>api.SessionExpired</c>).</summary>
public sealed class SessionExpiredException : ApiException
{
    public SessionExpiredException(string message) : base(message) { }
}

/// <summary>403 carrying Cloudflare challenge markup — a <c>cf_clearance</c> cookie is needed (<c>api.CloudflareBlocked</c>).</summary>
public sealed class CloudflareBlockedException : ApiException
{
    public CloudflareBlockedException(string message) : base(message) { }
}

/// <summary>5xx, non-JSON, no orgs, or any other unexpected transport/shape failure (<c>api.NetworkError</c>).</summary>
public sealed class NetworkException : ApiException
{
    public NetworkException(string message) : base(message) { }
    public NetworkException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Plan/subscription fields captured off the selected org during discovery and
/// surfaced to the tier-badge layer (<see cref="PlanLabel.Resolve"/>). Mirrors
/// the <c>_account</c> dict PR #25 attaches to the usage payload.
/// </summary>
public sealed record AccountPlan(
    string? RateLimitTier,
    string? BillingType,
    IReadOnlyList<string>? Capabilities);

/// <summary>
/// The fetch surface the <see cref="UsageFetcher"/> depends on. Extracting it
/// from the concrete client keeps the fetcher unit-testable with a canned
/// client (parity with the Python tests' <c>monkeypatch</c> of
/// <c>fetcher.ClaudeAPI</c>).
/// </summary>
public interface IClaudeApiClient
{
    /// <summary>Org-scoped usage payload with the <c>_account</c> plan key injected.</summary>
    Task<JsonObject> GetUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Daily Routines run-budget, or <c>null</c> when the account has no code access (404).</summary>
    Task<RoutineBudget?> GetRoutineBudgetAsync(CancellationToken cancellationToken = default);

    /// <summary>Plan fields captured during org discovery; <c>null</c> until the first fetch resolves an org.</summary>
    AccountPlan? Account { get; }
}

/// <summary>
/// A <see cref="DelegatingHandler"/> that makes outgoing requests read as a
/// real Chrome browser — a Chrome User-Agent, the <c>Sec-Fetch-*</c> /
/// <c>sec-ch-ua</c> client hints, and a JSON Accept. Replaces cloudscraper's
/// browser-emulation layer for the headers it can reproduce.
///
/// PARITY NUANCE (documented, not solved): cloudscraper additionally
/// auto-solves Cloudflare's JavaScript challenge by running the challenge VM;
/// a pure <see cref="HttpClient"/> cannot. The intent here is to look
/// browser-like so the soft path succeeds, and when a hard JS challenge still
/// fires the client surfaces <see cref="CloudflareBlockedException"/> so the UI
/// prompts the user for the <c>cf_clearance</c> cookie (the existing manual
/// fallback). See <c>ClaudeApiClient.CheckAsync</c>.
/// </summary>
public sealed class CloudflareAwareHandler : DelegatingHandler
{
    // Shared with the embedded sign-in WebView2 so the cf_clearance issued during
    // sign-in is valid when replayed here — Cloudflare binds cf_clearance to the
    // User-Agent that obtained it. Single source of truth in Core.
    private const string ChromeUserAgent = ClaudeSignIn.BrowserUserAgent;

    public CloudflareAwareHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var h = request.Headers;
        // TryAddWithoutValidation is a no-op when the header already exists, so
        // a caller-supplied value (or a test override) wins.
        if (!h.Contains("User-Agent")) h.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
        if (!h.Contains("Accept")) h.TryAddWithoutValidation("Accept", "application/json");
        if (!h.Contains("Accept-Language")) h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        h.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\", \"Google Chrome\";v=\"131\"");
        h.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        h.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// claude.ai API client, ported from <c>api.py</c> (PR #25 behavior). Replaces
/// cloudscraper with <see cref="HttpClient"/> behind a
/// <see cref="CloudflareAwareHandler"/>. Cookie auth is set per request
/// (<c>sessionKey</c> + optional <c>cf_clearance</c>) exactly as the Python
/// build does, so the same credentials resolve the same endpoints.
///
/// Endpoints:
/// <list type="bullet">
/// <item><c>/api/organizations</c> — discover + cache the org id, capturing
///   <c>rate_limit_tier</c> / <c>billing_type</c> / <c>capabilities</c> (PR #25).</item>
/// <item><c>/api/organizations/{id}/usage</c> — the tier usage payload; the
///   captured plan fields ride through on the inert <c>_account</c> key.</item>
/// <item><c>/v1/code/routines/run-budget</c> — the daily Routines budget; a
///   different base path that needs <c>x-organization-uuid</c> + the Anthropic
///   client headers. 404 → <c>null</c> (account without code access).</item>
/// </list>
///
/// Pure Core: no WPF/Win32. Inject an <see cref="HttpMessageHandler"/> via the
/// constructor to feed canned responses in tests — no live network.
/// </summary>
public sealed class ClaudeApiClient : IClaudeApiClient, IDisposable
{
    private const string ApiBase = "https://claude.ai/api";
    private const string RoutinesUrl = "https://claude.ai/v1/code/routines/run-budget";

    // Anthropic client headers required by the /v1/code/* endpoints. The
    // /api/* endpoints accept just the session cookie; /v1/code/* 404 unless
    // these are present alongside x-organization-uuid (port of _ROUTINES_HEADERS).
    private static readonly KeyValuePair<string, string>[] RoutinesHeaders =
    {
        new("anthropic-client-platform", "web_claude_ai"),
        new("anthropic-version", "2023-06-01"),
        new("anthropic-beta", "ccr-triggers-2026-01-30"),
    };

    private readonly HttpClient _http;
    private readonly string _sessionKey;
    private readonly string? _cfClearance;

    private string? _orgId;
    private JsonObject? _accountNode; // the _account dict injected into usage

    public AccountPlan? Account { get; private set; }

    /// <param name="sessionKey">The claude.ai <c>sessionKey</c> cookie value.</param>
    /// <param name="cfClearance">Optional <c>cf_clearance</c> cookie (the manual Cloudflare fallback).</param>
    /// <param name="innerHandler">
    /// Optional inner handler — tests pass a stub to serve fixtures. The client
    /// always wraps it in a <see cref="CloudflareAwareHandler"/> so the
    /// browser-like headers are exercised even under test. When null, a default
    /// <see cref="HttpClientHandler"/> with cookies disabled is used (we manage
    /// the Cookie header ourselves, matching api.py).
    /// </param>
    public ClaudeApiClient(string sessionKey, string? cfClearance = null, HttpMessageHandler? innerHandler = null)
    {
        _sessionKey = sessionKey;
        _cfClearance = cfClearance;
        var inner = innerHandler ?? new HttpClientHandler
        {
            UseCookies = false, // we build the Cookie header by hand (parity with api.py)
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(new CloudflareAwareHandler(inner), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15), // api.py timeout=15
        };
    }

    private string CookieHeader()
    {
        var header = $"sessionKey={_sessionKey}";
        if (!string.IsNullOrEmpty(_cfClearance))
            header += $"; cf_clearance={_cfClearance}";
        return header;
    }

    /// <summary>Detect a Cloudflare challenge / block page by body content (port of <c>_looks_like_cloudflare</c>).</summary>
    public static bool LooksLikeCloudflare(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("cf-challenge") || t.Contains("just a moment") || t.Contains("cloudflare");
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string url, IEnumerable<KeyValuePair<string, string>>? extraHeaders, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", CookieHeader());
        if (extraHeaders is not null)
            foreach (var kv in extraHeaders)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        return await _http.SendAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>Status → typed error mapping, 1:1 with <c>api._check</c>.</summary>
    private static async Task CheckAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        int code = (int)resp.StatusCode;
        if (code == 401)
            throw new SessionExpiredException("HTTP 401 -- session key rejected");
        if (code == 403)
        {
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
            catch { /* body unavailable — fall through to the non-CF branch */ }
            if (LooksLikeCloudflare(body))
                throw new CloudflareBlockedException("Cloudflare challenge -- cf_clearance needed");
            throw new SessionExpiredException("HTTP 403 -- session key rejected");
        }
        if (code >= 500)
            throw new NetworkException($"HTTP {code}");
        // Any other non-success (e.g. an unexpected 4xx) throws HttpRequestException,
        // which the fetcher surfaces as an "unknown" failure — matching api.py's
        // resp.raise_for_status() landing outside the typed hierarchy.
        resp.EnsureSuccessStatusCode();
    }

    private async Task<string> GetOrgIdAsync(CancellationToken ct)
    {
        if (_orgId is not null) return _orgId;

        using var resp = await SendGetAsync($"{ApiBase}/organizations", null, ct).ConfigureAwait(false);
        await CheckAsync(resp, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Shape parsing + plan capture live in ClaudeApiParsing so the WebView2
        // transport reuses the exact same logic (and the same parity tests).
        var discovery = ClaudeApiParsing.ParseOrganizations(body);
        _orgId = discovery.OrgId;
        _accountNode = discovery.AccountNode;
        Account = discovery.Account;

        return _orgId;
    }

    /// <inheritdoc />
    public async Task<JsonObject> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var orgId = await GetOrgIdAsync(cancellationToken).ConfigureAwait(false);

        using var resp = await SendGetAsync(
            $"{ApiBase}/organizations/{orgId}/usage", null, cancellationToken).ConfigureAwait(false);
        await CheckAsync(resp, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Parse + ride the captured plan fields through on the reserved _account
        // key (shared with the WebView2 transport via ClaudeApiParsing).
        return ClaudeApiParsing.ParseUsage(body, _accountNode);
    }

    /// <inheritdoc />
    public async Task<RoutineBudget?> GetRoutineBudgetAsync(CancellationToken cancellationToken = default)
    {
        var orgId = await GetOrgIdAsync(cancellationToken).ConfigureAwait(false);

        var extra = new List<KeyValuePair<string, string>>(RoutinesHeaders)
        {
            new("x-organization-uuid", orgId),
        };

        using var resp = await SendGetAsync(RoutinesUrl, extra, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null; // account without Routines / code access
        await CheckAsync(resp, cancellationToken).ConfigureAwait(false);

        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ClaudeApiParsing.ParseRoutineBudget(body);
    }

    public void Dispose() => _http.Dispose();
}
