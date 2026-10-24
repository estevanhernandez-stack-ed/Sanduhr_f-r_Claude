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
    public void A_row_naming_a_page_this_build_lacks_changes_nothing()
    {
        // Desk's Look moves to Desk Look in slice 3; until that page exists the old link stays put.
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
}
