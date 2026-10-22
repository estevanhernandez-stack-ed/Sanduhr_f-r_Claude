using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Launcher piece on the Desk (spec, Desk Launcher): real <see cref="DeskView"/> layouts on an STA thread. One
/// automation element per button, named by its label or the app's name; one hit region per button, exactly as the
/// layout says; a missing app draws dimmed; the Launcher always takes clicks.
/// </summary>
public class LauncherPieceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1.01, $"{expected} vs {actual}");

    /// <summary>True when something under the point would be given the mouse: a visual hit whose own chain up to the piece is hit-testable
    /// (VisualTreeHelper.HitTest also reports elements marked IsHitTestVisible=false, which input routing skips).</summary>
    private static bool TakesMouseAt(FrameworkElement piece, Point p)
    {
        bool taken = false;
        VisualTreeHelper.HitTest(piece, null, result =>
        {
            bool visible = true;
            for (DependencyObject? d = result.VisualHit; d is not null && !ReferenceEquals(d, piece); d = VisualTreeHelper.GetParent(d))
                if (d is UIElement { IsHitTestVisible: false }) { visible = false; break; }
            if (visible && !ReferenceEquals(result.VisualHit, piece)) taken = true;
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        return taken;
    }

    private static LauncherButton App(string id, string? label = null) => LauncherButton.Create(LauncherKind.App, id, null, label, null)!;

    private static LauncherModel Model(LauncherOrientation o, params LauncherButton[] buttons) => new(o, buttons);

    private static DeskView Build(string layout, DeskLauncherFrame? frame, double scale = 1)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Now = Now, Launcher = frame }, DeskInkMap.Light);
        return view;
    }

    private static LauncherPieceVisual? Piece(FrameworkElement root)
    {
        LauncherPieceVisual? found = null;
        void Walk(DependencyObject d)
        {
            if (d is LauncherPieceVisual p) { found = p; return; }
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            {
                Walk(child);
                if (found is not null) return;
            }
        }
        Walk(root);
        return found;
    }

    private static DeskIsland LauncherIsland(DeskView view)
    {
        var island = view.TakeIslands().Single(i => i.Widgets.Contains("launcher"));
        island.Content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        island.Content.Arrange(new Rect(island.Content.DesiredSize));
        island.Content.UpdateLayout();
        return island;
    }

    private sealed class Actions : IDeskClickActions
    {
        public List<int> Launched { get; } = [];
        public void PlayPause() { }
        public void OpenWeatherCredit() { }
        public void LaunchButton(int index) => Launched.Add(index);
        public void Menu(DeskMenuAction action) { }
    }

    private static DeskLauncherFrame Frame(LauncherModel model, IEnumerable<string>? missing = null, Dictionary<string, string>? names = null) =>
        new(model, new HashSet<string>(missing ?? []), new Dictionary<string, ImageSource>(), names ?? [], new Dictionary<string, ImageSource>());

    [Fact]
    public void EachButtonIsOneAutomationElementNamedByItsLabelOrTheAppName() => Sta.Run(() =>
    {
        var model = Model(LauncherOrientation.Row,
            App("Chrome._crx_dash", "Dash"),
            App("Spotify"),
            LauncherButton.Create(LauncherKind.Link, "https://www.example.com/x")!);
        var frame = Frame(model, names: new() { ["Spotify"] = "Spotify Music", ["Chrome._crx_dash"] = "626Labs Dashboard" });
        var island = LauncherIsland(Build("launcher:bl", frame));
        var piece = Piece(island.Content)!;

        var peer = UIElementAutomationPeer.CreatePeerForElement(piece);
        Assert.Equal("Launcher", peer.GetName());
        var buttons = peer.GetChildren();
        Assert.Equal(3, buttons.Count);
        Assert.All(buttons, b => Assert.Equal(AutomationControlType.Button, b.GetAutomationControlType()));
        Assert.Equal(["Dash", "Spotify Music", "example.com"], buttons.Select(b => b.GetName()));
        Assert.All(buttons, b => Assert.NotNull(b.GetPattern(PatternInterface.Invoke)));
    });

    [Fact]
    public void AnAppWithNoNameYetIsNamedByItsId() => Sta.Run(() =>
    {
        var island = LauncherIsland(Build("launcher:bl", Frame(Model(LauncherOrientation.Row, App("Spotify")))));
        var buttons = UIElementAutomationPeer.CreatePeerForElement(Piece(island.Content)!).GetChildren();
        Assert.Equal("Spotify", Assert.Single(buttons).GetName());
    });

    [Fact]
    public void InvokingAButtonThroughAutomationLaunchesIt() => Sta.Run(() =>
    {
        var actions = new Actions();
        var model = Model(LauncherOrientation.Row, App("A"), App("B"), App("C"));
        var island = DeskClicks.Prepare([LauncherIsland(Build("launcher:bl", Frame(model)))], piecesTakeClicks: false, actions).Single();
        var peers = UIElementAutomationPeer.CreatePeerForElement(Piece(island.Content)!).GetChildren();

        ((IInvokeProvider)peers[1].GetPattern(PatternInterface.Invoke)).Invoke();
        // The invoke is queued on the dispatcher; run it.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        Assert.Equal([1], actions.Launched);
    });

    [Theory]
    [InlineData(LauncherOrientation.Row, 1.0)]
    [InlineData(LauncherOrientation.Row, 0.6)]
    [InlineData(LauncherOrientation.Row, 1.6)]
    [InlineData(LauncherOrientation.Column, 1.0)]
    [InlineData(LauncherOrientation.Column, 1.6)]
    public void EachButtonHasItsOwnHitRegionAndTheGapsBetweenPassClicks(LauncherOrientation o, double scale) => Sta.Run(() =>
    {
        var model = Model(o, App("A", "One"), App("B"), App("C", "Three"), App("D"));
        var island = LauncherIsland(Build($"launcher:bl:{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}", Frame(model)));
        var piece = Piece(island.Content)!;
        var geometry = LauncherLayout.Compute(4, o, DeskSettings.Default.TimeSize * scale, anyLabel: true);

        Assert.Equal(4, piece.Buttons.Count);
        Near(geometry.Width, piece.Width);
        Near(geometry.Height, piece.Height);

        // The click rule over the real frames the buttons render at.
        var origin = piece.TransformToAncestor(island.Content).Transform(new Point(0, 0));
        var elements = piece.Buttons.Select(b =>
        {
            var r = b.TransformToAncestor(island.Content).TransformBounds(new Rect(b.RenderSize));
            return new DeskElement(DeskElementKind.LauncherButton, b.Index.ToString(), new DeskRect(r.X, r.Y, r.Width, r.Height));
        }).ToList();
        for (int i = 0; i < 4; i++)
        {
            var f = geometry.Slots[i].Frame;
            var e = elements[i];
            // Layout rounding snaps a frame to whole device pixels: within one.
            Near(origin.X + f.X, e.Frame.X);
            Near(origin.Y + f.Y, e.Frame.Y);
            Near(f.Width, e.Frame.Width);
            Near(f.Height, e.Frame.Height);
            Assert.Equal(i.ToString(), DeskHitTest.Element(e.Frame.MidX, e.Frame.MidY, elements)?.Key);
        }
        for (int i = 0; i < 3; i++)
        {
            var a = elements[i].Frame;
            var b = elements[i + 1].Frame;
            double gx = o == LauncherOrientation.Row ? (a.Right + b.Left) / 2 : a.MidX;
            double gy = o == LauncherOrientation.Row ? a.MidY : (a.Bottom + b.Top) / 2;
            Assert.Null(DeskHitTest.Element(gx, gy, elements));
            // WPF agrees: nothing that takes the mouse is in the piece between two buttons.
            Assert.False(TakesMouseAt(piece, new Point(gx - origin.X, gy - origin.Y)));
        }
    });

    [Fact]
    public void TheLauncherAlwaysTakesClicksWhateverTheSwitchSays() => Sta.Run(() =>
    {
        var model = Model(LauncherOrientation.Row, App("A"));
        foreach (var piecesTakeClicks in new[] { false, true })
        {
            var island = LauncherIsland(Build("launcher:bl clock:tl", Frame(model)));
            var all = island.Content is null ? [] : new[] { island };
            var prepared = DeskClicks.Prepare(all, piecesTakeClicks, new Actions()).Single();
            Assert.True(prepared.TakesClicks);
        }
    });

    [Fact]
    public void AnIslandWithoutTheLauncherStaysClickThroughWhenThePiecesDoNotTakeClicks() => Sta.Run(() =>
    {
        var view = Build("clock:bl launcher:tr", Frame(Model(LauncherOrientation.Row, App("A"))));
        var prepared = DeskClicks.Prepare(view.TakeIslands(), piecesTakeClicks: false, new Actions());
        Assert.Contains(prepared, i => i.Widgets.Contains("launcher") && i.TakesClicks);
        Assert.Contains(prepared, i => i.Widgets.Contains("clock") && !i.TakesClicks);
    });

    [Fact]
    public void AMissingAppDrawsDimmedAndNamesItselfAsGone() => Sta.Run(() =>
    {
        var model = Model(LauncherOrientation.Row, App("Gone", "Old"), App("Here", "New"));
        var island = LauncherIsland(Build("launcher:bl", Frame(model, missing: ["Gone"])));
        var piece = Piece(island.Content)!;
        Assert.True(piece.Buttons[0].Missing);
        Assert.True(piece.Buttons[0].Opacity < 0.5);
        Assert.False(piece.Buttons[1].Missing);
        Assert.Equal(1, piece.Buttons[1].Opacity);
        var names = UIElementAutomationPeer.CreatePeerForElement(piece).GetChildren().Select(c => c.GetName()).ToList();
        Assert.Equal(["Old, not installed", "New"], names);
    });

    [Fact]
    public void NoButtonsDrawsNothingAndArrangeModeDrawsTheName() => Sta.Run(() =>
    {
        var empty = Build("launcher:bl clock:bl", Frame(LauncherModel.Empty));
        Assert.False(empty.PieceFrames().ContainsKey("launcher"));
        var none = Build("launcher:bl clock:bl", null);
        Assert.False(none.PieceFrames().ContainsKey("launcher"));

        var settings = (DeskSettings.Default with { Enabled = true, Layout = "launcher:bl clock:bl", Font = "Segoe UI" }).Normalized();
        var arranging = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"])) { Arranging = true };
        arranging.Update(new DeskFrame { Now = Now }, DeskInkMap.Light);
        Assert.True(arranging.PieceFrames().ContainsKey("launcher"));
    });

    [Fact]
    public void ALauncherIsPlacedLikeAnyOtherPieceAndScalesWithItsSize() => Sta.Run(() =>
    {
        var model = Model(LauncherOrientation.Row, App("A"), App("B"));
        var small = Build("launcher:tr:0.6", Frame(model)).PieceFrames()["launcher"];
        var big = Build("launcher:tr:1.6", Frame(model)).PieceFrames()["launcher"];
        Assert.True(big.Width > small.Width * 2.5);
        // Anchored top right: its right edge is the same at both sizes.
        Assert.Equal(small.Right, big.Right, 1);
        Assert.Equal(small.Top, big.Top, 1);
    });

    [Fact]
    public void GlowAndDipAreSetPerButton() => Sta.Run(() =>
    {
        var model = Model(LauncherOrientation.Row, App("A"), App("B"));
        var piece = Piece(LauncherIsland(Build("launcher:bl", Frame(model))).Content)!;
        piece.Buttons[1].SetGlow(true);
        piece.Buttons[1].SetDip(true);
        Assert.True(piece.Buttons[1].IsGlowing && piece.Buttons[1].IsDipped);
        Assert.False(piece.Buttons[0].IsGlowing || piece.Buttons[0].IsDipped);
        piece.Buttons[1].SetGlow(false);
        piece.Buttons[1].SetDip(false);
        Assert.False(piece.Buttons[1].IsGlowing || piece.Buttons[1].IsDipped);
    });
}
