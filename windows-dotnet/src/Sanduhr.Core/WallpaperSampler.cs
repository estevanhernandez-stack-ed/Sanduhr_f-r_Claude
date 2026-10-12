namespace Sanduhr.Core;

/// <summary>The wallpaper position, with <c>IDesktopWallpaper</c>'s DESKTOP_WALLPAPER_POSITION values.</summary>
public enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>
/// The pure math behind the Automatic Desk ink (spec 6.7): where the wallpaper image lands on a monitor
/// for each position, and how bright the wallpaper is behind a screen rectangle. The App does the COM
/// calls and the image decoding; this only maps screen pixels back to image pixels, so it never needs a
/// screen capture. The decoded bitmap is usually a shrunken copy (256 px wide), so the sampler takes the
/// original image size separately for the modes whose layout depends on it (Center, Tile).
/// </summary>
public static class WallpaperSampler
{
    /// <summary>The most grid points one <see cref="Luminance"/> call reads, so cost does not grow with the island.</summary>
    public const int MaxSamplePoints = 4096;

    private readonly record struct DRect(double X, double Y, double W, double H)
    {
        public double Right => X + W;
        public double Bottom => Y + H;
    }

    /// <summary>
    /// Where the whole image is drawn on screen, in screen pixels, as Windows draws it. Center: natural size,
    /// centered on the monitor. Stretch: the monitor. Fit: scaled to fit inside, letterboxed, centered. Fill:
    /// scaled to cover, cropped, centered. Span: Fill across <paramref name="virtualScreen"/>. Tile: the first
    /// tile, at the monitor's top-left (the image repeats from there; <see cref="Luminance"/> handles the
    /// repetition). An image of zero size or an empty monitor gives an empty rect.
    /// </summary>
    public static PxRect ImageRect(int imageW, int imageH, PxRect monitor, WallpaperPosition pos, PxRect virtualScreen)
    {
        var r = Place(imageW, imageH, monitor, pos, virtualScreen);
        return new PxRect(Round(r.X), Round(r.Y), Round(r.W), Round(r.H));
    }

    /// <summary>
    /// The average relative luminance (0..1) of what is drawn behind <paramref name="region"/> (screen pixels,
    /// clipped to <paramref name="monitor"/>): image pixels mapped back through the position (nearest pixel,
    /// at most <see cref="MaxSamplePoints"/> points on a grid), and <paramref name="backgroundLuminance"/> for the
    /// parts the image does not cover (Center and Fit borders), weighted by area. <paramref name="bgra"/> is the
    /// decoded buffer, <paramref name="width"/> by <paramref name="height"/> pixels with <paramref name="stride"/>
    /// bytes per row; it is usually a shrunken copy (about 256 px wide) of the image, whose natural pixel size is
    /// <paramref name="naturalWidth"/> by <paramref name="naturalHeight"/>. Layout (Center's size, Tile's period,
    /// every mode's aspect) uses the natural size, then positions scale into the buffer. Null for an empty region
    /// (or one wholly off the monitor), a zero-size image or a buffer too small for its stated size.
    /// </summary>
    public static double? Luminance(
        ReadOnlySpan<byte> bgra, int width, int height, int stride, int naturalWidth, int naturalHeight,
        PxRect monitor, PxRect region, WallpaperPosition pos, PxRect virtualScreen, double backgroundLuminance)
    {
        if (width <= 0 || height <= 0 || stride < width * 4) return null;
        if (bgra.Length < (long)(height - 1) * stride + (long)width * 4) return null;
        if (naturalWidth <= 0 || naturalHeight <= 0) return null;
        int ow = naturalWidth;
        int oh = naturalHeight;

        int cx0 = Math.Max(region.X, monitor.X), cy0 = Math.Max(region.Y, monitor.Y);
        int cx1 = Math.Min(region.Right, monitor.Right), cy1 = Math.Min(region.Bottom, monitor.Bottom);
        if (region.IsEmpty || cx1 <= cx0 || cy1 <= cy0) return null;

        var img = Place(ow, oh, monitor, pos, virtualScreen);
        if (img.W <= 0 || img.H <= 0) return null;
        bool tile = pos == WallpaperPosition.Tile;

        int rw = cx1 - cx0, rh = cy1 - cy0;
        int side = (int)Math.Sqrt(MaxSamplePoints);
        int nx = Math.Min(rw, side), ny = Math.Min(rh, side);

        double sum = 0;
        for (int j = 0; j < ny; j++)
        {
            double sy = cy0 + (j + 0.5) * rh / ny;
            for (int i = 0; i < nx; i++)
            {
                double sx = cx0 + (i + 0.5) * rw / nx;
                double u, v;
                if (tile)
                {
                    u = Mod(sx - img.X, img.W) / img.W;
                    v = Mod(sy - img.Y, img.H) / img.H;
                }
                else
                {
                    if (sx < img.X || sx >= img.Right || sy < img.Y || sy >= img.Bottom)
                    {
                        sum += backgroundLuminance;
                        continue;
                    }
                    u = (sx - img.X) / img.W;
                    v = (sy - img.Y) / img.H;
                }
                int px = Math.Clamp((int)(u * width), 0, width - 1);
                int py = Math.Clamp((int)(v * height), 0, height - 1);
                int o = py * stride + px * 4;
                sum += InkHex.Luminance(bgra[o + 2], bgra[o + 1], bgra[o]);
            }
        }
        return sum / (nx * ny);
    }

    private static DRect Place(int imageW, int imageH, PxRect monitor, WallpaperPosition pos, PxRect virtualScreen)
    {
        if (imageW <= 0 || imageH <= 0 || monitor.IsEmpty) return new DRect(monitor.X, monitor.Y, 0, 0);
        double iw = imageW, ih = imageH;
        switch (pos)
        {
            case WallpaperPosition.Tile:
                return new DRect(monitor.X, monitor.Y, iw, ih);
            case WallpaperPosition.Stretch:
                return new DRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
            case WallpaperPosition.Fit:
                return Scaled(iw, ih, monitor, Math.Min(monitor.Width / iw, monitor.Height / ih));
            case WallpaperPosition.Fill:
                return Scaled(iw, ih, monitor, Math.Max(monitor.Width / iw, monitor.Height / ih));
            case WallpaperPosition.Span:
                var span = virtualScreen.IsEmpty ? monitor : virtualScreen;
                return Scaled(iw, ih, span, Math.Max(span.Width / iw, span.Height / ih));
            default: // Center
                return Scaled(iw, ih, monitor, 1.0);
        }
    }

    private static DRect Scaled(double iw, double ih, PxRect on, double scale)
    {
        double w = iw * scale, h = ih * scale;
        return new DRect(on.X + (on.Width - w) / 2, on.Y + (on.Height - h) / 2, w, h);
    }

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static double Mod(double a, double m)
    {
        double r = a % m;
        return r < 0 ? r + m : r;
    }
}
