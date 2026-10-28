using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Line = System.Windows.Shapes.Line;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using ShapePath = System.Windows.Shapes.Path;

namespace Sanduhr.App.Tests;

/// <summary>
/// Meter styles on the Desk itself (#336): each style draws the Claude limits with their pace marks, telemetry's rings sit side by
/// side and update in place, the Claude line's slim form is two short rows, a slanted stack of styled pieces is one sheared face that
/// Arrange Desk hits along its slant, a bar still draws exactly as before, and Settings, Desk has a named Style picker per piece that
/// has a style. Over a scratch AppData.
/// </summary>
public sealed class DeskMeterStylesViewTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-meterstyles-" + Guid.NewGuid().ToString("N")[..8]);

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

    private static DeskView View(string layout, DeskFrame frame)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI" }).Normalized();
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

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Session 42% and week 93% (over the warning line), both with reset times, so both carry pace marks.</summary>
    private static DeskUsage Usage() => new(
        [new DeskTier(TierModel.FiveHour, 42, Iso(Now.AddHours(2))), new DeskTier(TierModel.SevenDay, 93, Iso(Now.AddDays(3)))],
        Now.AddMinutes(-1), false, false, null);

    private static FrameworkElement Piece(DeskView view, string widget) =>
        All<FrameworkElement>(view).First(e => e.Tag as string == widget);

    /// <summary>The pace marks a piece draws: 2 px uprights in slim and ticks, a crossing line in a ring.</summary>
    private static int PaceMarks(FrameworkElement piece) =>
        All<Rectangle>(piece).Count(r => r.Width == 2) + All<Line>(piece).Count();

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    [InlineData(DeskMeterStyle.Ticks)]
    public void EveryStyleKeepsAPaceMarkOnEveryClaudeLimit(DeskMeterStyle style) => Sta.Run(() =>
    {
        string word = style == DeskMeterStyle.Bar ? "meters:tr" : $"meters:tr:1:0:{DeskMeterStyles.Word(style)}";
        var view = View(word, new DeskFrame { Usage = Usage(), Now = Now });
        var meters = Piece(view, "meters");
        Assert.Equal(2, PaceMarks(meters));
        var texts = All<TextBlock>(meters).Select(t => t.Text).ToList();
        Assert.Contains(style == DeskMeterStyle.Ring ? "93" : "93%", texts);
        // The week (93) is past Warn at (80) and under Urgent at (95): its value is yellow in every style.
        var warn = All<TextBlock>(meters).Single(t => t.Text.StartsWith("93", StringComparison.Ordinal));
        Assert.Equal(Color.FromRgb(0xFA, 0xCC, 0x15), ((SolidColorBrush)warn.Foreground).Color);
        view.Dispose();
    });

    [Fact]
    public void ABarDrawsTheMetersExactlyAsBeforeWithTheirResetLines() => Sta.Run(() =>
    {
        var view = View("meters:tr", new DeskFrame { Usage = Usage(), Now = Now });
        var meters = Piece(view, "meters");
        Assert.Equal(2, All<TextBlock>(meters).Count(t => t.Text.StartsWith("resets ", StringComparison.Ordinal)));
        Assert.Empty(All<ShapePath>(meters));
        Assert.Empty(All<Canvas>(meters));
        view.Dispose();
    });

    [Fact]
    public void SlimIsAboutHalfABarsHeight() => Sta.Run(() =>
    {
        double Height(string layout)
        {
            var view = View(layout, new DeskFrame { Usage = Usage(), Now = Now });
            var frames = view.PieceFrames();
            view.Dispose();
            return frames["meters"].Height;
        }
        double bar = Height("meters:tr"), slim = Height("meters:tr:1:0:slim");
        Assert.InRange(slim / bar, 0.3, 0.65);
    });

    [Fact]
    public void TicksAreTenCellsPerReadingAndLightToTheReading() => Sta.Run(() =>
    {
        var view = View("meters:tr:1:0:ticks", new DeskFrame { Usage = Usage(), Now = Now });
        var canvases = All<Canvas>(Piece(view, "meters")).ToList();
        Assert.Equal(2, canvases.Count);
        var session = canvases[0].Children.OfType<Rectangle>().Where(r => r.Width != 2).ToList();
        Assert.Equal(10, session.Count);
        Assert.Equal(4, session.Count(c => c.Fill is not null));     // 42% lights four
        Assert.All(session.Where(c => c.Fill is null), c => Assert.NotNull(c.Stroke));   // the rest are faint outlines
        view.Dispose();
    });

    [Fact]
    public void TelemetryRingsSitSideBySideAndUpdateInPlace() => Sta.Run(() =>
    {
        var source = new TelemetrySource();
        source.Preview(new TelemetrySample(23, 61, 48, Now));
        var view = View("telemetry:tr:1:0:ring", new DeskFrame { Now = Now, Telemetry = source });
        var row = (StackPanel)Piece(view, "telemetry");
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        Assert.Equal(3, row.Children.Count);
        Assert.Equal(["CPU", "RAM", "Disk"], All<TextBlock>(row).Where(t => t.Text is "CPU" or "RAM" or "Disk").Select(t => t.Text));
        Assert.Empty(All<Line>(row));   // no pace on a system reading
        var arcs = All<ShapePath>(row).ToList();
        var before = arcs[3].Data.Bounds;   // RAM's arc (each ring: its track, then its arc)
        source.Preview(new TelemetrySample(23, 97, 48, Now.AddSeconds(5)));
        Assert.Contains("97", All<TextBlock>(row).Select(t => t.Text));
        Assert.NotEqual(before, arcs[3].Data.Bounds);
        view.Dispose();
        source.Dispose();
    });

    [Fact]
    public void TheClaudeLinesSlimFormIsTwoShortRows() => Sta.Run(() =>
    {
        var usage = Usage();
        var line = View("claude:bl", new DeskFrame { Usage = usage, Now = Now });
        double wide = line.PieceFrames()["claude"].Width;
        line.Dispose();
        var view = View("claude:bl:1:0:slim", new DeskFrame { Usage = usage, Now = Now });
        var texts = All<TextBlock>(Piece(view, "claude")).Select(t => t.Text).ToList();
        Assert.Equal(DeskClaudeText.Compact(usage)!, texts);
        Assert.True(view.PieceFrames()["claude"].Width < wide * 0.7);
        view.Dispose();
    });

    [Fact]
    public void TheClaudeLineInRingsShowsTheSessionAndTheWeekWithPaceMarks() => Sta.Run(() =>
    {
        var view = View("claude:bl:1:0:ring", new DeskFrame { Usage = Usage(), Now = Now });
        var claude = Piece(view, "claude");
        var texts = All<TextBlock>(claude).Select(t => t.Text).ToList();
        Assert.Contains("claude", texts);
        Assert.Contains("session", texts);
        Assert.Contains("week", texts);
        Assert.Equal(2, All<Line>(claude).Count());
        // A sign-in falls back to the two rows.
        var signIn = View("claude:bl:1:0:ring", new DeskFrame { Usage = Usage() with { SignInNeeded = true }, Now = Now });
        Assert.Equal(["claude", "sign in again in Sanduhr"], All<TextBlock>(Piece(signIn, "claude")).Select(t => t.Text));
        view.Dispose();
        signIn.Dispose();
    });

    [Theory]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    [InlineData(DeskMeterStyle.Ticks)]
    public void ASlantedStackOfStyledPiecesIsOneShearedFace(DeskMeterStyle style) => Sta.Run(() =>
    {
        string s = DeskMeterStyles.Word(style);
        var view = View($"claude:mr:1:18:{s} meters:mr:1:18:{s}", new DeskFrame { Usage = Usage(), Now = Now });
        var quads = view.PieceQuads();
        foreach (var w in new[] { "claude", "meters" })
        {
            var q = quads[w];
            Assert.False(q.IsUpright);
            Assert.Equal((q.TopRight.X - q.TopLeft.X) * Math.Tan(18 * Math.PI / 180), q.TopRight.Y - q.TopLeft.Y, 1);
        }
        Assert.Equal(2, PaceMarks(Piece(view, "meters")));
        var island = view.TakeIslands().Single();
        Assert.Equal(18, Assert.IsType<SkewTransform>(island.Content.LayoutTransform).AngleY);
        view.Dispose();
    });

    [Fact]
    public void ArrangeDeskHitsARingRowAlongItsSlant() => Sta.Run(() =>
    {
        const string layout = "telemetry:mr:1:18:ring meters:tl:1:0:ticks";
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
        var source = new TelemetrySource();
        source.Preview(new TelemetrySample(23, 61, 48, Now));
        var frame = new DeskFrame { Usage = Usage(), Now = Now, Telemetry = source };
        var content = new DeskArrangeContent(Screen(), settings, new DeskFonts(), frame, new DeskArrangement(layout), primary: true);
        try
        {
            var r = content.PieceRects["telemetry"];
            // +18 falls to the right: the box's top right and bottom left are off the face; its top left and bottom right are on it.
            Assert.Equal("telemetry", content.PieceAt(new Point(r.Left + 3, r.Top + 3)));
            Assert.Equal("telemetry", content.PieceAt(new Point(r.Right - 3, r.Bottom - 3)));
            Assert.Null(content.PieceAt(new Point(r.Right - 3, r.Top + 3)));
            Assert.Null(content.PieceAt(new Point(r.Left + 3, r.Bottom - 3)));
            var m = content.PieceRects["meters"];
            Assert.Equal("meters", content.PieceAt(new Point(m.Right - 2, m.Bottom - 2)));
        }
        finally
        {
            content.Dispose();
            source.Dispose();
        }
    });

    // ---- Settings, Desk ----

    [Fact]
    public void SettingsOffersAStylePerPieceThatHasOneAndSavesIt()
    {
        Directory.CreateDirectory(_root);
        var store = new SettingsStore(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl meters:tr:1:0:ring" });
        var page = new DeskSettingsViewModel(store, ["Segoe UI"], () => []);
        var meters = page.Pieces.Single(p => p.Widget == "meters");
        Assert.True(meters.HasStyle);
        Assert.Equal(DeskMeterStyle.Ring, meters.Style.Style);
        Assert.False(page.Pieces.Single(p => p.Widget == "clock").HasStyle);
        Assert.Equal(["claude", "meters", "telemetry"], page.Pieces.Where(p => p.HasStyle).Select(p => p.Widget).Order());
        Assert.Equal(["Bars", "Slim", "Rings", "Ticks"], DeskSettingsViewModel.StyleOptions.Select(o => o.Name));

        meters.Style = DeskSettingsViewModel.StyleFor(DeskMeterStyle.Ticks);
        Assert.Equal(DeskMeterStyle.Ticks, new DeskArrangement(store.LoadDesk().Layout).Placement("meters")!.Style);
        meters.Style = DeskSettingsViewModel.StyleFor(DeskMeterStyle.Bar);
        Assert.Contains("meters:tr", store.LoadDesk().Layout);
        Assert.DoesNotContain(":bar", store.LoadDesk().Layout);
    }

    [Fact]
    public void TheStylePickerIsAComboBoxNamedForItsPiece() => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        Directory.CreateDirectory(_root);
        var store = new SettingsStore(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl:1:0:slim telemetry:tr" });
        var page = new DeskSettingsViewModel(store, ["Segoe UI"], () => []);
        var panel = new DeskPiecesPanel { DataContext = page };
        var window = new Window { Content = panel, Width = 700, Height = 900, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Left = -20000;
        window.Show();
        try
        {
            panel.UpdateLayout();
            var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(panel)).ToList();
            var combos = peers.Where(p => p.GetAutomationControlType() == AutomationControlType.ComboBox).Select(p => p.GetName()).ToList();
            Assert.Contains($"{SettingsNames.DeskClaudeLine}, style", combos);
            Assert.Contains($"{SettingsNames.DeskClaudeBars}, style", combos);
            Assert.Contains($"{SettingsNames.DeskTelemetry}, style", combos);
            Assert.DoesNotContain($"{SettingsNames.DeskClock}, style", combos);
            var claude = peers.Single(p => p.GetName() == $"{SettingsNames.DeskClaudeLine}, style");
            Assert.True(claude.IsEnabled());
            Assert.Contains("Rings", claude.GetHelpText());
            Assert.False(peers.Single(p => p.GetName() == $"{SettingsNames.DeskClaudeBars}, style").IsEnabled());   // meters hidden
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheStylePickerIsFoundBySearch()
    {
        foreach (var query in new[] { "rings", "ticks", "meter style" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskPieces);
    }

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    // ---- theme match: the usage signal on the Desk's meters ----

    /// <summary>The bar row's fill rectangles, in tier order (pace marks are 2 px wide; tracks stretch).</summary>
    private static List<Rectangle> Fills(FrameworkElement meters) =>
        All<Rectangle>(meters).Where(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Width > 2).ToList();

    [Fact]
    public void A_meter_past_Warn_at_wears_a_yellow_middle_stop_and_under_it_draws_as_before() => Sta.Run(() =>
    {
        var view = View("meters:tr", new DeskFrame { Usage = Usage(), Now = Now });   // session 42, week 93
        var fills = Fills(Piece(view, "meters"));
        Assert.IsType<SolidColorBrush>(fills[0].Fill);   // calm: the ink itself (Light ink is one white)
        var week = Assert.IsType<LinearGradientBrush>(fills[1].Fill);
        Assert.Equal([Colors.White, Color.FromRgb(0xFA, 0xCC, 0x15), Colors.White], week.GradientStops.Select(s => s.Color));
        var calm = All<TextBlock>(Piece(view, "meters")).Single(t => t.Text == "42%");
        Assert.Equal(Colors.White, ((SolidColorBrush)calm.Foreground).Color);
        view.Dispose();
    });

    [Fact]
    public void The_alert_thresholds_in_the_frame_move_the_signal() => Sta.Run(() =>
    {
        var view = View("meters:tr", new DeskFrame { Usage = Usage(), Now = Now, WarnAt = 40, UrgentAt = 90 });
        var fills = Fills(Piece(view, "meters"));
        Assert.Equal(Color.FromRgb(0xFA, 0xCC, 0x15), Assert.IsType<LinearGradientBrush>(fills[0].Fill).GradientStops[1].Color);
        Assert.Equal(Color.FromRgb(0xF8, 0x71, 0x71), Assert.IsType<LinearGradientBrush>(fills[1].Fill).GradientStops[1].Color);
        var urgent = All<TextBlock>(Piece(view, "meters")).Single(t => t.Text == "93%");
        Assert.Equal(Color.FromRgb(0xF8, 0x71, 0x71), ((SolidColorBrush)urgent.Foreground).Color);
        view.Dispose();
    });
}
