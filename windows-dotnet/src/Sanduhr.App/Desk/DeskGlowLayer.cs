using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>A Desk piece with its neon glow behind it (5.0 spec 5, section 7): child 0 is the blurred copy, child 1 the piece. It is as
/// big as the piece, carries the piece's tag (so the Desk's clicks find one piece, not two) and its alignment in the stack.</summary>
internal sealed class GlowHost : Grid;

/// <summary>
/// Draws the neon glow. The glow is a copy of the piece's own ink, blurred, under the piece: a rectangle the size of the piece, painted
/// with the piece through a <see cref="VisualBrush"/>, under a <see cref="BlurEffect"/>. So a gradient ink glows as a gradient, and
/// whatever the piece draws (a meter past its alert level, a song's look) glows in its own colors. A <see cref="DropShadowEffect"/>
/// would glow in one color only; that is kept as the cheaper stand-in (<see cref="OneColor"/>).
/// </summary>
internal static class DeskGlowLayer
{
    /// <summary>
    /// Under the desktop icons each island is drawn in software into a bitmap, and the blurred copy costs more there. True draws the
    /// glow in one color under the icons; the Desk above the icons always draws the copy. Set from the measurement in
    /// <c>DeskGlowCostTests</c> on the owner's PC against the spec's 8 ms budget. Measured 2026-11-20 on the owner's PC at scale 1.00 (middle of three runs): no glow 1.95 ms, blurred copy 8.54 ms, one color 6.34 ms.
    /// </summary>
    public static readonly bool UnderIconsOneColor = true;

    private static bool _failed;

    /// <summary>The element to put where <paramref name="piece"/> was: the piece itself when the ink does not glow, the piece with a
    /// one-color glow as its effect, or the piece wrapped with its blurred copy. A failure to build the glow draws the piece without
    /// it and is written to the debug log once: a glow is never worth a missing piece.</summary>
    public static FrameworkElement On(FrameworkElement piece, ResolvedInk ink)
    {
        if (ink.Glow <= 0) return piece;
        try
        {
            if (ink.GlowOneColor)
            {
                piece.Effect = OneColor(Middle(ink.Stops), ink.Glow);
                return piece;
            }
            return Wrap(piece, ink.Glow);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Failed(e);
            return piece;
        }
    }

    /// <summary>The piece with its blurred copy under it.</summary>
    public static GlowHost Wrap(FrameworkElement piece, double strength)
    {
        var copy = Copy(piece, strength);   // first: if this throws, the piece keeps its tag and its effect
        var host = new GlowHost { HorizontalAlignment = piece.HorizontalAlignment, Tag = piece.Tag };
        piece.Tag = null;
        piece.Effect = null;
        host.Children.Add(copy);
        host.Children.Add(piece);
        return host;
    }

    /// <summary>
    /// The blurred copy of <paramref name="source"/>, to sit under it in a panel that gives both the same frame (a <see cref="Grid"/>
    /// sized by the source). The brush shows exactly the source's layout box, kept in step as the source changes size (the clock's
    /// minute, a meter's number), so the copy never drifts off the ink.
    /// </summary>
    public static Rectangle Copy(FrameworkElement source, double strength)
    {
        var brush = new VisualBrush(source)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, source.ActualWidth, source.ActualHeight),
        };
        source.SizeChanged += (_, e) => brush.Viewbox = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        return new Rectangle
        {
            Fill = brush,
            Opacity = DeskGlow.Opacity(strength),
            Effect = new BlurEffect { Radius = DeskGlow.Radius(strength), KernelType = KernelType.Gaussian },
            IsHitTestVisible = false,
        };
    }

    /// <summary>The one-color glow: a shadow with no offset in <paramref name="hex"/>, at the strength's radius and opacity.</summary>
    public static DropShadowEffect OneColor(string hex, double strength) => new()
    {
        Color = DeskView.Rgb(hex),
        ShadowDepth = 0,
        BlurRadius = DeskGlow.Radius(strength),
        Opacity = DeskGlow.Opacity(strength),
    };

    /// <summary>The ink's middle stop (the second of two, the second of three), white for an ink with none.</summary>
    public static string Middle(IReadOnlyList<string> stops) => stops.Count == 0 ? InkHex.Fallback : stops[stops.Count / 2];

    /// <summary>Records a glow that could not be built, once per run. The type only, never what the piece shows.</summary>
    public static void Failed(Exception e)
    {
        if (_failed) return;
        _failed = true;
        System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk glow failed: {e.GetType().Name}");
    }
}
