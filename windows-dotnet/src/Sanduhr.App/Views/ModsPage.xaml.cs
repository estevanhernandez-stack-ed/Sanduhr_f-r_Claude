using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Settings, Mods &amp; Widgets (Settings v2 slice 4), in the Mods tab. <see cref="ModsPageViewModel"/> is the DataContext. Each time the
/// page becomes visible it lists what is installed (<see cref="ModsPageViewModel.Activate"/>); the catalog is fetched only when its
/// section opens.
/// </summary>
public partial class ModsPage : UserControl
{
    public ModsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Activate();
            else (DataContext as ModsPageViewModel)?.Deactivate();
        };
        DataContextChanged += (_, _) =>
        {
            if (IsVisible) Activate();
        };
    }

    private void Activate() => _ = (DataContext as ModsPageViewModel)?.Activate();

    /// <summary>
    /// The companion's fix button hides itself once the companion is on the desk; keyboard focus would go with it, so it lands on
    /// Install widget from file… next to the line that now says where the companion is (persona panel round 4). The command has run
    /// by the time this queued step does.
    /// </summary>
    private void CompanionFix_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!CompanionFixButton.IsVisible) InstallFromFileButton.Focus();
        });
}
