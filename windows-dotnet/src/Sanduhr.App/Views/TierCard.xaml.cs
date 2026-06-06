using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// One tier card. Adds two affordances over the pure binding surface: a
/// drag-to-reorder gesture (starts a <see cref="DragDrop"/> carrying this card's
/// <see cref="TierCardViewModel"/>; the host ItemsControl handles the drop) and
/// a "Hide this tier" context action. Both persist through
/// <see cref="WidgetViewModel"/> (parity with the Python Settings → Cards
/// hide/reorder, which wrote tier_order / hidden_tiers to settings.json).
/// </summary>
public partial class TierCard : UserControl
{
    private Point _dragStart;
    private bool _maybeDrag;

    public TierCard()
    {
        InitializeComponent();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _maybeDrag = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_maybeDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(p.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _maybeDrag = false;
                if (DataContext is TierCardViewModel vm)
                    DragDrop.DoDragDrop(this, vm, DragDropEffects.Move);
            }
        }
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _maybeDrag = false;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TierCardViewModel vm
            && Window.GetWindow(this)?.DataContext is WidgetViewModel host)
        {
            host.HideTier(vm.TierKey);
        }
    }
}
