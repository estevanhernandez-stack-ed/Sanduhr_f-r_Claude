using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.Core;

namespace Sanduhr.App.Theming;

/// <summary>
/// The WPF adapter over a Core <see cref="ThemeDefinition"/>: parses the theme's
/// hex colors into <see cref="Color"/>s and projects the full dial set into
/// <c>Sanduhr.*</c> DynamicResources via <see cref="Apply"/>. Every view binds to
/// those resources, so re-applying a different palette at runtime re-tints the
/// whole widget with no XAML changes — the item-8 theme switcher just swaps the
/// instance and calls <see cref="Apply"/>.
///
/// Ported from <c>widget.apply_theme</c>: chrome strips get a denser glass alpha
/// than cards (0.92 vs the theme's <c>glass_alpha</c>); a Mica-opt-out theme
/// (Matrix) forces both to 1.0 so it renders solid.
/// </summary>
public sealed class ThemePalette
{
    /// <summary>The catalog key this palette was built from (lowercase, e.g.
    /// "obsidian"). Persisted to <c>settings.json</c> and used to mark the active
    /// entry in the theme strip.</summary>
    public string Key { get; }

    public ThemeDefinition Definition { get; }

    public string Name => Definition.Name;

    public Color Bg { get; }
    public Color Glass { get; }
    public Color GlassOnMica { get; }
    public Color TitleBg { get; }
    public Color Border { get; }
    public Color Text { get; }
    public Color TextSecondary { get; }
    public Color TextDim { get; }
    public Color TextMuted { get; }
    public Color Accent { get; }
    public Color BarBg { get; }
    public Color FooterBg { get; }
    public Color PaceMarker { get; }
    public Color Sparkline { get; }
    public Color? BorderTint { get; }
    public Color? InnerHighlightColor { get; }
    public double InnerHighlightAlpha { get; }

    /// <summary>Matrix-style solid phosphor terminal: skip Mica, paint solid,
    /// monospace numerics, tight corners.</summary>
    public bool OptsOutOfMica => Definition.OptsOutOfMica;

    /// <summary>Card glass alpha (the theme's <c>glass_alpha</c>); 1.0 when the
    /// theme opts out of Mica so cards render opaque.</summary>
    public double CardAlpha => Definition.GlassAlpha;

    /// <summary>Card border alpha (the theme's <c>border_alpha</c>).</summary>
    public double BorderAlpha => Definition.BorderAlpha;

    /// <summary>Chrome strip glass alpha — denser than cards so light text stays
    /// legible against Mica bleed (widget.apply_theme: 0.92, or 1.0 for opt-out).</summary>
    public double ChromeAlpha => Definition.OptsOutOfMica ? 1.0 : 0.92;

    /// <summary>Card corner radius (the theme's <c>card_corner_radius</c>);
    /// defaults to the obsidian 10 px, Matrix tightens to 2 px.</summary>
    public int CardCornerRadius => Definition.CardCornerRadius ?? 10;

    public ThemePalette(string key, ThemeDefinition def)
    {
        Key = key;
        Definition = def;
        Bg = Hex(def.Bg);
        Glass = Hex(def.Glass);
        GlassOnMica = Hex(def.GlassOnMica);
        TitleBg = Hex(def.TitleBg);
        Border = Hex(def.Border);
        Text = Hex(def.Text);
        TextSecondary = Hex(def.TextSecondary);
        TextDim = Hex(def.TextDim);
        TextMuted = Hex(def.TextMuted);
        Accent = Hex(def.Accent);
        BarBg = Hex(def.BarBg);
        FooterBg = Hex(def.FooterBg);
        PaceMarker = Hex(def.PaceMarker);
        Sparkline = Hex(def.Sparkline);
        BorderTint = def.BorderTint is null ? null : Hex(def.BorderTint);
        InnerHighlightColor = def.InnerHighlight is null ? null : Hex(def.InnerHighlight.Color);
        InnerHighlightAlpha = def.InnerHighlight?.Alpha ?? 0.0;
    }

    /// <summary>The shipped obsidian default (themes.py "obsidian"). Keeps the
    /// item-5 look as the fallback when no saved theme resolves.</summary>
    public static ThemePalette Obsidian { get; } = new("obsidian", ThemeCatalog.BuiltIns["obsidian"]);

    /// <summary>
    /// The bar fill / percentage color ramp for a utilization, 1:1 with
    /// <c>themes.usage_color</c>: &lt;50 green, &lt;75 yellow, &lt;90 orange, else red.
    /// Delegates to Core so the ramp has a single source of truth.
    /// </summary>
    public static Color UsageColor(int pct) => Hex(ThemeCatalog.UsageColor(pct));

