using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The wallpaper preview file for Claude Code (spec, the agent-designed Desk, B): downscaled to 1600 px on its longest side,
/// decoded by content (the cache has no extension), written again only when the picture changes, and removed with the integration.</summary>
public sealed class WallpaperPreviewFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wall-" + Guid.NewGuid().ToString("N")[..8]);

    public WallpaperPreviewFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A solid PNG (BGRA pixels), written at <paramref name="path"/> under any name.</summary>
    private static void WritePng(string path, int width, int height, byte shade = 90)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = shade; pixels[i + 1] = 40; pixels[i + 2] = 160; pixels[i + 3] = 255; }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static (int W, int H) SizeOf(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    [Fact]
    public void ALargeWallpaperIsScaledSoItsLongestSideIs1600()
    {
        string source = Path.Combine(_root, "wide.png");
        WritePng(source, 3200, 1800);
        string dest = Path.Combine(_root, ".sanduhr", WallpaperPreview.FileName);
        Sta.Run(() =>
        {
            Assert.Equal(dest, new WallpaperPreviewFile().Ensure(source, dest));
            Assert.Equal((1600, 900), SizeOf(dest));
        });
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(dest)!, "*.tmp"));
    }

    [Fact]
    public void APortraitWallpaperAndASmallOneKeepTheirShape()
    {
        string tall = Path.Combine(_root, "tall.png");
        string small = Path.Combine(_root, "small.png");
        WritePng(tall, 1000, 4000);
        WritePng(small, 640, 480);
        Sta.Run(() =>
        {
            var file = new WallpaperPreviewFile();
            Assert.NotNull(file.Ensure(tall, Path.Combine(_root, "tall-preview.png")));
            Assert.Equal((400, 1600), SizeOf(Path.Combine(_root, "tall-preview.png")));
            Assert.NotNull(file.Ensure(small, Path.Combine(_root, "small-preview.png")));
            Assert.Equal((640, 480), SizeOf(Path.Combine(_root, "small-preview.png")));
        });
    }

    [Fact]
    public void WindowsCachedCopyWithNoExtensionIsDecodedByContent()
    {
        string cache = Path.Combine(_root, "TranscodedWallpaper");
        WritePng(cache, 2000, 1000);
        string dest = Path.Combine(_root, "preview.png");
        Sta.Run(() =>
        {
            Assert.Equal(dest, new WallpaperPreviewFile().Ensure(cache, dest));
            Assert.Equal((1600, 800), SizeOf(dest));
        });
    }

    [Fact]
    public void ThePreviewIsWrittenAgainOnlyWhenThePictureChanges()
    {
        string source = Path.Combine(_root, "w.png");
        WritePng(source, 2400, 1200);
        string dest = Path.Combine(_root, "preview.png");
        Sta.Run(() =>
        {
            var file = new WallpaperPreviewFile();
            file.Ensure(source, dest);
            var first = File.GetLastWriteTimeUtc(dest);
            Thread.Sleep(40);
            file.Ensure(source, dest);
            Assert.Equal(first, File.GetLastWriteTimeUtc(dest));        // nothing changed: not rewritten

            Thread.Sleep(40);
            WritePng(source, 1200, 2400, shade: 200);                    // a new wallpaper at the same path
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
            file.Ensure(source, dest);
            Assert.Equal((800, 1600), SizeOf(dest));
            Assert.True(File.GetLastWriteTimeUtc(dest) > first);
        });
    }

    [Fact]
    public void APictureThatWillNotDecodeOrIsGoneMakesNoPreview()
    {
        string junk = Path.Combine(_root, "junk");
        File.WriteAllText(junk, "this is not a picture");
        string empty = Path.Combine(_root, "empty");
        File.WriteAllBytes(empty, []);
        string dest = Path.Combine(_root, "preview.png");
        Sta.Run(() =>
        {
            var file = new WallpaperPreviewFile();
            Assert.Null(file.Ensure(junk, dest));
            Assert.Null(file.Ensure(empty, dest));
            Assert.Null(file.Ensure(Path.Combine(_root, "missing.png"), dest));
        });
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void DeleteRemovesThePreviewAndNeverThrows()
    {
        string source = Path.Combine(_root, "w.png");
        WritePng(source, 300, 200);
        string dest = Path.Combine(_root, "preview.png");
        Sta.Run(() =>
        {
            var file = new WallpaperPreviewFile();
            file.Ensure(source, dest);
            Assert.True(File.Exists(dest));
            file.Delete(dest);
            Assert.False(File.Exists(dest));
            file.Delete(dest);   // already gone
            // Made again after a delete, even though the picture did not change.
            Assert.NotNull(file.Ensure(source, dest));
            Assert.True(File.Exists(dest));
        });
    }

    // ---- through the service ----

    [Fact]
    public void TheStateOffersThePreviewOnlyWhileTheIntegrationIsInstalled()
    {
        var paths = new Paths(appDataBase: _root);
        var store = new SettingsStore(paths);
        string preview = Path.Combine(paths.ExternalDir, WallpaperPreview.FileName);
        Sta.Run(() =>
        {
            using var service = new DeskDesignService(paths, store);
            store.SaveMcpEnabled(true);
            service.Publish();
            var state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(paths.ExternalDir, DeskDesign.StateFile)))!;
            var wallpaper = state["wallpaper"]!;
            // This PC's wallpaper decides whether there is a picture; when there is, the path is the preview and it is small enough.
            if (wallpaper["path"] is { } path)
            {
                Assert.Equal(preview, (string)path!);
                Assert.True(File.Exists(preview));
                var (w, h) = SizeOf(preview);
                Assert.InRange(Math.Max(w, h), 1, WallpaperPreview.MaxSide);
                Assert.Contains((string)wallpaper["source"]!, new[] { "file", "cache" });
                Assert.Equal("image", (string)wallpaper["kind"]!);
            }
            else
            {
                Assert.False(File.Exists(preview));
            }

            // Turned off: the state and the picture both go.
            File.WriteAllBytes(preview, [137, 80, 78, 71]);
            store.SaveMcpEnabled(false);
            service.Publish();
            Assert.False(File.Exists(preview));
            Assert.False(File.Exists(Path.Combine(paths.ExternalDir, DeskDesign.StateFile)));
        });
    }
}
