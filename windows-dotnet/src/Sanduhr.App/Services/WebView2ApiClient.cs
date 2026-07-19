using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// claude.ai API client that fetches usage through a real, authenticated WebView2
/// browser instead of a raw <see cref="System.Net.Http.HttpClient"/>. This is the
/// item-3 architecture pivot: a pure HttpClient cannot pass claude.ai's Cloudflare
/// even with a valid <c>cf_clearance</c> + matched Chrome UA, because Cloudflare
/// binds its challenge to the browser's TLS/JA3 fingerprint and the short-lived
/// <c>__cf_bm</c> cookie. A genuine WebView2 clears Cloudflare natively, so we run
/// the API requests from inside the claude.ai page context (same origin, real
/// fingerprint, full cookie jar) via in-page <c>fetch()</c>.
///
/// Drop-in for <see cref="ClaudeApiClient"/>: same <c>(sessionKey, cfClearance)</c>
/// shape, same <see cref="IClaudeApiClient"/> surface, same typed errors and the
/// same shared <see cref="ClaudeApiParsing"/> parsing — so swapping the transport
/// is a one-line change in the view model and <see cref="UsageFetcher"/> is
/// untouched.
///
/// Mechanics:
/// <list type="number">
/// <item><b>Hidden host.</b> A borderless, off-screen, taskbar-less WPF window
///   hosts the WebView2 so it gets a real HWND (required to run JS) while staying
///   invisible. A dedicated transport profile lives at
///   <c>%APPDATA%\Sanduhr\webview2-fetch\</c>, separate from the sign-in profile.</item>
/// <item><b>Auth by cookie injection.</b> Before navigating, the <c>sessionKey</c>
///   (+ <c>cf_clearance</c> when supplied) is written into the WebView2 cookie jar
///   for both <c>.claude.ai</c> and <c>claude.ai</c>, then we navigate
///   <c>https://claude.ai</c>; the browser solves Cloudflare itself. sessionKey
///   alone authenticates the API, exactly as the Python build proves.</item>
/// <item><b>Async correlation via WebMessageReceived.</b> Each request injects a
///   script that does <c>fetch(...).then(... postMessage({id,status,body}))</c>;
///   replies are correlated back to a <see cref="TaskCompletionSource{TResult}"/>
///   by a generated id. <c>ExecuteScriptAsync</c> alone does not reliably await a
///   Promise, so the postMessage round-trip is the reliable path.</item>
/// </list>
///
/// All WebView2 access is marshalled to the UI thread (the dispatcher), and the
/// WebView2 is kept alive across the 5-minute refresh cadence rather than rebuilt
/// per fetch.
///
/// Secret hygiene: sessionKey / cf_clearance only ever enter the WebView2 cookie
/// jar; they are never logged and never written to disk in plaintext. The
/// diagnostic log at <c>%APPDATA%\Sanduhr\fetch-debug.log</c> records nav status,
/// each fetch's HTTP status, success/fail, and tier counts only.
/// </summary>
public sealed class WebView2ApiClient : IClaudeApiClient, IDisposable
{
    private const string Origin = ClaudeSignIn.CookieOrigin;          // https://claude.ai
    private const string ApiBase = "https://claude.ai/api";
    private const string RoutinesUrl = "https://claude.ai/v1/code/routines/run-budget";

    private static readonly TimeSpan NavTimeout = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    // Anthropic client headers the /v1/code/* endpoints require alongside
    // x-organization-uuid (port of _ROUTINES_HEADERS / ClaudeApiClient.RoutinesHeaders).
    private static readonly KeyValuePair<string, string>[] RoutinesHeaders =
    {
        new("anthropic-client-platform", "web_claude_ai"),
        new("anthropic-version", "2023-06-01"),
        new("anthropic-beta", "ccr-triggers-2026-01-30"),
    };

    private readonly string _sessionKey;
    private readonly string? _cfClearance;
    private readonly string _profileDir;
    private readonly string _logPath;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<FetchReply>> _pending = new();

