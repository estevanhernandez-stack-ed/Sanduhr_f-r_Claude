using System.IO;

namespace Sanduhr.App.Tests;

/// <summary>Mods &amp; Config calls everything a plugin, tags the ones that are mods, and explains its counts.</summary>
[Collection("SettingsWindow")] // the session folder choice is static, shared with the window tests
public sealed class ModsConfigPluginsTests : ModsConfigFixture
{
    /// <summary>A home with: alpha (installed, on, a plain plugin), beta (installed, off), gamma (switched on, no files),
    /// meters (an inline mod from the plugin folders list, no switch) and a settings.json with three switches.</summary>
    private string Build()
    {
        var home = Home(".claude-personal");
        var alpha = Path.Combine(Root, "cache", "alpha");
        var beta = Path.Combine(Root, "cache", "beta");
        var meters = Path.Combine(Root, "inline", "meters");
        Put(alpha, ".claude-plugin/plugin.json", """{"name":"alpha","version":"1.2.0","description":"A plain plugin."}""");
        Put(beta, ".claude-plugin/plugin.json", """{"name":"beta","version":"0.1.0"}""");
        Put(meters, ".claude-plugin/plugin.json", """{"name":"meters","version":"0.3.0"}""");
        Put(meters, "hooks/hooks.json", """{"modules":["register.tsx"]}""");
        Put(home, "plugins/installed_plugins.json",
            "{\"version\":2,\"plugins\":{\"alpha@m\":[{\"installPath\":\"" + alpha.Replace("\\", "\\\\") + "\",\"version\":\"1.2.0\"}],"
            + "\"beta@m\":[{\"installPath\":\"" + beta.Replace("\\", "\\\\") + "\",\"version\":\"0.1.0\"}]}}");
        Put(home, "settings.json",
            "{\"enabledPlugins\":{\"alpha@m\":true,\"beta@m\":false,\"gamma@m\":true},"
            + "\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + meters.Replace("\\", "\\\\") + "\"}}");
        return home;
    }

    [Fact]
    public void TheListIsTitledPluginsWithInstalledAndOnCounts()
    {
        var page = Page(configDir: Build());
        // alpha (on), beta (off), meters (inline, on) have files; gamma is switched on but has none here.
        Assert.Equal("Plugins (3 installed, 2 on)", page.PluginListTitle);
        Assert.Equal(4, page.Mods.Count);
    }

    [Fact]
    public void TheSwitchSummaryCountsSettingsJsonAndTheNoteExplainsTheDifference()
    {
        var page = Page(configDir: Build());
        Assert.Equal("2 on, 1 off", page.PluginsSummary);
        Assert.StartsWith("settings.json has 3 plugin switches (2 on, 1 off). The list differs because", page.PluginListNote);
        Assert.Contains("1 has no switch in settings.json", page.PluginListNote);
        Assert.Contains("1 is switched in settings.json or listed as installed, but the files are not in this folder", page.PluginListNote);
    }

    [Fact]
    public void OnlyRealModsWearTheModTag()
    {
        var page = Page(configDir: Build());
        Assert.Equal(["meters"], page.Mods.Where(m => m.IsMod).Select(m => m.Name).ToArray());
        Assert.False(page.Mods.Single(m => m.Name == "alpha").IsMod);
    }

    [Fact]
    public void ARowSwitchedOnWithoutFilesSaysSo()
    {
        var page = Page(configDir: Build());
        Assert.Equal("On, no files here", page.Mods.Single(m => m.Name == "gamma").StateText);
        Assert.Equal("On", page.Mods.Single(m => m.Name == "alpha").StateText);
    }

    [Fact]
    public void APluginWithNoVersionShowsNoVersionButItsAccessibleNameSaysUnknown()
    {
        var home = Home(".claude-personal");
        var plugin = Path.Combine(Root, "cache", "tool");
        Put(plugin, ".claude-plugin/plugin.json", """{"name":"tool"}""");
        Put(home, "plugins/installed_plugins.json",
            "{\"version\":2,\"plugins\":{\"tool@m\":[{\"installPath\":\"" + plugin.Replace("\\", "\\\\") + "\",\"version\":\"unknown\"}]}}");
        Put(home, "settings.json", """{"enabledPlugins":{"tool@m":true}}""");

        var row = Assert.Single(Page(configDir: home).Mods);
        Assert.Equal("", row.VersionText);
        Assert.DoesNotContain("unknown", row.VersionText);
        Assert.Equal("tool, version unknown, on", row.AutomationName);
    }
}
