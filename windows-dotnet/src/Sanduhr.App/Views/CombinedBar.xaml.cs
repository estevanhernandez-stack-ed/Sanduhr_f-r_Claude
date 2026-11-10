using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The combined bar for one window (agent layer): draws the view model's layout as rectangles sized by the canvas width. The
/// segments and ticks carry their own names for a screen reader; the control's own name is the whole sentence.
/// </summary>
public partial class CombinedBar : UserControl
{
    private const double TickWidth = 2;

    public CombinedBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Redraw();
        Draw.SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        Draw.Children.Clear();
        double w = Draw.ActualWidth, h = Draw.ActualHeight;
        if (DataContext is not CombinedBarViewModel bar || w <= 0 || h <= 0)
            return;
        Draw.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 3, 3);

        if (bar.Layout.Primary is { } primary)
            Draw.Children.Add(Segment(primary, bar.PrimaryBrush, w, h));
        if (bar.Layout.Beyond is { } beyond && bar.BeyondBrush is { } beyondBrush)
            Draw.Children.Add(Segment(beyond, beyondBrush, w, h));
        foreach (var tick in bar.Ticks)
        {
            var r = new Rectangle { Width = TickWidth, Height = h, Fill = tick.Brush, ToolTip = tick.Tooltip };
            AutomationProperties.SetName(r, tick.Tooltip);   // streamer: notname - a source's name and a percent, no account or project
            Canvas.SetLeft(r, Math.Clamp(tick.At * w - TickWidth / 2, 0, Math.Max(0, w - TickWidth)));
            Draw.Children.Add(r);
        }
    }

    private static Rectangle Segment(BarSegment seg, Brush brush, double w, double h)
    {
        var r = new Rectangle { Width = Math.Max(0, (seg.To - seg.From) * w), Height = h, Fill = brush };
        string name = $"{AgentSources.Name(seg.Source)} {Math.Round(seg.To * 100).ToString(CultureInfo.InvariantCulture)}%{(seg.Stale ? ", as of earlier" : "")}";
        r.ToolTip = name;
        AutomationProperties.SetName(r, name);   // streamer: notname - a source's name and a percent, no account or project
        Canvas.SetLeft(r, seg.From * w);
        return r;
    }
}
