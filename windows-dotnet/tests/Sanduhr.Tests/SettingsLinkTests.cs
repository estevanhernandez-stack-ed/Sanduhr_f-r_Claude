using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2 reach (spec, "Deep links and entry points"): page ids, links, the --settings switch, the
/// section each menu opens and where an opening Settings lands.</summary>
public class SettingsLinkTests
{
    [Fact]
    public void Every_page_has_a_lowercase_id_that_reads_back()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var id = SettingsIndex.PageId(page);
            Assert.Equal(id.ToLowerInvariant(), id);
            Assert.Equal(page, SettingsIndex.PageFromId(id));
            Assert.Equal(page, SettingsIndex.PageFromId(id.ToUpperInvariant()));
        }
        Assert.Equal("claudeusage", SettingsIndex.PageId(SettingsPage.ClaudeUsage));
        Assert.Null(SettingsIndex.PageFromId("3"));
        Assert.Null(SettingsIndex.PageFromId("desk look"));
        Assert.Null(SettingsIndex.PageFromId(""));
        Assert.Null(SettingsIndex.PageFromId(null));
        Assert.Equal(SettingsPage.DeskLook, SettingsIndex.PageFromId("desklook")); // a page since slice 3
    }

    [Theory]
    [InlineData("desk#pieces", SettingsPage.Desk, "pieces")]
    [InlineData("DESK#Pieces", SettingsPage.Desk, "pieces")]
    [InlineData("  claudeusage#sessions  ", SettingsPage.ClaudeUsage, "sessions")]
    [InlineData("desk", SettingsPage.Desk, null)]
    [InlineData("desk#", SettingsPage.Desk, null)]
    [InlineData("desk#nowhere", SettingsPage.Desk, null)]
    [InlineData("about#version", SettingsPage.About, "version")]
    [InlineData("history#chart", SettingsPage.History, "chart")]
    [InlineData("widget", SettingsPage.Widget, null)]
    [InlineData("widget#tray", SettingsPage.Widget, null)]
    [InlineData("", SettingsPage.General, null)]
    [InlineData("nonsense#tray", SettingsPage.General, null)]
    [InlineData("3", SettingsPage.General, null)]
    public void A_link_reads_as_a_page_and_an_anchor_on_it(string text, SettingsPage page, string? anchor) =>
        Assert.Equal(new SettingsLink(page, anchor), SettingsIndex.ParseLink(text));

    [Fact]
    public void No_text_is_no_link() => Assert.Null(SettingsIndex.ParseLink(null));

    [Fact]
    public void Check_keeps_a_live_anchor_and_drops_an_unknown_or_missing_one()
    {
        // A live anchor stays; a null anchor stays null; one the page doesn't have drops to the page's top.
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Check(SettingsPage.Desk, SettingsIndex.DeskPieces));
        Assert.Equal(new SettingsLink(SettingsPage.Desk, null), SettingsIndex.Check(SettingsPage.Desk, null));
        Assert.Equal(new SettingsLink(SettingsPage.Desk, null), SettingsIndex.Check(SettingsPage.Desk, "no-such-section"));
        Assert.Equal(new SettingsLink(SettingsPage.Desk, null), SettingsIndex.Check(SettingsPage.Desk, ""));
    }

    [Fact]
    public void Check_sends_an_old_place_to_where_it_lives_now()
    {
        // A sidebar without Widgets: Widgets#widgets-companion is an alias row, and its target (Desk, pieces) is in the index.
        var pages = SettingsNames.Pages.Where(p => p != SettingsPage.Widgets).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Check(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion, SettingsIndex.Anchors, pages));
        // With it too, now that the companion left Widgets (slice 3): the old section is gone from the page, so the alias carries it.
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Check(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
        // Desk's Launcher became a page in slice 3: the old section lands at the page's place line.
        Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace),
            SettingsIndex.Check(SettingsPage.Desk, SettingsIndex.DeskLauncher));
        // An alias whose target isn't there yet does nothing: Claude Code's meters wait for Mods & Widgets (slice 4), so a build
        // without the old section sends the link to the page's top.
        var withoutMeters = SettingsIndex.Anchors.Where(e => !(e.Page == SettingsPage.ClaudeCode && e.Anchor == SettingsIndex.Meters)).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, null),
            SettingsIndex.Check(SettingsPage.ClaudeCode, SettingsIndex.Meters, withoutMeters, SettingsNames.Pages));
    }

    [Fact]
    public void A_link_is_checked_by_the_same_rule_as_Check()
    {
        foreach (var text in new[] { "desk#pieces", "desk#nowhere", "desk", "history#chart", "desk#desk-launcher" })
        {
            var parsed = SettingsIndex.ParseLink(text)!;
            Assert.Equal(parsed, SettingsIndex.Check(parsed.Page, parsed.Anchor));
        }
    }

    [Theory]
    [InlineData(new[] { "--settings", "desk#pieces" }, "desk#pieces")]
    [InlineData(new[] { "--settings=about#version" }, "about#version")]
    [InlineData(new[] { "--SETTINGS", "alerts" }, "alerts")]
    [InlineData(new[] { "--demo", "--settings" }, "general")]
    [InlineData(new[] { "--settings", "--demo" }, "general")]
    [InlineData(new[] { "--settings=" }, "general")]
    [InlineData(new[] { "--settings", "" }, "general")]
    [InlineData(new[] { "--settings=   " }, "general")]
    [InlineData(new[] { "--demo" }, null)]
    [InlineData(new string[0], null)]
    public void The_settings_switch_reads_its_value(string[] args, string? expected) =>
        Assert.Equal(expected, SettingsIndex.LinkArgument(args));

    [Fact]
    public void A_launch_with_the_switch_opens_that_place()
    {
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.Sessions),
            SettingsIndex.LinkFromArgs(["--settings", "claudeusage#sessions"]));
        Assert.Null(SettingsIndex.LinkFromArgs(["--demo"]));
    }

    [Theory]
    [InlineData(SettingsPage.Accounts, SettingsIndex.AccountList)]
    [InlineData(SettingsPage.Alerts, SettingsIndex.Notifications)]
    [InlineData(SettingsPage.Desk, SettingsIndex.DeskPieces)]
    [InlineData(SettingsPage.DeskMessage, SettingsIndex.MessageLines)]
    [InlineData(SettingsPage.Launcher, SettingsIndex.LauncherButtons)]
    public void Menu_links_land_on_their_section(SettingsPage page, string anchor)
    {
        Assert.Equal(anchor, SettingsIndex.MenuAnchor(page));
        Assert.NotNull(SettingsIndex.Find(page, anchor));
    }

    [Fact]
    public void A_menu_with_no_section_opens_the_page_top() => Assert.Null(SettingsIndex.MenuAnchor(SettingsPage.Themes));

    [Fact]
    public void Settings_reopens_where_it_was_left_and_a_named_place_wins()
    {
        var last = new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces);
        Assert.Equal(new SettingsLink(SettingsPage.General, null), SettingsIndex.Landing(null, null, null));
        Assert.Equal(last, SettingsIndex.Landing(null, null, last));
        Assert.Equal(new SettingsLink(SettingsPage.Alerts, SettingsIndex.Notifications),
            SettingsIndex.Landing(SettingsPage.Alerts, SettingsIndex.Notifications, last));
    }
}
