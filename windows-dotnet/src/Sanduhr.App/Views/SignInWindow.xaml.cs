using System.Threading.Tasks;
using System.Windows;
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
/// the real Anthropic login (Google / email / passkey), and on the first nav to a
/// signed-in claude.ai URL pulls the <c>sessionKey</c> (+ <c>cf_clearance</c>)
/// straight off <c>CoreWebView2.CookieManager</c>. Cookie presence is the truth
/// signal; the URL gate just keeps us off the login/OAuth surfaces.
///
/// The captured secret is handed to the injected <see cref="_persist"/> delegate
/// (which writes it to the Credential Manager) and never touched again — never
/// logged, never written to disk in plaintext, never placed on the result.
/// </summary>
internal partial class SignInWindow : Window
{
    private readonly string _userDataDir;
    private readonly Func<CapturedCookies, string> _persist;
    private readonly TaskCompletionSource<SignInResult> _tcs = new();
    private bool _captured;
    private bool _firstNavComplete;

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
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataDir);
            await WebView.EnsureCoreWebView2Async(environment);

            // NavigationCompleted catches server-driven page loads; SourceChanged
            // catches the SPA route changes claude.ai does post-login. The
            // cookie check below is the truth signal for "user is authenticated."
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.SourceChanged += OnSourceChanged;

            // Keep OAuth/popup flows inside this window rather than dead-ending a
            // window.open() against a null opener.
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            WebView.CoreWebView2.Navigate(ClaudeSignIn.LoginUrl);
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
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        _ = TryCaptureAsync();
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        => _ = TryCaptureAsync();

    private async Task TryCaptureAsync()
    {
        if (_captured)
            return;

        try
        {
            var sourceUrl = WebView.CoreWebView2.Source;

            // Gate: only a signed-in claude.ai page (not /login, /oauth, …, not a
            // foreign OAuth host). Cookie presence is the actual confirmation.
            if (!ClaudeSignIn.IsSignedInUrl(sourceUrl))
                return;

            var rawCookies = await WebView.CoreWebView2.CookieManager
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
            if (!captured.HasSession)
                return;

            _captured = true;

            try
            {
                var label = _persist(captured);
                CompleteAndClose(new SignInResult.Success(label));
            }
            catch (Exception ex)
            {
                CompleteAndClose(new SignInResult.Failed($"Sign-in succeeded but saving the account failed: {ex.Message}"));
            }
        }
        catch (Exception ex)
        {
            CompleteAndClose(new SignInResult.Failed($"Sign-in capture failed: {ex.Message}"));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // If the window closed before we completed (user clicked X), report Cancelled.
        _tcs.TrySetResult(new SignInResult.Cancelled());
    }

    private void CompleteAndClose(SignInResult result)
    {
        _tcs.TrySetResult(result);
        if (IsVisible)
            Close();
    }
}
