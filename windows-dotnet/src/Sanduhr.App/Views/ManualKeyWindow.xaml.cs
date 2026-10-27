using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Minimal manual-paste fallback (the retained "Add Account by sessionKey" path
/// for power users / edge cases — when WebView2 is missing or the embedded login
/// won't cooperate). Collects a label + sessionKey (+ optional cf_clearance) and
/// persists through the injected delegate, which writes to the same
/// <c>CredentialStore</c> / Credential Manager the embedded flow uses. The full
/// account-management UI is item 7; this is the floor that keeps the path
/// reachable now.
/// </summary>
internal partial class ManualKeyWindow : Window
{
    private readonly Func<string, CapturedCookies, string> _persist;

    /// <summary>Set after a successful save; <c>Cancelled</c> otherwise.</summary>
    public SignInResult Result { get; private set; } = new SignInResult.Cancelled();

    /// <param name="suggestedLabel">Pre-filled account name (e.g. "Personal" or "Account 2").</param>
    /// <param name="persist">
    /// Saves (label, cookies) and returns the final label. May throw
    /// (invalid/duplicate label, credential write failure) — shown inline.
    /// </param>
    public ManualKeyWindow(string suggestedLabel, Func<string, CapturedCookies, string> persist, bool embeddedAvailable = false)
    {
        _persist = persist;
        InitializeComponent();
        LabelBox.Text = suggestedLabel;
        UseEmbeddedLink.Visibility = embeddedAvailable ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => SessionKeyBox.Focus();
    }

    /// <summary>Reauth mode: update the key for an EXISTING account. The label is
    /// shown but locked (in-place overwrite — the persist delegate targets the
    /// label by closure, so what's displayed can't drift from what's written).</summary>
    public static ManualKeyWindow ForReauth(
        string label, Func<string, CapturedCookies, string> persist, bool embeddedAvailable)
    {
        var w = new ManualKeyWindow(label, persist, embeddedAvailable);
        // Streamer mode (slice 7, CP 2): the window names the account by its codename. Save still targets the real label
        // (see OnSaveClick), so a codename is never persisted.
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        w._hidden = shown == label ? null : (label, shown);
        w.LabelBox.Text = shown;
        w.Title = "Update sessionKey";
        w.HeadlineText.Text = $"Update sessionKey for '{shown}'";
        w.IntroText.Text = "Paste a fresh claude.ai sessionKey for this account. Its history and settings stay put.";
        w.LabelBox.IsEnabled = false;
        w.LabelBox.Opacity = 0.6;
        w.SaveButton.Content = "Update";
        return w;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Re-sign-in under streamer mode: the box shows the codename, the account is the real label.
        var label = _hidden?.Real ?? LabelBox.Text?.Trim() ?? "";
        var sessionKey = SessionKeyBox.Text?.Trim() ?? "";
        var cf = CfClearanceBox.Text?.Trim();

        if (string.IsNullOrEmpty(sessionKey))
        {
            ShowError("Paste a sessionKey to continue.");
            return;
        }
        if (string.IsNullOrEmpty(label))
        {
            ShowError("Give the account a name.");
            return;
        }

        try
        {
            var captured = new CapturedCookies(sessionKey, string.IsNullOrEmpty(cf) ? null : cf);
            var savedLabel = _persist(label, captured);
            Result = new SignInResult.Success(savedLabel);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);   // streamer: masked - via ShowError: the text passes Mask.Paths and the codename swap (CP 18)
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = new SignInResult.Cancelled();
        DialogResult = false;
        Close();
    }

    /// <summary>"Use the secure sign-in window instead" — close with a UseEmbedded
    /// result so the coordinator bounces to the embedded sign-in flow. Only visible
    /// when the WebView2 runtime is present.</summary>
    private void OnUseEmbeddedClick(object sender, RoutedEventArgs e)
    {
        Result = new SignInResult.UseEmbedded();
        DialogResult = false; // close; the coordinator reads Result, not DialogResult
        Close();
    }

    /// <summary>Toggle the collapsed "Where do I find the sessionKey?" steps. A Hyperlink, so Tab reaches it and Enter opens it.</summary>
    private void OnHelpToggleClick(object sender, RoutedEventArgs e)
        => HelpSteps.Visibility = HelpSteps.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    /// <summary>Shows or hides the cf_clearance field behind "Seeing a Cloudflare error?" (WF31). A Hyperlink, so
    /// Tab reaches it and Enter opens it.</summary>
    private void OnCloudflareToggleClick(object sender, RoutedEventArgs e)
        => CloudflarePanel.Visibility = CloudflarePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    internal void ShowErrorForTests(string message) => ShowError(message);

    /// <summary>Re-sign-in under streamer mode: the real label and its codename, so an error never names the account (slice 7, M6).</summary>
    private (string Real, string Shown)? _hidden;

    private void ShowError(string message)
    {
        if (_hidden is { } h)
            message = message.Replace(h.Real, h.Shown, StringComparison.Ordinal);
        message = StreamerMode.Mask.Paths(message);
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
