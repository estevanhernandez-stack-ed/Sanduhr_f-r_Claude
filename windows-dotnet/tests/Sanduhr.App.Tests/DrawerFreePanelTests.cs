using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Drawers as free panels (drawers amendment 1, Task 8B): placed per monitor by the controller, never by the Desk's stacks; moved by
/// the title; the empty frame lets clicks through; drawer windows above widget windows.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerFreePanelTests
{
    private const string SecondId = @"\\?\DISPLAY#SECOND#1";

    /// <summary>A 3440x1440 primary at 100% with a 40 px taskbar at the bottom, and a 2880x1620 monitor at 150% to its right whose work area
    /// starts 60 px down (a taskbar at its top).</summary>
    private static IReadOnlyList<DeskScreen> TwoScreens()
    {
        var b1 = new PxRect(0, 0, 3440, 1440);
        var w1 = new PxRect(0, 0, 3440, 1400);
        var b2 = new PxRect(3440, 0, 2880, 1620);
        var w2 = new PxRect(3440, 60, 2880, 1560);
        return
        [
            new DeskScreen("ONE", b1, w1, 1.0, true, TaskbarClearance.Insets(b1, w1, [], 1.0), @"\\?\DISPLAY#MAIN#0"),
            new DeskScreen("TWO", b2, w2, 1.5, false, TaskbarClearance.Insets(b2, w2, [], 1.5), SecondId),
        ];
    }

    // ---- DrawerScreens ----

    [Fact]
    public void ToAnchor_finds_the_monitor_and_offsets_from_its_work_area_in_its_DIPs()
    {
        var screens = TwoScreens();
        var (main, a) = DrawerScreens.ToAnchor(new DrawerPlace(DeskMonitors.Main, 100, 50), screens);
        Assert.Same(screens[0], main);
        Assert.Equal(new Point(100, 50), a);

        var (second, b) = DrawerScreens.ToAnchor(new DrawerPlace(SecondId, 100, 50), screens);
        Assert.Same(screens[1], second);
        Assert.Equal(100, b.X, 6);
        Assert.Equal(60 / 1.5 + 50, b.Y, 6);
    }

    [Fact]
    public void ToAnchor_takes_the_primary_for_a_monitor_not_connected_and_clamps_into_the_work_area()
    {
        var screens = TwoScreens();
        var (s, a) = DrawerScreens.ToAnchor(new DrawerPlace(@"\\?\DISPLAY#GONE#9", 99999, -40), screens);
        Assert.Same(screens[0], s);
        Assert.Equal(new Point(3440 - DrawerPlacement.Width, 0), a);

        var (s2, b) = DrawerScreens.ToAnchor(new DrawerPlace(SecondId, 5000, 5000), screens);
        Assert.Same(screens[1], s2);
        Assert.Equal(2880 / 1.5 - DrawerPlacement.Width, b.X, 6);
        Assert.Equal(60 / 1.5 + (1560 / 1.5 - DrawerPlacement.FoldedHeight), b.Y, 6);
    }

    [Fact]
    public void ToPlace_takes_the_screen_holding_the_point_in_its_DIPs_and_the_primary_as_main()
    {
        var screens = TwoScreens();
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 300, 120), DrawerScreens.ToPlace(new PxPoint(300, 120), screens));
        var p = DrawerScreens.ToPlace(new PxPoint(3440 + 300, 60 + 150), screens);
        Assert.Equal(SecondId, p.Monitor);
        Assert.Equal(200, p.X, 6);
        Assert.Equal(100, p.Y, 6);
        // And back: the anchor is where the window's content was dropped, in that screen's DIPs.
        var (s, a) = DrawerScreens.ToAnchor(p, screens);
        Assert.Same(screens[1], s);
        Assert.Equal(new PxPoint(3440 + 300, 60 + 150), new PxPoint(s.Bounds.X + (int)Math.Round(a.X * s.Scale), s.Bounds.Y + (int)Math.Round(a.Y * s.Scale)));
    }

    [Fact]
    public void ToPlace_clamps_a_point_off_every_screen_onto_the_nearest_one()
    {
        var screens = TwoScreens();
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 0, 0), DrawerScreens.ToPlace(new PxPoint(-500, -300), screens));
        var far = DrawerScreens.ToPlace(new PxPoint(3440 + 2880 + 400, 2000), screens);
        Assert.Equal(SecondId, far.Monitor);
        Assert.Equal(2880 / 1.5 - DrawerPlacement.Width, far.X, 6);
        Assert.Equal(1560 / 1.5 - DrawerPlacement.FoldedHeight, far.Y, 6);
    }

    // ---- the piece ----

    private static (DrawerService Service, SettingsStore Store, Paths Paths) Make(DrawerSandbox box, Func<DrawerSettings, DrawerSettings>? edit = null,
        params string[] names)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local"));
        var store = new SettingsStore(paths);
        if (names.Length == 0) names = ["Images"];
        var defs = names.Select(n => new DrawerDef(DrawerIds.From(n), n, box.Dir(@"Desktop\Drawers\" + n), true)).ToArray();
        box.File(@"Desktop\Drawers\" + names[0] + @"\a.png");
        var settings = DrawerSettings.Default with { Enabled = true, Drawers = defs };
        store.SaveDrawers(edit is null ? settings : edit(settings));
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store, paths);
    }

    private static DrawerPiece Piece(DrawerService s, string id = "images")
    {
        var look = DrawerLook.From(DeskInkMap.Light.Default, null, null, null, new FontFamily("Segoe UI"));
        var piece = new DrawerPiece(s.Get(id)!, s, look, 1);
        piece.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        piece.Arrange(new Rect(piece.DesiredSize));
        piece.UpdateLayout();
        return piece;
    }

    [Fact]
    public void Only_items_the_title_and_its_lines_take_clicks_unless_a_button_is_down_busy_or_focused() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        var piece = Piece(s);
        var title = piece.TitleBar.TranslatePoint(new Point(40, 10), piece);
        var item = ((ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0)).TranslatePoint(new Point(10, 10), piece);
        var empty = piece.List.TranslatePoint(new Point(piece.List.ActualWidth - 12, piece.List.ActualHeight - 12), piece);
        var frameEdge = new Point(piece.ActualWidth - 3, piece.ActualHeight / 2);

        Assert.True(DrawerClickThrough.TakesClicks(piece, title, false, false, false));
        Assert.True(DrawerClickThrough.TakesClicks(piece, item, false, false, false));
        Assert.False(DrawerClickThrough.TakesClicks(piece, empty, false, false, false));
        Assert.False(DrawerClickThrough.TakesClicks(piece, frameEdge, false, false, false));
        Assert.False(DrawerClickThrough.TakesClicks(piece, new Point(-30, -30), false, false, false));

        Assert.True(DrawerClickThrough.TakesClicks(piece, empty, buttonDown: true, false, false));
        Assert.True(DrawerClickThrough.TakesClicks(piece, empty, false, busy: true, false));
        Assert.True(DrawerClickThrough.TakesClicks(piece, empty, false, false, focused: true));

        piece.Say("1 moved, 1 skipped.");
        piece.UpdateLayout();
        var status = piece.Status.TranslatePoint(new Point(10, 5), piece);
        Assert.True(DrawerClickThrough.TakesClicks(piece, status, false, false, false));
        piece.Dispose();
        s.Dispose();
    });

    [Fact]
    public void The_missing_line_takes_clicks() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        Directory.Delete(Path.Combine(box.Drawers, "Images"), recursive: true);
        s.Reload();
        var piece = Piece(s);
        var line = piece.MissingText.TranslatePoint(new Point(10, 5), piece);
        Assert.True(DrawerClickThrough.TakesClicks(piece, line, false, false, false));
        piece.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_title_click_without_moving_folds_and_a_drag_past_the_distance_asks_for_a_place_holding_the_service_busy() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        var asked = new List<(string Id, PxPoint At)>();
        s.PlaceRequested += (id, at) => asked.Add((id, at));
        var piece = Piece(s);

        piece.TitleDown(new PxPoint(500, 300), window: new PxPoint(400, 280), offset: new PxPoint(16, 16));
        piece.TitleMove(new PxPoint(501, 301));   // inside the drag distance: still a click
        Assert.False(s.Busy);
        piece.TitleUp(new PxPoint(501, 301));
        Assert.Contains("images", s.Settings.Folded);
        Assert.Empty(asked);

        piece.Dispose();
        piece = Piece(s);
        piece.TitleDown(new PxPoint(500, 300), window: new PxPoint(400, 280), offset: new PxPoint(16, 16));
        piece.TitleMove(new PxPoint(560, 340));
        Assert.True(s.Busy);
        piece.TitleMove(new PxPoint(620, 250));
        piece.TitleUp(new PxPoint(620, 250));
        Assert.False(s.Busy);
        Assert.Equal([("images", new PxPoint(400 + 120 + 16, 280 - 50 + 16))], asked);
        Assert.Contains("images", s.Settings.Folded);   // a move never folds
        piece.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_lost_capture_mid_move_still_places_and_releases_the_hold_and_one_before_moving_does_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        var asked = new List<PxPoint>();
        s.PlaceRequested += (_, at) => asked.Add(at);
        var piece = Piece(s);
        piece.TitleDown(new PxPoint(10, 10), new PxPoint(0, 0), new PxPoint(16, 16));
        piece.TitleLost();
        Assert.Empty(asked);
        Assert.DoesNotContain("images", s.Settings.Folded);

        piece.TitleDown(new PxPoint(10, 10), new PxPoint(0, 0), new PxPoint(16, 16));
        piece.TitleMove(new PxPoint(80, 90));
        piece.TitleLost();
        Assert.False(s.Busy);
        Assert.Equal([new PxPoint(70 + 16, 80 + 16)], asked);
        piece.Dispose();
        s.Dispose();
    });

    // ---- the controller ----

    private sealed class TwoScreenHost : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public IReadOnlyList<DeskSurface> Shown { get; private set; } = [];
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];
        public IReadOnlyList<DeskScreen> Screens() => TwoScreens();
        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shown = surfaces; IsShowing = true; }
        public void Hide() { Shown = []; IsShowing = false; }
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }

    private static (DeskController Desk, TwoScreenHost Host) Start(SettingsStore store, Paths paths, DrawerService s)
    {
        var host = new TwoScreenHost();
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths),
            paths: paths, drawers: s);
        desk.Reload();
        return (desk, host);
    }

    private static List<(DeskScreen Screen, DeskIsland Island)> DrawerIslands(TwoScreenHost host) =>
        [.. host.Shown.SelectMany(sf => sf.Islands.Where(i => i.Content is DrawerPiece).Select(i => (sf.Screen, i)))];

    private static string IdOf(DeskIsland i) => ((DrawerPiece)i.Content).State.Def.Id;

    [Fact]
    public void Each_shown_drawer_is_one_island_on_its_saved_monitor_and_one_with_no_place_arrives_top_right_of_the_primary() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box, d => d with
        {
            Places = new Dictionary<string, DrawerPlace> { ["images"] = new(SecondId, 100, 50), ["ghost"] = new(DeskMonitors.Main, 3080, 24) },
            Hidden = ["music"],
        }, "Images", "Documents", "Music");
        var (desk, host) = Start(store, paths, s);
        try
        {
            var drawers = DrawerIslands(host);
            Assert.Equal(["documents", "images"], drawers.Select(d => IdOf(d.Island)).Order());
            var images = drawers.Single(d => IdOf(d.Island) == "images");
            Assert.Equal(SecondId, images.Screen.Id);
            Assert.Equal(100, images.Island.Anchor.X, 6);
            Assert.Equal(60 / 1.5 + 50, images.Island.Anchor.Y, 6);
            var docs = drawers.Single(d => IdOf(d.Island) == "documents");
            Assert.True(docs.Screen.Primary);
            // The ghost's saved place is not a drawer's: the arrival does not step below it.
            Assert.Equal(new Point(3440 - DrawerPlacement.Margin - DrawerPlacement.Width, DrawerPlacement.Margin), docs.Island.Anchor);
            foreach (var (_, i) in drawers)
            {
                Assert.True(i.Drawer);
                Assert.True(i.TakesClicks);
                Assert.True(i.TakesFocus);
                Assert.Equal((HorizontalAlignment.Left, VerticalAlignment.Top), (i.Horizontal, i.Vertical));
            }
            // The Desk's own pieces come first on each screen, the drawers after them.
            foreach (var surface in host.Shown)
                Assert.True(surface.Islands.SkipWhile(i => !i.Drawer).All(i => i.Drawer));
            Assert.Contains(host.Shown.SelectMany(sf => sf.Islands), i => i.Widgets.Contains("clock") && !i.Drawer);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_saved_place_off_its_screen_is_clamped_and_a_preview_drawer_arrives_below_the_saved_column() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        double x = 3440 - DrawerPlacement.Margin - DrawerPlacement.Width;
        var (s, store, paths) = Make(box, d => d with
        {
            Places = new Dictionary<string, DrawerPlace> { ["images"] = new(DeskMonitors.Main, x, 24), ["documents"] = new(DeskMonitors.Main, 99999, 99999) },
        }, "Images", "Documents");
        var fresh = new DrawerDef("archives", "Archives", Path.Combine(box.Drawers, "Archives"), true);
        s.SetPending(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [fresh], []), box.Roots, s.Settings.Drawers));
        var (desk, host) = Start(store, paths, s);
        try
        {
            var drawers = DrawerIslands(host);
            Assert.Equal(new Point(3440 - DrawerPlacement.Width, 1400 - DrawerPlacement.FoldedHeight),
                drawers.Single(d => IdOf(d.Island) == "documents").Island.Anchor);
            var archives = drawers.Single(d => IdOf(d.Island) == "archives");
            Assert.True(archives.Screen.Primary);
            Assert.Equal(new Point(x, 24 + DrawerPlacement.OpenHeight + DrawerPlacement.Gap), archives.Island.Anchor);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Drawers_off_draw_no_drawer_and_the_Desk_view_never_draws_a_drawer_word() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box, d => d with { Enabled = false });
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl drawer.images:tl:1" });
        var (desk, host) = Start(store, paths, s);
        try
        {
            Assert.Empty(DrawerIslands(host));
            Assert.DoesNotContain(host.Shown.SelectMany(sf => sf.Islands), i => i.Widgets.Contains("drawer.images"));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_place_request_saves_the_screen_holding_the_point_clamped() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box);
        var (desk, host) = Start(store, paths, s);
        try
        {
            s.RequestPlace("images", new PxPoint(3440 + 300, 60 + 150));
            var saved = store.LoadDrawers().Places["images"];
            Assert.Equal(SecondId, saved.Monitor);
            Assert.Equal((200.0, 100.0), (Math.Round(saved.X, 6), Math.Round(saved.Y, 6)));
            var images = DrawerIslands(host).Single();
            Assert.Equal(SecondId, images.Screen.Id);

            s.RequestPlace("images", new PxPoint(-900, 5000));
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, 0, 1400 - DrawerPlacement.FoldedHeight), store.LoadDrawers().Places["images"]);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    // ---- the host's z-order ----

    [Fact]
    public void Drawers_go_to_the_bottom_first_so_they_sit_above_widgets_and_Show_desktop_lifts_them_last()
    {
        Assert.Equal([1, 3, 0, 2], DesktopLayerHost.DrawersFirst([false, true, false, true]));
        Assert.Equal([0, 2, 1, 3], DesktopLayerHost.DrawersLast([false, true, false, true]));
        Assert.Equal([0, 1], DesktopLayerHost.DrawersFirst([false, false]));
    }

    [Fact]
    public void On_the_real_host_a_drawer_window_sits_above_every_widget_window() => Sta.Run(() =>
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var screen = new DeskScreen("TEST", bounds, bounds, 1.0, true, DeskInsets.None);
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        try
        {
            DeskIsland Box(double x, bool drawer) =>
                new(new Border { Width = 120, Height = 40, Background = Brushes.White }, new Point(x, 300), HorizontalAlignment.Left, VerticalAlignment.Top)
                { Drawer = drawer, TakesClicks = drawer };
            host.Show([new DeskSurface(screen, [Box(100, false), Box(300, false), Box(500, true)])]);
            var h = host.Handles;
            Assert.True(Interop.DeskNative.IsBelow(h[0], h[2]));
            Assert.True(Interop.DeskNative.IsBelow(h[1], h[2]));
        }
        finally { host.Dispose(); }
    });
}
