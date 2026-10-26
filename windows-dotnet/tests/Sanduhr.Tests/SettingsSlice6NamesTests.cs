using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: Privacy's and About, Updates' copy. The sentences privacy rests on are pinned word for word, and every
/// number comes from the code that acts on it.</summary>
public class SettingsSlice6NamesTests
{
    [Fact]
    public void The_spec_words_are_the_specs()
    {
        Assert.Equal("Privacy", SettingsNames.PrivacyTitle);
        Assert.Equal("What leaves this PC", SettingsNames.WhatLeavesThisPc);
        Assert.Equal("What Claude can change", SettingsNames.WhatClaudeCanChange);
        Assert.Equal("Check for updates once a day", SettingsNames.CheckForUpdatesDaily);
        Assert.Equal("Check now", SettingsNames.CheckNow);
        Assert.Equal("The Microsoft Store keeps Sanduhr up to date.", SettingsNames.StoreKeepsUpToDate);
        Assert.StartsWith("Claude means any agent that uses Sanduhr's MCP server.", SettingsNames.AgentsIntro);
    }

    [Fact]
    public void Nothing_else_is_qualified_by_from_Sanduhr()
    {
        // Decision 3: WebView2 and the person's browser can reach the network on their own, so the sentence says whose calls it counts.
        Assert.Equal("Nothing else leaves this PC from Sanduhr. Sanduhr has no analytics.", SettingsNames.NothingElseLeaves);
        Assert.Contains("SmartScreen", SettingsNames.NetWebView2Note);
        // P11: the runtime can be missing on Windows 10, so it is not "part of Windows".
        Assert.DoesNotContain("part of Windows", SettingsNames.NetWebView2Note);
        Assert.Contains("(the Edge runtime apps use)", SettingsNames.NetWebView2Note);
        Assert.Contains("your own browser", SettingsNames.NetYourBrowserNote);
    }

    [Fact]
    public void Preflight_rulings_are_in_the_words()
    {
        // P4: the usage row names every ask and every cookie.
        Assert.StartsWith("When Sanduhr starts, after you sign in, every 5 minutes for the account in use, and when you refresh or switch accounts. A failed ask is tried again within seconds.", SettingsNames.NetClaudeUsageWhen);
        Assert.Contains("cf_clearance when Cloudflare issued one", SettingsNames.NetClaudeUsageCarries);
        Assert.Contains("the cookies claude.ai's own page sets", SettingsNames.NetClaudeUsageCarries);
        Assert.DoesNotContain("if you pasted one", SettingsNames.NetClaudeUsageCarries);
        // P5: the widget's own sign-in button opens the window too.
        Assert.Contains("the widget's own sign-in button", SettingsNames.NetSignInWhen);
        // P12: a new place or unit asks at once.
        Assert.EndsWith("and again at once when you change the place or the unit.", SettingsNames.NetWeatherWhen);
        // P6: what MCP answers carries paths and folder names, and says so in both places.
        foreach (var text in new[] { SettingsNames.AgentReadsCaption, SettingsNames.NetThroughClaudeCarries })
        {
            Assert.Contains("Launcher buttons", text);
            // ping's snapshot_path and get_desk's wallpaper copy are full paths under the Windows user folder.
            Assert.Contains("the paths of a small copy of your wallpaper and of Sanduhr's usage file, which include your Windows user folder", text);
            Assert.Contains("the names of the Claude Code folders Sanduhr found", text);
            Assert.Contains("plan", text);
        }
        // P17: the folder reads caption admits full paths.
        Assert.EndsWith("Project names go with them, or full project paths when Claude asks.", SettingsNames.AgentFolderReadsCaption);
        // P19: no "none of these work".
        Assert.DoesNotContain("None of these work", SettingsNames.AgentsIntro);
        Assert.Contains("each needs it installed", SettingsNames.AgentsIntro);
    }

