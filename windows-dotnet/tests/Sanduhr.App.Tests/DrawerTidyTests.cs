using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Tidy drawers with Undo (drawers look and placement, Task 6): the controller lines drawers up in work DIPs and hands back the places
/// before; the drawers bar holds Tidy's result like a review's. The Shell is never read (the icon reader is stubbed).</summary>
[Collection("SettingsWindow")]
public sealed class DrawerTidyTests
{
    private static IReadOnlyList<DeskScreen> Screen(int w, int h)
    {
        var b = new PxRect(0, 0, w, h);
        return [new DeskScreen("ONE", b, b, 1.0, true, DeskInsets.None, @"\\?\DISPLAY#MAIN#0")];
    }

    private static (DeskController Desk, DrawerService Service, SettingsStore Store) Desk(DrawerSandbox box, IReadOnlyList<DeskScreen> screens,
        Func<DrawerSettings, DrawerSettings> edit, params string[] names)
    {
        var (s, store, paths) = DrawerFreePanelTests.Make(box, edit, names);
        store.SaveDesk(store.LoadDesk() with { Layout = "" });
        var (desk, _) = DrawerFreePanelTests.Start(store, paths, s, screens);
        return (desk, s, store);
    }

    private static Dictionary<string, DrawerPlace> Places(params (string Id, double X, double Y)[] places) =>
        places.ToDictionary(p => p.Id, p => new DrawerPlace(DeskMonitors.Main, p.X, p.Y), StringComparer.Ordinal);

