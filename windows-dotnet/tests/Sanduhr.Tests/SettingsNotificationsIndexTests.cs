using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>5.0 (spec 4): the words for the glow's switches and Try it moved with them to Settings, Notifications; the glow's
/// section on the Claude Code page keeps the glow's own words.</summary>
public class SettingsNotificationsIndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Theory]
    [InlineData("try it")]
    [InlineData("waiting")]
    [InlineData("finishes")]
    public void The_moved_controls_words_land_on_Notifications(string query)
    {
        Assert.Equal(SettingsPage.Notifications, Top(query).Entry.Page);
        Assert.DoesNotContain(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.ClaudeCode && h.Entry.Anchor == SettingsIndex.Glow);
    }

    [Theory]
    [InlineData("glow")]
    [InlineData("edge light")]
    public void The_glows_own_words_still_find_the_Claude_Code_section(string query) =>
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.ClaudeCode && h.Entry.Anchor == SettingsIndex.Glow);

    [Fact]
    public void The_Claude_Code_page_links_to_Notifications_by_its_title() =>
        Assert.Equal("Notifications settings…", SettingsNames.NotificationsLinkTitle);
}
