using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>OLED care (spec 3.7.0) in Settings: found by what a person would type, on the Desk page.</summary>
public class OledCareSettingsTests
{
    [Theory]
    [InlineData("oled")]
    [InlineData("burn-in")]
    [InlineData("burn in")]
    [InlineData("drift")]
    [InlineData("dim")]
    public void TheSwitchIsSearchableByWhatAPersonWouldType(string query)
    {
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskOled);
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.OledCareSwitch).Page);
    }

    [Fact]
    public void TheKeyIsTheFrozenOne()
    {
        Assert.Equal("desk_oled_care", OledCare.Key);
        Assert.False(OledCare.DefaultOn);
    }
}
