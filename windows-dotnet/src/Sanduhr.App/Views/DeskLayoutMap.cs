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
/// Settings, Desk: a small map of every connected monitor, placed as Windows arranges them (4.0.1; one monitor
/// looks as it always did), each with the taskbar on its edge and the pieces that monitor draws outlined at their
/// anchors, in stacking order and at their size, inside the margins (the Mac's <c>DeskLayoutMap.swift</c>, which
/// draws the main screen only). With more than one monitor, each panel is captioned with the monitor's name. Laid out with the same Core calls the Desk uses
/// (<see cref="DeskArrangement.Stacks"/>, <see cref="DeskGeometry"/>, <see cref="TaskbarClearance"/>),
/// scaled to a map <see cref="MapHeight"/> DIPs tall. It reads the <see cref="DeskSettingsViewModel"/>
/// from its DataContext and redraws when the layout, margins or settings change, and draws the working
/// layout of an Arrange session live while one runs (<see cref="DeskSettingsViewModel.ArrangingLayout"/>). Strokes and text use
/// the theme's brushes by resource reference, so a theme change recolors it.
/// </summary>
internal sealed class DeskLayoutMap : Canvas
{
    /// <summary>The map's height with one monitor; its width follows the screen's shape (<see cref="DeskMapLayout"/>).</summary>
    public const double MapHeight = DeskMapLayout.MaxHeight;
    /// <summary>A piece's label size on the map at size 1.</summary>
    public const double LabelSize = 9;

    public const string AccessibleName =
        "A map of your monitors with each Desk piece at its place, in its order and at its size, and each taskbar on its edge.";

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

    /// <summary>One monitor as the map draws it: its bounds in the virtual desktop (physical pixels), its size and taskbar in DIPs,
    /// the ref the Desk's per-monitor rule reads, and its name for the caption.</summary>
    internal sealed record MapScreen(PxRect Bounds, Screen Screen, DeskScreenRef Ref, string? Name);

    /// <summary>
    /// Every connected monitor in screen order, each matched by its bounds to the named monitor the Desk's pieces are placed on
    /// (<see cref="DeskSettingsViewModel.Monitors"/>), as <see cref="Sanduhr.App.Desk.DeskMonitorSource"/> matches the Desk's screens.
    /// When Windows will not list the monitors, the primary alone (<see cref="ReadPrimary"/>), as the map always drew.
    /// </summary>
    internal static IReadOnlyList<MapScreen> ReadScreens(IReadOnlyList<DeskMonitorInfo> named)
    {
        try
        {
            var monitors = DeskNative.Monitors().Where(m => !m.Bounds.IsEmpty).ToList();
            if (monitors.Count == 0) return [Primary(named)];
            IReadOnlyList<PxRect> hidden = [];
            if (DeskNative.TaskbarAutoHides()) hidden = DeskNative.TaskbarRects();
            return [.. monitors
                .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
                .Select(m =>
                {
                    var info = named.FirstOrDefault(n => n.Left == m.Bounds.X && n.Top == m.Bounds.Y && n.Width == m.Bounds.Width && n.Height == m.Bounds.Height);
                    var insets = TaskbarClearance.Insets(m.Bounds, m.Work, hidden, m.Scale);
                    var screen = new Screen(m.Bounds.Width / m.Scale, m.Bounds.Height / m.Scale, insets, EdgeOf(insets));
                    return new MapScreen(m.Bounds, screen, new DeskScreenRef(info?.Id, m.Primary), info?.Name);
                })];
        }
        catch (Exception)
        {
            return [Primary(named)];
        }
    }

    private static MapScreen Primary(IReadOnlyList<DeskMonitorInfo> named)
    {
        var s = ReadPrimary();
        return new MapScreen(new PxRect(0, 0, (int)s.Width, (int)s.Height), s, new DeskScreenRef(named.FirstOrDefault(m => m.Primary)?.Id, true), null);
    }

    /// <summary>The captions' row under the panels, when there is more than one monitor.</summary>
    private const double CaptionRow = 14;
    /// <summary>The space between two touching monitors' panels.</summary>
    private const double PanelGap = 4;

