using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Moves, undo and return, file by file in a sandbox (5.0 drawers spec, section 4; drawers plan 2, Task 5).</summary>
public sealed class DrawerMoverTests
{
    private static DrawerDef Def(DrawerSandbox box, string name) => new(DrawerIds.From(name), name, Path.Combine(box.Drawers, name), true);

    private static DrawerPlanCheck Check(DrawerSandbox box, IReadOnlyList<DrawerDef> existing, params (string File, DrawerDef Drawer)[] moves)
    {
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, moves.Select(m => m.Drawer).Distinct().ToArray(),
            moves.Select(m => new DrawerMove(m.File, m.Drawer.Id)).ToArray());
        var check = DrawerPlanValidator.Check(plan, box.Roots, existing);
        Assert.True(check.Ok, check.Error);
        return check;
    }

    [Fact]
    public void Apply_creates_new_homes_moves_files_and_records_each_move() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.png");
        var images = Def(box, "Images");
        var run = new DrawerMover(box.Desktop).Apply(Check(box, [], (a, images)), []);
        var m = Assert.Single(run.Moved);
        Assert.Equal(a, m.From);
        Assert.Equal(Path.Combine(images.Home, "a.png"), m.To, ignoreCase: true);
        Assert.NotNull(m.WrittenUtc);
        Assert.Equal("1 moved.", run.Line);
    });

    [Fact]
    public void A_name_taken_in_the_drawer_gets_a_clash_name_and_nothing_is_overwritten() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        box.File(@"Desktop\Drawers\Images\a.png", "old");
        var a = box.File(@"Desktop\a.png", "new");
        var run = new DrawerMover(box.Desktop).Apply(Check(box, [images], (a, images)), [images]);
        Assert.Equal(Path.Combine(images.Home, "a (2).png"), Assert.Single(run.Moved).To, ignoreCase: true);
        Assert.Equal("old", File.ReadAllText(Path.Combine(images.Home, "a.png")));
    });

    [Fact]
    public void A_file_gone_before_Apply_is_skipped_and_the_rest_move() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var b = box.File(@"Desktop\b.png");
        var check = Check(box, [], (a, images), (b, images));
        File.Delete(a);   // the user tidied by hand while the bar was up
        var run = new DrawerMover(box.Desktop).Apply(check, []);
        Assert.Single(run.Moved);
        Assert.Equal("1 moved, 1 skipped: a.png was moved since.", run.Line);
    });

    [Fact]
    public void A_locked_file_is_skipped_with_the_app_holding_it_and_the_run_carries_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var docs = Def(box, "Documents");
        var locked = box.File(@"Desktop\report.docx");
        var free = box.File(@"Desktop\notes.txt");
        var check = Check(box, [], (locked, docs), (free, docs));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var run = new DrawerMover(box.Desktop).Apply(check, []);
        Assert.Equal(free, Assert.Single(run.Moved).From);
        var skip = Assert.Single(run.Skipped);
        Assert.Equal("report.docx", skip.Name);
        Assert.StartsWith("is open in ", skip.Reason);
        Assert.True(File.Exists(locked));
    });

    [SkippableFact]
    public void A_Desktop_junction_leading_outside_is_never_moved() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var outside = box.Dir("Elsewhere");
        var link = Path.Combine(box.Desktop, "link");
        Skip.IfNot(box.Junction(link, outside), "mklink /J is not available here.");
        var junk = Def(box, "Junk");
        Directory.CreateDirectory(junk.Home);
        var run = new DrawerMover(box.Desktop).Run([new UndoStep(link, Path.Combine(junk.Home, "link"))], [junk.Home], toDesktop: false);
        Assert.Equal(DrawerSkip.Outside, Assert.Single(run.Skipped).Reason);
        Assert.True(Directory.Exists(link));
    });

    [Fact]
    public void Undo_puts_files_back_and_skips_one_changed_since() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var b = box.File(@"Desktop\b.png");
        var mover = new DrawerMover(box.Desktop);
        var run = mover.Apply(Check(box, [], (a, images), (b, images)), []);
        var entry = new JournalEntry("e1", DateTime.UtcNow, DrawerPlanSource.Rules, null, run.Moved);
        File.SetLastWriteTimeUtc(Path.Combine(images.Home, "b.png"), DateTime.UtcNow.AddMinutes(5));   // edited since
        var undo = DrawerUndo.Plan(entry, p => DrawerMover.Exists(p) ? File.GetLastWriteTimeUtc(p) : null, DrawerMover.Exists);
        var back = mover.Undo(undo, [images.Home]);
        Assert.True(File.Exists(a));
        Assert.Equal("1 moved, 1 skipped: b.png changed since.", back.Line);
    });

    [Fact]
    public void Return_empties_owned_homes_and_removes_them_but_keeps_one_holding_a_locked_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var docs = Def(box, "Documents");
        box.File(@"Desktop\Drawers\Images\a.png");
        var locked = box.File(@"Desktop\Drawers\Documents\report.docx");
        box.File(@"Desktop\a.png");   // a clash on the way back
        var plan = DrawerReturn.Plan([images, docs], box.Drawers, box.Desktop,
            dir => Directory.EnumerateFileSystemEntries(dir).Select(p => new DesktopItem(p, Directory.Exists(p), DateTime.UtcNow)),
            DrawerMover.Exists);
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = new DrawerMover(box.Desktop).Return(plan, [images.Home, docs.Home]);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a (2).png")));
        Assert.False(Directory.Exists(images.Home));
        Assert.Equal([docs.Home, box.Drawers], result.KeptHomes);
        Assert.StartsWith("1 moved, 1 skipped: report.docx is open in ", result.Run.Line);
    });

    [Fact]
    public void A_home_holding_only_Windows_housekeeping_files_counts_as_empty() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var ini = box.File(@"Desktop\Drawers\Images\desktop.ini", "[.ShellClassInfo]");
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        var plan = new ReturnPlan([], [images.Home, box.Drawers]);
        Assert.Empty(new DrawerMover(box.Desktop).Return(plan, [images.Home]).KeptHomes);
        Assert.False(Directory.Exists(box.Drawers));
    });

    [Fact]
    public void A_move_that_landed_is_journaled_even_when_the_Shell_reports_failure_and_no_new_path() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png");
        // The Shell moved the file but could not name where (Ok false, no path): the disk says it moved.
        var mover = new DrawerMover(box.Desktop, (src, dir, name) =>
        {
            File.Move(src, Path.Combine(dir, name));
            return new ShellResult(false, null, unchecked((int)0x80004005));
        });
        var run = mover.Apply(Check(box, [], (a, images)), []);
        var m = Assert.Single(run.Moved);
        Assert.Equal(Path.Combine(images.Home, "a.png"), m.To, ignoreCase: true);
        Assert.Empty(run.Skipped);
    });

    [Fact]
    public void A_move_the_Shell_renamed_on_a_collision_is_journaled_at_the_name_it_took() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png", "new");
        var mover = new DrawerMover(box.Desktop, (src, dir, name) =>
        {
            File.Move(src, Path.Combine(dir, "a (2).png"));   // the Shell collision name, which it failed to report
            return new ShellResult(false, null, unchecked((int)0x80004005));
        });
        var run = mover.Apply(Check(box, [], (a, images)), []);
        Assert.Equal(Path.Combine(images.Home, "a (2).png"), Assert.Single(run.Moved).To, ignoreCase: true);
    });

    [Fact]
    public void The_journal_store_adds_reads_and_removes_entries()
    {
        using var box = new DrawerSandbox();
        var store = new DrawerJournalStore(Path.Combine(box.Root, "app", "drawers-journal.json"));
        var e = store.Add(DrawerPlanSource.User, null, [new JournalMove(@"C:\a", @"C:\b", false, DateTime.UtcNow)], DateTime.UtcNow);
        Assert.Equal(e.Id, Assert.Single(store.Load().Entries).Id);
        store.Remove(e.Id);
        Assert.Empty(store.Load().Entries);
    }

    [Fact]
    public void A_damaged_journal_file_reads_as_empty()
    {
        using var box = new DrawerSandbox();
        var file = box.File(@"app\drawers-journal.json", "{ not json");
        Assert.Empty(new DrawerJournalStore(file).Load().Entries);
    }
}
