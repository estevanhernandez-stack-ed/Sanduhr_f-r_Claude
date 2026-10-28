using System.IO;
using System.Windows.Media.Imaging;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The wallpaper preview file (spec, the agent-designed Desk, B): <c>~\.sanduhr\wallpaper-preview.png</c>, the wallpaper scaled so its
/// longest side is at most <see cref="WallpaperPreview.MaxSide"/> pixels, for Claude Code to look at when its own file-read
/// permission allows it. Written only while the MCP integration is installed; written again when the picture it was made from
/// changes (path, write time or size); removed with the integration and by Reset. The image is decoded from the file Windows names,
/// or from its cached copy when that file is gone, by content (the cache has no extension). Nothing is sent anywhere.
/// </summary>
internal sealed class WallpaperPreviewFile
{
    private string? _key;

    /// <summary>
    /// Makes sure the preview at <paramref name="destination"/> is the picture at <paramref name="source"/>, and returns the
    /// destination; null when it could not be made (the picture would not decode, or the folder is not writable). Cheap when nothing changed.
    /// </summary>
    public string? Ensure(string source, string destination)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists || info.Length == 0) return null;
            string key = WallpaperPreview.Key(source, info.LastWriteTimeUtc.Ticks, info.Length);
            if (key == _key && File.Exists(destination)) return destination;

            var png = Render(source);
            if (png is null) return null;
            string? dir = Path.GetDirectoryName(destination);   // streamer: notname - a file location used for IO, never drawn
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = destination + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                File.WriteAllBytes(temp, png);
                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { /* best effort */ }
            }
            _key = key;
            return destination;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Removes the preview (the integration is off, or Reset). Never throws.</summary>
    public void Delete(string destination)
    {
        _key = null;
        try { if (File.Exists(destination)) File.Delete(destination); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next change tries again */ }
    }

    /// <summary>The picture, scaled and encoded as PNG; null when it will not decode.</summary>
    internal static byte[]? Render(string source)
    {
        try
        {
            int w, h;
            using (var head = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var frame = BitmapDecoder.Create(head, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None).Frames[0];
                w = frame.PixelWidth;
                h = frame.PixelHeight;
            }
            var (fw, fh) = WallpaperPreview.FitSize(w, h);
            if (fw <= 0 || fh <= 0) return null;
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            if (fw < w) image.DecodePixelWidth = fw;   // scaled while decoding, never decoded at full size first
            image.EndInit();
            image.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException
                                      or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
