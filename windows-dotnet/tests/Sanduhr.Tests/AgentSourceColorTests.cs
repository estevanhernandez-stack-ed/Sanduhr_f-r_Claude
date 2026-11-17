using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSourceColorTests
{
    public static TheoryData<string, string> Pairs()
    {
        var d = new TheoryData<string, string>();
        foreach (var theme in ThemeCatalog.BuiltInOrder)
            foreach (var source in AgentSources.Burn)
                d.Add(theme, source);
        return d;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Default_colors_read_on_every_built_in_glass(string theme, string source)
    {
        var def = ThemeCatalog.BuiltIns[theme];
        Assert.True(ThemeLint.TryParseHex(def.Glass, out var glass));
        Assert.True(ThemeContrast.Ratio(AgentSources.DefaultColor(source), glass) >= ThemeContrast.NonTextMin,
            $"{source} on {theme} glass");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Default_colors_read_on_the_card_track_the_combined_bar_is_drawn_on(string theme, string source)
    {
        // The tier card's progress track and the combined bar's track are both Sanduhr.Color.BarBg (the theme's bar_bg).
        var def = ThemeCatalog.BuiltIns[theme];
        Assert.True(ThemeLint.TryParseHex(def.BarBg, out var track));
        double ratio = ThemeContrast.Ratio(AgentSources.DefaultColor(source), track);
        Assert.True(ratio >= ThemeContrast.NonTextMin, $"{source} on {theme} bar track: {ratio:0.00}");
    }

    [Fact]
    public void Defaults_are_not_the_brands_exact_hexes()
    {
        Assert.NotEqual("#D97757", AgentSources.DefaultColor(AgentSources.Claude), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("#10A37F", AgentSources.DefaultColor(AgentSources.Codex), StringComparer.OrdinalIgnoreCase);
    }
}
