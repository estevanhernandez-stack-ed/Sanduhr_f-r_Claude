using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The wallpaper math: where the image lands for each position and the brightness behind a region.</summary>
public class WallpaperSamplerTests
{
    private const int W = 8, H = 4;

    private static readonly PxRect Mon = new(0, 0, 800, 400);
    private static readonly PxRect Square = new(0, 0, 800, 800);

    /// <summary>An 8x4 BGRA bitmap, black on the left half and white on the right half.</summary>
    private static byte[] HalfAndHalf()
    {
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte v = x < W / 2 ? (byte)0 : (byte)255;
                int o = (y * W + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = v;
                px[o + 3] = 255;
            }
        return px;
    }

    private static double? Lum(PxRect monitor, PxRect region, WallpaperPosition pos, PxRect? virt = null,
        double bg = 0.5, int? ow = null, int? oh = null) =>
        WallpaperSampler.Luminance(HalfAndHalf(), W, H, W * 4, ow ?? W, oh ?? H, monitor, region, pos, virt ?? monitor, bg);

    // ---- ImageRect ----

    [Fact]
    public void CenterIsNaturalSizeCenteredOnTheMonitor()
    {
        Assert.Equal(new PxRect(460, 190, 1000, 20), WallpaperSampler.ImageRect(1000, 20, Mon with { Width = 1920, Height = 400 }, WallpaperPosition.Center, Mon));
        Assert.Equal(new PxRect(-100, -50, 1000, 500), WallpaperSampler.ImageRect(1000, 500, Mon, WallpaperPosition.Center, Mon));
    }

    [Fact]
    public void StretchFillsTheMonitor()
    {
        Assert.Equal(new PxRect(1920, 0, 2560, 1440),
            WallpaperSampler.ImageRect(100, 100, new PxRect(1920, 0, 2560, 1440), WallpaperPosition.Stretch, new PxRect(0, 0, 4480, 1440)));
    }

    [Fact]
    public void FitLetterboxesInsideTheMonitor()
    {
        // 800x400 image on a square: scale 1, centered vertically.
        Assert.Equal(new PxRect(0, 200, 800, 400), WallpaperSampler.ImageRect(800, 400, Square, WallpaperPosition.Fit, Square));
        // A tall image on a wide monitor: pillarboxed.
        Assert.Equal(new PxRect(300, 0, 200, 400), WallpaperSampler.ImageRect(100, 200, Mon, WallpaperPosition.Fit, Mon));
    }

    [Fact]
    public void FillCoversAndCropsCentered()
    {
        Assert.Equal(new PxRect(-400, 0, 1600, 800), WallpaperSampler.ImageRect(800, 400, Square, WallpaperPosition.Fill, Square));
        Assert.Equal(new PxRect(0, -200, 800, 800), WallpaperSampler.ImageRect(100, 100, Mon, WallpaperPosition.Fill, Mon));
    }

    [Fact]
    public void SpanIsFillAcrossTheVirtualScreenWhicheverMonitorAsks()
    {
        var a = new PxRect(0, 0, 800, 400);
        var b = new PxRect(800, 0, 800, 400);
        var virt = new PxRect(0, 0, 1600, 400);
        var expected = new PxRect(0, -200, 1600, 800);
        Assert.Equal(expected, WallpaperSampler.ImageRect(8, 4, a, WallpaperPosition.Span, virt));
        Assert.Equal(expected, WallpaperSampler.ImageRect(8, 4, b, WallpaperPosition.Span, virt));
    }

    [Fact]
    public void TileReturnsTheFirstTileAtTheMonitorOrigin()
    {
        var m = new PxRect(1920, 100, 800, 400);
        Assert.Equal(new PxRect(1920, 100, 200, 100), WallpaperSampler.ImageRect(200, 100, m, WallpaperPosition.Tile, m));
    }

