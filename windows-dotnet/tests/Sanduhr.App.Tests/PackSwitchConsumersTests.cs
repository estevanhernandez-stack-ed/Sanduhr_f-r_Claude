using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4 Done: "switching a pack off removes it from its picker", in the App: the theme strip, the ink presets, the four font
/// pickers and the companion's sprites follow a switch while Settings is open, and a pack that is in use falls back without losing the
/// saved choice (Review Focus 1).
/// </summary>
[Collection("SettingsWindow")]
public sealed class PackSwitchConsumersTests : AppWindowFixture
{
    private const string PackTheme = "good-theme:fixture-dark";

    [Fact]
    public void Switching_off_the_theme_in_use_falls_back_unsaved_and_back_on_restores_it() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-theme");
        widget.ReloadUserThemes();
        widget.ApplyThemeByKey(PackTheme);
        Assert.Equal(PackTheme, widget.Palette.Key);

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-theme", false).Ok);
        widget.ReloadUserThemes();

        Assert.DoesNotContain(widget.Themes, t => t.Key == PackTheme);
        Assert.Equal("obsidian", widget.Palette.Key);
        Assert.Equal(PackTheme, new SettingsStore(paths).LoadTheme());   // shown, not saved

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-theme", true).Ok);
        widget.ReloadUserThemes();
        Assert.Equal(PackTheme, widget.Palette.Key);
    });

    [Fact]
    public void A_palette_pack_switched_off_leaves_the_ink_presets_and_the_ink_keeps_its_colors() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-palette");
        var store = new SettingsStore(paths);
        var desk = new DeskSettingsViewModel(store, ["Segoe UI"], () => [], () => PackInkPresets.Merged(InstalledPackKit.Store(paths)),
            () => InstalledPackKit.Store(paths));
        desk.InkPreset = "plain · Fixture palette";

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-palette", false).Ok);
        desk.Reload();

        Assert.DoesNotContain("plain · Fixture palette", desk.InkPresetNames);
        Assert.Null(desk.InkPreset);
        Assert.Equal("ff2a6d,05d9e8", store.LoadDeskInk().Color);
    });

    [Fact]
    public void Font_pickers_drop_a_family_that_leaves_and_keep_their_selection() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro", "Fixture Sans", "Segoe UI"];
        svm.PacksChanged();
        svm.Desk.Font = "Segoe UI";
        Assert.Contains("Fixture Sans", svm.Desk.FontChoices);
        Assert.Contains(svm.MessagePage.FontOptions, c => c.Value == "Fixture Sans");
        Assert.Contains("Fixture Sans", svm.ThemeMatch.Editor.FontChoices);
        Assert.Contains("Fixture Sans", svm.Themes.Studio.FontChoices);
        Assert.Contains("Fixture Sans", svm.Themes.Studio.DeskEditor.FontChoices);

        svm.PickerFonts = () => ["EsteFont Pro", "Segoe UI"];
        svm.PacksChanged();

        Assert.DoesNotContain("Fixture Sans", svm.Desk.FontChoices);
        Assert.DoesNotContain(svm.MessagePage.FontOptions, c => c.Value == "Fixture Sans");
        Assert.DoesNotContain("Fixture Sans", svm.ThemeMatch.Editor.FontChoices);
        Assert.DoesNotContain("Fixture Sans", svm.Themes.Studio.FontChoices);
        Assert.DoesNotContain("Fixture Sans", svm.Themes.Studio.DeskEditor.FontChoices);
        Assert.Equal("Segoe UI", svm.Desk.Font);
        Assert.Equal(DeskSettingsViewModel.SystemFont, svm.Desk.FontChoices[^1]);
    });

    [Fact]
    public void A_saved_font_from_a_pack_switched_off_stays_selected() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro", "Fixture Sans", "Segoe UI"];
        svm.PacksChanged();
        svm.Desk.Font = "Fixture Sans";
        svm.MessagePage.Font = svm.MessagePage.FontOptions.Single(c => c.Value == "Fixture Sans");

        svm.PickerFonts = () => ["EsteFont Pro", "Segoe UI"];
        svm.PacksChanged();

        Assert.Equal("Fixture Sans", svm.Desk.Font);
        Assert.Contains("Fixture Sans", svm.Desk.FontChoices);
        Assert.Equal("Fixture Sans", svm.MessagePage.Font.Value);
        Assert.Equal("Fixture Sans", new SettingsStore(new Paths()).LoadDesk().Font);   // Settings' stores live in the dev profile
    });

    [Fact]
    public void The_companion_skips_a_sprite_that_is_off_and_returns_to_it() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-sprite");
        InstalledPackKit.Install(paths, "good-sprite-stages");
        var store = new SettingsStore(paths);
        store.SaveCompanion(new CompanionSettings("good-sprite-stages", true));
        var companion = new DeskCompanionViewModel(store, () => InstalledPackKit.Store(paths));
        companion.Reload();
        Assert.Equal("good-sprite-stages", companion.SelectedCompanion!.Id);

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-sprite-stages", false).Ok);
        companion.Reload();
        Assert.Equal(["good-sprite"], companion.Companions.Select(c => c.Id));
        Assert.Equal("good-sprite", companion.SelectedCompanion!.Id);
        Assert.Equal("good-sprite-stages", store.LoadCompanion().Pack);   // the choice is kept

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-sprite-stages", true).Ok);
        companion.Reload();
        Assert.Equal("good-sprite-stages", companion.SelectedCompanion!.Id);
    });

    [Fact]
    public void FontListSync_moves_and_trims_in_place()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<string>(["a", "b", "c", "d"]);
        var seen = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => seen.Add(e.Action);

        FontListSync.Sync(list, ["a", "c", "e", "d"]);

        Assert.Equal(["a", "c", "e", "d"], list);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, seen);
    }

    [Fact]
    public void The_page_tells_the_windows_pickers_when_a_pack_changes() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        int asked = 0;
        svm.PickerFonts = () => { asked++; return ["EsteFont Pro"]; };
        var page = new ModsPageViewModel();
        svm.ModsPage = page;

        page.SettingsChanged!.Invoke();

        Assert.Equal(1, asked);
    });

    [Fact]
    public void Themes_counts_the_theme_packs_and_follows_a_change() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro"];
        var paths = ScratchPaths();
        svm.Themes.Widgets = () => InstalledPackKit.Store(paths);   // the scratch widgets folder
        svm.PacksChanged();
        Assert.Equal(SettingsNames.ThemePacksLine(0), svm.Themes.ThemePacksLine);

        InstalledPackKit.Install(paths, "good-theme");
        svm.PacksChanged();
        Assert.Equal(SettingsNames.ThemePacksLine(1), svm.Themes.ThemePacksLine);
        Assert.Equal("mods#mods", svm.Themes.PacksLink);
        Assert.Equal(SettingsNames.LinkTitle(SettingsPage.Mods), svm.Themes.PacksLinkTitle);

        // Owner-confirmed: a pack that is switched off still counts.
        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-theme", false).Ok);
        svm.PacksChanged();
        Assert.Equal(SettingsNames.ThemePacksLine(1), svm.Themes.ThemePacksLine);
    });
}
