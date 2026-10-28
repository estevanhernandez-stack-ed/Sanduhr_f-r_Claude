using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match's pure rules: who owns a theme, the name and key of the theme Follow makes, the look spelled out into a theme,
/// and what Match now writes into the Desk's own settings.</summary>
public class ThemeMatchTests
{
    [Theory]
    [InlineData("obsidian", ThemeOrigin.BuiltIn)]
    [InlineData("good-theme:fixture-dark", ThemeOrigin.Pack)]
    [InlineData("dusk", ThemeOrigin.User)]
    public void Origin_says_who_owns_the_theme(string key, ThemeOrigin origin) => Assert.Equal(origin, ThemeMatch.Origin(key));

    [Fact]
    public void The_new_themes_name_is_the_theme_plus_Desk_made_unique()
    {
        Assert.Equal("Obsidian + Desk", ThemeMatch.NewThemeName("Obsidian", _ => false));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Obsidian + Desk", "Obsidian + Desk 2" };
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian", taken.Contains));
        // Following again from a "+ Desk" theme does not stack the suffix.
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian + Desk", taken.Contains));
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian + Desk 2", taken.Contains));
    }

    [Fact]
    public void A_long_name_is_cut_so_the_suffix_fits_the_strip()
    {
        string name = ThemeMatch.NewThemeName("Midnight Aurora Borealis", _ => false);   // 24 characters, the limit
        Assert.Equal("Midnight Aurora B + Desk", name);
        Assert.True(name.Length <= ThemeLint.MaxNameLength);
        Assert.True(ThemeLint.Lint(ThemeMatch.WithDesk(ThemeCatalog.BuiltIns["obsidian"], null, name)).Ok);
    }

    [Fact]
    public void The_file_key_never_lands_on_a_built_in_or_a_file_that_is_there()
    {
        Assert.Equal("obsidian-desk", ThemeMatch.NewThemeKey("Obsidian + Desk", _ => false));
        Assert.Equal("obsidian-desk-2", ThemeMatch.NewThemeKey("Obsidian + Desk", k => k == "obsidian-desk"));
        Assert.Equal("obsidian-2", ThemeMatch.NewThemeKey("Obsidian", _ => false));   // a built-in's key is never taken over
    }

