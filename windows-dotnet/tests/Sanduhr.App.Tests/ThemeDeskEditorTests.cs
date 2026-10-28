using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match, the theme's Desk look editor: rows start empty with the rules' values greyed, a row commits the whole block
/// with its change, a row that does not parse commits nothing and says why, and clearing a row gives the part back to its rule.</summary>
public sealed class ThemeDeskEditorTests
{
    private static (ThemeDeskEditorViewModel Editor, List<ThemeDesk> Commits) Editor(ThemeDefinition theme)
    {
        var commits = new List<ThemeDesk>();
        var e = new ThemeDeskEditorViewModel(["Consolas", "Segoe UI"]) { Committed = commits.Add };
        e.Load(theme);
        return (e, commits);
    }

    [Fact]
    public void Rows_start_empty_with_the_rules_greyed() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Equal("", e.Ink);
        Assert.Equal("#e8e4dc, #6c63ff", e.InkDefault);
        Assert.Equal("#6c63ff, #e8e4dc", e.BarsDefault);
        Assert.Equal("#6c63ff", e.GlowDefault);
        Assert.Equal("#0a0a0a", e.PlateColorDefault);
        Assert.Equal("#070707", e.ShadowDefault);
        Assert.True(e.Plate);
        Assert.Equal(SettingsNames.ThemeDeskFromTheme, e.Font);
        Assert.Equal(SettingsNames.DesksOwnFont, e.FontDefault);
        Assert.Empty(commits);   // showing a theme is no edit
    });

    [Fact]
    public void A_row_commits_the_whole_block_with_its_change() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        e.Ink = "#FFF, 9ad7ff";
        Assert.Equal(["#ffffff", "#9ad7ff"], commits[^1].Ink);
        e.Plate = false;
        Assert.False(commits[^1].Plate);
        Assert.Equal(["#ffffff", "#9ad7ff"], commits[^1].Ink);   // the earlier change rides along
        e.Font = "Consolas";
        Assert.Equal("Consolas", commits[^1].Font);
        e.Glow = "#38bdf8";
        Assert.Equal("#38bdf8", commits[^1].Glow);
        Assert.Null(e.Problem);
    });

    [Fact]
    public void A_row_that_does_not_parse_commits_nothing_and_says_why() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        e.Bars = "red";
        Assert.Empty(commits);
        Assert.Equal(SettingsNames.ThemeColorsProblem, e.Problem);
        e.Bars = "#111111, #222222, #333333, #444444, #555555";
        Assert.Empty(commits);
        e.Glow = "#6c63ff, #ffffff";
        Assert.Equal(SettingsNames.ThemeColorProblem, e.Problem);
        Assert.Empty(commits);
        e.Bars = "#111111";
        Assert.Null(e.Problem);
        Assert.Equal(["#111111"], Assert.Single(commits).Bars);
    });

    [Fact]
    public void A_theme_with_a_desk_block_shows_it_and_clearing_a_row_gives_it_back_to_the_rule() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Ink = ["#ffffff"], Plate = false, Font = "Consolas" } });
        Assert.Equal("#ffffff", e.Ink);
        Assert.False(e.Plate);
        Assert.Equal("Consolas", e.Font);
        e.Ink = "";
        Assert.Null(commits[^1].Ink);
        Assert.False(commits[^1].Plate);
    });

    [Fact]
    public void Retyping_what_is_already_there_asks_nothing() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Ink = ["#e8e4dc", "#6c63ff"], Plate = false } });
        e.Ink = "#E8E4DC, #6C63FF";
        e.Glow = "";
        e.Bars = "";
        e.Plate = false;
        Assert.Empty(commits);
        e.Ink = "#FFF";
        Assert.Single(commits);
    });

    [Fact]
    public void A_font_this_PC_does_not_have_is_added_so_the_picker_is_not_blank() => Sta.Run(() =>
    {
        var (e, _) = Editor(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Font = "Zapfino Nowhere" } });
        Assert.Contains("Zapfino Nowhere", e.FontChoices);
        Assert.Equal("Zapfino Nowhere", e.Font);
    });
}
