using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 8 (WF37; spec, Keyboard and screen reader, Contrast; Done line): TextDim and TextMuted read at 4.5:1 or better on
/// the Settings page and sidebar in all six built-in themes, computed from the theme tokens. TextDim's floor is a step higher so the
/// ramp keeps its steps (owner taste 1). Every built-in theme is dark: none has a light variant to measure (decision 9).
/// Measured and accepted by the owner, not asserted: on GlassHover (sidebar hover, the History account list's selected rows) TextDim
/// reads 3.70 to 3.86 and TextMuted 3.31 to 3.47 (preflight P9).
/// </summary>
public class ThemeContrastTests
{
    public static TheoryData<string> BuiltIns()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThemeCatalog.BuiltInOrder) data.Add(key);
        return data;
    }

    [Fact]
    public void Six_built_in_themes_are_measured() => Assert.Equal(6, ThemeCatalog.BuiltInOrder.Count);

    [Fact]
    public void The_measure_is_WCAGs_and_the_surfaces_are_what_Settings_paints()
    {
        Assert.Equal(21.0, ThemeContrast.Ratio("#ffffff", (0, 0, 0)), 2);
        Assert.Equal(1.0, ThemeContrast.Ratio("#777777", (0x77 / 255.0, 0x77 / 255.0, 0x77 / 255.0)), 6);
        var obsidian = ThemeContrast.SettingsSurfaces(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Equal(new[] { ThemeContrast.Page, ThemeContrast.Well, ThemeContrast.Sidebar }, obsidian.Select(s => s.Name));
        Assert.Equal("#1c1c1c", ThemeContrast.Hex(obsidian[0].Color));   // Glass, solid
        Assert.Equal("#0d0d0d", ThemeContrast.Hex(obsidian[1].Color));   // Bg
        Assert.Equal("#151515", ThemeContrast.Hex(obsidian[2].Color));   // #161616 at 0.92 over #0d0d0d
        var matrix = ThemeCatalog.BuiltIns["matrix"];   // opts out of Mica: its sidebar is TitleBg itself
        Assert.Equal(matrix.TitleBg, ThemeContrast.Hex(ThemeContrast.SettingsSurfaces(matrix)[2].Color));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void TextDim_and_TextMuted_read_on_the_page_the_wells_and_the_sidebar(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        var bad = new List<string>();
        foreach (var (token, hex, min) in new[] { ("TextDim", def.TextDim, ThemeContrast.DimFloor), ("TextMuted", def.TextMuted, ThemeContrast.TextMin) })
            foreach (var s in ThemeContrast.SettingsSurfaces(def))
            {
                double r = ThemeContrast.Ratio(hex, s.Color);
                if (r < min) bad.Add($"{key} {token} {hex} reads {r:0.00}:1 on the {s.Name} {ThemeContrast.Hex(s.Color)} (needs {min:0.0}:1)");
            }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Text_and_TextSecondary_already_read_and_the_ramp_still_steps_down(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        Assert.True(ThemeContrast.Worst(def.Text, def) >= ThemeContrast.TextMin, $"{key} Text");
        Assert.True(ThemeContrast.Worst(def.TextSecondary, def) >= ThemeContrast.TextMin, $"{key} TextSecondary");
        double L(string hex) { ThemeLint.TryParseHex(hex, out var c); return ThemeLint.Luminance(c); }
        Assert.True(L(def.Text) > L(def.TextSecondary) && L(def.TextSecondary) > L(def.TextDim) && L(def.TextDim) > L(def.TextMuted),
            $"{key}'s ramp no longer steps down: {def.Text} {def.TextSecondary} {def.TextDim} {def.TextMuted}");
    }

    /// <summary>The twelve values, pinned: a later change to a built-in's dim text is a reviewed change (the owner's before/after table).
    /// Blueprint's TextDim is #9194b3, the smallest raise that reads 5.0 on every surface (preflight P11).</summary>
    [Fact]
    public void The_twelve_values_are_the_plans()
    {
        var expected = new Dictionary<string, (string Dim, string Muted)>
        {
            ["obsidian"] = ("#8b8b8b", "#848484"),
            ["aurora"] = ("#808ea3", "#7487a1"),
            ["ember"] = ("#a18277", "#a47767"),
            ["mint"] = ("#5fa17e", "#4a9b70"),
            ["matrix"] = ("#009933", "#009030"),
            ["blueprint"] = ("#9194b3", "#878bb2"),
        };
        foreach (var (key, (dim, muted)) in expected)
        {
            Assert.Equal(dim, ThemeCatalog.BuiltIns[key].TextDim);
            Assert.Equal(muted, ThemeCatalog.BuiltIns[key].TextMuted);
        }
    }

    /// <summary>P8: a hex that does not parse must not take the apply path down; the selected label falls back to Text.</summary>
    [Fact]
    public void SelectedLabel_and_Ratio_never_throw_on_a_bad_hex()
    {
        var def = ThemeCatalog.BuiltIns["obsidian"];
        Assert.Equal(def.Text, ThemeContrast.SelectedLabel(def with { Accent = "nope" }));
        Assert.Equal(def.Text, ThemeContrast.SelectedLabel(def with { Glass = "" }));
        Assert.True(ThemeContrast.Ratio("nope", ThemeContrast.SettingsSurfaces(def)[0].Color) >= 1.0);
        Assert.Equal(3, ThemeContrast.SettingsSurfaces(def with { Bg = "nope" }).Count);
    }

    /// <summary>Carried from slice 2 (T12, T20): the Quit question's primary button draws #0d0d0d on the accent; it reads in every built-in.
    /// The banner's 1 px accent border on Bg (slice 7 carry) is a non-text indicator.</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_dialogs_primary_text_and_the_banners_border_read_on_every_accent(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        ThemeLint.TryParseHex(def.Accent, out var accent);
        ThemeLint.TryParseHex(def.Bg, out var bg);
        Assert.True(ThemeContrast.Ratio(ThemeContrast.PrimaryButtonText, accent) >= ThemeContrast.TextMin, $"{key}: dialog primary text");
        Assert.True(ThemeContrast.Ratio(def.Accent, bg) >= ThemeContrast.NonTextMin, $"{key}: banner border");
    }

    /// <summary>Spec: Obsidian's selected label uses Text with an accent bar. The rule is computed, so it holds for any theme (decision 11).</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_selected_label_reads_and_its_bar_shows(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        ThemeLint.TryParseHex(def.Glass, out var row);
        string label = ThemeContrast.SelectedLabel(def);
        Assert.Equal(key == "obsidian" ? def.Text : def.Accent, label);
        Assert.True(ThemeContrast.Ratio(label, row) >= ThemeContrast.TextMin, $"{key}: label {label}");
        Assert.True(ThemeContrast.Ratio(def.Accent, row) >= ThemeContrast.NonTextMin, $"{key}: the bar");
    }

    /// <summary>Review Focus 4: a custom theme whose accent can't carry text on its row gets Text too.</summary>
    [Fact]
    public void A_custom_theme_with_a_dark_accent_gets_Text()
    {
        var custom = ThemeCatalog.BuiltIns["aurora"] with { Accent = "#1e3a8a" };
        Assert.Equal(custom.Text, ThemeContrast.SelectedLabel(custom));
    }

    /// <summary>Decision D2 (task 8b): accent-coloured text reads 4.5:1 on Glass in every built-in, and is Accent itself where Accent already does.</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void AccentText_reads_on_Glass_and_is_Accent_where_Accent_already_does(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        ThemeLint.TryParseHex(def.Glass, out var glass);
        ThemeLint.TryParseHex(def.Accent, out var accent);
        string text = ThemeContrast.AccentText(def);
        Assert.True(ThemeContrast.Ratio(text, glass) >= ThemeContrast.TextMin, $"{key}: {text}");
        if (ThemeContrast.Ratio(def.Accent, glass) >= ThemeContrast.TextMin) Assert.Equal(def.Accent, text);
        else Assert.NotEqual(def.Accent, text);
    }

    /// <summary>Obsidian is the fallback and first-run theme: its accent text lands near the owner-approved #7870ff (4.51:1), same hue.</summary>
    [Fact]
    public void Obsidians_AccentText_is_the_lightest_least_step_near_7870ff()
    {
        var def = ThemeCatalog.BuiltIns["obsidian"];
        string text = ThemeContrast.AccentText(def);
        ThemeLint.TryParseHex(def.Glass, out var glass);
        Assert.Equal("#7870ff", text);
        Assert.InRange(ThemeContrast.Ratio(text, glass), 4.5, 4.6);
    }

    [Fact]
    public void AccentText_never_throws_on_a_bad_hex_and_a_light_page_gets_a_darker_accent()
    {
        var def = ThemeCatalog.BuiltIns["obsidian"];
        Assert.Equal("nope", ThemeContrast.AccentText(def with { Accent = "nope" }));
        Assert.Equal(def.Accent, ThemeContrast.AccentText(def with { Glass = "" }));
        var light = def with { Glass = "#ffffff", Accent = "#9a9aff" };
        ThemeLint.TryParseHex("#ffffff", out var white);
        string text = ThemeContrast.AccentText(light);
        Assert.True(ThemeContrast.Ratio(text, white) >= ThemeContrast.TextMin, text);
    }
}