    private sealed class FakeHost : IDrawerReviewHost
    {
        public bool OtherReviewOpen { get; set; }
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply, Dismiss;
        public void Preview(string layout) { }
        public void EndPreview() { }
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss) { Model = model; Apply = apply; Dismiss = dismiss; }
        public void CloseBar() => Model = null;
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => null;
    }

    private static (DrawerReviewService Review, FakeHost Host) Review(DrawerService s, SettingsStore store)
    {
        var host = new FakeHost();
        return (new DrawerReviewService(s, store, host), host);
    }

    [Fact]
    public void Tidy_lines_drawers_up_and_undo_puts_every_place_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400), ("documents", 100, 600));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images", "Documents");
        try
        {
            var result = desk.Tidy(DrawerTidy.Default, DeskMonitors.Main);
            Assert.NotNull(result);
            Assert.Equal(2, result!.Value.Moved);
            Assert.Equal(saved, result.Value.Before);
            var after = store.LoadDrawers().Places;
            // Top right, a column down: 1920 - 24 - 360 across, then the margin and an open drawer's height plus the gap down.
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), after["images"]);   // nearer the corner, so first
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24 + DrawerPlacement.OpenHeight + DrawerPlacement.Gap), after["documents"]);

            desk.RestorePlaces(result.Value.Before);
            Assert.Equal(saved, store.LoadDrawers().Places);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Tidy_saves_the_corner_and_direction_it_was_given_and_moves_nothing_already_lined_up() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = Places(("images", 700, 400)) }, "Images");
        try
        {
            var tidy = new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row);
            Assert.Equal(1, desk.Tidy(tidy, null)!.Value.Moved);
            Assert.Equal(tidy, store.LoadDrawers().Tidy);
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 24, 1080 - 24 - DrawerPlacement.OpenHeight), store.LoadDrawers().Places["images"]);
            Assert.Equal(0, desk.Tidy(tidy, null)!.Value.Moved);   // already there
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Tidy_stays_off_the_desktop_icons() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = Places(("images", 700, 400)) }, "Images");
        try
        {
            var icons = new DrawerRect(1400, 0, 520, 400);
            desk.IconAreaOverride = [icons];
            desk.Tidy(DrawerTidy.Default, null);
            var p = store.LoadDrawers().Places["images"];
            Assert.False(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight).Overlaps(icons));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Tidy_refuses_while_a_review_is_open_and_moves_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images");
        var (review, host) = Review(s, store);
        var wired = desk.TidyBusy;
        try
        {
            desk.TidyBusy = () => true;
            Assert.Null(desk.Tidy(DrawerTidy.Default, DeskMonitors.Main));
            Assert.Equal(saved, store.LoadDrawers().Places);

            // The real wiring: a drawers result bar that is still up holds Tidy off until Done.
            desk.TidyBusy = wired;
            DrawerReviewService.Current = review;
            Assert.Null(review.ShowResult("3 drawers lined up.", () => { }));
            Assert.Null(desk.Tidy(DrawerTidy.Default, DeskMonitors.Main));
            Assert.Equal(saved, store.LoadDrawers().Places);
            host.Dismiss!();
            Assert.NotNull(desk.Tidy(DrawerTidy.Default, DeskMonitors.Main));
        }
        finally { DrawerReviewService.Current = null; desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drawer_that_cannot_fit_keeps_its_place_and_nothing_lands_on_it_or_off_screen() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 10, 10), ("music", 140, 400), ("documents", 20, 380));
        // A 500x450 screen holds one open drawer's column: the second open drawer has no room, and Documents (folded, 42 high) has none
        // left under the first.
        var (desk, s, store) = Desk(box, Screen(500, 450), d => d with { Places = saved, Folded = ["documents"] }, "Images", "Music", "Documents");
        try
        {
            Assert.Equal(1, desk.Tidy(DrawerTidy.Default, DeskMonitors.Main)!.Value.Moved);
            var after = store.LoadDrawers().Places;
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 500 - 24 - 360, 24), after["images"]);   // the one that fit did move
            Assert.Equal(saved["music"], after["music"]);         // no room: it keeps its place
            Assert.Equal(saved["documents"], after["documents"]);
            var moved = new DrawerRect(after["images"].X, after["images"].Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
            foreach (var (id, p) in after)
            {
                Assert.True(p.X >= 0 && p.Y >= 0, id);
                Assert.True(p.X + DrawerPlacement.Width <= 500 && p.Y + DrawerPlacement.FoldedHeight <= 450, id);
                if (id == "images") continue;
                // Nothing tidied landed on a drawer that kept its place.
                Assert.False(moved.Overlaps(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, id == "documents" ? DrawerPlacement.FoldedHeight : DrawerPlacement.OpenHeight)), id);
            }
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Show_result_holds_the_bar_until_Done_and_Undo_runs_once_then_drops_Undo() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = DrawerFreePanelTests.Make(box);
        var (review, host) = Review(s, store);
        try
        {
            int undone = 0;
            Assert.False(review.BarOpen);
            Assert.Null(review.ShowResult("2 drawers lined up.", () => undone++));
            Assert.True(review.BarOpen);
            Assert.False(review.Reviewing);
            Assert.Equal("2 drawers lined up.", host.Model!.Title);
            Assert.False(host.Model.ApplyOff);
            Assert.Equal(DrawerReview.Undo, host.Model.ApplyLabel);
            Assert.Equal(DrawerReview.Busy, review.ShowResult("again", () => { }));   // a second bar waits for the first

            host.Apply!();
            Assert.Equal(1, undone);
            Assert.True(review.BarOpen);   // the line stays, until Done
            Assert.Equal("2 drawers lined up.", host.Model!.Title);
            Assert.True(host.Model.ApplyOff);
            host.Apply!();
            Assert.Equal(1, undone);       // Undo only ever runs once

            host.Dismiss!();
            Assert.False(review.BarOpen);
            Assert.Null(host.Model);
        }
        finally { s.Dispose(); }
    });

    [Fact]
    public void Show_result_is_refused_while_another_review_is_open() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = DrawerFreePanelTests.Make(box);
        var (review, host) = Review(s, store);
        try
        {
            host.OtherReviewOpen = true;
            Assert.Equal(DrawerReview.Busy, review.ShowResult("x", () => { }));
            Assert.False(review.BarOpen);
            Assert.Null(host.Model);
        }
        finally { s.Dispose(); }
    });
}
