using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 3 (spec, "Desk: the split" and the pages it creates): the names, the index rows, the aliases that
/// go live and the jump links.</summary>
public class SettingsDeskSplitIndexTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static string TabRange(string tag)
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var m = Regex.Match(xaml, "<TabItem\\b[^>]*?\\bTag=\"" + tag + "\"[^>]*>");
        Assert.True(m.Success, "no TabItem with Tag=" + tag);
        return xaml[m.Index..xaml.IndexOf("</TabItem>", m.Index, StringComparison.Ordinal)];
    }

    [Fact]
    public void A_feature_page_says_where_its_piece_sits()
    {
        Assert.Equal("On the Desk: bottom right.", SettingsNames.PieceStatus("Bottom right", deskOn: true));
        Assert.Equal("On the Desk: top right. The Desk is off: turn it on in Settings, Desk.", SettingsNames.PieceStatus("Top right", deskOn: false));
        Assert.Equal("Not on the Desk.", SettingsNames.PieceStatus(null, deskOn: true));
        Assert.Equal("Place it on the Desk", SettingsNames.PlaceOnDesk);
        Assert.Empty(SettingsNames.PathProblems(SettingsNames.DeskIsOff));
    }

    [Fact]
    public void The_Order_buttons_say_which_piece_and_where()
    {
        Assert.Equal("Move up", SettingsNames.MoveUp);
        Assert.Equal("Move down", SettingsNames.MoveDown);
        Assert.Equal("Move Weather up in bottom left", SettingsNames.MoveUpIn("Weather", "Bottom left"));
        Assert.Equal("Move Clock and date down in top right", SettingsNames.MoveDownIn("Clock and date", "Top right"));
        Assert.Equal("Weather, on the Desk", SettingsNames.PieceShownSpoken("Weather"));
    }

    [Fact]
    public void The_new_pages_open_with_their_core_words()
    {
        Assert.Equal("Watchers are " + SettingsNames.DefineWatchers + ".", SettingsNames.WatchersLine);
        Assert.Equal("The companion is " + SettingsNames.DefineCompanion + ".", SettingsNames.CompanionLine);
        Assert.Equal("Get one in Widgets…", SettingsNames.GetACompanion);   // follows PageTitle; slice 4 moves the catalog
        Assert.Equal("Where each piece sits now. Move pieces with the rows below or Arrange Desk…", SettingsNames.DeskMapCaption);
    }

    [Fact]
    public void The_Designs_summary_counts_and_says_when_put_back_is_ready()
    {
        Assert.Equal("None saved yet.", SettingsNames.DesignsSummary(0, canPutBack: false));
        Assert.Equal("1 saved.", SettingsNames.DesignsSummary(1, canPutBack: false));
        Assert.Equal("3 saved. Put my Desk back is ready.", SettingsNames.DesignsSummary(3, canPutBack: true));
    }

    [Fact]
    public void Quit_asks_in_the_tables_words() =>
        Assert.Equal(("Quit Sanduhr? The widget and the Desk close.", "Quit", "Cancel"),
            (SettingsNames.QuitQuestion, SettingsNames.QuitConfirm, SettingsNames.QuitCancel));

    [Fact]
    public void An_anchor_row_whose_target_is_missing_falls_back_to_the_page_row()
    {
        // A later build without Widgets in the sidebar, where Mods has a catalog but no "mods" section yet: the installed-widgets
        // link can't land on its own target, so it lands where the Widgets page as a whole went.
        var anchors = SettingsIndex.Anchors
            .Where(e => !(e.Page == SettingsPage.Mods && e.Anchor == SettingsIndex.ModList))
            .Append(new SettingsEntry("Catalog", SettingsPage.Mods, "catalog", []))
            .ToList();
        var pages = SettingsNames.Pages.Where(p => p != SettingsPage.Widgets).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.Mods, "catalog"),
            SettingsIndex.Resolve(SettingsPage.Widgets, SettingsIndex.WidgetsInstalled, anchors, pages));
    }

    [Fact]
    public void A_live_page_keeps_an_anchor_no_alias_carries()
    {
        // While the old page is in the sidebar there is no page row to fall back to: the link stays, and Check drops the dead
        // anchor to the page top.
        Assert.Equal(new SettingsLink(SettingsPage.Widgets, "gone"),
            SettingsIndex.Resolve(SettingsPage.Widgets, "gone", SettingsIndex.Anchors, SettingsNames.Pages));
        Assert.Equal(new SettingsLink(SettingsPage.Widgets, null), SettingsIndex.Check(SettingsPage.Widgets, "gone"));
    }

    [Fact]
    public void Retired_page_ids_still_read_so_their_aliases_can_carry_old_links()
    {
        Assert.Equal(SettingsPage.History, SettingsIndex.PageFromId("history"));
        Assert.Equal(SettingsPage.Widgets, SettingsIndex.PageFromId("Widgets"));
        Assert.Null(SettingsIndex.PageFromId("history2"));
    }

    [Fact]
    public void The_Desktop_group_holds_the_split_pages_in_the_specs_order()
    {
        var desktop = SettingsNames.Sidebar.Single(g => g.Title == "Desktop").Pages;
        Assert.Equal([SettingsPage.Desk, SettingsPage.DeskLook, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather,
            SettingsPage.Watchers, SettingsPage.Launcher, SettingsPage.Widgets], desktop);
        Assert.Equal(["desklook", "watchers", "launcher"],
            new[] { SettingsPage.DeskLook, SettingsPage.Watchers, SettingsPage.Launcher }.Select(SettingsIndex.PageId));
    }

    [Theory]
    [InlineData("message", SettingsPage.DeskMessage)]
    [InlineData("nowPlaying", SettingsPage.NowPlaying)]
    [InlineData("weather", SettingsPage.Weather)]
    [InlineData("watchers", SettingsPage.Watchers)]
    [InlineData("launcher", SettingsPage.Launcher)]
    [InlineData("glow", SettingsPage.ClaudeCode)]
    public void A_piece_with_a_page_names_it(string widget, SettingsPage page) =>
        Assert.Equal(page, SettingsNames.DeskPiecePage(widget));

    [Theory]
    [InlineData("clock")]
    [InlineData("claude")]
    [InlineData("meters")]
    [InlineData("telemetry")]
    [InlineData("companion")]
    public void A_piece_without_a_page_names_none(string widget) => Assert.Null(SettingsNames.DeskPiecePage(widget));

    [Fact]
    public void The_pieces_section_is_called_Pieces_and_still_found_by_its_old_name()
    {
        var entry = SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskPieces)!;
        Assert.Equal(SettingsNames.PiecesSection, entry.Title);
        Assert.Contains(SettingsIndex.Search("where each piece sits"), h => h.Entry == entry);
        Assert.Contains(SettingsIndex.Search("move up"), h => h.Entry == entry);
        Assert.Equal(["Where each piece sits"], SettingsNames.RetiredNames("Where each piece sits"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.DeskMapCaption));   // a whole-name retirement: the caption may say it
        Assert.Empty(SettingsNames.PathProblems("Move it in Settings, Desk, Pieces."));
    }

    [Fact]
    public void The_companion_is_found_on_Desk_Pieces_and_its_old_link_lands_there()
    {
        Assert.Null(SettingsIndex.Find(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
        Assert.Contains(SettingsIndex.Search("companion"), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskPieces);
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Resolve(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
        Assert.Equal(["Show the companion on the Desk"], SettingsNames.RetiredNames("Show the companion on the Desk"));
    }

    [Fact]
    public void Desk_Look_holds_four_sections_and_the_old_links_land_there()
    {
        Assert.Equal([SettingsIndex.DeskLook, SettingsIndex.DeskSizes, SettingsIndex.DeskInk, SettingsIndex.DeskPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.FontsSection, SettingsNames.SizesSection, SettingsNames.InkAndColor, SettingsNames.BackPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Title));
        foreach (var (from, anchor, to) in new[]
        {
            (SettingsPage.Desk, SettingsIndex.DeskLook, SettingsIndex.DeskLook),
            (SettingsPage.Desk, SettingsIndex.DeskInk, SettingsIndex.DeskInk),
            (SettingsPage.Desk, SettingsIndex.DeskPlate, SettingsIndex.DeskPlate),
            (SettingsPage.DeskMessage, SettingsIndex.MessageLook, SettingsIndex.DeskSizes),
        })
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, to), SettingsIndex.Resolve(from, anchor));
        Assert.Null(SettingsIndex.Find(SettingsPage.DeskMessage, SettingsIndex.MessageLook));
    }

    [Fact]
    public void Font_lands_on_Desk_Look_Fonts_first()
    {
        // Slice 2's boost, live now that its page is (slice 2 final review: pin "font" once Desk Look lands).
        var top = SettingsIndex.Search("font")[0];
        Assert.Equal((SettingsPage.DeskLook, SettingsIndex.DeskLook, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        Assert.Contains(SettingsIndex.Search("message color"), h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskInk);
        Assert.Contains(SettingsIndex.Search("clock size"), h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskSizes);
    }

    [Fact]
    public void The_moved_names_sit_on_Desk_Look()
    {
        foreach (var name in new[] { SettingsNames.DeskFont, SettingsNames.MessageFont, SettingsNames.MessageColor, SettingsNames.DeskPlateSwitch, SettingsNames.DeskInkPicker })
            Assert.Equal(SettingsPage.DeskLook, SettingsNames.Table.Single(t => t.Name == name).Page);
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Ink and color"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Back plate"));
    }

    [Fact]
    public void Watchers_holds_its_switches_and_where_they_show()
    {
        Assert.Equal([SettingsIndex.Watchers, SettingsIndex.CiRunners, SettingsIndex.WatchersPlace],
            SettingsIndex.EntriesOn(SettingsPage.Watchers).Select(e => e.Anchor));
        Assert.Equal(new SettingsLink(SettingsPage.Watchers, SettingsIndex.Watchers),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskWatchers));
        // Slice 2's boost, live now: "docker" and "runner" land on the CI runners first.
        foreach (var q in new[] { "docker", "runner", "runners" })
            Assert.Equal((SettingsPage.Watchers, SettingsIndex.CiRunners, -1),
                (SettingsIndex.Search(q)[0].Entry.Page, SettingsIndex.Search(q)[0].Entry.Anchor, SettingsIndex.Search(q)[0].Rank));
        Assert.Equal(SettingsPage.Watchers, SettingsNames.Table.Single(t => t.Name == SettingsNames.WatchersSwitch).Page);
    }

    [Fact]
    public void Launcher_holds_where_it_shows_and_its_buttons()
    {
        Assert.Equal([SettingsIndex.LauncherPlace, SettingsIndex.LauncherButtons],
            SettingsIndex.EntriesOn(SettingsPage.Launcher).Select(e => e.Anchor));
        Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskLauncher));
        Assert.Equal(["Edit buttons…"], SettingsNames.RetiredNames("Edit buttons…"));
        Assert.Equal("Launcher settings…", SettingsNames.LinkTitle(SettingsPage.Launcher));
    }

    [Fact]
    public void The_Desk_page_runs_in_the_specs_order()
    {
        var marks = Regex.Matches(TabRange("Desk"), "views:SettingsAnchor\\.Id=\"([\\w-]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["desk", "arrange", "map", "pieces", "desk-clicks", "signin-reminders", "desk-designs", "desk-advanced", "margins", "desk-oled"], marks);
        Assert.DoesNotContain("signin-reminders", TabRange("Alerts"));
    }

    [Fact]
    public void What_sits_in_Designs_and_Advanced_says_so()
    {
        Assert.Equal(SettingsNames.DeskAdvanced, SettingsIndex.Search("margins")[0].Entry.CollapsedIn);
        Assert.Contains(SettingsIndex.Search("OLED care"),
            h => h.Entry.Anchor == SettingsIndex.DeskOled && h.Entry.CollapsedIn == SettingsNames.DeskAdvanced);
        Assert.Contains(SettingsIndex.Search("let Claude change the Desk directly"),
            h => h.Entry.Anchor == SettingsIndex.DeskDesigns && h.Entry.CollapsedIn == SettingsNames.DeskDesigns);
        Assert.Contains(SettingsIndex.Search("put my desk back"),
            h => h.Entry.Anchor == SettingsIndex.DeskDesigns && h.Entry.CollapsedIn == SettingsNames.DeskDesigns);
        Assert.Null(SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskDesigns)!.CollapsedIn);    // the section's own row
        Assert.Null(SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskAdvanced)!.CollapsedIn);
    }

    [Fact]
    public void The_moved_Desk_links_land_in_their_new_sections()
    {
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskDesigns), SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskDirect));
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.SignInReminders), SettingsIndex.Resolve(SettingsPage.Alerts, SettingsIndex.SignInReminders));
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.SignInReminders).Page);
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Advanced"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Designs"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Behavior"));
    }
}
