using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>Theme match: the Desk draws a theme's look from the frame (plate tint, glow, Now playing's gradient, the meters' colors,
/// track and tick); with none of it set it draws its own look exactly as before.</summary>
public sealed class DeskThemeLookViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static DeskView View(string layout, DeskFrame frame, bool plate = false)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI", Plate = plate }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(frame, DeskInkMap.Light);
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex)!;

    private static DeskUsage Usage() => new(
        [new DeskTier(TierModel.FiveHour, 42, null), new DeskTier(TierModel.SevenDay, 63, null)], Now.AddMinutes(-1), false, false, null);

    [Fact]
    public void The_plate_takes_the_frames_tint_and_without_one_stays_black() => Sta.Run(() =>
    {
        Border Plate(DeskView v) => All<Border>(v).Single(b => b.Effect is BlurEffect);
        var own = View("clock:bl", new DeskFrame { Now = Now }, plate: true);
        Assert.Equal(Color.FromArgb(0x4C, 0, 0, 0), ((SolidColorBrush)Plate(own).Background).Color);
        var themed = View("clock:bl", new DeskFrame { Now = Now, PlateHex = "0a0a0a" }, plate: true);
        Assert.Equal(Color.FromArgb(0x4C, 0x0a, 0x0a, 0x0a), ((SolidColorBrush)Plate(themed).Background).Color);
    });

    [Fact]
    public void The_message_glow_takes_the_frames_color() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = true, Color = "6c63ff,e8e4dc" };
        var frame = new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) { GlowHex = "38bdf8" } };
        var view = View("message:tl", frame);
        var line = All<TextBlock>(view).Single(t => t.Text == "keep building.");
        Assert.Equal(C("38bdf8"), Assert.IsType<DropShadowEffect>(line.Effect).Color);
        var own = View("message:tl", frame with { Message = frame.Message! with { GlowHex = null } });
        var ownLine = All<TextBlock>(own).Single(t => t.Text == "keep building.");
        Assert.Equal(C("e8e4dc"), Assert.IsType<DropShadowEffect>(ownLine.Effect).Color);   // the middle stop, as before
    });

    [Fact]
    public void Meters_draw_the_themes_bars_track_and_tick() => Sta.Run(() =>
    {
        var paced = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 42, Now.AddHours(2).ToString("o")), new DeskTier(TierModel.SevenDay, 63, null)], Now.AddMinutes(-1), false, false, null);
        var frame = new DeskFrame { Now = Now, Usage = paced, MeterStops = ["6c63ff", "e8e4dc"], MeterTrackHex = "2a2a2a", PaceHex = "ff6b6b" };
        var view = View("meters:tr", frame);
        var meters = All<FrameworkElement>(view).First(e => e.Tag as string == "meters");
        var rects = All<Rectangle>(meters).ToList();
        var fill = rects.First(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Width > 2);
        Assert.Equal([C("6c63ff"), C("e8e4dc")], Assert.IsType<LinearGradientBrush>(fill.Fill).GradientStops.Select(s => s.Color));
        Assert.Contains(rects, r => r.Fill is SolidColorBrush b && b.Color == C("2a2a2a"));   // the track
        Assert.Contains(rects, r => r.Width == 2 && r.Fill is SolidColorBrush b && b.Color == C("ff6b6b"));   // the pace tick
    });

    [Fact]
    public void Now_playings_position_bar_wears_the_frames_gradient() => Sta.Run(() =>
    {
        var track = new NowPlayingInfo { Title = "Song", Artist = "Band", Duration = 200, Elapsed = 50, ElapsedAt = Now, Playing = false };
        var view = View("nowPlaying:br", new DeskFrame { Now = Now, Track = track, NowPlayingStops = ["6c63ff", "e8e4dc"] });
        var bar = All<Rectangle>(view).First(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Fill is LinearGradientBrush);
        var g = (LinearGradientBrush)bar.Fill;
        Assert.Equal(BrushMappingMode.Absolute, g.MappingMode);
        Assert.Equal([C("6c63ff"), C("e8e4dc")], g.GradientStops.Select(s => s.Color));
    });

    [Fact]
    public void Ticks_past_a_low_Warn_at_light_the_signal_in_the_lit_cells() => Sta.Run(() =>
    {
        var usage = new DeskUsage([new DeskTier(TierModel.FiveHour, 40, null)], Now.AddMinutes(-1), false, false, null);
        var view = View("meters:tr:1:0:ticks", new DeskFrame { Now = Now, Usage = usage, WarnAt = 30, UrgentAt = 90 });
        var canvas = All<Canvas>(All<FrameworkElement>(view).First(e => e.Tag as string == "meters")).Single();
        var lit = canvas.Children.OfType<Rectangle>().Where(r => r.Width != 2 && r.Fill is not null)
            .Select(r => ((SolidColorBrush)r.Fill).Color).ToList();
        Assert.Equal(4, lit.Count);
        Assert.Contains(C("facc15"), lit);
        Assert.DoesNotContain(Colors.White, lit);   // Light ink's base: no lit cell is pure base ink
    });

    [Fact]
    public void A_songs_own_look_keeps_its_solid_bars_under_a_followed_theme() => Sta.Run(() =>
    {
        var track = new NowPlayingInfo { Title = "Song", Artist = "Band", Duration = 200, Elapsed = 50, ElapsedAt = Now, Playing = false };
        List<Brush?> Bars(IReadOnlyList<string>? themed)
        {
            var settings = (DeskSettings.Default with { Enabled = true, Layout = "nowPlaying:bl", Font = "Segoe UI" }).Normalized();
            var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
            view.Update(new DeskFrame
            {
                Now = Now, Track = track, Bars = true, Audio = new AudioLevelSource(),
                Look = new NowPlayingLookDraw(["ff2a6d", "05d9e8"], LookStyle.Plain), NowPlayingStops = themed,
            }, DeskInkMap.Light);
            var cells = All<Canvas>(view).SelectMany(c => c.Children.OfType<Rectangle>()).Where(r => r.Height <= 1.5).Select(r => (Brush?)r.Fill).ToList();
            view.Dispose();
            return cells;
        }
        var plain = Bars(null);
        var followed = Bars(["6c63ff", "e8e4dc"]);
        Assert.NotEmpty(plain);
        Assert.All(plain, b => Assert.IsType<SolidColorBrush>(b));
        Assert.Equal(plain.Select(b => ((SolidColorBrush)b!).Color), followed.Select(b => Assert.IsType<SolidColorBrush>(b).Color));
    });
}
