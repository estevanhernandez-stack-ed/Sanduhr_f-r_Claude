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
    private string Sanduhr => Path.Combine(_tmp, "appdata", "Sanduhr");

    public McpConfigTests()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Sanduhr);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best effort */ }
    }

    private void MakeHome(string name) => Directory.CreateDirectory(Path.Combine(Home, name, "projects"));

    private void WriteConsent(params (string Key, bool On)[] entries)
    {
        var map = entries.ToDictionary(e => e.Key, e => e.On);
        File.WriteAllText(Path.Combine(Sanduhr, "settings.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { ["mcp_roots"] = map }));
    }

    private McpConfig Resolve(string? claudeConfigDir = null)
        => McpConfig.Resolve(Home, Sanduhr, Path.Combine(_tmp, "local"),
            n => n == "CLAUDE_CONFIG_DIR" ? claudeConfigDir : null);

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
        File.WriteAllText(Path.Combine(Sanduhr, "settings.json"), "{ not json");

        var cfg = Resolve();

        Assert.Equal(new[] { ".claude" }, cfg.RootsFound);
        Assert.Empty(cfg.ConsentedRoots);
    }
}
