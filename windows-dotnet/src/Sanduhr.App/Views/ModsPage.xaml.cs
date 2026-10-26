using System.Windows.Controls;
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
        };
        DataContextChanged += (_, _) =>
        {
            if (IsVisible) Activate();
        };
    }

    private void Activate() => _ = (DataContext as ModsPageViewModel)?.Activate();
}
