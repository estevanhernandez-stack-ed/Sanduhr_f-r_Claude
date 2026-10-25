using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sanduhr.App.Views;

/// <summary>
/// Marks a section of a Settings page with its anchor (<see cref="Sanduhr.Core.SettingsIndex"/>), so
/// search and deep links can scroll to it: <c>views:SettingsAnchor.Id="tray"</c>. Ids are page-local.
/// </summary>
public static class SettingsAnchor
{
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(SettingsAnchor), new PropertyMetadata(null));

    public static string? GetId(DependencyObject d) => (string?)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string? value) => d.SetValue(IdProperty, value);

    /// <summary>The first element under <paramref name="root"/> marked <paramref name="id"/>, depth first.</summary>
    public static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement fe && GetId(fe) == id)
            return fe;
        // Logical tree: pages that were never shown have no visual tree yet.
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject d && Find(d, id) is { } found)
                return found;
        }
        return null;
    }
}

/// <summary>
/// The sidebar's group heading, drawn above the first page of each group:
/// set by <c>SettingsWindow</c> from <c>SettingsNames.Sidebar</c> on that page's TabItem.
/// </summary>
public static class SettingsSidebar
{
    public static readonly DependencyProperty GroupHeaderProperty = DependencyProperty.RegisterAttached(
        "GroupHeader", typeof(string), typeof(SettingsSidebar), new PropertyMetadata(null));

    public static string? GetGroupHeader(DependencyObject d) => (string?)d.GetValue(GroupHeaderProperty);
    public static void SetGroupHeader(DependencyObject d, string? value) => d.SetValue(GroupHeaderProperty, value);
}

/// <summary>
/// A link to a Settings place on a button anywhere in the Settings window, page controls in their own files included:
/// <c>views:SettingsLinkTo.Link="desk#pieces"</c>. The window opens it when the button is clicked (<c>SettingsWindow</c>
/// listens for every Click), reading the text with <c>SettingsIndex.ParseLink</c>, so aliases and unknown anchors land as any
/// link does.
/// </summary>
public static class SettingsLinkTo
{
    public static readonly DependencyProperty LinkProperty = DependencyProperty.RegisterAttached(
        "Link", typeof(string), typeof(SettingsLinkTo), new PropertyMetadata(null));

    public static string? GetLink(DependencyObject d) => (string?)d.GetValue(LinkProperty);
    public static void SetLink(DependencyObject d, string? value) => d.SetValue(LinkProperty, value);
}

/// <summary>A rounded accent outline around a section search or a link just opened, for 1.5 s.</summary>
internal sealed class AnchorHighlightAdorner : Adorner
{
    private readonly Pen _pen;

    private AnchorHighlightAdorner(UIElement adorned, Brush brush) : base(adorned)
    {
        IsHitTestVisible = false;
        _pen = new Pen(brush, 2);
        _pen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize;
        dc.DrawRoundedRectangle(null, _pen, new Rect(-4, -3, size.Width + 8, size.Height + 6), 6, 6);
    }

    /// <summary>Outline <paramref name="element"/> for <paramref name="duration"/>; does nothing when it has no adorner layer.</summary>
    public static void Flash(FrameworkElement element, Brush brush, TimeSpan duration)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
            return;
        var adorner = new AnchorHighlightAdorner(element, brush);
        layer.Add(adorner);
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            layer.Remove(adorner);
        };
        timer.Start();
    }
}
