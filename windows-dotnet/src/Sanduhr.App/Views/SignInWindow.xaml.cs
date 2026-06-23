using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The embedded "Sign in to Claude" window — ROROROblox's <c>CookieCaptureWindow</c>
/// pattern lifted 1:1, retargeted from roblox.com/.ROBLOSECURITY to
/// claude.ai/sessionKey.
///
/// Hosts a WebView2 against an app-owned user-data folder (passed in, rooted at
/// <c>%APPDATA%\Sanduhr\webview2\</c> — isolated from the user's Chrome/Edge),
/// navigates <see cref="ClaudeSignIn.LoginUrl"/>, lets the user authenticate via
/// the real Anthropic login (Google / email / passkey), and once the
/// <c>sessionKey</c> cookie appears on any claude.ai page pulls it (+
/// <c>cf_clearance</c>) straight off <c>CoreWebView2.CookieManager</c>.
///
/// Capture is driven by THREE triggers: NavigationCompleted, SourceChanged, AND a
/// 1.5s poll. The poll exists because claude.ai is a SPA — after auth it often
/// sets the sessionKey cookie via XHR and client-side-routes without firing a
/// navigation event we can hook, so an event-only capture (which works for
/// Roblox) silently never fires here. Cookie presence is the truth signal; the
/// gate is host-only (any claude.ai page), matching RORORO — the per-capture
/// profile starts empty, so there is no stale cookie to guard against.
///
/// The captured secret is handed to the injected <see cref="_persist"/> delegate
/// (which writes it to the Credential Manager) and never touched again — never
/// logged, never written to disk in plaintext, never placed on the result. The
/// debug log records only the URL path + cookie PRESENCE booleans, never values.
/// </summary>
internal partial class SignInWindow : Window
{
    private readonly string _userDataDir;
    private readonly Func<CapturedCookies, string> _persist;
    private readonly TaskCompletionSource<SignInResult> _tcs = new();
    private bool _captured;
    private bool _firstNavComplete;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _loadTimeout;
    private bool _loadFailed;

