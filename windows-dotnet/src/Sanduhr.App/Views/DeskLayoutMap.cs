using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Sanduhr.App.Interop;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Settings, Desk: a small map of the primary monitor with the taskbar on its edge and every placed
/// piece outlined at its anchor, in stacking order and at its size, inside the margins (the Mac's
/// <c>DeskLayoutMap.swift</c>). Laid out with the same Core calls the Desk uses
/// (<see cref="DeskArrangement.Stacks"/>, <see cref="DeskGeometry"/>, <see cref="TaskbarClearance"/>),
/// scaled to a map <see cref="MapHeight"/> DIPs tall. It reads the <see cref="DeskSettingsViewModel"/>
/// from its DataContext and redraws when the layout, margins or settings change, and draws the working
/// layout of an Arrange session live while one runs (<see cref="DeskSettingsViewModel.ArrangingLayout"/>). Strokes and text use
/// the theme's brushes by resource reference, so a theme change recolors it.
/// </summary>
internal sealed class DeskLayoutMap : Canvas
{
    /// <summary>The map's height; its width follows the screen's shape.</summary>
    public const double MapHeight = 132;
    /// <summary>A piece's label size on the map at size 1.</summary>
    public const double LabelSize = 9;

    public const string AccessibleName =
        "A map of the screen with each Desk piece at its place, in its order and at its size, and the taskbar on its edge.";

    private DeskSettingsViewModel? _vm;

