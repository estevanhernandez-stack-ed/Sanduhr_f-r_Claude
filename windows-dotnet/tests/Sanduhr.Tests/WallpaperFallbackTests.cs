using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Which wallpaper file is read when the one Windows names is gone, and the size of the preview copy.</summary>
public sealed class WallpaperFallbackTests
{
    private const string Themes = @"C:\Users\x\AppData\Roaming\Microsoft\Windows\Themes";
    private const string Gone = @"C:\Users\x\Downloads\wallhaven-zx3zlj.png";

    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private static string In(string file) => Path.Combine(Themes, file);

    [Fact]
    public void TheFileWindowsNamesWinsWhenItIsThere()
    {
        var c = WallpaperFallback.Choose(Gone, 0, Themes, Existing(Gone, In("TranscodedWallpaper")));
        Assert.Equal(new WallpaperChoice(Gone, "file"), c);
        Assert.True(c.Readable);
    }

    [Fact]
    public void ADeletedFileFallsBackToWindowsCachedCopy()
    {
        var c = WallpaperFallback.Choose(Gone, 0, Themes, Existing(In("TranscodedWallpaper")));
        Assert.Equal(new WallpaperChoice(In("TranscodedWallpaper"), "cache"), c);
    }

    [Fact]
    public void AMonitorsOwnCacheFileIsPreferredToTheWholeDesktopOne()
    {
        var both = Existing(In("Transcoded_001"), In("TranscodedWallpaper"));
        Assert.Equal(In("Transcoded_001"), WallpaperFallback.Choose(Gone, 1, Themes, both).Path);
        // Monitor 0 has no file of its own here, so the shared one stands in.
        Assert.Equal(In("TranscodedWallpaper"), WallpaperFallback.Choose(Gone, 0, Themes, both).Path);
        Assert.Equal(In("Transcoded_000"), WallpaperFallback.Choose(Gone, 0, Themes, Existing(In("Transcoded_000"), In("TranscodedWallpaper"))).Path);
    }

    [Fact]
    public void TheCachedFilesFolderIsLookedInToo()
    {
        string cached = Path.Combine(Themes, "CachedFiles", "Transcoded_002");
        var c = WallpaperFallback.Choose(null, 2, Themes, Existing(cached));
        Assert.Equal(new WallpaperChoice(cached, "cache"), c);
    }

    [Fact]
    public void NoNamedFileFallsBackToTheCacheToo()
    {
        var c = WallpaperFallback.Choose("  ", 0, Themes, Existing(In("TranscodedWallpaper")));
        Assert.Equal("cache", c.Source);
    }

    [Fact]
    public void ASolidColorDesktopNeverReadsAStaleCache()
    {
        // Windows leaves TranscodedWallpaper behind when the user switches to a plain color.
        var c = WallpaperFallback.Choose(null, 0, Themes, Existing(In("TranscodedWallpaper")), plainColor: true);
        Assert.Equal(new WallpaperChoice(null, null), c);
        Assert.False(c.Readable);
    }

    [Fact]
    public void NothingReadableKeepsTheNamedPathWithNoSource()
    {
        var c = WallpaperFallback.Choose(Gone, 0, Themes, Existing());
        Assert.Equal(new WallpaperChoice(Gone, null), c);
        Assert.False(c.Readable);
        Assert.Equal(new WallpaperChoice(null, null), WallpaperFallback.Choose(null, 0, Themes, Existing()));
    }

    [Fact]
    public void ThePlainColorFlagDoesNotStopADeletedFileFromUsingItsCache()
    {
        // A path was named, so a picture was chosen; only "no path at all" means a solid color.
        var c = WallpaperFallback.Choose(Gone, 0, Themes, Existing(In("TranscodedWallpaper")), plainColor: true);
        Assert.Equal("cache", c.Source);
    }

    [Fact]
    public void CacheNamesFollowWindows()
    {
        Assert.Equal("Transcoded_000", WallpaperFallback.MonitorCacheFile(0));
        Assert.Equal("Transcoded_012", WallpaperFallback.MonitorCacheFile(12));
        Assert.Equal("Transcoded_000", WallpaperFallback.MonitorCacheFile(-3));
        Assert.Equal(@"C:\Users\x\AppData\Roaming\Microsoft\Windows\Themes", WallpaperFallback.ThemesDir(@"C:\Users\x\AppData\Roaming"));
    }

    [Fact]
    public void TheStateNamesThePreviewCopyAndWhereItWasMadeFrom()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var state = DeskState.Build(current, [], WallpaperOverview.Combine([]), @"C:\Users\me\.sanduhr\wallpaper-preview.png", null, [],
            direct: false, writerVersion: "3.7.0", wallpaperSource: WallpaperFallback.SourceCache, wallpaperIsImage: true);
        var wall = state["wallpaper"]!;
        Assert.Equal(@"C:\Users\me\.sanduhr\wallpaper-preview.png", (string?)wall["path"]);
        Assert.Equal("cache", (string?)wall["source"]);
        Assert.Equal("image", (string?)wall["kind"]);

        // No preview could be written, but the wallpaper is a picture: still an image, with no path to open and no source.
        var none = DeskState.Build(current, [], WallpaperOverview.Combine([]), null, null, [], direct: true, writerVersion: "3.7.0", wallpaperIsImage: true);
        Assert.Null(none["wallpaper"]!["path"]);
        Assert.Null(none["wallpaper"]!["source"]);
        Assert.Equal("image", (string?)none["wallpaper"]!["kind"]);
        Assert.True((bool)none["direct"]!);
    }

    // ---- the preview copy ----

    [Theory]
    [InlineData(3840, 2160, 1600, 900)]
    [InlineData(1080, 1920, 900, 1600)]
    [InlineData(1600, 1600, 1600, 1600)]
    [InlineData(1920, 1080, 1600, 900)]
    [InlineData(800, 600, 800, 600)]      // never enlarged
    [InlineData(0, 100, 0, 0)]
    [InlineData(100, -4, 0, 0)]
    public void ThePreviewKeepsItsShapeAndItsLongestSideIsAtMost1600(int w, int h, int ew, int eh)
    {
        Assert.Equal((ew, eh), WallpaperPreview.FitSize(w, h));
    }

    [Fact]
    public void AnExtremeShapeKeepsAtLeastOnePixel()
    {
        var (w, h) = WallpaperPreview.FitSize(100_000, 10);
        Assert.Equal(1600, w);
        Assert.Equal(1, h);
    }

    [Fact]
    public void ThePreviewIsStaleWhenThePathTheTimeOrTheSizeChanges()
    {
        string k = WallpaperPreview.Key("a.png", 10, 100);
        Assert.Equal(k, WallpaperPreview.Key("a.png", 10, 100));
        Assert.NotEqual(k, WallpaperPreview.Key("b.png", 10, 100));
        Assert.NotEqual(k, WallpaperPreview.Key("a.png", 11, 100));
        Assert.NotEqual(k, WallpaperPreview.Key("a.png", 10, 101));
        Assert.Equal("wallpaper-preview.png", WallpaperPreview.FileName);
    }
}
