using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawers plan 2 final review's fixes: Apply after the drawers changed under a review (I1), streamer mode on every drawers
/// surface (I3), a failed settings save after Apply (M1), a picked root (M3) and a rename's journal (M4). In the SettingsWindow
/// collection, since these tests turn streamer mode on and the classes there read it.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerFinalReviewTests
{
    private sealed class FakeHost : IDrawerReviewHost
    {
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply, Dismiss;
        public bool OtherReviewOpen => false;
        public void Preview(string layout) { }
        public void EndPreview() { }
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss) { Model = model; Apply = apply; Dismiss = dismiss; }
        public void CloseBar() => Model = null;
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => null;
    }

    private sealed record Rig(DrawerService Drawers, DrawerReviewService Review, SettingsStore Store, FakeHost Host, DrawersSettingsViewModel Vm);

    private static Rig Make(DrawerSandbox box, string? root = null, IReadOnlyList<string>? synced = null, DrawerDef[]? defs = null)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        defs ??= [];
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Root = root, Drawers = defs });
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, synced ?? []), Dispatcher.CurrentDispatcher);
        var host = new FakeHost();
        var review = new DrawerReviewService(drawers, store, host);
        var vm = new DrawersSettingsViewModel(drawers, review, store) { Confirm = _ => true, PickFolder = () => null };
        return new Rig(drawers, review, store, host, vm);
    }

    private static DrawerDef Owned(string root, string name) => new(DrawerIds.From(name), name, Path.Combine(root, name), true);

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void StreamerOn(DrawerSandbox box) => StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));

    // ---- I1: the drawers change under a review ----

    [Fact]
    public void Turning_drawers_off_on_the_page_ends_the_review_and_a_late_Apply_moves_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Null(r.Review.SortNow());
        var apply = r.Host.Apply!;
        r.Vm.Enabled = false;
        Pump();
        Assert.False(r.Review.Reviewing);
        apply();   // a click that was already on its way
        Assert.True(File.Exists(pdf));
        Assert.False(Directory.Exists(Path.Combine(box.Drawers, "Documents")));
        r.Vm.Dispose();
        r.Drawers.Dispose();
    });

    [Fact]
    public void Removing_a_drawer_on_the_page_ends_the_review_and_a_late_Apply_moves_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box, defs: [Owned(box.Drawers, "Documents")]);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Null(r.Review.SortNow());
        var apply = r.Host.Apply!;
        r.Vm.Remove("documents");
        Assert.False(r.Review.Reviewing);
        apply();
        Assert.True(File.Exists(pdf));
        Assert.Empty(r.Store.LoadDrawers().Drawers);
        r.Vm.Dispose();
        r.Drawers.Dispose();
    });

    [Fact]
    public void Apply_after_drawers_went_off_elsewhere_moves_nothing_and_offers_no_Undo() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Null(r.Review.SortNow());
        r.Drawers.TurnOff();   // Reset's path: the page never saw it
        r.Host.Apply!();
        Assert.False(r.Review.Reviewing);
        Assert.Equal(DrawerReview.DrawersOff, r.Host.Model!.Title);
        Assert.False(r.Host.Model.CanApply);
        Assert.True(File.Exists(pdf));
        Assert.Empty(r.Drawers.Journal.Load().Entries);
        r.Drawers.Dispose();
    });

    [Fact]
    public void Apply_checks_the_plan_again_and_shows_the_refusal_when_the_folder_moved() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Null(r.Review.SortNow());
        r.Drawers.Save(r.Drawers.Settings with { Root = box.Dir("Elsewhere") });
        r.Host.Apply!();
        Assert.Contains("must live in the Drawers folder", r.Host.Model!.Title);
        Assert.False(r.Host.Model.CanApply);
        Assert.True(File.Exists(pdf));
        Assert.Empty(r.Store.LoadDrawers().Drawers);
        r.Drawers.Dispose();
    });

    [Fact]
    public void The_mover_skips_a_move_into_a_drawer_it_does_not_know() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.png");
        var check = new DrawerPlanCheck(true, null, [], [new DrawerMove(a, "gone")], []);
        var run = new DrawerMover(box.Desktop).Apply(check, []);
        Assert.Empty(run.Moved);
        var skip = Assert.Single(run.Skipped);
        Assert.Equal(("a.png", DrawerSkip.CouldNotMove), (skip.Name, skip.Reason));
        Assert.True(File.Exists(a));
    });

    // ---- M1: the journal before the settings ----

    [Fact]
    public void A_settings_save_that_fails_after_Apply_still_says_the_files_moved_and_Undo_brings_them_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Null(r.Review.SortNow());
        var settings = Directory.GetFiles(Path.Combine(box.Root, "appdata"), "settings.json", SearchOption.AllDirectories).Single();
        File.SetAttributes(settings, FileAttributes.ReadOnly);   // the write throws UnauthorizedAccessException
        try
        {
            r.Host.Apply!();
            Assert.False(File.Exists(pdf));
            Assert.Equal("1 moved.", r.Host.Model!.Title);
            Assert.True(r.Host.Model.CanApply);   // Undo
            Assert.Single(r.Drawers.Journal.Load().Entries);
            Assert.Empty(r.Store.LoadDrawers().Drawers);   // the save did fail
            r.Host.Apply!();   // Undo
            Assert.True(File.Exists(pdf));
            Assert.Equal("1 moved.", r.Host.Model!.Title);
        }
        finally { File.SetAttributes(settings, FileAttributes.Normal); }
        r.Drawers.Dispose();
    });

    [Fact]
    public void A_failed_save_and_a_locked_journal_say_the_files_moved_and_that_Undo_is_not_available() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.NotNull(r.Drawers.Journal.Add(DrawerPlanSource.User, null, [new JournalMove(@"C:\a", @"C:\b", false, DateTime.UtcNow)], DateTime.UtcNow));
        var journal = Directory.GetFiles(box.Root, "*journal*.json", SearchOption.AllDirectories).Single();
        var settings = Directory.GetFiles(Path.Combine(box.Root, "appdata"), "settings.json", SearchOption.AllDirectories).Single();
        Assert.Null(r.Review.SortNow());
        File.SetAttributes(settings, FileAttributes.ReadOnly);
        try
        {
            using (new FileStream(journal, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                r.Host.Apply!();
        }
        finally { File.SetAttributes(settings, FileAttributes.Normal); }
        Assert.False(File.Exists(pdf));
        Assert.Equal("1 moved. Undo is not available for this move.", r.Host.Model!.Title);
        Assert.DoesNotContain("Nothing more was moved", r.Host.Model.Title);
        Assert.False(r.Host.Model.CanApply);
        r.Drawers.Dispose();
    });

    // ---- M3: a picked root stays ----

    [Fact]
    public void Turning_off_keeps_a_picked_Drawers_folder_and_removes_its_emptied_homes() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var picked = box.Dir("Picked");
        var r = Make(box, root: picked, defs: [Owned(picked, "Images")]);
        box.File(@"Picked\Images\a.png");
        Assert.Equal("1 moved.", r.Drawers.TurnOff().Line);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.False(Directory.Exists(Path.Combine(picked, "Images")));
        Assert.True(Directory.Exists(picked));
        r.Drawers.Dispose();
    });

    [Fact]
    public void Turning_off_removes_the_default_Drawers_folder_once_it_is_empty() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box, defs: [Owned(box.Drawers, "Images")]);
        box.File(@"Desktop\Drawers\Images\a.png");
        Assert.Equal("1 moved.", r.Drawers.TurnOff().Line);
        Assert.False(Directory.Exists(box.Drawers));
        r.Drawers.Dispose();
    });

    // ---- M4: a renamed drawer's journal ----

    [Fact]
    public void Undo_after_renaming_the_drawer_still_brings_the_file_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box, defs: [Owned(box.Drawers, "Documents")]);
        var pdf = box.File(@"Desktop\notes.pdf");
        Assert.Single(r.Drawers.MoveIn([pdf], "documents").Moved);
        Assert.Null(r.Drawers.RenameDrawer("documents", "Papers"));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Papers", "notes.pdf")));
        var entry = r.Drawers.Journal.Load().Entries.Single();
        Assert.StartsWith(Path.Combine(box.Drawers, "Papers"), entry.Moves.Single().To, StringComparison.OrdinalIgnoreCase);
        var run = r.Drawers.Undo(entry.Id);
        Assert.Single(run.Moved);
        Assert.True(File.Exists(pdf));
        r.Drawers.Dispose();
    });

    // ---- I3: streamer mode ----

    [Fact]
    public void The_result_bar_shows_counts_not_file_names_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        box.File(@"Desktop\notes.pdf");
        r.Review.RunApply = (_, _) => new DrawerRun([], [new DrawerSkipped("salary-2026.xlsx", DrawerSkip.CouldNotMove)]);
        Assert.Null(r.Review.SortNow());
        StreamerOn(box);
        try
        {
            r.Host.Apply!();
            Assert.Equal("0 moved, 1 skipped.", r.Host.Model!.Title);
            Assert.DoesNotContain("salary", r.Host.Model.Title);
        }
        finally { StreamerMode.Reset(); r.Drawers.Dispose(); }
    });

    [Fact]
    public void The_shown_line_is_counts_while_on_keeps_the_Undo_suffix_and_is_the_full_line_while_off() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var run = new DrawerRun([], [new DrawerSkipped("salary-2026.xlsx", DrawerSkip.MovedSince)]) { UndoLost = true };
        Assert.Equal(run.Line + " Undo is not available for this move.", run.Shown);
        Assert.Equal("1 moved.", new DrawerRun([new JournalMove(@"C:\a", @"C:\b", false, null)], []).Shown);
        StreamerOn(box);
        try
        {
            Assert.Equal("0 moved, 1 skipped. Undo is not available for this move.", run.Shown);
            Assert.Equal("1 moved.", new DrawerRun([new JournalMove(@"C:\a", @"C:\b", false, null)], []).Shown);
        }
        finally { StreamerMode.Reset(); }
    });

    [Fact]
    public void The_review_bar_names_drawers_by_codename_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        r.Drawers.Save(r.Drawers.Settings with { Rules = [new DrawerRule(DrawerRuleKind.NameContains, "tax-2026", Text: "invoice")] });
        box.File(@"Desktop\invoice-0042.txt");
        StreamerOn(box);
        try
        {
            Assert.Null(r.Review.SortNow());
            Assert.DoesNotContain(r.Host.Model!.Changes, l => l.Contains("Tax 2026", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(r.Host.Model.Changes, l => l.EndsWith(" (new): 1 file", StringComparison.Ordinal));
        }
        finally { StreamerMode.Reset(); r.Drawers.Dispose(); }
    });

    [Fact]
    public void The_OneDrive_line_hides_the_profile_path_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box, root: box.Dir("Elsewhere"), synced: [box.Desktop]);
        box.File(@"Desktop\notes.pdf");
        StreamerOn(box);
        try
        {
            Assert.Null(r.Review.SortNow());
            var warning = Assert.Single(r.Host.Model!.Warnings);
            Assert.StartsWith("Your Desktop syncs with OneDrive", warning);
            Assert.DoesNotContain(box.Root, warning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.GetFileName(box.Root), warning, StringComparison.OrdinalIgnoreCase);
        }
        finally { StreamerMode.Reset(); r.Drawers.Dispose(); }
    });

    [Fact]
    public void The_page_says_counts_for_Undo_turning_off_and_Remove_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var docs = Owned(box.Drawers, "Documents");
        var r = Make(box, defs: [docs, Owned(box.Drawers, "Images")]);
        var salary = box.File(@"Desktop\salary-2026.xlsx");
        r.Drawers.MoveIn([salary], "documents");
        File.Delete(Path.Combine(docs.Home, "salary-2026.xlsx"));   // gone since: Undo names it as skipped
        var locked = box.File(@"Desktop\Drawers\Images\passport-scan.png");
        StreamerOn(box);
        try
        {
            r.Vm.Undo(r.Drawers.Journal.Load().Entries.Single().Id);
            Assert.Equal("0 moved, 1 skipped.", r.Vm.Status);

            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                r.Vm.Remove("images");
            Assert.DoesNotContain("passport", r.Vm.Status);
            Assert.StartsWith("0 moved, 1 skipped.", r.Vm.Status);

            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                r.Vm.Enabled = false;
                Pump();
            }
            Assert.DoesNotContain("passport", r.Vm.Status);
            Assert.StartsWith("0 moved, 1 skipped.", r.Vm.Status);
        }
        finally { StreamerMode.Reset(); r.Vm.Dispose(); r.Drawers.Dispose(); }
    });

    [Fact]
    public void A_streamer_flip_clears_the_pages_status_line() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var r = Make(box);
        r.Vm.Status = "1 moved, 1 skipped: salary-2026.xlsx was moved since.";
        StreamerOn(box);
        try
        {
            r.Vm.ReloadNames();   // what Settings' ReloadNames calls when streamer mode moves
            Assert.Equal("", r.Vm.Status);
        }
        finally { StreamerMode.Reset(); r.Vm.Dispose(); r.Drawers.Dispose(); }
    });

    [Fact]
    public void The_inline_rename_opens_blank_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var item = new DrawerItem(box.File(@"Desktop\Drawers\Images\salary-2026.xlsx"), isFolder: false, pending: false);
        Assert.Equal(("salary-2026.xlsx", 11), DrawerPiece.EditorStart(item));
        StreamerOn(box);
        try { Assert.Equal(("", 0), DrawerPiece.EditorStart(item)); }
        finally { StreamerMode.Reset(); }
    });
}
