using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// 3.9.1: installed font packs are drawn from a copy under <c>widgets\.fonts\</c>, never from the pack folder itself. WPF keeps a font
/// file open once it has drawn from it (see PackFontLockTests in the App tests), and a pack folder with an open file can't be
/// renamed, which is the first thing Uninstall does. These pin the copy and the clean-up.
/// </summary>
public sealed class PackFontsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-packfonts-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng,
        () => new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    private static void Install(WidgetStore store, string name = "good-font", WidgetSource source = WidgetSource.Catalog)
    {
        var files = WidgetPackFixtures.Load("good", name);
        var v = PackValidator.Validate(files, name, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        Assert.True(store.Install(files, v.Manifest!, source).Ok);
    }

    [Fact]
    public void An_installed_font_pack_is_offered_as_a_copy_of_its_font_files()
    {
        var store = Store();
        Install(store);

        var folder = Assert.Single(PackFonts.Folders(store));

        Assert.Equal("good-font", folder.PackId);
        Assert.Equal("Fixture font", folder.PackName);
        Assert.StartsWith(Path.Combine(store.Root, ".fonts") + Path.DirectorySeparatorChar, folder.Dir);
        Assert.NotEqual(store.PackDir("good-font"), folder.Dir);
        Assert.Equal(File.ReadAllBytes(Path.Combine(store.PackDir("good-font"), "fixture.ttf")),
            File.ReadAllBytes(Assert.Single(Directory.GetFiles(folder.Dir))));
        Assert.Equal(["fixture.ttf"], Directory.GetFiles(folder.Dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Asking_again_reuses_the_copy()
    {
        var store = Store();
        Install(store);
        var first = Assert.Single(PackFonts.Folders(store));
        File.WriteAllText(Path.Combine(first.Dir, "marker.txt"), "x");

        var second = Assert.Single(PackFonts.Folders(store));

        Assert.Equal(first.Dir, second.Dir);
        Assert.True(File.Exists(Path.Combine(second.Dir, "marker.txt")));
    }

    [Fact]
    public void Other_kinds_and_a_disabled_font_pack_are_not_copied()
    {
        var store = Store();
        Install(store, "good-palette");
        Assert.Empty(PackFonts.Folders(store));
        Assert.False(Directory.Exists(Path.Combine(store.Root, ".fonts")));

        Install(store);
        File.WriteAllText(store.RegistryFile, File.ReadAllText(store.RegistryFile).Replace("\"enabled\": true", "\"enabled\": false"));
        Assert.Empty(PackFonts.Folders(store));
    }

    [Fact]
    public void An_open_font_copy_does_not_stop_the_pack_being_uninstalled()
    {
        var store = Store();
        Install(store);
        var folder = Assert.Single(PackFonts.Folders(store));

        // What WPF does once it has drawn from the file: it keeps it open without FILE_SHARE_DELETE.
        using var held = new FileStream(Directory.GetFiles(folder.Dir)[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = store.Uninstall("good-font");

        Assert.True(result.Ok, result.Error);
        Assert.False(Directory.Exists(store.PackDir("good-font")));
        Assert.Empty(store.Load().Packs);
    }

    [Fact]
    public void The_copy_of_a_removed_pack_is_swept_once_nothing_holds_it()
    {
        var store = Store();
        Install(store);
        var folder = Assert.Single(PackFonts.Folders(store));
        var held = new FileStream(Directory.GetFiles(folder.Dir)[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(store.Uninstall("good-font").Ok);

        Assert.Empty(PackFonts.Folders(store));
        Assert.True(Directory.Exists(folder.Dir)); // still held: left, not an error

        held.Dispose();
        Assert.Empty(PackFonts.Folders(store));
        Assert.False(Directory.Exists(folder.Dir));
    }

    [Fact]
    public void A_copy_another_caller_finished_first_is_used_and_the_pack_is_not_dropped()
    {
        var store = Store();
        Install(store);

        // The other caller wins between our copy and our rename: its folder is already in place when ours is moved.
        var folders = PackFonts.Folders(store, beforeMove: dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "fixture.ttf"), [1]);
        });

        var folder = Assert.Single(folders);
        Assert.Equal("good-font", folder.PackId);
        Assert.Equal(["fixture.ttf"], Directory.GetFiles(folder.Dir).Select(Path.GetFileName));
        Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(store.Root, ".fonts")), d => d.Contains(".tmp-"));
    }

    [Fact]
    public void Many_callers_at_once_all_get_the_pack()
    {
        var store = Store();
        Install(store);

        var results = new PackFontFolder[16][];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 },
            i => results[i] = [.. PackFonts.Folders(store)]);

        Assert.All(results, r => Assert.Equal("good-font", Assert.Single(r).PackId));
    }
}
