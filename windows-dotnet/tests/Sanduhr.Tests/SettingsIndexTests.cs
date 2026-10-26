using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The anchors, the search index and its matching rules.</summary>
public class SettingsIndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Fact]
    public void Anchors_are_unique_per_page_and_on_sidebar_pages()
    {
        var pairs = SettingsIndex.Anchors.Select(e => (e.Page, e.Anchor)).ToList();
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
        foreach (var e in SettingsIndex.Anchors)
        {
            Assert.NotNull(e.Anchor);
            Assert.Contains(e.Page, SettingsNames.Pages);
        }
    }

    [Fact]
    public void Subsection_is_set_only_on_Claude_Usage_and_always_there()
    {
        string?[] views = [SettingsIndex.OverviewView, SettingsIndex.TrendsView, SettingsIndex.SessionsView];
        foreach (var e in SettingsIndex.Anchors)
        {
            if (e.Page == SettingsPage.ClaudeUsage)
                Assert.Contains(e.Subsection, views);
            else
                Assert.Null(e.Subsection);
        }
    }

    [Fact]
    public void Every_sidebar_page_has_an_anchor()
    {
        foreach (var page in SettingsNames.Pages)
            Assert.NotEmpty(SettingsIndex.EntriesOn(page));
    }

    [Fact]
    public void All_has_one_page_entry_per_sidebar_page_titled_by_PageTitle()
    {
        var pageEntries = SettingsIndex.All.Where(e => e.Anchor is null).ToList();
        Assert.Equal(SettingsNames.Pages.Count, pageEntries.Count);
        foreach (var page in SettingsNames.Pages)
            Assert.Equal(SettingsNames.PageTitle(page), Assert.Single(pageEntries, e => e.Page == page).Title);
    }

    [Fact]
    public void Every_table_name_is_findable_at_its_anchor()
    {
        foreach (var (name, page) in SettingsNames.Table)
        {
            Assert.True(SettingsIndex.NameAnchors.TryGetValue(name, out var anchor), $"no anchor for {name}");
            Assert.NotNull(SettingsIndex.Find(page, anchor));
            // A control inside a section whose opening asks the network is not offered by search at all (it could not land inside it).
            if (SettingsIndex.FetchingSections.ContainsKey((page, anchor!)) && SettingsIndex.CollapsedSections.ContainsKey((page, anchor!))
                && !SettingsIndex.Anchors.Any(e => e.Title == name))
            {
                Assert.DoesNotContain(SettingsIndex.Search(name), h => h.Entry.Title == name);
                continue;
            }
            var lands = SettingsIndex.FetchingSections.TryGetValue((page, anchor!), out var safe) ? safe : anchor;
            Assert.Contains(SettingsIndex.Search(name), h => h.Entry.Page == page && h.Entry.Anchor == lands);
        }
    }

    [Theory]
    [InlineData("percent", SettingsPage.General, SettingsIndex.Tray)]
    [InlineData("hotkey", SettingsPage.General, SettingsIndex.Shortcuts)]
    [InlineData("vault", SettingsPage.ClaudeUsage, SettingsIndex.Vault)]
    [InlineData("statusline", SettingsPage.ClaudeCode, SettingsIndex.Folders)]
    [InlineData("MCP", SettingsPage.ClaudeCode, SettingsIndex.Folders)]
    [InlineData("glow", SettingsPage.ClaudeCode, SettingsIndex.Glow)]
    [InlineData("cleanupPeriodDays", SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)]
    [InlineData("follow", SettingsPage.Accounts, SettingsIndex.Follow)]
    [InlineData("license", SettingsPage.About, SettingsIndex.Notices)]
    [InlineData("trademark", SettingsPage.About, SettingsIndex.Trademark)]
    public void Synonyms_and_words_resolve_to_the_expected_top_hit(string query, SettingsPage page, string anchor)
    {
        var top = Top(query);
        Assert.Equal(page, top.Entry.Page);
        Assert.Equal(anchor, top.Entry.Anchor);
    }

    [Fact]
    public void Statusline_and_MCP_left_Claude_Usage_for_the_Claude_Code_page()
    {
        Assert.Null(SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.Statusline));
        Assert.Null(SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.Mcp));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), SettingsIndex.Resolve(SettingsPage.ClaudeCode, SettingsIndex.Statusline));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), SettingsIndex.Resolve(SettingsPage.ClaudeCode, SettingsIndex.Mcp));
        Assert.Null(Top("statusline").Entry.Subsection);
    }

    [Fact]
    public void Csv_first_hit_is_the_meter_history_export()
    {
        // "Export meter history…" has no "csv" in its title any more; both exports match "csv" through a
        // synonym (rank 1), and the tie goes to sidebar order: History before Claude Usage.
        var top = Top("csv");
        Assert.Equal(1, top.Rank);
        Assert.Equal(SettingsPage.History, top.Entry.Page);
        Assert.Equal(SettingsIndex.HistoryExport, top.Entry.Anchor);
        Assert.Equal(SettingsNames.ExportMeterHistory, top.Entry.Title);
        Assert.Contains(SettingsIndex.Search("csv"),
            h => h.Entry.Page == SettingsPage.ClaudeUsage && h.Entry.Anchor == SettingsIndex.Sessions && h.Rank == 1);
    }

    [Theory]
    [InlineData("vault", SettingsPage.ClaudeUsage, SettingsIndex.Vault)]
    [InlineData("history vault", SettingsPage.ClaudeUsage, SettingsIndex.Vault)]
    [InlineData("homes", SettingsPage.Publish, SettingsIndex.Homes)]
    [InlineData("export", SettingsPage.History, SettingsIndex.HistoryExport)]
    [InlineData("meter history", SettingsPage.History, SettingsIndex.HistoryExport)]
    public void Old_words_still_find_the_renamed_controls(string query, SettingsPage page, string anchor) =>
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == page && h.Entry.Anchor == anchor);

    [Fact]
    public void Endpoint_leads_with_the_Publish_page_row_then_Destination()
    {
        // "endpoint" is a synonym of both the Publish page entry and its Destination anchor (both
        // rank 1, same page). Ties break by index order, and All lists page entries before anchors,
        // so the page row comes first and Destination follows.
        var hits = SettingsIndex.Search("endpoint");
        Assert.Equal(SettingsPage.Publish, hits[0].Entry.Page);
        Assert.Null(hits[0].Entry.Anchor);
        Assert.Contains(hits, h => h.Entry.Page == SettingsPage.Publish && h.Entry.Anchor == SettingsIndex.Destination);
    }

    [Fact]
    public void Title_matches_rank_before_synonym_matches()
    {
        var top = Top("tray");
        Assert.Equal(0, top.Rank);
        Assert.Equal(SettingsNames.TrayShows, top.Entry.Title);
        Assert.Equal(SettingsPage.General, top.Entry.Page);
        Assert.Equal(SettingsIndex.Tray, top.Entry.Anchor);
    }

    [Fact]
    public void Words_folds_accents_case_and_punctuation()
    {
        Assert.Equal(["fur", "claude"], SettingsIndex.Words("Für Claude!"));
        Assert.Equal(["ctrl", "alt", "s"], SettingsIndex.Words("Ctrl+Alt+S"));
        Assert.Empty(SettingsIndex.Words(null));
        Assert.Empty(SettingsIndex.Words("  "));
    }

    [Fact]
    public void Matches_uses_word_prefixes_and_the_phrase_prefix()
    {
        Assert.True(SettingsIndex.Matches(SettingsIndex.Words("tra sh"), "Tray shows"));
        Assert.True(SettingsIndex.Matches(SettingsIndex.Words("shows tray"), "Tray shows"));
        Assert.False(SettingsIndex.Matches(SettingsIndex.Words("rays"), "Tray shows"));
        Assert.True(SettingsIndex.Matches(SettingsIndex.Words("tray sh"), "Tray shows"));
    }

    [Fact]
    public void Blank_and_nonsense_queries_find_nothing()
    {
        Assert.Empty(SettingsIndex.Search(""));
        Assert.Empty(SettingsIndex.Search("   "));
        Assert.Empty(SettingsIndex.Search(null));
        Assert.Empty(SettingsIndex.Search("zzqxv"));
    }

    [Fact]
    public void Grouped_lists_each_page_once_in_best_hit_order_without_page_rows_or_repeat_titles()
    {
        foreach (var query in new[] { "percent", "csv", "tray", "vault", "sign in", "a" })
        {
            var hits = SettingsIndex.Search(query);
            var groups = SettingsIndex.Grouped(hits);

            var expectedOrder = hits.Select(h => h.Entry.Page).Distinct().ToList();
            Assert.Equal(expectedOrder, groups.Select(g => g.Page).ToList());

            foreach (var (page, entries) in groups)
            {
                Assert.All(entries, e =>
                {
                    Assert.NotNull(e.Anchor);
                    Assert.Equal(page, e.Page);
                });
                Assert.Equal(entries.Count, entries.Select(e => e.Title).Distinct().Count());
            }
        }
    }

    [Fact]
    public void Widget_words_find_Mods_and_Widgets()
    {
        Assert.Null(SettingsNames.GroupTitle(SettingsPage.Widgets));
        Assert.Equal("Extend", SettingsNames.GroupTitle(SettingsPage.Mods));
        Assert.Equal(SettingsPage.Mods, SettingsIndex.Search("widgets")[0].Entry.Page);
        Assert.Contains((SettingsNames.WidgetCatalogCheck, SettingsPage.Mods), SettingsNames.Table);
    }
}
