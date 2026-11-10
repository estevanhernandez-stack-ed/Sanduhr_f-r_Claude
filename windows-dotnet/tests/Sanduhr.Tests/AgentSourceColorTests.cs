using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSourceColorTests
{
    public static TheoryData<string, string> Pairs()
    {
        var d = new TheoryData<string, string>();
        foreach (var theme in ThemeCatalog.BuiltInOrder)
            foreach (var source in AgentSources.All)
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

    [Fact]
    public void Defaults_are_not_the_brands_exact_hexes()
    {
        Assert.NotEqual("#D97757", AgentSources.DefaultColor(AgentSources.Claude), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("#10A37F", AgentSources.DefaultColor(AgentSources.Codex), StringComparer.OrdinalIgnoreCase);
    }
}
