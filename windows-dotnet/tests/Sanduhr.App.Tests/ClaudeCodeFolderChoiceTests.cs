using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>Which folder the Claude Code page's install combo opens on (the same order as Mods &amp; Config).</summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeFolderChoiceTests : AppWindowFixture
{
    private const string Off = """{"enabledPlugins":{"a@m":false}}""";
    private const string Many = """{"enabledPlugins":{"a@m":true,"b@m":true,"c@m":true}}""";

    public ClaudeCodeFolderChoiceTests() => ClaudeCodeViewModel.ForgetSessionChoice();

    private string Home(string name, string settings)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    private static string Selected(ClaudeCodeViewModel page) => page.SelectedFolder!.Display;

    [Fact]
    public void OpensOnTheFolderClaudeConfigDirNamesNotOnTheFirstFolder() => AppHost.Run(() =>
    {
        Home(".claude", Off);
        var personal = Home(".claude-personal", Many);
        Home(".claude-work", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        Assert.Equal(@"~\.claude-personal", Selected(new ClaudeCodeViewModel(DemoWidget())));
    });

    [Fact]
    public void WithoutTheVariableItOpensOnTheActiveAccountsFolder() => AppHost.Run(() =>
    {
        Home(".claude", Many);
        var work = Home(".claude-work", Off);
        var widget = DemoWidget();
        widget.SetAccountFolder(widget.ShownActiveAccount!, work);
        Assert.Equal(@"~\.claude-work", Selected(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void WithNeitherItOpensOnTheFolderWithTheMostPluginsOn() => AppHost.Run(() =>
    {
        Home(".claude", Off);
        Home(".claude-personal", Many);
        Assert.Equal(@"~\.claude-personal", Selected(new ClaudeCodeViewModel(DemoWidget())));
    });

    [Fact]
    public void WithNothingOnAnywhereItOpensOnTheFirstFolder() => AppHost.Run(() =>
    {
        Home(".claude", Off);
        Home(".claude-personal", Off);
        Assert.Equal(@"~\.claude", Selected(new ClaudeCodeViewModel(DemoWidget())));
    });

    [Fact]
    public void YourPickIsRememberedForTheSessionEvenInANewPage() => AppHost.Run(() =>
    {
        Home(".claude", Off);
        var personal = Home(".claude-personal", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        var widget = DemoWidget();
        var page = new ClaudeCodeViewModel(widget);
        page.SelectedFolder = page.Folders.Single(f => f.Display == @"~\.claude");

        Assert.Equal(@"~\.claude", Selected(new ClaudeCodeViewModel(widget)));
        ClaudeCodeViewModel.ForgetSessionChoice();
        Assert.Equal(@"~\.claude-personal", Selected(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void ReloadingKeepsTheFolderYouAreOn() => AppHost.Run(() =>
    {
        Home(".claude", Off);
        var personal = Home(".claude-personal", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        var page = new ClaudeCodeViewModel(DemoWidget());
        page.SelectedFolder = page.Folders.Single(f => f.Display == @"~\.claude");
        page.Reload();
        Assert.Equal(@"~\.claude", Selected(page));
    });
}
