using System.Windows;
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>A feature page's line about its Desk piece (Settings v2, slice 3). Its DataContext is a
/// <see cref="ViewModels.DeskPieceStatusViewModel"/>.</summary>
public partial class DeskPieceStatusPanel : UserControl
{
    public DeskPieceStatusPanel()
    {
        InitializeComponent();
        PlaceButton.IsVisibleChanged += PlaceButtonVisibleChanged;
    }

    /// <summary>"Place it on the Desk" collapses when it works. A keyboard user who pressed it would be left with no focus (the
    /// next Tab restarts at the window top), so the focus moves to "Desk settings…", which takes its spot (review I2).</summary>
    private void PlaceButtonVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true || !PlaceButton.IsKeyboardFocused) return;
        DeskLink.Focus();
    }
}
