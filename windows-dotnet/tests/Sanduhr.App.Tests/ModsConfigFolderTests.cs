namespace Sanduhr.App.Tests;

/// <summary>Which folder Mods &amp; Config opens on.</summary>
[Collection("SettingsWindow")] // the session folder choice is static, shared with the window tests
public sealed class ModsConfigFolderTests : ModsConfigFixture
{
    private const string Off = """{"enabledPlugins":{"a@m":false}}""";
    private const string Many = """{"enabledPlugins":{"a@m":true,"b@m":true,"c@m":true}}""";

    [Fact]
    public void OpensOnTheFolderClaudeConfigDirNamesNotOnTheFirstFolder()
    {
        Home(".claude", Off);
        var personal = Home(".claude-personal", Many);
        Home(".claude-work", Many + "");
        var page = Page(configDir: personal);
        Assert.Equal(@"~\.claude-personal", page.SelectedFolder!.Display);
    }

    [Fact]
    public void WithoutTheEnvironmentVariableItOpensOnTheFolderLinkedToTheActiveAccount()
    {
        Home(".claude", Many);
        var work = Home(".claude-work", Off);
        var page = Page(activeAccountFolder: work);
        Assert.Equal(@"~\.claude-work", page.SelectedFolder!.Display);
    }

    [Fact]
    public void WithNeitherItOpensOnTheFolderWithTheMostPluginsOn()
    {
        Home(".claude", Off);
        Home(".claude-personal", Many);
        var page = Page();
        Assert.Equal(@"~\.claude-personal", page.SelectedFolder!.Display);
    }

    [Fact]
    public void WithNothingOnAnywhereItOpensOnTheFirstFolder()
    {
        Home(".claude", Off);
        Home(".claude-personal", Off);
        Assert.Equal(@"~\.claude", Page().SelectedFolder!.Display);
    }

    [Fact]
    public void YourPickIsRememberedForTheSessionEvenInANewPage()
    {
        Home(".claude", Off);
        var personal = Home(".claude-personal", Many);
        var page = Page(configDir: personal);
        page.SelectedFolder = page.Folders.Single(f => f.Display == @"~\.claude");

        var again = Page(configDir: personal);
        Assert.Equal(@"~\.claude", again.SelectedFolder!.Display);
    }

    [Fact]
    public void NoFoldersLeavesNothingSelected()
    {
        var page = Page();
        Assert.Null(page.SelectedFolder);
        Assert.False(page.HasFolders);
    }
}
