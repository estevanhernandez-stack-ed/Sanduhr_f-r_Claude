using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Desk pieces per monitor (spec, Desk pieces per monitor), the app side: Settings' Monitor dropdown (its choices, what it writes, its
/// names for a screen reader), Arrange mode's Show on control and a piece dragged onto another monitor's surface, and the design
/// service naming monitors and refusing ones that are not there.
/// </summary>
public sealed class DeskMonitorTests : IDisposable
{
    private const string MainId = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SideId = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-monitors-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    private static readonly IReadOnlyList<DeskMonitorInfo> Connected = DeskMonitorNames.Named(
    [
        new DeskMonitorInfo(MainId, "", 3440, 1440, true, 0, 0),
        new DeskMonitorInfo(SideId, "", 1920, 1080, false, 3440, 0),
    ]);

    public DeskMonitorTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root);
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Page(string layout, Func<IReadOnlyList<DeskMonitorInfo>>? monitors = null)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = layout });
        return new DeskSettingsViewModel(_store, ["Segoe UI"], monitors ?? (() => Connected));
    }

    private static DeskPieceRow Row(DeskSettingsViewModel page, string widget) => page.Pieces.Single(p => p.Widget == widget);

    // ---- Settings: the Monitor dropdown ----

    [Fact]
    public void EachPlacedPieceOffersAllMainAndEachConnectedMonitor() =>
        Sta.Run(() =>
        {
            var page = Page("clock:tl claude:bl@main");
            var clock = Row(page, "clock");
            Assert.Equal(["All monitors", "Main monitor", "3440\u00D71440, main", "1920\u00D71080, right of main"], clock.MonitorChoices.Select(c => c.Name));
            Assert.Equal("All monitors", clock.Monitor.Name);
            Assert.Equal("Main monitor", Row(page, "claude").Monitor.Name);
            Assert.True(clock.IsPlaced);
            Assert.False(Row(page, "weather").IsPlaced);   // a hidden piece has nothing to place on a monitor
        });

    [Fact]
    public void ChoosingAMonitorWritesItInTheLayoutAndNothingElseMoves() =>
        Sta.Run(() =>
        {
            var page = Page("clock:tl:1.4 claude:bl");
            var clock = Row(page, "clock");
            clock.Monitor = clock.MonitorChoices.Single(c => c.Value == SideId);
            string stored = _store.LoadDesk().Layout;
            Assert.Equal("clock:tl:1.4@" + DeskMonitors.Escape(SideId) + " claude:bl", stored);
            Assert.Equal(SideId, Row(page, "clock").Monitor.Value);

            clock.Monitor = clock.MonitorChoices.Single(c => c.Value == "main");
            Assert.Equal("clock:tl:1.4@main claude:bl", _store.LoadDesk().Layout);
            clock.Monitor = clock.MonitorChoices[0];
            Assert.Equal("clock:tl:1.4 claude:bl", _store.LoadDesk().Layout);
        });

    [Fact]
    public void AMonitorThatIsNotConnectedShowsAsSuchAndStaysStoredUntilYouChooseAnother() =>
        Sta.Run(() =>
        {
            const string gone = @"\\?\DISPLAY#GONE#1";
            var page = Page("clock:tl@" + DeskMonitors.Escape(gone));
            var clock = Row(page, "clock");
            Assert.Equal(DeskMonitorNames.NotConnected, clock.Monitor.Name);
            Assert.Equal(gone, clock.Monitor.Value);
            // Changing something else about the piece leaves it where it is stored.
            clock.Size = 1.2;
            Assert.Equal("clock:tl:1.2@" + DeskMonitors.Escape(gone), _store.LoadDesk().Layout);
            clock.Monitor = clock.MonitorChoices.Single(c => c.Value == "main");
            Assert.Equal("clock:tl:1.2@main", _store.LoadDesk().Layout);
        });

    [Fact]
    public void ReloadReadsTheMonitorsAgainSoAPluggedInScreenAppears() =>
        Sta.Run(() =>
        {
            var monitors = Connected.Take(1).ToList();
            var page = Page("clock:tl", () => monitors);
            Assert.Equal(3, Row(page, "clock").MonitorChoices.Count);
            monitors = [.. Connected];
            page.Reload();
            Assert.Equal(4, Row(page, "clock").MonitorChoices.Count);
            Assert.Equal(2, page.Monitors.Count);
        });

    // ---- Settings: what a screen reader sees ----

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    /// <summary>The name of the entry a combo box shows chosen.</summary>
    private static string Selected(AutomationPeer combo) =>
        ((DeskMonitorChoice)((ComboBox)((FrameworkElementAutomationPeer)combo).Owner).SelectedItem).Name;

    [Fact]
    public void TheMonitorDropdownIsAComboBoxNamedForItsPieceAndShowsItsChoice() =>
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var page = Page("clock:tl claude:bl@main");
            var panel = new DeskPiecesPanel { DataContext = page };
            var window = new Window { Content = panel, Width = 700, Height = 700, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            window.Left = -20000;
            window.Show();
            try
            {
                panel.UpdateLayout();
                var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(panel)).ToList();
                AutomationPeer Combo(string name) => peers.Single(p => p.GetName() == name && p.GetAutomationControlType() == AutomationControlType.ComboBox);

                var clock = Combo("Clock and date, monitor");
                Assert.True(clock.IsEnabled());
                Assert.NotNull(clock.GetPattern(PatternInterface.ExpandCollapse));
                Assert.Equal("All monitors", Selected(clock));
                Assert.Equal("Main monitor", Selected(Combo("Claude meters (line), monitor")));
                // A hidden piece's dropdown is there but off, as its size is.
                Assert.False(Combo("Weather, monitor").IsEnabled());
                // The piece's place and size keep the names they had.
                Assert.Contains(peers, p => p.GetName() == "Clock and date, place");
                Assert.Contains(peers, p => p.GetName() == "Clock and date, size");
            }
            finally { window.Close(); }
        });

    [Fact]
    public void TheMonitorSettingIsSearchableByMonitorScreenAndDisplay()
    {
        foreach (var query in new[] { "monitor", "screen", "display", "main monitor" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskPieces);
    }

    // ---- Arrange ----

    private static DeskScreen Screen(int x, bool primary, string id) =>
        new("DISPLAY" + (primary ? 1 : 2), new PxRect(x, 0, 1920, 1080), new PxRect(x, 0, 1920, 1032), 1.0, primary,
            TaskbarClearance.Insets(new PxRect(x, 0, 1920, 1080), new PxRect(x, 0, 1920, 1032), [], 1.0), id);

    private static readonly DeskFrame Frame = new() { Now = new DateTimeOffset(2026, 10, 21, 10, 9, 0, TimeSpan.Zero) };

    private static (DeskArrangeContent Main, DeskArrangeContent Side, DeskArrangement Working) Surfaces(string layout, bool showOn = true)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
        var working = new DeskArrangement(layout);
        var fonts = new DeskFonts();
        string[] ids = [MainId, SideId];
        return (new DeskArrangeContent(Screen(0, true, MainId), settings, fonts, Frame, working, primary: true, ids, showOn),
                new DeskArrangeContent(Screen(1920, false, SideId), settings, fonts, Frame, working, primary: false, ids, showOn),
                working);
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void EachMonitorsSurfaceDrawsOnlyItsPiecesAndThoseOnEveryMonitor() =>
        Sta.Run(() =>
        {
            var (main, side, _) = Surfaces("clock:tl claude:bl@main meters:br@" + DeskMonitors.Escape(SideId));
            Assert.Equal(["claude", "clock"], main.PieceRects.Keys.Order());
            Assert.Equal(["clock", "meters"], side.PieceRects.Keys.Order());
            main.Dispose();
            side.Dispose();
        });

    [Fact]
    public void ShowOnIsOnEveryPiecesChromeWithNamesThatSayWhatItDoes() =>
        Sta.Run(() =>
        {
            var (main, side, _) = Surfaces("clock:tl claude:bl@main");
            var clock = main.Buttons[DeskArrangeContent.ShowOnKey("clock")];
            Assert.Equal("Show on: All monitors", clock.Content);
            Assert.Equal(DeskArrangeCopy.ShowOnLabel("Clock and date", everyMonitor: true), AutomationProperties.GetName(clock));
            Assert.Contains("Press to show it on this monitor only", AutomationProperties.GetName(clock));
            var claude = main.Buttons[DeskArrangeContent.ShowOnKey("claude")];
            Assert.Equal("Show on: This monitor", claude.Content);
            Assert.Contains("Press to show it on all monitors", AutomationProperties.GetName(claude));
            Assert.Contains(DeskArrangeContent.HideKey("clock"), main.Buttons.Keys);   // Hide is still there
            Assert.Contains(DeskArrangeContent.ShowOnKey("clock"), side.Buttons.Keys);
            Assert.DoesNotContain(DeskArrangeContent.ShowOnKey("claude"), side.Buttons.Keys);   // claude is not drawn there
            main.Dispose();
            side.Dispose();
        });

    [Fact]
    public void WithOneMonitorThereIsNoShowOn() =>
        Sta.Run(() =>
        {
            var (main, side, _) = Surfaces("clock:tl", showOn: false);
            Assert.DoesNotContain(DeskArrangeContent.ShowOnKey("clock"), main.Buttons.Keys);
            main.Dispose();
            side.Dispose();
        });

    [Fact]
    public void ShowOnTurnsAPieceIntoThisMonitorsAndBack() =>
        Sta.Run(() =>
        {
            var (main, side, working) = Surfaces("clock:tl claude:bl");
            int toggled = 0;
            string? key = null;
            side.Toggled += k => { toggled++; key = k; };

            // On the second monitor: "this monitor" is that monitor's own id.
            Click(side.Buttons[DeskArrangeContent.ShowOnKey("clock")]);
            Assert.Equal(SideId, working.Placement("clock")!.Monitor);
            Assert.Equal((1, DeskArrangeContent.ShowOnKey("clock")), (toggled, key));
            main.Relayout();
            side.Relayout();
            Assert.DoesNotContain("clock", main.PieceRects.Keys);
            Assert.Contains("clock", side.PieceRects.Keys);
            Assert.Equal("Show on: This monitor", side.Buttons[DeskArrangeContent.ShowOnKey("clock")].Content);

            // Pressed again: every monitor.
            Click(side.Buttons[DeskArrangeContent.ShowOnKey("clock")]);
            Assert.Null(working.Placement("clock")!.Monitor);
            main.Relayout();
            Assert.Contains("clock", main.PieceRects.Keys);

            // On the main monitor "this monitor" is the main one.
            Click(main.Buttons[DeskArrangeContent.ShowOnKey("claude")]);
            Assert.Equal("main", working.Placement("claude")!.Monitor);
            Assert.Equal("clock:tl claude:bl@main", working.Layout);
            main.Dispose();
            side.Dispose();
        });

    [Fact]
    public void ADropFromAnotherMonitorMovesASingleMonitorPieceHereAndKeepsAnEveryMonitorPieceEverywhere() =>
        Sta.Run(() =>
        {
            var (main, side, working) = Surfaces("clock:tl claude:bl@main meters:tr@" + DeskMonitors.Escape(MainId));
            int dropped = 0;
            side.Dropped += () => dropped++;

            // Dropped at the bottom right of the second monitor: the Claude line leaves the main monitor for it.
            side.DropFrom("claude", new Point(1850, 1000));
            Assert.Equal(SideId, working.Placement("claude")!.Monitor);
            Assert.Equal(DeskAnchor.BR, working.Placement("claude")!.Anchor);
            Assert.Equal(1, dropped);
            main.Relayout();
            side.Relayout();
            Assert.DoesNotContain("claude", main.PieceRects.Keys);
            Assert.Contains("claude", side.PieceRects.Keys);

            // A piece on every monitor takes the new anchor and stays on every monitor.
            side.DropFrom("clock", new Point(1850, 40));
            Assert.Null(working.Placement("clock")!.Monitor);
            Assert.Equal(DeskAnchor.TR, working.Placement("clock")!.Anchor);

            // Back onto the main monitor's surface: it is the main monitor's again.
            main.DropFrom("claude", new Point(60, 1000));
            Assert.Equal("main", working.Placement("claude")!.Monitor);
            Assert.Equal(DeskAnchor.BL, working.Placement("claude")!.Anchor);
            main.Dispose();
            side.Dispose();
        });

    [Fact]
    public void ADragThatEndsOnAnotherMonitorAsksTheModeWhereItLandedAndOtherwiseDropsHere() =>
        Sta.Run(() =>
        {
            var (main, side, working) = Surfaces("clock:tl claude:bl@main");
            var calls = new List<string>();
            // The mode's job, stood in for: a point past the main monitor's right edge is on the second one.
            main.Locate = (from, p) =>
            {
                if (p.X < 1920) return null;
                return (side, new Point(p.X - 1920, p.Y));
            };
            main.CancelHoverElsewhere = _ => calls.Add("cancel");
            var claude = main.PieceRects["claude"];
            main.BeginDrag("claude", new Point(claude.X + 10, claude.Y + 10));
            main.MoveDrag(new Point(1900, 500));
            Assert.Null(side.Lit);   // still on the main monitor: nothing hovers there
            main.MoveDrag(new Point(1920 + 1800, 60));
            Assert.Equal(DeskAnchor.TR, side.Lit);
            Assert.Null(main.Lit);
            main.EndDrag(new Point(1920 + 1800, 60));
            Assert.Equal(SideId, working.Placement("claude")!.Monitor);
            Assert.Equal(DeskAnchor.TR, working.Placement("claude")!.Anchor);
            Assert.Null(side.Lit);   // the hover is gone

            // A drag that ends on its own monitor behaves as before.
            main.Relayout();
            side.Relayout();
            var clock = main.PieceRects["clock"];
            main.BeginDrag("clock", new Point(clock.X + 5, clock.Y + 5));
            main.EndDrag(new Point(1800, 1000));
            Assert.Equal(DeskAnchor.BR, working.Placement("clock")!.Anchor);
            Assert.Null(working.Placement("clock")!.Monitor);
            main.Dispose();
            side.Dispose();
        });

    // ---- the design service ----

    private void Propose(DeskDesignService s, string id, string json)
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(s.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + json + "}"));
        s.Handoff.Check();
    }

    private JsonObject Result() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!;

    private static string Quote(string id) => JsonValue.Create(id)!.ToJsonString();

    [Fact]
    public void AnApplyPlacesAPieceOnAMonitorAndTheReviewNamesIt() =>
        Sta.Run(() =>
        {
            _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
            _store.SaveDeskDirect(true);
            using var s = new DeskDesignService(_paths, _store) { Monitors = () => Connected, InstalledFonts = () => ["Segoe UI"] };
            Propose(s, "m1", "{\"name\":\"Split\",\"layout\":{\"clock\":{\"monitor\":" + Quote(SideId) + "},\"claude\":{\"monitor\":\"main\"}}}");
            Assert.Equal("applied", (string?)Result()["result"]!["status"]);
            Assert.Equal("clock:bl@" + DeskMonitors.Escape(SideId) + " claude:bl@main", _store.LoadDesk().Layout);
            var changes = Result()["result"]!["changes"]!.AsArray().Select(n => (string)n!).ToList();
            Assert.Contains("Clock and date: moved to 1920\u00D71080, right of main", changes);
            Assert.Contains("Claude meters (line): moved to the main monitor", changes);
            // The look before is kept, and it is on every monitor.
            var before = s.Designs().Single(d => d.Name == "Before Split");
            Assert.All(before.Design.Layout!.Where(p => !p.IsHidden), p => Assert.True(DeskMonitors.FromWord(p.Monitor) is null));
        });

    [Fact]
    public void AMonitorThatIsNotConnectedIsRefusedBeforeAnyPreview() =>
        Sta.Run(() =>
        {
            _store.SaveDeskDirect(true);
            using var s = new DeskDesignService(_paths, _store) { Monitors = () => Connected, InstalledFonts = () => ["Segoe UI"] };
            Propose(s, "m2", """{"layout":{"clock":{"anchor":"tl","monitor":"\\\\?\\DISPLAY#GONE#1"}}}""");
            var result = Result()["result"]!;
            Assert.Equal("rejected", (string?)result["status"]);
            Assert.Equal("layout.clock.monitor", (string?)result["reasons"]![0]!["field"]);
            Assert.Empty(s.Designs());
        });

    [Fact]
    public void TheStateListsTheConnectedMonitorsAndTheWallpaperSummaryCarriesTheirIds() =>
        Sta.Run(() =>
        {
            _store.SaveMcpEnabled(true);
            using var s = new DeskDesignService(_paths, _store) { Monitors = () => Connected, InstalledFonts = () => ["Segoe UI"] };
            s.Publish();
            var state = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.StateFile)))!;
            var monitors = state["monitors"]!.AsArray();
            Assert.Equal([MainId, SideId], monitors.Select(m => (string)m!["id"]!));
            Assert.Equal("3440\u00D71440, main", (string?)monitors[0]!["name"]);
            Assert.Equal("1920\u00D71080, right of main", (string?)monitors[1]!["name"]);
            // Every placed piece of the current design says where it is drawn.
            Assert.Equal("all", (string?)state["design"]!["layout"]!["clock"]!["monitor"]);
            Assert.Empty(DeskDesign.Parse(state["design"]).Reasons);
        });
}
