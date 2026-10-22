using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// The whole Settings window, realized page by page over an invented Claude Code profile and sample accounts,
/// asked for its UI Automation names the way Narrator does. No item in any list may be read out as a type name
/// ("Sanduhr.App.ViewModels.ModRowViewModel") or a record's text ("ModsLine { Name = ... }").
/// </summary>
[Collection("SettingsWindow")]
public sealed class SettingsWindowNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-names-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly string? _oldDev = Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", _oldDev);
        ClaudeCodeProfile.ProfileOverride = null;
        ClaudeCodeProfile.ConfigDirOverride = null;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    /// <summary>A name that is a type or a record's text rather than words for a person.</summary>
    internal static bool Leaks(string name) =>
        name.Contains("ViewModel") || name.Contains("Sanduhr.App") || name.Contains("Sanduhr.Core") || name.Contains("System.")
        || name.Contains("{ ") || name.Contains(" }") || name.Contains("<>z__");

    /// <summary>A home with plugins (one a mod), a hook and an MCP server, so Mods &amp; Config has rows to name.</summary>
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

    private static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    [Fact]
    public void NoPageReadsAsATypeNameOrARecord() => AppHost.Run(() =>
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", Path.Combine(_root, "dev"));
        var profile = Path.Combine(_root, "profile");
        Directory.CreateDirectory(Path.Combine(profile, ".claude", "projects"));
        SeedPersonalHome(Path.Combine(profile, ".claude-personal"));
        ClaudeCodeProfile.ProfileOverride = profile;
        ClaudeCodeProfile.ConfigDirOverride = "";

        var widget = new WidgetViewModel();
        widget.StartDemo();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Show();
        var leaks = new List<string>();
        var seen = new Dictionary<string, List<string>>();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = item;
                Pump();
                window.UpdateLayout();
                var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(window)).ToList();
                seen[item.Tag?.ToString() ?? "?"] = peers.Select(p => p.GetName() ?? "").ToList();
                foreach (var p in peers)
                {
                    var name = p.GetName() ?? "";
                    if (Leaks(name))
                        leaks.Add($"[{item.Tag}] {p.GetAutomationControlType()} '{name}' ({p.GetClassName()})");
                }
            }
        }
        finally { window.Close(); }
        Assert.True(leaks.Count == 0, string.Join("\n", leaks.Distinct()));
    });
}
