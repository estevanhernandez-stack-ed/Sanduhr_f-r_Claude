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
}
