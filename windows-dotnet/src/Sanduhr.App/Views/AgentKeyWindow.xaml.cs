using System.Windows;

namespace Sanduhr.App.Views;

/// <summary>
/// Masked entry for the 626 Labs agent API key — ManualKeyWindow's paste
/// pattern with a <see cref="System.Windows.Controls.PasswordBox"/> so the key
/// never renders on screen. The caller persists <see cref="Key"/> through
/// <c>UsagePublishService.SaveKey</c> (Credential Manager); this window holds
/// it only until it closes.
/// </summary>
internal partial class AgentKeyWindow : Window
{
    /// <summary>The pasted key once the user clicks Save; empty otherwise.</summary>
    public string Key { get; private set; } = "";

    public AgentKeyWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => KeyBox.Focus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password?.Trim() ?? "";
        if (key.Length == 0)
        {
            ErrorText.Text = "Paste an agent key to continue.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Key = key;
        KeyBox.Clear();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        KeyBox.Clear();
        DialogResult = false;
        Close();
    }
}