    public DeskLayoutMap()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        AutomationProperties.SetName(this, AccessibleName);   // streamer: notname - the map's fixed accessible name
        DataContextChanged += (_, _) => Attach(DataContext as DeskSettingsViewModel);
        // A tab switch unloads the page and Unloaded detaches; coming back re-attaches (PR #209 review, 7).
        Loaded += (_, _) => { Attach(DataContext as DeskSettingsViewModel); Redraw(); };
        Unloaded += (_, _) => Attach(null);
    }

    /// <summary>The primary monitor as the map draws it: size in DIPs, and the taskbar's reach.</summary>
    internal sealed record Screen(double Width, double Height, DeskInsets Taskbar, TaskbarEdge Edge);

    /// <summary>1920x1080 at 100% with a 48 px taskbar along the bottom, for a machine that cannot say.</summary>
    internal static Screen Fallback { get; } =
        new(1920, 1080, new DeskInsets(0, 0, 0, 48), TaskbarEdge.Bottom);

    /// <summary>The primary monitor now, or <see cref="Fallback"/> when Windows will not say.</summary>
    internal static Screen ReadPrimary()
    {
        try
        {
            var monitors = DeskNative.Monitors();
            var m = monitors.FirstOrDefault(x => x.Primary) ?? monitors.FirstOrDefault();
            if (m is null || m.Bounds.IsEmpty) return Fallback;
            IReadOnlyList<PxRect> hidden = [];
            if (DeskNative.TaskbarAutoHides()) hidden = DeskNative.TaskbarRects();
            var insets = TaskbarClearance.Insets(m.Bounds, m.Work, hidden, m.Scale);
            return new Screen(m.Bounds.Width / m.Scale, m.Bounds.Height / m.Scale, insets, EdgeOf(insets));
        }
        catch (Exception)
        {
            return Fallback;
        }
    }

    private static TaskbarEdge EdgeOf(DeskInsets i)
    {
        var edge = TaskbarEdge.Bottom;
        double best = i.Bottom;
        if (i.Top > best) { best = i.Top; edge = TaskbarEdge.Top; }
        if (i.Left > best) { best = i.Left; edge = TaskbarEdge.Left; }
        if (i.Right > best) { edge = TaskbarEdge.Right; }
        return edge;
    }

    /// <summary>The name a widget key goes by on the map (the names table's).</summary>
    internal static string NameOf(string widget) => SettingsNames.DeskPieceName(widget);

    private void Attach(DeskSettingsViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmChanged;
        }
        Redraw();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // One redraw per saved change: Settings carries the layout, sizes and margins.
        if (e.PropertyName is nameof(DeskSettingsViewModel.Settings) or nameof(DeskSettingsViewModel.ArrangingLayout))
            Redraw();
    }

    private void Ref(FrameworkElement el, DependencyProperty dp, string key) => el.SetResourceReference(dp, key);

    private void Redraw()
    {
        Children.Clear();
        var settings = _vm?.Settings ?? DeskSettings.Default;
        var screen = ReadPrimary();
        double scale = MapHeight / Math.Max(1, screen.Height);
        double width = Math.Round(screen.Width * scale);
        Width = width;
        Height = MapHeight;

        // The screen: a faint panel with the theme's border.
        var panel = new Border { Width = width, Height = MapHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Opacity = 1 };
        Ref(panel, Border.BackgroundProperty, "Sanduhr.Brush.Bg");
        Ref(panel, Border.BorderBrushProperty, "Sanduhr.Brush.TextDim");
        Children.Add(panel);

        // The taskbar band on its edge.
        double thick = Math.Max(3, (screen.Edge switch
        {
            TaskbarEdge.Top => screen.Taskbar.Top,
            TaskbarEdge.Left => screen.Taskbar.Left,
            TaskbarEdge.Right => screen.Taskbar.Right,
            _ => screen.Taskbar.Bottom,
        }) * scale);
        var band = new Rectangle { RadiusX = 2, RadiusY = 2, Opacity = 0.6 };
        Ref(band, Shape.FillProperty, "Sanduhr.Brush.TextDim");
        switch (screen.Edge)
        {
            case TaskbarEdge.Top:
                band.Width = width - 2; band.Height = thick; SetLeft(band, 1); SetTop(band, 1);
                break;
            case TaskbarEdge.Left:
                band.Width = thick; band.Height = MapHeight - 2; SetLeft(band, 1); SetTop(band, 1);
                break;
            case TaskbarEdge.Right:
                band.Width = thick; band.Height = MapHeight - 2; SetLeft(band, width - thick - 1); SetTop(band, 1);
                break;
            default:
                band.Width = width - 2; band.Height = thick; SetLeft(band, 1); SetTop(band, MapHeight - thick - 1);
                break;
        }
        Children.Add(band);

        // The content rectangle, as the Desk computes it, then scaled to the map.
        double inset = settings.TimeSize * 0.18;
        var m = settings.Margins;
        var margins = DeskGeometry.Margins(m.Left, m.Right, m.Top, m.Bottom, screen.Taskbar, inset);
        var c = DeskGeometry.Content(screen.Width, screen.Height, margins, inset);
        var content = new DeskRect(c.X * scale, c.Y * scale, c.Width * scale, c.Height * scale);

        // The map is the main monitor: the pieces on every monitor and on the main one (a piece on a monitor that is gone falls back to it too).
        var monitors = _vm?.Monitors ?? [];
        var main = new DeskScreenRef(monitors.FirstOrDefault(m => m.Primary)?.Id, true);
        var stacks = new DeskArrangement(_vm?.ArrangingLayout ?? _vm?.Layout ?? settings.Layout)
            .ForScreen(main, [.. monitors.Select(m => m.Id)])
            .Stacks(Sanduhr.App.Desk.DeskView.Drawn);
        foreach (var anchor in DeskAnchors.All)
        {
            if (!stacks.TryGetValue(anchor, out var pieces) || pieces.Count == 0) continue;
            var labels = pieces.Select(Label).ToList();
            foreach (var l in labels) l.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            const double gap = 3;
            double stackHeight = labels.Sum(l => l.DesiredSize.Height) + gap * (labels.Count - 1);
            var (px, py) = DeskGeometry.Point(anchor, content);
            double top = DeskAnchors.Row(anchor) switch
            {
                DeskRow.Top => py,
                DeskRow.Middle => py - stackHeight / 2,
                _ => py - stackHeight,
            };
            foreach (var l in labels)
            {
                double w = l.DesiredSize.Width;
                double left = DeskAnchors.Column(anchor) switch
                {
                    DeskColumn.Left => px,
                    DeskColumn.Center => px - w / 2,
                    _ => px - w,
                };
                // Keep the label on the map even when the margins are large.
                SetLeft(l, Math.Clamp(left, 1, Math.Max(1, width - w - 1)));
                SetTop(l, Math.Clamp(top, 1, Math.Max(1, MapHeight - l.DesiredSize.Height - 1)));
                Children.Add(l);
                top += l.DesiredSize.Height + gap;
            }
        }
        InvalidateMeasure();
    }

    private Border Label(DeskPlacement p)
    {
        double s = p.Scale;
        var text = new TextBlock
        {
            Text = NameOf(p.Widget),
            FontSize = LabelSize * s,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.NoWrap,
        };
        Ref(text, TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        var box = new Border
        {
            Child = text,
            Padding = new Thickness(5 * s, 2 * s, 5 * s, 2 * s),
            BorderThickness = new Thickness(0.75),
            CornerRadius = new CornerRadius(3),
        };
        Ref(box, Border.BorderBrushProperty, "Sanduhr.Brush.Accent");
        return box;
    }
}
