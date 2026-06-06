using System.Diagnostics;
using System.Windows;

namespace Sanduhr.App.Modals;

/// <summary>
/// Shown when the Evergreen WebView2 runtime is absent — lifted 1:1 from
/// ROROROblox. <see cref="DialogResult"/> conventions:
/// <list type="bullet">
/// <item><c>true</c> — user clicked Install Now (sent to the installer).</item>
/// <item><c>false</c> — user chose "Paste a key instead" (caller opens the
///   manual-paste fallback).</item>
/// <item><c>null</c> — closed / Learn More only (caller does nothing further).</item>
/// </list>
/// </summary>
internal partial class WebView2NotInstalledWindow : Window
{
    private const string EvergreenInstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const string LearnMoreUrl = "https://learn.microsoft.com/en-us/microsoft-edge/webview2/";

    public WebView2NotInstalledWindow()
    {
        InitializeComponent();
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(EvergreenInstallerUrl);
        DialogResult = true;
        Close();
    }

    private void OnPasteKeyClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnLearnMoreClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(LearnMoreUrl);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // If the OS can't open URLs at all, the user has bigger problems than our modal.
        }
    }
}
