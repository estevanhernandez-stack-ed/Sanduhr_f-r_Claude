using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>Settings, Claude Code (Settings v2 slice 4): one card per folder, and the Claude Code glow. DataContext: the ClaudeCodeViewModel.</summary>
public partial class ClaudeCodePage : UserControl
{
    private ClaudeCodeViewModel? _vm;

    public ClaudeCodePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.HoldsLanded -= OnHoldsLanded;
            _vm = DataContext as ClaudeCodeViewModel;
            if (_vm is not null) _vm.HoldsLanded += OnHoldsLanded;
        };
    }

    /// <summary>
    /// A link to folder-config opened a card's report: after the window has brought the cards list into view (and put focus on its first
    /// control), bring that card in and focus its report's header, so the keyboard and a screen reader land in the card whose report opened.
    /// </summary>
    private void OnHoldsLanded(CcFolderRowViewModel row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (Cards.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement card) return;
            card.UpdateLayout();
            // A card with its report open is taller than the window: ask for its top, so the card's name lands in view.
            card.BringIntoView(new Rect(0, 0, card.ActualWidth, Math.Min(card.ActualHeight, 48)));
            if (TryFindResource("Sanduhr.Brush.Accent") is Brush accent)
                AnchorHighlightAdorner.Flash(card, accent, AnchorHighlightAdorner.Duration);
            if (FindHeader(card) is { IsVisible: true } header)
                header.Focus();
        }));

    /// <summary>The card's "What this folder holds" toggle: the Expander's HeaderSite part.</summary>
    private static ToggleButton? FindHeader(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Expander expander)
            {
                expander.ApplyTemplate();
                return expander.Template.FindName("HeaderSite", expander) as ToggleButton;
            }
            if (FindHeader(child) is { } found) return found;
        }
        return null;
    }
}
