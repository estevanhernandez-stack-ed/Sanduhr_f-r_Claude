using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 2 (spec, "Deep links and entry points" and "Search"): the alias table, the boosts, the
/// collapsed-section field and the index's agreement with the XAML.</summary>
public class SettingsReachIndexTests
{
    /// <summary>The v2 page ids (spec, the sidebar table), lower case.</summary>
    private static readonly string[] V2PageIds =
    [
        "general", "accounts", "alerts", "privacy", "claudeusage", "publish", "desk", "desklook", "deskmessage",
        "nowplaying", "weather", "watchers", "launcher", "claudecode", "mods", "widget", "themes", "about",
    ];

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    [Fact]
    public void Every_live_page_and_anchor_resolves_to_itself()
    {
        foreach (var e in SettingsIndex.Anchors)
            Assert.Equal(new SettingsLink(e.Page, e.Anchor), SettingsIndex.Resolve(e.Page, e.Anchor));
        foreach (var page in SettingsNames.Pages)
            Assert.Equal(new SettingsLink(page, null), SettingsIndex.Resolve(page, null));
        Assert.Equal(new SettingsLink(SettingsPage.Widget, null), SettingsIndex.Resolve(SettingsPage.Widget, null));
    }

    [Fact]
    public void Every_alias_whose_target_page_exists_lands_there_once_its_source_is_retired()
    {
        var rows = SettingsIndex.Aliases.Where(a => SettingsIndex.PageFromId(a.ToPageId) is not null).ToList();
        Assert.NotEmpty(rows);
        // A later build: every source gone, every target present.
        var anchors = SettingsIndex.Anchors
            .Where(e => !SettingsIndex.Aliases.Any(a => a.FromPage == e.Page && a.FromAnchor == e.Anchor))
            .Concat(rows.Select(a => new SettingsEntry(a.ToAnchor, SettingsIndex.PageFromId(a.ToPageId)!.Value, a.ToAnchor, [])))
            .DistinctBy(e => (e.Page, e.Anchor))
            .ToList();
        var pages = SettingsNames.Pages.Where(p => p is not SettingsPage.History and not SettingsPage.Widgets).ToList();
        foreach (var a in rows)
        {
            var to = SettingsIndex.PageFromId(a.ToPageId)!.Value;
            Assert.Equal(new SettingsLink(to, a.ToAnchor), SettingsIndex.Resolve(a.FromPage, a.FromAnchor, anchors, pages));
        }
        // A retired page with an anchor it never had still lands on the page's new home.
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, "meters"),
            SettingsIndex.Resolve(SettingsPage.History, "nowhere", anchors, pages));
    }

    [Fact]
    public void An_alias_whose_target_anchor_is_missing_keeps_the_place()
    {
        // Desk Look exists; without its `look` section the old link stays put.
        var withoutLook = SettingsIndex.Anchors.Where(e => e.Anchor != SettingsIndex.DeskLook).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskLook),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskLook, withoutLook, SettingsNames.Pages));
    }

    [Fact]
    public void The_alias_table_has_one_row_per_old_place_and_names_v2_pages_only()
    {
        var sources = SettingsIndex.Aliases.Select(a => (a.FromPage, a.FromAnchor)).ToList();
        Assert.Equal(sources.Count, sources.Distinct().Count());
        Assert.All(SettingsIndex.Aliases, a => Assert.Contains(a.ToPageId, V2PageIds));
        Assert.Equal(22, SettingsIndex.Aliases.Count);
    }

    [Fact]
    public void Claudes_suggestion_on_Message_is_indexed()
    {
        Assert.NotNull(SettingsIndex.Find(SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion));
        var top = SettingsIndex.Search("suggestion")[0];
        Assert.Equal((SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Every_anchor_marked_in_XAML_is_in_the_index()
    {
        var views = Path.Combine(AppDir(), "Views");
        var xaml = File.ReadAllText(Path.Combine(views, "SettingsWindow.xaml"));
        var missing = new List<string>();
        foreach (Match tab in Regex.Matches(xaml, "<TabItem\\b[^>]*?\\bTag=\"(\\w+)\"[^>]*>"))
        {
            var page = Enum.Parse<SettingsPage>(tab.Groups[1].Value);
            int end = xaml.IndexOf("</TabItem>", tab.Index, StringComparison.Ordinal);
            var range = xaml[tab.Index..(end < 0 ? xaml.Length : end)];
            var texts = new List<string> { range };
            foreach (Match c in Regex.Matches(range, "<views:(\\w+Page)\\b"))
            {
                var file = Path.Combine(views, c.Groups[1].Value + ".xaml");
                if (File.Exists(file)) texts.Add(File.ReadAllText(file));
            }
            foreach (var text in texts)
                foreach (Match m in Regex.Matches(text, "views:SettingsAnchor\\.Id=\"([\\w-]+)\""))
                    if (SettingsIndex.Find(page, m.Groups[1].Value) is null)
                        missing.Add($"{page}/{m.Groups[1].Value}");
        }
        Assert.True(missing.Count == 0, "Marked in XAML but not in the index: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_boost_ranks_first_only_once_its_section_is_live()
    {
        foreach (var boost in SettingsIndex.Boosts)
        {
            bool live = SettingsIndex.PageFromId(boost.PageId) is { } page && SettingsIndex.Find(page, boost.Anchor) is not null;
            foreach (var q in boost.Queries)
                Assert.True(live == SettingsIndex.Search(q).Any(h => h.Rank < 0), $"\"{q}\" (live: {live})");
        }
    }

    [Fact]
    public void A_boost_waits_for_its_section_then_ranks_first()
    {
        var later = SettingsIndex.All.Append(new SettingsEntry("Updates", SettingsPage.About, "updates", [])).ToList();
        var top = SettingsIndex.Search("update", later)[0];
        Assert.Equal((SettingsPage.About, "updates", -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        Assert.Equal("updates", SettingsIndex.Search("Updates", later)[0].Entry.Anchor);
        // Accounts' "update sign-in" is still found, below it.
        Assert.Contains(SettingsIndex.Search("update", later), h => h.Entry.Page == SettingsPage.Accounts);
        // Typing part of the word is not the word.
        Assert.DoesNotContain(SettingsIndex.Search("upd", later), h => h.Rank < 0);
    }

    [Fact]
    public void Pack_words_land_on_the_catalog_once_Mods_and_Widgets_has_one()
    {
        var later = SettingsIndex.All.Append(new SettingsEntry("Catalog", SettingsPage.Mods, "catalog", [])).ToList();
        foreach (var q in new[] { "widgets", "packs", "pack" })
        {
            var top = SettingsIndex.Search(q, later)[0];
            Assert.Equal((SettingsPage.Mods, "catalog"), (top.Entry.Page, top.Entry.Anchor));
        }
    }

    [Fact]
    public void Every_boost_names_a_v2_page_and_the_spec_words()
    {
        Assert.All(SettingsIndex.Boosts, b => Assert.Contains(b.PageId, V2PageIds));
        var words = SettingsIndex.Boosts.SelectMany(b => b.Queries).ToList();
        foreach (var w in new[] { "font", "update", "updates", "docker", "runner", "privacy", "offline", "network", "send",
                     "stream", "streamer", "screen share", "hide names", "obs", "demo", "codename", "widgets", "packs", "pack" })
            Assert.Contains(w, words);
    }

    [Fact]
    public void A_hit_in_a_collapsed_section_carries_the_section_name()
    {
        var margins = new SettingsEntry("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, ["edge"], CollapsedIn: "Advanced");
        Assert.Equal("Advanced", SettingsIndex.Search("edge", [margins]).Single().Entry.CollapsedIn);
        // Nothing is collapsed until slice 3 folds Designs and Advanced.
        Assert.All(SettingsIndex.All, e => Assert.Null(e.CollapsedIn));
    }
}
