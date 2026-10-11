using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public sealed class ModInventoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-modinv-" + Guid.NewGuid().ToString("N"));
    private readonly string _profile;
    private readonly string _folder;

    public ModInventoryTests()
    {
        _profile = Path.Combine(_root, "profile");
        _folder = Path.Combine(_profile, ".claude-test");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    private void Put(string relative, string content, string? baseDir = null)
    {
        var path = Path.Combine(baseDir ?? _folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private FolderReport Read() => ModInventory.Read(_folder, _profile);

    [Fact]
    public void MissingFolderIsAllMissing()
    {
        var r = ModInventory.Read(Path.Combine(_root, "nope"), _profile);
        Assert.False(r.Exists);
        Assert.Equal(SettingsKind.Missing, r.Settings.Kind);
        Assert.Null(r.Statusline);
        Assert.Empty(r.Hooks);
        Assert.Empty(r.EnabledPlugins);
        Assert.Empty(r.Mods);
        Assert.Empty(r.Mcp.UserServers);
        Assert.Equal(0, r.Mcp.LocalProjectCount);
        Assert.False(r.ClaudeMd.Exists);
        Assert.Null(r.Retention.CleanupPeriodDays);
        Assert.False(r.Retention.Warning);
    }

    [Fact]
    public void FolderWithoutSettingsIsMissingSettings()
    {
        var r = Read();
        Assert.True(r.Exists);
        Assert.Equal(SettingsKind.Missing, r.Settings.Kind);
    }

    [Fact]
    public void UnparseableSettingsReportThePosition()
    {
        Put("settings.json", "{\n  \"a\": 1,\n  \"b\": 2,\n}\n");
        var r = Read();
        Assert.Equal(SettingsKind.Unreadable, r.Settings.Kind);
        Assert.Equal(4, r.Settings.Line);
        Assert.True(r.Settings.Column >= 1);
        Assert.NotEmpty(r.Settings.Message);
        Assert.Null(r.Statusline);
        Assert.Empty(r.Hooks);
        Assert.False(r.Retention.Warning);
    }

    [Fact]
    public void CommentsMakeSettingsUnreadableNotEmpty()
    {
        Put("settings.json", "{\n  // hello\n  \"a\": 1\n}\n");
        var r = Read();
        Assert.Equal(SettingsKind.Unreadable, r.Settings.Kind);
        Assert.Equal(2, r.Settings.Line);
    }

    [Fact]
    public void StatuslineCommandIsMasked()
    {
        Put("settings.json", """
            {"statusLine":{"type":"command","command":"node show.js --api-key sk-live-abcdefghijkl"}}
            """);
        var s = Read().Statusline;
        Assert.NotNull(s);
        Assert.DoesNotContain("sk-live", s!.Command);
        Assert.Contains(ConfigMask.Masked, s.Command);
        Assert.False(s.IsSanduhr);
    }

    [Fact]
    public void SanduhrStatuslineIsRecognized()
    {
        Put("settings.json", """
            {"statusLine":{"type":"command","command":"powershell -File C:\\Users\\x\\AppData\\Roaming\\Sanduhr\\sanduhr-statusline.ps1"}}
            """);
        Assert.True(Read().Statusline!.IsSanduhr);
    }

    [Fact]
    public void HooksAreCountedPerEventWithSanduhrsMarked()
    {
        Put("settings.json", """
            {"hooks":{
              "Notification":[
                {"matcher":"permission_prompt","hooks":[{"type":"command","command":"powershell -c \"ni $env:APPDATA\\Sanduhr\\glow-waiting.signal\""}]},
                {"hooks":[{"type":"command","command":"echo hi"}]}],
              "Stop":[
                {"hooks":[{"type":"command","command":"powershell -c touch glow-done.signal"}]}],
              "PreToolUse":[]
            }}
            """);
        var hooks = Read().Hooks.ToDictionary(h => h.Event);
        Assert.Equal(new HookEventSummary("Notification", 2, 1), hooks["Notification"]);
        Assert.Equal(new HookEventSummary("Stop", 1, 1), hooks["Stop"]);
        Assert.Equal(new HookEventSummary("PreToolUse", 0, 0), hooks["PreToolUse"]);
    }

    [Fact]
    public void PluginDirsModsAreScanned()
    {
        var mod = Path.Combine(_root, "mods", "sanduhr-meters");
        Put(".claude-plugin/plugin.json", """{"name":"sanduhr-meters","version":"1.2.0","description":"Meters"}""", mod);
        Put("hooks/hooks.json", """{"modules":["./src/index.ts"]}""", mod);
        Put("src/index.ts", "$.ui.status({ text: 'x' }); $.fs.read('a'); on('command.run', { command: 'meters' }, f);", mod);
        Put("src/index.test.ts", "$.http.fetch('x'); $.process.run('y');", mod);
        Put("types/api.d.ts", "$.process.spawn", mod);

        var plain = Path.Combine(_root, "mods", "plain");
        Put(".claude-plugin/plugin.json", """{"name":"plain"}""", plain);
        Put("commands/hello.md", "hi", plain);

        var gone = Path.Combine(_root, "mods", "gone");
        Put("settings.json", $$$"""
            {"env":{"CLAUDE_CODE_PLUGIN_DIRS":"{{{mod.Replace("\\", "\\\\")}}};{{{plain.Replace("\\", "\\\\")}}};{{{gone.Replace("\\", "\\\\")}}}"},
             "enabledPlugins":{"plain@inline":false}}
            """);

        var mods = Read().Mods;
        Assert.Equal(3, mods.Count);
        var m = mods[0];
        Assert.Equal("sanduhr-meters", m.Name);
        Assert.True(m.IsSanduhr);
        Assert.True(m.IsMod);
        Assert.Equal("1.2.0", m.Version);
        Assert.Equal(ModOriginKind.PluginDirs, m.Origin);
        Assert.Equal(ModState.On, m.State);
        Assert.Contains(ModSurface.Status, m.Touches.Surfaces);
        Assert.Contains(ModSurface.Commands, m.Touches.Surfaces);
        Assert.Contains(ModCapability.Files, m.Touches.Capabilities);
        Assert.DoesNotContain(ModCapability.Http, m.Touches.Capabilities);
        Assert.DoesNotContain(ModCapability.Process, m.Touches.Capabilities);
        Assert.Equal(["meters"], m.Touches.Commands);

        var p = mods[1];
        Assert.False(p.IsSanduhr);
        Assert.False(p.IsMod);
        Assert.Equal(ModState.Off, p.State);
        Assert.Equal(["hello"], p.Touches.Commands);
        Assert.Contains(ModSurface.Commands, p.Touches.Surfaces);

        Assert.Equal(ModState.Missing, mods[2].State);
        Assert.Equal("gone", mods[2].Name);
    }

    [Fact]
    public void InstalledPluginsCombineRecordsAndEnabledKeys()
    {
        var install = Path.Combine(_root, "cache", "tool");
        Put(".claude-plugin/plugin.json", """{"name":"tool","version":"2.0.0","description":"A tool"}""", install);
        Put("hooks/hooks.json", """{"hooks":{"PreToolUse":[{"hooks":[{"type":"command","command":"x"}]}]}}""", install);
        Put(".mcp.json", "{}", install);
        Put("plugins/installed_plugins.json", $$$"""
            {"version":2,"plugins":{"tool@market":[{"scope":"user","installPath":"{{{install.Replace("\\", "\\\\")}}}","version":"1.9.0"}],
                                    "dead@market":[{"installPath":"{{{Path.Combine(_root, "absent").Replace("\\", "\\\\")}}}","version":"0.1.0"}]}}
            """);
        Put("settings.json", """
            {"enabledPlugins":{"tool@market":true,"dead@market":true,"synced@claude-ai":false,"x@inline":true}}
            """);

        var r = Read();
        var byName = r.Mods.ToDictionary(m => m.Name);
        Assert.Equal(3, byName.Count);

        var tool = byName["tool"];
        Assert.Equal(ModOriginKind.Installed, tool.Origin);
        Assert.Equal("market", tool.Marketplace);
        Assert.Equal("tool@market", tool.Key);
        Assert.Equal("2.0.0", tool.Version);
        Assert.Equal(ModState.On, tool.State);
        Assert.False(tool.IsMod);
        Assert.Contains(ModCapability.Process, tool.Touches.Capabilities);

        Assert.Equal(ModState.Missing, byName["dead"].State);

        var synced = byName["synced"];
        Assert.Null(synced.Path);
        Assert.Equal(ModState.Off, synced.State);
        Assert.Equal("claude-ai", synced.Marketplace);

        Assert.True(r.EnabledPlugins["tool@market"]);
        Assert.False(r.EnabledPlugins["synced@claude-ai"]);
    }

    [Fact]
    public void SkillsDirPluginsAreListed()
    {
        Put("skills/mine/.claude-plugin/plugin.json", """{"name":"mine"}""");
        Put("skills/notaplugin/readme.md", "x");
        var mods = Read().Mods;
        var only = Assert.Single(mods);
        Assert.Equal("mine", only.Name);
        Assert.Equal(ModOriginKind.SkillsDir, only.Origin);
        Assert.Equal("mine@skills-dir", only.Key);
    }

    [Fact]
    public void McpNamesOnlyAndLocalScopeCount()
    {
        Put(".claude.json", """
            {"oauthAccount":{"emailAddress":"a@b.c"},
             "mcpServers":{"zeta":{"command":"x","env":{"K":"secret-value"}},"alpha":{"type":"http","url":"https://u:p@h/"}},
             "projects":{
               "C:/one":{"mcpServers":{"p1":{"command":"y"}}},
               "C:/two":{"mcpServers":{}},
               "C:/three":{"allowedTools":[]},
               "C:/four":{"mcpServers":{"p2":{},"p3":{}}}}}
            """);
        var mcp = Read().Mcp;
        Assert.Equal(["alpha", "zeta"], mcp.UserServers);
        Assert.Equal(2, mcp.LocalProjectCount);
        Assert.DoesNotContain("C:/", string.Join("|", mcp.UserServers));
    }

    [Fact]
    public void UnreadableMcpConfigDegradesToNothing()
    {
        Put(".claude.json", "{ not json");
        var mcp = Read().Mcp;
        Assert.Empty(mcp.UserServers);
        Assert.Equal(0, mcp.LocalProjectCount);
    }

    [Fact]
    public void ClaudeMdSizes()
    {
        Put("CLAUDE.md", "one\ntwo\nthree");
        var a = Read().ClaudeMd;
        Assert.True(a.Exists);
        Assert.Equal(3, a.Lines);
        Assert.Equal(13, a.Bytes);

        Put("CLAUDE.md", "one\ntwo\n");
        var b = Read().ClaudeMd;
        Assert.Equal(2, b.Lines);
        Assert.Equal(8, b.Bytes);

        File.Delete(Path.Combine(_folder, "CLAUDE.md"));
        Assert.False(Read().ClaudeMd.Exists);
    }

    [Fact]
    public void RetentionWarnsWhenUnset()
    {
        Put("settings.json", "{}");
        var unset = Read().Retention;
        Assert.Null(unset.CleanupPeriodDays);
        Assert.True(unset.Warning);
        Assert.Equal("Transcripts older than 30 days are deleted at the next start", unset.Message);

        Put("settings.json", """{"cleanupPeriodDays":90}""");
        var set = Read().Retention;
        Assert.Equal(90, set.CleanupPeriodDays);
        Assert.False(set.Warning);
        Assert.Null(set.Message);
    }

    [Fact]
    public void CredentialsFileIsNeverNeeded()
    {
        Put(".credentials.json", "{ this is not even json");
        Put("settings.json", "{}");
        var r = Read();
        Assert.Equal(SettingsKind.Readable, r.Settings.Kind);
    }
}
