using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Follow the widget theme" and "Match now", over a scratch widget and its own themes folder: a built-in or a pack theme is
/// never edited; a theme of the person's asks add-or-new; Cancel writes nothing; Follow off and Match now leave the look as the Desk's own;
/// the Desk is saved first so Put my Desk back returns both sides. The prompts are replaced; one that should not be asked throws.
/// </summary>
[Collection("SettingsWindow")] // the fixture swaps process-wide statics (SANDUHR_DEV_PROFILE, the widget catalog handler)
public sealed class ThemeMatchServiceTests : AppWindowFixture
{
    /// <summary>The App's host in one line: after a design is written the widget reloads its themes and selects the design's.</summary>
    private sealed class ReloadingHost(WidgetViewModel widget) : IDeskDesignHost
    {
        public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss) { }
        public void EndPreview() { }
        public void Applied(string? themeKey)
        {
            widget.ReloadUserThemes();
            if (themeKey is not null) widget.ApplyThemeByKey(themeKey);
        }
    }

    private sealed record Rig(ThemeMatchService Service, WidgetViewModel Widget, SettingsStore Store, DeskDesignService Designs, Paths Paths) : IDisposable
    {
        public void Dispose() => Designs.Dispose();
    }

    private Rig Make()
    {
        var paths = ScratchPaths();
        var (widget, _) = ScratchWidget("Personal");
        var store = new SettingsStore(paths);
        var designs = new DeskDesignService(paths, store);
        var service = new ThemeMatchService(store, widget, () => designs)
        {
            AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new"),
        };
        return new Rig(service, widget, store, designs, paths);
    }

    /// <summary>A theme of the person's, "Dusk": Aurora's colors (border tint, inner highlight, bloom) under another name, made active.</summary>
    private static string AddDusk(Rig r)
    {
        var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk", Font = "Segoe UI" };
        Directory.CreateDirectory(r.Paths.ThemesDir);
        string path = Path.Combine(r.Paths.ThemesDir, "dusk.json");
        File.WriteAllText(path, ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        r.Widget.ReloadUserThemes();
        r.Widget.ApplyThemeByKey("dusk");
        return path;
    }

    [Fact]
    public void Follow_on_a_built_in_makes_Theme_plus_Desk_and_follows_it() => AppHost.Run(() =>
    {
        using var r = Make();
        var result = r.Service.FollowOn();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.FollowingTheme("Obsidian + Desk"), result.Message);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);
        var made = r.Widget.GetThemeDefinition("obsidian-desk")!;
        Assert.Equal("Obsidian + Desk", made.Name);
        Assert.Equal(["#e8e4dc", "#6c63ff"], made.Desk!.Ink);
        Assert.Null(made.Desk.Bars);   // the widget keeps its ramp
        Assert.True(r.Store.LoadDeskFollowsTheme());
        Assert.True(r.Designs.CanPutBack);
        Assert.False(File.Exists(Path.Combine(r.Paths.ThemesDir, "obsidian.json")));   // the built-in is never edited
    });

    [Fact]
    public void Follow_on_never_edits_a_pack_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        InstalledPackKit.Install(r.Paths, "good-theme");
        r.Widget.ReloadUserThemes();
        r.Widget.ApplyThemeByKey("good-theme:fixture-dark");
        Assert.True(r.Service.FollowOn().Ok);   // no question: a pack's theme is not the person's to edit
        Assert.DoesNotContain(':', r.Widget.ActiveThemeKey);
        Assert.True(File.Exists(Path.Combine(r.Paths.ThemesDir, r.Widget.ActiveThemeKey + ".json")));
    });

    [Fact]
    public void Add_writes_the_desk_into_the_user_theme_keeping_every_other_field() => AppHost.Run(() =>
    {
        using var r = Make();
        AddDusk(r);
        var before = r.Widget.GetThemeDefinition("dusk")!;
        string? asked = null;
        r.Service.AskAddOrNew = name => { asked = name; return ThemeAddChoice.AddToTheme; };

        Assert.True(r.Service.FollowOn().Ok);

        Assert.Equal("Dusk", asked);
        Assert.Equal("dusk", r.Widget.ActiveThemeKey);
        var after = r.Widget.GetThemeDefinition("dusk")!;
        Assert.NotNull(after.Desk);
        Assert.Equal(before with { Desk = after.Desk }, after);
        Assert.True(r.Store.LoadDeskFollowsTheme());
    });

    [Fact]
    public void Make_a_new_theme_leaves_the_user_theme_alone() => AppHost.Run(() =>
    {
        using var r = Make();
        string path = AddDusk(r);
        byte[] bytes = File.ReadAllBytes(path);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.NewTheme;

        Assert.True(r.Service.FollowOn().Ok);

        Assert.Equal("dusk-desk", r.Widget.ActiveThemeKey);
        Assert.Equal("Dusk + Desk", r.Widget.GetThemeDefinition("dusk-desk")!.Name);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    });

    [Fact]
    public void Cancel_writes_nothing_and_Follow_stays_off() => AppHost.Run(() =>
    {
        using var r = Make();
        AddDusk(r);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.Cancel;

        var result = r.Service.FollowOn();

        Assert.False(result.Ok);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(["dusk.json"], Directory.GetFiles(r.Paths.ThemesDir).Select(f => Path.GetFileName(f)));
        Assert.False(r.Designs.CanPutBack);
        Assert.Equal("dusk", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void Follow_off_keeps_the_look_as_the_desks_own() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        var result = r.Service.FollowOff();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.KeptAsOwnLook, result.Message);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(new DeskInkSettings(DeskInkMode.Custom, "e8e4dc,6c63ff", true), r.Store.LoadDeskInk());
        Assert.True(r.Store.LoadDeskPlate());
        Assert.Equal("6c63ff,e8e4dc", r.Store.LoadDeskMessage().Color);
        Assert.True(r.Store.LoadNowPlaying().BarsGradient);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);   // the theme stays where it is
    });

    [Fact]
    public void Match_now_copies_once_saves_first_and_never_follows() => AppHost.Run(() =>
    {
        using var r = Make();
        var result = r.Service.MatchNow();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.MatchedTheme("Obsidian"), result.Message);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(DeskInkMode.Custom, r.Store.LoadDeskInk().Mode);
        Assert.Equal("obsidian", r.Widget.ActiveThemeKey);
        Assert.False(Directory.Exists(r.Paths.ThemesDir) && Directory.GetFiles(r.Paths.ThemesDir).Length > 0);   // no theme is made
        Assert.Equal("Before matching Obsidian", Assert.Single(r.Designs.Designs()).Name);
    });

    [Fact]
    public void Put_my_Desk_back_after_Follow_returns_both_sides() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        Assert.True(r.Designs.PutBack().Ok);
        Assert.Equal("obsidian", r.Store.LoadTheme());
        Assert.False(r.Store.LoadDeskFollowsTheme());
    });

    private static string AddPlatelessDusk(Rig r)
    {
        var dusk = ThemeCatalog.BuiltIns["aurora"] with
        {
            Name = "Dusk",
            Desk = new ThemeDesk { Plate = false, Ink = ["#ffffff"] },
        };
        Directory.CreateDirectory(r.Paths.ThemesDir);
        File.WriteAllText(Path.Combine(r.Paths.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        r.Widget.ReloadUserThemes();
        r.Widget.ApplyThemeByKey("dusk");
        return "dusk";
    }

    [Fact]
    public void Match_now_turns_the_plate_on_when_the_ink_would_not_read_and_says_so() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.WallpaperLuminance = () => 0.9;
        var result = r.Service.MatchNow();
        Assert.True(result.Ok);
        Assert.True(r.Store.LoadDeskPlate());
        Assert.EndsWith(SettingsNames.ThemeMatchPlateNote, result.Message);
    });

    [Fact]
    public void Match_now_leaves_the_plate_off_when_it_reads_and_adds_no_note() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.WallpaperLuminance = () => 0.05;
        var result = r.Service.MatchNow();
        Assert.False(r.Store.LoadDeskPlate());
        Assert.Equal(SettingsNames.MatchedTheme("Dusk"), result.Message);
    });

    [Fact]
    public void Follow_off_keeps_a_plate_the_guard_was_holding_on() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.AddToTheme;
        Assert.True(r.Service.FollowOn().Ok);
        r.Service.PlateForced = () => true;
        Assert.True(r.Service.FollowOff().Ok);
        Assert.True(r.Store.LoadDeskPlate());
    });

    [Fact]
    public void Follow_off_without_a_forced_plate_follows_the_theme_plate() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.AddToTheme;
        r.Service.FollowOn();
        r.Service.FollowOff();
        Assert.False(r.Store.LoadDeskPlate());
    });

    [Fact]
    public void Put_my_Desk_back_after_Add_restores_the_old_theme_as_dusk_2_and_leaves_the_edited_one() => AppHost.Run(() =>
    {
        using var r = Make();
        string path = AddDusk(r);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.AddToTheme;
        Assert.True(r.Service.FollowOn().Ok);
        r.Designs.Host = new ReloadingHost(r.Widget);   // what the App does after a design is written
        Assert.True(r.Designs.PutBack().Ok);
        Assert.Equal("dusk-2", r.Widget.ActiveThemeKey);
        Assert.Equal("Dusk", r.Widget.GetThemeDefinition("dusk-2")!.Name);
        Assert.Contains("\"desk\"", File.ReadAllText(path));
        Assert.False(r.Store.LoadDeskFollowsTheme());
    });

    [Fact]
    public void A_reserved_device_name_is_taken_as_a_theme_key() => AppHost.Run(() =>
    {
        using var r = Make();
        Assert.Equal("con-2", ThemeMatch.NewThemeKey("Con", r.Service.KeyTaken));
        Assert.Equal("lpt1-2", ThemeMatch.NewThemeKey("LPT1", r.Service.KeyTaken));
        Assert.Equal("nul-2", ThemeMatch.NewThemeKey("nul", r.Service.KeyTaken));
        Assert.Equal("console", ThemeMatch.NewThemeKey("Console", r.Service.KeyTaken));
    });

    [Fact]
    public void Saving_before_a_change_after_Dispose_writes_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Designs.Dispose();
        Assert.False(r.Designs.SaveBeforeChange("late"));
        Assert.False(r.Designs.CanPutBack);
    });
}