    private void Redraw()
    {
        Children.Clear();
        var settings = _vm?.Settings ?? DeskSettings.Default;
        var named = _vm?.Monitors ?? [];
        var screens = ReadScreens(named);
        var rects = DeskMapLayout.Fit([.. screens.Select(s => s.Bounds)]);
        bool several = screens.Count > 1;
        double width = Math.Round(rects.Max(r => r.X + r.Width));
        double mapHeight = Math.Round(rects.Max(r => r.Y + r.Height));
        Width = width;
        Height = mapHeight + (several ? CaptionRow : 0);

        // A piece on a monitor that is gone falls back to the main one, as on the Desk.
        var layout = new DeskArrangement(_vm?.ArrangingLayout ?? _vm?.Layout ?? settings.Layout);
        string[] connected = [.. named.Select(m => m.Id)];
        for (int i = 0; i < screens.Count; i++)
        {
            var r = rects[i];
            double inset = several ? PanelGap / 2 : 0;
            var box = several
                ? new DeskRect(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset)
                : new DeskRect(0, 0, width, mapHeight);
            DrawScreen(screens[i].Screen, box, settings, layout.ForScreen(screens[i].Ref, connected));
            if (several && screens[i].Name is { } name)
            {
                var caption = new TextBlock
                {
                    Text = name,   // streamer: notname - a monitor's size and side ("1920×1080, right of main"), never a person's name
                    FontSize = 8,
                    Width = Math.Max(1, box.Width),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                };
                Ref(caption, TextBlock.ForegroundProperty, "Sanduhr.Brush.TextDim");
                SetLeft(caption, box.X);
                SetTop(caption, mapHeight + 2);
                Children.Add(caption);
            }
        }
        InvalidateMeasure();
    }

    /// <summary>One monitor's panel at <paramref name="box"/> (map DIPs): the screen, its taskbar band, and its pieces laid out with
    /// the same Core calls the Desk uses, scaled from the monitor's DIPs to the panel.</summary>
    private void DrawScreen(Screen screen, DeskRect box, DeskSettings settings, DeskArrangement pieces)
    {
        double scale = box.Height / Math.Max(1, screen.Height);
        double width = box.Width, height = box.Height;

        // The screen: a faint panel with the theme's border.
        var panel = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Opacity = 1 };
        Ref(panel, Border.BackgroundProperty, "Sanduhr.Brush.Bg");
        Ref(panel, Border.BorderBrushProperty, "Sanduhr.Brush.TextDim");
        SetLeft(panel, box.X);
        SetTop(panel, box.Y);
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
                band.Width = width - 2; band.Height = thick; SetLeft(band, box.X + 1); SetTop(band, box.Y + 1);
                break;
            case TaskbarEdge.Left:
                band.Width = thick; band.Height = height - 2; SetLeft(band, box.X + 1); SetTop(band, box.Y + 1);
                break;
            case TaskbarEdge.Right:
                band.Width = thick; band.Height = height - 2; SetLeft(band, box.X + width - thick - 1); SetTop(band, box.Y + 1);
                break;
            default:
                band.Width = width - 2; band.Height = thick; SetLeft(band, box.X + 1); SetTop(band, box.Y + height - thick - 1);
                break;
        }
        Children.Add(band);

        // The content rectangle, as the Desk computes it, then scaled to the panel.
        double inset = settings.TimeSize * 0.18;
        var m = settings.Margins;
        var margins = DeskGeometry.Margins(m.Left, m.Right, m.Top, m.Bottom, screen.Taskbar, inset);
        var c = DeskGeometry.Content(screen.Width, screen.Height, margins, inset);
        var content = new DeskRect(c.X * scale, c.Y * scale, c.Width * scale, c.Height * scale);

        var stacks = pieces.Stacks(Sanduhr.App.Desk.DeskView.Drawn);
        foreach (var anchor in DeskAnchors.All)
        {
            if (!stacks.TryGetValue(anchor, out var placed) || placed.Count == 0) continue;
            var labels = placed.Select(Label).ToList();
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
                // Keep the label on its panel even when the margins are large.
                SetLeft(l, box.X + Math.Clamp(left, 1, Math.Max(1, width - w - 1)));
                SetTop(l, box.Y + Math.Clamp(top, 1, Math.Max(1, height - l.DesiredSize.Height - 1)));
                Children.Add(l);
                top += l.DesiredSize.Height + gap;
            }
        }
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
