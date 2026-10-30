using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawers review from the rules' plan to Apply, Edit, Undo and Dismiss, with a fake bar (drawers plan 2, Task 9). In the
/// SettingsWindow collection: the bar's drawer names and result line read streamer mode, which tests there turn on.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerReviewServiceTests
{
    private sealed class FakeHost : IDrawerReviewHost
    {
        public bool OtherReviewOpen { get; set; }
        public string? Previewing { get; private set; }
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply, Edit_, Dismiss;
        public DrawerPlanEdits? NextEdit { get; set; }

        public void Preview(string layout) => Previewing = layout;
        public void EndPreview() => Previewing = null;
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss) { Model = model; Apply = apply; Edit_ = edit; Dismiss = dismiss; }
        public void CloseBar() => Model = null;
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => NextEdit;
    }

    private static (DrawerReviewService Review, DrawerService Drawers, SettingsStore Store, FakeHost Host) Make(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true });
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        var host = new FakeHost();
        return (new DrawerReviewService(drawers, store, host), drawers, store, host);
    }

    [Fact]
    public void Sort_now_previews_the_rules_plan_with_new_drawers_placed_and_files_faded() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var shot = box.File(@"Desktop\Screenshot 2026-10-30 101500.png");
        box.File(@"Desktop\notes.pdf");
        Assert.Null(review.SortNow());
        Assert.True(review.Reviewing);
        Assert.Equal("2 files into 2 drawers", host.Model!.Title);
        Assert.Equal(DrawerReview.FirstOnLine, host.Model.Note);
        Assert.NotNull(host.Edit_);
        Assert.Equal("clock:bl", host.Previewing);   // the Desk's layout unchanged
        Assert.Same(drawers.PreviewDrawers.Single(d => d.Def.Id == "screenshots"), drawers.Get("screenshots"));
        Assert.True(drawers.Get("screenshots")!.Items.Single().Pending);
        Assert.True(File.Exists(shot));   // nothing moves until Apply
        drawers.Dispose();
    });

    [Fact]
    public void Apply_moves_saves_the_drawers_and_shows_the_result_with_Undo() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        Assert.False(review.Reviewing);
        Assert.Null(host.Previewing);
        Assert.False(File.Exists(pdf));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Documents", "notes.pdf")));
        Assert.Equal("1 moved.", host.Model!.Title);
        Assert.Equal(DrawerReview.Undo, host.Model.ApplyLabel);
        var s = store.LoadDrawers();
        Assert.Equal(["documents"], s.Drawers.Select(d => d.Id));
        Assert.True(s.AutoTidyUnlocked);
        Assert.Equal("clock:bl", store.LoadDesk().Layout);   // free panels: Apply writes no layout word
        Assert.Empty(s.Places);
        Assert.Empty(drawers.PreviewDrawers);
        Assert.NotNull(drawers.Get("documents"));   // a saved drawer now
        Assert.Equal(DrawerPlanSource.Rules, Assert.Single(drawers.Journal.Load().Entries).Source);
        drawers.Dispose();
    });

    [Fact]
    public void Undo_from_the_result_puts_the_files_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        host.Apply!();   // the result's Undo
        Assert.True(File.Exists(pdf));
        Assert.Empty(drawers.Journal.Load().Entries);
        Assert.False(host.Model!.CanApply);   // nothing left to undo
        drawers.Dispose();
    });

    [Fact]
    public void Dismiss_moves_nothing_and_puts_the_Desk_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Dismiss!();
        Assert.False(review.Reviewing);
        Assert.Null(host.Model);
        Assert.Null(host.Previewing);
        Assert.True(File.Exists(pdf));
        Assert.Empty(store.LoadDrawers().Drawers);
        Assert.Null(drawers.Get("documents"));
        drawers.Dispose();
    });

    [Fact]
    public void Edit_unticks_a_file_and_renames_a_new_drawer_before_Apply() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var keep = box.File(@"Desktop\keep.pdf");
        box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.NextEdit = new DrawerPlanEdits(new HashSet<string> { keep }, new Dictionary<string, string> { ["documents"] = "Papers" });
        host.Edit_!();
        Assert.Equal("1 file into 1 drawer", host.Model!.Title);
        host.Apply!();
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Papers", "notes.pdf")));
        drawers.Dispose();
    });

    [Fact]
    public void A_name_rule_for_a_drawer_not_made_yet_makes_it() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        store.SaveDrawers(store.LoadDrawers() with { Rules = [new DrawerRule(DrawerRuleKind.NameContains, "receipts", Text: "invoice")] });
        drawers.Reload();
        box.File(@"Desktop\invoice-0042.txt");
        Assert.Null(review.SortNow());
        Assert.Contains("Receipts (new): 1 file", host.Model!.Changes);
        drawers.Dispose();
    });

    [Fact]
    public void The_review_names_both_folders_when_only_the_Desktop_syncs() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var elsewhere = box.Dir("Elsewhere");
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Root = elsewhere });
        // The sandbox Desktop stands in for a OneDrive-synced one; the moved Drawers folder is outside it.
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, [box.Desktop]), Dispatcher.CurrentDispatcher);
        var host = new FakeHost();
        box.File(@"Desktop\notes.pdf");
        Assert.Null(new DrawerReviewService(drawers, store, host).SortNow());
        Assert.Contains(host.Model!.Warnings, w => w == DrawerSync.Warning(box.Desktop, elsewhere, [box.Desktop]));
        drawers.Dispose();
    });

    [Fact]
    public void A_Desktop_with_nothing_to_sort_opens_no_review() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\project.code-workspace");
        Assert.Equal(DrawerReview.NothingToSort, review.SortNow());
        Assert.Null(host.Model);
        drawers.Dispose();
    });

    [Fact]
    public void Only_one_review_is_open_at_a_time() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\notes.pdf");
        host.OtherReviewOpen = true;
        Assert.Equal(DrawerReview.Busy, review.SortNow());
        host.OtherReviewOpen = false;
        Assert.Null(review.SortNow());
        Assert.Equal(DrawerReview.Busy, review.SortNow());
        drawers.Dispose();
    });

    [Fact]
    public void A_refused_Edit_keeps_the_review_on_the_previous_plan_and_says_why() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var shot = box.File(@"Desktop\Screenshot 2026-10-30 101500.png");
        box.File(@"Desktop\notes.pdf");
        review.SortNow();
        // Two new drawers renamed to the same folder: the validator refuses the plan.
        host.NextEdit = new DrawerPlanEdits(new HashSet<string>(),
            new Dictionary<string, string> { ["screenshots"] = "Same", ["documents"] = "Same" });
        host.Edit_!();
        Assert.True(review.Reviewing);
        Assert.Equal("2 files into 2 drawers", host.Model!.Title);
        Assert.Contains(host.Model.Warnings, w => w.Contains("Another drawer already uses the folder"));
        Assert.True(File.Exists(shot));
        host.Apply!();   // the previous plan still applies
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Screenshots", "Screenshot 2026-10-30 101500.png")));
        drawers.Dispose();
    });

    [Fact]
    public void Edit_that_unticks_every_file_keeps_the_review_and_says_so() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.NextEdit = new DrawerPlanEdits(new HashSet<string> { pdf }, new Dictionary<string, string>());
        host.Edit_!();
        Assert.True(review.Reviewing);
        Assert.Contains(DrawerReview.NothingToSort, host.Model!.Warnings);
        drawers.Dispose();
    });

    [Fact]
    public void Apply_with_a_locked_journal_offers_no_Undo_and_says_so() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        // A journal that exists and is held open, so the entry cannot be written.
        Assert.NotNull(drawers.Journal.Add(DrawerPlanSource.User, null, [new JournalMove(@"C:\a", @"C:\b", false, DateTime.UtcNow)], DateTime.UtcNow));
        var file = Directory.GetFiles(box.Root, "*journal*.json", SearchOption.AllDirectories).Single();
        review.SortNow();
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            host.Apply!();
        Assert.False(File.Exists(pdf));   // the move stood
        Assert.StartsWith("1 moved.", host.Model!.Title);
        Assert.EndsWith(" Undo is not available for this move.", host.Model.Title);
        Assert.False(host.Model.CanApply);
        Assert.Equal("Enter or Escape closes.", host.Model.Keys);
        drawers.Dispose();
    });

    [Fact]
    public void The_result_after_Apply_has_its_own_keys_line() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        Assert.Equal("Enter or Escape closes.", host.Model!.Keys);
        drawers.Dispose();
    });

    /// <summary>A host with a real review window (never shown), so the keys and clicks go through the window's own handling.</summary>
    private sealed class WindowHost : IDrawerReviewHost
    {
        public DeskReviewWindow? Window;
        private Action _apply = () => { }, _dismiss = () => { };
        public bool Closed { get; private set; }
        public bool OtherReviewOpen => false;
        public void Preview(string layout) { }
        public void EndPreview() { }
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss)
        {
            (_apply, _dismiss) = (apply, dismiss);
            if (Window is null)
            {
                Window = new DeskReviewWindow();
                Window.ApplyRequested += () => _apply();
                Window.DismissRequested += () => _dismiss();
            }
            Window.SetModel(model);
        }
        public void CloseBar() { Closed = true; Window = null; }
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => null;
    }

    private static (DrawerReviewService Review, DrawerService Drawers, WindowHost Host) MakeWindowed(DrawerSandbox box)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true });
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        var host = new WindowHost();
        return (new DrawerReviewService(drawers, store, host), drawers, host);
    }

    [Fact]
    public void On_the_result_Enter_twice_leaves_the_files_sorted_and_closes_the_bar() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, host) = MakeWindowed(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        var window = host.Window!;
        Assert.True(window.HandleKey(System.Windows.Input.Key.Enter, null));   // Apply
        Assert.False(File.Exists(pdf));
        Assert.True(window.HandleKey(System.Windows.Input.Key.Enter, null));   // the result: Enter closes
        Assert.False(File.Exists(pdf));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Documents", "notes.pdf")));
        Assert.True(host.Closed);
        drawers.Dispose();
    });

    [Fact]
    public void A_click_on_Undo_on_the_result_still_undoes() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, host) = MakeWindowed(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        var window = host.Window!;
        window.HandleKey(System.Windows.Input.Key.Enter, null);
        window.ReviewBar.Apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(File.Exists(pdf));
        drawers.Dispose();
    });

    [Fact]
    public void Apply_that_throws_ends_the_preview_and_shows_a_result_with_no_Undo() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.RunApply = (_, _) => throw new IOException("disk gone");
        review.SortNow();
        host.Apply!();
        Assert.False(review.Reviewing);
        Assert.Null(host.Previewing);
        Assert.Empty(drawers.PreviewDrawers);
        Assert.Equal(DrawerReview.ApplyFailed, host.Model!.Title);
        Assert.False(host.Model.CanApply);
        Assert.True(File.Exists(pdf));
        drawers.Dispose();
    });

    [Fact]
    public void Done_on_the_result_closes_the_bar() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        host.Dismiss!();   // Done
        Assert.Null(host.Model);
        drawers.Dispose();
    });
}
