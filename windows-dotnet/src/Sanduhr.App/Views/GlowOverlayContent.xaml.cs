using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The look of the Claude Code glow, apart from the window that carries it so the render harness can
/// draw it over a fake target. Public only so the harness can reach it.
/// </summary>
public partial class GlowOverlayContent : UserControl
{
    /// <summary>The wash starts at about 55% of the color.</summary>
    public const byte WashAlpha = 0x8C;

    /// <summary>
    /// The glow color for <paramref name="e"/>, read from the live resources at call time so theme
    /// switches apply. Waiting is the theme accent (<c>Sanduhr.Brush.Accent</c>), done the theme's
    /// success color (<c>Sanduhr.Brush.Success</c>, see <see cref="ThemePalette.SuccessFor"/>); without
    /// those resources the healthy-bar green, darkened on a light background.
    /// </summary>
    public static Color ColorFor(ClaudeCodeEvent e)
    {
        var res = Application.Current?.Resources;
        if (e == ClaudeCodeEvent.Waiting)
            return res?["Sanduhr.Brush.Accent"] is SolidColorBrush a ? a.Color : Color.FromRgb(0x4d, 0xa3, 0xff);
        if (res?["Sanduhr.Brush.Success"] is SolidColorBrush s) return s.Color;
        var bg = res?["Sanduhr.Brush.Bg"] is SolidColorBrush b ? b.Color : Colors.Black;
        return ThemePalette.SuccessFor("", bg);
    }

    public GlowOverlayContent() => InitializeComponent();

    private Sanduhr.Core.GlowEdgeSide _side = Sanduhr.Core.GlowEdgeSide.Top;

    /// <summary>Which edge the light comes from (spec 6.8): the line sits on that edge and the wash falls
    /// inward; the ends fade along the edge. Top is S4's look. Call before <see cref="SetColor"/>.</summary>
    public void SetSide(Sanduhr.Core.GlowEdgeSide side)
    {
        _side = side;
        bool vertical = side is Sanduhr.Core.GlowEdgeSide.Left or Sanduhr.Core.GlowEdgeSide.Right;
        Line.Height = vertical ? double.NaN : 2;
        Line.Width = vertical ? 2 : double.NaN;
        Line.VerticalAlignment = side switch
        {
            Sanduhr.Core.GlowEdgeSide.Bottom => System.Windows.VerticalAlignment.Bottom,
            Sanduhr.Core.GlowEdgeSide.Top => System.Windows.VerticalAlignment.Top,
            _ => System.Windows.VerticalAlignment.Stretch,
        };
        Line.HorizontalAlignment = side switch
        {
            Sanduhr.Core.GlowEdgeSide.Left => System.Windows.HorizontalAlignment.Left,
            Sanduhr.Core.GlowEdgeSide.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Stretch,
        };
        var mask = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = vertical ? new System.Windows.Point(0, 1) : new System.Windows.Point(1, 0),
        };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.22));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.78));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        mask.Freeze();
        Root.OpacityMask = mask;
    }

    /// <summary>Line at full strength; wash fading from ~55% to transparent over the band's depth, away
    /// from the edge the light comes from.</summary>
    public void SetColor(Color c)
    {
        var line = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        line.Freeze();
        Line.Fill = line;

        var (from, to) = _side switch
        {
            Sanduhr.Core.GlowEdgeSide.Bottom => (new System.Windows.Point(0, 1), new System.Windows.Point(0, 0)),
            Sanduhr.Core.GlowEdgeSide.Left => (new System.Windows.Point(0, 0), new System.Windows.Point(1, 0)),
            Sanduhr.Core.GlowEdgeSide.Right => (new System.Windows.Point(1, 0), new System.Windows.Point(0, 0)),
            _ => (new System.Windows.Point(0, 0), new System.Windows.Point(0, 1)),
        };
        var wash = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(WashAlpha, c.R, c.G, c.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        wash.Freeze();
        Wash.Fill = wash;
    }
}
