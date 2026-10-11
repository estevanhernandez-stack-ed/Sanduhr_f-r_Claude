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
    /// switches apply. Waiting is the theme accent (<c>Sanduhr.Brush.Accent</c>). Done is the green the
    /// tier cards use for a healthy bar (<c>ThemePalette.UsageColor(0)</c>), darkened on light themes
    /// (judged from <c>Sanduhr.Brush.Bg</c>) so it stays visible.
    /// </summary>
    public static Color ColorFor(ClaudeCodeEvent e)
    {
        var res = Application.Current?.Resources;
        if (e == ClaudeCodeEvent.Waiting)
            return res?["Sanduhr.Brush.Accent"] is SolidColorBrush a ? a.Color : Color.FromRgb(0x4d, 0xa3, 0xff);
        var green = ThemePalette.UsageColor(0);
        if (res?["Sanduhr.Brush.Bg"] is SolidColorBrush bg && Luma(bg.Color) > 0.6)
            return Color.FromRgb(0x16, 0xa3, 0x4a);
        return green;
    }

    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public GlowOverlayContent() => InitializeComponent();

    /// <summary>Line at full strength; wash fading from ~55% to transparent over the band's height.</summary>
    public void SetColor(Color c)
    {
        var line = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        line.Freeze();
        Line.Fill = line;

        var wash = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
        };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(WashAlpha, c.R, c.G, c.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        wash.Freeze();
        Wash.Fill = wash;
    }
}
