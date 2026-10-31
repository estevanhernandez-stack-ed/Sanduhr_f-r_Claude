using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Desktop off (setup spec, section 1): the Desk's own pieces are filtered out of what is drawn and what is wanted, a design under
/// review shows in full, Arrange does not begin, and the saved layout is never written filtered.</summary>
[Collection("SettingsWindow")]
public sealed class DeskDesktopOffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-deskoff-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskDesktopOffTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class OneScreen : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var b = new PxRect(0, 0, 1920, 1080);
            return [new DeskScreen("MAIN", b, b, 1.0, true, TaskbarClearance.Insets(b, b, [], 1.0), @"\?\DISPLAY#MAIN#0")];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) => IsShowing = true;
        public void Hide() => IsShowing = false;
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }

    private DeskController Desk(string layout, bool desktop)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = layout });
        _store.SaveSetup(new SetupSettings(desktop, new Dictionary<string, bool>()));
        var desk = new DeskController(_store, () => new OneScreen(), () => DeskUsage.Empty, new NowPlayingHub(),
            new DeskMessageSource(_store, _paths), paths: _paths);
        desk.Reload();
        return desk;
    }

    [Fact]
    public void Desktop_off_unplaces_the_desks_own_pieces_and_Arrange_does_not_begin() => Sta.Run(() =>
    {
        var desk = Desk("clock:bl watchers:mr", desktop: false);
        try
        {
            Assert.False(desk.DesktopShown);
            Assert.False(desk.WatchersPlaced);
            desk.BeginArrange();
            Assert.False(desk.Arranging);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void Desktop_on_keeps_pieces_and_Arrange_begins() => Sta.Run(() =>
    {
        var desk = Desk("clock:bl watchers:mr", desktop: true);
        try
        {
            Assert.True(desk.DesktopShown);
            Assert.True(desk.WatchersPlaced);
            desk.BeginArrange();
            Assert.True(desk.Arranging);
            desk.EndArrange(DeskArrangeEnd.Cancel);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_design_under_review_shows_in_full_and_the_saved_layout_stays_whole() => Sta.Run(() =>
    {
        const string saved = "clock:bl watchers:mr";
        var desk = Desk(saved, desktop: false);
        try
        {
            Assert.False(desk.WatchersPlaced);
            desk.ShowPreview(new DeskDesignSettings(DeskSettings.Default with { Enabled = true, Layout = "clock:tl watchers:mr" },
                DeskInkSettings.Default, LauncherModel.Empty));
            Assert.True(desk.WatchersPlaced);
            desk.EndPreview();
            Assert.False(desk.WatchersPlaced);
            Assert.Equal(saved, _store.LoadDesk().Layout);
        }
        finally { desk.Dispose(); }
    });

    private static DeskDesignSettings Design(string layout) =>
        new(DeskSettings.Default with { Enabled = true, Layout = layout }, DeskInkSettings.Default, LauncherModel.Empty);

    [Fact]
    public void Desktop_off_still_places_a_drawer() => Sta.Run(() =>
    {
        var desk = Desk("clock:bl drawer.images:tr", desktop: false);
        try
        {
            Assert.False(desk.IsPlaced("clock"));
            Assert.True(desk.IsPlaced("drawer.images"));
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_drawers_preview_shows_only_drawers_a_design_preview_shows_all_and_EndPreview_restores_the_filter() => Sta.Run(() =>
    {
        var desk = Desk("clock:bl drawer.images:tr", desktop: false);
        try
        {
            desk.ShowPreview(Design("clock:bl drawer.images:tr"), full: false);
            Assert.False(desk.IsPlaced("clock"));
            Assert.True(desk.IsPlaced("drawer.images"));
            desk.EndPreview();

            desk.ShowPreview(Design("clock:bl drawer.images:tr"), full: true);
            Assert.True(desk.IsPlaced("clock"));
            Assert.True(desk.IsPlaced("drawer.images"));
            desk.EndPreview();
            Assert.False(desk.IsPlaced("clock"));
            Assert.True(desk.IsPlaced("drawer.images"));
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void Desktop_going_off_under_a_running_Arrange_ends_it_without_saving() => Sta.Run(() =>
    {
        var desk = Desk("clock:bl watchers:mr", desktop: true);
        try
        {
            desk.BeginArrange();
            Assert.True(desk.Arranging);
            _store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool>()));
            desk.Reload();
            Assert.False(desk.Arranging);
            Assert.Equal("clock:bl watchers:mr", _store.LoadDesk().Layout);
        }
        finally { desk.Dispose(); }
    });
}
