using System.Windows;
using System.Windows.Input;
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
        w.Title = "Update sessionKey";
        w.HeadlineText.Text = $"Update sessionKey for '{label}'";
        w.IntroText.Text = "Paste a fresh claude.ai sessionKey for this account. Its history and settings stay put.";
        w.LabelBox.IsEnabled = false;
        w.LabelBox.Opacity = 0.6;
        w.SaveButton.Content = "Update";
        return w;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text?.Trim() ?? "";
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
            ShowError(ex.Message);
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
    private void OnUseEmbeddedClick(object sender, MouseButtonEventArgs e)
    {
        Result = new SignInResult.UseEmbedded();
        DialogResult = false; // close; the coordinator reads Result, not DialogResult
        Close();
    }

    /// <summary>Toggle the collapsed "Where do I find the sessionKey?" DevTools steps.</summary>
    private void OnHelpToggleClick(object sender, MouseButtonEventArgs e)
        => HelpSteps.Visibility = HelpSteps.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
