using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The four drawer looks (looks and placement plan, Task 4): what each draws, when labels get chips, and the Glass window.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerLookTests
{
    private static readonly FontFamily Font = new("Segoe UI");

    private static DrawerLook L(DrawerLookKind k, int opacity, double wall) =>
        DrawerLook.From(DeskInkMap.Light.Default, null, null, Font, k, opacity, wall);

    private static DrawerService Service(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        box.File(Path.Combine(@"Desktop\Drawers\Images", "a.png"));
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" });
        return new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
    }

    [Fact]
    public void Solid_uses_the_opacity_setting()
    {
        Assert.Equal((byte)Math.Round(0.7 * 255), L(DrawerLookKind.Solid, 70, 0.1).Plate.A);
    }

    [Fact]
    public void The_old_overload_is_Solid_at_no_less_than_55()
    {
        var look = DrawerLook.From(DeskInkMap.Light.Default, null, null, null, Font);
        Assert.Equal(DrawerLookKind.Solid, look.Kind);
        Assert.Equal((byte)Math.Round(0.55 * 255), look.Plate.A);
        Assert.False(look.Chips);
    }

    [Fact]
    public void Outline_and_bare_keep_a_fill_that_still_takes_clicks()
    {
        foreach (var k in new[] { DrawerLookKind.Outline, DrawerLookKind.Bare })
        {
            var look = L(k, 70, 0.1);
            Assert.InRange(look.Plate.A, (byte)1, (byte)5);
        }
    }

    [Fact]
    public void Borders_follow_the_look()
    {
        Assert.Equal((byte)Math.Round(0.7 * 255), L(DrawerLookKind.Outline, 55, 0.1).Border.A);
        Assert.Equal(L(DrawerLookKind.Outline, 55, 0.1).Border, L(DrawerLookKind.Outline, 55, 0.1).HoverBorder);
        var bare = L(DrawerLookKind.Bare, 55, 0.1);
        Assert.Equal(0, bare.Border.A);
        Assert.Equal((byte)Math.Round(0.35 * 255), bare.HoverBorder.A);
        Assert.Equal(0, L(DrawerLookKind.Solid, 55, 0.1).Border.A);
        Assert.Equal((byte)Math.Round(0.25 * 255), L(DrawerLookKind.Glass, 55, 0.1).Edge.A);
        Assert.Equal(0, L(DrawerLookKind.Solid, 55, 0.1).Edge.A);
    }

    [Fact]
    public void Labels_go_on_chips_when_the_wallpaper_is_too_close_to_the_ink()
    {
        // light ink on a light wallpaper: contrast fails, so chips
        Assert.True(L(DrawerLookKind.Bare, 55, 0.9).Chips);
        Assert.True(L(DrawerLookKind.Outline, 55, 0.9).Chips);
        Assert.False(L(DrawerLookKind.Bare, 55, 0.02).Chips);
        Assert.False(L(DrawerLookKind.Solid, 80, 0.9).Chips);   // a plate at 80% carries its own contrast
        Assert.False(L(DrawerLookKind.Solid, 55, 0.9).Chips);   // 55 is the floor: no chips at or above it
        Assert.True(L(DrawerLookKind.Solid, 40, 0.9).Chips);    // a faint plate over a light wallpaper does need them
    }

    [Fact]
    public void Glass_is_lighter_than_solid_at_the_same_setting()
    {
        Assert.True(L(DrawerLookKind.Glass, 80, 0.1).Plate.A < L(DrawerLookKind.Solid, 80, 0.1).Plate.A);
    }

    [Fact]
    public void Every_look_builds_a_piece_with_its_group_name() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var s = Service(box);
        foreach (var k in Enum.GetValues<DrawerLookKind>())
            foreach (double wall in new[] { 0.02, 0.9 })
            {
                var piece = new DrawerPiece(s.Get("images")!, s, L(k, 55, wall), 1);
                piece.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                piece.Arrange(new Rect(piece.DesiredSize));
                piece.UpdateLayout();
                Assert.Equal("Images drawer, 1 item", AutomationProperties.GetName(piece));
                Assert.Equal(DrawerPiece.FrameWidth, piece.Frame.Width);
            }
        s.Dispose();
    });

    [Fact]
    public void Bare_has_a_smaller_title_and_chips_are_only_drawn_when_the_look_asks() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var s = Service(box);
        var bare = new DrawerPiece(s.Get("images")!, s, L(DrawerLookKind.Bare, 55, 0.9), 1);
        var solid = new DrawerPiece(s.Get("images")!, s, L(DrawerLookKind.Solid, 55, 0.9), 1);
        Assert.True(bare.TitleText.FontSize < solid.TitleText.FontSize);
        Assert.True(bare.TitleBar.Height < solid.TitleBar.Height);
        var chip = Assert.IsType<SolidColorBrush>(bare.Resources["Drawer.Chip"]);
        Assert.Equal(178, chip.Color.A);
        Assert.Same(Brushes.Transparent, solid.Resources["Drawer.Chip"]);
        s.Dispose();
    });

    [Fact]
    public void Streamer_mode_still_blurs_the_labels_in_every_look() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var s = Service(box);
        try
        {
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));
            foreach (var k in Enum.GetValues<DrawerLookKind>())
            {
                var piece = new DrawerPiece(s.Get("images")!, s, L(k, 55, 0.1), 1);
                Assert.IsType<BlurEffect>(piece.Resources["Drawer.LabelEffect"]);
            }
        }
        finally { StreamerMode.Reset(); }
        s.Dispose();
    });

    // ---- the Glass window ----

    private static readonly PxRect Bounds = new(0, 0, 1920, 1080);
    private static DeskScreen Screen() => new("TEST", Bounds, Bounds, 1.0, true, DeskInsets.None);

    private static DeskIsland Island(bool drawer, bool glass) =>
        new(new Border { Width = 360, Height = 330, Background = Brushes.White }, new Point(500, 300), HorizontalAlignment.Left, VerticalAlignment.Top)
        { Drawer = drawer, Glass = glass, TakesClicks = drawer };

    [Fact]
    public void A_Glass_drawer_window_is_the_panel_with_no_pad_and_the_panel_stays_put() => Sta.Run(() =>
    {
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        try
        {
            host.Show([new DeskSurface(Screen(), [Island(drawer: true, glass: false), Island(drawer: true, glass: true)])]);
            var plain = DeskNative.Rect(host.Handles[0])!.Value;
            var glass = DeskNative.Rect(host.Handles[1])!.Value;
            // Without Glass the window has the 16 px room around the panel; with it the window is the panel, at the same panel position.
            Assert.Equal((500 - 16, 300 - 16, 360 + 32, 330 + 32), (plain.X, plain.Y, plain.Width, plain.Height));
            Assert.Equal((500, 300, 360, 330), (glass.X, glass.Y, glass.Width, glass.Height));
        }
        finally { host.Dispose(); }
    });

    [Fact]
    public void A_Glass_flag_on_a_widget_island_changes_nothing() => Sta.Run(() =>
    {
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        try
        {
            host.Show([new DeskSurface(Screen(), [Island(drawer: false, glass: true), Island(drawer: true, glass: false)])]);
            Assert.All(host.Blurred, on => Assert.False(on));
            Assert.Equal(360 + 32, DeskNative.Rect(host.Handles[0])!.Value.Width);
        }
        finally { host.Dispose(); }
    });

    [Fact]
    public void Leaving_Glass_takes_the_pad_back_and_the_blur_off() => Sta.Run(() =>
    {
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        try
        {
            host.Show([new DeskSurface(Screen(), [Island(drawer: true, glass: true)])]);
            Assert.Equal(360, DeskNative.Rect(host.Handles[0])!.Value.Width);
            host.Show([new DeskSurface(Screen(), [Island(drawer: true, glass: false)])]);
            Assert.Equal(360 + 32, DeskNative.Rect(host.Handles[0])!.Value.Width);
            Assert.False(host.Blurred[0]);
        }
        finally { host.Dispose(); }
    });

    [Fact]
    public void The_backdrop_never_throws_on_a_window_that_is_not_there()
    {
        Assert.False(DrawerBackdrop.Apply(IntPtr.Zero, 0x01000000));
        Assert.False(DrawerBackdrop.Apply(new IntPtr(0x7FFF0001), 0x01000000));
        DrawerBackdrop.Clear(IntPtr.Zero);
        DrawerBackdrop.Clear(new IntPtr(0x7FFF0001));
    }

    [Fact]
    public void The_near_clear_tint_is_alpha_1_in_ABGR_order()
    {
        Assert.Equal(0x01332211, DrawerBackdrop.NearClearTint(0x11, 0x22, 0x33));
    }
}
