using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Desk, "Show my CI runners' jobs" (spec ci-watchers-3.9): off by default, saved at once,
/// tells App only when the value really changes, and findable in Settings search.</summary>
public sealed class CiRunnersSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-cirunners-" + Guid.NewGuid().ToString("N")[..8]);

    public CiRunnersSwitchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Compact(string s) => s.Replace(" ", "").Replace("\r", "").Replace("\n", "");

    [Fact]
    public void TheSwitchIsOffByDefault()
    {
        var store = new SettingsStore(new Paths(appDataBase: _root));
        var page = new DeskSettingsViewModel(store, ["Segoe UI"]);
        Assert.False(page.WatchersCiRunners);
        Assert.False(store.LoadWatchersCiRunners());
    }

    [Fact]
    public void TurningItOnSavesAtOnceAndTurningItOffRemovesTheKey()
    {
        var paths = new Paths(appDataBase: _root);
        var store = new SettingsStore(paths);
        var page = new DeskSettingsViewModel(store, ["Segoe UI"]);
        page.WatchersCiRunners = true;
        Assert.True(new SettingsStore(paths).LoadWatchersCiRunners());
        Assert.Contains("\"watchers_ci_runners\":true", Compact(File.ReadAllText(paths.SettingsFile)));
        page.WatchersCiRunners = false;
        Assert.False(new SettingsStore(paths).LoadWatchersCiRunners());
        Assert.DoesNotContain("watchers_ci_runners", File.ReadAllText(paths.SettingsFile));
    }

    [Fact]
    public void ChangedFiresOncePerRealChangeAndNotForTheSameValue()
    {
        var store = new SettingsStore(new Paths(appDataBase: _root));
        var page = new DeskSettingsViewModel(store, ["Segoe UI"]);
        var fired = 0;
        page.CiRunnersChanged = () => fired++;
        page.WatchersCiRunners = false;
        Assert.Equal(0, fired);
        page.WatchersCiRunners = true;
        Assert.Equal(1, fired);
        page.WatchersCiRunners = true;
        Assert.Equal(1, fired);
        page.WatchersCiRunners = false;
        Assert.Equal(2, fired);
    }

    [Fact]
    public void TheSwitchIsSearchableAndSitsOnTheDeskPage()
    {
        foreach (var query in new[] { "show my CI runners' jobs", "runner", "GitHub Actions", "Docker" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskWatchers);
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.CiRunnersSwitch).Page);
    }
}
