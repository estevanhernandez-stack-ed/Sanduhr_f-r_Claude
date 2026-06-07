using System.Windows;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Tabbed settings window (port of <c>settings_dialog.py</c>'s shell). Hosts the
/// Accounts tab (multi-account management) + a thin General tab. Opened non-modally
/// and kept single-instance by the App so a live account switch from here reflects
/// in the widget immediately. Styled to the obsidian/glass theme via inline
/// resources matching the widget palette.
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
}
