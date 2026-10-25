using System.Windows.Controls;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk Look (Settings v2, slice 3): fonts, sizes, ink and color, the back plate.</summary>
public partial class DeskLookPage : UserControl
{
    public DeskLookPage()
    {
        InitializeComponent();
        // The message's size, color, glow and font live here: read them fresh each time the page shows (DeskMessagePage does
        // the same for the rest of its settings).
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Refresh();
        };
        DataContextChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    private void Refresh() => (DataContext as SettingsViewModel)?.MessagePage.RefreshSettings();
}
