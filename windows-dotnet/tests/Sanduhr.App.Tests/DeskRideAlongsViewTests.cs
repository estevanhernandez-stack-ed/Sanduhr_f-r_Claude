using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Path = System.IO.Path;
using Polygon = System.Windows.Shapes.Polygon;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>
/// The 3.8 ride-alongs on the Desk itself: a slanted stack (#314) is drawn as one sheared face, its island is sized for the slant and
/// Arrange Desk hits and outlines the slant; the weather piece shows the nickname (#315) and Settings, Weather keeps it; the bars ease,
/// carry peak caps and fade with gradient bars, and stay calm with Windows' animations off. Over a scratch AppData.
/// </summary>
public sealed class DeskRideAlongsViewTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-ridealong-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

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

    private static DeskUsage Usage()
    {
        string iso = Now.AddHours(2).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        return new DeskUsage([new DeskTier(TierModel.FiveHour, 42, iso)], Now.AddMinutes(-1), false, false, null);
    }

    // ---- F. the fold ----

    [Fact]
    public void ASlantedStackIsOneShearedFaceAndItsNeighboursStayUpright() => Sta.Run(() =>
    {
        var view = View("clock:bl:1:-18 claude:bl:1:-18 meters:tr", new DeskFrame { Usage = Usage(), Now = Now });
        var quads = view.PieceQuads();
        Assert.False(quads["clock"].IsUpright);
        Assert.False(quads["claude"].IsUpright);
        Assert.True(quads["meters"].IsUpright);
        // -18 rises to the right: the top right corner sits above the top left by width * tan 18.
        var q = quads["clock"];
        double width = q.TopRight.X - q.TopLeft.X;
        Assert.Equal(-width * Math.Tan(18 * Math.PI / 180), q.TopRight.Y - q.TopLeft.Y, 1);

        var islands = view.TakeIslands();
        var slanted = islands.Single(i => i.Widgets.Contains("clock"));
        var skew = Assert.IsType<SkewTransform>(slanted.Content.LayoutTransform);
        Assert.Equal(-18, skew.AngleY);
        Assert.Equal(0, skew.AngleX);
        Assert.Contains("claude", slanted.Widgets);   // the whole stack, one island
        Assert.True(islands.Single(i => i.Widgets.Contains("meters")).Content.LayoutTransform.Value.IsIdentity);
        // The island is measured with its slant, so its window fits the sheared face.
        slanted.Content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(slanted.Content.DesiredSize.Height > q.BottomLeft.Y - q.TopLeft.Y);
        view.Dispose();
    });

    [Fact]
    public void WithThePlateOnThePlateSlantsWithItsStack() => Sta.Run(() =>
    {
        var view = View("clock:tl:1:12", new DeskFrame { Usage = Usage(), Now = Now }, plate: true);
        var island = view.TakeIslands().Single();
        Assert.IsType<SkewTransform>(island.Content.LayoutTransform);
        Assert.Contains(All<Border>(island.Content), b => b.Effect is System.Windows.Media.Effects.BlurEffect);   // the plate is inside the slanted host
        view.Dispose();
    });

    [Fact]
    public void ArrangeDeskHitsAndOutlinesTheSlantNotTheBoxAroundIt() => Sta.Run(() =>
    {
        const string layout = "clock:bl:1:-18 meters:tr";
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
        var frame = new DeskFrame { Usage = Usage(), Now = Now };
        var content = new DeskArrangeContent(Screen(), settings, new DeskFonts(), frame, new DeskArrangement(layout), primary: true);
        try
        {
            var r = content.PieceRects["clock"];
            // The box's bottom right is below the rising face: nothing is there. The bottom left is the face's own corner.
            Assert.Null(content.PieceAt(new Point(r.Right - 3, r.Bottom - 3)));
            Assert.Equal("clock", content.PieceAt(new Point(r.Left + 3, r.Bottom - 3)));
            Assert.Equal("clock", content.PieceAt(new Point(r.Right - 3, r.Top + 3)));
            var meters = content.PieceRects["meters"];
            Assert.Equal("meters", content.PieceAt(new Point(meters.Right - 2, meters.Bottom - 2)));   // upright: the whole box
            // The slanted piece is outlined along its slant; the upright one keeps its rounded rectangle.
            Assert.Single(All<Polygon>(content));

            content.BeginDrag("clock", new Point(r.Left + 10, r.Bottom - 10));
            Assert.Equal(2, All<Polygon>(content).Count());   // the ghost follows the slant too
            content.CancelDrag();
        }
        finally { content.Dispose(); }
    });

    // ---- E. the weather nickname ----

    private static readonly WeatherPlace FortWorth = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);

    [Fact]
    public void TheWeatherPieceShowsTheNicknameInPlaceOfTheCity() => Sta.Run(() =>
    {
        var reading = new WeatherReading(72, 2, 80, 61, 80, 75, 60, Now.AddMinutes(-5));
        foreach (var r in new WeatherReading?[] { reading, null })
        {
            var view = View("weather:mr", new DeskFrame
            {
                Now = Now, WeatherOn = true, Place = FortWorth, Weather = r, WeatherNickname = "Funky Town",
            });
            var texts = All<TextBlock>(view).Select(t => t.Text).ToList();
            Assert.Contains("Funky Town", texts);
            Assert.DoesNotContain("Fort Worth", texts);
            view.Dispose();
        }
        var plain = View("weather:mr", new DeskFrame { Now = Now, WeatherOn = true, Place = FortWorth, Weather = reading });
        Assert.Contains("Fort Worth", All<TextBlock>(plain).Select(t => t.Text));
        plain.Dispose();
    });

    [Fact]
    public void SettingsWeatherKeepsTheNicknameLocallyAndAcrossAPlaceChange()
    {
        var store = new SettingsStore(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));
        store.SaveDeskWeather(new DeskWeatherSettings(FortWorth, WeatherUnit.Fahrenheit));
        var page = new WeatherPageViewModel(store);
        int changed = 0;
        page.Changed = () => changed++;
        Assert.Equal("", page.Nickname);

        page.Nickname = "  Funky Town ";
        Assert.Equal("Funky Town", store.LoadDeskWeather().Nickname);
        Assert.Equal(FortWorth, store.LoadDeskWeather().Place);
        Assert.Equal(1, changed);

        page.Unit = WeatherPageViewModel.Units.First(u => u.Unit == WeatherUnit.Celsius);
        Assert.Equal("Funky Town", store.LoadDeskWeather().Nickname);

        // A design changes it while the page is open; a later unit change keeps the design's nickname.
        store.SaveDeskWeather(store.LoadDeskWeather() with { Nickname = "Cowtown" });
        page.Unit = WeatherPageViewModel.Units.First(u => u.Unit == WeatherUnit.Fahrenheit);
        Assert.Equal("Cowtown", store.LoadDeskWeather().Nickname);

        page.Nickname = "";
        Assert.Equal("", store.LoadDeskWeather().Nickname);
        Assert.Contains("never sent", WeatherPageViewModel.NicknameHint);
        Assert.Equal(24, WeatherPageViewModel.NicknameMax);
    }

    // ---- G. livelier bars ----

    private static (DeskView View, AudioLevelSource Audio) Bars(bool gradient, bool calm, string[]? ink = null)
    {
        var audio = new AudioLevelSource { CalmNow = () => calm };
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "nowPlaying:bl", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        var track = new NowPlayingInfo { Title = "Midnight City", Artist = "M83", Duration = 243, Elapsed = 101, ElapsedAt = Now, Playing = true, AppId = "Spotify.exe" };
        var inks = ink is null ? DeskInkMap.Light
            : new DeskInkMap(DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, string.Join(",", ink), true), null, null));
        view.Update(new DeskFrame { Now = Now, Track = track, Bars = true, BarsGradient = gradient, Audio = audio }, inks);
        return (view, audio);
    }

    private static List<Border> Caps(DeskView view) => All<Canvas>(view).Single().Children.OfType<Border>().ToList();

    [Fact]
    public void TheBarsCarryPeakCapsThatRideAboveThem() => Sta.Run(() =>
    {
        var (view, audio) = Bars(gradient: false, calm: false);
        foreach (var l in new[] { 0.9f, 0.8f, 0.7f, 0.2f, 0.1f, 0.05f }) audio.Pump(l);
        var canvas = All<Canvas>(view).Single();
        var bars = canvas.Children.OfType<Rectangle>().ToList();
        var caps = Caps(view);
        Assert.Equal(bars.Count, caps.Count);
        Assert.Contains(caps, c => c.Visibility == Visibility.Visible);
        for (int i = 0; i < bars.Count; i++)
            if (caps[i].Visibility == Visibility.Visible)
                Assert.True(Canvas.GetBottom(caps[i]) >= bars[i].Height, "a cap rides on or above its bar");
        Assert.All(caps, c => Assert.True(Canvas.GetBottom(c) + c.Height <= canvas.Height + 0.01));
        view.Dispose();
        audio.Dispose();
    });

    [Fact]
    public void WithAnimationsOffTheBarsAreCalmAndHaveNoCaps() => Sta.Run(() =>
    {
        var (view, audio) = Bars(gradient: false, calm: true);
        foreach (var l in new[] { 0.9f, 0.8f, 0.7f }) audio.Pump(l);
        Assert.All(Caps(view), c => Assert.Equal(Visibility.Collapsed, c.Visibility));
        Assert.True(audio.Calm);
        view.Dispose();
        audio.Dispose();
    });

    [Fact]
    public void GradientBarsSpreadTheInkAcrossTheRowAndFadeEachBar() => Sta.Run(() =>
    {
        var (view, audio) = Bars(gradient: true, calm: false, ink: ["ff0000", "0000ff"]);
        var bars = All<Canvas>(view).Single().Children.OfType<Rectangle>().ToList();
        var fills = bars.Select(b => Assert.IsType<LinearGradientBrush>(b.Fill)).ToList();
        Assert.All(fills, f => Assert.True(f.GradientStops[1].Color.A < f.GradientStops[0].Color.A));   // fades to its base
        Assert.Equal(Color.FromRgb(0xff, 0, 0), fills[0].GradientStops[0].Color);
        Assert.Equal(Color.FromRgb(0, 0, 0xff), fills[^1].GradientStops[0].Color);
        view.Dispose();
        audio.Dispose();

        var (plain, source) = Bars(gradient: false, calm: false, ink: ["ff0000", "0000ff"]);
        Assert.DoesNotContain(All<Canvas>(plain).Single().Children.OfType<Rectangle>(), b => b.Fill is LinearGradientBrush { StartPoint.X: 0.5 });
        plain.Dispose();
        source.Dispose();
    });

    [Fact]
    public void ThePrivacyPromiseStandsInTheCode()
    {
        // The bars read a level; nothing here may start to capture audio. The promise is written where the source is.
        var src = File.ReadAllText(Path.Combine(WindowsDotnet(), "src", "Sanduhr.App", "Desk", "DeskSources.cs"));
        Assert.Contains("A level only: no audio is captured.", src);
        Assert.DoesNotContain("IAudioCaptureClient", src);
        Assert.DoesNotContain("Loopback", src, StringComparison.OrdinalIgnoreCase);
        var core = File.ReadAllText(Path.Combine(WindowsDotnet(), "src", "Sanduhr.Core", "AudioBars.cs"));
        Assert.Contains("no audio is captured", core);
    }

    private static string WindowsDotnet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
