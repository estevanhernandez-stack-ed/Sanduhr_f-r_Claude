using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>Settings, Widget (Settings v2, slice 5).</summary>
public partial class WidgetPage : UserControl
{
    public WidgetPage() => InitializeComponent();

    // Limit order: a Move re-parents the moved row's container, and a button that reaches an end collapses while it holds the
    // keyboard. Either drops focus to the top of the window, so after the list has updated the keyboard goes back to the moved row (P13).
    private void LimitOrder_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: LimitOrderRowViewModel row } button)
            return;
        bool wasUp = System.Windows.Automation.AutomationProperties.GetName(button) == row.MoveUpName;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FocusMovedRow(row, wasUp)));
    }

    /// <summary>Focuses the moved row's same button, or its other one when that button collapsed (the row reached an end).</summary>
    private void FocusMovedRow(LimitOrderRowViewModel row, bool wasUp)
    {
        LimitOrderList.UpdateLayout();
        if (LimitOrderList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement container)
            return;
        container.UpdateLayout();
        var buttons = Buttons(container).ToList();
        var same = buttons.FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == (wasUp ? row.MoveUpName : row.MoveDownName));
        var other = buttons.FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == (wasUp ? row.MoveDownName : row.MoveUpName));
        if (same is { IsVisible: true })
            same.Focus();
        else if (other is { IsVisible: true })
            other.Focus();
    }

    private static IEnumerable<ButtonBase> Buttons(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase b) yield return b;
            foreach (var deeper in Buttons(child)) yield return deeper;
        }
    }
}
