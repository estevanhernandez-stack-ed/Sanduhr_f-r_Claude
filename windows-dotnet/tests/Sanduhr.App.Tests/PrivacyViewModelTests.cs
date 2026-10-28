using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: Privacy reads each consent from the store its own page writes (decision 10) and the MCP server's install
/// the way the Claude Code cards do (decision 11), on a visit and not at construction (P20).</summary>
[Collection("SettingsWindow")]
public sealed class PrivacyViewModelTests : AppWindowFixture
{
    private (PrivacyViewModel Vm, SettingsStore Pages, Dictionary<string, bool> Roots, WidgetViewModel Widget) Make(
        bool publishOn = false, Func<string, bool>? registered = null)
    {
        var (widget, _) = ScratchWidget("Personal");
        var pages = new SettingsStore(new Paths());   // where Watchers, Desk and Now playing write (the fixture's dev profile)
        var roots = new Dictionary<string, bool>(StringComparer.Ordinal);
        var vm = new PrivacyViewModel(pages, ClaudeCodeHost.For(widget), () => roots, () => publishOn, registered);
        return (vm, pages, roots, widget);
    }

    private static string StateOf(PrivacyViewModel vm, string id) =>
        vm.Agents[AgentConsents.All.ToList().FindIndex(c => c.Id == id)].State;

