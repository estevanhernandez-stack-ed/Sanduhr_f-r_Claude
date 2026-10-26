using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>Which folder card a link to Claude Code, folder-config opens (slice 4, decision 5; the same order Mods &amp; Config's combo used).</summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeFolderChoiceTests : AppWindowFixture
{
    private const string Off = """{"enabledPlugins":{"a@m":false}}""";
    private const string Many = """{"enabledPlugins":{"a@m":true,"b@m":true,"c@m":true}}""";

    public ClaudeCodeFolderChoiceTests() => ClaudeCodeViewModel.ForgetSessionChoice();

    private static string Opened(ClaudeCodeViewModel page)
    {
        var card = page.OpenHoldsForLanding()!;
        Assert.True(card.HoldsOpen);
        Assert.Single(page.Folders, f => f.HoldsOpen);
        return card.Display;
    }

    [Fact]
    public void Opens_the_folder_CLAUDE_CONFIG_DIR_names_not_the_first() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        var personal = Home(".claude-personal", Many);
        Home(".claude-work", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void Without_the_variable_it_opens_the_active_accounts_folder() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Many);
        var work = Home(".claude-work", Off);
        var (widget, _) = ScratchWidget("Personal");
        widget.SetAccountFolder("Personal", work);
        Assert.Equal(@"~\.claude-work", Opened(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void With_neither_it_opens_the_folder_with_the_most_plugins_on() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        Home(".claude-personal", Many);
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void With_nothing_on_anywhere_it_opens_the_first() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        Home(".claude-personal", Off);
        Assert.Equal(@"~\.claude", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void The_folder_you_opened_last_wins_for_the_session_even_in_a_new_page() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        var (widget, _) = ScratchWidget("Personal");
        new ClaudeCodeViewModel(widget).Folders.Single(f => f.Display == @"~\.claude").HoldsOpen = true;

        Assert.Equal(@"~\.claude", Opened(new ClaudeCodeViewModel(widget)));
        ClaudeCodeViewModel.ForgetSessionChoice();
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void No_folders_opens_nothing() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), true);
        Assert.Null(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget).OpenHoldsForLanding());
    });
}
