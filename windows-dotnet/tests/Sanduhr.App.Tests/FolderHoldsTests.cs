using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A folder card's "What this folder holds" (slice 4): Mods &amp; Config's read-only report for one folder, read when opened.</summary>
[Collection("SettingsWindow")]
public sealed class FolderHoldsTests : ModsPageFixture
{
    private string Seeded()
    {
        var home = Home(".claude-personal",
            "{\"enabledPlugins\":{\"alpha@m\":true,\"beta@m\":false,\"gamma@m\":true},"
            + "\"statusLine\":{\"type\":\"command\",\"command\":\"node other.js\"},"
            + "\"hooks\":{\"Notification\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo hi\"}]}]},"
            + "\"cleanupPeriodDays\":45}");
        Put(home, ".claude.json", """{"mcpServers":{"files":{"command":"x"}},"projects":{"a":{"mcpServers":{"y":{}}}}}""");
        Put(home, "CLAUDE.md", "one\ntwo\n");
        return home;
    }

    [Fact]
    public void Nothing_is_read_until_the_section_is_opened()
    {
        var holds = new FolderHoldsViewModel(Host(), Seeded());
        Assert.False(holds.Loaded);
        Assert.Equal("", holds.SettingsSummary);

        holds.EnsureLoaded();

        Assert.True(holds.Loaded);
        Assert.Equal("Readable", holds.SettingsSummary);
    }

    [Fact]
    public void It_reports_the_folders_settings_statusline_hooks_switches_MCP_CLAUDE_md_and_retention()
    {
        var holds = new FolderHoldsViewModel(Host(), Seeded());
        holds.EnsureLoaded();

        Assert.Equal("Someone else's statusline", holds.StatuslineSummary);
        Assert.Equal("node other.js", holds.StatuslineCommand);
        Assert.Equal("1 group on 1 event", holds.HooksSummary);
        Assert.Equal("Notification, 1 group", Assert.Single(holds.HookLines).AutomationName);
        Assert.Equal("2 on, 1 off", holds.PluginsSummary);
        Assert.Equal(["alpha@m, On", "beta@m, Off", "gamma@m, On"], holds.PluginLines.Select(l => l.AutomationName));
        Assert.Equal("1 server", holds.McpSummary);
        Assert.Equal(["files"], holds.McpNames);
        Assert.Equal("1 project has their own servers.", holds.McpProjects);
        Assert.StartsWith("2 lines", holds.ClaudeMdSummary);
        Assert.Equal("45 days.", holds.RetentionSummary);
        Assert.False(holds.RetentionWarning);
    }

    [Fact]
    public void An_unreadable_settings_json_says_where_and_changes_nothing()
    {
        var home = Home(".claude", "{ \"a\": 1, }");
        var before = File.ReadAllBytes(SettingsOf(home));
        var holds = new FolderHoldsViewModel(Host(), home);
        holds.EnsureLoaded();

        Assert.True(holds.SettingsUnreadable);
        Assert.StartsWith("Unreadable: line 1", holds.SettingsSummary);
        Assert.Equal("Unknown while settings.json is unreadable.", holds.RetentionSummary);
        Assert.Equal(before, File.ReadAllBytes(SettingsOf(home)));
    }

    [Fact]
    public void Unset_retention_warns_in_the_words_of_Claude_Codes_rule()
    {
        var holds = new FolderHoldsViewModel(Host(), Home(".claude", "{}"));
        holds.EnsureLoaded();
        Assert.True(holds.RetentionWarning);
        Assert.Equal(ModInventory.RetentionWarning + ".", holds.RetentionSummary);
    }

    [Fact]
    public void Refresh_reads_the_folder_again()
    {
        var home = Home(".claude", "{}");
        var holds = new FolderHoldsViewModel(Host(), home);
        holds.EnsureLoaded();
        Assert.Equal("None", holds.McpSummary);

        Put(home, ".claude.json", """{"mcpServers":{"a":{},"b":{}}}""");
        holds.RefreshCommand.Execute(null);

        Assert.Equal("2 servers", holds.McpSummary);
        Assert.Equal(@"Refresh, read ~\.claude again", holds.RefreshName);
        Assert.Equal(SettingsNames.RefreshFolderNamed(@"~\.claude"), holds.RefreshName);
    }

    [Fact]
    public void The_host_reads_every_known_folder_with_its_meters_state()
    {
        Home(".claude", "{}");
        Home(".claude-work", "{ broken");
        var folders = Host().ReadFolders();
        Assert.Equal([@"~\.claude", @"~\.claude-work"], folders.Select(f => f.Display));
        Assert.Equal([FolderState.NotInstalled, FolderState.Unreadable], folders.Select(InstalledItems.MetersState));
    }

    [Fact]
    public void A_folder_whose_read_throws_still_lists_and_the_others_are_read()
    {
        Home(".claude", "{}");
        Home(".claude-bad", "{}");
        var host = Host() with { ReadReport = (folder, profile) => folder.EndsWith(".claude-bad", StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidOperationException("boom") : ModInventory.Read(folder, profile) };

        var folders = host.ReadFolders();

        Assert.Equal([@"~\.claude", @"~\.claude-bad"], folders.Select(f => f.Display));
        Assert.Equal([FolderState.NotInstalled, FolderState.Unreadable], folders.Select(InstalledItems.MetersState));
    }
}
