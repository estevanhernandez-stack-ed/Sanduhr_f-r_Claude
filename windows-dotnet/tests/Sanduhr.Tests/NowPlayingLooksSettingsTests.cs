using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The Looks section of Settings, Now playing: where search finds it and what it is called.</summary>
public class NowPlayingLooksSettingsTests
{
    [Fact]
    public void TheLooksPageIsInSettingsAndSearchableByWhatAPersonWouldType()
    {
        foreach (var query in new[] { "looks", "song style", "palette", "Style what's playing", "Let Claude style songs directly", "Clear Looks", "propose_now_playing_looks" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.NowPlaying && h.Entry.Anchor == SettingsIndex.NowPlayingLooks);
        Assert.Equal("np-looks", SettingsIndex.NowPlayingLooks);
        foreach (var name in new[] { SettingsNames.NowPlayingStyle, SettingsNames.NowPlayingLooksDirect, SettingsNames.ClearLooks })
            Assert.Equal(SettingsPage.NowPlaying, SettingsNames.Table.Single(t => t.Name == name).Page);
    }
}
