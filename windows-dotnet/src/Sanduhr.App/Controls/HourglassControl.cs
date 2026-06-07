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
/// top sheen, faint tinted bulbs, a visible neck. The vessel always carries the
/// 626 cyan→magenta tint (branded glass). The walls are derived from the SAME
/// cell metrics the grains use, so the glass always sits just OUTSIDE the
/// outermost grain — sand never crosses the wall (see <see cref="OnRender"/>).</item>
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

    // --- Containment geometry (why the walls flare) -------------------------
    // The CA mask is the bowtie dx <= dy + 1. The outermost FILLED cell in a row
    // therefore sits ONE cell beyond a pure dx<=dy cone, and the grain field's
    // outer edge is a slope-(-1) staircase whose outermost corners lie on the
    // line (xCell + yCell) = Width + 2 on the right (mirror on the left). A
    // straight corner→throat chord (the OLD walls) is shallower than that
    // staircase, so the edge grains crossed it near the top — and would cross
    // again at the bottom once the lower bulb stacked up. Giving the walls the
    // staircase's own slope (-1) plus a small gap fixes both extremes, but it
    // flares the top/bottom corners ~2 cells past the grain field. We reserve
    // that headroom in the size budget so the flared vessel still fits the panel.
    private const double OuterCells = 2.0;   // staircase reach past the grid edge
    private const double GapCells = 0.5;     // breathing room between wall and sand

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

        int n = sim.Width;                 // 31
        int cx = sim.CenterX;              // 15

        // Budget the drawable square, then size the cell so the FLARED vessel
        // (grain field + the outer flare + gap on each side) fits inside it.
        // acrossCells = n + 2*(OuterCells + GapCells) = 31 + 5 = 36.
        double budget = Math.Min(w, h) - 24;
        if (budget < 20)
            budget = Math.Min(w, h);
        double acrossCells = n + 2.0 * (OuterCells + GapCells);
        double cell = budget / acrossCells;

        // The grain field is the centered n×n grid; the vessel flares around it.
        double gridPx = n * cell;
        double ox = (w - gridPx) / 2.0;    // grain origin (cell 0,0 top-left)
        double oy = (h - gridPx) / 2.0;

        double mid = n / 2.0;              // 15.5 — the throat row (in cells)

        // Vessel extents (pixels). Diagonals run on the grain envelope + gap, so
        // every edge grain clears them; corners flare OuterCells+GapCells past the
        // grid, top/bottom edges sit GapCells above/below the grain field.
        double left = ox - (OuterCells + GapCells) * cell;
        double right = ox + (n + OuterCells + GapCells) * cell;
        double top = oy - GapCells * cell;
        double bot = oy + (n + GapCells) * cell;
        double midY = oy + mid * cell;
        double waistL = ox + (mid - OuterCells - GapCells) * cell;
        double waistR = ox + (mid + OuterCells + GapCells) * cell;

        // Neck hairlines stay at the REAL throat (3 cells: x = cx-1 .. cx+2),
        // reading as the glass tube nested inside the bowtie pinch.
        double neckL = ox + (cx - 1) * cell;
        double neckR = ox + (cx + 2) * cell;

        var vessel = BuildBowtie(left, right, waistL, waistR, top, bot, midY);
        DrawVessel(dc, vessel, neckL, neckR, midY, left, top, cell);

        bool retro = IsRetro(Palette);
        var sandBrush = new SolidColorBrush(Palette.Accent);
        sandBrush.Freeze();

        // No decorative falling-stream line: the drain reads from the REAL grains.
        // Every grain the CA holds is drawn below (top bulb draining, bottom bulb
        // filling), so the pile in the lower bulb grows on its own as sand crosses
        // the throat. The only neck cue is the two short hairline ticks above.

        // Grains — crisp, full contrast, no backing haze. Square for retro/terminal
        // themes; soft round for glass themes. Floor the drawn size so it never mushes.
        double gap = retro ? cell * 0.08 : cell * 0.16;
        double drawn = Math.Max(cell - gap, 2.0);
        double radius = drawn / 2.0;

        // Belt-and-suspenders: clip to the vessel interior so a grain can NEVER
        // paint past the glass, even when the cell-size floor kicks in on a tiny
        // panel. The geometry already keeps edge grains a full GapCells inside the
        // wall, so this clips nothing in normal sizes — no beads sliced mid-grain.
        dc.PushClip(vessel);
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
        dc.Pop();
    }

    /// <summary>The bowtie silhouette, traced clockwise from the top-left. The
    /// diagonals carry slope (-1) so they parallel the grain staircase; the waist
    /// points sit just outside the throat so the neck tube nests inside.</summary>
    private static Geometry BuildBowtie(
        double left, double right, double waistL, double waistR,
        double top, double bot, double midY)
    {
        var p1 = new Point(left, top);
        var p2 = new Point(right, top);
        var p3 = new Point(waistR, midY);
        var p4 = new Point(right, bot);
        var p5 = new Point(left, bot);
        var p6 = new Point(waistL, midY);

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
        return outline;
    }

    private static void DrawVessel(
        DrawingContext dc, Geometry outline,
        double neckL, double neckR, double midY,
        double left, double top, double cell)
    {
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

        // Neck tube: two short vertical hairlines at the real throat so the
        // pinch reads as a glass tube nested inside the bowtie waist.
        double neckHalf = cell * 1.2;
        var neckPen = new Pen(wallBrush, 1.0);
        neckPen.Freeze();
        dc.DrawLine(neckPen, new Point(neckL, midY - neckHalf), new Point(neckL, midY + neckHalf));
        dc.DrawLine(neckPen, new Point(neckR, midY - neckHalf), new Point(neckR, midY + neckHalf));

        // Top sheen: a short bright highlight catching the top-left edge.
        var sheenPen = new Pen(new SolidColorBrush(WithAlpha(Colors.White, 0.30)), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        sheenPen.Freeze();
        double sheenInset = cell * 2.5;
        dc.DrawLine(sheenPen,
            new Point(left + sheenInset, top + cell * 0.9),
            new Point(left + sheenInset + cell * 5, top + cell * 0.9));
    }

    /// <summary>Retro/terminal themes (opt out of Mica or carry a monospace face)
    /// get square pixel grains — phosphor feel, honest to the CA. Mirrors the Mac
    /// build's per-theme grain split.</summary>
    private static bool IsRetro(ThemePalette palette)
        => palette.OptsOutOfMica || !string.IsNullOrEmpty(palette.Definition.MonospaceFont);

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
}
