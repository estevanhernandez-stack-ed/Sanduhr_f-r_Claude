using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/Paths.cs — ported from the locations encoded in
/// paths.py. The single source of truth for where Sanduhr reads and writes.
/// </summary>
public class PathsTests
{
    [Fact]
    public void App_data_dir_is_sanduhr_under_appdata_and_is_created()
    {
        using var temp = new TempDir();
        var paths = new Paths(temp.Path);
        var expected = Path.Combine(temp.Path, "Sanduhr");
        Assert.Equal(expected, paths.AppDataDir);
        Assert.True(Directory.Exists(expected)); // created on access
    }

    [Fact]
    public void History_file_locations_match_python_schema()
    {
        using var temp = new TempDir();
        var paths = new Paths(temp.Path);
        var dir = Path.Combine(temp.Path, "Sanduhr");
        Assert.Equal(Path.Combine(dir, "history.json"), paths.HistoryFile);
        Assert.Equal(Path.Combine(dir, "history.Personal.json"), paths.HistoryFileFor("Personal"));
        Assert.Equal(Path.Combine(dir, "settings.json"), paths.SettingsFile);
    }

    [Fact]
    public void Themes_and_sounds_dirs_live_under_appdata()
    {
        using var temp = new TempDir();
        var paths = new Paths(temp.Path);
        var dir = Path.Combine(temp.Path, "Sanduhr");
        Assert.Equal(Path.Combine(dir, "themes"), paths.ThemesDir);
        Assert.Equal(Path.Combine(dir, "sounds"), paths.SoundsDir);
    }

    [Fact]
    public void Legacy_v1_paths_live_under_home()
    {
        using var temp = new TempDir();
        var home = Path.Combine(temp.Path, "home");
        var paths = new Paths(temp.Path, home);
        Assert.Equal(Path.Combine(home, ".claude-usage-widget", "config.json"), paths.LegacyConfigFile);
        Assert.Equal(Path.Combine(home, ".claude-usage-widget", "history.json"), paths.LegacyHistoryFile);
    }

    [Fact]
    public void WebView2FetchDir_is_under_appdata_sanduhr()
    {
        using var temp = new TempDir();
        var p = new Paths(temp.Path);
        Assert.Equal(Path.Combine(temp.Path, "Sanduhr", "webview2-fetch"), p.WebView2FetchDir);
    }

    [Fact]
    public void VaultDir_lives_under_local_appdata_not_roaming()
    {
        using var roaming = new TempDir();
        using var home = new TempDir();
        using var local = new TempDir();
        var paths = new Paths(roaming.Path, home.Path, local.Path);
        Assert.Equal(Path.Combine(local.Path, "Sanduhr", "vault"), paths.VaultDir);
        Assert.StartsWith(local.Path, paths.VaultDir);
        Assert.False(paths.VaultDir.StartsWith(roaming.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void VaultDir_is_not_auto_created()
    {
        using var roaming = new TempDir();
        using var home = new TempDir();
        using var local = new TempDir();
        var paths = new Paths(roaming.Path, home.Path, local.Path);
        _ = paths.VaultDir;
        Assert.False(Directory.Exists(paths.VaultDir));
    }
}
