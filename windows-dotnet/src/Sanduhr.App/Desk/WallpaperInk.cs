using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Automatic ink's reading of the wallpaper (spec 6.7): how bright the wallpaper is behind a Desk
/// island. It reads the wallpaper IMAGE FILE Windows reports for each monitor (through
/// <c>IDesktopWallpaper</c>) and its placement, never the screen, so nothing of the user's windows is
/// ever read. Each file is decoded once at about 256 px wide and kept by path and write time;
/// <see cref="Invalidate"/> (a wallpaper or monitor change) drops what was read. Anything unknown
/// answers null, which the ink reads as Light.
/// </summary>
internal sealed class WallpaperInk
{
    private const int DecodeWidth = 256;

    private sealed record Decoded(byte[] Pixels, int Width, int Height, int Stride, int NaturalWidth, int NaturalHeight);

    private readonly Dictionary<(string Path, DateTime Written), Decoded?> _images = [];
    private (IReadOnlyList<DeskSensorsNative.WallpaperMonitor> Monitors, int Position, uint Background)? _info;
    private bool _read;

    /// <summary>The wallpaper or the monitors changed: read everything again on the next ask.</summary>
    public void Invalidate()
    {
        _images.Clear();
        _info = null;
        _read = false;
        _summaryKey = null;
    }

    /// <summary>Reads the shell's wallpaper facts (monitors, image paths, position, color) again on the next ask, keeping
    /// the decoded images: <see cref="Overview"/> re-reads an image only when its path or write time changed.</summary>
    public void Refresh()
    {
        _info = null;
        _read = false;
    }

    private const int SummaryWidth = 256;
    private string? _summaryKey;
    private WallpaperOverview? _summary;
    private string? _summaryPath;
    private string? _summarySource;

    /// <summary>Where the primary monitor's image came from in the last <see cref="Overview"/>: <see cref="WallpaperFallback.SourceFile"/>
    /// (the file Windows names), <see cref="WallpaperFallback.SourceCache"/> (Windows' cached copy, because that file is gone) or null.</summary>
    public string? PathSource => _summarySource;