    [Fact]
    public void No_timing_in_the_slice_6_block_is_a_literal()
    {
        // The words and the timers can't drift: every "N minutes" and "N hours" is built from a Core constant.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.Core", "SettingsNames.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var lines = File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Sanduhr.Core", "SettingsNames.cs"));
        var start = Array.FindIndex(lines, l => l.Contains("Settings v2, slice 6: Privacy", StringComparison.Ordinal));
        Assert.True(start >= 0);
        var literal = new System.Text.RegularExpressions.Regex(@"\d+ (second|minute|hour)s?");
        var bad = lines.Skip(start).Where(l => literal.IsMatch(l)).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void The_publish_row_says_project_names_leave()
    {
        Assert.Contains("so the project names in the folders you tick do leave this PC", SettingsNames.NetPublishCarries);
        Assert.Contains("this PC's name", SettingsNames.NetPublishCarries);
    }

    [Fact]
    public void Every_number_comes_from_the_code_that_acts_on_it()
    {
        Assert.Contains($"every {(int)UsageSchedule.Refresh.TotalMinutes} minutes for the account in use", SettingsNames.NetClaudeUsageWhen);
        Assert.Contains($"every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes", SettingsNames.NetClaudeUsageWhen);
        Assert.Contains($"every {(int)WeatherSchedule.Refresh.TotalMinutes} minutes", SettingsNames.NetWeatherWhen);
        Assert.Contains($"every {(int)UpdateCheck.Interval.TotalHours} hours", SettingsNames.NetUpdatesWhen);
        Assert.Contains($"every {(int)UpdateCheck.Interval.TotalHours} hours", SettingsNames.UpdatesCaption);
        Assert.Contains($"every {(int)StoreUpdate.CheckInterval.TotalHours} hours", SettingsNames.NetStoreWhen);
    }

    [Fact]
    public void The_MCP_status_counts_folders_in_words()
    {
        Assert.Equal("Sanduhr found no Claude Code folder, so its MCP server isn't installed in one.", SettingsNames.McpInstalledStatus(0, 0));
        Assert.Equal("Sanduhr's MCP server isn't installed in any Claude Code folder.", SettingsNames.McpInstalledStatus(0, 2));
        Assert.Equal("Sanduhr's MCP server is installed in 1 of 2 Claude Code folders.", SettingsNames.McpInstalledStatus(1, 2));
        Assert.Equal("Sanduhr's MCP server is installed in your Claude Code folder.", SettingsNames.McpInstalledStatus(1, 1));
        Assert.Equal("Sanduhr's MCP server is installed in all 3 Claude Code folders.", SettingsNames.McpInstalledStatus(3, 3));
    }

    [Fact]
    public void Consent_states_read_in_words()
    {
        Assert.Equal(("On", "Off", "Asks you first", "No switch"),
            (SettingsNames.AgentOn, SettingsNames.AgentOff, SettingsNames.AgentAsksFirst, SettingsNames.AgentNoSwitch));
        Assert.Equal("On, with Publish daily at", SettingsNames.AgentFollowsPublish(true));
        Assert.Equal("Off, with Publish daily at", SettingsNames.AgentFollowsPublish(false));
        Assert.Equal("No Claude Code folder found.", SettingsNames.AgentFolders([]));
        Assert.Equal(@"On for ~\.claude. Off for ~\.claude-work and ~\.claude-x.",
            SettingsNames.AgentFolders([(@"~\.claude", true), (@"~\.claude-work", false), (@"~\.claude-x", false)]));
        Assert.Equal(@"Off for ~\.claude.", SettingsNames.AgentFolders([(@"~\.claude", false)]));
    }

    [Fact]
    public void Check_now_says_what_it_found()
    {
        Assert.Equal("Sanduhr 4.0.1 is ready. Restart to update is in the tray menu and on the widget.", SettingsNames.UpdateReady("4.0.1"));
        Assert.Equal("Check now asks once, even with the switch off.", SettingsNames.CheckNowCaption);
    }

    [Fact]
    public void Hosts_that_are_prose_are_pinned_word_for_word()
    {
        // P4, P10, P13: Hosts that read as sentences live here, not in NetworkDisclosure.
        Assert.Equal("claude.ai (and what claude.ai's page loads)", SettingsNames.NetClaudeUsageHosts);
        Assert.Equal("claude.ai, and Google's sign-in if you choose it", SettingsNames.NetSignInHosts);
        Assert.Equal("api.github.com, github.com and GitHub's download hosts", SettingsNames.NetUpdatesHosts);
        Assert.Equal("the Microsoft Store, through Windows", SettingsNames.NetStoreHosts);
        Assert.Equal("the endpoint URL you set (and any address it redirects to)", SettingsNames.NetPublishHosts);
        Assert.Equal("the service Claude Code sends your conversation to", SettingsNames.NetThroughClaudeHosts);
        foreach (var text in new[]
        {
            SettingsNames.NetClaudeUsageHosts, SettingsNames.NetSignInHosts, SettingsNames.NetUpdatesHosts, SettingsNames.NetStoreHosts,
            SettingsNames.NetPublishHosts, SettingsNames.NetThroughClaudeHosts,
        })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.Empty(SettingsNames.UnknownLinks(text));
        }
    }

    [Fact]
    public void Slice_6_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.PrivacyLine, SettingsNames.NetClaudeUsageWhen, SettingsNames.NetClaudeUsageCarries, SettingsNames.NetClaudeUsageOff,
            SettingsNames.NetSignInWhen, SettingsNames.NetSignInCarries, SettingsNames.NetSignInOff, SettingsNames.NetWeatherWhen,
            SettingsNames.NetWeatherCarries, SettingsNames.NetWeatherOff, SettingsNames.NetCatalogWhen, SettingsNames.NetCatalogCarries,
            SettingsNames.NetCatalogOff, SettingsNames.NetUpdatesWhen, SettingsNames.NetUpdatesCarries, SettingsNames.NetUpdatesOff,
            SettingsNames.NetStoreWhen, SettingsNames.NetStoreCarries, SettingsNames.NetStoreOff, SettingsNames.NetPublishWhen,
            SettingsNames.NetPublishCarries, SettingsNames.NetPublishOff, SettingsNames.NetPublishAgentWhen, SettingsNames.NetPublishAgentCarries,
            SettingsNames.NetPublishAgentOff, SettingsNames.NetThroughClaudeWhen, SettingsNames.NetThroughClaudeCarries,
            SettingsNames.NetThroughClaudeOff, SettingsNames.NothingElseLeaves, SettingsNames.NetWebView2Note, SettingsNames.NetYourBrowserNote,
            SettingsNames.AgentsIntro, SettingsNames.AgentWatchersCaption, SettingsNames.AgentDeskDirectCaption, SettingsNames.AgentSongsDirectCaption,
            SettingsNames.AgentFolderReadsCaption, SettingsNames.AgentPublishCaption, SettingsNames.AgentThemeCaption, SettingsNames.AgentMessagesCaption,
            SettingsNames.AgentWidgetPacksCaption, SettingsNames.AgentReadsCaption, SettingsNames.PrivacyWhereLine, SettingsNames.UpdatesCaption,
            SettingsNames.CheckNowCaption, SettingsNames.UpdateChecking, SettingsNames.UpdateUpToDate, SettingsNames.UpdateNotInstalled,
            SettingsNames.UpdateFailed, SettingsNames.StoreKeepsUpToDate, SettingsNames.McpInstalledStatus(1, 2),
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.Empty(SettingsNames.UnknownLinks(text));
        }
    }
}
