using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Drawer state across redraws, listings, watchers, hand moves and holds, in a sandbox (drawers plan 2, Task 6).</summary>
public sealed class DrawerServiceTests
{
    private static (DrawerService Service, SettingsStore Store) Make(DrawerSandbox box, params string[] drawers)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var defs = drawers.Select(n => new DrawerDef(DrawerIds.From(n), n, Path.Combine(box.Drawers, n), true)).ToArray();
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = defs });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store);
    }

    /// <summary>Lets the dispatcher run queued work and timers for a while (the watcher's debounce).</summary>
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void Each_drawer_lists_its_folder_folders_first_and_skips_housekeeping_files() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\b.png");
        box.File(@"Desktop\Drawers\Images\a.png");
        box.Dir(@"Desktop\Drawers\Images\zfolder");
        box.File(@"Desktop\Drawers\Images\desktop.ini");
        var (s, _) = Make(box, "Images");
        Assert.Equal(["zfolder", "a.png", "b.png"], s.Get("images")!.Items.Select(i => i.Name));
        Assert.Equal("Images drawer, 3 items", s.Get("images")!.SpokenName);
        s.Dispose();
    });

    [Fact]
    public void A_drawer_whose_folder_is_gone_says_so_instead_of_crashing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        Directory.Delete(Path.Combine(box.Drawers, "Images"));
        s.Reload();
        Assert.True(s.Get("images")!.Missing);
        Assert.Empty(s.Get("images")!.Items);
        s.Dispose();
    });

    [Fact]
    public void A_folder_deleted_while_watched_turns_the_drawer_missing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\a.png");
        var (s, _) = Make(box, "Images");
        Directory.Delete(Path.Combine(box.Drawers, "Images"), recursive: true);
        Pump(1500);
        Assert.True(s.Get("images")!.Missing);
        s.Dispose();
    });

    [Fact]
    public void A_file_added_outside_Sanduhr_appears_after_the_debounce() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        box.File(@"Desktop\Drawers\Images\new.png");
        Pump(1500);
        Assert.Contains(s.Get("images")!.Items, i => i.Name == "new.png");
        s.Dispose();
    });

    [Fact]
    public void A_hand_move_lands_in_the_drawer_and_is_one_journal_entry() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var run = s.MoveIn([box.File(@"Desktop\a.png"), box.File(@"Desktop\b.png")], "images");
        Assert.Equal(2, run.Moved.Count);
        var entry = Assert.Single(s.Journal.Load().Entries);
        Assert.Equal(DrawerPlanSource.User, entry.Source);
        Assert.Equal(2, entry.Moves.Count);
        s.Dispose();
    });

    [Fact]
    public void A_hand_move_that_cannot_be_journaled_says_undo_is_lost() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var journal = Path.Combine(box.Root, "appdata", "Sanduhr", "drawers-journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        using (new FileStream(journal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var run = s.MoveIn([a], "images");
            Assert.Single(run.Moved);
            Assert.True(run.UndoLost);
        }
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "a.png")));
        s.Dispose();
    });

    [Fact]
    public void Dropping_a_file_on_the_drawer_already_holding_it_does_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\Drawers\Images\a.png");
        var (s, _) = Make(box, "Images");
        Assert.False(s.Accepts([a], "images"));
        var run = s.MoveIn([a], "images");
        Assert.Empty(run.Moved);
        Assert.Single(Directory.GetFiles(Path.Combine(box.Drawers, "Images")));
        Assert.Empty(s.Journal.Load().Entries);
        s.Dispose();
    });

    [Fact]
    public void A_file_from_outside_the_Desktop_and_the_drawers_is_not_accepted() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        Assert.False(s.Accepts([box.File(@"Downloads\a.png")], "images"));
        Assert.True(s.Accepts([box.File(@"Desktop\a.png")], "images"));
        s.Dispose();
    });

    [Fact]
    public void Undo_of_a_hand_move_puts_the_file_back_and_drops_the_entry() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var a = box.File(@"Desktop\a.png");
        s.MoveIn([a], "images");
        var run = s.Undo(s.Journal.Load().Entries[0].Id);
        Assert.Single(run.Moved);
        Assert.True(File.Exists(a));
        Assert.Empty(s.Journal.Load().Entries);
        s.Dispose();
    });

    [Fact]
    public void Holds_make_the_service_busy_until_the_last_is_released() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        int ended = 0;
        s.BusyEnded += () => ended++;
        var a = s.Hold();
        var b = s.Hold();
        Assert.True(s.Busy);
        a.Dispose();
        Assert.Equal(0, ended);
        b.Dispose();
        b.Dispose();   // releasing twice is harmless
        Assert.False(s.Busy);
        Assert.Equal(1, ended);
        s.Dispose();
    });

    [Fact]
    public void Fold_and_hide_are_saved() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box, "Images");
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl drawer.images:tl:1" });
        s.SetFolded("images", true);
        Assert.True(s.Get("images")!.Folded);
        Assert.Equal(["images"], store.LoadDrawers().Folded);
        s.Hide("images");
        Assert.Null(new DeskArrangement(store.LoadDesk().Layout).Placement("drawer.images"));
        s.ShowAll();
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(store.LoadDesk().Layout).Placement("drawer.images")!.Anchor);
        s.Dispose();
    });

    [Fact]
    public void Renaming_an_owned_drawer_renames_its_folder_and_keeps_its_id() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\a.png");
        var (s, store) = Make(box, "Images");
        Assert.Null(s.RenameDrawer("images", "Photos"));
        var d = Assert.Single(store.LoadDrawers().Drawers);
        Assert.Equal(("images", "Photos", Path.Combine(box.Drawers, "Photos")), (d.Id, d.Name, d.Home));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Photos", "a.png")));
        Assert.NotNull(s.RenameDrawer("images", "CON"));
        s.Dispose();
    });

    [Fact]
    public void Renaming_a_file_goes_through_the_Shell_and_refuses_a_taken_name() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        var (s, _) = Make(box, "Images");
        Assert.NotNull(s.RenameItem(a, "b.png"));
        Assert.Null(s.RenameItem(a, "c.png"));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "c.png")));
        s.Dispose();
    });

    [Fact]
    public void Pending_items_show_faded_in_their_drawer_and_new_drawers_preview() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var c = box.File(@"Desktop\c.pdf");
        var docs = new DrawerDef("documents", "Documents", Path.Combine(box.Drawers, "Documents"), true);
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, [docs], [new DrawerMove(a, "images"), new DrawerMove(c, "documents")]);
        s.SetPending(DrawerPlanValidator.Check(plan, box.Roots, s.Settings.Drawers));
        Assert.True(s.Get("images")!.Items.Single(i => i.Path == a).Pending);
        Assert.True(s.Get("documents")!.Items.Single().Pending);
        s.ClearPending();
        Assert.Empty(s.Get("images")!.Items);
        Assert.Null(s.Get("documents"));
        s.Dispose();
    });

    [Fact]
    public void The_Desktop_listing_flags_hidden_items_and_leaves_out_the_Public_Desktop() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box);
        var h = box.File(@"Desktop\hidden.txt");
        File.SetAttributes(h, FileAttributes.Hidden);
        box.File(@"Desktop\shown.txt");
        box.File(@"Public\shared.lnk");
        var list = s.DesktopListing();
        Assert.Contains(list, i => i.Path == h && i.Hidden);
        Assert.Contains(list, i => i.Path.EndsWith("shown.txt") && !i.Hidden);
        Assert.DoesNotContain(list, i => i.Path.EndsWith("shared.lnk"));
        s.Dispose();
    });

    [Fact]
    public void A_view_drawer_shows_an_existing_folder_and_two_names_get_different_ids() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box);
        Assert.Null(s.AddView("Фото", box.Dir("Downloads")));
        Assert.Null(s.AddView("写真", box.Dir("Pictures")));
        var ids = store.LoadDrawers().Drawers.Select(d => (d.Id, d.Owned)).ToArray();
        Assert.Equal([("drawer", false), ("drawer-2", false)], ids);
        s.Dispose();
    });

    [Fact]
    public void Turned_off_drawers_are_not_drawn() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box, "Images");
        store.SaveDrawers(store.LoadDrawers() with { Enabled = false });
        s.Reload();
        Assert.Null(s.Get("images"));
        s.Dispose();
    });
}
