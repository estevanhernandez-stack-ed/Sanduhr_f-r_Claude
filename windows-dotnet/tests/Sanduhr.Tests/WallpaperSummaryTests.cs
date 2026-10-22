using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The wallpaper summary's math on generated pictures: flat, gradient, two halves, a busy corner.</summary>
public class WallpaperSummaryTests
{
    private sealed class Picture(int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Bgra { get; } = new byte[width * height * 4];
        public int Stride => Width * 4;

        public Picture Fill(Func<int, int, (byte R, byte G, byte B)> pixel)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var (r, g, b) = pixel(x, y);
                    int o = y * Stride + x * 4;
                    Bgra[o] = b; Bgra[o + 1] = g; Bgra[o + 2] = r; Bgra[o + 3] = 255;
                }
            }
            return this;
        }

        public WallpaperSummary Summary() =>
            WallpaperSummary.From(Bgra, Width, Height, Stride, WallpaperSummary.DefaultAnchors(Width, Height))!;
    }

    [Fact]
    public void AFlatPictureHasOneColorAndNoEdges()
    {
        var s = new Picture(160, 90).Fill((_, _) => (200, 100, 50)).Summary();

        var dominant = Assert.Single(s.Dominant);
        Assert.Equal("#c86432", dominant.Hex);
        Assert.Equal(1.0, dominant.Share);
        Assert.Equal(Math.Round(InkHex.Luminance(200, 100, 50), 3), s.Brightness);
        Assert.Equal(8, s.Anchors.Count);
        Assert.All(s.Anchors, a =>
        {
            Assert.Equal(s.Brightness, a.Brightness);
            Assert.Equal(0, a.Busyness);
        });
    }

    [Fact]
    public void ABlackPictureIsDarkAndAWhiteOneIsBright()
    {
        Assert.Equal(0, new Picture(64, 64).Fill((_, _) => (0, 0, 0)).Summary().Brightness);
        Assert.Equal(1, new Picture(64, 64).Fill((_, _) => (255, 255, 255)).Summary().Brightness);
    }

    [Fact]
    public void AGradientIsDarkerOnTheLeftThanTheRightAndHasNoEdges()
    {
        var s = new Picture(256, 144).Fill((x, _) => ((byte)x, (byte)x, (byte)x)).Summary();

        Assert.True(s.At(DeskAnchor.TL)!.Brightness < s.At(DeskAnchor.TC)!.Brightness);
        Assert.True(s.At(DeskAnchor.TC)!.Brightness < s.At(DeskAnchor.TR)!.Brightness);
        Assert.True(s.At(DeskAnchor.ML)!.Brightness < s.At(DeskAnchor.MR)!.Brightness);
        // The same column reads the same top and bottom.
        Assert.Equal(s.At(DeskAnchor.TL)!.Brightness, s.At(DeskAnchor.BL)!.Brightness);
        Assert.All(s.Anchors, a => Assert.Equal(0, a.Busyness));
        Assert.True(s.Brightness is > 0.1 and < 0.4);   // the mean of a gamma ramp's luminance
    }

    [Fact]
    public void TwoHalvesAreTwoDominantColorsOfEqualShare()
    {
        var s = new Picture(100, 100).Fill((x, _) => x < 50 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255)).Summary();

        Assert.Equal(2, s.Dominant.Count);
        Assert.Equal(["#000000", "#ffffff"], s.Dominant.Select(c => c.Hex).Order(StringComparer.Ordinal));
        Assert.All(s.Dominant, c => Assert.Equal(0.5, c.Share));
        Assert.Equal(0.5, s.Brightness, 2);
    }

    [Fact]
    public void AtMostFiveDominantColors_LargestFirst_SharesAddUp()
    {
        // Eight vertical bands of unequal width in distinct colors.
        (byte R, byte G, byte B)[] bands = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0), (255, 0, 255), (0, 255, 255), (128, 128, 128), (30, 60, 90)];
        var s = new Picture(240, 40).Fill((x, _) => bands[Math.Min(7, x / 30)]).Summary();

        Assert.Equal(WallpaperSummary.MaxDominant, s.Dominant.Count);
        Assert.Equal(s.Dominant.OrderByDescending(c => c.Share).Select(c => c.Share), s.Dominant.Select(c => c.Share));
        Assert.InRange(s.Dominant.Sum(c => c.Share), 0.999, 1.001);
        Assert.All(s.Dominant, c => Assert.Matches("^#[0-9a-f]{6}$", c.Hex));
    }

    [Fact]
    public void ABusyCornerIsBusyAndTheRestIsNot()
    {
        // Mid gray everywhere; a black and white checkerboard of 2-pixel squares in the bottom right.
        var s = new Picture(200, 120).Fill((x, y) =>
            x >= 130 && y >= 80 && ((x / 2 + y / 2) % 2 == 0) ? ((byte)255, (byte)255, (byte)255) :
            x >= 130 && y >= 80 ? ((byte)0, (byte)0, (byte)0) : ((byte)128, (byte)128, (byte)128)).Summary();

        Assert.True(s.At(DeskAnchor.BR)!.Busyness > 0.5, $"BR busyness {s.At(DeskAnchor.BR)!.Busyness}");
        Assert.Equal(0, s.At(DeskAnchor.TL)!.Busyness);
        Assert.Equal(0, s.At(DeskAnchor.TC)!.Busyness);
        Assert.Equal(0, s.At(DeskAnchor.ML)!.Busyness);
        // The checkerboard averages to mid brightness; the gray is darker than white and brighter than black.
        Assert.InRange(s.At(DeskAnchor.BR)!.Brightness, 0.3, 0.7);
    }

    [Fact]
    public void BusynessIsBetweenZeroAndOne()
    {
        var rng = new Random(7);
        var s = new Picture(120, 80).Fill((_, _) => ((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256))).Summary();
        Assert.All(s.Anchors, a => Assert.InRange(a.Busyness, 0.5, 1.0));   // noise is edges nearly everywhere
        Assert.All(s.Anchors, a => Assert.InRange(a.Brightness, 0, 1));
    }

    [Fact]
    public void TheSameBufferGivesTheSameSummary()
    {
        var rng = new Random(11);
        var picture = new Picture(200, 100).Fill((x, y) => ((byte)(x + rng.Next(40)), (byte)(y * 2), (byte)(x ^ y)));
        var a = new WallpaperOverview(picture.Summary().Dominant, 0, [new WallpaperMonitorSummary(0, true, 200, 100, picture.Summary())]);
        var b = new WallpaperOverview(picture.Summary().Dominant, 0, [new WallpaperMonitorSummary(0, true, 200, 100, picture.Summary())]);
        Assert.Equal(a.ToJson().ToJsonString(), b.ToJson().ToJsonString());
    }

    [Fact]
    public void AHugeBufferIsSampledNotWalked()
    {
        // 4000 x 2250 is 9 million pixels; the summary must still be quick and sensible.
        var picture = new Picture(4000, 2250).Fill((_, _) => (10, 20, 30));
        var started = DateTime.UtcNow;
        var s = picture.Summary();
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal("#0a141e", s.Dominant[0].Hex);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 10, 5)]      // stride too small
    [InlineData(10, 10, 40)]     // buffer too short (checked below by passing an empty span)
    public void AnUnusableBufferIsNull(int width, int height, int stride)
    {
        Assert.Null(WallpaperSummary.From(new byte[8], width, height, stride, []));
    }

    [Fact]
    public void AnAnchorBoxOffThePictureReadsTheWholePicturesBrightness()
    {
        var picture = new Picture(40, 40).Fill((_, _) => (255, 255, 255));
        var s = WallpaperSummary.From(picture.Bgra, 40, 40, picture.Stride, [new AnchorBox(DeskAnchor.TL, 500, 500, 10, 10)])!;
        Assert.Equal(1, s.At(DeskAnchor.TL)!.Brightness);
        Assert.Equal(0, s.At(DeskAnchor.TL)!.Busyness);
    }

    [Fact]
    public void ABoxPartlyOffThePictureIsClipped()
    {
        var picture = new Picture(40, 40).Fill((x, _) => x < 20 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255));
        var s = WallpaperSummary.From(picture.Bgra, 40, 40, picture.Stride, [new AnchorBox(DeskAnchor.BR, 30, 30, 100, 100)])!;
        Assert.Equal(1, s.At(DeskAnchor.BR)!.Brightness);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(256, 144)]
    [InlineData(64, 64)]
    [InlineData(10, 10)]
    public void DefaultAnchorsAreEightBoxesInsideTheBuffer(int width, int height)
    {
        var boxes = WallpaperSummary.DefaultAnchors(width, height);
        Assert.Equal(8, boxes.Count);
        Assert.Equal(DeskAnchors.All, boxes.Select(b => b.Anchor));
        Assert.All(boxes, b =>
        {
            Assert.True(b.X >= 0 && b.Y >= 0 && b.Width > 0 && b.Height > 0);
            Assert.True(b.X + b.Width <= width && b.Y + b.Height <= height, $"{b}");
        });
        Assert.True(boxes[0].X < boxes[2].X);                       // TL left of TR
        Assert.True(boxes[0].Y < boxes[5].Y);                       // TL above BL
        Assert.Equal((width - boxes[1].Width) / 2, boxes[1].X);     // TC centered
    }

    // ---- across monitors and in JSON ----

    [Fact]
    public void CombineWeightsByPixelsAndMergesNearColors()
    {
        var dark = new Picture(64, 36).Fill((_, _) => (10, 10, 10)).Summary();
        var bright = new Picture(64, 36).Fill((_, _) => (240, 240, 240)).Summary();
        var o = WallpaperOverview.Combine([
            new WallpaperMonitorSummary(0, true, 3000, 2000, dark),
            new WallpaperMonitorSummary(1, false, 1000, 2000, bright)]);

        Assert.Equal(2, o.Monitors.Count);
        Assert.Equal(0.75 * dark.Brightness + 0.25 * bright.Brightness, o.Brightness, 3);
        Assert.Equal(2, o.Dominant.Count);
        Assert.Equal(0.75, o.Dominant[0].Share, 3);
        Assert.Equal("#0a0a0a", o.Dominant[0].Hex);
    }

    [Fact]
    public void CombineMergesTheSameColorSeenOnTwoMonitors()
    {
        var a = new Picture(64, 36).Fill((_, _) => (100, 120, 140)).Summary();
        var b = new Picture(64, 36).Fill((_, _) => (102, 118, 141)).Summary();
        var o = WallpaperOverview.Combine([new WallpaperMonitorSummary(0, true, 100, 100, a), new WallpaperMonitorSummary(1, false, 100, 100, b)]);
        var only = Assert.Single(o.Dominant);
        Assert.Equal(1.0, only.Share, 3);
    }

    [Fact]
    public void CombineOfNothingIsEmpty()
    {
        var o = WallpaperOverview.Combine([]);
        Assert.Empty(o.Monitors);
        Assert.Empty(o.Dominant);
    }

    [Fact]
    public void AnOverviewRoundTripsThroughJson()
    {
        var picture = new Picture(160, 90).Fill((x, y) => ((byte)x, (byte)(y * 2), (byte)(x + y)));
        var o = WallpaperOverview.Combine([new WallpaperMonitorSummary(0, true, 2560, 1440, picture.Summary())]);

        var json = o.ToJson();
        var back = WallpaperOverview.FromJson(json)!;
        Assert.Equal(json.ToJsonString(), back.ToJson().ToJsonString());
        Assert.Equal(8, ((System.Text.Json.Nodes.JsonObject)json["monitors"]![0]!["anchors"]!).Count);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"brightness\":\"bright\"}")]
    [InlineData("{\"brightness\":0.5,\"monitors\":[5]}")]
    public void ASummaryThatIsNotOneReadsAsNull(string json)
    {
        Assert.Null(WallpaperOverview.FromJson(System.Text.Json.Nodes.JsonNode.Parse(json)));
    }

    [Fact]
    public void ForeignNumbersAreClamped()
    {
        var o = WallpaperOverview.FromJson(System.Text.Json.Nodes.JsonNode.Parse(
            """{"brightness":7,"monitors":[{"brightness":-3,"anchors":{"tl":{"brightness":9,"busyness":-1}}}]}"""))!;
        Assert.Equal(1, o.Brightness);
        Assert.Equal(0, o.Monitors[0].Summary.Brightness);
        Assert.Equal(1, o.Monitors[0].Summary.At(DeskAnchor.TL)!.Brightness);
        Assert.Equal(0, o.Monitors[0].Summary.At(DeskAnchor.TL)!.Busyness);
    }

    // ---- drawing a monitor the way Windows lays the wallpaper out ----

    private static readonly PxRect Screen = new(0, 0, 1600, 900);

    private static (byte R, byte G, byte B) PixelAt((byte[] Bgra, int Width, int Height) r, int x, int y)
    {
        int o = (y * r.Width + x) * 4;
        return (r.Bgra[o + 2], r.Bgra[o + 1], r.Bgra[o]);
    }

    [Fact]
    public void FillCoversTheMonitorWithTheImage()
    {
        var wide = new Picture(200, 100).Fill((x, _) => x < 100 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255));
        var r = WallpaperRender.Monitor(wide.Bgra, 200, 100, wide.Stride, 2000, 1000, Screen, WallpaperPosition.Fill, Screen, (9, 9, 9), 160)!.Value;
        Assert.Equal((160, 90), (r.Width, r.Height));
        Assert.Equal(((byte)255, (byte)0, (byte)0), PixelAt(r, 5, 5));
        Assert.Equal(((byte)0, (byte)0, (byte)255), PixelAt(r, 150, 85));
        Assert.DoesNotContain(Enumerable.Range(0, r.Width * r.Height).Select(i => r.Bgra[i * 4 + 1]), g => g == 9);
    }

    [Fact]
    public void FitLeavesTheBackgroundColorAboveAndBelow()
    {
        var square = new Picture(100, 100).Fill((_, _) => (255, 255, 255));
        var r = WallpaperRender.Monitor(square.Bgra, 100, 100, square.Stride, 1000, 1000, Screen, WallpaperPosition.Fit, Screen, (10, 20, 30), 160)!.Value;
        Assert.Equal(((byte)10, (byte)20, (byte)30), PixelAt(r, 2, 40));      // left border
        Assert.Equal(((byte)255, (byte)255, (byte)255), PixelAt(r, 80, 45));  // the picture in the middle
        Assert.Equal(((byte)10, (byte)20, (byte)30), PixelAt(r, 157, 40));    // right border
    }

    [Fact]
    public void CenterDrawsASmallImageAtItsNaturalSizeInTheMiddle()
    {
        var small = new Picture(40, 40).Fill((_, _) => (255, 0, 0));
        var r = WallpaperRender.Monitor(small.Bgra, 40, 40, small.Stride, 400, 400, Screen, WallpaperPosition.Center, Screen, (0, 0, 0), 160)!.Value;
        Assert.Equal(((byte)255, (byte)0, (byte)0), PixelAt(r, 80, 45));
        Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(r, 10, 10));
    }

    [Fact]
    public void StretchUsesEveryPixelOfTheMonitor()
    {
        var p = new Picture(10, 10).Fill((x, y) => (y < 5 ? (byte)255 : (byte)0, 0, 0));
        var r = WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 100, 100, Screen, WallpaperPosition.Stretch, Screen, (0, 0, 9), 80)!.Value;
        Assert.Equal(((byte)255, (byte)0, (byte)0), PixelAt(r, 40, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(r, 40, 42));
    }

    [Fact]
    public void TileRepeatsTheImage()
    {
        var p = new Picture(10, 10).Fill((x, _) => x < 5 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)255, (byte)0));
        // Natural 800 wide on a 1600 monitor: two tiles across; the left half of each tile is red.
        var r = WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 800, 450, Screen, WallpaperPosition.Tile, Screen, (0, 0, 0), 160)!.Value;
        Assert.Equal(((byte)255, (byte)0, (byte)0), PixelAt(r, 5, 5));
        Assert.Equal(((byte)0, (byte)255, (byte)0), PixelAt(r, 45, 5));
        Assert.Equal(((byte)255, (byte)0, (byte)0), PixelAt(r, 85, 5));
    }

    [Fact]
    public void AMonitorNotAtTheOriginIsDrawnFromItsOwnCorner()
    {
        var p = new Picture(10, 10).Fill((_, _) => (1, 2, 3));
        var second = new PxRect(1600, 0, 800, 600);
        var r = WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 100, 100, second, WallpaperPosition.Fill, new PxRect(0, 0, 2400, 900), (0, 0, 0), 80)!.Value;
        Assert.Equal((80, 60), (r.Width, r.Height));
        Assert.Equal(((byte)1, (byte)2, (byte)3), PixelAt(r, 40, 30));
    }

    [Fact]
    public void RenderRefusesWhatItCannotDraw()
    {
        var p = new Picture(10, 10);
        Assert.Null(WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 100, 100, new PxRect(0, 0, 0, 0), WallpaperPosition.Fill, Screen, (0, 0, 0), 80));
        Assert.Null(WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 0, 100, Screen, WallpaperPosition.Fill, Screen, (0, 0, 0), 80));
        Assert.Null(WallpaperRender.Monitor(new byte[4], 10, 10, p.Stride, 100, 100, Screen, WallpaperPosition.Fill, Screen, (0, 0, 0), 80));
        Assert.Null(WallpaperRender.Monitor(p.Bgra, 10, 10, p.Stride, 100, 100, Screen, WallpaperPosition.Fill, Screen, (0, 0, 0), 0));
    }

    [Fact]
    public void ASolidColorDesktopSummarizesAsFlat()
    {
        var (bgra, w, h) = WallpaperRender.Flat((40, 50, 60));
        var s = WallpaperSummary.From(bgra, w, h, w * 4, WallpaperSummary.DefaultAnchors(w, h))!;
        Assert.Equal("#28323c", Assert.Single(s.Dominant).Hex);
        Assert.All(s.Anchors, a => Assert.Equal(s.Brightness, a.Brightness));
    }
}
