using System.Diagnostics;
using System.Windows;
using Sanduhr.App.Services;

namespace Sanduhr.App.Modals;

/// <summary>
/// Shown when the Evergreen WebView2 runtime is absent. <see cref="DialogResult"/>:
/// <list type="bullet">
/// <item><c>true</c> — the user installed the runtime and clicked Retry, and the
///   runtime is now detected (caller re-enters the embedded sign-in).</item>
/// <item><c>false</c> — the user chose "Paste a key instead" (caller opens the
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
        // Don't close — switch to a waiting state so the user can install the runtime
        // and click Retry without losing the sign-in flow. Retry re-probes and, when
        // the runtime is detected, closes with DialogResult=true so the caller re-enters
        // the embedded sign-in. "Paste a key instead" stays available as the bail-out.
        InstallButton.Visibility = Visibility.Collapsed;
        LearnMoreButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (SignInCoordinator.IsRuntimeAvailable())
        {
            DialogResult = true; // runtime now present — caller re-enters the embedded flow
            Close();
        }
        else
        {
            StatusText.Text = "Still not detected. Finish the install (it may need a moment), then Retry.";
        }
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
