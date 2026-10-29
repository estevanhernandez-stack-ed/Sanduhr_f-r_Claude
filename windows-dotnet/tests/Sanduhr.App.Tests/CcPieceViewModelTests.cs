using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>The Claude Code page's piece cards: the install button says what it will do once the piece is already in the folder.</summary>
public class CcPieceViewModelTests
{
    private static CcPieceViewModel Piece(string title, string verb = "Update…") =>
        new(title, "does a thing", () => { }, () => { }, verb);

    [Theory]
    [InlineData("MCP server")]
    [InlineData("Claude Code glow hook")]
    [InlineData("Meters above the prompt")]
    public void AnAbsentPieceOffersInstallAndAnInstalledOneOffersUpdate(string title)
    {
        var piece = Piece(title);
        Assert.Equal("Install…", piece.InstallLabel);
        Assert.Equal($"Install {title}", piece.InstallName);

        piece.IsInstalled = true;
        Assert.Equal("Update…", piece.InstallLabel);
        Assert.Equal($"Update {title}", piece.InstallName);
        Assert.NotEqual("Install…", piece.InstallLabel);

        piece.IsInstalled = false;
        Assert.Equal("Install…", piece.InstallLabel);
    }

    [Fact]
    public void TheStatuslineKeepsItsChangeVerb()
    {
        var piece = Piece("Statusline", "Change…");
        Assert.Equal("Install…", piece.InstallLabel);
        piece.IsInstalled = true;
        Assert.Equal("Change…", piece.InstallLabel);
        Assert.Equal("Change Statusline", piece.InstallName);
    }

    [Fact]
    public void TheLabelNamesAndHelpTextFollowTheStateToTheView()
    {
        var piece = Piece("MCP server");
        var changed = new List<string?>();
        piece.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        piece.IsInstalled = true;
        Assert.Contains(nameof(CcPieceViewModel.InstallLabel), changed);
        Assert.Contains(nameof(CcPieceViewModel.InstallName), changed);
        Assert.Contains(nameof(CcPieceViewModel.InstallHelp), changed);
        Assert.Contains("again", piece.InstallHelp);
        piece.IsInstalled = false;
        Assert.StartsWith("Asks first, then adds MCP server", piece.InstallHelp);
    }

    [Fact]
    public void RemoveIsNamedPerPiece()
    {
        Assert.Equal("Remove Meters above the prompt", Piece("Meters above the prompt").RemoveName);
    }
}
