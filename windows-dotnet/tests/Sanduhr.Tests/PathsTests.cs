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
}
