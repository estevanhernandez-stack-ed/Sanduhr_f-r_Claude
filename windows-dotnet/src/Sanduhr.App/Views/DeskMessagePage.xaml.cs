using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Settings, Message (spec 6.2, 6.9): the Desk message's line editor, rotation, special days, look and
/// Claude's suggestion card. Constructor-free XAML; <see cref="DeskMessagePageViewModel"/> is the
/// DataContext. The page asks the view model to <see cref="DeskMessagePageViewModel.Activate"/> each time
/// it becomes visible, so the host only has to set the DataContext (it may call Activate itself too).
/// </summary>
public partial class DeskMessagePage : UserControl
{
    public DeskMessagePage()
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

    private void Activate() => (DataContext as DeskMessagePageViewModel)?.Activate();

    /// <summary>Add line puts a new row in with its controls open: its text box takes focus and scrolls into view.</summary>
    private void LineText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not MessageRowViewModel { IsNew: true } row) return;
        row.IsNew = false;
        box.Focus();
        box.BringIntoView();
    }
}
