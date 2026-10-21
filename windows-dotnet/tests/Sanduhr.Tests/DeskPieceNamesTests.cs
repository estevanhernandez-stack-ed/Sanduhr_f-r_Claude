using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The names the layout map and Arrange mode give each Desk piece (spec 7.3).</summary>
public class DeskPieceNamesTests
{
    [Theory]
    [InlineData("message", "Message")]
    [InlineData("clock", "Clock and date")]
    [InlineData("claude", "Claude meters (line)")]
    [InlineData("meters", "Claude meters (bars)")]
    [InlineData("nowPlaying", "Now playing")]
    [InlineData("telemetry", "System (CPU, RAM, disk)")]
    [InlineData("weather", "Weather")]
    [InlineData("glow", "Claude Code glow")]
    [InlineData("watchers", "Watchers")]
    public void EachDrawnPieceHasItsName(string widget, string name) =>
        Assert.Equal(name, SettingsNames.DeskPieceName(widget));

    [Fact]
    public void AnUnknownPieceGoesByItsKey() =>
        Assert.Equal("meetings", SettingsNames.DeskPieceName("meetings"));

    [Fact]
    public void ArrangeDeskIsOnTheDeskPageAndSearchable()
    {
        Assert.Contains((SettingsNames.ArrangeDesk, SettingsPage.Desk), SettingsNames.Table);
        Assert.Equal(DeskArrangeCopy.SettingsButton, SettingsNames.ArrangeDesk);
        var entry = SettingsIndex.All.Single(e => e.Title == SettingsNames.ArrangeDesk);
        foreach (var word in new[] { "drag", "move", "rearrange", "arrange" })
            Assert.Contains(word, entry.Synonyms);
    }
}
