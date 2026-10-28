using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: the Privacy page in the sidebar and the index, and the words that land on it.</summary>
public class SettingsSlice6IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("offline")]
    [InlineData("network")]
    [InlineData("send")]
    public void Privacy_words_land_on_what_leaves_this_PC_first(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Network, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
    }

    [Fact]
    public void Privacy_groups_first_for_its_own_name_and_send_still_finds_the_test_alert()
    {
        Assert.Equal(SettingsPage.Privacy, SettingsIndex.Grouped(SettingsIndex.Search("privacy"))[0].Page);
        Assert.Contains(SettingsIndex.Search("send"), h => h.Entry.Page == SettingsPage.Alerts && h.Entry.Anchor == SettingsIndex.TestAlert);
        Assert.DoesNotContain("privacy", SettingsIndex.PageSynonyms[SettingsPage.About]);
    }

    [Fact]
    public void Privacy_is_the_first_groups_fourth_page()
    {
        Assert.Equal([SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts, SettingsPage.Privacy], SettingsNames.Sidebar[0].Pages);
        Assert.Equal("Privacy", SettingsNames.PageTitle(SettingsPage.Privacy));
        Assert.Equal(SettingsPage.Privacy, SettingsNames.Linked("Privacy settings…"));
        Assert.Empty(SettingsNames.TitlesToCome);
        Assert.Equal(SettingsPage.Privacy, SettingsIndex.PageFromId("privacy"));
    }

    [Fact]
    public void Privacy_keeps_its_two_slice_6_sections_after_streamer_mode()
    {
        // Slice 7 put Streamer mode and Show the real name first; slice 6's two follow, in order (SettingsSlice7IndexTests pins the four).
        Assert.Equal([SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor).Distinct().Skip(2));
        Assert.Equal([SettingsNames.WhatLeavesThisPc, SettingsNames.WhatClaudeCanChange],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Title).Skip(2));
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("what Claude can change")]
    [InlineData("consent")]
    public void Agent_words_find_the_Privacy_page_first(string query) =>
        Assert.Equal(SettingsPage.Privacy, Top(query).Entry.Page);

    [Fact]
    public void MCP_still_finds_the_Claude_Code_folders_first() =>
        Assert.Equal((SettingsPage.ClaudeCode, SettingsIndex.Folders), (Top("MCP").Entry.Page, Top("MCP").Entry.Anchor));

    [Fact]
    public void Words_the_other_pages_own_still_land_on_them()
    {
        Assert.Equal(SettingsPage.Publish, Top("endpoint").Entry.Page);   // P2: Privacy's search words leave these alone
        Assert.Equal(SettingsPage.Desk, Top("telemetry").Entry.Page);
        Assert.Equal(SettingsPage.Publish, Top("upload").Entry.Page);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("what leaves this PC")]
    public void The_privacy_boosts_put_Privacy_first_in_the_sidebar_grouping(string query)
    {
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Network), (Top(query).Entry.Page, Top(query).Entry.Anchor));
        Assert.Equal(SettingsPage.Privacy, SettingsIndex.Grouped(SettingsIndex.Search(query))[0].Page);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("updates")]
    public void Update_words_land_on_About_Updates_first(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.About, SettingsIndex.Updates, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        if (query == "update")
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Accounts);   // "Update sign-in…" still found, below
    }

    [Fact]
    public void About_has_jump_links_to_its_sections_in_page_order()
    {
        var links = SettingsIndex.JumpLinks(SettingsPage.About);
        Assert.Equal([SettingsIndex.Version, SettingsIndex.Updates, SettingsIndex.Trademark, SettingsIndex.Where, SettingsIndex.Reset, SettingsIndex.Notices],
            links.Select(l => l.Anchor));
        Assert.Equal(SettingsNames.UpdatesSection, links[1].Title);
        // Reset's link is the section's name, not the command's (preflight P15).
        Assert.Equal(SettingsNames.ResetJump, links[4].Title);
        Assert.NotEqual(SettingsNames.ResetSanduhr, links[4].Title);
        Assert.Equal(SettingsIndex.Updates, SettingsIndex.NameAnchors[SettingsNames.CheckForUpdatesDaily]);
        Assert.Equal(SettingsIndex.Updates, SettingsIndex.NameAnchors[SettingsNames.CheckNow]);
    }
}
