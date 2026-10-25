using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 3 (spec, "Desk: the split" and the pages it creates): the names, the index rows, the aliases that
/// go live and the jump links.</summary>
public class SettingsDeskSplitIndexTests
{
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
}
