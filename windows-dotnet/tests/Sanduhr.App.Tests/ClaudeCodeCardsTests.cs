using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: each Claude Code folder card holds everything Sanduhr installs for that folder and acts on that folder only
/// (WF4, WF9, WF27; Review Focus 5). The install combo is gone.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeCardsTests : AppWindowFixture
{
    private string Home(string name, string settings = "{}")
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    private (ClaudeCodeViewModel Page, List<ConsentSpec> Asked) Page(Func<ConsentSpec, ConsentChoice?>? answer = null)
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new ClaudeCodeViewModel(widget);
        var asked = new List<ConsentSpec>();
        page.AskConsent = spec => { asked.Add(spec); return answer?.Invoke(spec); };
        return (page, asked);
    }

    private static readonly ConsentChoice Yes = new(StatuslineMode.Replace, [], "");

    private static CcFolderRowViewModel Card(ClaudeCodeViewModel page, string name) =>
        page.Folders.Single(f => f.Display == @"~\" + name);

    [Fact]
    public void Every_card_has_its_own_pieces_and_there_is_no_install_combo() => AppHost.Run(() =>
    {
        Home(".claude-personal");
        Home(".claude-work");
        var (page, _) = Page();

        Assert.Equal(3, page.Folders.Count);   // .claude (the fixture's), .claude-personal, .claude-work
        Assert.All(page.Folders, f =>
        {
            Assert.Equal("Statusline", f.Statusline.Title);
            Assert.Equal("MCP server", f.Mcp.Title);
            Assert.Equal(SettingsNames.ClaudeCodeGlowHook, f.Hooks.Title);
        });
        Assert.Null(typeof(ClaudeCodeViewModel).GetProperty("SelectedFolder"));
        Assert.Null(typeof(ClaudeCodeViewModel).GetProperty("Meters"));
    });

    [Fact]
    public void Each_cards_consent_names_its_own_folder_and_a_no_writes_nothing() => AppHost.Run(() =>
    {
        var work = Home(".claude-work");
        var before = File.ReadAllBytes(Path.Combine(work, "settings.json"));
        var (page, asked) = Page(answer: _ => null);

        Card(page, ".claude-work").Statusline.InstallCommand.Execute(null);
        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);

        Assert.Equal([@"~\.claude-work", @"~\.claude-work"], asked.Select(a => a.Folder));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(work, "settings.json")));
    });

    [Fact]
    public void The_glow_hook_installs_into_the_cards_folder_only() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal");
        var work = Home(".claude-work");
        var personalBefore = File.ReadAllBytes(Path.Combine(personal, "settings.json"));
        var (page, _) = Page(answer: _ => Yes);
        Assert.Equal(SettingsNames.GlowHookIn([]), page.GlowHookStatus);

        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);

        Assert.Contains("glow-waiting.signal", File.ReadAllText(Path.Combine(work, "settings.json")));
        Assert.Equal(personalBefore, File.ReadAllBytes(Path.Combine(personal, "settings.json")));
        Assert.True(Card(page, ".claude-work").Hooks.IsInstalled);
        Assert.False(Card(page, ".claude-personal").Hooks.IsInstalled);
        Assert.Equal(SettingsNames.GlowHookIn([@"~\.claude-work"]), page.GlowHookStatus);
    });

    [Fact]
    public void A_card_removes_only_its_own_folders_glow_hook() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal");
        var work = Home(".claude-work");
        var (page, _) = Page(answer: _ => Yes);
        Card(page, ".claude-personal").Hooks.InstallCommand.Execute(null);
        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);
        var workBefore = File.ReadAllBytes(Path.Combine(work, "settings.json"));

        Card(page, ".claude-personal").Hooks.RemoveCommand.Execute(null);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(personal, "settings.json")));   // put back exactly
        Assert.Equal(workBefore, File.ReadAllBytes(Path.Combine(work, "settings.json")));
        Assert.Equal(SettingsNames.GlowHookIn([@"~\.claude-work"]), page.GlowHookStatus);
    });

    [Fact]
    public void A_card_for_a_folder_added_by_hand_offers_no_MCP_server() => AppHost.Run(() =>
    {
        var elsewhere = Path.Combine(Root, "elsewhere", "cc");
        Directory.CreateDirectory(elsewhere);
        var (widget, _) = ScratchWidget("Personal");
        widget.SaveCcFoldersAdded([elsewhere]);
        var page = new ClaudeCodeViewModel(widget);

        var card = page.Folders.Single(f => f.IsAdded);
        Assert.False(card.Mcp.CanInstall);
        Assert.Equal("Not available for folders added by hand.", card.Mcp.StatusText);
    });

    [Fact]
    public void The_meters_line_reads_each_folders_own_state() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var entryDir = Path.Combine(widget.SanduhrExternalDir, "mods", "v0.3.0-20261025120000", "sanduhr-meters");
        Directory.CreateDirectory(Path.Combine(entryDir, ".claude-plugin"));
        File.WriteAllText(Path.Combine(entryDir, ".claude-plugin", "plugin.json"), """{ "name": "sanduhr-meters", "version": "0.3.0" }""");
        var entry = entryDir.Replace("\\", "\\\\");
        Home(".claude-on", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + entry + "\"}}");
        Home(".claude-off", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + entry + "\"},\"enabledPlugins\":{\"sanduhr-meters@inline\":false}}");
        Home(".claude-broken", "{ nope");
        var page = new ClaudeCodeViewModel(widget);

        Assert.Equal(SettingsNames.FolderOn, Card(page, ".claude-on").MetersText);
        Assert.Equal(SettingsNames.FolderOff, Card(page, ".claude-off").MetersText);
        Assert.Equal(SettingsNames.FolderNotInstalled, Card(page, ".claude").MetersText);
        Assert.Equal(SettingsNames.FolderUnreadable, Card(page, ".claude-broken").MetersText);
        Assert.Equal("mods#" + SettingsIndex.Meters, Card(page, ".claude").MetersLink);
        Assert.StartsWith(SettingsNames.LinkTitle(SettingsPage.Mods), Card(page, ".claude").MetersLinkName);
    });

    [Fact]
    public void With_no_account_the_card_says_how_to_link_one() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        var page = new ClaudeCodeViewModel(widget);
        var card = Assert.Single(page.Folders);
        Assert.True(card.NoAccounts);

        var (signedIn, _) = ScratchWidget("Personal");
        Assert.True(Assert.Single(new ClaudeCodeViewModel(signedIn).Folders).HasAccounts);
    });

    [Fact]
    public void Opening_a_cards_holds_reads_that_folder_and_a_reload_keeps_it_open() => AppHost.Run(() =>
    {
        Home(".claude-work", """{"cleanupPeriodDays":12}""");
        var (page, _) = Page();
        var card = Card(page, ".claude-work");
        Assert.False(card.Holds.Loaded);

        card.HoldsOpen = true;
        Assert.Equal("12 days.", card.Holds.RetentionSummary);

        page.Reload();
        Assert.True(Card(page, ".claude-work").HoldsOpen);
        Assert.False(Card(page, ".claude").HoldsOpen);
    });

    [Fact]
    public void A_folder_whose_meters_files_are_gone_reads_Files_missing_not_On() => AppHost.Run(() =>
    {
        // P12: settings.json still lists the entry, but the folder it points at is gone. The same rule as Mods & Widgets' meters row.
        var (widget, _) = ScratchWidget("Personal");
        var gone = Path.Combine(widget.SanduhrExternalDir, "mods", "v0.1.0-20250101000000", "sanduhr-meters").Replace("\\", "\\\\");
        Home(".claude-gone", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + gone + "\"}}");
        var page = new ClaudeCodeViewModel(widget);

        Assert.Equal(SettingsNames.FolderMissing, Card(page, ".claude-gone").MetersText);
    });

    [Fact]
    public void The_page_follows_the_host_it_was_built_with_not_a_later_change_to_the_profile() => AppHost.Run(() =>
    {
        // P17: one source for the profile and CLAUDE_CONFIG_DIR. Changing the override after the page is built changes nothing in it.
        var personal = Home(".claude-personal", """{"enabledPlugins":{"a@m":true,"b@m":true}}""");
        var work = Home(".claude-work", """{"enabledPlugins":{"a@m":true,"b@m":true,"c@m":true}}""");
        ClaudeCodeProfile.ConfigDirOverride = personal;
        var (widget, _) = ScratchWidget("Personal");
        var page = new ClaudeCodeViewModel(widget);

        ClaudeCodeProfile.ConfigDirOverride = work;
        var row = page.OpenHoldsForLanding()!;

        Assert.Equal(@"~\.claude-personal", row.Display);
    });

    [Fact]
    public void An_install_buttons_help_names_this_folder_never_a_picker() => AppHost.Run(() =>
    {
        // P23: the install combo is gone, so the help text cannot say "the selected folder".
        var (page, _) = Page();
        var card = Card(page, ".claude");
        Assert.Equal(SettingsNames.PieceInstallHelp("Statusline"), card.Statusline.InstallHelp);
        Assert.Equal(SettingsNames.PieceInstallHelp("MCP server"), card.Mcp.InstallHelp);
        Assert.DoesNotContain("selected folder", card.Hooks.InstallHelp);
        Assert.EndsWith("to this folder.", card.Hooks.InstallHelp);
    });

    [Fact]
    public void An_unreadable_folder_offers_no_install_and_says_it_cannot_be_read() => AppHost.Run(() =>
    {
        Home(".claude-broken", "{ nope");
        var (page, _) = Page();
        var card = Card(page, ".claude-broken");

        Assert.False(card.Statusline.CanInstall);
        Assert.False(card.Hooks.CanInstall);
        Assert.Equal(SettingsNames.FolderUnreadable, card.MetersText);
        card.HoldsOpen = true;
        Assert.True(card.Holds.SettingsUnreadable);
        Assert.StartsWith("Unreadable", card.Holds.SettingsSummary);
        Assert.NotEqual("", card.Holds.SettingsDetail);
    });
}
