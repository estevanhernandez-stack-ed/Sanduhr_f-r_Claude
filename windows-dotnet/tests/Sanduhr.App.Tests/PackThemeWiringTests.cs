using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Installs one of the shared good fixture packs into a widgets folder, the way Settings does.</summary>
internal static class InstalledPackKit
{
    public static WidgetStore Store(Paths paths) => new(paths.WidgetsDir, WidgetCatalogService.EncodePng);

    public static void Install(Paths paths, string fixture, WidgetSource source = WidgetSource.Catalog)
    {
        // Never the real %LOCALAPPDATA%: a test that forgets to scratch the paths must fail, not install into the user's widgets.
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(paths.WidgetsDir), StringComparison.OrdinalIgnoreCase);
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", fixture);
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes, StringComparer.Ordinal);
        var v = PackValidator.Validate(files, fixture, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        var r = Store(paths).Install(files, v.Manifest!, source);
        Assert.True(r.Ok, r.Error);
    }
}

/// <summary>
/// 3.9.1: a theme from an installed pack is in the widget's theme list, and removing the theme in use (its pack uninstalled, or
/// a user theme file deleted) switches to Obsidian inside the reload, not at the next launch.
/// </summary>
[Collection("SettingsWindow")]
public sealed class PackThemeWiringTests : AppWindowFixture
{
    private const string PackKey = "good-theme:fixture-dark";

    [Fact]
    public void An_installed_theme_pack_is_in_the_theme_strip_after_a_reload() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        Assert.DoesNotContain(widget.Themes, t => t.Key == PackKey);

        InstalledPackKit.Install(new Paths(), "good-theme");
        widget.ReloadUserThemes();

        Assert.Contains(widget.Themes, t => t.Key == PackKey);
        Assert.Equal("Fixture Dark", widget.GetThemeDefinition(PackKey)!.Name);
    });

    [Fact]
    public void Removing_the_pack_whose_theme_is_in_use_switches_to_obsidian_at_once() => AppHost.Run(() =>
    {
        var paths = new Paths();
        InstalledPackKit.Install(paths, "good-theme");
        var widget = DemoWidget();
        widget.ApplyThemeByKey(PackKey);
        Assert.Equal(PackKey, widget.ActiveThemeKey);

        Assert.True(InstalledPackKit.Store(paths).Uninstall("good-theme").Ok);
        widget.ReloadUserThemes();

        Assert.Equal("obsidian", widget.ActiveThemeKey);
        Assert.DoesNotContain(widget.Themes, t => t.Key == PackKey);
        Assert.Equal("obsidian", Assert.Single(widget.Themes, t => t.IsActive).Key);
        // Shown, not saved: a pack that reads as gone for a moment (a failed read) must not rewrite the choice. Next launch resolves it again.
        Assert.Equal(PackKey, new SettingsStore(paths).LoadTheme());
    });

    [Fact]
    public void Deleting_the_user_theme_file_in_use_switches_to_obsidian_at_once() => AppHost.Run(() =>
    {
        var paths = new Paths();
        Directory.CreateDirectory(paths.ThemesDir);
        var dusk = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        dusk["name"] = "Dusk";
        string file = Path.Combine(paths.ThemesDir, "dusk.json");
        File.WriteAllText(file, dusk.ToJsonString());
        var widget = DemoWidget();
        widget.ApplyThemeByKey("dusk");
        Assert.Equal("dusk", widget.ActiveThemeKey);

        File.Delete(file);
        widget.ReloadUserThemes();

        Assert.Equal("obsidian", widget.ActiveThemeKey);
        Assert.Equal("obsidian", Assert.Single(widget.Themes, t => t.IsActive).Key);
        Assert.Equal("dusk", new SettingsStore(paths).LoadTheme());
    });

    [Fact]
    public void The_pack_theme_comes_back_when_the_pack_reads_again() => AppHost.Run(() =>
    {
        var paths = new Paths();
        InstalledPackKit.Install(paths, "good-theme");
        var widget = DemoWidget();
        widget.ApplyThemeByKey(PackKey);
        var packDir = InstalledPackKit.Store(paths).PackDir("good-theme");
        string moved = packDir + "-away";
        Directory.Move(packDir, moved);
        widget.ReloadUserThemes();
        Assert.Equal("obsidian", widget.ActiveThemeKey);

        Directory.Move(moved, packDir);
        widget.ReloadUserThemes();
        Assert.Contains(widget.Themes, t => t.Key == PackKey);
        widget.ApplyThemeByKey(new SettingsStore(paths).LoadTheme());

        Assert.Equal(PackKey, widget.ActiveThemeKey);
    });

    [Fact]
    public void Keeping_the_theme_in_use_leaves_it_alone_on_a_reload() => AppHost.Run(() =>
    {
        var paths = new Paths();
        InstalledPackKit.Install(paths, "good-theme");
        var widget = DemoWidget();
        widget.ApplyThemeByKey(PackKey);

        widget.ReloadUserThemes();

        Assert.Equal(PackKey, widget.ActiveThemeKey);
        Assert.Equal(PackKey, Assert.Single(widget.Themes, t => t.IsActive).Key);
    });
}
