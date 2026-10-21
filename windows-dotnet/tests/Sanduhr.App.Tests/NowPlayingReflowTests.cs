using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Desk stack closes up when the now-playing piece goes (Command Center's System skin sliding up):
/// real <see cref="DeskView"/> layouts on an STA thread, compared piece by piece. The piece fully gone
/// leaves the other pieces exactly where a layout drawn without it puts them; partway they sit between;
/// and the island's window never changes size while it moves, with the stack held against its anchor.
/// </summary>
public class NowPlayingReflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static readonly NowPlayingInfo Track = new()
    {
        Title = "Midnight City", Artist = "M83", Duration = 243, Elapsed = 101, ElapsedAt = Now, Playing = true, AppId = "Spotify.exe",
    };

    private static DeskView Build(string layout, bool withTrack, double presence, bool plate = false)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI", Plate = plate }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame
        {
            Now = Now,
            Track = withTrack ? Track : null,
            Presence = presence,
            WeatherOn = true, // draws "Set a city in Settings", so it is a neighbour that is always there
        }, DeskInkMap.Light);
        return view;
    }

    public static IEnumerable<object[]> Stacks()
    {
        // The piece first, between two others, and last; bottom, top and middle anchors; with the plate.
        foreach (var anchor in new[] { "bl", "tl", "ml", "br" })
        foreach (var plate in new[] { false, true })
        {
            yield return [$"nowPlaying:{anchor} clock:{anchor} weather:{anchor}", plate];
            yield return [$"clock:{anchor} nowPlaying:{anchor} weather:{anchor}", plate];
            yield return [$"clock:{anchor} weather:{anchor} nowPlaying:{anchor}", plate];
        }
    }

    private static void Near(Rect a, Rect b) =>
        Assert.True(Math.Abs(a.X - b.X) < 0.51 && Math.Abs(a.Y - b.Y) < 0.51
            && Math.Abs(a.Width - b.Width) < 0.51 && Math.Abs(a.Height - b.Height) < 0.51, $"{a} vs {b}");

    [Theory]
    [MemberData(nameof(Stacks))]
    public void GonePieceLeavesNoHole(string layout, bool plate) => Sta.Run(() =>
    {
        var without = Build(layout, withTrack: false, presence: 1, plate).PieceFrames();
        var gone = Build(layout, withTrack: true, presence: 0, plate).PieceFrames();

        Assert.False(gone.ContainsKey("nowPlaying")); // no height, nothing to draw
        foreach (var piece in new[] { "clock", "weather" })
            Near(without[piece], gone[piece]);
    });

    [Theory]
    [MemberData(nameof(Stacks))]
    public void UpPieceTakesItsRoom(string layout, bool plate) => Sta.Run(() =>
    {
        var without = Build(layout, withTrack: false, presence: 1, plate).PieceFrames();
        var up = Build(layout, withTrack: true, presence: 1, plate).PieceFrames();

        Assert.True(up.TryGetValue("nowPlaying", out var np) && np.Height > 10);
        // Something moved to make room: the whole stack is taller by at least the piece.
        double Extent(IReadOnlyDictionary<string, Rect> f, bool withPiece)
        {
            var rects = f.Where(kv => withPiece || kv.Key != "nowPlaying").Select(kv => kv.Value).ToList();
            return rects.Max(r => r.Bottom) - rects.Min(r => r.Top);
        }
        Assert.True(Extent(up, true) >= Extent(without, false) + np.Height - 0.51);
    });

    [Theory]
    [MemberData(nameof(Stacks))]
    public void PartwayTheNeighboursSitBetween(string layout, bool plate) => Sta.Run(() =>
    {
        double Y(double presence, string piece) => Build(layout, true, presence, plate).PieceFrames()[piece].Y;
        foreach (var piece in new[] { "clock", "weather" })
        {
            double closed = Y(0, piece), open = Y(1, piece), half = Y(0.5, piece);
            double lo = Math.Min(closed, open) - 0.51, hi = Math.Max(closed, open) + 0.51;
            Assert.InRange(half, lo, hi);
            // And it is a real move: a quarter and three quarters are ordered along the way.
            double q1 = Y(0.25, piece), q3 = Y(0.75, piece);
            if (open > closed) Assert.True(q1 <= half + 0.51 && half <= q3 + 0.51);
            else Assert.True(q1 >= half - 0.51 && half >= q3 - 0.51);
        }
    });

    [Theory]
    [InlineData("clock:bl nowPlaying:bl weather:bl")]
    [InlineData("nowPlaying:tl clock:tl weather:tl")]
    public void SetPresenceMovesALiveViewInPlace(string layout) => Sta.Run(() =>
    {
        var without = Build(layout, withTrack: false, presence: 1).PieceFrames();
        var view = Build(layout, withTrack: true, presence: 1);
        Assert.True(view.PieceFrames().ContainsKey("nowPlaying"));

        view.SetPresence(0);
        var gone = view.PieceFrames();
        Assert.False(gone.ContainsKey("nowPlaying"));
        foreach (var piece in new[] { "clock", "weather" }) Near(without[piece], gone[piece]);

        view.SetPresence(1);
        Assert.True(view.PieceFrames().ContainsKey("nowPlaying"));
    });

    private static FrameworkElement? Find(DependencyObject node, string tag)
    {
        if (node is FrameworkElement fe && Equals(fe.Tag, tag)) return fe;
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static DeskIsland OnlyIsland(DeskView view)
    {
        var islands = view.TakeIslands();
        Assert.Single(islands);
        return islands[0];
    }

    private static (Size Size, Rect Clock, Rect Weather) Settle(DeskIsland island)
    {
        var content = island.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        Rect Of(string tag) => Find(content, tag) is { } e
            ? e.TransformToAncestor(content).TransformBounds(new Rect(e.RenderSize))
            : Rect.Empty;
        return (content.DesiredSize, Of("clock"), Of("weather"));
    }

    [Theory]
    [InlineData("nowPlaying:bl clock:bl weather:bl")]
    [InlineData("clock:bl nowPlaying:bl weather:bl")]
    [InlineData("clock:bl weather:bl nowPlaying:bl")]
    public void TheIslandWindowDoesNotChangeSizeWhileThePieceMoves(string layout) => Sta.Run(() =>
    {
        var gone = Settle(OnlyIsland(Build(layout, true, 0)));
        var half = Settle(OnlyIsland(Build(layout, true, 0.5)));
        var up = Settle(OnlyIsland(Build(layout, true, 1)));
        Assert.Equal(up.Size, gone.Size);
        Assert.Equal(up.Size, half.Size);
    });

    [Fact]
    public void BottomStackStaysAgainstItsAnchorEdgeWhileThePieceIsGone() => Sta.Run(() =>
    {
        // The piece is last: gone, the weather line is the last piece and sits on the island's bottom edge.
        var gone = Settle(OnlyIsland(Build("clock:bl weather:bl nowPlaying:bl", true, 0)));
        Assert.Equal(gone.Size.Height, gone.Weather.Bottom, 0.51);
        // The piece is first: gone, the clock is the first piece but the stack still ends on the bottom edge.
        var first = Settle(OnlyIsland(Build("nowPlaying:bl clock:bl weather:bl", true, 0)));
        Assert.Equal(first.Size.Height, first.Weather.Bottom, 0.51);
    });

    [Fact]
    public void TopStackStaysAgainstItsAnchorEdgeWhileThePieceIsGone() => Sta.Run(() =>
    {
        // The piece is first: gone, the next piece starts at the top edge (no leftover gap above it).
        var gone = Settle(OnlyIsland(Build("nowPlaying:tl clock:tl weather:tl", true, 0)));
        Assert.Equal(0, Math.Min(gone.Clock.Top, gone.Weather.Top), 0.51);
    });

    [Fact]
    public void TheReflowedPieceKeepsItsTagForClicks() => Sta.Run(() =>
    {
        var island = OnlyIsland(Build("clock:bl nowPlaying:bl", true, 1));
        Assert.NotNull(Find(island.Content, "nowPlaying"));
        Assert.Contains("nowPlaying", island.Widgets);
    });
}
