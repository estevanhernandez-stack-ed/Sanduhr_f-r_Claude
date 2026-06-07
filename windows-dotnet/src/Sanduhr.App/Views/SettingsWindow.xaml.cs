using System.Windows;
using System.Windows.Input;
using Sanduhr.App.Interop;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Tabbed settings window (port of <c>settings_dialog.py</c>'s shell). Hosts the
/// Accounts tab (multi-account management), the Themes tab (swatch grid +
/// agent-assisted theme creation), and a thin General tab. Opened non-modally and
/// kept single-instance by the App so a live account switch from here reflects in
/// the widget immediately.
///
/// Fully themed: a custom <see cref="System.Windows.Shell.WindowChrome"/> title bar
/// (no gray OS chrome — parity with the widget) and every surface bound to the
/// app-level <c>Sanduhr.Brush.*</c> DynamicResources, so the whole window —
/// including the title bar — re-tints live when the active theme changes.
/// </summary>
internal partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        // Modal dialogs spawned by the Accounts tab (rename, confirm) parent to us.
        ViewModel.Accounts.AttachOwner(this);
        // Themes-tab message boxes (save / copy / errors) parent to us too.
        ViewModel.Themes.AttachOwner(this);
    }

    /// <summary>Borderless window: paint the OS frame edge + rounded corners dark so
    /// the custom themed chrome doesn't sit inside a light OS frame.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MicaHelper.ApplyDarkRoundedChrome(this);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
