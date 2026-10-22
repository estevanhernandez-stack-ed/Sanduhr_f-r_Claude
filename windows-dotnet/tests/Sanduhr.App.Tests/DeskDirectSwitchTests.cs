using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Desk, "Let Claude change the Desk directly" (spec, the agent-designed Desk): off by default, saved at once.
/// What it does to a proposal is in <see cref="DeskDesignReviewTests"/>.</summary>
public sealed class DeskDirectSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-direct-" + Guid.NewGuid().ToString("N")[..8]);

    public DeskDirectSwitchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheSwitchIsOffByDefaultAndSavesAtOnce()
    {
        var paths = new Paths(appDataBase: _root);
        var store = new SettingsStore(paths);
        var page = new DeskSettingsViewModel(store, ["Segoe UI"]);
        Assert.False(page.DirectDesign);
        page.DirectDesign = true;
        Assert.True(store.LoadDeskDirect());
        Assert.Contains("\"desk_direct\":true", File.ReadAllText(paths.SettingsFile).Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        page.DirectDesign = false;
        Assert.False(store.LoadDeskDirect());
        Assert.DoesNotContain("desk_direct", File.ReadAllText(paths.SettingsFile));
    }

    [Fact]
    public void ChangingItElsewhereIsReadFreshEveryTime()
    {
        var paths = new Paths(appDataBase: _root);
        var store = new SettingsStore(paths);
        var page = new DeskSettingsViewModel(store, ["Segoe UI"]);
        store.SaveDeskDirect(true);
        Assert.True(page.DirectDesign);
    }

    [Fact]
    public void TheSwitchIsSearchableByWhatAPersonWouldTypeAndSitsOnTheDeskPage()
    {
        foreach (var query in new[] { "let Claude change the Desk directly", "propose_desk", "agents" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskDirect);
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.DeskDirectSwitch).Page);
    }
}