    /// <summary>
    /// What the wallpaper looks like on each monitor, for <c>get_desk</c> (spec, the agent-designed Desk, B), and the
    /// primary monitor's image path. Each monitor is drawn from the same decoded image the Automatic ink reads, the way
    /// Windows lays it out (fit mode, background color), then summarized by Core's pure math. Kept by wallpaper path and
    /// last-write time, so only a wallpaper, monitor or position change reads again. A monitor whose image cannot be
    /// decoded is left out; none left answers null. Nothing leaves this process.
    /// </summary>
    public (WallpaperOverview? Overview, string? Path) Overview()
    {
        try
        {
            if (!_read)
            {
                _info = DeskSensorsNative.Wallpaper();
                _read = true;
            }
            if (_info is not { } info || info.Monitors.Count == 0) return (null, null);

            string key = string.Join(";", info.Monitors.Select(m =>
                $"{m.Left},{m.Top},{m.Right},{m.Bottom},{m.Path}|{(m.Path is not null && File.Exists(m.Path) ? File.GetLastWriteTimeUtc(m.Path).Ticks : 0)}"))
                + $"|{info.Position}|{info.Background}";
            if (key == _summaryKey) return (_summary, _summaryPath);
            _images.Clear();   // a different wallpaper: forget the old pictures (a slideshow would otherwise keep them all)

            var virt = VirtualScreen(info.Monitors);
            var bg = ((byte)(info.Background & 0xFF), (byte)((info.Background >> 8) & 0xFF), (byte)((info.Background >> 16) & 0xFF));
            var list = new List<WallpaperMonitorSummary>();
            foreach (var m in info.Monitors)
            {
                var monitor = new PxRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
                if (monitor.IsEmpty) continue;
                (byte[] Bgra, int Width, int Height)? drawn;
                if (m.Path is null)
                {
                    drawn = WallpaperRender.Flat(bg);   // a plain color desktop
                }
                else
                {
                    var image = Decode(m.Path);
                    if (image is null) continue;
                    drawn = WallpaperRender.Monitor(image.Pixels, image.Width, image.Height, image.Stride,
                        image.NaturalWidth, image.NaturalHeight, monitor, (WallpaperPosition)info.Position, virt, bg, SummaryWidth);
                }
                if (drawn is not { } d) continue;
                var summary = WallpaperSummary.From(d.Bgra, d.Width, d.Height, d.Width * 4, WallpaperSummary.DefaultAnchors(d.Width, d.Height));
                if (summary is null) continue;
                list.Add(new WallpaperMonitorSummary(list.Count, m.Left == 0 && m.Top == 0, monitor.Width, monitor.Height, summary));
            }
            _summary = list.Count == 0 ? null : WallpaperOverview.Combine(list);
            var primary = info.Monitors.FirstOrDefault(m => m.Left == 0 && m.Top == 0 && m.Path is not null)
                          ?? info.Monitors.FirstOrDefault(m => m.Path is not null);
            _summaryPath = primary?.Path;
            _summarySource = primary?.Source;
            _summaryKey = key;
            return (_summary, _summaryPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return (null, null);
        }
    }

    /// <summary>The wallpaper's average relative luminance behind <paramref name="region"/> (physical
    /// pixels) on <paramref name="screen"/>, or null when it can't be known.</summary>
    public double? Luminance(DeskScreen screen, PxRect region)
    {
        try
        {
            if (!_read)
            {
                _info = DeskSensorsNative.Wallpaper();
                _read = true;
            }
            if (_info is not { } info) return null;
            var mon = info.Monitors.FirstOrDefault(m =>
                m.Left == screen.Bounds.X && m.Top == screen.Bounds.Y &&
                m.Right - m.Left == screen.Bounds.Width && m.Bottom - m.Top == screen.Bounds.Height);
            if (mon is null) return null;
            var monitor = new PxRect(mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top);
            double background = ColorLuminance(info.Background);
            if (mon.Path is null) return background; // a plain color desktop
            var image = Decode(mon.Path);
            if (image is null) return null;
            var virt = VirtualScreen(info.Monitors);
            return WallpaperSampler.Luminance(image.Pixels, image.Width, image.Height, image.Stride,
                image.NaturalWidth, image.NaturalHeight, monitor, region, (WallpaperPosition)info.Position, virt, background);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static PxRect VirtualScreen(IReadOnlyList<DeskSensorsNative.WallpaperMonitor> monitors)
    {
        int l = monitors.Min(m => m.Left), t = monitors.Min(m => m.Top);
        int r = monitors.Max(m => m.Right), b = monitors.Max(m => m.Bottom);
        return new PxRect(l, t, r - l, b - t);
    }

    /// <summary>COLORREF 0x00BBGGRR as relative luminance.</summary>
    private static double ColorLuminance(uint colorref) =>
        InkHex.Luminance($"{colorref & 0xFF:x2}{(colorref >> 8) & 0xFF:x2}{(colorref >> 16) & 0xFF:x2}");

    private Decoded? Decode(string path)
    {
        if (!File.Exists(path)) return null;
        var key = (path, File.GetLastWriteTimeUtc(path));
        if (_images.TryGetValue(key, out var hit)) return hit;
        Decoded? d = null;
        try
        {
            int naturalW, naturalH;
            using (var head = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // The header alone, for the natural size (Center mode draws at it).
                var frame = BitmapDecoder.Create(head, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None).Frames[0];
                naturalW = frame.PixelWidth;
                naturalH = frame.PixelHeight;
            }
            if (naturalW > 0 && naturalH > 0)
            {
                // Decoded straight to about 256 px wide (JPEG scales while decoding), never at full size.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var small = new BitmapImage();
                small.BeginInit();
                small.CacheOption = BitmapCacheOption.OnLoad;
                small.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                small.StreamSource = stream;
                if (naturalW > DecodeWidth) small.DecodePixelWidth = DecodeWidth;
                small.EndInit();
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
                var pixels = new byte[stride * h];
                bgra.CopyPixels(pixels, stride, 0);
                d = new Decoded(pixels, w, h, stride, naturalW, naturalH);
            }
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException)
        {
            d = null;
        }
        _images[key] = d;
        return d;
    }
}
