using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The saved designs on disk (spec, the agent-designed Desk, saved designs): one file per design, the ten-kept rule, and a
/// Launcher image staying on disk while a design or the Launcher uses it.</summary>
public sealed class DesignStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-store-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;

    public DesignStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Image(char c) => new string(c, 64) + ".png";

    private static SavedDesign WithImage(string id, int minutes, string image, bool pinned = false) =>
        new(id.PadRight(8, '0'), "Look " + id, pinned, new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
            DeskDesign.Parse(JsonNode.Parse("{\"launcher\":{\"orientation\":\"row\",\"buttons\":[{\"kind\":\"link\",\"target\":\"https://example.com\",\"image\":\"" + image + "\"}]}}")).Design!);

    private string PlaceImage(string name)
    {
        Directory.CreateDirectory(_paths.LauncherDir);
        string path = Path.Combine(_paths.LauncherDir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void ALauncherImageStaysOnDiskWhileAnyKeptDesignUsesIt()
    {
        var store = new DesignStore(_paths);
        string shared = PlaceImage(Image('a'));
        string only = PlaceImage(Image('b'));
        store.Add(WithImage("one", 1, Image('a')), LauncherModel.Empty);
        store.Add(WithImage("two", 2, Image('a')), LauncherModel.Empty);
        store.Add(WithImage("three", 3, Image('b')), LauncherModel.Empty);

        store.Delete("three".PadRight(8, '0'), LauncherModel.Empty);
        Assert.False(File.Exists(only));        // nothing else used it
        store.Delete("one".PadRight(8, '0'), LauncherModel.Empty);
        Assert.True(File.Exists(shared));       // "two" still does
        store.Delete("two".PadRight(8, '0'), LauncherModel.Empty);
        Assert.False(File.Exists(shared));
    }

    [Fact]
    public void ALauncherImageTheLauncherUsesNowIsNeverDeletedWithADesign()
    {
        var store = new DesignStore(_paths);
        string path = PlaceImage(Image('c'));
        var launcher = LauncherModel.Parse(JsonNode.Parse("{\"orientation\":\"row\",\"buttons\":[{\"kind\":\"link\",\"target\":\"https://example.com\",\"image\":\"" + Image('c') + "\"}]}"));
        store.Add(WithImage("only", 1, Image('c')), launcher);
        store.Delete("only".PadRight(8, '0'), launcher);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TheNewestDesignSurvivesEvenWhenEveryOtherIsPinned()
    {
        var store = new DesignStore(_paths);
        for (int i = 0; i < 10; i++) store.Add(WithImage("p" + i, i, Image('d'), pinned: true), LauncherModel.Empty);
        Assert.Equal(10, store.List().Count);
        var kept = store.Add(WithImage("fresh", 100, Image('d')), LauncherModel.Empty);
        Assert.Equal(11, kept.Count);                                        // a library of pins may hold more than ten
        Assert.Contains(store.List(), d => d.Id == "fresh".PadRight(8, '0'));
    }

    [Fact]
    public void TheOldestUnpinnedDesignGoesPastTenAndItsOwnImageWithIt()
    {
        var store = new DesignStore(_paths);
        string oldest = PlaceImage(Image('f'));
        store.Add(WithImage("old", 0, Image('f')), LauncherModel.Empty);
        for (int i = 1; i <= 10; i++) store.Add(WithImage("n" + i.ToString("00"), i, Image('9')), LauncherModel.Empty);
        Assert.Equal(10, store.List().Count);
        Assert.DoesNotContain(store.List(), d => d.Id == "old".PadRight(8, '0'));
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(Path.Combine(store.Dir, "old00000.json")));
    }

    [Fact]
    public void AFileThatIsNotADesignIsSkippedAndDesignsNeedTheirOwnIdAsTheirName()
    {
        var store = new DesignStore(_paths);
        store.Add(WithImage("good", 1, Image('e')), LauncherModel.Empty);
        File.WriteAllText(Path.Combine(store.Dir, "junk.json"), "{ not json");
        File.WriteAllText(Path.Combine(store.Dir, "other.json"), File.ReadAllText(Path.Combine(store.Dir, "good0000.json")));   // wrong file name for its id
        Assert.Equal(["good0000"], store.List().Select(d => d.Id).ToArray());
        Assert.Null(store.Load("../good0000"));
        Assert.Null(store.Load(null));
    }

    [Fact]
    public void AnEmptyOrMissingFolderListsNothing()
    {
        var store = new DesignStore(_paths);
        Assert.Empty(store.List());
        Assert.False(store.Delete("nope0000", LauncherModel.Empty));
        Assert.False(store.ImageExists(Image('z')));
        Assert.False(store.ImageExists("../x.png"));
    }
}
