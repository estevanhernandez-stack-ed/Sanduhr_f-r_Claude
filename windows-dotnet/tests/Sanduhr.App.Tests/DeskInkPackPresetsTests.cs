using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>3.9.1: Settings, Desk, Ink: the palettes of installed palette packs are in the preset list after the built-ins, the list
/// follows an install or an uninstall, and an ink picked from a removed pack keeps its colors.</summary>
public sealed class DeskInkPackPresetsTests : IDisposable
{
    private const string PackLabel = "plain · Fixture palette";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-inkpacks-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskInkPackPresetsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Page() =>
        new(_store, ["Segoe UI"], () => [], () => PackInkPresets.Merged(InstalledPackKit.Store(_paths)));

    [Fact]
    public void WithoutPacksTheListIsTheFiveBuiltIns()
    {
        var names = Page().InkPresetNames;
        Assert.Equal(DeskInkPresets.All.Select(p => p.Name), names);
    }

    [Fact]
    public void AnInstalledPalettePackJoinsTheListWhenThePageReloads()
    {
        var page = Page();
        InstalledPackKit.Install(_paths, "good-palette");
        Assert.Equal(5, page.InkPresetNames.Count);

        page.Reload();

        Assert.Equal(6, page.InkPresetNames.Count);
        Assert.Equal(PackLabel, page.InkPresetNames[5]);
    }

    [Fact]
    public void PickingAPackPaletteSavesItsColorsAndTheChoiceSurvivesAReload()
    {
        InstalledPackKit.Install(_paths, "good-palette");
        var page = Page();

        page.InkPreset = PackLabel;

        Assert.Equal("ff2a6d,05d9e8", _store.LoadDeskInk().Color);
        Assert.Equal("ff2a6d,05d9e8", page.InkColors);
        page.Reload();
        Assert.Equal(PackLabel, page.InkPreset);
    }

    [Fact]
    public void RemovingThePackDropsTheEntryButTheDeskKeepsTheColors()
    {
        InstalledPackKit.Install(_paths, "good-palette");
        var page = Page();
        page.InkPreset = PackLabel;

        Assert.True(InstalledPackKit.Store(_paths).Uninstall("good-palette").Ok);
        page.Reload();

        Assert.Equal(5, page.InkPresetNames.Count);
        Assert.Null(page.InkPreset);
        Assert.Equal("ff2a6d,05d9e8", page.InkColors);
        Assert.Equal("ff2a6d,05d9e8", _store.LoadDeskInk().Color);
    }

    [Fact]
    public void A_typed_hex_switches_Light_or_Dark_to_Custom()
    {
        var page = Page();
        page.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Dark);
        page.InkColors = "ff8800";
        Assert.Equal(DeskInkMode.Custom, page.InkChoice.Mode);
        Assert.True(page.IsCustomInk);
        Assert.Equal(DeskInkMode.Custom, _store.LoadDeskInk().Mode);
        Assert.Equal("ff8800", _store.LoadDeskInk().Color);
    }

    [Fact]
    public void The_presets_show_whenever_the_ink_is_not_Automatic_and_a_preset_sets_Custom()
    {
        var page = Page();
        Assert.False(page.ShowsInkColors);                    // Automatic
        page.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Light);
        Assert.True(page.ShowsInkColors);
        page.InkPreset = "Sky";
        Assert.Equal(DeskInkMode.Custom, page.InkChoice.Mode);
        Assert.Equal(DeskInkMode.Custom, _store.LoadDeskInk().Mode);
        Assert.Equal("9ad7ff", _store.LoadDeskInk().Color);
    }
}
