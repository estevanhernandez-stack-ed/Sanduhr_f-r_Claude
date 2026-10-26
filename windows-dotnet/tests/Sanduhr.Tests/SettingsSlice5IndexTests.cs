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

    [Theory]
    [InlineData("clear history", SettingsIndex.AccountData)]
    [InlineData("remove account", SettingsIndex.AccountData)]
    [InlineData("linked folder", SettingsIndex.AccountData)]
    [InlineData("add account", SettingsIndex.SignInSection)]
    [InlineData("sessionKey", SettingsIndex.SignInSection)]
    [InlineData("label", SettingsIndex.SignInSection)]
    public void Account_words_find_their_section(string query, string anchor)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Accounts, anchor), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Clear_history_is_retired_as_a_label_but_the_new_one_is_live()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("Clear history"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.ClearAccountHistory));
    }

    [Fact]
    public void Old_history_links_land_on_the_Meters_view()
    {
        Assert.DoesNotContain(SettingsPage.History, SettingsNames.Pages);
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.ParseLink("history"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.ParseLink("history#chart"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), SettingsIndex.ParseLink("history#export"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.LinkFromArgs(["--settings", "history"]));
        Assert.Equal(SettingsIndex.MetersView, SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory)!.Subsection);
        Assert.Equal(SettingsIndex.MetersView, SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport)!.Subsection);
    }

    [Fact]
    public void History_words_find_the_Meters_view()
    {
        Assert.Equal((SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), (Top("meter history").Entry.Page, Top("meter history").Entry.Anchor));
        // "history" alone also finds Accounts' "Clear this account's history…" first (Accounts comes before Claude Usage in the sidebar).
        Assert.Contains(SettingsIndex.Search("history").Take(3), h => (h.Entry.Page, h.Entry.Anchor) == (SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory));
        Assert.Equal((SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), (Top("csv").Entry.Page, Top("csv").Entry.Anchor));
    }

    [Fact]
    public void The_History_page_title_is_retired()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("History"));
        Assert.NotEmpty(SettingsNames.RetiredNames("History chart"));
        Assert.NotEmpty(SettingsNames.RetiredNames("History settings…"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.MeterHistorySection));
        Assert.NotEmpty(SettingsNames.PathProblems("Settings, History"));
        Assert.Equal("History", SettingsNames.PageTitle(SettingsPage.History));   // kept for the aliases and logs
    }
}
