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

    // ---- the page flip ----

    [Fact]
    public void Mods_and_Widgets_is_the_Extend_groups_second_page_and_Widgets_leaves_the_sidebar()
    {
        Assert.Equal([SettingsPage.ClaudeCode, SettingsPage.Mods], SettingsNames.Sidebar.Single(g => g.Title == "Extend").Pages);
        Assert.DoesNotContain(SettingsPage.Widgets, SettingsNames.Pages);
        Assert.Equal("Mods & Widgets", SettingsNames.PageTitle(SettingsPage.Mods));
        Assert.Equal("Mods & Widgets settings…", SettingsNames.LinkTitle(SettingsPage.Mods));
        Assert.Equal([SettingsNames.PrivacyTitle], SettingsNames.TitlesToCome);
    }

    [Fact]
    public void The_page_holds_meters_installed_and_the_collapsed_catalog()
    {
        Assert.Equal([SettingsIndex.Meters, SettingsIndex.ModList, SettingsIndex.Catalog], SettingsIndex.EntriesOn(SettingsPage.Mods).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.MetersSection, SettingsNames.InstalledSection, SettingsNames.CatalogSection],
            SettingsIndex.EntriesOn(SettingsPage.Mods).Select(e => e.Title));
        Assert.Equal(SettingsNames.CatalogSection, SettingsIndex.CollapsedSections[(SettingsPage.Mods, SettingsIndex.Catalog)]);
        Assert.Equal(SettingsNames.CatalogSection,
            SettingsIndex.All.Single(e => e.Title == SettingsNames.WidgetCatalogCheck).CollapsedIn);   // "(in Catalog)"
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.Mods));   // three sections: no jump links (decision 19)
    }

    [Theory]
    [InlineData(SettingsPage.Widgets, "widgets-catalog", SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.Widgets, "widgets-installed", SettingsPage.Mods, "mods")]
    [InlineData(SettingsPage.Widgets, "widgets-companion", SettingsPage.Desk, "pieces")]
    [InlineData(SettingsPage.Widgets, null, SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.Widgets, "gone", SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.ClaudeCode, "meters", SettingsPage.Mods, "meters")]
    public void Every_slice_4_alias_lands_on_its_new_home(SettingsPage from, string? anchor, SettingsPage to, string toAnchor)
    {
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.Resolve(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor),
            SettingsIndex.ParseLink(SettingsIndex.PageId(from) + (anchor is null ? "" : "#" + anchor)));
    }

    [Theory]
    [InlineData("mods#config", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData("mods#mcpservers", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData("mods#claudemd", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData("mods#retention", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData("mods#mods", SettingsPage.Mods, "mods")]
    [InlineData("mods", SettingsPage.Mods, null)]
    [InlineData("claudecode#statusline", SettingsPage.ClaudeCode, "folders")]
    [InlineData("claudecode#mcp", SettingsPage.ClaudeCode, "folders")]
    public void Mods_and_Config_links_land_after_the_flip(string link, SettingsPage to, string? toAnchor) =>
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.ParseLink(link));

    [Fact]
    public void Pack_words_land_on_the_catalog_now_that_it_is_live()
    {
        // Slice 2's per-slice boost check (final review T4): each slice pins its own boost against the real index.
        foreach (var q in new[] { "widgets", "packs", "pack" })
        {
            var top = SettingsIndex.Search(q)[0];
            Assert.Equal((SettingsPage.Mods, SettingsIndex.Catalog, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        }
    }

    [Theory]
    [InlineData("hourglass", SettingsIndex.Catalog)]
    [InlineData("install", SettingsIndex.Catalog)]
    [InlineData("uninstall", SettingsIndex.ModList)]
    [InlineData("made here", SettingsIndex.ModList)]
    [InlineData("read-only", SettingsIndex.ModList)]
    [InlineData("sanduhr-meters", SettingsIndex.Meters)]
    public void Mods_and_Widgets_words_land_on_their_section(string query, string anchor)
    {
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Mods && h.Entry.Anchor == anchor);
    }

    [Fact]
    public void Prose_that_names_the_page_reads_as_the_page()
    {
        // Slice 1 final review T1: "Settings, Mods & Widgets" read as a page "Mods" before the page had this title.
        Assert.Empty(SettingsNames.PathProblems("Install packs in Settings, Mods & Widgets, Catalog."));
        Assert.Empty(SettingsNames.PathProblems("Settings, Mods & Widgets"));
        Assert.NotEmpty(SettingsNames.PathProblems("Settings, Widgets"));
        Assert.Equal(["Mods & Config"], SettingsNames.RetiredNames("Open Mods & Config"));
        Assert.Equal(["Widgets settings…"], SettingsNames.RetiredNames("Widgets settings…"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.LinkTitle(SettingsPage.Mods)));   // P3: the row is whole-name only
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.FilterWidgets));   // the chip may say "Widgets"
        Assert.Contains("Settings, Mods & Widgets", WhatsNewCards.Table.Single(c => c.Id == "widgets").Body);
    }
}
