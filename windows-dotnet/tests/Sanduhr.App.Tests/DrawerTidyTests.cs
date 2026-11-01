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
            Assert.Equal(saved["images"], result.Value.Changes["images"].Before);
            var after = store.LoadDrawers().Places;
            // Top right, a column down: 1920 - 24 - 360 across, then the margin and an open drawer's height plus the gap down.
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), after["images"]);   // nearer the corner, so first
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24 + DrawerPlacement.OpenHeight + DrawerPlacement.Gap), after["documents"]);

            desk.RestorePlaces(result.Value.Changes);
            Assert.Equal(saved, store.LoadDrawers().Places);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Undo_puts_a_never_placed_drawer_back_where_it_was_drawn_after_Tidy_changed_the_corner() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d, "Images");
        try
        {
            Assert.Empty(store.LoadDrawers().Places);
            // No saved place: it arrives top right (1536, 24). Tidy to the bottom left, then Undo.
            var result = desk.Tidy(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), null);
            Assert.Equal(1, result!.Value.Moved);
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 24, 1080 - 24 - DrawerPlacement.OpenHeight), store.LoadDrawers().Places["images"]);
            desk.RestorePlaces(result.Value.Changes);
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), store.LoadDrawers().Places["images"]);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Undo_leaves_a_drawer_dragged_since_where_it_was_dropped() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400), ("documents", 100, 600));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images", "Documents");
        try
        {
            var result = desk.Tidy(DrawerTidy.Default, null)!.Value;
            var dropped = new DrawerPlace(DeskMonitors.Main, 333, 111);
            s.SetPlace("images", dropped);
            desk.RestorePlaces(result.Changes);
            var after = store.LoadDrawers().Places;
            Assert.Equal(dropped, after["images"]);        // the drag survives
            Assert.Equal(saved["documents"], after["documents"]);   // the untouched one is put back
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Undo_does_not_bring_back_the_place_of_a_drawer_removed_since() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400), ("documents", 100, 600));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images", "Documents");
        try
        {
            var result = desk.Tidy(DrawerTidy.Default, null)!.Value;
            var now = s.Settings;
            s.Save(now with
            {
                Drawers = [.. now.Drawers.Where(d => d.Id != "documents")],
                Places = now.Places.Where(p => p.Key != "documents").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            });
            desk.RestorePlaces(result.Changes);
            var after = store.LoadDrawers();
            Assert.DoesNotContain("documents", after.Places.Keys);
            Assert.Equal(saved["images"], after.Places["images"]);
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

            // The real wiring: a drawers review that is open holds Tidy off until it is dismissed.
            desk.TidyBusy = wired;
            DrawerReviewService.Current = review;
            box.File(@"Desktop\notes.pdf");
            Assert.Null(review.SortNow());
            Assert.True(review.Reviewing);
            Assert.Null(desk.Tidy(DrawerTidy.Default, DeskMonitors.Main));
            Assert.Equal(saved, store.LoadDrawers().Places);
            host.Dismiss!();
            Assert.False(review.Reviewing);
            Assert.NotNull(desk.Tidy(DrawerTidy.Default, DeskMonitors.Main));
        }
        finally { DrawerReviewService.Current = null; desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Tidy_and_a_chat_replace_a_result_bar_that_has_no_review_behind_it_and_a_second_Tidy_runs() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = Places(("images", 700, 400)) }, "Images");
        var (review, host) = Review(s, store);
        DrawerReviewService.Current = review;
        try
        {
            var first = desk.Tidy(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), null);
            Assert.NotNull(first);
            Assert.Null(review.ShowResult("1 drawer lined up.", () => { }));
            Assert.True(review.BarOpen);
            Assert.False(review.Reviewing);

            // The result bar is up, with no review: a second Tidy runs, and its result replaces the first.
            var second = desk.Tidy(new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), null);
            Assert.NotNull(second);
            Assert.Equal(1, second!.Value.Moved);
            Assert.Null(review.ShowResult("second", () => { }));
            Assert.Equal("second", host.Model!.Title);

            // A chat's look and arrangement replace it too.
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass","arrange":{"corner":"br"}}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Glass, store.LoadDrawers().Look);
            Assert.StartsWith(SetupCopy.ChatDrawerSettingsDone, host.Model!.Title);
        }
        finally { DrawerReviewService.Current = null; desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void UseRules_still_refuses_while_a_review_is_open() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d, "Images");
        var (review, _) = Review(s, store);
        DrawerReviewService.Current = review;
        try
        {
            box.File(@"Desktop\notes.pdf");
            Assert.Null(review.SortNow());
            Assert.True(review.Reviewing);
            Assert.Equal(DrawerReview.Busy, review.UseRules([new DrawerRule(DrawerRuleKind.Images, "images")], ["Images"]));
            Assert.Equal(DrawerReview.Busy, ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass"}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Solid, store.LoadDrawers().Look);
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

    // ---- the AI chat's drawer settings (Task 8) ----

    private static PastedDrawerLook Look(string json) => PastedSetup.Read(json).DrawerLook!;

    [Fact]
    public void A_chat_look_without_arrange_is_saved_and_moves_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass","opacity":70}}"""), s, desk, review));
            var after = store.LoadDrawers();
            Assert.Equal(DrawerLookKind.Glass, after.Look);
            Assert.Equal(70, after.Opacity);
            Assert.Equal(saved, after.Places);
            Assert.False(review.BarOpen);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_look_with_only_opacity_keeps_the_look() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Look = DrawerLookKind.Outline }, "Images");
        var (review, _) = Review(s, store);
        try
        {
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"opacity":40}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Outline, store.LoadDrawers().Look);
            Assert.Equal(40, store.LoadDrawers().Opacity);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_arrange_lines_up_from_its_corner_takes_the_missing_part_from_the_users_tidy_and_undo_restores_places_and_look() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400), ("documents", 100, 600));
        var mine = new DrawerTidy(DrawerCorner.TopRight, DrawerFlow.Row);
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved, Tidy = mine }, "Images", "Documents");
        var (review, host) = Review(s, store);
        try
        {
            // The chat names the corner only: the direction stays the user's (a row).
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"bare","opacity":30,"arrange":{"corner":"bl"}}}"""), s, desk, review));
            var after = store.LoadDrawers();
            Assert.Equal(DrawerLookKind.Bare, after.Look);
            Assert.Equal(30, after.Opacity);
            Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), after.Tidy);
            Assert.NotEqual(saved, after.Places);
            Assert.True(review.BarOpen);
            Assert.StartsWith(SetupCopy.ChatDrawerSettingsDone, host.Model!.Title);
            Assert.Equal(DrawerReview.Undo, host.Model.ApplyLabel);

            host.Apply!();   // Undo
            var undone = store.LoadDrawers();
            Assert.Equal(saved, undone.Places);
            Assert.Equal(DrawerLookKind.Solid, undone.Look);
            Assert.Equal(DrawerStyle.DefaultOpacity, undone.Opacity);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Undo_after_a_chat_arrange_keeps_a_look_opacity_or_tidy_the_user_changed_since_and_restores_the_rest() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400));
        var mine = new DrawerTidy(DrawerCorner.TopRight, DrawerFlow.Row);
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved, Tidy = mine }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass","opacity":70,"arrange":{"corner":"bl"}}}"""), s, desk, review));
            // The user changes the look and the remembered Tidy on the settings page; the opacity stays what the chat set.
            s.Save(s.Settings with { Look = DrawerLookKind.Outline, Tidy = new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column) });

            host.Apply!();   // Undo
            var after = store.LoadDrawers();
            Assert.Equal(saved, after.Places);                                  // the places Tidy wrote go back
            Assert.Equal(DrawerLookKind.Outline, after.Look);                   // the user's look stays
            Assert.Equal(DrawerStyle.DefaultOpacity, after.Opacity);            // untouched since: put back
            Assert.Equal(new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), after.Tidy);   // the user's Tidy stays
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Undo_after_a_chat_arrange_puts_the_remembered_tidy_back_when_the_user_left_it() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var mine = new DrawerTidy(DrawerCorner.TopRight, DrawerFlow.Row);
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = Places(("images", 700, 400)), Tidy = mine }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"arrange":{"corner":"bl"}}}"""), s, desk, review));
            Assert.Equal(DrawerCorner.BottomLeft, store.LoadDrawers().Tidy.Corner);
            host.Apply!();
            Assert.Equal(mine, store.LoadDrawers().Tidy);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_arrange_that_moves_nothing_and_saves_no_look_is_a_plain_line_with_no_bar() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Equal(DrawerReview.TidyNothing, ChatDrawerSettings.Apply(Look("""{"drawers":{"arrange":{"corner":"tr","direction":"column"}}}"""), s, desk, review));
            Assert.False(review.BarOpen);
            Assert.Null(host.Model);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_arrange_alone_leaves_the_look_and_says_only_what_tidy_did() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = Places(("images", 700, 400)), Look = DrawerLookKind.Glass }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"arrange":{"corner":"tl","direction":"column"}}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Glass, store.LoadDrawers().Look);
            Assert.Equal(DrawerReview.Tidied(1), host.Model!.Title);
            host.Apply!();
            Assert.Equal(DrawerLookKind.Glass, store.LoadDrawers().Look);   // Undo has no look to put back
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_arrange_that_moves_nothing_still_saves_the_look_with_an_undo_for_it() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d, "Images");
        var (review, host) = Review(s, store);
        try
        {
            // The one drawer arrives top right in a column: that is already where this arrange puts it.
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"outline","arrange":{"corner":"tr","direction":"column"}}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Outline, store.LoadDrawers().Look);
            Assert.Equal(SetupCopy.ChatDrawerSettingsResult(true, DrawerReview.TidyNothing), host.Model!.Title);
            host.Apply!();
            Assert.Equal(DrawerLookKind.Solid, store.LoadDrawers().Look);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_look_is_refused_busy_while_a_review_is_open_and_nothing_changes() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Places = saved }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            desk.TidyBusy = () => true;   // a Desk design or widget review is up
            Assert.Equal(DrawerReview.Busy, ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass"}}"""), s, desk, review));
            Assert.Equal(DrawerReview.Busy, ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass","arrange":{"corner":"tl"}}}"""), s, desk, review));
            Assert.Equal(DrawerLookKind.Solid, store.LoadDrawers().Look);
            Assert.Equal(saved, store.LoadDrawers().Places);

            desk.TidyBusy = () => false;
            Assert.Null(review.ShowResult("3 drawers lined up.", () => { }));   // a result bar is up, with no review: the chat replaces it
            Assert.Null(ChatDrawerSettings.Apply(Look("""{"drawers":{"opacity":30}}"""), s, desk, review));
            Assert.Equal(30, store.LoadDrawers().Opacity);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_chat_look_and_arrange_with_drawers_off_saves_the_look_skips_tidy_and_says_so() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var saved = Places(("images", 700, 400));
        var (desk, s, store) = Desk(box, Screen(1920, 1080), d => d with { Enabled = false, Places = saved }, "Images");
        var (review, host) = Review(s, store);
        try
        {
            Assert.Equal(SetupCopy.ChatDrawerSettingsSavedOff,
                ChatDrawerSettings.Apply(Look("""{"drawers":{"look":"glass","opacity":70,"arrange":{"corner":"tl"}}}"""), s, desk, review));
            var after = store.LoadDrawers();
            Assert.Equal(DrawerLookKind.Glass, after.Look);
            Assert.Equal(70, after.Opacity);
            Assert.Equal(saved, after.Places);                 // Tidy did not run
            Assert.Equal(DrawerTidy.Default, after.Tidy);
            Assert.False(review.BarOpen);
            Assert.Null(host.Model);

            // An arrangement alone, with nothing to save, is still just the drawers-off line.
            Assert.Equal(DrawerReview.DrawersOff, ChatDrawerSettings.Apply(Look("""{"drawers":{"arrange":{"corner":"tl"}}}"""), s, desk, review));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });
}