    [Fact]
    public void ImageRectIsEmptyForNothingToDraw()
    {
        Assert.True(WallpaperSampler.ImageRect(0, 10, Mon, WallpaperPosition.Fill, Mon).IsEmpty);
        Assert.True(WallpaperSampler.ImageRect(10, 10, new PxRect(0, 0, 0, 0), WallpaperPosition.Fill, Mon).IsEmpty);
    }

    [Fact]
    public void PositionValuesMatchIDesktopWallpaper()
    {
        Assert.Equal(0, (int)WallpaperPosition.Center);
        Assert.Equal(1, (int)WallpaperPosition.Tile);
        Assert.Equal(2, (int)WallpaperPosition.Stretch);
        Assert.Equal(3, (int)WallpaperPosition.Fit);
        Assert.Equal(4, (int)WallpaperPosition.Fill);
        Assert.Equal(5, (int)WallpaperPosition.Span);
    }

    // ---- Luminance: each mode, regions on each half ----

    [Fact]
    public void StretchSeesEachHalf()
    {
        Assert.Equal(0.0, Lum(Mon, new PxRect(0, 0, 100, 100), WallpaperPosition.Stretch)!.Value, 4);
        Assert.Equal(1.0, Lum(Mon, new PxRect(700, 0, 100, 100), WallpaperPosition.Stretch)!.Value, 4);
        Assert.Equal(0.5, Lum(Mon, new PxRect(0, 0, 800, 400), WallpaperPosition.Stretch)!.Value, 2);
    }

    [Fact]
    public void FillCropsSoTheSplitStaysAtTheMonitorCenter()
    {
        Assert.Equal(0.0, Lum(Square, new PxRect(0, 0, 200, 100), WallpaperPosition.Fill)!.Value, 4);
        Assert.Equal(1.0, Lum(Square, new PxRect(600, 0, 200, 100), WallpaperPosition.Fill)!.Value, 4);
    }

    [Fact]
    public void FitBordersAreTheBackground()
    {
        // Image occupies y 200..600; y 0..100 is the letterbox.
        Assert.Equal(0.3, Lum(Square, new PxRect(0, 0, 100, 100), WallpaperPosition.Fit, bg: 0.3)!.Value, 4);
        Assert.Equal(0.0, Lum(Square, new PxRect(0, 250, 100, 100), WallpaperPosition.Fit, bg: 0.3)!.Value, 4);
        Assert.Equal(1.0, Lum(Square, new PxRect(700, 250, 100, 100), WallpaperPosition.Fit, bg: 0.3)!.Value, 4);
    }

    [Fact]
    public void FitRegionStraddlingTheBorderMixesByArea()
    {
        // y 100..300: the top half is background (0.3), the bottom half the black side of the image.
        var l = Lum(Square, new PxRect(0, 100, 100, 200), WallpaperPosition.Fit, bg: 0.3)!.Value;
        Assert.Equal(0.15, l, 3);
        // Same strip on the white side: 0.5 * 0.3 + 0.5 * 1.
        var w = Lum(Square, new PxRect(700, 100, 100, 200), WallpaperPosition.Fit, bg: 0.3)!.Value;
        Assert.Equal(0.65, w, 3);
    }

    [Fact]
    public void CenterUsesTheOriginalSizeForLayoutAndBackgroundAround()
    {
        // The bitmap is a shrunken copy of a 400x200 image: it sits at (200,100) with the split at x=400.
        Assert.Equal(new PxRect(200, 100, 400, 200), WallpaperSampler.ImageRect(400, 200, Mon, WallpaperPosition.Center, Mon));
        Assert.Equal(0.7, Lum(Mon, new PxRect(0, 0, 200, 100), WallpaperPosition.Center, bg: 0.7, ow: 400, oh: 200)!.Value, 4);
        Assert.Equal(0.0, Lum(Mon, new PxRect(200, 100, 200, 200), WallpaperPosition.Center, bg: 0.7, ow: 400, oh: 200)!.Value, 4);
        Assert.Equal(1.0, Lum(Mon, new PxRect(400, 100, 200, 200), WallpaperPosition.Center, bg: 0.7, ow: 400, oh: 200)!.Value, 4);
        Assert.Equal(0.7, Lum(Mon, new PxRect(600, 0, 200, 400), WallpaperPosition.Center, bg: 0.7, ow: 400, oh: 200)!.Value, 4);
    }

