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
/// account that was saved — made active by add flows, updated in place (active
/// pointer untouched) by reauth flows.</summary>
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
    /// Run the embedded WebView2 login (add-account semantics). Falls back to the
    /// manual-paste modal when the WebView2 runtime is missing or capture fails.
    /// </summary>
    public Task<SignInOutcome> SignInEmbeddedAsync(Window? owner)
        => RunEmbeddedAsync(owner, PersistEmbedded);

    /// <summary>
    /// Re-authenticate the ACTIVE account in place via the embedded browser —
    /// captured cookies overwrite the existing slot instead of allocating a new
    /// label, and a degrade-to-paste stays in place too. Used by the widget's
    /// Expired/Blocked recovery card for embedded-origin accounts.
    /// </summary>
    public Task<SignInOutcome> ReauthenticateActiveAsync(Window? owner)
    {
        var active = _accounts.GetActive();
        // No active account (theoretical): keep the historic create-"Personal" fallback.
        return active is null
            ? RunEmbeddedAsync(owner, PersistReauth)
            : ReauthenticateEmbeddedAsync(owner, active);
    }

    /// <summary>In-place embedded re-auth for a SPECIFIC label (Settings "Update
    /// sign-in…" works on non-active accounts). Manual fallback stays in place.</summary>
    public Task<SignInOutcome> ReauthenticateEmbeddedAsync(Window? owner, string label)
        => RunEmbeddedAsync(
            owner,
            cookies => PersistReauthFor(label, cookies),
            manualFallback: o => ReauthenticateManualAsync(o, label));

    /// <summary>In-place MANUAL re-auth of the active account — the recovery card's
    /// route for manual-origin accounts (and its "Paste a key instead" during
    /// recovery). Falls back to add semantics only when no account exists.</summary>
    public Task<SignInOutcome> ReauthenticateManualActiveAsync(Window? owner)
    {
        var active = _accounts.GetActive();
        return active is null ? SignInManual(owner) : ReauthenticateManualAsync(owner, active);
    }

    /// <summary>In-place manual re-auth for a SPECIFIC label: the paste modal in
    /// reauth mode (label locked), persisting via <see cref="PersistReauthManual"/> —
    /// no new account, no label prompt. "Use the secure sign-in window instead"
    /// bounces to the embedded reauth for the SAME label.</summary>
    public async Task<SignInOutcome> ReauthenticateManualAsync(Window? owner, string label)
    {
        var window = ManualKeyWindow.ForReauth(
            label, (_, cookies) => PersistReauthManual(label, cookies), IsRuntimeAvailable());
        SetOwner(window, owner);
        window.ShowDialog();

        return window.Result switch
        {
            SignInResult.Success s => new SignInOutcome(true, s.Label),
            SignInResult.UseEmbedded => await ReauthenticateEmbeddedAsync(owner, label),
            _ => SignInOutcome.NotAdded,
        };
    }

    /// <summary>Embedded re-auth save targeting a specific label (not the active
    /// pointer): overwrite its slots in place and stamp Embedded origin.</summary>
    private string PersistReauthFor(string label, CapturedCookies cookies)
    {
        _accounts.SaveCredentials(label, cookies.SessionKey!, cookies.CfClearance);
        SetOriginSafe(label, AccountOrigin.Embedded);
        return label;
    }

    /// <summary>Manual re-auth save: overwrite the label's slots in place — the
    /// missing counterpart to <see cref="PersistReauth"/> that used to make every
    /// recovery-paste allocate a duplicate "Account N".</summary>
    private string PersistReauthManual(string label, CapturedCookies cookies)
    {
        _accounts.SaveCredentials(label, cookies.SessionKey, cookies.CfClearance);
        SetOriginSafe(label, AccountOrigin.Manual);
        return label;
    }

    /// <summary>
    /// The shared embedded-sign-in engine. <paramref name="persist"/> decides the
    /// account semantics: <see cref="PersistEmbedded"/> adds (first-run "Personal" or
    /// a new slot), <see cref="PersistReauth"/> overwrites the active slot in place.
    /// A runtime-missing → install → Retry path re-enters THIS method with the SAME
    /// persist delegate, so a post-install retry keeps the original semantics (a
    /// re-auth stays in-place, an add stays an add).
    /// </summary>
    private async Task<SignInOutcome> RunEmbeddedAsync(
        Window? owner,
        Func<CapturedCookies, string> persist,
        Func<Window?, Task<SignInOutcome>>? manualFallback = null)
    {
        // Add-flows fall back to the add-semantics manual modal (today's behavior);
        // reauth flows pass their own in-place manual variant.
        Func<Window?, Task<SignInOutcome>> manual =
            manualFallback ?? (o => SignInManual(o, x => RunEmbeddedAsync(x, persist, manualFallback)));

        if (!IsRuntimeAvailable())
            return await ShowRuntimeMissingThenMaybeManual(owner, o => RunEmbeddedAsync(o, persist, manualFallback), manual);

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
            return await manual(owner);
        }

        var window = new SignInWindow(dir, persist);
        SetOwner(window, owner);
        var result = await window.RunAsync().ConfigureAwait(true);

        switch (result)
        {
            case SignInResult.Success s:
                _userData.SweepStale(); // best-effort cleanup; session is captured + saved
                return new SignInOutcome(true, s.Label);

            case SignInResult.RuntimeMissing:
                return await ShowRuntimeMissingThenMaybeManual(owner, o => RunEmbeddedAsync(o, persist, manualFallback), manual);

            case SignInResult.Failed f:
                var retry = ShowMessage(owner,
                    $"{f.Message}\n\nPaste a sessionKey by hand instead?",
                    MessageBoxButton.YesNo);
                return retry == MessageBoxResult.Yes
                    ? await manual(owner)
                    : SignInOutcome.NotAdded;

            case SignInResult.UseManual:
                // Straight to manual paste (e.g. the Google notice) — no extra confirm.
                return await manual(owner);

            default: // Cancelled
                return SignInOutcome.NotAdded;
        }
    }

    /// <summary>Re-auth save: overwrite the ACTIVE account's slots in place. If somehow
    /// no active account exists, fall back to first-run create-"Personal" semantics.</summary>
    private string PersistReauth(CapturedCookies cookies)
    {
        var active = _accounts.GetActive();
        if (active is null)
        {
            _credentials.Save(cookies.SessionKey, cookies.CfClearance);
            var label = _accounts.GetActive() ?? "Personal";
            SetOriginSafe(label, AccountOrigin.Embedded);
            return label;
        }
        _credentials.Save(cookies.SessionKey, cookies.CfClearance); // overwrites the active slot in place
        SetOriginSafe(active, AccountOrigin.Embedded);
        return active;
    }

    /// <summary>Open the manual sessionKey-paste fallback. <paramref name="bounceTo"/>
    /// is the embedded flow to re-enter if the user clicks "Use the secure sign-in
    /// window instead" — defaults to the add-account embedded flow; the coordinator's
    /// own fallbacks pass their originating flow so a re-auth that bounces stays a
    /// re-auth.</summary>
    public async Task<SignInOutcome> SignInManual(Window? owner, Func<Window?, Task<SignInOutcome>>? bounceTo = null)
    {
        var suggested = _accounts.GetActive() is null ? "Personal" : NextFreeLabel();
        var window = new ManualKeyWindow(suggested, PersistManual, IsRuntimeAvailable());
        SetOwner(window, owner);
        window.ShowDialog();

        return window.Result switch
        {
            SignInResult.Success s => new SignInOutcome(true, s.Label),
            SignInResult.UseEmbedded => await (bounceTo ?? SignInEmbeddedAsync)(owner),
            _ => SignInOutcome.NotAdded,
        };
    }

    // -- persistence semantics ------------------------------------------------

    /// <summary>Embedded-flow save: the first account auto-creates "Personal";
    /// subsequent ones prompt for a name (defaulting to the next free slot) and
    /// become active.</summary>
    private string PersistEmbedded(CapturedCookies cookies)
    {
        if (_accounts.GetActive() is null)
        {
            _credentials.Save(cookies.SessionKey, cookies.CfClearance);
            var label = _accounts.GetActive() ?? "Personal";
            SetOriginSafe(label, AccountOrigin.Embedded);
            return label;
        }
        var name = PromptForAccountName(NextFreeLabel());
        _accounts.AddAccount(name, cookies.SessionKey!, cookies.CfClearance);
        _accounts.SetActive(name);
        SetOriginSafe(name, AccountOrigin.Embedded);
        return name;
    }

    /// <summary>Ask the user to name a newly-added account during embedded sign-in,
    /// defaulting to (and falling back to) <paramref name="suggested"/> — the next free
    /// "Account N" slot. Validated like <c>ManualKeyWindow</c> (1-32 of [A-Za-z0-9 _-],
    /// unique); an empty, invalid, or cancelled entry keeps the suggested label.</summary>
    private string PromptForAccountName(string suggested)
    {
        var prompt = new TextPromptWindow("Name this account", "Account name", suggested);
        if (Application.Current?.MainWindow is { IsLoaded: true } main)
            prompt.Owner = main;
        if (prompt.ShowDialog() != true)
            return suggested;

        var name = prompt.Value.Trim();
        var existing = new HashSet<string>(_accounts.ListAccounts(), StringComparer.OrdinalIgnoreCase);
        if (name.Length == 0
            || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9 _-]{1,32}$")
            || existing.Contains(name))
        {
            return suggested;
        }
        return name;
    }

    /// <summary>Manual-flow save: explicit user-named slot, then make it active.</summary>
    private string PersistManual(string label, CapturedCookies cookies)
    {
        _accounts.AddAccount(label, cookies.SessionKey!, cookies.CfClearance);
        _accounts.SetActive(label);
        SetOriginSafe(label, AccountOrigin.Manual);
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

    /// <summary>Best-effort origin stamp — the label is absent from the registry
    /// only in the theoretical first-run PersistReauth fallback, where Embedded
    /// is the default reading anyway.</summary>
    private void SetOriginSafe(string label, AccountOrigin origin)
    {
        if (_accounts.ListAccounts().Contains(label))
            _accounts.SetOrigin(label, origin);
    }

    // -- WebView2 runtime + fallbacks -----------------------------------------

    internal static bool IsRuntimeAvailable()
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

    private async Task<SignInOutcome> ShowRuntimeMissingThenMaybeManual(
        Window? owner,
        Func<Window?, Task<SignInOutcome>> retryEmbedded,
        Func<Window?, Task<SignInOutcome>> manual)
    {
        var modal = new WebView2NotInstalledWindow();
        SetOwner(modal, owner);
        var choice = modal.ShowDialog();
        // true  == the modal's Retry found the runtime — re-enter the embedded flow.
        // false == "Paste a key instead".  null == closed / Learn More only.
        if (choice == true)
            return await retryEmbedded(owner);
        return choice == false ? await manual(owner) : SignInOutcome.NotAdded;
    }

    private static void SetOwner(Window window, Window? owner)
    {
        if (owner is not null && owner.IsLoaded)
            window.Owner = owner;
    }

    private static MessageBoxResult ShowMessage(Window? owner, string text, MessageBoxButton buttons)
        => ThemedDialog.Show(owner, "Sign in to Claude", text, buttons, ThemedDialogKind.Warning);
}