    /// <summary>
    /// Register this palette into a resource dictionary under stable
    /// <c>Sanduhr.*</c> keys. Idempotent — overwrites existing keys so a live
    /// theme switch re-tints everything. Applies the full dial set: card alpha
    /// from <c>glass_alpha</c>, border from <c>border_alpha</c>/<c>border_tint</c>,
    /// inner-highlight edge, accent stripe/sparkline, card corner radius, the
    /// value-label font (monospace for terminal themes), and the accent-bloom
    /// glow (terminal themes only — parity with widget.py).
    /// </summary>
    public void Apply(ResourceDictionary res)
    {
        Set(res, "Sanduhr.Color.Accent", Accent);
        Set(res, "Sanduhr.Color.AccentFade", Color.FromArgb(0, Accent.R, Accent.G, Accent.B));
        Set(res, "Sanduhr.Color.PaceMarker", PaceMarker);
        Set(res, "Sanduhr.Color.BarBg", BarBg);
        Set(res, "Sanduhr.Color.Sparkline", Sparkline);

        SetBrush(res, "Sanduhr.Brush.Bg", Bg);
        SetBrush(res, "Sanduhr.Brush.Glass", Glass);
        // A lifted glass for hover / selected surfaces (Settings buttons, list rows,
        // tab hover). Solid so it reads the same whether or not Mica is behind it.
        SetBrush(res, "Sanduhr.Brush.GlassHover", Lighten(Glass, 0.10));
        SetBrush(res, "Sanduhr.Brush.GlassChrome", WithAlpha(GlassOnMica, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.CardGlass", WithAlpha(GlassOnMica, CardAlpha));
        SetBrush(res, "Sanduhr.Brush.TitleBg", WithAlpha(TitleBg, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.FooterBg", WithAlpha(FooterBg, ChromeAlpha));
        SetBrush(res, "Sanduhr.Brush.Border", WithAlpha(BorderTint ?? Border, BorderAlpha));
        SetBrush(res, "Sanduhr.Brush.Text", Text);
        SetBrush(res, "Sanduhr.Brush.TextSecondary", TextSecondary);
        SetBrush(res, "Sanduhr.Brush.TextDim", TextDim);
        SetBrush(res, "Sanduhr.Brush.TextMuted", TextMuted);
        SetBrush(res, "Sanduhr.Brush.Accent", Accent);
        SetBrush(res, "Sanduhr.Brush.PaceMarker", PaceMarker);

        // 1-px top-edge light catch (inner_highlight); Transparent when the theme
        // leaves it null (restrained themes).
        SetBrush(res, "Sanduhr.Brush.InnerHighlight",
            InnerHighlightColor is { } ih ? WithAlpha(ih, InnerHighlightAlpha) : Colors.Transparent);

        // Card corner-radius dial (Matrix tightens to 2 px for the CRT feel).
        res["Sanduhr.CardCornerRadius"] = new CornerRadius(CardCornerRadius);

        // Value-label font: monospace for terminal themes (Matrix → Cascadia Code,
        // falling back to Consolas via WPF's built-in comma fallback), else the
        // widget default. The numeric/tier text is the only surface that switches.
        res["Sanduhr.Font.Value"] = ResolveValueFont();

        // Accent-bloom glow on the value label — applied ONLY for monospace
        // terminal themes (widget._apply_monospace_if_needed gates the bloom drop
        // shadow on monospace_font being set). Non-terminal themes get an inert,
        // invisible effect so the DynamicResource always resolves to an Effect.
        res["Sanduhr.Effect.ValueBloom"] = BuildValueBloom();
    }

    private FontFamily ResolveValueFont()
    {
        if (!string.IsNullOrEmpty(Definition.MonospaceFont))
        {
            var fallback = string.IsNullOrEmpty(Definition.MonospaceFallback)
                ? "Consolas"
                : Definition.MonospaceFallback;
            // WPF resolves the comma list left-to-right, skipping uninstalled
            // families automatically — no manual exactMatch probe needed.
            return new FontFamily($"{Definition.MonospaceFont}, {fallback}");
        }
        return new FontFamily("Segoe UI Variable Display, Segoe UI");
    }

    private Effect BuildValueBloom()
    {
        if (!string.IsNullOrEmpty(Definition.MonospaceFont))
        {
            var glow = new DropShadowEffect
            {
                ShadowDepth = 0,
                Direction = 0,
                BlurRadius = Definition.AccentBloom.Blur,
                Color = Accent,
                Opacity = Definition.AccentBloom.Alpha,
            };
            glow.Freeze();
            return glow;
        }
        var inert = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 0, Opacity = 0 };
        inert.Freeze();
        return inert;
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

    /// <summary>Blend a color toward white by <paramref name="amount"/> (0..1) for a
    /// subtle lifted surface — used for hover / selected states that need to read a
    /// notch brighter than the resting glass in every theme.</summary>
    private static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(c.R + (255 - c.R) * amount),
            (byte)Math.Round(c.G + (255 - c.G) * amount),
            (byte)Math.Round(c.B + (255 - c.B) * amount));
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