    [Fact]
    public void ADownscaledBufferIsLaidOutByTheNaturalSize()
    {
        // A 2000x1000 image decoded to a 256x128 buffer (black left half, white right half), Center on 1920x1080:
        // the image sits at (-40, 40) with the split at screen x = 960.
        const int bw = 256, bh = 128;
        var buf = new byte[bw * bh * 4];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                byte v = x < bw / 2 ? (byte)0 : (byte)255;
                int o = (y * bw + x) * 4;
                buf[o] = buf[o + 1] = buf[o + 2] = v;
                buf[o + 3] = 255;
            }
        var mon = new PxRect(0, 0, 1920, 1080);
        Assert.Equal(new PxRect(-40, 40, 2000, 1000), WallpaperSampler.ImageRect(2000, 1000, mon, WallpaperPosition.Center, mon));

        double? At(PxRect region) => WallpaperSampler.Luminance(
            buf, bw, bh, bw * 4, 2000, 1000, mon, region, WallpaperPosition.Center, mon, 0.7);

        Assert.Equal(0.0, At(new PxRect(0, 200, 100, 100))!.Value, 4);
        Assert.Equal(1.0, At(new PxRect(1000, 200, 100, 100))!.Value, 4);
        Assert.Equal(0.5, At(new PxRect(900, 200, 120, 100))!.Value, 2);
        // Rows 0..40 are above the image (background), 40..80 the black side: half and half.
        Assert.Equal(0.35, At(new PxRect(0, 0, 100, 80))!.Value, 2);
        // Treating the buffer as the natural size would put the image at (832, 476) and miss all of this.
        Assert.Equal(0.7, WallpaperSampler.Luminance(buf, bw, bh, bw * 4, bw, bh, mon, new PxRect(0, 200, 100, 100),
            WallpaperPosition.Center, mon, 0.7)!.Value, 4);
    }

    [Fact]
    public void TileRepeatsFromTheMonitorTopLeft()
    {
        // 200x100 tiles on a monitor that does not start at the origin: black 0..100, white 100..200, repeat.
        var m = new PxRect(1920, 0, 800, 400);
        Assert.Equal(0.0, Lum(m, new PxRect(1920, 0, 100, 100), WallpaperPosition.Tile, ow: 200, oh: 100)!.Value, 4);
        Assert.Equal(1.0, Lum(m, new PxRect(2020, 0, 100, 100), WallpaperPosition.Tile, ow: 200, oh: 100)!.Value, 4);
        Assert.Equal(0.0, Lum(m, new PxRect(2120, 0, 100, 100), WallpaperPosition.Tile, ow: 200, oh: 100)!.Value, 4);
        Assert.Equal(1.0, Lum(m, new PxRect(2420, 300, 100, 100), WallpaperPosition.Tile, ow: 200, oh: 100)!.Value, 4);
        // Two full tiles average to half.
        Assert.Equal(0.5, Lum(m, new PxRect(1920, 0, 400, 100), WallpaperPosition.Tile, ow: 200, oh: 100)!.Value, 2);
    }

    [Fact]
    public void SpanAcrossTwoMonitorsLandsEachOnItsOwnHalf()
    {
        var a = new PxRect(0, 0, 800, 400);
        var b = new PxRect(800, 0, 800, 400);
        var virt = new PxRect(0, 0, 1600, 400);
        Assert.Equal(0.0, Lum(a, new PxRect(0, 0, 800, 400), WallpaperPosition.Span, virt)!.Value, 4);
        Assert.Equal(1.0, Lum(b, new PxRect(800, 0, 800, 400), WallpaperPosition.Span, virt)!.Value, 4);
        Assert.Equal(0.0, Lum(a, new PxRect(600, 100, 100, 100), WallpaperPosition.Span, virt)!.Value, 4);
        Assert.Equal(1.0, Lum(b, new PxRect(900, 100, 100, 100), WallpaperPosition.Span, virt)!.Value, 4);
    }

    [Fact]
    public void SpanWithMonitorLeftOfTheOriginUsesVirtualScreenCoordinates()
    {
        // Left monitor at negative x: the split of the spanned image is at the virtual center (x = 0).
        var left = new PxRect(-800, 0, 800, 400);
        var right = new PxRect(0, 0, 800, 400);
        var virt = new PxRect(-800, 0, 1600, 400);
        Assert.Equal(0.0, Lum(left, new PxRect(-300, 0, 200, 100), WallpaperPosition.Span, virt)!.Value, 4);
        Assert.Equal(1.0, Lum(right, new PxRect(100, 0, 200, 100), WallpaperPosition.Span, virt)!.Value, 4);
    }

    // ---- Edges ----

    [Fact]
    public void RegionPartlyOffTheMonitorIsClippedToIt()
    {
        // x -100..100 clipped to 0..100: all on the black half.
        Assert.Equal(0.0, Lum(Mon, new PxRect(-100, 0, 200, 100), WallpaperPosition.Stretch)!.Value, 4);
        // x 750..950 clipped to 750..800: all white.
        Assert.Equal(1.0, Lum(Mon, new PxRect(750, 0, 200, 100), WallpaperPosition.Stretch)!.Value, 4);
    }

    [Fact]
    public void RegionWhollyOffTheMonitorIsUnknown()
    {
        Assert.Null(Lum(Mon, new PxRect(900, 0, 100, 100), WallpaperPosition.Stretch));
        Assert.Null(Lum(Mon, new PxRect(-200, 0, 100, 100), WallpaperPosition.Stretch));
    }

    [Fact]
    public void EmptyRegionOrZeroSizeImageIsUnknown()
    {
        Assert.Null(Lum(Mon, new PxRect(10, 10, 0, 50), WallpaperPosition.Fill));
        Assert.Null(WallpaperSampler.Luminance(new byte[16], 0, 0, 0, 1, 1, Mon, new PxRect(0, 0, 10, 10), WallpaperPosition.Fill, Mon, 0.5));
        Assert.Null(WallpaperSampler.Luminance(new byte[8], 2, 2, 8, 2, 2, Mon, new PxRect(0, 0, 10, 10), WallpaperPosition.Fill, Mon, 0.5));
    }

    [Fact]
    public void StrideWiderThanTheRowIsHonored()
    {
        // 2x1 image, 16-byte rows: black then white, with padding.
        var px = new byte[16];
        px[4] = px[5] = px[6] = 255;
        var l = WallpaperSampler.Luminance(px, 2, 1, 16, 2, 1, Mon, new PxRect(0, 0, 800, 400), WallpaperPosition.Stretch, Mon, 0.5);
        Assert.Equal(0.5, l!.Value, 2);
    }

    [Fact]
    public void ALargeRegionIsSampledOnABoundedGrid()
    {
        var big = new PxRect(0, 0, 3840, 2160);
        var l = WallpaperSampler.Luminance(HalfAndHalf(), W, H, W * 4, W, H, big, big, WallpaperPosition.Stretch, big, 0.5);
        Assert.Equal(0.5, l!.Value, 2);
        Assert.True(WallpaperSampler.MaxSamplePoints <= 4096);
    }

    [Fact]
    public void SolidMidGrayReadsAsItsWcagLuminance()
    {
        var px = new byte[4 * 4];
        for (int i = 0; i < 4; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = 0x80; px[i * 4 + 3] = 255; }
        var l = WallpaperSampler.Luminance(px, 2, 2, 8, 2, 2, Mon, new PxRect(0, 0, 50, 50), WallpaperPosition.Stretch, Mon, 0.5);
        Assert.Equal(0.2159, l!.Value, 3);
    }
}
