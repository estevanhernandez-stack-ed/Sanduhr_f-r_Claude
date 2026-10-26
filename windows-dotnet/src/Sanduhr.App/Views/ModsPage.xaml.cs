using System.Windows.Controls;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Settings, Mods &amp; Widgets: the one list (spec, Mods &amp; Widgets). Until slice 4's page flip it is hosted in the Widgets tab.
/// <see cref="ModsPageViewModel"/> is the DataContext. Each time the page becomes visible it asks the view model to
/// <see cref="ModsPageViewModel.Activate"/>, which is the only time the catalog is fetched without a click.
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
