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

    [Theory]
    [InlineData("history")]
    [InlineData("meter history")]
    [InlineData("meters")]
    [InlineData("usage history")]
    public void History_words_land_on_the_meter_history_section_first(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void History_still_finds_the_accounts_clear_row_below_and_groups_claude_usage_first()
    {
        var hits = SettingsIndex.Search("history");
        Assert.Contains(hits, h => h.Entry.Page == SettingsPage.Accounts && h.Entry.Title == SettingsNames.ClearAccountHistory);
        Assert.Equal(SettingsPage.ClaudeUsage, SettingsIndex.Grouped(hits)[0].Page);
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
    [InlineData("sample")]
    [InlineData("what gets sent")]
    [InlineData("payload")]
    public void Sample_words_find_what_gets_sent(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Publish, SettingsIndex.Sample), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Publish_usage_has_jump_links_to_its_sections_in_page_order()
    {
        var links = SettingsIndex.JumpLinks(SettingsPage.Publish);
        Assert.Equal([SettingsIndex.Destination, SettingsIndex.Token, SettingsIndex.Homes, SettingsIndex.Schedule, SettingsIndex.Sample],
            links.Select(l => l.Anchor));
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Title)));
        Assert.Equal(SettingsNames.PublishSampleSection, links[^1].Title);
        Assert.Equal(SettingsIndex.Sample, SettingsIndex.NameAnchors[SettingsNames.ShowASample]);
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

    [Fact]
    public void Widget_is_a_page_and_widget_without_an_anchor_still_shows_the_widget()
    {
        Assert.Contains(SettingsPage.Widget, SettingsNames.Pages);
        Assert.Equal("Widget", SettingsNames.GroupTitle(SettingsPage.Widget));
        Assert.Equal([SettingsPage.Widget, SettingsPage.Themes], SettingsNames.Sidebar.Single(g => g.Title == "Widget").Pages);
        Assert.True(SettingsIndex.ShowsTheWidget(new SettingsLink(SettingsPage.Widget, null)));
        Assert.True(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget")!));
        Assert.True(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget#tray")!));   // an anchor the page doesn't have drops
        Assert.False(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget#widget-options")!));
        Assert.Equal(new SettingsLink(SettingsPage.Widget, SettingsIndex.WidgetOptions), SettingsIndex.ParseLink("widget#widget-options"));
        Assert.Equal(SettingsIndex.WidgetOptions, SettingsIndex.MenuAnchor(SettingsPage.Widget));
        foreach (var card in WhatsNewCards.Table.Where(c => c.Destination == SettingsPage.Widget))
            Assert.True(SettingsIndex.ShowsTheWidget(new SettingsLink(card.Destination, card.Anchor)), card.Id);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("graph style")]
    [InlineData("on top")]
    [InlineData("deep work")]
    [InlineData("limit order")]
    [InlineData("tier order")]
    public void Widget_words_find_the_widget_options(string query) =>
        Assert.Contains(SettingsIndex.Search(query).Take(3), h => h.Entry.Page == SettingsPage.Widget && h.Entry.Anchor == SettingsIndex.WidgetOptions);

    [Fact]
    public void Just_the_Desk_has_a_section_and_the_query_still_has_an_always_visible_hit()
    {
        Assert.NotNull(SettingsIndex.Find(SettingsPage.Widget, SettingsIndex.JustTheDeskSection));
        Assert.Equal((SettingsPage.Widget, SettingsIndex.JustTheDeskSection),
            (Top("show the widget").Entry.Page, Top("show the widget").Entry.Anchor));
        // P16: the Widget section is collapsed unless the mode is on, so the Desk row keeps the words too.
        Assert.Contains(SettingsIndex.Search("Just the Desk"), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.SignInReminders);
    }
}