    [Fact]
    public void Every_switch_is_off_by_default_and_the_others_say_what_they_are() => AppHost.Run(() =>
    {
        var (vm, _, _, widget) = Make();
        try
        {
            Assert.Equal(AgentConsents.All.Count, vm.Agents.Count);
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.Watchers));
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.DeskDirect));
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.SongsDirect));
            Assert.Equal(SettingsNames.AgentFollowsPublish(false), StateOf(vm, AgentConsents.Publish));
            Assert.Equal(SettingsNames.AgentNoSwitch, StateOf(vm, AgentConsents.Theme));
            Assert.Equal(SettingsNames.AgentAsksFirst, StateOf(vm, AgentConsents.Messages));
            Assert.Same(NetworkDisclosure.All, vm.Network);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Each_switch_row_reads_what_its_own_page_wrote() => AppHost.Run(() =>
    {
        var (vm, pages, _, widget) = Make(publishOn: true);
        try
        {
            // Each value goes in through the page that owns the switch, not by writing the key.
            var desk = new DeskSettingsViewModel(pages, ["Segoe UI"]);
            desk.WatchersAgents = true;
            desk.DirectDesign = true;
            var songs = new NowPlayingPageViewModel(pages, null);
            songs.LooksDirect = true;
            // Publish usage: the page's service saves through SavePublishEnabled, and the page reads LoadPublishSettings.
            pages.SavePublishEnabled(true);
            Assert.True(pages.LoadPublishSettings().Enabled);

            vm.Reload();

            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.Watchers));
            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.DeskDirect));
            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.SongsDirect));
            Assert.Equal(SettingsNames.AgentFollowsPublish(true), StateOf(vm, AgentConsents.Publish));
            Assert.Equal($"{SettingsNames.WatchersSwitch}, {SettingsNames.AgentOn}", vm.Agents[0].ToString());
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_switch_turned_off_on_its_page_reads_off_on_the_next_visit() => AppHost.Run(() =>
    {
        var (vm, pages, _, widget) = Make();
        try
        {
            var desk = new DeskSettingsViewModel(pages, ["Segoe UI"]);
            desk.WatchersAgents = true;
            vm.Reload();
            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.Watchers));
            desk.WatchersAgents = false;
            vm.Reload();
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.Watchers));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_folder_row_reads_the_switch_the_Claude_Code_card_wrote() => AppHost.Run(() =>
    {
        var work = Home(".claude-work");
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var host = ClaudeCodeHost.For(widget);
            var vm = new PrivacyViewModel(new SettingsStore(new Paths()), host, widget.LoadMcpRoots, () => false);
            var page = new ClaudeCodeViewModel(widget);
            page.Folders.Single(f => f.Display == @"~\.claude").McpReadable = true;   // the card's own switch
            vm.Reload();

            Assert.Equal(SettingsNames.AgentFolders([(host.Display(Path.Combine(Profile, ".claude")), true), (host.Display(work), false)]),
                StateOf(vm, AgentConsents.FolderReads));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_folder_row_lists_each_found_folder_with_its_read_switch() => AppHost.Run(() =>
    {
        var work = Home(".claude-work");
        var (vm, _, roots, widget) = Make();
        try
        {
            var host = ClaudeCodeHost.For(widget);
            roots[".claude"] = true;
            vm.Reload();
            Assert.Equal(SettingsNames.AgentFolders([(host.Display(Path.Combine(Profile, ".claude")), true), (host.Display(work), false)]),
                StateOf(vm, AgentConsents.FolderReads));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_constructor_does_not_check_the_install_and_each_visit_does() => AppHost.Run(() =>
    {
        Home(".claude-work");
        var calls = 0;
        var (vm, _, _, widget) = Make(registered: _ => { calls++; return true; });
        try
        {
            Assert.Equal(0, calls);
            Assert.Equal("", vm.McpStatus);

            vm.Reload();
            Assert.Equal(2, calls);   // once per discovered folder
            Assert.Equal(SettingsNames.McpInstalledStatus(2, 2), vm.McpStatus);

            vm.Reload();
            Assert.Equal(4, calls);   // every visit looks again
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_MCP_status_counts_the_folders_holding_Sanduhrs_entry() => AppHost.Run(() =>
    {
        Home(".claude-work");
        var (vm, _, _, widget) = Make();
        try
        {
            vm.Reload();
            Assert.Equal(SettingsNames.McpInstalledStatus(0, 2), vm.McpStatus);

            var host = ClaudeCodeHost.For(widget);
            Assert.True(new McpIntegrationInstaller(host.Profile, host.SanduhrAppDataDir, externalDir: host.SanduhrExternalDir).Register(".claude").Ok);
            vm.Reload();
            Assert.Equal(SettingsNames.McpInstalledStatus(1, 2), vm.McpStatus);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_folder_that_cannot_be_read_does_not_count() => AppHost.Run(() =>
    {
        var (vm, _, _, widget) = Make(registered: _ => throw new IOException("locked"));
        try
        {
            vm.Reload();
            Assert.Equal(SettingsNames.McpInstalledStatus(0, 1), vm.McpStatus);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void With_no_Claude_Code_folder_both_lines_say_so() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var empty = Path.Combine(Root, "empty-profile");
            Directory.CreateDirectory(empty);
            var host = ClaudeCodeHost.For(widget) with { Profile = empty, ConfigDir = null, AddedFolders = () => [] };
            var vm = new PrivacyViewModel(new SettingsStore(new Paths()), host, () => new Dictionary<string, bool>(), () => false);
            vm.Reload();

            Assert.Equal(SettingsNames.McpInstalledStatus(0, 0), vm.McpStatus);
            Assert.Equal(SettingsNames.AgentFolders([]), StateOf(vm, AgentConsents.FolderReads));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_links_land_where_the_page_says() => AppHost.Run(() =>
    {
        var (vm, _, _, widget) = Make();
        try
        {
            Assert.Equal("claudecode#folders", vm.ClaudeCodeLink);
            Assert.Equal("about#where", vm.AboutLink);
            Assert.StartsWith(vm.ClaudeCodeLinkTitle + ", ", vm.McpStatusLinkName);
            Assert.StartsWith(vm.AboutLinkTitle + ", ", vm.AboutLinkName);
            Assert.All(vm.Agents, a => Assert.StartsWith(a.LinkTitle + ", ", a.LinkName));
        }
        finally { widget.Dispose(); }
    });
}
