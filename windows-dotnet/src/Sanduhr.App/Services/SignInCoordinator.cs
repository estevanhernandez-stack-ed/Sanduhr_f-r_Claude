using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Sanduhr.App.Modals;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>Result of a sign-in attempt. <see cref="Added"/> is the signal the
/// widget should rebuild its fetcher and re-fetch; <see cref="Label"/> is the
/// account that was saved (and made active).</summary>
public readonly record struct SignInOutcome(bool Added, string? Label)
{
    public static readonly SignInOutcome NotAdded = new(false, null);
}

/// <summary>
/// Orchestrates the embedded "Sign in to Claude" flow and its fallbacks — the
/// App-side counterpart to ROROROblox's <c>CookieCapture</c>. Owns the WebView2
/// runtime pre-check, the per-capture user-data folder lifecycle
/// (<see cref="WebView2UserDataDirectory"/> rooted at <c>%APPDATA%\Sanduhr\webview2\</c>),
/// the account-persistence semantics (first-run → "Personal"; subsequent →
/// named slot + make active), and the graceful WebView2-missing → manual-paste
/// degradation.
///
/// Stateless over the OS credential store: it constructs its own
/// <see cref="AccountStore"/>/<see cref="CredentialStore"/> against the same
/// Windows Credential Manager the widget reads, so a save here is visible to the
/// widget's next <c>ReloadAfterSignIn</c>.
/// </summary>
public sealed class SignInCoordinator
{
    private readonly AccountStore _accounts;
    private readonly CredentialStore _credentials;
    private readonly WebView2UserDataDirectory _userData;

    public SignInCoordinator()
    {
        var paths = new Paths();
        _accounts = new AccountStore(new WindowsCredentialManager(AccountStore.Service));
        _credentials = new CredentialStore(_accounts, paths);
        _userData = new WebView2UserDataDirectory(Path.Combine(paths.AppDataDir, "webview2"));
    }

    /// <summary>
    /// Run the embedded WebView2 login. Falls back to the manual-paste modal when
    /// the WebView2 runtime is missing or capture fails.
    /// </summary>
    public async Task<SignInOutcome> SignInEmbeddedAsync(Window? owner)
    {
        if (!IsRuntimeAvailable())
            return ShowRuntimeMissingThenMaybeManual(owner);

        string dir;
        try
        {
            dir = _userData.AllocateNew();
            _userData.SweepStale(exclude: dir);
        }
        catch (Exception ex)
        {
            ShowMessage(owner,
                $"Couldn't prepare the sign-in browser profile: {ex.Message}",
                MessageBoxButton.OK);
            return SignInManual(owner);
        }

        var window = new SignInWindow(dir, PersistEmbedded);
        SetOwner(window, owner);
        var result = await window.RunAsync().ConfigureAwait(true);

        switch (result)
        {
            case SignInResult.Success s:
                _userData.SweepStale(); // best-effort cleanup; session is captured + saved
                return new SignInOutcome(true, s.Label);

            case SignInResult.RuntimeMissing:
                return ShowRuntimeMissingThenMaybeManual(owner);

            case SignInResult.Failed f:
                var retry = ShowMessage(owner,
                    $"{f.Message}\n\nPaste a sessionKey by hand instead?",
                    MessageBoxButton.YesNo);
                return retry == MessageBoxResult.Yes ? SignInManual(owner) : SignInOutcome.NotAdded;

            default: // Cancelled
                return SignInOutcome.NotAdded;
        }
    }

    /// <summary>Open the manual sessionKey-paste fallback directly.</summary>
    public SignInOutcome SignInManual(Window? owner)
    {
        var suggested = _accounts.GetActive() is null ? "Personal" : NextFreeLabel();
        var window = new ManualKeyWindow(suggested, PersistManual);
        SetOwner(window, owner);
        window.ShowDialog();
        return window.Result is SignInResult.Success s
            ? new SignInOutcome(true, s.Label)
            : SignInOutcome.NotAdded;
    }

    // -- persistence semantics ------------------------------------------------

    /// <summary>Embedded-flow save: first account auto-creates "Personal";
    /// subsequent ones get a named slot and become active.</summary>
    private string PersistEmbedded(CapturedCookies cookies)
    {
        if (_accounts.GetActive() is null)
        {
            _credentials.Save(cookies.SessionKey, cookies.CfClearance);
            return _accounts.GetActive() ?? "Personal";
        }
        var label = NextFreeLabel();
        _accounts.AddAccount(label, cookies.SessionKey!, cookies.CfClearance);
        _accounts.SetActive(label);
        return label;
    }

    /// <summary>Manual-flow save: explicit user-named slot, then make it active.</summary>
    private string PersistManual(string label, CapturedCookies cookies)
    {
        _accounts.AddAccount(label, cookies.SessionKey!, cookies.CfClearance);
        _accounts.SetActive(label);
        return label;
    }

    private string NextFreeLabel()
    {
        var existing = new HashSet<string>(_accounts.ListAccounts(), StringComparer.OrdinalIgnoreCase);
        for (int n = 2; ; n++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "Account {0}", n);
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    // -- WebView2 runtime + fallbacks -----------------------------------------

    private static bool IsRuntimeAvailable()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString(browserExecutableFolder: null);
            return !string.IsNullOrEmpty(version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private SignInOutcome ShowRuntimeMissingThenMaybeManual(Window? owner)
    {
        var modal = new WebView2NotInstalledWindow();
        SetOwner(modal, owner);
        var choice = modal.ShowDialog();
        // false == "Paste a key instead"; true == went to installer; null == closed.
        return choice == false ? SignInManual(owner) : SignInOutcome.NotAdded;
    }

    private static void SetOwner(Window window, Window? owner)
    {
        if (owner is not null && owner.IsLoaded)
            window.Owner = owner;
    }

    private static MessageBoxResult ShowMessage(Window? owner, string text, MessageBoxButton buttons)
        => owner is not null && owner.IsLoaded
            ? MessageBox.Show(owner, text, "Sign in to Claude", buttons, MessageBoxImage.Warning)
            : MessageBox.Show(text, "Sign in to Claude", buttons, MessageBoxImage.Warning);
}