    private Window? _host;
    private WebView2? _web;
    private Task? _ready;

    private string? _orgId;
    private JsonObject? _accountNode;
    private bool _disposed;

    public AccountPlan? Account { get; private set; }

    /// <param name="sessionKey">The claude.ai <c>sessionKey</c> cookie value.</param>
    /// <param name="cfClearance">Optional <c>cf_clearance</c> cookie (injected too, though the live browser usually clears CF on its own).</param>
    public WebView2ApiClient(string sessionKey, string? cfClearance = null)
    {
        _sessionKey = sessionKey;
        _cfClearance = cfClearance;
        var paths = new Paths();
        _profileDir = paths.WebView2FetchDir;
        _logPath = Path.Combine(paths.AppDataDir, "fetch-debug.log");
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>The reply correlated back from an in-page fetch. Exactly one of a
    /// 2xx status / typed status / <see cref="Error"/> is meaningful.</summary>
    private readonly record struct FetchReply(int Status, string? Body, string? Error);

    // -- IClaudeApiClient -----------------------------------------------------

    /// <inheritdoc />
    public Task<JsonObject> GetUsageAsync(CancellationToken cancellationToken = default)
        => OnUiAsync(() => GetUsageCoreAsync(cancellationToken));

    /// <inheritdoc />
    public Task<RoutineBudget?> GetRoutineBudgetAsync(CancellationToken cancellationToken = default)
        => OnUiAsync(() => GetRoutineBudgetCoreAsync(cancellationToken));

    private async Task<JsonObject> GetUsageCoreAsync(CancellationToken ct)
    {
        await EnsureReadyOrResetAsync(ct).ConfigureAwait(true);

        var orgId = await GetOrgIdCoreAsync(ct).ConfigureAwait(true);

        var reply = await FetchAsync($"{ApiBase}/organizations/{orgId}/usage", null, ct).ConfigureAwait(true);
        var body = RequireBody(reply, "usage");
        JsonObject data;
        try { data = ClaudeApiParsing.ParseUsage(body, _accountNode); }
        catch (CloudflareBlockedException)
        {
            // The page cleared init but is now wedged behind a challenge —
            // EnsureReadyOrResetAsync only re-initializes when _ready is null
            // (_ready ??= InitAsync(ct)), so drop the cached init task and let
            // the next cycle rebuild the page.
            _ready = null;
            throw;
        }
        Log($"usage: status={reply.Status} tiers={CountTiers(data)}");
        return data;
    }

    private async Task<RoutineBudget?> GetRoutineBudgetCoreAsync(CancellationToken ct)
    {
        await EnsureReadyOrResetAsync(ct).ConfigureAwait(true);

        var orgId = await GetOrgIdCoreAsync(ct).ConfigureAwait(true);

        var headers = new List<KeyValuePair<string, string>>(RoutinesHeaders)
        {
            new("x-organization-uuid", orgId),
        };

        var reply = await FetchAsync(RoutinesUrl, headers, ct).ConfigureAwait(true);
        if (reply.Error is not null)
            throw new NetworkException($"Routines fetch failed: {reply.Error}");
        if (reply.Status == 404)
        {
            Log("routines: 404 (no code access) -> null");
            return null; // account without Routines / code access
        }
        ClaudeApiParsing.ThrowForStatus(reply.Status, reply.Body);
        var budget = ClaudeApiParsing.ParseRoutineBudget(reply.Body ?? "");
        Log($"routines: status={reply.Status} budget={(budget is null ? "null" : "ok")}");
        // Diagnostic for the routines-null-on-200 case: ParseRoutineBudget expects a
        // top-level {used, limit} object. When a 200 still parses to null, the body's
        // actual shape differs — log its TOP-LEVEL JSON KEYS ONLY (never values, no
        // secrets) so the real shape is visible next run without guessing.
        if (budget is null)
            Log($"routines: NULL-PARSE top-level keys=[{TopLevelJsonKeys(reply.Body)}]");
        return budget;
    }

    private async Task<string> GetOrgIdCoreAsync(CancellationToken ct)
    {
        if (_orgId is not null) return _orgId;

        var reply = await FetchAsync($"{ApiBase}/organizations", null, ct).ConfigureAwait(true);
        var body = RequireBody(reply, "organizations");
        ClaudeApiParsing.OrgDiscovery discovery;
        try { discovery = ClaudeApiParsing.ParseOrganizations(body); }
        catch (CloudflareBlockedException)
        {
            // Same wedge as GetUsageCoreAsync: drop the cached init task so
            // EnsureReadyOrResetAsync re-navigates the page next cycle instead
            // of retrying fetches against a page stuck behind a challenge.
            _ready = null;
            throw;
        }
        _orgId = discovery.OrgId;
        _accountNode = discovery.AccountNode;
        Account = discovery.Account;
        Log($"orgs: status={reply.Status} tier={discovery.Account.RateLimitTier ?? "(none)"}");
        return _orgId;
    }

    /// <summary>Map a fetch reply to its 2xx body or throw the typed error
    /// (transport error → Network, then status mapping).</summary>
    private static string RequireBody(FetchReply reply, string what)
    {
        if (reply.Error is not null)
            throw new NetworkException($"{what} fetch failed: {reply.Error}");
        ClaudeApiParsing.ThrowForStatus(reply.Status, reply.Body);
        return reply.Body ?? "";
    }

    // -- WebView2 lifecycle (all on the UI thread) ----------------------------

    private async Task EnsureReadyOrResetAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _ready ??= InitAsync(ct);
            await _ready.ConfigureAwait(true);
        }
        catch
        {
            // A one-time init failure (e.g. transient WebView2 / network) must not
            // wedge the client forever — clear the cached task so the next refresh
            // re-initializes from scratch.
            _ready = null;
            throw;
        }
    }

    private async Task InitAsync(CancellationToken ct)
    {
        // Re-init must not strand the prior host/webview. EnsureReadyOrResetAsync's
        // catch and the CloudflareBlockedException handlers above null out _ready to
        // force a rebuild, and while a challenge stays wedged the 5-minute refresh
        // timer re-enters InitAsync every cycle — without this teardown each cycle
        // would abandon the previous hidden Window + its live msedgewebview2.exe
        // process (and its OnWebMessageReceived subscription) instead of replacing it.
        TearDownHostAndWebView();

        Directory.CreateDirectory(_profileDir);
        Log("init: creating hidden host + WebView2 environment");

        // Borderless, off-screen, taskbar-less, non-activating host: a real HWND
        // (so the WebView2 runs JS) that is never visible to the user.
        _host = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 480,
            Height = 360,
            Left = -32000,
            Top = -32000,
            Title = "Sanduhr usage transport",
        };
        _web = new WebView2();
        _host.Content = _web;
        _host.Show(); // realizes the HWND without stealing focus (ShowActivated=false)

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _profileDir).ConfigureAwait(true);
        await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = _web.CoreWebView2;
        core.Settings.UserAgent = ClaudeSignIn.BrowserUserAgent;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;

        InjectAuthCookies(core);

        // Navigate and wait for the first NavigationCompleted — the browser solves
        // Cloudflare during this load. We don't fail on a nav miss; the fetch below
        // is the real source of truth (and has its own timeout).
        var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e) => navTcs.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnNav;
        try
        {
            core.Navigate(Origin);
            var done = await Task.WhenAny(navTcs.Task, Task.Delay(NavTimeout, ct)).ConfigureAwait(true);
            bool navOk = done == navTcs.Task && navTcs.Task.Result;
            Log($"init: nav ok={navOk} host={SafeHost(core.Source)}");
        }
        finally
        {
            core.NavigationCompleted -= OnNav;
        }
    }

    private void InjectAuthCookies(CoreWebView2 core)
    {
        // SESSION-BLEED GUARD (multi-account). The transport profile at
        // %APPDATA%\Sanduhr\webview2-fetch\ is SHARED across accounts, so its
        // cookie jar still holds the PREVIOUS account's claude.ai cookies on disk
        // (sessionKey, __Secure-* auth cookies, lastActiveOrg, __cf_bm, …). A new
        // WebView2ApiClient is built per account switch (WidgetViewModel.RebuildFetcher),
        // and each runs InitAsync against this same profile. Injecting the new
        // sessionKey is NOT enough on its own: a stale auth cookie can out-rank it
        // and make the in-page fetch return the OLD account's usage. So we wipe the
        // jar clean here, every (re)build, BEFORE adding the new account's cookies
        // and BEFORE navigating — guaranteeing the browser presents only the
        // account we're switching to. This is the same multi-account isolation the
        // sign-in flow gets from its per-capture user-data folders. cf_clearance is
        // re-injected below when supplied, and the live browser re-solves Cloudflare
        // on navigation regardless, so clearing everything is safe.
        try
        {
            core.CookieManager.DeleteAllCookies();
            Log("init: cleared transport cookie jar (anti-bleed)");
        }
        catch (Exception ex)
        {
            Log($"init: cookie clear failed: {ex.Message}");
        }

        // Both domain variants: .claude.ai (host-wide) and claude.ai (exact). The
        // browser sends whichever matches the request origin.
        DateTime expires = DateTime.UtcNow.AddDays(30);
        foreach (var domain in new[] { ".claude.ai", "claude.ai" })
        {
            var session = core.CookieManager.CreateCookie(ClaudeSignIn.SessionKeyCookie, _sessionKey, domain, "/");
            session.IsSecure = true;
            session.IsHttpOnly = true;
            session.Expires = expires;
            core.CookieManager.AddOrUpdateCookie(session);

            if (!string.IsNullOrEmpty(_cfClearance))
            {
                var cf = core.CookieManager.CreateCookie(ClaudeSignIn.CfClearanceCookie, _cfClearance, domain, "/");
                cf.IsSecure = true;
                cf.Expires = expires;
                core.CookieManager.AddOrUpdateCookie(cf);
            }
        }
        Log($"init: cookies injected (sessionKey present={!string.IsNullOrEmpty(_sessionKey)} cf={!string.IsNullOrEmpty(_cfClearance)})");
    }

    // -- in-page fetch + correlation ------------------------------------------

    /// <summary>
    /// Run a GET from the claude.ai page context and await the correlated reply.
    /// Must be called on the UI thread (the awaits yield to the dispatcher so
    /// WebMessageReceived can resolve the pending task).
    /// </summary>
    private async Task<FetchReply> FetchAsync(
        string url, IEnumerable<KeyValuePair<string, string>>? headers, CancellationToken ct)
    {
        if (_web?.CoreWebView2 is null)
            return new FetchReply(0, null, "WebView2 not initialized");

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<FetchReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(FetchTimeout);
        using var reg = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
            {
                if (ct.IsCancellationRequested)
                    pending.TrySetCanceled(ct);
                else
                    pending.TrySetResult(new FetchReply(0, null, "fetch timed out"));
            }
        });

        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync(BuildFetchScript(id, url, headers)).ConfigureAwait(true);
            return await tcs.Task.ConfigureAwait(true);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Build the injected fetch script. URL / headers / id are JSON-serialized so
    /// they embed as valid, injection-safe JS literals. The fetch reads the body
    /// as text for every HTTP status (fetch only rejects on transport failure, not
    /// on 4xx/5xx), then postMessages {id,status,body} or {id,error}.
    /// </summary>
    private static string BuildFetchScript(string id, string url, IEnumerable<KeyValuePair<string, string>>? headers)
    {
        var headerMap = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (headers is not null)
            foreach (var kv in headers)
                headerMap[kv.Key] = kv.Value;

        string idJson = JsonSerializer.Serialize(id);
        string urlJson = JsonSerializer.Serialize(url);
        string headersJson = JsonSerializer.Serialize(headerMap);

        return $$"""
        (function () {
          try {
            var id = {{idJson}};
            var url = {{urlJson}};
            var headers = {{headersJson}};
            fetch(url, { method: 'GET', credentials: 'include', headers: headers })
              .then(function (r) { return r.text().then(function (t) { return { status: r.status, body: t }; }); })
              .then(function (res) { window.chrome.webview.postMessage(JSON.stringify({ id: id, status: res.status, body: res.body })); })
              .catch(function (e) { window.chrome.webview.postMessage(JSON.stringify({ id: id, error: String(e) })); });
          } catch (e) {
            window.chrome.webview.postMessage(JSON.stringify({ id: {{idJson}}, error: 'inject: ' + String(e) }));
          }
        })();
        """;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch { return; } // not a string message — not ours

        if (string.IsNullOrEmpty(raw)) return;

        JsonNode? node;
        try { node = JsonNode.Parse(raw); }
        catch { return; }
        if (node is not JsonObject obj) return;

        var id = (string?)obj["id"];
        if (id is null || !_pending.TryRemove(id, out var tcs)) return;

        var error = (string?)obj["error"];
        if (error is not null)
        {
            tcs.TrySetResult(new FetchReply(0, null, error));
            return;
        }

        int status = obj["status"] is JsonNode s ? (int)s.GetValue<double>() : 0;
        string? body = (string?)obj["body"];
        tcs.TrySetResult(new FetchReply(status, body, null));
    }

    // -- helpers --------------------------------------------------------------

    /// <summary>Run <paramref name="work"/> on the UI thread. The whole client is
    /// UI-thread-affine; <see cref="UsageFetcher"/> awaits with ConfigureAwait(false)
    /// so the continuation can land on a pool thread — this re-marshals.</summary>
    private Task<T> OnUiAsync<T>(Func<Task<T>> work)
        => _dispatcher.CheckAccess()
            ? work()
            : _dispatcher.InvokeAsync(work).Task.Unwrap();

    private static int CountTiers(JsonObject data)
    {
        int n = 0;
        foreach (var kv in data)
            if (kv.Key != "_account" && kv.Value is JsonObject)
                n++;
        return n;
    }

    private static string SafeHost(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "(?)";

    /// <summary>Top-level JSON object keys of a response body, comma-joined — KEYS
    /// ONLY, never values. Used by the routines-null diagnostic to surface the
    /// body's real shape without ever logging secret/usage values.</summary>
    private static string TopLevelJsonKeys(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "(empty)";
        try
        {
            return JsonNode.Parse(body) switch
            {
                JsonObject o => string.Join(",", o.Select(kv => kv.Key)),
                JsonArray => "(array)",
                _ => "(non-object)",
            };
        }
        catch
        {
            return "(non-json)";
        }
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never break a fetch
        }
    }

    /// <summary>
    /// Unsubscribe + dispose/close the current <see cref="_web"/>/<see cref="_host"/>
    /// (if any) and null the fields. Shared by <see cref="InitAsync"/> (tearing down
    /// a prior instance before re-init) and <see cref="Dispose"/> (final teardown).
    /// Every step is independently try/catch-swallowed: a half-initialized prior
    /// instance (e.g. <see cref="_web"/> constructed but its CoreWebView2 never came
    /// up) must not throw and block whichever caller needs teardown to complete —
    /// InitAsync's re-init in particular must never fail because the OLD instance
    /// didn't clean up nicely.
    /// </summary>
    private void TearDownHostAndWebView()
    {
        try
        {
            if (_web?.CoreWebView2 is not null)
                _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        catch { /* best-effort */ }
        try { _web?.Dispose(); } catch { /* best-effort */ }
        try { _host?.Close(); } catch { /* best-effort */ }
        _web = null;
        _host = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_dispatcher.CheckAccess())
            TearDownHostAndWebView();
        else
            _dispatcher.Invoke(TearDownHostAndWebView);
    }
}
