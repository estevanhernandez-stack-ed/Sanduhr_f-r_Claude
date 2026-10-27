using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A folder card's "What this folder holds" on Settings, Claude Code (slice 4): the read-only report for one folder, read when opened.</summary>
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
    public void A_refresh_that_fails_leaves_no_line_from_the_last_read()
    {
        // Slice 4 row 25: load once with a full report, then the read throws. Every summary goes empty; nothing from the last read stays.
        bool fail = false;
        var host = Host() with { ReadReport = (folder, profile) => fail ? throw new IOException("held") : ModInventory.Read(folder, profile) };
        var holds = new FolderHoldsViewModel(host, Seeded());
        holds.Refresh();
        Assert.NotEqual("", holds.StatuslineSummary);
        Assert.NotEqual("", holds.HooksSummary);
        Assert.NotEqual("", holds.PluginsSummary);
        Assert.NotEqual("", holds.McpSummary);

        fail = true;
        holds.Refresh();

        Assert.Equal(SettingsNames.HoldsCantRead, holds.SettingsSummary);
        Assert.True(holds.Loaded);
        Assert.Equal("", holds.SettingsDetail);
        Assert.Equal("", holds.StatuslineSummary);
        Assert.Equal("", holds.StatuslineCommand);
        Assert.Equal("", holds.HooksSummary);
        Assert.Equal("", holds.PluginsSummary);
        Assert.Equal("", holds.McpSummary);
        Assert.Equal("", holds.McpProjects);
        Assert.Equal("", holds.ClaudeMdSummary);
        Assert.Equal("", holds.RetentionSummary);
        Assert.False(holds.RetentionWarning);
        Assert.False(holds.SettingsUnreadable);
    }

    [Fact]
    public void A_failed_refresh_after_an_unreadable_read_clears_the_unreadable_flag()
    {
        bool fail = false;
        var host = Host() with { ReadReport = (folder, profile) => fail ? throw new IOException("held") : ModInventory.Read(folder, profile) };
        var holds = new FolderHoldsViewModel(host, Home(".claude", "{ \"a\": 1, }"));
        holds.Refresh();
        Assert.True(holds.SettingsUnreadable);

        fail = true;
        holds.Refresh();

        Assert.False(holds.SettingsUnreadable);
        Assert.Equal("", holds.SettingsDetail);
    }

    [Fact]
    public void A_missing_folder_and_Sanduhrs_own_statusline_have_their_lines()
    {
        // Slice 4 row 26: the two lines the earlier pins left out.
        var missing = new FolderHoldsViewModel(Host(), Home(".claude-nothing"));
        missing.Refresh();
        var ours = new FolderHoldsViewModel(Host(), Home(".claude-ours",
            "{\"statusLine\":{\"type\":\"command\",\"command\":\"powershell -File sanduhr-statusline.ps1\"}}"));
        ours.Refresh();

        Assert.Equal(SettingsNames.SettingsMissing, missing.SettingsSummary);
        Assert.Equal(SettingsNames.StatuslineSanduhrs, ours.StatuslineSummary);
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