    [Theory]
    [InlineData("  Dusk  ", "Dusk")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A name far longer than the strip allows", "A name far longer than t")]
    public void CleanName_trims_and_cuts(string? raw, string? expected) => Assert.Equal(expected, ThemeMatch.CleanName(raw));

    [Fact]
    public void Follow_spells_the_look_out_but_leaves_bars_and_font_as_the_theme_had_them()
    {
        var obsidian = ThemeCatalog.BuiltIns["obsidian"];
        var desk = ThemeMatch.SpelledOut(obsidian);
        Assert.Equal(["#e8e4dc", "#6c63ff"], desk.Ink);
        Assert.Equal(["#6c63ff", "#e8e4dc"], desk.Message);
        Assert.Equal("#6c63ff", desk.Glow);
        Assert.True(desk.Plate);
        Assert.Equal("#0a0a0a", desk.PlateColor);
        Assert.Equal("#070707", desk.Shadow);
        Assert.Null(desk.Bars);   // writing bars would turn the widget's ramp into a gradient
        Assert.Null(desk.Font);
        // Spelled out, it is the same look.
        Assert.Equal(ThemeDeskLook.From(obsidian).ToJson().ToJsonString(), ThemeDeskLook.From(obsidian with { Desk = desk }).ToJson().ToJsonString());
        // A theme that set bars and a desk font keeps them.
        var own = ThemeMatch.SpelledOut(obsidian with { Desk = new ThemeDesk { Bars = ["#ff0000"], Font = "Consolas" } });
        Assert.Equal(["#ff0000"], own.Bars);
        Assert.Equal("Consolas", own.Font);
    }

    [Fact]
    public void WithDesk_keeps_every_other_field_of_the_theme()
    {
        var aurora = ThemeCatalog.BuiltIns["aurora"] with { Font = "Segoe UI" };
        var back = ThemeLint.Lint(ThemeMatch.WithDesk(aurora, ThemeMatch.SpelledOut(aurora), "Aurora + Desk")).Theme!;
        Assert.Equal(aurora with { Name = "Aurora + Desk", Desk = back.Desk }, back);
        Assert.Equal("#38bdf8", back.InnerHighlight!.Color);
        Assert.NotNull(back.Desk);
    }

    [Fact]
    public void Match_now_writes_what_the_desks_own_settings_hold()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["matrix"]);
        var own = ThemeMatch.OwnSettings(look, DeskSettings.Default with { Plate = false, Font = "" }, DeskInkSettings.Default with { Shadow = false },
            DeskMessageSettings.Default with { Glow = false, Size = 70 }, NowPlayingSettings.Default);
        Assert.Equal(DeskInkMode.Custom, own.Ink.Mode);
        Assert.Equal("00ff41", own.Ink.Color);
        Assert.True(own.Ink.Shadow);
        Assert.Equal("Cascadia Code", own.Desk.Font);
        Assert.True(own.Desk.Plate);
        Assert.Equal("00ff41", own.Message.Color);
        Assert.True(own.Message.Glow);
        Assert.Equal(70, own.Message.Size);   // sizes are no part of a look
        Assert.True(own.NowPlaying.BarsGradient);
    }

    [Fact]
    public void A_theme_without_a_font_leaves_the_desks_own_font()
    {
        var own = ThemeMatch.OwnSettings(ThemeDeskLook.From(ThemeCatalog.BuiltIns["aurora"]), DeskSettings.Default with { Font = "Consolas" },
            DeskInkSettings.Default, DeskMessageSettings.Default, NowPlayingSettings.Default);
        Assert.Equal("Consolas", own.Desk.Font);
        Assert.Equal("e2e8f0,38bdf8", own.Ink.Color);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Match_now_copies_the_look_into_the_own_color_and_leaves_Use_the_themes_colors_as_it_was(bool themeColors)
    {
        var own = ThemeMatch.OwnSettings(ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]), DeskSettings.Default, DeskInkSettings.Default,
            DeskMessageSettings.Default with { ThemeColors = themeColors, Color = "ff00ff" }, NowPlayingSettings.Default);
        Assert.Equal("6c63ff,e8e4dc", own.Message.Color);
        Assert.Equal(themeColors, own.Message.ThemeColors);
    }

    [Fact]
    public void Use_the_themes_colors_is_on_for_a_new_install_and_off_for_a_key_without_the_field_and_round_trips()
    {
        Assert.True(DeskMessageSettings.Decode(null).ThemeColors);
        Assert.False(DeskMessageSettings.Decode(System.Text.Json.Nodes.JsonNode.Parse("""{"color":"ff00ff"}""")).ThemeColors);
        Assert.False(DeskMessageSettings.Decode(System.Text.Json.Nodes.JsonNode.Parse("""{"theme_colors":false}""")).ThemeColors);
        foreach (bool on in new[] { true, false })
            Assert.Equal(on, DeskMessageSettings.Decode((DeskMessageSettings.Default with { ThemeColors = on }).Encode()).ThemeColors);
    }

    [Fact]
    public void A_font_this_PC_lacks_leaves_the_desks_own_font()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["matrix"]);
        var desk = DeskSettings.Default with { Font = "Consolas" };
        var has = ThemeMatch.OwnSettings(look, desk, DeskInkSettings.Default, DeskMessageSettings.Default, NowPlayingSettings.Default, f => f == "Cascadia Code");
        Assert.Equal("Cascadia Code", has.Desk.Font);
        var lacks = ThemeMatch.OwnSettings(look, desk, DeskInkSettings.Default, DeskMessageSettings.Default, NowPlayingSettings.Default, f => f == "Some Made Up Family");
        Assert.Equal("Consolas", lacks.Desk.Font);
        Assert.True(lacks.Desk.Plate);
    }
}
