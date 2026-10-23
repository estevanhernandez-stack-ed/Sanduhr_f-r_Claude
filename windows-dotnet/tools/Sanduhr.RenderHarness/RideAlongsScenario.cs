using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The 3.8 ride-alongs scenario (spec 3.8, section 7): F, the fold: a left stack slanted -18 and a right stack +18, composited the
// way the host places islands (each island's window sized for its slanted face), then Arrange Desk over the same layout with the
// slanted outlines and a drag ghost; E, the weather piece under a nickname, with Settings, Weather showing "Show it as"; G, the
// bars mid-song with peak caps, gradient bars, and the calm bars Windows' animations-off asks for. Sample data only: no Desk
// windows, no media sessions, no meter, no network (the weather reading and the levels are made up).
public static partial class Program
{
    private static async Task RenderRideAlongs(WidgetViewModel vm, Assembly asm)
    {
        var paths = new Paths();
        var store = new SettingsStore(paths);
        var now = new DateTimeOffset(2026, 10, 22, 21, 9, 0, TimeSpan.Zero);
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var fonts = new DeskFonts();
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 34, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, 63, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);
        var reading = new WeatherReading(72, 2, 80, 61, 80, 75, 60, now.AddMinutes(-5));
        var track = new NowPlayingInfo
        {
            Title = "Midnight City", Artist = "M83", Duration = 243, Elapsed = 101, ElapsedAt = now, Playing = true, AppId = "Spotify.exe",
        };
        // A made-up run of peak levels: loud, then a dip, so the caps hang above falling bars.
        float[] run = [0.2f, 0.5f, 0.9f, 0.7f, 0.95f, 0.6f, 0.8f, 0.4f, 0.85f, 0.3f, 0.75f, 0.5f, 0.9f, 0.35f, 0.65f, 0.2f, 0.1f, 0.05f, 0.08f, 0.04f,
            0.02f, 0f, 0f, 0.6f, 0.3f, 0f, 0f, 0f, 0f, 0f];
        var telemetry = new TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        var neon = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "22d3ee,a855f7,ec4899", true), null, null);
        var dark = Color.FromRgb(0x10, 0x14, 0x1c);

        (IReadOnlyList<DeskIsland> Islands, DeskView View, AudioLevelSource Audio) Draw(string layout, bool gradient, bool calm, string nickname, DeskInkMap inks)
        {
            var audio = new AudioLevelSource { CalmNow = () => calm };
            var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Plate = false }).Normalized();
            var view = new DeskView(screen, settings, fonts);
            view.Update(new DeskFrame
            {
                Now = now, Usage = usage, Track = track, Bars = true, BarsGradient = gradient, Audio = audio, Telemetry = telemetry,
                WeatherOn = true, Place = place, Weather = reading, WeatherNickname = nickname,
            }, inks);
            foreach (var l in run) audio.Pump(l * l);
            return (view.TakeIslands(), view, audio);
        }

        // ---- F + E + G: the Neon Column fold, two faces meeting, the weather as Funky Town, gradient bars with caps ----
        const string fold = "clock:ml:1:-18 weather:ml:1:-18 nowPlaying:ml:1:-18 telemetry:mr:1:18 claude:mr:1:18 meters:mr:1:18";
        foreach (var (name, inks) in new[] { ("neon", new DeskInkMap(neon)), ("light", DeskInkMap.Light) })
        {
            var (islands, view, audio) = Draw(fold, gradient: name == "neon", calm: false, "Funky Town", inks);
            foreach (var island in islands)
            {
                // A stack with now playing comes wrapped in its reserve frame: the slant is on the stack inside it.
                var face = island.Content is Decorator { Child: FrameworkElement inner } && island.Content.LayoutTransform.Value.IsIdentity ? inner : island.Content;
                Log.Add($"ridealongs fold-{name}: island [{string.Join(",", island.Widgets)}] slant {face.LayoutTransform.Value}");
            }
            SaveScaled(Composite(islands, dark), $"ridealongs-fold-{name}", 1.0);
            await Settle();
            view.Dispose();
            audio.Dispose();
        }

        // ---- the same stacks upright, for the before picture, with the place's own name ----
        {
            var (islands, view, audio) = Draw(fold.Replace(":1:-18", "").Replace(":1:18", ""), gradient: false, calm: false, "", DeskInkMap.Light);
            SaveScaled(Composite(islands, dark), "ridealongs-upright-light", 1.0);
            await Settle();
            view.Dispose();
            audio.Dispose();
        }

        // ---- G close up: the bars alone, lively with caps, gradient, and calm ----
        foreach (var (name, gradient, calm) in new[] { ("lively", false, false), ("gradient", true, false), ("calm", false, true) })
        {
            var (islands, view, audio) = Draw("nowPlaying:bl:1.6", gradient, calm, "", new DeskInkMap(neon));
            var island = islands.Single();
            var host = new Border { Child = island.Content, Background = new SolidColorBrush(dark), Padding = new Thickness(24) };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            SaveScaled(host, $"ridealongs-bars-{name}", 2.0);
            Log.Add($"ridealongs bars-{name}: calm={audio.Calm}");
            await Settle();
            view.Dispose();
            audio.Dispose();
        }

        // ---- Arrange Desk over the fold: outlines along the slant, and a drag ghost that keeps it ----
        {
            var settings = (DeskSettings.Default with { Enabled = true, Layout = fold }).Normalized();
            var frame = new DeskFrame { Usage = usage, Now = now, Telemetry = telemetry, WeatherOn = true, Place = place, Weather = reading, WeatherNickname = "Funky Town" };
            var content = new DeskArrangeContent(screen, settings, fonts, frame, new DeskArrangement(fold), primary: true);
            var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x24, 0x30)) };
            root.Children.Add(content);
            root.Measure(new Size(1920, 1080));
            root.Arrange(new Rect(0, 0, 1920, 1080));
            await Settle();
            Save(root, "ridealongs-arrange-fold");
            var clock = content.PieceRects["clock"];
            Log.Add($"ridealongs arrange: clock box {clock}; box's bottom right hits '{content.PieceAt(new Point(clock.Right - 3, clock.Bottom - 3)) ?? "nothing"}', bottom left hits '{content.PieceAt(new Point(clock.Left + 3, clock.Bottom - 3))}'");
            content.BeginDrag("clock", new Point(clock.X + clock.Width / 2, clock.Y + clock.Height / 2));
            content.MoveDrag(new Point(900, 300));
            await Settle();
            Save(root, "ridealongs-arrange-drag");
            content.CancelDrag();
            content.Dispose();
        }
        telemetry.Dispose();

        // ---- Settings, Weather with the nickname, and Now playing with Gradient bars ----
        store.SaveDeskWeather(new DeskWeatherSettings(place, WeatherUnit.Fahrenheit, "Funky Town"));
        store.SaveNowPlaying(store.LoadNowPlaying() with { Bars = true, BarsGradient = true });
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        AssertOnlySampleAccounts(svm);
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        sw.Height = 1100;
        await Settle();
        swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Weather, SettingsIndex.WeatherPlace]);
        await Settle();
        Save((FrameworkElement)sw.Content, "ridealongs-settings-weather");
        swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.NowPlaying, SettingsIndex.NowPlayingBars]);
        await Settle();
        Save((FrameworkElement)sw.Content, "ridealongs-settings-nowplaying-bars");
        sw.Close();
        await Settle();
    }
}
