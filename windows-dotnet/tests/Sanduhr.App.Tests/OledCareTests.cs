using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// OLED care (spec 3.7.0), the App side: the switch is named "OLED care" for a screen reader, off by default and saved at once;
/// the Desk's drift pauses while Arrange mode runs and while a design is under review and comes back after; the idle dim reaches
/// the host and the widget; and the white cap reaches the Desk's ink. Core's rules are in OledCareTests there.
/// </summary>
public sealed class OledCareTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-oled-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public OledCareTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---- The switch ----

    [Fact]
    public void TheSwitchIsOffByDefaultAndSavesAtOnce()
    {
        var page = new DeskSettingsViewModel(_store, ["Segoe UI"]);
        int changed = 0;
        page.Changed = () => changed++;
        Assert.False(page.OledCare);
        page.OledCare = true;
        Assert.True(_store.LoadOledCare());
        Assert.Contains("\"desk_oled_care\":true", File.ReadAllText(_paths.SettingsFile).Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        Assert.Equal(1, changed);
        page.OledCare = false;
        Assert.False(_store.LoadOledCare());
        Assert.DoesNotContain("desk_oled_care", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void TheSwitchIsACheckBoxNamedOledCareThatAnyToggleReachesTheStore() => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var page = new DeskSettingsViewModel(_store, ["Segoe UI"]);
        var panel = new OledCarePanel { DataContext = page };
        var window = new Window { Content = panel, Width = 640, Height = 200, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -20000 };
        window.Show();
        try
        {
            panel.UpdateLayout();
            var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(panel)).ToList();
            var box = Assert.Single(peers, p => p.GetAutomationControlType() == AutomationControlType.CheckBox);
            Assert.Equal("OLED care", box.GetName());
            Assert.Equal(OledCare.Caption, box.GetHelpText());

            var toggle = (IToggleProvider)box.GetPattern(PatternInterface.Toggle)!;
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            toggle.Toggle();
            Assert.True(_store.LoadOledCare());
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            toggle.Toggle();
            Assert.False(_store.LoadOledCare());
        }
        finally { window.Close(); }
    });

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    // ---- The host moves the window ----

    private static (DesktopLayerHost Host, DeskScreen Screen) ShownHost(Point anchor, DeskInsets taskbar)
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1080);
        var screen = new DeskScreen("TEST", bounds, work, 1.0, true, taskbar);
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        var content = new System.Windows.Controls.Border { Width = 200, Height = 50, Background = System.Windows.Media.Brushes.White };
        host.Show([new DeskSurface(screen, [new DeskIsland(content, anchor, HorizontalAlignment.Left, VerticalAlignment.Top)])]);
        return (host, screen);
    }

    [Fact]
    public void TheHostMovesTheWholeWindowByTheOrbitAndBackToTheAnchor() => Sta.Run(() =>
    {
        var (host, _) = ShownHost(new Point(300, 300), DeskInsets.None);
        try
        {
            var hwnd = host.Handles[0];
            var home = Interop.DeskNative.Rect(hwnd)!.Value;
            Assert.Equal(300 - 16, home.X); // the anchor less the 16 DIP shadow room

            host.SetDrift(2);
            var (ox, oy) = OledDrift.Offset(2);
            var moved = Interop.DeskNative.Rect(hwnd)!.Value;
            Assert.Equal((home.X + ox, home.Y + oy, home.Width, home.Height), (moved.X, moved.Y, moved.Width, moved.Height));

            host.SetDrift(null);
            Assert.Equal(home, Interop.DeskNative.Rect(hwnd)!.Value);
        }
        finally { host.Dispose(); }
    });

    [Fact]
    public void ADriftedWindowNeverLeavesTheWorkAreaOrCrossesTheTaskbarClearance() => Sta.Run(() =>
    {
        // The piece sits in the top left corner and 40 px above the bottom, with a 40 px auto-hidden taskbar at the bottom.
        var (host, screen) = ShownHost(new Point(0, 1080 - 40 - 50), new DeskInsets(0, 0, 0, 40));
        try
        {
            var hwnd = host.Handles[0];
            for (int k = 0; k < OledDrift.Steps; k++)
            {
                host.SetDrift(k);
                var r = Interop.DeskNative.Rect(hwnd)!.Value;
                int left = r.X + 16, top = r.Y + 16, right = r.X + r.Width - 16, bottom = r.Y + r.Height - 16; // the content, less the shadow room
                Assert.True(left >= 0 && top >= 0, $"step {k}: content at {left},{top}");
                Assert.True(bottom <= 1080 - 40, $"step {k}: content bottom {bottom}");
                Assert.True(right > left && bottom > top);
            }
        }
        finally { host.Dispose(); }
    });

    [Fact]
    public void AWindowMadeWhileDriftedOrDimmedStartsThatWay() => Sta.Run(() =>
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var screen = new DeskScreen("TEST", bounds, bounds, 1.0, true, DeskInsets.None);
        var host = new DesktopLayerHost { OwnedByShell = false, WatchShowDesktop = false };
        try
        {
            host.SetDrift(3);
            host.SetDim(0.4, TimeSpan.Zero);
            var content = new System.Windows.Controls.Border { Width = 200, Height = 50 };
            host.Show([new DeskSurface(screen, [new DeskIsland(content, new Point(300, 300), HorizontalAlignment.Left, VerticalAlignment.Top)])]);
            var (ox, oy) = OledDrift.Offset(3);
            var r = Interop.DeskNative.Rect(host.Handles[0])!.Value;
            Assert.Equal((300 - 16 + ox, 300 - 16 + oy), (r.X, r.Y));
            Assert.Equal(0.4, content.Parent is System.Windows.Controls.Border b ? b.Opacity : -1);
        }
        finally { host.Dispose(); }
    });

    // ---- The Desk ----

    private sealed class FakeHost : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public IReadOnlyList<DeskSurface> Shown { get; private set; } = [];
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];
        public List<int?> Drifts { get; } = [];
        public List<(double Opacity, TimeSpan Ease)> Dims { get; } = [];
        public int? Drift => Drifts.Count == 0 ? null : Drifts[^1];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            return [new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0))];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shown = surfaces; IsShowing = true; }
        public void Hide() { Shown = []; IsShowing = false; }
        public void SetDrift(int? step) => Drifts.Add(step);
        public void SetDim(double opacity, TimeSpan ease) => Dims.Add((opacity, ease));
        public void Dispose() { }
    }

    private (DeskController Desk, FakeHost Host, OledCareDriver Oled, DeskMessageSource Messages, NowPlayingHub Hub) Start(DateTimeOffset? lastInput = null, Func<DateTimeOffset>? clock = null)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        _store.SaveOledCare(true);
        var host = new FakeHost();
        var oled = new OledCareDriver(() => lastInput, clock ?? (() => DateTimeOffset.UtcNow));
        var messages = new DeskMessageSource(_store, _paths);
        var hub = new NowPlayingHub();
        var desk = new DeskController(_store, () => host, () => DeskUsage.Empty, hub, messages, paths: _paths, oled: oled);
        desk.Reload();
        return (desk, host, oled, messages, hub);
    }

    [Fact]
    public void WithTheCareOnTheDriftFollowsTheOrbitAndOffPutsEveryPieceBackOnItsAnchor() => Sta.Run(() =>
    {
        var (desk, host, oled, messages, hub) = Start();
        try
        {
            Assert.True(oled.On);
            Assert.Equal(0, host.Drift);
            oled.Advance();
            Assert.Equal(1, host.Drift);
            oled.Advance();
            Assert.Equal(2, host.Drift);

            _store.SaveOledCare(false);
            desk.Reload();
            Assert.False(oled.On);
            Assert.Null(host.Drift);
            oled.Advance(); // off: the clocks are stopped and a stray tick does nothing
            Assert.Null(host.Drift);
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    [Fact]
    public void DriftPausesWhileArrangingAndComesBackWhenArrangeEnds() => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var (desk, host, oled, messages, hub) = Start();
        try
        {
            oled.Advance();
            Assert.Equal(1, host.Drift);

            desk.BeginArrange();
            Assert.True(desk.Arranging);
            Assert.Null(host.Drift); // pieces sit at their true anchor while you arrange
            oled.Advance();          // the orbit moves on underneath, but nothing drifts
            Assert.Null(host.Drift);

            desk.EndArrange(DeskArrangeEnd.Cancel);
            Assert.False(desk.Arranging);
            Assert.Equal(2, host.Drift); // and picks up where the orbit is now
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    [Fact]
    public void DriftPausesWhileADesignIsUnderReviewAndComesBackAfter() => Sta.Run(() =>
    {
        var (desk, host, oled, messages, hub) = Start();
        try
        {
            oled.Advance();
            Assert.Equal(1, host.Drift);

            desk.ShowPreview(new DeskDesignSettings(DeskSettings.Default with { Enabled = true, Layout = "clock:tr" }, DeskInkSettings.Default, LauncherModel.Empty));
            Assert.True(desk.Previewing);
            Assert.Null(host.Drift);
            oled.Advance();
            Assert.Null(host.Drift);

            desk.EndPreview();
            Assert.Equal(2, host.Drift);
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    [Fact]
    public void TheIdleDimReachesTheDeskAndTheWidgetAndAnyInputRestoresBoth() => Sta.Run(() =>
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var lastInput = now;
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        _store.SaveOledCare(true);
        var host = new FakeHost();
        var oled = new OledCareDriver(() => lastInput, () => now);
        var messages = new DeskMessageSource(_store, _paths);
        var hub = new NowPlayingHub();
        var desk = new DeskController(_store, () => host, () => DeskUsage.Empty, hub, messages, paths: _paths, oled: oled);
        var widget = new List<(bool Dimmed, TimeSpan Ease)>();
        desk.WidgetDimRequested += (d, e) => widget.Add((d, e));
        try
        {
            desk.Reload();
            oled.Poll();
            Assert.Empty(host.Dims);

            now += TimeSpan.FromMinutes(5);
            oled.Poll();
            Assert.Equal(OledIdle.DeskDimmedOpacity, Assert.Single(host.Dims).Opacity);
            Assert.True(Assert.Single(widget).Dimmed);
            oled.Poll(); // still away: no second dim
            Assert.Single(host.Dims);

            lastInput = now; // a key press
            oled.Poll();
            Assert.Equal(2, host.Dims.Count);
            Assert.Equal(1.0, host.Dims[1].Opacity);
            Assert.False(widget[1].Dimmed);
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    [Fact]
    public void SwitchingTheCareOffWhileDimmedWakesBoth() => Sta.Run(() =>
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var (desk, host, oled, messages, hub) = Start(lastInput: now - TimeSpan.FromHours(1), clock: () => now);
        var widget = new List<bool>();
        desk.WidgetDimRequested += (d, _) => widget.Add(d);
        try
        {
            oled.Poll();
            Assert.True(oled.Dimmed);
            _store.SaveOledCare(false);
            desk.Reload();
            Assert.False(oled.Dimmed);
            Assert.Equal(1.0, host.Dims[^1].Opacity);
            Assert.Equal([true, false], widget);
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    [Fact]
    public void TheInkIsCappedBelowPureWhiteOnlyWithTheCareOn() => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var (desk, host, oled, messages, hub) = Start();
        try
        {
            _store.SaveDeskInk(DeskInkSettings.Default with { Mode = DeskInkMode.Light });
            desk.Reload();
            Assert.Contains("e6e6e6", InkHexesOf(host), StringComparer.Ordinal);
            Assert.DoesNotContain("ffffff", InkHexesOf(host), StringComparer.Ordinal);

            _store.SaveOledCare(false);
            desk.Reload();
            Assert.Contains("ffffff", InkHexesOf(host), StringComparer.Ordinal);
        }
        finally { desk.Dispose(); messages.Dispose(); hub.Dispose(); }
    });

    /// <summary>The ink colors the Desk drew with, read off the first text it drew.</summary>
    private static List<string> InkHexesOf(FakeHost host)
    {
        var hexes = new List<string>();
        foreach (var island in host.Shown.SelectMany(s => s.Islands))
            Collect(island.Content, hexes);
        return hexes;
    }

    private static void Collect(DependencyObject d, List<string> hexes)
    {
        if (d is System.Windows.Controls.TextBlock { Foreground: System.Windows.Media.SolidColorBrush b } && b.Color.A > 0)
            hexes.Add($"{b.Color.R:x2}{b.Color.G:x2}{b.Color.B:x2}");
        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            Collect(child, hexes);
    }
}
