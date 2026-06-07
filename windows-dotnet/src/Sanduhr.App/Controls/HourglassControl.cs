using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Controls;

/// <summary>
/// The WPF view for the focus-timer hourglass — the "un-fogging" rebuild of
/// <c>focus.py</c>'s <c>paintEvent</c>. The cellular-automaton MODEL is the pure
/// <see cref="HourglassSimulation"/> (cert-load-bearing, unit-tested); this draws
/// it per the LOCKED spec (docs/spec.md "Focus hourglass — view rebuild"):
///
/// <list type="bullet">
/// <item><b>Vessel:</b> thin-line vector glass — hairline bowtie walls, a subtle
/// top sheen, faint tinted bulbs, a visible neck + falling stream. The vessel
/// always carries the 626 cyan→magenta tint (branded glass).</item>
/// <item><b>Grains, per theme:</b> square pixel grains on retro/terminal themes
/// (phosphor feel, honest to the CA), soft round anti-aliased grains on glass
/// themes.</item>
/// <item><b>Color:</b> sand follows the active theme accent, drawn crisp at full
/// contrast. <b>No alpha-60 backing haze</b> (the old foggy look is gone).</item>
/// <item><b>Floor:</b> grain size scales with the panel but is floored so grains
/// never mush below legibility.</item>
/// </list>
/// </summary>
public sealed class HourglassControl : FrameworkElement
{
    // 626 brand stops — the glass vessel always carries this cyan→magenta tint,
    // independent of the active theme (themed sand inside a branded vessel).
    private static readonly Color BrandCyan = (Color)ColorConverter.ConvertFromString("#17d4fa")!;
    private static readonly Color BrandMagenta = (Color)ColorConverter.ConvertFromString("#f22f89")!;

    /// <summary>The pure CA model to render. Set once by the focus view; the view
    /// calls <see cref="FrameworkElement.InvalidateVisual"/> each physics tick.</summary>
    public HourglassSimulation? Simulation { get; set; }

    /// <summary>Active palette — supplies the accent (sand color) and the
    /// retro-vs-glass grain shape.</summary>
    public ThemePalette Palette { get; set; } = ThemePalette.Obsidian;

    protected override void OnRender(DrawingContext dc)
    {
        var sim = Simulation;
        if (sim is null)
            return;

        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20)
            return;

        // Same cell layout as focus.py: a centered square, 31x31 cells.
        double size = Math.Min(w, h) - 24;
        if (size < 20)
            size = Math.Min(w, h);
        double cell = size / sim.Width;
        double ox = (w - size) / 2;
        double oy = (h - size) / 2;

        int cx = sim.CenterX, cy = sim.CenterY;
        double yTop = oy;
        double yBot = oy + size;
        double yMid = oy + size / 2.0;

        // Waist: the throat is 3 cells wide (x = cx-1 .. cx+1) at the center row.
        double waistLeftX = ox + (cx - 1) * cell;
        double waistRightX = ox + (cx + 2) * cell;
        double leftX = ox;
        double rightX = ox + size;

        DrawVessel(dc, leftX, rightX, waistLeftX, waistRightX, yTop, yMid, yBot, cell);

        bool retro = IsRetro(Palette);
        var sandBrush = new SolidColorBrush(Palette.Accent);
        sandBrush.Freeze();

        // No decorative falling-stream line: the drain reads from the REAL grains.
        // Every grain the CA holds is drawn below (top bulb draining, bottom bulb
        // filling), so the pile in the lower bulb grows on its own as sand crosses
        // the throat. The old fixed vertical "stream" bar was a static artifact and
        // is gone; the only neck cue is the two short hairline ticks in DrawVessel.

        // Grains — crisp, full contrast, no backing haze. Square for retro/terminal
        // themes; soft round for glass themes. Floor the drawn size so it never mushes.
        double gap = retro ? cell * 0.08 : cell * 0.16;
        double drawn = Math.Max(cell - gap, 2.0);
        double radius = drawn / 2.0;

