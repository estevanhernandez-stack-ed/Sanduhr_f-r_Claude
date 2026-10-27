using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: Streamer mode's anchors, its search words, Privacy's four sections and jump links.</summary>
public class SettingsSlice7IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Fact]
    public void Privacy_holds_four_sections_in_the_carrys_order_with_jump_links()
    {
        Assert.Equal([SettingsIndex.Streamer, SettingsIndex.StreamerAllowlist, SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor).Distinct());
        Assert.Equal([SettingsIndex.Streamer, SettingsIndex.StreamerAllowlist, SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.JumpLinks(SettingsPage.Privacy).Select(l => l.Anchor));
        Assert.Equal([SettingsNames.StreamerSection, SettingsNames.ShowRealNameSection, SettingsNames.WhatLeavesThisPc, SettingsNames.WhatClaudeCanChange],
            SettingsIndex.JumpLinks(SettingsPage.Privacy).Select(l => l.Title));
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("streamer")]
    [InlineData("screen share")]
    [InlineData("hide names")]
    [InlineData("obs")]
    [InlineData("demo")]
    [InlineData("streamer mode")]
    public void Streaming_words_land_on_streamer_mode_first(string query) =>
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Streamer), (Top(query).Entry.Page, Top(query).Entry.Anchor));

    [Theory]
    [InlineData("codename")]
    [InlineData("codenames")]
    public void Codename_lands_on_the_allowlist_first(string query) =>
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.StreamerAllowlist), (Top(query).Entry.Page, Top(query).Entry.Anchor));

    /// <summary>Slice 6's P2 lesson: the new entries take no word another page owns.</summary>
    [Theory]
    [InlineData("record")]
    [InlineData("hide")]
    [InlineData("account")]
    [InlineData("folder")]
    [InlineData("share")]
    [InlineData("names")]
    public void Words_other_pages_own_do_not_land_on_streamer_mode(string query)
    {
        var top = Top(query).Entry;
        Assert.False(top.Page == SettingsPage.Privacy && (top.Anchor == SettingsIndex.Streamer || top.Anchor == SettingsIndex.StreamerAllowlist),
            $"\"{query}\" lands on {top.Page}#{top.Anchor}");
    }

    [Fact]
    public void Shuffle_finds_the_switchs_section()
    {
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Streamer), (Top("shuffle").Entry.Page, Top("shuffle").Entry.Anchor));
    }

    [Fact]
    public void The_switch_and_shuffle_are_in_the_names_table_on_Privacy()
    {
        Assert.Contains((SettingsNames.StreamerSection, SettingsPage.Privacy), SettingsNames.Table);
        Assert.Contains((SettingsNames.ShuffleCodenames, SettingsPage.Privacy), SettingsNames.Table);
        Assert.Equal(SettingsIndex.Streamer, SettingsIndex.NameAnchors[SettingsNames.StreamerSection]);
        Assert.Equal(SettingsIndex.Streamer, SettingsIndex.NameAnchors[SettingsNames.ShuffleCodenames]);
    }

    [Fact]
    public void The_masking_pages_are_the_eleven_the_reach_inventory_names()
    {
        Assert.Equal(
            [SettingsPage.Accounts, SettingsPage.Privacy, SettingsPage.ClaudeUsage, SettingsPage.Publish, SettingsPage.Desk, SettingsPage.Weather,
             SettingsPage.Watchers, SettingsPage.ClaudeCode, SettingsPage.Mods, SettingsPage.About, SettingsPage.Launcher],
            SettingsIndex.StreamerPages);
    }
}
