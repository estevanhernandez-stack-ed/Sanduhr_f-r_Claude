using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// One tier card. Adds two affordances over the pure binding surface: a
/// drag-to-reorder gesture (starts a <see cref="DragDrop"/> carrying this card's
/// <see cref="TierCardViewModel"/>; the host ItemsControl handles the drop) and
/// a "Hide this limit" context action. Both persist through
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

    /// <summary>Rebuild the menu each time it opens so it reflects current state.</summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ContextMenu is not { } menu)
            return;
        menu.Items.Clear();
        if (DataContext is not TierCardViewModel vm
            || Window.GetWindow(this)?.DataContext is not WidgetViewModel host)
        {
            e.Handled = true;   // nothing to offer, so no empty menu
            return;
        }
        string key = vm.TierKey;

        if (host.CanHideUntilReset(key))
            menu.Items.Add(Item(SettingsNames.HideUntilItResets, () => host.HideUntilReset(key)));
        menu.Items.Add(Item(SettingsNames.HideThisLimit,() => host.HideTier(key)));
        menu.Items.Add(host.WarningsOn(key)
            ? Item(SettingsNames.StopWarnings, () => host.SetWarnings(key, false))
            : Item(SettingsNames.WarnAgain, () => host.SetWarnings(key, true)));
        menu.Items.Add(new Separator());

        var hidden = host.HiddenTierKeys;
        if (hidden.Count > 0)
        {
            var sub = new MenuItem { Header = SettingsNames.HiddenLimits };
            foreach (var hiddenKey in hidden)
            {
                var k = hiddenKey;
                sub.Items.Add(Item(TierModel.Label(k), () => host.ShowTier(k)));
            }
            menu.Items.Add(sub);
        }
        menu.Items.Add(Item(SettingsNames.LinkTitle(SettingsPage.Alerts), () => _ = host.OpenSettingsAtAsync(SettingsPage.Alerts)));
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }
}
