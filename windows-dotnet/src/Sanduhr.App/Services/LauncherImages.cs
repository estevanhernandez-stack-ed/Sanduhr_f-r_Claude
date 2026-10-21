using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>What an image import did: the stored file's name, or a short reason it was refused.</summary>
internal readonly record struct LauncherImageImport(string? FileName, string? Error)
{
    public bool Ok => FileName is not null;
}

/// <summary>
/// The Launcher's button images (spec, Desk Launcher). A PNG the person picks is checked
/// (<see cref="LauncherImagePolicy"/>: a real PNG, at most 1 MB, no huge bitmap), decoded and drawn into a 256 px
/// square, re-encoded, and stored in <see cref="Paths.LauncherDir"/> under a hash of the result, so the stored
/// file is always an image Sanduhr itself wrote, never the picked file. No image is ever fetched from the
/// network. A file leaves with its button (<see cref="Delete"/>) and with Reset's Settings part.
/// </summary>
internal static class LauncherImages
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.Ordinal);

    /// <summary>Imports <paramref name="sourcePath"/>; never throws.</summary>
    public static LauncherImageImport Import(Paths paths, string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists) return new(null, "That file is gone.");
            var head = new byte[8];
            int read;
            using (var peek = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                read = peek.Read(head, 0, head.Length);
            if (LauncherImagePolicy.RefuseFile(info.Length, head.AsSpan(0, read)) is { } refusal) return new(null, refusal);

            byte[] encoded = Encode(sourcePath, out var sizeRefusal);
            if (sizeRefusal is not null) return new(null, sizeRefusal);

            var name = LauncherImagePolicy.FileName(encoded);
            var dir = paths.LauncherDir;
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, name);
            if (!File.Exists(target))
            {
                var temp = Path.Combine(dir, name + ".tmp");
                File.WriteAllBytes(temp, encoded);
                File.Move(temp, target, overwrite: true);
            }
            return new(name, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or InvalidOperationException or FileFormatException or ArgumentException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            return new(null, "Sanduhr couldn't read that image.");
        }
    }

    /// <summary>Decodes the PNG and draws it, scaled to fit, centered on a transparent 256 px square; returns the new PNG's bytes.</summary>
    private static byte[] Encode(string path, out string? refusal)
    {
        refusal = null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // No cache option: the header is read for the size, and the pixels only after the size is accepted.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder || decoder.Frames.Count == 0)
        {
            refusal = "Choose a PNG image.";
            return [];
        }
        var frame = decoder.Frames[0];
        if (LauncherImagePolicy.RefuseSize(frame.PixelWidth, frame.PixelHeight) is { } tooBig)
        {
            refusal = tooBig;
            return [];
        }

        var (x, y, w, h) = LauncherImagePolicy.Fit(frame.PixelWidth, frame.PixelHeight);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(frame, new Rect(x, y, w, h));
        var target = new RenderTargetBitmap(LauncherImagePolicy.Side, LauncherImagePolicy.Side, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>The stored image as a frozen bitmap; null when the name is not a stored name or the file is gone or unreadable.</summary>
    public static ImageSource? Load(Paths paths, string? name)
    {
        if (!LauncherButton.IsImageName(name)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(name!, out var cached) && cached is not null) return cached;
        }
        try
        {
            var file = Path.Combine(paths.LauncherDir, name!);
            if (!File.Exists(file)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(file, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            lock (Cache) Cache[name!] = bmp;
            return bmp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Deletes stored images by name (only names that pass the stored-name rule are touched); never throws.</summary>
    public static void Delete(Paths paths, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!LauncherButton.IsImageName(name)) continue;
            lock (Cache) Cache.Remove(name);
            try
            {
                var file = Path.Combine(paths.LauncherDir, name);
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left behind: Reset or the next removal takes it.
            }
        }
    }
}
