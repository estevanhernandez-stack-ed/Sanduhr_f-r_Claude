using System.Text.Json;

namespace Sanduhr.Mcp.Tests;

/// <summary>
/// Root resolution over temp-directory fixtures (never the real profile): roots are the
/// discovered Claude Code homes, and a root counts only when mcp_roots consents to its
/// discovered name. A consent key is never turned into a path.
/// </summary>
public sealed class McpConfigTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sanduhr-mcpcfg-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_tmp, "home");
    private string External => Path.Combine(_tmp, "home", ".sanduhr");
    private string AppDataSanduhr => Path.Combine(_tmp, "appdata", "Sanduhr");
    private string Local => Path.Combine(_tmp, "local");

    public McpConfigTests()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(External);
        Directory.CreateDirectory(AppDataSanduhr);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best effort */ }
    }

    private void MakeHome(string name) => Directory.CreateDirectory(Path.Combine(Home, name, "projects"));

    private void WriteConsent(params (string Key, bool On)[] entries)
    {
        var map = entries.ToDictionary(e => e.Key, e => e.On);
        File.WriteAllText(Path.Combine(External, "mcp-settings.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { ["schema_version"] = 1, ["mcp_roots"] = map }));
    }

    private McpConfig Resolve(string? claudeConfigDir = null, string? snapshotOverride = null, string? vaultOverride = null)
        => McpConfig.Resolve(Home, External, AppDataSanduhr, Local,
            n => n switch
            {
                "CLAUDE_CONFIG_DIR" => claudeConfigDir,
                "SANDUHR_SNAPSHOT_PATH" => snapshotOverride,
                "SANDUHR_VAULT_DIR" => vaultOverride,
                _ => null,
            });

    [Fact]
    public void Two_consented_homes_resolve_in_order()
    {
        MakeHome(".claude-work");
        MakeHome(".claude");
        WriteConsent((".claude", true), (".claude-work", true));

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude", ".claude-work" }, cfg.RootsFound);
        Assert.Equal(
            new[] { (".claude", Path.Combine(Home, ".claude")), (".claude-work", Path.Combine(Home, ".claude-work")) },
            cfg.ConsentedRoots);
    }

    [Fact]
    public void Unconsented_home_is_found_but_not_consented()
    {
        MakeHome(".claude");
        MakeHome(".claude-work");
        WriteConsent((".claude", true), (".claude-work", false));

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude", ".claude-work" }, cfg.RootsFound);
        Assert.Equal(new[] { ".claude" }, cfg.ConsentedRoots.Select(r => r.Name));
    }

    [Fact]
    public void Consent_key_without_a_folder_is_ignored()
    {
        MakeHome(".claude");
        WriteConsent((".claude", true), (".claude-ghost", true));

        var cfg = Resolve();

        Assert.DoesNotContain(".claude-ghost", cfg.RootsFound);
        Assert.Equal(new[] { ".claude" }, cfg.ConsentedRoots.Select(r => r.Name));
    }

    [Fact]
    public void Traversal_and_absolute_consent_keys_never_become_roots()
    {
        MakeHome(".claude");
        var outside = Path.Combine(_tmp, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "projects"));
        WriteConsent((".claude", true), ("..\\evil", true), (outside, true), ("..\\outside", true));

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude" }, cfg.ConsentedRoots.Select(r => r.Name));
        Assert.DoesNotContain(cfg.ConsentedRoots, r => r.Path.Contains("evil") || r.Path.Contains("outside"));
    }

    [Fact]
    public void Backup_folder_is_ignored_even_when_consented()
    {
        MakeHome(".claude");
        MakeHome(".claude-backup-1");
        WriteConsent((".claude", true), (".claude-backup-1", true));

        var cfg = Resolve();

        Assert.DoesNotContain(".claude-backup-1", cfg.RootsFound);
        Assert.Equal(new[] { ".claude" }, cfg.ConsentedRoots.Select(r => r.Name));
    }

    [Fact]
    public void Missing_settings_consents_nothing()
    {
        MakeHome(".claude");

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude" }, cfg.RootsFound);
        Assert.Empty(cfg.ConsentedRoots);
    }

    [Fact]
    public void Unparseable_settings_consents_nothing()
    {
        MakeHome(".claude");
        File.WriteAllText(Path.Combine(External, "mcp-settings.json"), "{ not json");

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude" }, cfg.RootsFound);
        Assert.Empty(cfg.ConsentedRoots);
    }

    [Fact]
    public void Agent_consent_reads_known_names_from_the_projection_only()
    {
        File.WriteAllText(Path.Combine(External, "mcp-settings.json"),
            """{"schema_version":1,"mcp_roots":{},"mcp_agents":{"codex":true,"antigravity":false,"other":true}}""");
        Assert.Equal(new[] { "codex" }, Resolve().ConsentedAgents);
    }

    [Fact]
    public void Consent_comes_from_the_projection_not_settings_json()
    {
        MakeHome(".claude");
        File.WriteAllText(Path.Combine(AppDataSanduhr, "settings.json"), """{"mcp_roots":{".claude":true}}""");

        Assert.Empty(Resolve().ConsentedRoots);
    }

    [Theory]
    [InlineData("""{"schema_version":2,"mcp_roots":{".claude":true}}""")]
    [InlineData("""{"schema_version":"1","mcp_roots":{".claude":true}}""")]
    [InlineData("""{"mcp_roots":{".claude":true}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"schema_version":1,"mcp_roots":[".claude"]}""")]
    public void Other_schema_or_shape_consents_nothing(string json)
    {
        MakeHome(".claude");
        File.WriteAllText(Path.Combine(External, "mcp-settings.json"), json);

        Assert.Empty(Resolve().ConsentedRoots);
    }

    [Fact]
    public void Paths_follow_the_external_folder()
    {
        var cfg = Resolve();

        Assert.Equal(Path.Combine(External, "snapshot.json"), cfg.SnapshotPath);
        Assert.Equal(Path.Combine(External, "mcp-settings.json"), cfg.McpSettingsPath);
        Assert.Equal(Path.Combine(External, "publish-request.json"), cfg.PublishRequestPath);
        Assert.Equal(Path.Combine(External, "publish-result.json"), cfg.PublishResultPath);
        Assert.Equal(Path.Combine(External, "theme-request.json"), cfg.ThemeRequestPath);
        Assert.Equal(Path.Combine(External, "theme-result.json"), cfg.ThemeResultPath);
    }

    [Fact]
    public void History_dir_is_appdata_without_the_override_and_follows_it_with_one()
    {
        Assert.Equal(AppDataSanduhr, Resolve().HistoryDir);

        string snap = Path.Combine(_tmp, "fixture", "snap.json");
        var cfg = Resolve(snapshotOverride: snap);
        Assert.Equal(snap, cfg.SnapshotPath);
        Assert.Equal(Path.Combine(_tmp, "fixture"), cfg.HistoryDir);
    }

    [Fact]
    public void Vault_dirs_are_cache_then_real_unless_overridden()
    {
        var cfg = Resolve();
        string real = Path.Combine(Local, "Sanduhr", "vault");
        string cache = Path.Combine(Local, "Packages", "626LabsLLC.SanduhrfrClaude_wz1chhb2h2v4a",
            "LocalCache", "Local", "Sanduhr", "vault");
        Assert.Equal(new[] { cache, real }, cfg.VaultDirs);
        Assert.Equal(real, cfg.VaultDir);

        string fixture = Path.Combine(_tmp, "vault-fixture");
        var over = Resolve(vaultOverride: fixture);
        Assert.Equal(new[] { fixture }, over.VaultDirs);
        Assert.Equal(fixture, over.VaultDir);
    }

    [Fact]
    public void Without_a_dev_profile_the_folders_are_the_real_ones()
    {
        var (external, appDataSanduhr, local) = McpConfig.Folders(@"C:\home", @"C:\roam", @"C:\loc", devProfile: null);
        Assert.Equal(Path.Combine(@"C:\home", ".sanduhr"), external);
        Assert.Equal(Path.Combine(@"C:\roam", "Sanduhr"), appDataSanduhr);
        Assert.Equal(@"C:\loc", local);

        Assert.Equal(external, McpConfig.Folders(@"C:\home", @"C:\roam", @"C:\loc", "  ").External);
    }

    [Fact]
    public void A_dev_profile_moves_every_folder_into_the_profile_as_Paths_does()
    {
        // #340: Core's Paths maps SANDUHR_DEV_PROFILE to <profile>\external, \roaming and \local;
        // the server must agree, or its handoff files land where the installed widget reads.
        var (external, appDataSanduhr, local) = McpConfig.Folders(@"C:\home", @"C:\roam", @"C:\loc", @"D:\dev");
        Assert.Equal(Path.Combine(@"D:\dev", "external"), external);
        Assert.Equal(Path.Combine(@"D:\dev", "roaming", "Sanduhr"), appDataSanduhr);
        Assert.Equal(Path.Combine(@"D:\dev", "local"), local);

        var cfg = McpConfig.Resolve(Home, external, appDataSanduhr, local, _ => null);
        Assert.Equal(Path.Combine(external, "widget-request.json"), cfg.WidgetRequestPath);
        Assert.Equal(Path.Combine(external, "snapshot.json"), cfg.SnapshotPath);
        Assert.DoesNotContain(".sanduhr", cfg.WidgetRequestPath);
    }
}
