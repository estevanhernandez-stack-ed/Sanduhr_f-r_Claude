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
            AskWhere = (_, _) => throw new Xunit.Sdk.XunitException("asked where"),
            AskName = _ => throw new Xunit.Sdk.XunitException("asked a name"),
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
        r.Service.WallpaperLuminance = () => [0.9];
        var result = r.Service.MatchNow();
        Assert.True(result.Ok);
        Assert.True(r.Store.LoadDeskPlate());
        Assert.EndsWith(SettingsNames.ThemeMatchPlateNote, result.Message);
    });

    [Fact]
    public void Match_now_turns_the_plate_on_when_only_one_of_two_screens_would_not_read() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.WallpaperLuminance = () => [0.05, 0.9];
        var result = r.Service.MatchNow();
        Assert.True(r.Store.LoadDeskPlate());
        Assert.EndsWith(SettingsNames.ThemeMatchPlateNote, result.Message);
    });

    [Fact]
    public void Match_now_leaves_the_plate_off_when_it_reads_and_adds_no_note() => AppHost.Run(() =>
    {
        using var r = Make();
        AddPlatelessDusk(r);
        r.Service.WallpaperLuminance = () => [0.05];
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

    // ---- a hand edit while following ----

    private static ThemeDesk Edited(ThemeDefinition def) => ThemeMatch.SpelledOut(def) with { Ink = ["#ffffff"] };

    [Fact]
    public void A_hand_edit_on_a_built_in_follow_offers_only_a_new_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Store.SaveDeskFollowsTheme(true);   // following while the built-in itself is active (a Desk from a design, or an older build)
        bool? offered = null;
        r.Service.AskWhere = (_, canUpdate) => { offered = canUpdate; return ThemeEditChoice.NewTheme; };
        r.Service.AskName = suggested => { Assert.Equal("Obsidian + Desk", suggested); return "Obsidian Neon"; };

        var result = r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"]));

        Assert.True(result.Ok);
        Assert.False(offered);
        Assert.Equal("obsidian-neon", r.Widget.ActiveThemeKey);
        Assert.Equal(["#ffffff"], r.Widget.GetThemeDefinition("obsidian-neon")!.Desk!.Ink);
        Assert.False(File.Exists(Path.Combine(r.Paths.ThemesDir, "obsidian.json")));
    });

    [Fact]
    public void Update_writes_the_edit_into_the_active_user_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();   // obsidian-desk, a theme of the person's now
        var before = r.Widget.GetThemeDefinition("obsidian-desk")!;
        bool? offered = null;
        r.Service.AskWhere = (name, canUpdate) => { Assert.Equal("Obsidian + Desk", name); offered = canUpdate; return ThemeEditChoice.UpdateTheme; };

        var result = r.Service.Edit(Edited(before));

        Assert.True(result.Ok);
        Assert.True(offered);
        Assert.Equal(SettingsNames.ThemeUpdated("Obsidian + Desk"), result.Message);
        var after = r.Widget.GetThemeDefinition("obsidian-desk")!;
        Assert.Equal(["#ffffff"], after.Desk!.Ink);
        Assert.Equal(before with { Desk = after.Desk }, after);
    });

    [Fact]
    public void Cancel_leaves_the_theme_as_it_was() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        string path = Path.Combine(r.Paths.ThemesDir, "obsidian-desk.json");
        byte[] bytes = File.ReadAllBytes(path);
        r.Service.AskWhere = (_, _) => ThemeEditChoice.Cancel;

        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    });

    [Fact]
    public void A_name_left_empty_saves_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        int files = Directory.GetFiles(r.Paths.ThemesDir).Length;
        r.Service.AskWhere = (_, _) => ThemeEditChoice.NewTheme;
        r.Service.AskName = _ => "   ";

        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);
        Assert.Equal(files, Directory.GetFiles(r.Paths.ThemesDir).Length);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void Not_following_an_edit_asks_nothing_and_does_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);   // AskWhere would throw
    });

    [Fact]
    public void A_typed_reserved_name_does_not_become_a_device_file() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Store.SaveDeskFollowsTheme(true);
        r.Service.AskWhere = (_, _) => ThemeEditChoice.NewTheme;
        r.Service.AskName = _ => "Con";

        Assert.True(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);
        Assert.Equal("con-2", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void A_theme_that_fails_lint_leaves_no_Before_design_and_no_file() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Store.SaveDeskFollowsTheme(true);
        r.Service.AskWhere = (_, _) => ThemeEditChoice.NewTheme;
        r.Service.AskName = _ => "Broken";
        var bad = Edited(ThemeCatalog.BuiltIns["obsidian"]) with { Ink = ["not-a-color"] };

        Assert.False(r.Service.Edit(bad).Ok);
        Assert.False(r.Designs.CanPutBack);
        Assert.False(File.Exists(Path.Combine(r.Paths.ThemesDir, "broken.json")));
        Assert.Equal("obsidian", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void A_target_that_already_exists_on_the_new_path_is_not_written_and_no_Before_design_is_left() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Store.SaveDeskFollowsTheme(true);
        Directory.CreateDirectory(Path.Combine(r.Paths.ThemesDir, "taken.json"));   // KeyTaken sees no file; the move would still have to refuse
        r.Service.AskWhere = (_, _) => ThemeEditChoice.NewTheme;
        r.Service.AskName = _ => "Taken";

        var result = r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"]));

        Assert.False(result.Ok);
        Assert.False(r.Designs.CanPutBack);
        Assert.Equal("obsidian", r.Widget.ActiveThemeKey);
        Assert.True(r.Store.LoadDeskFollowsTheme());
    });

    [Fact]
    public void Follow_on_and_Match_now_with_the_design_service_disposed_change_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Designs.Dispose();

        var on = r.Service.FollowOn();
        Assert.False(on.Ok);
        Assert.Equal(SettingsNames.CouldNotSaveDeskFirst, on.Message);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Empty(Directory.Exists(r.Paths.ThemesDir) ? Directory.GetFiles(r.Paths.ThemesDir) : []);

        var match = r.Service.MatchNow();
        Assert.False(match.Ok);
        Assert.Equal(SettingsNames.CouldNotSaveDeskFirst, match.Message);
        Assert.Empty(Directory.Exists(r.Paths.ThemesDir) ? Directory.GetFiles(r.Paths.ThemesDir) : []);
    });
}
