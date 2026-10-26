using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 4: a widget pack's on switch is widgets.json's existing "enabled" field, and every consumer honors it (spec,
/// Mods & Widgets: "Every consumer must honor it: theme list, ink presets, font lists, companion"). One test per Core consumer.
/// </summary>
public sealed class PackEnabledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-packon-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng,
        () => new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private static void Install(WidgetStore store, string name, WidgetSource source = WidgetSource.Catalog)
    {
        var files = WidgetPackFixtures.Load("good", name);
        var v = PackValidator.Validate(files, name, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        Assert.True(store.Install(files, v.Manifest!, source).Ok);
    }

    [Fact]
    public void SetEnabled_writes_only_that_rows_flag_and_reads_back()
    {
        var store = Store();
        Install(store, "good-palette");
        Install(store, "good-theme");

        Assert.True(store.SetEnabled("good-palette", false).Ok);

        var reg = store.Load();
        Assert.False(reg.Find("good-palette")!.Enabled);
        Assert.True(reg.Find("good-theme")!.Enabled);
        Assert.Contains("\"enabled\": false", File.ReadAllText(store.RegistryFile));

        Assert.True(store.SetEnabled("good-palette", true).Ok);
        Assert.True(store.Load().Find("good-palette")!.Enabled);
    }

    [Fact]
    public void SetEnabled_to_the_same_value_writes_nothing()
    {
        var store = Store();
        Install(store, "good-palette");
        var stamped = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(store.RegistryFile, stamped);

        Assert.True(store.SetEnabled("good-palette", true).Ok);

        Assert.Equal(stamped, File.GetLastWriteTimeUtc(store.RegistryFile));
    }

    [Theory]
    [InlineData("not-installed")]
    [InlineData("..\\evil")]
    [InlineData("")]
    public void SetEnabled_refuses_an_id_that_is_not_an_installed_pack(string id)
    {
        var store = Store();
        Install(store, "good-palette");
        var json = File.ReadAllText(store.RegistryFile);

        var r = store.SetEnabled(id, false);

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.Equal(json, File.ReadAllText(store.RegistryFile));
    }

    [Fact]
    public void An_update_keeps_the_switch_off()
    {
        var store = Store();
        Install(store, "good-palette");
        Assert.True(store.SetEnabled("good-palette", false).Ok);

        Install(store, "good-palette");   // same source again: an update

        Assert.False(store.Load().Find("good-palette")!.Enabled);
    }

    [Fact]
    public void A_theme_pack_that_is_off_leaves_the_theme_list()
    {
        var store = Store();
        Install(store, "good-theme");
        var themes = Path.Combine(_root, "themes");
        Assert.Contains("good-theme:fixture-dark", PackThemes.Merged(themes, store).Keys);

        store.SetEnabled("good-theme", false);

        Assert.DoesNotContain("good-theme:fixture-dark", PackThemes.Merged(themes, store).Keys);
    }

    [Fact]
    public void A_palette_pack_that_is_off_leaves_the_ink_presets()
    {
        var store = Store();
        Install(store, "good-palette");
        Assert.Equal(DeskInkPresets.All.Count + 1, PackInkPresets.Merged(store).Count);

        store.SetEnabled("good-palette", false);

        Assert.Equal(DeskInkPresets.All.Select(p => p.Name), PackInkPresets.Merged(store).Select(p => p.Name));
    }

    [Fact]
    public void A_font_pack_that_is_off_leaves_the_font_folders_and_comes_back_on()
    {
        var store = Store();
        Install(store, "good-font");
        Assert.Single(PackFonts.Folders(store));

        store.SetEnabled("good-font", false);
        Assert.Empty(PackFonts.Folders(store));

        store.SetEnabled("good-font", true);
        Assert.Equal("good-font", Assert.Single(PackFonts.Folders(store)).PackId);
    }

    [Fact]
    public void ActivePack_skips_a_sprite_that_is_off()
    {
        var store = Store();
        Install(store, "good-sprite");
        Install(store, "good-sprite-stages");
        string? Kind(string id) => store.ReadInfo(id)?.Kind;
        var chosen = new CompanionSettings("good-sprite-stages", true);
        Assert.Equal("good-sprite-stages", chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite-stages", false);
        Assert.Equal("good-sprite", chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite", false);
        Assert.Null(chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite-stages", true);
        Assert.Equal("good-sprite-stages", chosen.ActivePack(store.Load(), Kind));
    }
}
