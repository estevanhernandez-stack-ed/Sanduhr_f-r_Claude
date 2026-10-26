using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 5: the index, the menus and the cards for Accounts, Alerts, Claude Usage, Publish usage, Widget and About.</summary>
public class SettingsSlice5IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Fact]
    public void The_tier_card_and_the_limits_card_land_on_each_limit()
    {
        Assert.Equal(SettingsIndex.EachLimit, SettingsIndex.MenuAnchor(SettingsPage.Alerts));
        Assert.NotNull(SettingsIndex.Find(SettingsPage.Alerts, SettingsIndex.EachLimit));
        var card = WhatsNewCards.Table.Single(c => c.Id == "limits-come-and-go");
        Assert.Equal((SettingsPage.Alerts, SettingsIndex.EachLimit), (card.Destination, card.Anchor));
    }

    [Theory]
    [InlineData("each limit")]
    [InlineData("limit")]
    [InlineData("warn for this limit")]
    [InlineData("hidden limits")]
    [InlineData("until it resets")]
    public void Limit_words_find_each_limit(string query) =>
        Assert.Contains(SettingsIndex.Search(query).Take(3), h => h.Entry.Page == SettingsPage.Alerts && h.Entry.Anchor == SettingsIndex.EachLimit);

    [Fact]
    public void Alerts_has_jump_links_to_its_sections_in_page_order()
    {
        var links = SettingsIndex.JumpLinks(SettingsPage.Alerts);
        Assert.Equal([SettingsIndex.Thresholds, SettingsIndex.EachLimit, SettingsIndex.Sound, SettingsIndex.TestAlert], links.Select(l => l.Anchor));
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Title)));
    }
}
