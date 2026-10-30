using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Turning drawers off, removing one drawer, and the uninstall's return, in a sandbox (drawers plan 2, Task 10).</summary>
public sealed class DrawerTurnOffTests
{
    /// <summary>Every drawer gets a saved place, is hidden and is folded, so the tests can see those entries go with it.</summary>
    private static (DrawerService S, SettingsStore Store, Paths Paths) Make(DrawerSandbox box, params DrawerDef[] defs)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = defs } with
        {
            Places = defs.ToDictionary(d => d.Id, d => new DrawerPlace("m", 10, 20), StringComparer.Ordinal),
            Hidden = [.. defs.Select(d => d.Id)],
            Folded = [.. defs.Select(d => d.Id)],
        });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store, paths);
    }

    private static DrawerDef Owned(DrawerSandbox box, string name) => new(DrawerIds.From(name), name, Path.Combine(box.Drawers, name), true);

    [Fact]
    public void Turning_off_puts_every_file_back_removes_the_folders_and_clears_the_drawers() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, Owned(box, "Images"), Owned(box, "Documents"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.Dir(@"Desktop\Drawers\Documents\project");
        Assert.Equal("Put 2 files back on the Desktop?", DrawerReturn.Prompt(s.ReturnPlan()!.Moves.Count));
        var result = s.TurnOff();
        Assert.Equal("2 moved.", result.Run.Line);
        Assert.Equal("2 moved.", result.Line);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.True(Directory.Exists(Path.Combine(box.Desktop, "project")));
        Assert.False(Directory.Exists(box.Drawers));
        var after = store.LoadDrawers();
        Assert.False(after.Enabled);
        Assert.Empty(after.Drawers);
        Assert.Empty(after.Places);
        Assert.Empty(after.Hidden);
        Assert.Empty(after.Folded);
        s.Dispose();
    });

    [Fact]
    public void A_locked_file_keeps_its_drawer_listed_so_turning_off_again_finishes() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var docs = Owned(box, "Documents");
        var (s, store, _) = Make(box, Owned(box, "Images"), docs);
        box.File(@"Desktop\Drawers\Images\a.png");
        var locked = box.File(@"Desktop\Drawers\Documents\report.docx");
        DrawerReturnRun first;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            first = s.TurnOff();
        Assert.StartsWith("1 moved, 1 skipped: report.docx is open in ", first.Run.Line);
        Assert.Equal(2, first.KeptHomes.Count);   // the home and the root
        var kept = store.LoadDrawers();
        Assert.Equal(["documents"], kept.Drawers.Select(d => d.Id));
        Assert.Equal(["documents"], kept.Places.Keys);   // the kept drawer keeps its place, hidden word and fold; the returned one's go
        Assert.Equal(["documents"], kept.Hidden);
        Assert.Equal(["documents"], kept.Folded);
        Assert.False(kept.Enabled);
        var second = s.TurnOff();
        Assert.Equal("1 moved.", second.Run.Line);
        Assert.False(Directory.Exists(box.Drawers));
        var done = store.LoadDrawers();
        Assert.Empty(done.Drawers);
        Assert.Empty(done.Places);
        Assert.Empty(done.Hidden);
        Assert.Empty(done.Folded);
        s.Dispose();
    });

    [Fact]
    public void A_view_drawer_keeps_its_files_where_they_are() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var downloads = box.Dir("Downloads");
        var file = box.File(@"Downloads\setup.exe");
        var (s, _, _) = Make(box, new DrawerDef("downloads", "Downloads", downloads, Owned: false));
        s.TurnOff();
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(downloads));
        s.Dispose();
    });

    [Fact]
    public void A_hidden_file_in_a_drawer_goes_back_too() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, Owned(box, "Images"));
        var hidden = box.File(@"Desktop\Drawers\Images\secret.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        s.TurnOff();
        Assert.True(File.Exists(Path.Combine(box.Desktop, "secret.txt")));
        Assert.False(Directory.Exists(box.Drawers));
        s.Dispose();
    });

    [Fact]
    public void Removing_one_owned_drawer_returns_its_files_and_leaves_the_others() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, Owned(box, "Images"), Owned(box, "Documents"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Documents\b.pdf");
        s.Remove("images");
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Documents", "b.pdf")));
        var after = store.LoadDrawers();
        Assert.Equal(["documents"], after.Drawers.Select(d => d.Id));
        Assert.Equal(["documents"], after.Places.Keys);
        Assert.Equal(["documents"], after.Hidden);
        Assert.Equal(["documents"], after.Folded);
        s.Dispose();
    });

    [Fact]
    public void Removing_a_drawer_with_a_locked_file_keeps_it_listed_with_its_place() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, Owned(box, "Images"));
        var locked = box.File(@"Desktop\Drawers\Images\a.png");
        DrawerReturnRun run;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            run = s.Remove("images");
        Assert.Single(run.KeptHomes);
        var after = store.LoadDrawers();
        Assert.Equal(["images"], after.Drawers.Select(d => d.Id));
        Assert.Equal(["images"], after.Places.Keys);
        s.Dispose();
    });

    [Fact]
    public void The_uninstall_return_puts_files_back_and_stops_when_its_time_is_up() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, paths) = Make(box, Owned(box, "Images"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        s.Dispose();
        var places = new DrawerPlaces(box.Desktop, box.Public, []);
        var none = DrawerUninstall.Run(paths, places, TimeSpan.Zero)!;
        Assert.Equal(2, none.Run.Skipped.Count(k => k.Reason == DrawerSkip.NotInTime));
        var all = DrawerUninstall.Run(paths, places, TimeSpan.FromSeconds(15))!;
        Assert.Equal(2, all.Run.Moved.Count);
        Assert.False(Directory.Exists(box.Drawers));
    });
}
