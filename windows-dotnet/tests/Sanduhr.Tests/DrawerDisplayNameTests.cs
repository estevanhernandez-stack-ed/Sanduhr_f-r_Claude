using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerDisplayNameTests
{
    [Theory]
    [InlineData("Phasmophobia.lnk", "Phasmophobia")]
    [InlineData("Phasmophobia.LNK", "Phasmophobia")]
    [InlineData("Google Photos.url", "Google Photos")]
    [InlineData("Old tool.pif", "Old tool")]
    [InlineData("My.File.lnk", "My.File")]
    [InlineData("report.docx", "report.docx")]
    [InlineData("notes.lnk.txt", "notes.lnk.txt")]
    [InlineData("noextension", "noextension")]
    [InlineData(".lnk", ".lnk")]
    [InlineData("", "")]
    public void Display_hides_shortcut_url_and_pif_extensions_only(string name, string shown) =>
        Assert.Equal(shown, DrawerNames.Display(name));

    [Theory]
    [InlineData("Game.lnk", "Game 2", "Game 2.lnk")]
    [InlineData("Game.lnk", "Game 2.lnk", "Game 2.lnk")]
    [InlineData("Game.LNK", "Game 2", "Game 2.LNK")]
    [InlineData("site.url", "Site", "Site.url")]
    [InlineData("report.docx", "memo.docx", "memo.docx")]
    [InlineData("report.docx", "memo", "memo")]
    public void A_rename_keeps_the_hidden_extension_on_disk(string original, string typed, string onDisk) =>
        Assert.Equal(onDisk, DrawerNames.WithHiddenExtension(original, typed));

    [Fact]
    public void The_result_line_draws_skipped_shortcuts_without_their_extension()
    {
        var line = DrawerReview.ResultLine(2, [new DrawerSkipped("Game.lnk", DrawerSkip.CouldNotMove)]);
        Assert.Equal("2 moved, 1 skipped: Game could not be moved.", line);
    }
}
