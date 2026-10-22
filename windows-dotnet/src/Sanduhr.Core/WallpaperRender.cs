namespace Sanduhr.Core;

/// <summary>
/// Draws one monitor's wallpaper the way Windows lays it out (Center, Tile, Stretch, Fit, Fill, Span), into a small BGRA
/// buffer of the monitor's own aspect, so <see cref="WallpaperSummary"/> can read what is behind each Desk anchor without
/// ever looking at the screen. Pure math over the decoded image, like <see cref="WallpaperSampler"/> (whose
/// <see cref="WallpaperSampler.ImageRect"/> it uses for the placement).
/// </summary>
public static class WallpaperRender
{
    /// <summary>
    /// The monitor as drawn, <paramref name="outWidth"/> pixels wide and as tall as the monitor's aspect makes it.
    /// <paramref name="bgra"/> is the decoded image (usually a shrunken copy) whose natural size is
    /// <paramref name="naturalWidth"/> by <paramref name="naturalHeight"/>; <paramref name="background"/> fills what the image
    /// does not cover (Center and Fit borders). Null for an empty monitor, a zero-size image or a buffer too small for its
    /// stated size.
    /// </summary>
    public static (byte[] Bgra, int Width, int Height)? Monitor(
        ReadOnlySpan<byte> bgra, int width, int height, int stride, int naturalWidth, int naturalHeight,
        PxRect monitor, WallpaperPosition pos, PxRect virtualScreen, (byte R, byte G, byte B) background, int outWidth)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || naturalWidth <= 0 || naturalHeight <= 0 || monitor.IsEmpty || outWidth <= 0) return null;
        if (bgra.Length < (long)(height - 1) * stride + (long)width * 4) return null;
        int outHeight = Math.Max(1, (int)Math.Round((double)outWidth * monitor.Height / monitor.Width));
        var img = WallpaperSampler.ImageRect(naturalWidth, naturalHeight, monitor, pos, virtualScreen);
        if (img.IsEmpty) return null;
        bool tile = pos == WallpaperPosition.Tile;

        var output = new byte[outWidth * outHeight * 4];
        for (int j = 0; j < outHeight; j++)
        {
            double sy = monitor.Y + (j + 0.5) * monitor.Height / outHeight;
            for (int i = 0; i < outWidth; i++)
            {
                double sx = monitor.X + (i + 0.5) * monitor.Width / outWidth;
                int o = (j * outWidth + i) * 4;
                double u, v;
                if (tile)
                {
                    u = Mod(sx - img.X, img.Width) / img.Width;
                    v = Mod(sy - img.Y, img.Height) / img.Height;
                }
                else if (sx < img.X || sx >= img.Right || sy < img.Y || sy >= img.Bottom)
                {
                    output[o] = background.B; output[o + 1] = background.G; output[o + 2] = background.R; output[o + 3] = 255;
                    continue;
                }
                else
                {
                    u = (sx - img.X) / img.Width;
                    v = (sy - img.Y) / img.Height;
                }
                int px = Math.Clamp((int)(u * width), 0, width - 1);
                int py = Math.Clamp((int)(v * height), 0, height - 1);
                int s = py * stride + px * 4;
                output[o] = bgra[s]; output[o + 1] = bgra[s + 1]; output[o + 2] = bgra[s + 2]; output[o + 3] = 255;
            }
        }
        return (output, outWidth, outHeight);
    }

    /// <summary>A plain-color desktop (no image): a one-pixel picture of the color, which summarizes as flat.</summary>
    public static (byte[] Bgra, int Width, int Height) Flat((byte R, byte G, byte B) color) =>
        ([color.B, color.G, color.R, 255], 1, 1);

    private static double Mod(double a, double m)
    {
        double r = a % m;
        return r < 0 ? r + m : r;
    }
}
