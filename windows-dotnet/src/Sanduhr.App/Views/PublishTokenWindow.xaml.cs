using System.Windows;

namespace Sanduhr.App.Views;

/// <summary>
/// Masked entry for the publish token — ManualKeyWindow's paste pattern with
/// a <see cref="System.Windows.Controls.PasswordBox"/> so the token never
/// renders on screen. The caller persists <see cref="Token"/> through
/// <c>UsagePublishService.SaveToken</c> (Credential Manager); this window
/// holds it only until it closes. An optional preset hint (e.g. where the
/// 626 Labs dashboard mints its key) is appended to the explainer.
/// </summary>
internal partial class PublishTokenWindow : Window
{
    /// <summary>The pasted token once the user clicks Save; empty otherwise.</summary>
    public string Token { get; private set; } = "";

    public PublishTokenWindow(string? presetHint = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(presetHint))
            HintText.Text = presetHint.Trim().TrimEnd('.') + ". " + HintText.Text;
        Loaded += (_, _) => TokenBox.Focus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password?.Trim() ?? "";
        if (token.Length == 0)
        {
            ErrorText.Text = "Paste a token to continue.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Token = token;
        TokenBox.Clear();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        TokenBox.Clear();
        DialogResult = false;
        Close();
    }
}
