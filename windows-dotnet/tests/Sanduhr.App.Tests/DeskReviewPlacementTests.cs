using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// #319, the app side: the Desk names the screen the review bar opens on from the design under review and the screens it has, and
/// the bar centers itself on that screen's work area in physical pixels (a 3440 x 1440 primary beside a 1920 x 1080 secondary).
/// </summary>
public sealed class DeskReviewPlacementTests : IDisposable
{
    private const string MainId = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SideId = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-review-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskReviewPlacementTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class TwoScreens : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var main = new PxRect(0, 0, 3440, 1440);
            var mainWork = new PxRect(0, 0, 3440, 1392);
            var side = new PxRect(3440, 0, 1920, 1080);
            return
            [
                new DeskScreen("MAIN", main, mainWork, 1.0, true, TaskbarClearance.Insets(main, mainWork, [], 1.0), MainId),
                new DeskScreen("SIDE", side, side, 1.25, false, TaskbarClearance.Insets(side, side, [], 1.25), SideId),
            ];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) => IsShowing = true;
        public void Hide() => IsShowing = false;
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }

    private static string On(string id) => "@" + DeskMonitors.Escape(id);

    private DeskController Desk(string savedLayout)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = savedLayout });
        var desk = new DeskController(_store, () => new TwoScreens(), () => DeskUsage.Empty, new NowPlayingHub(),
            new DeskMessageSource(_store, _paths), paths: _paths);
        desk.Reload();
        return desk;
    }

    private static DeskDesignSettings Design(string layout) =>
        new(DeskSettings.Default with { Enabled = true, Layout = layout }, DeskInkSettings.Default, LauncherModel.Empty);

    [Fact]
    public void A_design_drawn_on_the_secondary_reviews_on_the_secondary() => Sta.Run(() =>
    {
        var desk = Desk($"clock:tl{On(SideId)} claude:bl{On(SideId)}");
        try
        {
            var design = Design($"clock:tr{On(SideId)} claude:bl{On(SideId)}");
            desk.ShowPreview(design);
            Assert.Equal("SIDE", desk.ReviewScreen(design)?.Device);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_design_on_every_monitor_reviews_on_the_primary() => Sta.Run(() =>
    {
        var desk = Desk("clock:tl claude:bl");
        try
        {
            var design = Design("clock:tr claude:bl");
            desk.ShowPreview(design);
            Assert.Equal("MAIN", desk.ReviewScreen(design)?.Device);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void The_bar_centers_on_its_screens_work_area_in_physical_pixels()
    {
        var side = new PxRect(3440, 0, 1920, 1080);
        Assert.Equal((3440 + (1920 - 800) / 2, 10), DeskReviewWindow.BarOrigin(side, 1.25, 800));
        // Wider than the screen: flush with its left edge, never off it.
        Assert.Equal((3440, 8), DeskReviewWindow.BarOrigin(side, 1.0, 2400));
        // A work area that starts below a top taskbar.
        Assert.Equal((0 + (3440 - 1000) / 2, 48 + 8), DeskReviewWindow.BarOrigin(new PxRect(0, 48, 3440, 1392), 1.0, 1000));
    }
}
