using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings, Desk, Launcher (spec, Desk Launcher) over a scratch AppData: the key in settings.json, the rows and what editing
/// them saves, the picture files that leave with their button, and the PNG import's checks. The two pickers and the file
/// dialog are the only parts a person has to drive by hand.
/// </summary>
[Collection("SettingsWindow")] // swaps the shared LauncherAppCache.Current, which a whole Settings window also reads
public sealed class LauncherSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-launcher-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public LauncherSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static LauncherButton App(string id, string? label = null, string? image = null) =>
        LauncherButton.Create(LauncherKind.App, id, image, label, null)!;

    private LauncherSettingsViewModel Open(LauncherModel? model = null, bool placed = true)
    {
        if (model is not null) _store.SaveLauncher(model);
        return new LauncherSettingsViewModel(_store, _paths, () => placed);
    }

    private static string MakePng(string path, int w, int h)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(Brushes.OrangeRed, null, new System.Windows.Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(path);
        enc.Save(f);
        return path;
    }

    // ---- the settings key ----

    [Fact]
    public void AbsentKeyReadsAsNoButtonsAndNothingIsWrittenForEmpty()
    {
        Assert.True(_store.LoadLauncher().IsEmpty);
        _store.SaveLauncher(LauncherModel.Empty);
        Assert.False(JsonNode.Parse(File.ReadAllText(_paths.SettingsFile))!.AsObject().ContainsKey("desk_launcher"));
    }

    [Fact]
    public void TheModelRoundTripsThroughSettingsJsonAndOtherKeysSurvive()
    {
        _store.SaveDeskPlate(true);
        var model = new LauncherModel(LauncherOrientation.Column, [App("Spotify", "Music"), LauncherButton.Create(LauncherKind.Link, "https://example.com")!]);
        _store.SaveLauncher(model);
        var back = _store.LoadLauncher();
        Assert.Equal(model.Orientation, back.Orientation);
        Assert.Equal(model.Buttons, back.Buttons);
        Assert.True(_store.LoadDeskPlate());
        // Emptying a row Launcher removes the key; a column with no buttons keeps its choice.
        _store.SaveLauncher(new LauncherModel(LauncherOrientation.Column, []));
        Assert.Equal(LauncherOrientation.Column, _store.LoadLauncher().Orientation);
        _store.SaveLauncher(LauncherModel.Empty);
        Assert.False(JsonNode.Parse(File.ReadAllText(_paths.SettingsFile))!.AsObject().ContainsKey("desk_launcher"));
    }

    [Fact]
    public void AHandEditedFileWithHostileEntriesLoadsOnlyTheGoodOnes()
    {
        File.WriteAllText(_paths.SettingsFile, """
            { "desk_launcher": { "orientation": "column", "buttons": [
              { "kind": "app", "target": "C:\\Windows\\..\\cmd.exe" },
              { "kind": "link", "target": "javascript:alert(1)" },
              { "kind": "link", "target": "https://example.com", "image": "..\\..\\x.png" },
              { "kind": "app", "target": "Spotify" } ] } }
            """);
        var m = _store.LoadLauncher();
        Assert.Equal(LauncherOrientation.Column, m.Orientation);
        Assert.Equal(["https://example.com", "Spotify"], m.Buttons.Select(b => b.Target));
        Assert.All(m.Buttons, b => Assert.Null(b.Image));
    }


    // ---- membership and auto-place on the first button ----

    private sealed class FakeCatalog(params LauncherAppInfo[] apps) : Sanduhr.App.Interop.ILauncherAppCatalog
    {
        public IReadOnlyList<LauncherAppInfo> Enumerate() => apps;
        public bool Exists(string aumid) => apps.Any(a => a.Aumid == aumid);
        public BitmapSource? Icon(string aumid, int px) => null;
    }

    private const string Gui = "{6D809377-6AF0-444B-8957-A3773F02200E}";

    /// <summary>Runs <paramref name="body"/> with the app's shared cache holding exactly these apps (as the shell listed them).</summary>
    private static void WithListing(LauncherAppInfo[] apps, Action body)
    {
        var cache = new LauncherAppCache(() => new FakeCatalog(apps));
        var saved = LauncherAppCache.Current;
        LauncherAppCache.Current = cache;
        try
        {
            cache.Refresh([], force: true);
            for (int i = 0; i < 200 && !cache.Known; i++) Thread.Sleep(25);
            Assert.True(cache.Known);
            body();
        }
        finally
        {
            LauncherAppCache.Current = saved;
            cache.Dispose();
        }
    }

    [Fact]
    public void AnAppIsSavedOnlyWhileTheStartMenuListingHasIt()
    {
        var listed = new LauncherAppInfo("7-Zip File Manager", Gui + @"\7-Zip\7zFM.exe");
        WithListing([listed], () =>
        {
            var vm = Open(placed: true);
            vm.Add(App(Gui + @"\Other\other.exe"), "refused");           // well-formed, not listed
            Assert.Empty(_store.LoadLauncher().Buttons);
            Assert.Equal("refused", vm.Problem);
            vm.Add(App("Sanduhr.NotListed"), "refused");                 // an AUMID, not listed
            Assert.Empty(_store.LoadLauncher().Buttons);

            vm.Add(App(listed.Aumid), "refused");                        // the listed path form
            Assert.Equal([listed.Aumid], _store.LoadLauncher().Buttons.Select(b => b.Target));
        });
    }

    [Fact]
    public void ALinkNeedsNoListing()
    {
        WithListing([], () =>
        {
            var vm = Open(placed: true);
            vm.Add(LauncherButton.Create(LauncherKind.Link, "https://example.com")!, "refused");
            Assert.Single(_store.LoadLauncher().Buttons);
        });
    }

    [Fact]
    public void TheFirstButtonPlacesAHiddenLauncherAndSaysWhere()
    {
        WithListing([new LauncherAppInfo("A", "A")], () =>
        {
            int placedCalls = 0;
            bool placed = false;
            var vm = new LauncherSettingsViewModel(_store, _paths, () => placed, () => { placedCalls++; placed = true; return "bottom right"; });
            vm.Add(App("A"), "refused");
            Assert.Equal(1, placedCalls);
            Assert.True(vm.HasPlacementNote);
            Assert.Equal("Placed at bottom right. Move it in Settings, Desk, Pieces.", vm.PlacementNote);

            vm.Add(LauncherButton.Create(LauncherKind.Link, "https://example.com")!, "refused");   // the second button places nothing
            Assert.Equal(1, placedCalls);

            vm.PlacementChanged();                                       // the person moved it by hand: the note goes
            Assert.False(vm.HasPlacementNote);
        });
    }

    [Fact]
    public void AnAlreadyPlacedLauncherIsLeftWhereItIs()
    {
        WithListing([new LauncherAppInfo("A", "A")], () =>
        {
            int placedCalls = 0;
            var vm = new LauncherSettingsViewModel(_store, _paths, () => true, () => { placedCalls++; return "bottom left"; });
            vm.Add(App("A"), "refused");
            Assert.Equal(0, placedCalls);
            Assert.False(vm.HasPlacementNote);
        });
    }

    [Fact]
    public void TheDeskSettingsPlacesTheLauncherAtItsDefaultPlaceAndSavesIt()
    {
        var desk = new DeskSettingsViewModel(_store, [], () => []);
        Assert.Equal("top left", desk.PlaceLauncherIfHidden());       // DeskArrangement.DefaultPlaceFor("launcher")
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(_store.LoadDesk().Layout).Placement("launcher")!.Anchor);
        Assert.Null(desk.PlaceLauncherIfHidden());                    // already placed: nothing moves
    }

    // ---- the rows ----

    [Fact]
    public void RowsFollowTheSavedButtonsAndNameThem()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("Chrome._crx_x", "Dash"), App("Spotify"), LauncherButton.Create(LauncherKind.Link, "https://www.example.com/p")!]));
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(["Dash", "Spotify", "example.com"], vm.Rows.Select(r => r.Title));
        Assert.Equal("3 of 8 buttons", vm.CountText);
        Assert.True(vm.CanAdd);
        Assert.Equal("Row", vm.SelectedOrientation);
        Assert.True(vm.Rows[0].IsApp && !vm.Rows[2].IsApp);
        Assert.Equal("Use the app's icon", vm.Rows[0].UseDefaultImageText);
        Assert.Equal("Use the link icon", vm.Rows[2].UseDefaultImageText);
        Assert.Equal("Spotify", vm.Rows[1].Subject);
    }

    [Fact]
    public void ThePlacementNoteShowsOnlyWhileTheButtonsHaveNoPlace()
    {
        Assert.False(Open(placed: false).HasPlacementNote);   // nothing to place yet
        var hidden = Open(new LauncherModel(LauncherOrientation.Row, [App("A")]), placed: false);
        Assert.True(hidden.HasPlacementNote);
        Assert.Contains("Settings, Desk, Pieces", hidden.PlacementNote);
        Assert.False(Open(new LauncherModel(LauncherOrientation.Row, [App("A")]), placed: true).HasPlacementNote);
    }

    [Fact]
    public void LabelAndGlowEditsSaveAtOnceAndTellTheDesk()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A"), App("B")]));
        int changed = 0;
        vm.Changed = () => changed++;

        vm.Rows[1].Label = "  Notes  ";
        Assert.Equal("Notes", _store.LoadLauncher().Buttons[1].Label);
        vm.Rows[1].Glow = "FF8800";
        Assert.Equal("#ff8800", _store.LoadLauncher().Buttons[1].Glow);
        Assert.Equal(2, changed);

        vm.Rows[1].Glow = "";
        Assert.Null(_store.LoadLauncher().Buttons[1].Glow);
        vm.Rows[1].Label = "";
        Assert.Null(_store.LoadLauncher().Buttons[1].Label);
        Assert.Equal("B", vm.Rows[1].Title);
        Assert.Equal(4, changed);
    }

    [Fact]
    public void ABadGlowColorIsNotSavedAndSaysWhy()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A")]));
        vm.Rows[0].Glow = "orange";
        Assert.True(vm.Rows[0].HasGlowProblem);
        Assert.Null(_store.LoadLauncher().Buttons[0].Glow);
        vm.Rows[0].Glow = "#abc";
        Assert.False(vm.Rows[0].HasGlowProblem);
        Assert.Equal("#aabbcc", _store.LoadLauncher().Buttons[0].Glow);
    }

    [Fact]
    public void MoveUpMoveDownAndRemoveReorderAndSave()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A"), App("B"), App("C")]));
        Assert.False(vm.Rows[0].CanMoveUp);
        Assert.False(vm.Rows[2].CanMoveDown);

        vm.MoveUpCommand.Execute(vm.Rows[2]);
        Assert.Equal(["A", "C", "B"], _store.LoadLauncher().Buttons.Select(b => b.Target));
        Assert.Equal(["A", "C", "B"], vm.Rows.Select(r => r.Target));
        Assert.True(vm.Rows[1].IsSelected);

        vm.MoveDownCommand.Execute(vm.Rows[0]);
        Assert.Equal(["C", "A", "B"], _store.LoadLauncher().Buttons.Select(b => b.Target));

        vm.RemoveCommand.Execute(vm.Rows[1]);
        Assert.Equal(["C", "B"], _store.LoadLauncher().Buttons.Select(b => b.Target));
        Assert.Equal("2 of 8 buttons", vm.CountText);

        vm.MoveUpCommand.Execute(vm.Rows[0]);   // already first: nothing
        Assert.Equal(["C", "B"], _store.LoadLauncher().Buttons.Select(b => b.Target));
    }

    [Fact]
    public void RowOrColumnSavesAndKeepsTheButtons()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A")]));
        vm.SelectedOrientation = "Column";
        var saved = _store.LoadLauncher();
        Assert.Equal(LauncherOrientation.Column, saved.Orientation);
        Assert.Single(saved.Buttons);
        vm.SelectedOrientation = "Row";
        Assert.Equal(LauncherOrientation.Row, _store.LoadLauncher().Orientation);
    }

    [Fact]
    public void SelectMarksTheOneRowThatAMissingAppClickLandsOn()
    {
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A"), App("B"), App("C")]));
        vm.Select(1);
        Assert.Equal([false, true, false], vm.Rows.Select(r => r.IsSelected));
        vm.Select(2);
        Assert.Equal([false, false, true], vm.Rows.Select(r => r.IsSelected));
    }

    // ---- pictures ----

    [Fact]
    public void ImportingAPngStoresA256SquareUnderItsHashAndLoadsBack()
    {
        var source = MakePng(Path.Combine(_root, "wide.png"), 400, 100);
        var result = LauncherImages.Import(_paths, source);
        Assert.True(result.Ok, result.Error);
        Assert.True(LauncherButton.IsImageName(result.FileName));
        var stored = Path.Combine(_paths.LauncherDir, result.FileName!);
        Assert.True(File.Exists(stored));
        // The stored file is Sanduhr's own 256 px square, not the picked file.
        var loaded = (BitmapSource)LauncherImages.Load(_paths, result.FileName)!;
        Assert.Equal((256, 256), (loaded.PixelWidth, loaded.PixelHeight));
        Assert.NotEqual(File.ReadAllBytes(source), File.ReadAllBytes(stored));
        // Importing the same picture again lands on the same name.
        Assert.Equal(result.FileName, LauncherImages.Import(_paths, source).FileName);
    }

    [Fact]
    public void AnImportRefusesNonPngsEmptyFilesAndOversizedOnes()
    {
        var txt = Path.Combine(_root, "notes.png");
        File.WriteAllText(txt, "this is not a png");
        Assert.False(LauncherImages.Import(_paths, txt).Ok);

        var empty = Path.Combine(_root, "empty.png");
        File.WriteAllBytes(empty, []);
        Assert.False(LauncherImages.Import(_paths, empty).Ok);

        var big = Path.Combine(_root, "big.png");
        var bytes = new byte[LauncherImagePolicy.MaxBytes + 1];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        File.WriteAllBytes(big, bytes);
        var refused = LauncherImages.Import(_paths, big);
        Assert.False(refused.Ok);
        Assert.Contains("1 MB", refused.Error);

        Assert.False(LauncherImages.Import(_paths, Path.Combine(_root, "missing.png")).Ok);
        Assert.False(Directory.Exists(_paths.LauncherDir) && Directory.EnumerateFiles(_paths.LauncherDir).Any());
    }

    [Fact]
    public void ARealPngWithAGarbledBodyIsRefusedNotCrashed()
    {
        var path = MakePng(Path.Combine(_root, "ok.png"), 64, 64);
        var bytes = File.ReadAllBytes(path);
        for (int i = 40; i < bytes.Length - 20; i++) bytes[i] ^= 0x5A;
        var broken = Path.Combine(_root, "broken.png");
        File.WriteAllBytes(broken, bytes);
        Assert.False(LauncherImages.Import(_paths, broken).Ok);
    }

    [Fact]
    public void ARemovedButtonTakesItsPictureAndASharedPictureStaysWhileAnyButtonUsesIt()
    {
        var file = LauncherImages.Import(_paths, MakePng(Path.Combine(_root, "p.png"), 100, 100)).FileName!;
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A", image: file), App("B", image: file), App("C")]));
        var stored = Path.Combine(_paths.LauncherDir, file);

        vm.RemoveCommand.Execute(vm.Rows[0]);
        Assert.True(File.Exists(stored));          // B still uses it
        vm.RemoveCommand.Execute(vm.Rows[0]);
        Assert.False(File.Exists(stored));         // nobody does
    }

    [Fact]
    public void UsingTheAppsIconDropsThePictureAndItsFile()
    {
        var file = LauncherImages.Import(_paths, MakePng(Path.Combine(_root, "q.png"), 100, 100)).FileName!;
        var vm = Open(new LauncherModel(LauncherOrientation.Row, [App("A", image: file)]));
        Assert.True(vm.Rows[0].CanUseDefaultImage);

        vm.UseDefaultImageCommand.Execute(vm.Rows[0]);
        Assert.Null(_store.LoadLauncher().Buttons[0].Image);
        Assert.False(File.Exists(Path.Combine(_paths.LauncherDir, file)));
        Assert.False(vm.Rows[0].CanUseDefaultImage);
    }

    [Fact]
    public void OnlyStoredNamesAreEverDeleted()
    {
        Directory.CreateDirectory(_paths.LauncherDir);
        var keep = Path.Combine(_paths.LauncherDir, "notes.txt");
        File.WriteAllText(keep, "mine");
        var outside = Path.Combine(_root, "outside.png");
        File.WriteAllText(outside, "mine");
        LauncherImages.Delete(_paths, ["notes.txt", "..\\outside.png", "../outside.png", new string('a', 64) + ".png"]);
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ResetsSettingsPartListsThePicturesFolder()
    {
        Directory.CreateDirectory(_paths.LauncherDir);
        File.WriteAllBytes(Path.Combine(_paths.LauncherDir, new string('c', 64) + ".png"), [1]);
        var inventory = ResetPlan.Inventory(_paths);
        Assert.Contains(inventory[ResetPart.Settings], i => i.IsDirectory && i.Path == _paths.LauncherDir);
        Assert.DoesNotContain(inventory[ResetPart.History], i => i.Path == _paths.LauncherDir);
    }
}