    /// <param name="userDataDir">App-owned WebView2 profile folder (already allocated).</param>
    /// <param name="persist">
    /// Persists the captured cookies and returns the account label saved under.
    /// Runs on the UI thread inside the capture handler; may throw — the window
    /// converts a throw into <see cref="SignInResult.Failed"/>.
    /// </param>
    public SignInWindow(string userDataDir, Func<CapturedCookies, string> persist)
    {
        _userDataDir = userDataDir;
        _persist = persist;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public Task<SignInResult> RunAsync()
    {
        Show();
        return _tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("window loaded; creating environment");
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataDir);
            await WebView.EnsureCoreWebView2Async(environment);

            // Use the SAME UA the API client sends (ClaudeSignIn.BrowserUserAgent):
            // (1) cf_clearance is UA-bound, so a clearance captured here must be
            //     replayed by ClaudeApiClient under an identical UA or CF rejects it.
            // (2) WebView2's default UA carries Edge/embedded markers Google's
            //     sign-in refuses; a clean desktop-Chrome UA is the standard
            //     mitigation for OAuth inside a WebView2.
            WebView.CoreWebView2.Settings.UserAgent = ClaudeSignIn.BrowserUserAgent;

            // NavigationCompleted catches server-driven loads; SourceChanged catches
            // SPA route changes. The poll below catches the case neither fires —
            // claude.ai setting the cookie via XHR without a hooked nav event.
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.SourceChanged += OnSourceChanged;

            // Keep OAuth/popup flows inside this window rather than dead-ending a
            // window.open() against a null opener.
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            WebView.CoreWebView2.Navigate(ClaudeSignIn.LoginUrl);

            // Poll fallback — the load-bearing fix for "the window never closed." Plus a
            // load timeout that surfaces an error + escape hatch if the page never paints
            // (offline / blocked), instead of an indefinite "Loading claude.ai…".
            StartCaptureTimers();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            CompleteAndClose(new SignInResult.RuntimeMissing());
        }
        catch (Exception ex)
        {
            CompleteAndClose(new SignInResult.Failed($"Couldn't start the sign-in browser: {ex.Message}"));
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Reuse the top-level WebView for popups so multi-step logins don't break.
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Uri))
            WebView.CoreWebView2.Navigate(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_firstNavComplete && e.IsSuccess)
        {
            _firstNavComplete = true;
            StopLoadTimeout();
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        else if (!_firstNavComplete && !e.IsSuccess)
        {
            // The first load itself failed (offline / DNS / cert). Surface the error +
            // escape hatch rather than waiting out the full timeout on a dead load.
            ShowLoadError("claude.ai didn't load. Check your connection and try again.");
        }
        UpdateOAuthNotice();
        _ = TryCaptureAsync("nav");
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        UpdateOAuthNotice();
        _ = TryCaptureAsync("source");
    }

    /// <summary>Start (or restart, after a "Try again") the capture poll and the
    /// first-load timeout.</summary>
    private void StartCaptureTimers()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _pollTimer.Tick += (_, _) => _ = TryCaptureAsync("poll");
        _pollTimer.Start();

        _loadTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _loadTimeout.Tick += (_, _) =>
        {
            if (!_firstNavComplete && !_captured)
                ShowLoadError("claude.ai didn't load in time. Check your connection and try again.");
        };
        _loadTimeout.Start();
    }

    /// <summary>Swap the loading overlay for an error panel with retry + manual-paste
    /// escape hatch. Marks the window load-failed so a subsequent user close reports
    /// <see cref="SignInResult.Failed"/> (offering manual paste) rather than a Cancel.</summary>
    private void ShowLoadError(string message)
    {
        if (_captured)
            return;
        _loadFailed = true;
        StopPoll();
        StopLoadTimeout();
        Log($"load error surfaced: {message}");
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ErrorMessage.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void OnTryAgainClick(object sender, RoutedEventArgs e)
    {
        _loadFailed = false;
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            WebView.CoreWebView2?.Navigate(ClaudeSignIn.LoginUrl);
            StartCaptureTimers();
        }
        catch (Exception ex)
        {
            ShowLoadError($"Couldn't restart the sign-in browser: {ex.Message}");
        }
    }

    private void OnPasteInsteadClick(object sender, RoutedEventArgs e)
        => CompleteAndClose(new SignInResult.Failed("Couldn't load the embedded sign-in."));

    /// <summary>Show the Google-OAuth guidance banner while the WebView is on a Google
    /// auth host (where sign-in renders blank), and hide it back on claude.ai.</summary>
    private void UpdateOAuthNotice()
    {
        var host = SafeHost(WebView.CoreWebView2?.Source);
        var onGoogle = host.StartsWith("accounts.google", StringComparison.OrdinalIgnoreCase)
                       || host.StartsWith("accounts.youtube", StringComparison.OrdinalIgnoreCase);
        GoogleNotice.Visibility = onGoogle ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGoogleNoticePasteClick(object sender, RoutedEventArgs e)
        => CompleteAndClose(new SignInResult.Failed(
            "Google sign-in won't load in the embedded window. Paste a sessionKey instead, or use email / a passkey on the Claude page."));

    private async Task TryCaptureAsync(string trigger)
    {
        if (_captured)
            return;

        try
        {
            var core = WebView.CoreWebView2;
            if (core is null)
                return;

            // NO URL gate. The CookieManager is profile-global, so we query
            // claude.ai's cookie jar directly no matter what page the WebView is
            // currently showing. claude.ai/login redirects straight to a separate
            // Anthropic auth host (signin-debug.log showed the WebView is never on
            // a claude.ai URL during login), so any URL-based gate never opens —
            // but the sessionKey cookie still lands on claude.ai the instant auth
            // completes, and the poll catches it then. Fresh per-capture profile =
            // no stale cookie, so unconditional cookie presence is a safe signal.
            var rawCookies = await core.CookieManager
                .GetCookiesAsync(ClaudeSignIn.CookieOrigin)
                .ConfigureAwait(true);

            var byName = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var c in rawCookies)
            {
                // last-non-empty wins (handles .claude.ai vs claude.ai duplicates)
                if (!byName.TryGetValue(c.Name, out var existing) || string.IsNullOrEmpty(existing))
                    byName[c.Name] = c.Value;
            }

            var captured = ClaudeSignIn.Extract(byName);
            // PRESENCE only — never the value. host + count tell us where the login
            // lives and whether claude.ai cookies are accruing yet.
            Log($"{trigger}: host={SafeHost(core.Source)} claudeCookies={rawCookies.Count} sessionKey={captured.HasSession} cf={(!string.IsNullOrEmpty(captured.CfClearance))}");

            if (!captured.HasSession)
                return;

            _captured = true;
            StopPoll();

            try
            {
                var label = _persist(captured);
                Log("captured + persisted (no value logged)");
                CompleteAndClose(new SignInResult.Success(label));
            }
            catch (Exception ex)
            {
                CompleteAndClose(new SignInResult.Failed($"Sign-in succeeded but saving the account failed: {ex.Message}"));
            }
        }
        catch (Exception ex)
        {
            // A transient cookie-read hiccup must NOT tear down the window — the
            // poll will retry. Only a genuine capture+persist failure closes it.
            Log($"{trigger}: capture threw (non-fatal, will retry): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopPoll();
        StopLoadTimeout();
        // If the window closed while showing a load error (user clicked X after a failed
        // load), report Failed so the coordinator offers the manual-paste fallback. A
        // clean close before any error is a deliberate Cancel.
        _tcs.TrySetResult(_loadFailed
            ? new SignInResult.Failed("The sign-in window closed after a load error.")
            : new SignInResult.Cancelled());
    }

    private void StopPoll()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void StopLoadTimeout()
    {
        _loadTimeout?.Stop();
        _loadTimeout = null;
    }

    private void CompleteAndClose(SignInResult result)
    {
        StopPoll();
        StopLoadTimeout();
        _tcs.TrySetResult(result);
        if (IsVisible)
            Close();
    }

    // -- diagnostics (presence only, never secret values) ---------------------

    private static string SafeHost(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "(?)";

    private static void Log(string message)
    {
        try
        {
            var dir = new Paths().AppDataDir;
            File.AppendAllText(
                Path.Combine(dir, "signin-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never break the sign-in flow
        }
    }
}
