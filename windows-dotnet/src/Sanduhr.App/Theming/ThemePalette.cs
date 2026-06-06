using System.Windows;
using System.Windows.Media;

namespace Sanduhr.App.Theming;

/// <summary>
/// A single Sanduhr color theme — the obsidian-like default for this milestone.
/// Ported from <c>themes.py</c>'s "obsidian" entry. The full 5-palette
/// <c>ThemeModel</c> + the runtime switcher are item 8; this type stays
/// <b>injectable</b> (construct a different palette, call <see cref="Apply"/>)
/// so item 8 only has to swap the instance, not rewrite the binding surface.
///
/// All views bind to the registered brushes via
/// <c>{DynamicResource Sanduhr.Brush.*}</c>, so re-applying a new palette at
/// runtime re-tints the whole widget with no XAML changes.
/// </summary>
public sealed class ThemePalette
{
    public required string Name { get; init; }
    public required Color Bg { get; init; }
    public required Color Glass { get; init; }
    public required Color GlassOnMica { get; init; }
    public required Color TitleBg { get; init; }
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextDim { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Accent { get; init; }
    public required Color BarBg { get; init; }
    public required Color FooterBg { get; init; }
    public required Color PaceMarker { get; init; }
    public required Color Sparkline { get; init; }

    /// <summary>Glass fill alpha for the root panel laid over Mica (parity with
    /// the Python chrome alpha 0.92 — mostly solid, faint Mica bleed at edges).</summary>
    public double ChromeAlpha { get; init; } = 0.92;

    /// <summary>Card glass alpha (Python obsidian glass_alpha 0.85).</summary>
    public double CardAlpha { get; init; } = 0.85;

    /// <summary>The shipped obsidian-like default (themes.py "obsidian").</summary>
    public static ThemePalette Obsidian { get; } = new()
    {
        Name = "Obsidian",
        Bg = Hex("#0d0d0d"),
        Glass = Hex("#1c1c1c"),
        GlassOnMica = Hex("#1a1a1c"),
        TitleBg = Hex("#161616"),
        Border = Hex("#333333"),
        Text = Hex("#e8e4dc"),
        TextSecondary = Hex("#b8b4ac"),
        TextDim = Hex("#777777"),
        TextMuted = Hex("#555555"),
        Accent = Hex("#6c63ff"),
        BarBg = Hex("#2a2a2a"),
        FooterBg = Hex("#111111"),
        PaceMarker = Hex("#ff6b6b"),
        Sparkline = Hex("#6c63ff"),
        ChromeAlpha = 0.92,
        CardAlpha = 0.85,
    };

    /// <summary>
    /// The bar fill / percentage color ramp for a utilization, 1:1 with
    /// <c>themes.usage_color</c>: &lt;50 green, &lt;75 yellow, &lt;90 orange, else red.
    /// </summary>
    public static Color UsageColor(int pct)
    {
        if (pct < 50) return Hex("#4ade80");
        if (pct < 75) return Hex("#facc15");
        if (pct < 90) return Hex("#fb923c");
        return Hex("#f87171");
    }

    /// <summary>
    /// Register this palette's brushes/colors into a resource dictionary under
    /// stable <c>Sanduhr.*</c> keys. Idempotent — overwrites existing keys, so
    /// item 8 can re-apply a different palette live. The root glass fill carries
    /// <see cref="ChromeAlpha"/> so Mica bleeds subtly at the panel edges.
    /// </summary>
    public void Apply(ResourceDictionary res)
    {
        Set(res, "Sanduhr.Color.Accent", Accent);
        Set(res, "Sanduhr.Color.PaceMarker", PaceMarker);
        Set(res, "Sanduhr.Color.BarBg", BarBg);
        Set(res, "Sanduhr.Color.Sparkline", Sparkline);

        SetBrush(res, "Sanduhr.Brush.Bg", Bg);
        SetBrush(res, "Sanduhr.Brush.Glass", Glass);
        SetBrush(res, "Sanduhr.Brush.GlassChrome", WithAlpha(GlassOnMica, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.CardGlass", WithAlpha(GlassOnMica, CardAlpha));
        SetBrush(res, "Sanduhr.Brush.TitleBg", WithAlpha(TitleBg, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.FooterBg", WithAlpha(FooterBg, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.Border", WithAlpha(Border, 0.30));
        SetBrush(res, "Sanduhr.Brush.Text", Text);
        SetBrush(res, "Sanduhr.Brush.TextSecondary", TextSecondary);
        SetBrush(res, "Sanduhr.Brush.TextDim", TextDim);
        SetBrush(res, "Sanduhr.Brush.TextMuted", TextMuted);
        SetBrush(res, "Sanduhr.Brush.Accent", Accent);
        SetBrush(res, "Sanduhr.Brush.PaceMarker", PaceMarker);
    }

    private static void Set(ResourceDictionary res, string key, Color c) => res[key] = c;

    private static void SetBrush(ResourceDictionary res, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        res[key] = b;
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
