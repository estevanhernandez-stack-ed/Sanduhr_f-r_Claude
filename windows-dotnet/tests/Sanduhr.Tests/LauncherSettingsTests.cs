using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The Launcher in Settings search, the piece list and the Reset inventory.</summary>
public class LauncherSettingsTests
{
    [Theory]
    [InlineData("launcher")]
    [InlineData("button")]
    [InlineData("shortcut")]
    [InlineData("app")]
    [InlineData("Add app")]
    [InlineData("Add link")]
    public void SearchFindsTheLauncherButtons(string query) =>
        Assert.Contains(SettingsIndex.Search(query),
            h => h.Entry.Page == SettingsPage.Launcher && h.Entry.Anchor == SettingsIndex.LauncherButtons);

    [Fact]
    public void LauncherIsTheTopHitForItsOwnName()
    {
        var top = SettingsIndex.Search("launcher")[0];
        Assert.Equal((SettingsPage.Launcher, (string?)null), (top.Entry.Page, top.Entry.Anchor));   // the page itself
    }

    [Fact]
    public void TheAddButtonsAreInTheNamesTableOnTheLauncherPage()
    {
        Assert.Contains((SettingsNames.LauncherAddApp, SettingsPage.Launcher), SettingsNames.Table);
        Assert.Contains((SettingsNames.LauncherAddLink, SettingsPage.Launcher), SettingsNames.Table);
        Assert.Equal("Add app…", SettingsNames.LauncherAddApp);
        Assert.Equal("Add link…", SettingsNames.LauncherAddLink);
    }

    [Fact]
    public void TheLauncherIsAKnownPieceThatIsNotPlacedByDefault()
    {
        Assert.Contains("launcher", DeskArrangement.Known);
        Assert.Null(new DeskArrangement(DeskArrangement.Standard).Placement("launcher"));
        Assert.Equal("Launcher", SettingsNames.DeskPieceName("launcher"));
        var placed = new DeskArrangement("launcher:tr:1.2 clock:bl");
        Assert.Equal(DeskAnchor.TR, placed.Placement("launcher")!.Anchor);
        Assert.Equal(1.2, placed.Placement("launcher")!.Scale);
    }

    [Fact]
    public void TheSettingsKeyAndWidgetWordAreTheSpecsOnes()
    {
        Assert.Equal("desk_launcher", LauncherModel.Key);
        Assert.Equal("launcher", LauncherModel.Widget);
        Assert.Equal(8, LauncherModel.MaxButtons);
        Assert.Equal(24, LauncherButton.MaxLabel);
    }
}
