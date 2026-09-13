using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The Theme Studio's editable state, minus WPF: a name, the fourteen role-named
/// colors as the user typed them, the two dials the studio exposes, and the
/// untouched extras carried from whatever theme the draft started from (so a
/// Save keeps a Matrix-style theme's monospace font even though the studio
/// never shows it). <see cref="ToJson"/> is what Save writes and Copy JSON
/// copies; <see cref="Lint"/> is the live findings list. A color that does
/// not parse stays in the draft (the row shows it as an error) while
/// <see cref="TryGetColor"/> keeps the last good value for the preview.
/// </summary>
public sealed class ThemeDraft
{
    /// <summary>The fourteen color tokens in the order the studio shows them,
    /// each with the one-line job from the agent prompt.</summary>
    public static readonly IReadOnlyList<(string Field, string Label, string Job)> ColorTokens = new[]
    {
        ("bg", "Background", "Window background solid fallback (darkest shade of the palette)"),
        ("glass", "Glass", "Solid-fallback card background"),
        ("glass_on_mica", "Glass on Mica", "Card background tuned for blending over Mica (usually near bg, a touch darker)"),
        ("title_bg", "Title bar", "Title bar solid fallback"),
        ("footer_bg", "Footer", "Footer solid fallback"),
        ("border", "Border", "Neutral card border"),
        ("bar_bg", "Bar (empty)", "The unfilled part of each progress bar"),
        ("text", "Text", "Primary text: percentages, title. High contrast on the card"),
        ("text_secondary", "Text secondary", "Tier labels; same hue as text, a step darker"),
        ("text_dim", "Text dim", "Reset countdown, footer; the ramp's third step"),
        ("text_muted", "Text muted", "Reset date, empty states; the ramp's darkest step"),
        ("accent", "Accent", "The brand color: sparkline, active marker, top stripe"),
        ("sparkline", "Sparkline", "Trend line; usually the accent hue"),
        ("pace_marker", "Pace marker", "The tick on each bar; must stand out on every fill"),
    };

    private readonly Dictionary<string, string> _colors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double R, double G, double B)> _lastGood = new(StringComparer.Ordinal);
    private readonly JsonObject _extras = new();

    public string Name { get; set; } = "";
    public double GlassAlpha { get; set; } = 0.80;
    public double BorderAlpha { get; set; } = 0.40;

    /// <summary>The catalog key the draft was loaded from, for the studio's "Start
    /// from" selection and the default Save name; null for a blank draft.</summary>
    public string? SourceKey { get; private set; }

    public ThemeDraft()
    {
        foreach (var (field, _, _) in ColorTokens)
            _colors[field] = "";
    }

    /// <summary>A draft with every value of an existing theme, extras included.</summary>
    public static ThemeDraft FromDefinition(string key, ThemeDefinition def)
    {
        var d = new ThemeDraft { SourceKey = key, Name = def.Name, GlassAlpha = def.GlassAlpha, BorderAlpha = def.BorderAlpha };
        d.SetColor("bg", def.Bg);
        d.SetColor("glass", def.Glass);
        d.SetColor("glass_on_mica", def.GlassOnMica);
        d.SetColor("title_bg", def.TitleBg);
        d.SetColor("footer_bg", def.FooterBg);
        d.SetColor("border", def.Border);
        d.SetColor("bar_bg", def.BarBg);
        d.SetColor("text", def.Text);
        d.SetColor("text_secondary", def.TextSecondary);
        d.SetColor("text_dim", def.TextDim);
        d.SetColor("text_muted", def.TextMuted);
        d.SetColor("accent", def.Accent);
        d.SetColor("sparkline", def.Sparkline);
        d.SetColor("pace_marker", def.PaceMarker);

        // Extras the studio does not edit but Save must not drop.
        if (def.BorderTint is not null) d._extras["border_tint"] = def.BorderTint;
        d._extras["accent_bloom"] = new JsonObject { ["blur"] = def.AccentBloom.Blur, ["alpha"] = def.AccentBloom.Alpha };
        if (def.InnerHighlight is { } ih) d._extras["inner_highlight"] = new JsonObject { ["color"] = ih.Color, ["alpha"] = ih.Alpha };
        if (def.OptsOutOfMica) d._extras["opts_out_of_mica"] = true;
        if (def.MonospaceFont is not null) d._extras["monospace_font"] = def.MonospaceFont;
        if (def.MonospaceFallback is not null) d._extras["monospace_fallback"] = def.MonospaceFallback;
        if (def.CardCornerRadius is { } r) d._extras["card_corner_radius"] = r;
        if (def.BreathPeriodMs is { } b) d._extras["breath_period_ms"] = b;
        if (def.BgGrid) d._extras["bg_grid"] = true;
        return d;
    }

    public string GetColor(string field) => _colors.TryGetValue(field, out var v) ? v : "";

    /// <summary>Store what the user typed, valid or not. Returns true when it
    /// parses; the parsed value becomes the row's last good color.</summary>
    public bool SetColor(string field, string hex)
    {
        if (!_colors.ContainsKey(field))
            throw new ArgumentException($"Unknown color token '{field}'.", nameof(field));
        hex = hex.Trim();
        _colors[field] = hex;
        if (ThemeLint.TryParseHex(hex, out var rgb))
        {
            _lastGood[field] = rgb;
            return true;
        }
        return false;
    }

    public bool IsColorValid(string field) => ThemeLint.TryParseHex(GetColor(field), out _);

    /// <summary>The last color that parsed for a row; null while nothing ever has.</summary>
    public (double R, double G, double B)? TryGetColor(string field)
        => _lastGood.TryGetValue(field, out var rgb) ? rgb : null;

    /// <summary>The theme document: name, the fourteen colors as typed, the two
    /// dials, then the carried extras.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["name"] = Name };
        foreach (var (field, _, _) in ColorTokens)
            o[field] = _colors[field];
        o["glass_alpha"] = Math.Round(GlassAlpha, 2);
        o["border_alpha"] = Math.Round(BorderAlpha, 2);
        foreach (var (k, v) in _extras)
            o[k] = v?.DeepClone();
        return o;
    }

    public string ToJsonText() => ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public ThemeLintResult Lint() => ThemeLint.Lint(ToJson());

    /// <summary>The theme the widget can preview right now: the lint's definition
    /// when the draft is valid, otherwise the last good color per row over the
    /// current dials, so a keystroke mid-hex never breaks the live preview.
    /// Null until every row has parsed at least once.</summary>
    public ThemeDefinition? PreviewDefinition()
    {
        var lint = Lint();
        if (lint.Theme is not null)
            return lint.Theme;
        var o = ToJson();
        foreach (var (field, _, _) in ColorTokens)
        {
            if (!_lastGood.TryGetValue(field, out var rgb))
                return null;
            o[field] = ToHex(rgb);
        }
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > ThemeLint.MaxNameLength)
            o["name"] = "Draft";
        return ThemeLint.Lint(o).Theme;
    }

    public static string ToHex((double R, double G, double B) c)
        => $"#{(int)Math.Round(c.R * 255):x2}{(int)Math.Round(c.G * 255):x2}{(int)Math.Round(c.B * 255):x2}";
}