        for (int y = 0; y < sim.Height; y++)
        {
            for (int x = 0; x < sim.Width; x++)
            {
                if (!sim.HasSand(x, y))
                    continue;
                double px = ox + x * cell;
                double py = oy + y * cell;
                if (retro)
                {
                    dc.DrawRectangle(sandBrush, null, new Rect(px, py, drawn, drawn));
                }
                else
                {
                    var center = new Point(px + cell / 2.0, py + cell / 2.0);
                    dc.DrawEllipse(sandBrush, null, center, radius, radius);
                }
            }
        }
    }

    private static void DrawVessel(
        DrawingContext dc,
        double leftX, double rightX, double waistLeftX, double waistRightX,
        double yTop, double yMid, double yBot, double cell)
    {
        // The bowtie silhouette, traced clockwise from the top-left.
        var p1 = new Point(leftX, yTop);
        var p2 = new Point(rightX, yTop);
        var p3 = new Point(waistRightX, yMid);
        var p4 = new Point(rightX, yBot);
        var p5 = new Point(leftX, yBot);
        var p6 = new Point(waistLeftX, yMid);

        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            ctx.BeginFigure(p1, isFilled: true, isClosed: true);
            ctx.LineTo(p2, true, true);
            ctx.LineTo(p3, true, true);
            ctx.LineTo(p4, true, true);
            ctx.LineTo(p5, true, true);
            ctx.LineTo(p6, true, true);
        }
        outline.Freeze();

        // Faint tinted bulbs — a very subtle 626 cyan→magenta wash (NOT the old
        // alpha-60 haze; ~0.08 so the grains stay the loudest thing in the glass).
        var bulbFill = new LinearGradientBrush(
            WithAlpha(BrandCyan, 0.10), WithAlpha(BrandMagenta, 0.07),
            new Point(0, 0), new Point(0, 1));
        bulbFill.Freeze();
        dc.DrawGeometry(bulbFill, null, outline);

        // Hairline walls in the branded cyan→magenta tint.
        var wallBrush = new LinearGradientBrush(
            WithAlpha(BrandCyan, 0.65), WithAlpha(BrandMagenta, 0.65),
            new Point(0, 0), new Point(0, 1));
        wallBrush.Freeze();
        var wallPen = new Pen(wallBrush, 1.0) { LineJoin = PenLineJoin.Round };
        wallPen.Freeze();
        dc.DrawGeometry(null, wallPen, outline);

        // Neck tube: two short vertical hairlines at the waist so the throat reads
        // as a pinched neck rather than a bare crossing.
        double neckHalf = cell * 1.2;
        var neckPen = new Pen(wallBrush, 1.0);
        neckPen.Freeze();
        dc.DrawLine(neckPen, new Point(waistLeftX, yMid - neckHalf), new Point(waistLeftX, yMid + neckHalf));
        dc.DrawLine(neckPen, new Point(waistRightX, yMid - neckHalf), new Point(waistRightX, yMid + neckHalf));

        // Top sheen: a short bright highlight catching the top-left edge.
        var sheenPen = new Pen(new SolidColorBrush(WithAlpha(Colors.White, 0.30)), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        sheenPen.Freeze();
        double sheenInset = cell * 2.5;
        dc.DrawLine(sheenPen,
            new Point(leftX + sheenInset, yTop + cell * 0.9),
            new Point(leftX + sheenInset + cell * 5, yTop + cell * 0.9));
    }

    /// <summary>Retro/terminal themes (opt out of Mica or carry a monospace face)
    /// get square pixel grains — phosphor feel, honest to the CA. Mirrors the Mac
    /// build's per-theme grain split.</summary>
    private static bool IsRetro(ThemePalette palette)
        => palette.OptsOutOfMica || !string.IsNullOrEmpty(palette.Definition.MonospaceFont);

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
}
