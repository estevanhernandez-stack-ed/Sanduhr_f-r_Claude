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

    // ---- agent layer: a piece's source ----

    /// <summary>Claude's session 42 and week 93 (Usage), plus Codex's session 20 and week 97: the week continues past Claude's, the session is a tick.</summary>
    private static DeskUsage TwoSources(params string[] staleCodex) => Usage() with
    {
        SourceTiers = new Dictionary<string, IReadOnlyList<DeskTier>>
        {
            ["codex"] = [new DeskTier(TierModel.FiveHour, 20, Iso(Now.AddHours(1))), new DeskTier(TierModel.SevenDay, 97, Iso(Now.AddDays(2)))],
        },
        SourceStaleTiers = new Dictionary<string, IReadOnlySet<string>> { ["codex"] = new HashSet<string>(staleCodex) },
    };

    private static List<string> Ids(FrameworkElement piece) =>
        [.. All<FrameworkElement>(piece).Select(System.Windows.Automation.AutomationProperties.GetAutomationId).Where(id => id.Length > 0)];

    private static bool Drawn(DeskView view, string widget) => All<FrameworkElement>(view).Any(e => e.Tag as string == widget);

    private static string WordFor(string piece, DeskMeterStyle style, string source = "combined")
    {
        string word = style == DeskMeterStyle.Bar ? $"{piece}:tr" : $"{piece}:tr:1:0:{DeskMeterStyles.Word(style)}";
        return source == "combined" ? word : $"{word}:src={source}";
    }

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    public void ACombinedPieceWithTwoSourcesDrawsTheTwoToneFillAndTheTicks(DeskMeterStyle style) => Sta.Run(() =>
    {
        var view = View(WordFor("meters", style), new DeskFrame { Usage = TwoSources(), Now = Now });
        var meters = Piece(view, "meters");
        var ids = Ids(meters);
        Assert.Equal(["source-primary:claude", "source-primary:claude"], ids.Where(i => i.StartsWith("source-primary", StringComparison.Ordinal)));
        Assert.Equal(["source-beyond:codex"], ids.Where(i => i.StartsWith("source-beyond", StringComparison.Ordinal)));   // the week only
        Assert.Equal(["source-tick:codex", "source-tick:codex"], ids.Where(i => i.StartsWith("source-tick", StringComparison.Ordinal)));
        // The rows are the joined windows, with Claude's pace marks kept.
        var texts = All<TextBlock>(meters).Select(t => t.Text).ToList();
        Assert.Contains(style == DeskMeterStyle.Ring ? "93" : "93%", texts);
        if (style != DeskMeterStyle.Ring)
        {
            Assert.Contains("Session (5hr)", texts);
            Assert.Contains("Weekly", texts);
            Assert.Equal(2, All<Rectangle>(meters).Count(r => r.Width == 2));
        }
        else Assert.Equal(2, All<Line>(meters).Count(l => l.StrokeThickness == 2));
        view.Dispose();
    });

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    public void TheCombinedFillWearsEachSourcesOwnColor(DeskMeterStyle style) => Sta.Run(() =>
    {
        var colors = new Dictionary<string, string> { ["claude"] = "#102030", ["codex"] = "#A0B0C0" };
        var view = View(WordFor("meters", style), new DeskFrame { Usage = TwoSources() with { SourceColors = colors }, Now = Now });
        var meters = Piece(view, "meters");
        Brush? Of(string id) => All<FrameworkElement>(meters).Where(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == id)
            .Select(e => e switch { Rectangle r => r.Fill, ShapePath p => p.Stroke, Line l => l.Stroke, _ => null }).FirstOrDefault();
        Assert.Equal(Color.FromRgb(0x10, 0x20, 0x30), ((SolidColorBrush)Of("source-primary:claude")!).Color);
        Assert.Equal(Color.FromRgb(0xA0, 0xB0, 0xC0), ((SolidColorBrush)Of("source-beyond:codex")!).Color);
        Assert.Equal(Color.FromRgb(0xA0, 0xB0, 0xC0), ((SolidColorBrush)Of("source-tick:codex")!).Color);
        view.Dispose();
    });

    [Fact]
    public void ACombinedSlimFillSpansThePrimaryThenTheFurtherSource() => Sta.Run(() =>
    {
        var view = View(WordFor("meters", DeskMeterStyle.Slim), new DeskFrame { Usage = TwoSources(), Now = Now });
        var meters = Piece(view, "meters");
        Rectangle Find(string id) => All<Rectangle>(meters).First(r => System.Windows.Automation.AutomationProperties.GetAutomationId(r) == id);
        double weekPrimary = All<Rectangle>(meters).Where(r => System.Windows.Automation.AutomationProperties.GetAutomationId(r) == "source-primary:claude")
            .Max(r => r.Width);   // the week (93%) is the longer one
        var beyond = Find("source-beyond:codex");
        // The further source starts one radius early, under the primary's rounded end, and runs on past it (to 97%).
        Assert.Equal(weekPrimary, beyond.Margin.Left + beyond.RadiusX, 1);
        Assert.True(beyond.Margin.Left + beyond.Width > weekPrimary);
        view.Dispose();
    });

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    public void ACombinedRowNamesEverySourceAndWindowNotJustItsColor(DeskMeterStyle style) => Sta.Run(() =>
    {
        var view = View(WordFor("meters", style), new DeskFrame { Usage = TwoSources("seven_day"), Now = Now });
        var names = All<FrameworkElement>(Piece(view, "meters")).Select(System.Windows.Automation.AutomationProperties.GetName).Where(n => n.Length > 0).ToList();
        Assert.Contains("Session (5hr). Claude 42%. Codex 20%.", names);
        Assert.Contains("Weekly. Claude 93%. Codex 97%, as of earlier.", names);
        view.Dispose();
    });

    [Fact]
    public void AStaleCodexTierDrawsDimmedAndAFreshOneDoesNot() => Sta.Run(() =>
    {
        var view = View(WordFor("meters", DeskMeterStyle.Slim), new DeskFrame { Usage = TwoSources("seven_day"), Now = Now });
        var meters = Piece(view, "meters");
        Brush Fill(string id) => All<Rectangle>(meters).First(r => System.Windows.Automation.AutomationProperties.GetAutomationId(r) == id).Fill;
        var ticks = All<Rectangle>(meters).Where(r => System.Windows.Automation.AutomationProperties.GetAutomationId(r) == "source-tick:codex").ToList();
        Assert.Equal(1.0, ticks[0].Fill.Opacity);                      // the session's Codex tier is fresh
        Assert.Equal(CombinedBarViewModel.StaleOpacity, ticks[1].Fill.Opacity);   // the weekly one is not
        Assert.Equal(CombinedBarViewModel.StaleOpacity, Fill("source-beyond:codex").Opacity);
        view.Dispose();
    });

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    public void CombinedWithOneSourceDrawsExactlyTodaysRows(DeskMeterStyle style) => Sta.Run(() =>
    {
        // No Codex tiers: the piece is what it was (compare with the same frame under an explicit Claude source).
        var frame = new DeskFrame { Usage = Usage(), Now = Now };
        var combined = View(WordFor("meters", style), frame);
        var claude = View(WordFor("meters", style, "claude"), frame);
        Assert.Empty(Ids(Piece(combined, "meters")));
        Assert.Equal(All<TextBlock>(Piece(claude, "meters")).Select(t => t.Text), All<TextBlock>(Piece(combined, "meters")).Select(t => t.Text));
        Assert.Equal(All<FrameworkElement>(Piece(claude, "meters")).Count(), All<FrameworkElement>(Piece(combined, "meters")).Count());
        Assert.Equal(2, PaceMarks(Piece(combined, "meters")));
        combined.Dispose();
        claude.Dispose();
    });

    [Fact]
    public void APieceSetToClaudeIgnoresCodexAndOneSetToCodexDrawsCodexRowsOnly() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Usage = TwoSources(), Now = Now };
        var claude = View(WordFor("meters", DeskMeterStyle.Slim, "claude"), frame);
        Assert.Empty(Ids(Piece(claude, "meters")));
        Assert.Contains("Weekly - All Models", All<TextBlock>(Piece(claude, "meters")).Select(t => t.Text));
        claude.Dispose();

        var codex = View(WordFor("meters", DeskMeterStyle.Slim, "codex"), frame);
        var meters = Piece(codex, "meters");
        var texts = All<TextBlock>(meters).Select(t => t.Text).ToList();
        Assert.Equal(["20%", "Session (5hr)", "97%", "Weekly"], texts);   // each row: its value, then its label
        Assert.Empty(Ids(meters));                                      // one source: no fill of two
        Assert.Equal(0, All<Rectangle>(meters).Count(r => r.Width == 2));   // Codex has no pace mark
        codex.Dispose();
    });

    [Theory]
    [InlineData(DeskMeterStyle.Bar)]
    [InlineData(DeskMeterStyle.Slim)]
    [InlineData(DeskMeterStyle.Ring)]
    [InlineData(DeskMeterStyle.Ticks)]
    public void ACodexPieceWithCodexOffOrNotInstalledDrawsNothing(DeskMeterStyle style) => Sta.Run(() =>
    {
        foreach (var piece in new[] { "meters", "claude" })
        {
            var view = View(WordFor(piece, style, "codex"), new DeskFrame { Usage = Usage(), Now = Now });   // Claude has numbers, Codex has none
            Assert.False(Drawn(view, piece));
            view.Dispose();
            // Signed out of Claude does not bring a sign-in line to a Codex piece either.
            var signedOut = View(WordFor(piece, style, "codex"), new DeskFrame { Usage = Usage() with { SignInNeeded = true }, Now = Now });
            Assert.False(Drawn(signedOut, piece));
            signedOut.Dispose();
        }
    });

    [Fact]
    public void ACombinedPieceWithNoClaudeAndACodexDrawsCodexAlone() => Sta.Run(() =>
    {
        var codexOnly = TwoSources() with { Tiers = [], LineTiers = [] };
        var view = View("meters:tr", new DeskFrame { Usage = codexOnly, Now = Now });
        Assert.Equal(["20%", "Session (5hr)", "97%", "Weekly"], All<TextBlock>(Piece(view, "meters")).Select(t => t.Text).Where(t => !t.StartsWith("resets", StringComparison.Ordinal)));
        view.Dispose();
        // A sign-in still shows the sign-in line: Claude is on.
        var signIn = View("meters:tr", new DeskFrame { Usage = codexOnly with { SignInNeeded = true }, Now = Now });
        Assert.Equal(["sign in again in Sanduhr"], All<TextBlock>(Piece(signIn, "meters")).Select(t => t.Text));
        signIn.Dispose();
    });

    [Fact]
    public void TheClaudeLineReadsCodexForACodexPieceAndIsUnchangedForClaude() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Usage = TwoSources(), Now = Now };
        var codex = View("claude:bl:src=codex", frame);
        Assert.Equal(["Codex 5h 20% · wk 97%"], All<TextBlock>(Piece(codex, "claude")).Select(t => t.Text));
        codex.Dispose();
        foreach (var source in new[] { "combined", "claude" })
        {
            var view = View(source == "combined" ? "claude:bl" : "claude:bl:src=claude", frame);
            Assert.Equal([DeskClaudeText.Line(frame.Usage)!], All<TextBlock>(Piece(view, "claude")).Select(t => t.Text));
            view.Dispose();
        }
    });

    [Fact]
    public void TheClaudeLineInRingsCombinesTheSourcesAndNamesCodexWhenSetToIt() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Usage = TwoSources(), Now = Now };
        var combined = View("claude:bl:1:0:ring", frame);
        var ids = Ids(Piece(combined, "claude"));
        Assert.Equal(2, ids.Count(i => i == "source-tick:codex"));
        Assert.Contains("source-beyond:codex", ids);
        combined.Dispose();

        var codex = View("claude:bl:1:0:ring:src=codex", frame);
        var texts = All<TextBlock>(Piece(codex, "claude")).Select(t => t.Text).ToList();
        Assert.Contains("Codex", texts);
        Assert.Contains("20", texts);
        Assert.Contains("97", texts);
        Assert.Empty(Ids(Piece(codex, "claude")));
        Assert.Empty(All<Line>(Piece(codex, "claude")));   // no pace on Codex
        codex.Dispose();
    });

    [Fact]
    public void TheClaudeLinesSlimFormForCodexIsItsOwnTwoRows() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Usage = TwoSources(), Now = Now };
        var view = View("claude:bl:1:0:slim:src=codex", frame);
        var texts = All<TextBlock>(Piece(view, "claude")).Select(t => t.Text).ToList();
        Assert.Equal(DeskClaudeText.Compact(DeskMeters.ForSource(frame.Usage, "codex", Now))!, texts);
        Assert.StartsWith("Codex", texts[0], StringComparison.Ordinal);
        view.Dispose();
    });

    // ---- Settings, Desk: Shows ----

    [Fact]
    public void SettingsOffersShowsOnTheClaudeLineAndTheClaudeLimitsAndSavesIt()
    {
        Directory.CreateDirectory(_root);
        var store = new SettingsStore(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl meters:tr:1:0:ring telemetry:br" });
        var page = new DeskSettingsViewModel(store, ["Segoe UI"], () => []);
        Assert.Equal(["claude", "meters"], page.Pieces.Where(p => p.HasSource).Select(p => p.Widget).Order());
        Assert.Equal(["Combined", "Claude", "Codex"], DeskSettingsViewModel.SourceOptions.Select(o => o.Name));
        var meters = page.Pieces.Single(p => p.Widget == "meters");
        Assert.Equal("combined", meters.Source.Source);

        meters.Source = DeskSettingsViewModel.SourceFor("codex");
        Assert.Equal("codex", new DeskArrangement(store.LoadDesk().Layout).Placement("meters")!.Source);
        Assert.Equal("clock:bl meters:tr:1:0:ring:src=codex telemetry:br", store.LoadDesk().Layout);
        meters.Source = DeskSettingsViewModel.SourceFor("combined");
        Assert.DoesNotContain("src=", store.LoadDesk().Layout);
        // A reload (a newer layout from elsewhere) reads the picker back.
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "meters:tr:src=claude" });
        var again = new DeskSettingsViewModel(store, ["Segoe UI"], () => []);
        Assert.Equal("claude", again.Pieces.Single(x => x.Widget == "meters").Source.Source);
    }

    [Fact]
    public void TheShowsPickerIsAComboBoxNamedForItsPieceAndOnlyOnThePiecesThatTakeASource() => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        Directory.CreateDirectory(_root);
        var store = new SettingsStore(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl:1:0:slim meters:tr telemetry:tr" });
        var page = new DeskSettingsViewModel(store, ["Segoe UI"], () => []);
        var panel = new DeskPiecesPanel { DataContext = page };
        var window = new Window { Content = panel, Width = 700, Height = 900, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Left = -20000;
        window.Show();
        try
        {
            panel.UpdateLayout();
            var combos = Walk(UIElementAutomationPeer.CreatePeerForElement(panel))
                .Where(p => p.GetAutomationControlType() == AutomationControlType.ComboBox).Select(p => p.GetName()).ToList();
            Assert.Contains($"{SettingsNames.DeskClaudeLine}, shows", combos);
            Assert.Contains($"{SettingsNames.DeskClaudeBars}, shows", combos);
            Assert.DoesNotContain($"{SettingsNames.DeskTelemetry}, shows", combos);
            Assert.DoesNotContain($"{SettingsNames.DeskClock}, shows", combos);
            // The style pickers are still there beside them.
            Assert.Contains($"{SettingsNames.DeskTelemetry}, style", combos);
        }
        finally { window.Close(); }
    });
}
