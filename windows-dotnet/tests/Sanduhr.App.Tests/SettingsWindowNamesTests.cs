using System.IO;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The whole Settings window, realized page by page over an invented Claude Code profile and sample accounts,
/// asked for its UI Automation names the way Narrator does. No item in any list may be read out as a type name
/// ("Sanduhr.App.ViewModels.ModRowViewModel") or a record's text ("ModsLine { Name = ... }").
/// </summary>
[Collection("SettingsWindow")]
public sealed class SettingsWindowNamesTests : AppWindowFixture
{
    /// <summary>A home with plugins (one a mod), a hook and an MCP server, so Mods &amp; Widgets and the folder card have rows to name.</summary>
    private static void SeedPersonalHome(string home)
    {
        Directory.CreateDirectory(Path.Combine(home, "projects"));
        var cache = Path.Combine(home, "plugins", "cache");
        void Plugin(string name, string version, bool mod)
        {
            var dir = Path.Combine(cache, name);
            Directory.CreateDirectory(Path.Combine(dir, ".claude-plugin"));
            File.WriteAllText(Path.Combine(dir, ".claude-plugin", "plugin.json"), "{\"name\":\"" + name + "\",\"version\":\"" + version + "\"}");
            if (!mod) return;
            Directory.CreateDirectory(Path.Combine(dir, "hooks"));
            File.WriteAllText(Path.Combine(dir, "hooks", "hooks.json"), "{\"modules\":[\"register.tsx\"]}");
        }
        Plugin("alpha", "1.2.0", false);
        Plugin("beta-mod", "0.1.0", true);
        string Rec(string n, string v) => "\"" + n + "@m\":[{\"installPath\":\"" + Path.Combine(cache, n).Replace("\\", "\\\\") + "\",\"version\":\"" + v + "\"}]";
        File.WriteAllText(Path.Combine(home, "plugins", "installed_plugins.json"),
            "{\"version\":2,\"plugins\":{" + Rec("alpha", "1.2.0") + "," + Rec("beta-mod", "0.1.0") + "}}");
        File.WriteAllText(Path.Combine(home, "settings.json"),
            "{\"enabledPlugins\":{\"alpha@m\":true,\"beta-mod@m\":false,\"ghost@m\":true},"
            + "\"hooks\":{\"Notification\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo hi\"}]}]}}");
        File.WriteAllText(Path.Combine(home, ".claude.json"), "{\"mcpServers\":{\"files\":{\"command\":\"x\"}}}");
    }

    [DesktopFact]
    public void NoPageReadsAsATypeNameOrARecord() => AppHost.Run(() =>
    {
        SeedPersonalHome(Path.Combine(Profile, ".claude-personal"));
        var widget = DemoWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        var leaks = new List<string>();
        var seen = new Dictionary<string, List<string>>();
        try
        {
            svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-personal").HoldsOpen = true;
            var tabs = (TabControl)window.FindName("Tabs");
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = item;
                var names = NamesOf(window);
                seen[item.Tag?.ToString() ?? "?"] = names;
                leaks.AddRange(LeaksIn(names).Select(n => $"[{item.Tag}] {n}"));
            }
        }
        finally { window.Close(); }
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));

        // The rows that did leak read as words now, and the seeded plugin rows are there to be named on Mods & Widgets.
        // Demo mode shows its own sample accounts whatever the machine's Credential Manager holds; the active one is marked.
        foreach (var account in DemoMode.Accounts)
            Assert.Contains(account == widget.ShownActiveAccount ? account + ", active" : account, seen["Accounts"]);
        Assert.Contains(seen["ClaudeCode"], n => n.StartsWith(@"Folder ~\.claude"));
        Assert.Contains("alpha, version 1.2.0, on in 1 of 2 folders, read-only", seen["Mods"]);
        Assert.Contains("beta-mod, version 0.1.0, on in 0 of 2 folders, mod, read-only", seen["Mods"]);
        // ghost is switched on in settings.json with no files behind it: on in that folder, its version unknown.
        Assert.Contains("ghost, version unknown, on in 1 of 2 folders, read-only", seen["Mods"]);
        Assert.Contains(@"~\.claude-personal, Not installed", seen["Mods"]);   // the meters row for the folder
        Assert.Contains("Notification, 1 group", seen["ClaudeCode"]);
        Assert.Contains("alpha@m, On", seen["ClaudeCode"]);
    });

    [Fact]
    public void The_first_page_of_each_group_carries_its_heading_from_the_names_table() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        try
        {
            var tabs = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().ToList();
            var firsts = SettingsNames.Sidebar.ToDictionary(g => g.Pages[0].ToString(), g => g.Title);
            foreach (var tab in tabs)
                Assert.Equal(firsts.GetValueOrDefault((string)tab.Tag), SettingsSidebar.GetGroupHeader(tab));
        }
        finally { window.Close(); }
    });
}
