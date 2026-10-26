using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 4: the Claude Code cards and Mods &amp; Widgets in the index, the names table and the alias table.</summary>
public sealed class SettingsModsIndexTests
{
    [Fact]
    public void The_Claude_Code_page_holds_folders_their_report_and_the_glow()
    {
        Assert.Equal([SettingsIndex.Folders, SettingsIndex.FolderConfig, SettingsIndex.Glow, SettingsIndex.GlowWhere],
            SettingsIndex.EntriesOn(SettingsPage.ClaudeCode).Select(e => e.Anchor));
        Assert.Equal(SettingsNames.FolderHolds, SettingsIndex.Find(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)!.Title);
        Assert.Equal(SettingsNames.DeskGlow, SettingsIndex.Find(SettingsPage.ClaudeCode, SettingsIndex.Glow)!.Title);
        Assert.Equal(SettingsNames.FolderHolds, SettingsIndex.CollapsedSections[(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)]);
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.ClaudeCode));   // two sections: no jump links (decision 19)
    }

    [Theory]
    [InlineData(SettingsPage.ClaudeCode, "statusline", SettingsPage.ClaudeCode, "folders")]
    [InlineData(SettingsPage.ClaudeCode, "mcp", SettingsPage.ClaudeCode, "folders")]
    [InlineData(SettingsPage.Mods, "config", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "mcpservers", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "claudemd", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "retention", SettingsPage.ClaudeCode, "folder-config")]
    public void Every_claude_code_and_config_alias_lands_on_a_card(SettingsPage from, string anchor, SettingsPage to, string toAnchor)
    {
        Assert.Null(SettingsIndex.Find(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.Resolve(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.ParseLink(SettingsIndex.PageId(from) + "#" + anchor));
    }

    [Theory]
    [InlineData("statusline", SettingsIndex.Folders)]
    [InlineData("MCP", SettingsIndex.Folders)]
    [InlineData("glow hook", SettingsIndex.Folders)]
    [InlineData("cleanupPeriodDays", SettingsIndex.FolderConfig)]
    [InlineData("CLAUDE.md", SettingsIndex.FolderConfig)]
    [InlineData("glow", SettingsIndex.Glow)]
    public void Claude_Code_words_land_on_the_card_or_the_glow(string query, string anchor)
    {
        var top = SettingsIndex.Search(query)[0];
        Assert.Equal((SettingsPage.ClaudeCode, anchor), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Names_inside_the_report_say_it_is_collapsed_and_the_hook_lives_on_the_cards()
    {
        Assert.Equal(SettingsIndex.FolderConfig, SettingsIndex.NameAnchors[SettingsNames.TranscriptsKept]);
        Assert.Contains((SettingsNames.TranscriptsKept, SettingsPage.ClaudeCode), SettingsNames.Table);
        var row = SettingsIndex.All.Single(e => e.Title == SettingsNames.TranscriptsKept);
        Assert.Equal(SettingsNames.FolderHolds, row.CollapsedIn);
        Assert.Equal(SettingsIndex.Folders, SettingsIndex.NameAnchors[SettingsNames.ClaudeCodeGlowHook]);
    }

    [Fact]
    public void The_tour_and_the_statusline_card_still_land()
    {
        var card = WhatsNewCards.Table.Single(c => c.Id == "statusline-label");
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), SettingsIndex.Check(card.Destination, card.Anchor));
    }
}
