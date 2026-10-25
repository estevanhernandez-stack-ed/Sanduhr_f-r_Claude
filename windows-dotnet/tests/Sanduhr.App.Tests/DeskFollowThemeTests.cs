using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Follow the widget theme": with <c>desk_follows_theme</c> on the Desk draws the active theme's look and redraws on every
/// theme change; off, it draws its own settings untouched. The readability guard turns a plateless look's plate on where its ink would not
/// read. Over a scratch AppData, with the wallpaper sample replaced.
/// </summary>
public sealed class DeskFollowThemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-follow-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskFollowThemeTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class FakeHost : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public IReadOnlyList<DeskSurface> Shown { get; private set; } = [];
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            return [new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0))];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shown = surfaces; IsShowing = true; }
        public void Hide() { Shown = []; IsShowing = false; }
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }

    private (DeskController Desk, FakeHost Host) Start(bool follows, double wallpaper = 0.05)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" });
        _store.SaveDeskFollowsTheme(follows);
        var host = new FakeHost();
        var desk = new DeskController(_store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(_store, _paths), paths: _paths)
        {
            WallpaperLuminance = (_, _) => wallpaper,
        };
        desk.ThemeChanged(ThemeCatalog.BuiltIns["obsidian"]);
        desk.Reload();
        return (desk, host);
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    /// <summary>The clock's digits (the bold text of the clock piece).</summary>
    private static TextBlock Clock(FakeHost host) =>
        host.Shown.SelectMany(s => s.Islands).SelectMany(i => All<TextBlock>(i.Content)).First(t => t.FontWeight == FontWeights.Bold);

    private static Color[] Stops(Brush b) => b is LinearGradientBrush g ? [.. g.GradientStops.Select(s => s.Color)] : [((SolidColorBrush)b).Color];

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex)!;

    [Fact]
    public void Following_draws_the_themes_ink_and_off_draws_the_desks_own() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            Assert.Equal([C("e8e4dc"), C("6c63ff")], Stops(Clock(host).Foreground));   // Obsidian: text to accent
            _store.SaveDeskFollowsTheme(false);
            desk.Reload();
            Assert.Equal(C("ffffff"), Stops(Clock(host).Foreground)[0]);              // its own Automatic ink on a dark wallpaper: white
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void Following_restyles_on_every_theme_change() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"]);
            Assert.Equal([C("e2e8f0"), C("38bdf8")], Stops(Clock(host).Foreground));
            // A theme's font comes with it (Consolas: on every Windows, so the Desk's font check never swaps it for EsteFont Pro).
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"] with { Font = "Consolas" });
            Assert.Equal("Consolas", Clock(host).FontFamily.Source);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_studio_preview_reaches_a_following_desk() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            // The Studio's preview raises ThemeChanged with a draft definition (a "studio" palette): the Desk draws it.
            desk.ThemeChanged(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Ink = ["#ffd08a"] } });
            Assert.Equal(C("ffd08a"), Stops(Clock(host).Foreground)[0]);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_desk_that_does_not_follow_ignores_theme_changes() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: false);
        try
        {
            var before = Clock(host);
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"]);   // Automatic ink: no redraw
            Assert.Same(before, Clock(host));
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void The_guard_turns_a_plateless_looks_plate_on_where_the_ink_would_not_read() => Sta.Run(() =>
    {
        var plateless = ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Plate = false } };
        var (desk, host) = Start(follows: true, wallpaper: 0.9);
        int raised = 0;
        desk.PlateForcedChanged += () => raised++;
        try
        {
            desk.ThemeChanged(plateless);
            Assert.True(desk.PlateForced);
            Assert.Equal(1, raised);
            desk.WallpaperLuminance = (_, _) => 0.05;
            desk.Reload();
            Assert.False(desk.PlateForced);
            Assert.Equal(2, raised);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_controller_built_frame_carries_the_alert_thresholds_and_a_change_reaches_it_on_reload() => Sta.Run(() =>
    {
        _store.SaveAlertConfig(new AlertConfig(true, 55, 70, true, false));
        var (desk, _) = Start(follows: false);
        try
        {
            Assert.Equal(55, desk.LastFrame!.WarnAt);
            Assert.Equal(70, desk.LastFrame!.UrgentAt);
            _store.SaveAlertConfig(new AlertConfig(true, 60, 85, true, false));
            desk.Reload();
            Assert.Equal(60, desk.LastFrame!.WarnAt);
            Assert.Equal(85, desk.LastFrame!.UrgentAt);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void The_resolver_leaves_the_own_look_alone_and_carries_the_theme_look_when_following()
    {
        var own = DeskSettings.Default with { Font = "", Plate = false };
        var frame = new DeskFrame();
        Assert.Null(DeskLookResolver.For(false, ThemeCatalog.BuiltIns["obsidian"]));
        Assert.Null(DeskLookResolver.For(true, null));
        Assert.Same(own, DeskLookResolver.Settings(own, null));
        Assert.Same(frame, DeskLookResolver.Frame(frame, null));

        var matrix = DeskLookResolver.For(true, ThemeCatalog.BuiltIns["matrix"])!;
        Assert.Equal(["00ff41", "00ff41"], matrix.Ink.Stops);
        Assert.Equal("010501", matrix.Ink.ShadowHex);
        Assert.True(matrix.Ink.ShadowOpacity > 0);
        var s = DeskLookResolver.Settings(own, matrix);
        Assert.Equal("Cascadia Code", s.Font);
        Assert.True(s.Plate);
        Assert.Equal("", DeskLookResolver.Settings(own, DeskLookResolver.For(true, ThemeCatalog.BuiltIns["aurora"])).Font);   // no theme font: its own

        var msg = new DeskMessageFrame(new MessageToday(usual: "keep building."), DeskMessageSettings.Default with { Glow = false });
        var themed = DeskLookResolver.Frame(frame with { Message = msg }, DeskLookResolver.For(true, ThemeCatalog.BuiltIns["obsidian"]));
        Assert.Equal("0a0a0a", themed.PlateHex);
        Assert.Equal(["6c63ff", "e8e4dc"], themed.NowPlayingStops);
        Assert.Equal(["6c63ff", "e8e4dc"], themed.MeterStops);
        Assert.Equal("2a2a2a", themed.MeterTrackHex);
        Assert.Equal("ff6b6b", themed.PaceHex);
        Assert.Equal("6c63ff,e8e4dc", themed.Message!.Settings.Color);
        Assert.True(themed.Message.Settings.Glow);
        Assert.Equal("6c63ff", themed.Message.GlowHex);
    }

    [Fact]
    public void A_look_font_this_pc_has_is_used_and_a_missing_one_leaves_the_desks_own()
    {
        var own = DeskSettings.Default with { Font = "", Plate = false };
        var present = DeskLookResolver.For(true, ThemeCatalog.BuiltIns["obsidian"] with { Font = "Consolas" });
        var missing = DeskLookResolver.For(true, ThemeCatalog.BuiltIns["obsidian"] with { Font = "Zzyzx Not A Real Family" });
        bool Has(string f) => f == "Consolas";
        Assert.Equal("Consolas", DeskLookResolver.Settings(own, present, Has).Font);
        Assert.Equal("", DeskLookResolver.Settings(own, missing, Has).Font);
    }

    [Fact]
    public void Arrange_mode_draws_what_the_desk_shows_while_following() => Sta.Run(() =>
    {
        var (desk, _) = Start(follows: true);
        try
        {
            var arrange = desk.ArrangeLook(new DeskFrame());
            Assert.Equal(ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).Plate, arrange.Settings.Plate);
            Assert.Equal(ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).PlateColor, arrange.Frame.PlateHex);
            desk.ThemeChanged(ThemeCatalog.BuiltIns["obsidian"] with { Font = "Consolas" });
            Assert.Equal("Consolas", desk.ArrangeLook(new DeskFrame()).Settings.Font);
            _store.SaveDeskFollowsTheme(false);
            desk.Reload();
            Assert.Equal("", desk.ArrangeLook(new DeskFrame()).Settings.Font);   // not following: the Desk's own
            Assert.Null(desk.ArrangeLook(new DeskFrame()).Frame.PlateHex);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_design_under_review_follows_or_not_as_the_design_says() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: false);
        try
        {
            var settings = DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" };
            Assert.Equal(C("ffffff"), Stops(Clock(host).Foreground)[0]);
            desk.ShowPreview(new DeskDesignSettings(settings, DeskInkSettings.Default, LauncherModel.Empty, null, FollowsTheme: true));
            Assert.Equal([C("e8e4dc"), C("6c63ff")], Stops(Clock(host).Foreground));   // previewed as following
            desk.EndPreview();
            Assert.Equal(C("ffffff"), Stops(Clock(host).Foreground)[0]);               // back to the Desk's own
        }
        finally { desk.Dispose(); }
    });
}
