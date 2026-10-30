using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawer piece, themed and named, in the Desk's stacks; redraws wait while a drawer is busy (drawers plan 2, Task 7).</summary>
[Collection("SettingsWindow")]
public sealed class DrawerPieceTests
{
    private static (DrawerService Service, SettingsStore Store, Paths Paths) Make(DrawerSandbox box, params string[] files)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        foreach (var f in files) box.File(Path.Combine(@"Desktop\Drawers\Images", f));
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl drawer.images:tl:1", Font = "" });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store, paths);
    }

    private static DrawerLook Look() => DrawerLook.From(DeskInkMap.Light.Default, null, null, null, new FontFamily("Segoe UI"));

    private static DrawerPiece Piece(DrawerService s)
    {
        var piece = new DrawerPiece(s.Get("images")!, s, Look(), 1);
        piece.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        piece.Arrange(new Rect(piece.DesiredSize));
        piece.UpdateLayout();
        return piece;
    }

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    [Fact]
    public void The_drawer_is_a_named_group_and_each_item_is_named_by_its_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png", "b.png");
        var piece = Piece(s);
        Assert.Equal("Images drawer, 2 items", AutomationProperties.GetName(piece));
        Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Group,
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(piece).GetAutomationControlType());
        var item = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.Equal("a.png", AutomationProperties.GetName(item));
        Assert.Equal("drawer.images", piece.Tag);
        s.Dispose();
    });

    [Fact]
    public void Folded_shows_the_title_with_its_count_and_no_list() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png");
        s.SetFolded("images", true);
        var piece = Piece(s);
        Assert.Equal("Images · 1", piece.TitleText.Text);
        Assert.Equal(Visibility.Collapsed, piece.List.Visibility);
        s.Dispose();
    });

    [Fact]
    public void A_missing_folder_shows_its_line() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        Directory.Delete(Path.Combine(box.Drawers, "Images"));
        s.Reload();
        var piece = Piece(s);
        Assert.Equal(Visibility.Visible, piece.MissingText.Visibility);
        Assert.Equal(DrawerState.MissingLine, piece.MissingText.Text);
        Assert.Equal(Visibility.Collapsed, piece.List.Visibility);
        s.Dispose();
    });

    [Fact]
    public void Pending_items_are_drawn_faded() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png");
        var b = box.File(@"Desktop\b.png");
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, [], [new DrawerMove(b, "images")]);
        s.SetPending(DrawerPlanValidator.Check(plan, box.Roots, s.Settings.Drawers));
        var piece = Piece(s);
        var pending = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.Equal(0.45, pending.Opacity, 3);
        Assert.Equal(1.0, ((ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0)).Opacity, 3);
        s.Dispose();
    });

    [Fact]
    public void The_frame_takes_the_plate_at_a_readable_strength_and_the_title_the_ink() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        var piece = Piece(s);
        var plate = ((SolidColorBrush)piece.Frame.Background).Color;
        Assert.Equal((byte)Math.Round(DrawerLook.MinPlateOpacity * 255), plate.A);
        Assert.Equal((0, 0, 0), (plate.R, plate.G, plate.B));   // light ink: a dark plate
        Assert.Equal(Look().Ink, ((SolidColorBrush)piece.TitleText.Foreground).Color);
        var themed = DrawerLook.From(DeskInkMap.Light.Default, "123456", 0.8, "ff8800", new FontFamily("Segoe UI"));
        Assert.Equal(Color.FromArgb(204, 0x12, 0x34, 0x56), themed.Plate);
        Assert.Equal(Color.FromRgb(0xff, 0x88, 0x00), themed.Accent);
        s.Dispose();
    });

    [Fact]
    public void Streamer_mode_codenames_the_drawer_and_blurs_the_labels() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "salary-2026.xlsx");
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));
        try
        {
            var piece = Piece(s);
            Assert.DoesNotContain("Images", piece.TitleText.Text);
            Assert.DoesNotContain("Images", AutomationProperties.GetName(piece));
            Assert.IsType<BlurEffect>(piece.Resources["Drawer.LabelEffect"]);
            var item = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.DoesNotContain("salary", AutomationProperties.GetName(item));
            var label = (TextBlock)FindNamed(item, "Label");
            Assert.DoesNotContain("salary", AutomationProperties.GetName(label));
        }
        finally { StreamerMode.Reset(); s.Dispose(); }
    });

    [Fact]
    public void The_Desk_draws_a_placed_drawer_and_its_island_takes_clicks_and_focus() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, "a.png");
        var view = new DeskView(Screen(), store.LoadDesk(), new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Drawers = s }, DeskInkMap.Light);
        var islands = DeskClicks.Prepare(view.TakeIslands(), false, new NoClicks());
        var island = islands.Single(i => i.Widgets.Contains("drawer.images"));
        Assert.True(island.TakesClicks);
        Assert.True(island.TakesFocus);
        Assert.False(islands.Single(i => i.Widgets.Contains("clock")).TakesFocus);
        view.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_drawer_word_with_no_drawer_draws_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box);
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl drawer.ghost:tl:1" });
        var view = new DeskView(Screen(), store.LoadDesk(), new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Drawers = s }, DeskInkMap.Light);
        Assert.DoesNotContain(view.TakeIslands(), i => i.Widgets.Contains("drawer.ghost"));
        view.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_redraw_waits_while_a_drawer_is_busy_and_runs_when_it_is_released() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box, "a.png");
        var host = new CountingHost();
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths),
            paths: paths, drawers: s);
        desk.Reload();
        int before = host.Shows;
        var hold = s.Hold();
        desk.Reload();
        Assert.Equal(before, host.Shows);
        hold.Dispose();
        Assert.Equal(before + 1, host.Shows);
        desk.Dispose();
        s.Dispose();
    });

    [Fact]
    public void Arrange_mode_names_a_drawer_by_its_name() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        DrawerService.Current = s;
        try
        {
            Assert.Equal("Images drawer", DeskView.PieceName("drawer.images"));
            Assert.Equal("Drawer", DeskView.PieceName("drawer.ghost"));
            Assert.Equal(SettingsNames.DeskPieceName("clock"), DeskView.PieceName("clock"));
            Assert.Contains("drawer.images", DeskView.DrawnFor(new DeskArrangement("clock:bl drawer.images:tl:1")));
        }
        finally { DrawerService.Current = null; s.Dispose(); }
    });

    private static FrameworkElement FindNamed(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is FrameworkElement { Name: var n } fe && n == name) return fe;
            if (FindNamed(c, name) is { } deeper) return deeper;
        }
        return null!;
    }

    [Fact]
    public void HasDrawer_is_quick_on_a_deep_island_without_a_drawer_and_finds_one_at_the_bottom() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png");
        FrameworkElement Nest(FrameworkElement leaf)
        {
            FrameworkElement cur = leaf;
            for (int i = 0; i < 24; i++)
                cur = i % 2 == 0 ? new Border { Child = cur } : (FrameworkElement)new StackPanel { Children = { cur } };
            return cur;
        }
        var plain = Nest(new TextBlock { Text = "x" });
        var withDrawer = Nest(new DrawerPiece(s.Get("images")!, s, Look(), 1));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var none = DeskClicks.Prepare([new DeskIsland(plain, new Point(), HorizontalAlignment.Left, VerticalAlignment.Top) { Widgets = ["clock"] }], false, new NoClicks());
        var found = DeskClicks.Prepare([new DeskIsland(withDrawer, new Point(), HorizontalAlignment.Left, VerticalAlignment.Top) { Widgets = ["drawer.images"] }], false, new NoClicks());
        clock.Stop();
        Assert.False(none[0].TakesFocus);
        Assert.True(found[0].TakesFocus);
        Assert.True(clock.ElapsedMilliseconds < 2000);
        s.Dispose();
    });

    [Fact]
    public void The_views_tick_while_a_drawer_is_busy_and_a_privacy_change_redraws_past_the_hold() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box, "a.png");
        var host = new CountingHost();
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths),
            paths: paths, drawers: s);
        desk.Reload();
        int before = host.Shows;
        var hold = s.Hold();
        try
        {
            desk.MinuteWithFullDue();
            Assert.Equal(before + 1, host.Shows);      // the in-place tick still shows
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));
            desk.PrivacyChanged();
            Assert.True(host.Shows >= before + 2);      // the redraw ran past the busy hold
        }
        finally { StreamerMode.Reset(); hold.Dispose(); desk.Dispose(); s.Dispose(); }
    });

    private sealed class NoClicks : IDeskClickActions
    {
        public void PlayPause() { }
        public void OpenWeatherCredit() { }
        public void Menu(DeskMenuAction action) { }
        public void LaunchButton(int index) { }
    }

    private sealed class CountingHost : IDeskHost
    {
        public int Shows { get; private set; }
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            return [new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0))];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shows++; IsShowing = true; }
        public void Hide() => IsShowing = false;
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }
}
