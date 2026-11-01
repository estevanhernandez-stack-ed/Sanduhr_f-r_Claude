using System.Windows;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Snapping while dragging and keeping clear of the desktop icons (drawers look and placement, Task 5): the controller answers a drop
/// and a live drag in the units of the place (work-area DIPs per monitor) from inputs in screen DIPs (Desk pieces) and physical pixels (icons,
/// the cursor). The Shell is never read: the icon reader is a seam.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerPlacementLiveTests
{
    private const string SecondId = @"\\?\DISPLAY#SECOND#1";

    private static IReadOnlyList<DeskScreen> One1080()
    {
        var b = new PxRect(0, 0, 1920, 1080);
        return [new DeskScreen("ONE", b, b, 1.0, true, DeskInsets.None, @"\\?\DISPLAY#MAIN#0")];
    }

    /// <summary>A primary 2880x1620 at 150% whose work area starts 60 px down (a taskbar at its top), so Work != Bounds on y; and a second
    /// 1920x1080 at 100% to its right whose work area starts 100 px in (a taskbar at its left), so Work != Bounds on x.</summary>
    private static IReadOnlyList<DeskScreen> TaskbarsTopAndLeft()
    {
        var b1 = new PxRect(0, 0, 2880, 1620);
        var w1 = new PxRect(0, 60, 2880, 1560);
        var b2 = new PxRect(2880, 0, 1920, 1080);
        var w2 = new PxRect(2980, 0, 1820, 1080);
        return
        [
            new DeskScreen("TOP", b1, w1, 1.5, true, TaskbarClearance.Insets(b1, w1, [], 1.5), @"\\?\DISPLAY#MAIN#0"),
            new DeskScreen("LEFT", b2, w2, 1.0, false, TaskbarClearance.Insets(b2, w2, [], 1.0), SecondId),
        ];
    }

    private static (DeskController Desk, DrawerService Service, SettingsStore Store, DrawerFreePanelTests.TwoScreenHost Host) Desk(
        DrawerSandbox box, IReadOnlyList<DeskScreen> screens, Func<DrawerSettings, DrawerSettings>? edit = null, string layout = "",
        params string[] names)
    {
        var (s, store, paths) = DrawerFreePanelTests.Make(box, edit, names);
        store.SaveDesk(store.LoadDesk() with { Layout = layout });   // no Desk pieces unless a test asks: they are snap targets too
        var (desk, host) = DrawerFreePanelTests.Start(store, paths, s, screens);
        return (desk, s, store, host);
    }

    private static DrawerPlace Saved(SettingsStore store, string id) => store.LoadDrawers().Places[id];

    // ---- a drop: snap, then keep clear ----

    [Fact]
    public void A_drop_snaps_then_keeps_clear_of_the_icons() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, One1080());
        try
        {
            desk.IconAreaOverride = [new DrawerRect(0, 0, 300, 1032)];
            s.RequestPlace("images", new PxPoint(110, 200));
            var p = Saved(store, "images");
            // 110 snaps to the 8 DIP grid (112), which overlaps the icons; the nearest free spot is the gap past them.
            Assert.Equal(300 + DrawerPlacement.Gap, p.X, 6);
            Assert.Equal(200, p.Y, 6);
            Assert.Equal(DeskMonitors.Main, p.Monitor);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void With_snap_and_keep_clear_off_a_drop_lands_where_it_was_dropped() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, One1080(), d => d with { Snap = false, KeepClear = false });
        try
        {
            desk.IconAreaOverride = [new DrawerRect(0, 0, 2000, 1080)];   // would swallow it, were it consulted
            s.RequestPlace("images", new PxPoint(803, 501));
            Assert.Equal(803, Saved(store, "images").X, 6);
            Assert.Equal(501, Saved(store, "images").Y, 6);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Alt_skips_the_snap_but_not_the_icons() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, One1080());
        try
        {
            s.RequestPlace("images", new PxPoint(803, 501), snap: false);   // on the grid it would be 800 x 504
            Assert.Equal((803.0, 501.0), (Saved(store, "images").X, Saved(store, "images").Y));
            desk.IconAreaOverride = [new DrawerRect(700, 400, 300, 300)];
            s.RequestPlace("images", new PxPoint(803, 501), snap: false);
            var p = Saved(store, "images");
            Assert.False(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight).Overlaps(new DrawerRect(700, 400, 300, 300)));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drop_snaps_to_another_drawers_edge_in_work_DIPs_on_a_150_percent_screen_with_a_taskbar_on_top() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, TaskbarsTopAndLeft(), d => d with
        {
            Places = new Dictionary<string, DrawerPlace> { ["images"] = new(DeskMonitors.Main, 200, 100) },
        }, "", "Images", "Documents");
        try
        {
            // Documents dropped 3 DIPs past Images' right edge plus the gap (200 + 360 + 12 = 572) and 3 below its top (100):
            // physical = work origin (0, 60) + DIPs x 1.5.
            s.RequestPlace("documents", new PxPoint((int)Math.Round((572 + 3) * 1.5), 60 + (int)Math.Round((100 + 3) * 1.5)));
            var p = Saved(store, "documents");
            Assert.Equal(DeskMonitors.Main, p.Monitor);
            Assert.Equal(572, p.X, 6);
            Assert.Equal(100, p.Y, 6);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drop_snaps_to_the_screens_margin_from_the_work_area_not_the_bounds() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            // The second screen's work area starts 100 px in (its taskbar is on the left): 24 DIPs of margin is x = 2980 + 24 on the screen.
            s.RequestPlace("images", new PxPoint(2980 + 24 + 5, 700));
            var p = Saved(store, "images");
            Assert.Equal(SecondId, p.Monitor);
            Assert.Equal(DrawerPlacement.Margin, p.X, 6);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drop_snaps_to_a_Desk_piece_whose_rect_started_in_bounds_DIPs() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, TaskbarsTopAndLeft(), layout: "clock:bl");
        try
        {
            var top = desk.ScreensForTests[0];
            var piece = Assert.Single(desk.PieceRects(top));   // the clock at the bottom left, in work DIPs
            Assert.True(piece.X >= 0 && piece.Right <= top.Work.Width / top.Scale);
            // A taskbar 40 DIPs tall on top: the piece's bottom is that much higher in work DIPs than in the bounds' DIPs.
            Assert.True(piece.Bottom <= top.Work.Height / top.Scale);
            Assert.True(piece.Bottom > top.Work.Height / top.Scale - 400, $"a bottom-left piece sits at the bottom, not at {piece.Bottom}");

            // Dropped 4 DIPs short of the gap to the right of the piece, and at a height that snaps to nothing else.
            double x = piece.Right + DrawerPlacement.Gap - 4;
            s.RequestPlace("images", DrawerScreens.WorkToPx(x, 500, top));
            Assert.Equal(piece.Right + DrawerPlacement.Gap, Saved(store, "images").X, 3);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void With_the_desktop_off_no_piece_rects_are_kept() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, TaskbarsTopAndLeft(), layout: "clock:bl");
        try
        {
            Assert.NotEmpty(desk.PieceRects(desk.ScreensForTests[0]));
            store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool>()));
            desk.Reload();
            Assert.Empty(desk.PieceRects(desk.ScreensForTests[0]));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    // ---- a live drag ----

    [Fact]
    public void The_live_snapper_answers_while_dragging_and_is_cleared_on_dispose() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, One1080());
        try
        {
            Assert.NotNull(s.Snapper);
            desk.IconAreaOverride = [new DrawerRect(0, 0, 2000, 1080)];   // not consulted while moving
            var snapped = s.Snapper!("images", new PxPoint(1920 - 360 - 30, 30));
            Assert.Equal(new PxPoint(1920 - 24 - 360, 24), snapped);
        }
        finally { desk.Dispose(); }
        Assert.Null(s.Snapper);
        s.Dispose();
    });

    [Fact]
    public void The_live_snapper_works_from_the_work_origin_at_150_percent_and_on_a_left_taskbar() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            // Primary: work area at y = 60 px, 1920 x 1040 DIPs. Right margin: x = 1920 - 24 - 360 DIPs; top margin: y = 24 DIPs.
            var snapped = s.Snapper!("images", new PxPoint((int)Math.Round((1536 + 4) * 1.5), 60 + (int)Math.Round((24 + 5) * 1.5)));
            Assert.Equal(new PxPoint(2304, 60 + 36), snapped);
            // Second: work area at x = 2980, scale 1.
            Assert.Equal(new PxPoint(2980 + 24, 24), s.Snapper!("images", new PxPoint(2980 + 24 + 6, 24 - 5)));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void The_live_snapper_hands_back_a_point_it_cannot_place_and_snaps_nothing_with_snap_off() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, One1080(), d => d with { Snap = false });
        try
        {
            Assert.Equal(new PxPoint(803, 501), s.Snapper!("images", new PxPoint(803, 501)));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Work_to_pixels_and_back_is_the_same_place() => Sta.Run(() =>
    {
        foreach (var screen in TaskbarsTopAndLeft())
        {
            var px = DrawerScreens.WorkToPx(300, 150, screen);
            var place = DrawerScreens.ToPlace(px, TaskbarsTopAndLeft())!;
            Assert.Equal(300, place.X, 0);
            Assert.Equal(150, place.Y, 0);
        }
    });

    // ---- the pieces and the icons, in work DIPs ----

    [Fact]
    public void A_stack_rect_in_bounds_DIPs_loses_the_work_areas_offset_and_an_icon_rect_in_pixels_divides_by_the_scale()
    {
        var top = TaskbarsTopAndLeft()[0];     // work starts 60 px = 40 DIPs down
        var left = TaskbarsTopAndLeft()[1];    // work starts 100 px = 100 DIPs in
        Assert.Equal(new DrawerRect(100, 10, 200, 80), DrawerScreens.ScreenDipsToWork(new Rect(100, 50, 200, 80), top));
        Assert.Equal(new DrawerRect(20, 50, 200, 80), DrawerScreens.ScreenDipsToWork(new Rect(120, 50, 200, 80), left));
        Assert.Equal(new DrawerRect(200, 200, 76, 98), DrawerScreens.PxToWork(new PxRect(300, 360, 114, 147), top));
        Assert.Equal(new DrawerRect(20, 100, 76, 98), DrawerScreens.PxToWork(new PxRect(3000, 100, 76, 98), left));
    }

    [Fact]
    public void An_icon_rect_read_in_pixels_becomes_the_area_in_work_DIPs_grown_by_the_gap_on_its_own_screen() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            desk.IconReader = () => [new PxRect(300, 360, 114, 147), new PxRect(3000, 100, 76, 98)];
            var (top, left) = (desk.ScreensForTests[0], desk.ScreensForTests[1]);
            desk.Reload();
            var g = DrawerPlacement.Gap;
            Assert.Equal([new DrawerRect(200 - g, 200 - g, 76 + 2 * g, 98 + 2 * g)], desk.IconArea(top));
            Assert.Equal([new DrawerRect(20 - g, 100 - g, 76 + 2 * g, 98 + 2 * g)], desk.IconArea(left));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void The_icon_area_is_read_once_per_invalidation_and_read_again_after_a_reload_or_a_screen_change() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, host) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            int reads = 0;
            desk.IconReader = () => { reads++; return []; };
            desk.Reload();   // the redraw places the drawers, which asks for the primary's icon area
            var (top, left) = (desk.ScreensForTests[0], desk.ScreensForTests[1]);
            desk.IconArea(top);
            desk.IconArea(left);
            desk.IconArea(top);
            Assert.Equal(1, reads);   // one reading serves every screen, and every later ask
            desk.Reload();
            desk.IconArea(left);
            Assert.Equal(2, reads);
            host.RaiseScreensChanged();
            desk.IconArea(top);
            Assert.Equal(3, reads);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void An_icon_belongs_to_the_screen_holding_its_centre_and_is_clipped_to_that_screens_bounds() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            var (top, left) = (desk.ScreensForTests[0], desk.ScreensForTests[1]);
            // Bottom row overhanging the top screen's bottom edge (1620): centre at 1573, so it stays, cut to 120 px tall.
            Assert.Equal(new PxRect(300, 1500, 114, 120), DrawerScreens.ClipToScreen(new PxRect(300, 1500, 114, 147), top));
            Assert.Null(DrawerScreens.ClipToScreen(new PxRect(300, 1500, 114, 147), left));
            // Straddling the seam at x = 2880 with its centre on the second screen: that screen's, cut at its left edge.
            var seam = new PxRect(2850, 100, 76, 98);
            Assert.Null(DrawerScreens.ClipToScreen(seam, top));
            Assert.Equal(new PxRect(2880, 100, 46, 98), DrawerScreens.ClipToScreen(seam, left));
            // Centre off the bottom: another screen's (here, nobody's).
            Assert.Null(DrawerScreens.ClipToScreen(new PxRect(300, 1580, 114, 147), top));

            desk.IconReader = () => [new PxRect(300, 1500, 114, 147), new PxRect(2850, 100, 76, 98)];
            desk.Reload();
            var g = DrawerPlacement.Gap;
            // 150%: 300 px = 200 DIPs across, 1500 - 60 = 1440 px = 960 DIPs down, 114 x 120 px = 76 x 80 DIPs.
            Assert.Equal([new DrawerRect(200 - g, 960 - g, 76 + 2 * g, 80 + 2 * g)], desk.IconArea(top));
            // Second screen (100%, work origin 2980, 0): clipped x from 2880 = -100 in work DIPs, 46 wide.
            Assert.Equal([new DrawerRect(-100 - g, 100 - g, 46 + 2 * g, 98 + 2 * g)], desk.IconArea(left));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drop_reads_the_icons_once_for_its_snap_and_its_lane() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, One1080(), null, "", "Images", "Documents");
        try
        {
            int reads = 0;
            desk.IconReader = () => { reads++; return []; };
            s.RequestPlace("images", new PxPoint(803, 501));
            Assert.Equal(2, reads);   // the drop reads once for its snap and its lane; the save then reloads the Desk, which reads once more (three before the fix)
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    // ---- the reader failing ----

    [Fact]
    public void A_failed_icon_read_falls_back_to_the_default_columns_on_the_primary_only_and_never_throws() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, _, _) = Desk(box, TaskbarsTopAndLeft());
        try
        {
            desk.IconReader = () => throw new System.Runtime.InteropServices.COMException("explorer restarting");
            desk.SpacingReader = () => (49, new PxPoint(114, 147));   // 49 icons, 76 x 98 DIPs a cell at 150%
            desk.Reload();
            var (top, left) = (desk.ScreensForTests[0], desk.ScreensForTests[1]);
            // Work is 1040 DIPs tall: 10 rows of 98, so 5 columns of 76.
            Assert.Equal([new DrawerRect(0, 0, 5 * 76, 1040)], desk.IconArea(top));
            Assert.Empty(desk.IconArea(left));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_null_read_with_no_spacing_either_uses_the_desktops_item_count_and_a_failing_fallback_is_no_area() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\a.txt");
        box.File(@"Desktop\b.txt");
        var (desk, s, _, _) = Desk(box, One1080());
        try
        {
            desk.IconReader = () => null;
            desk.SpacingReader = () => null;
            desk.Reload();
            var area = desk.IconArea(desk.ScreensForTests[0]);
            Assert.All(area, r => Assert.True(r.X == 0 && r.H == 1080 && r.W > 0));
            desk.SpacingReader = () => throw new InvalidOperationException("no shell");
            desk.Reload();
            Assert.Empty(desk.IconArea(desk.ScreensForTests[0]));   // nothing throws, and no guess is made
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drop_with_the_reader_failing_still_saves_a_place_on_screen() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Desk(box, One1080());
        try
        {
            desk.IconReader = () => throw new System.Runtime.InteropServices.COMException("explorer restarting");
            desk.SpacingReader = () => throw new System.Runtime.InteropServices.COMException("explorer restarting");
            s.RequestPlace("images", new PxPoint(500, 300));
            var p = Saved(store, "images");
            Assert.InRange(p.X, 0, 1920 - DrawerPlacement.Width);   // saved, on screen, with no icon area to avoid
            Assert.InRange(p.Y, 0, 1080 - DrawerPlacement.FoldedHeight);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    // ---- arrival through Tidy's slots, around the icons ----

    [Fact]
    public void A_drawer_with_no_place_arrives_by_the_tidy_corner_on_the_primary_work_height() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, host) = Desk(box, One1080(), d => d with { Tidy = new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), KeepClear = false });
        try
        {
            var at = DrawerFreePanelTests.DrawerIslands(host).Single().Island.Anchor;
            Assert.Equal(new Point(DrawerPlacement.Margin, 1080 - DrawerPlacement.Margin - DrawerPlacement.OpenHeight), at);

            // An arrival stays where it arrived for the session (it is saved, or kept in memory when the file cannot be written), so icons
            // that move later do not push it. Arriving clear of the icons is DrawerArrivalTests' A_4_5_file_arrives_through_Tidys_slots_off_the_icons.
            s.Save(s.Settings with { KeepClear = true });
            desk.IconAreaOverride = [new DrawerRect(0, 0, 300, 1080)];
            desk.Reload();
            Assert.Equal(at, DrawerFreePanelTests.DrawerIslands(host).Single().Island.Anchor);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    // ---- the piece ----

    [Fact]
    public void Alt_held_during_the_move_drops_without_snapping_and_releasing_it_first_changes_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = DrawerFreePanelTests.Make(box);
        var asked = new List<bool>();
        s.PlaceRequested += (_, _, canSnap) => asked.Add(canSnap);
        var piece = DrawerFreePanelTests.Piece(s);
        bool alt = true;
        piece.AltDown = () => alt;
        piece.TitleDown(new PxPoint(500, 300), new PxPoint(400, 280), new PxPoint(16, 16));
        piece.TitleMove(new PxPoint(620, 250));
        alt = false;   // let go of Alt a moment before the mouse button
        piece.TitleUp(new PxPoint(620, 250));
        Assert.Equal([false], asked);
        piece.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_title_drop_asks_for_a_snapping_place_when_snap_is_on_and_not_when_it_is_off() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        foreach (bool snap in new[] { true, false })
        {
            var (s, _, _) = DrawerFreePanelTests.Make(box, d => d with { Snap = snap });
            var asked = new List<bool>();
            s.PlaceRequested += (_, _, canSnap) => asked.Add(canSnap);
            var piece = DrawerFreePanelTests.Piece(s);
            piece.TitleDown(new PxPoint(500, 300), new PxPoint(400, 280), new PxPoint(16, 16));
            piece.TitleMove(new PxPoint(620, 250));
            piece.TitleUp(new PxPoint(620, 250));
            Assert.Equal([snap], asked);
            piece.Dispose();
            s.Dispose();
        }
    });
}
