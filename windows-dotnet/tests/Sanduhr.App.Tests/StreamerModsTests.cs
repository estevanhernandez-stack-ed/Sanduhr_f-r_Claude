using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Mods &amp; Widgets' rows and the Desk's companion picker (spec, Reach inside Settings, Mods &amp; Widgets;
/// CPs 13, 14, 24). Catalog packs are public and keep their names; plugins, marketplaces and packs made here are masked.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerModsTests : AppWindowFixture
{
    private static InstalledItem Plugin(string name, string marketplace) =>
        new(name + "@" + marketplace, name, InstalledKind.ClaudeCodePlugin, null, marketplace, "1.0.0", null, [], true, SwitchRoute.None,
            SettingsNames.ReadOnlyClaudeCode, false, false, $"{name} does things for {marketplace}.");

    private static InstalledItem Pack(string name, bool madeHere) =>
        new("pack." + name, name, InstalledKind.WidgetPack, "sprite", madeHere ? SettingsNames.WidgetMadeHere : SettingsNames.SourceFromCatalog,
            "1.0.0", null, null, true, SwitchRoute.PackEnabled, null, madeHere, false, "A sprite.");

    [Fact]
    public void A_plugin_row_masks_its_name_marketplace_and_description_and_says_so_everywhere() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var row = InstalledRowViewModel.ForTests(Plugin("qxplugin", "qxmarket"));
        foreach (var (what, text) in new[]
        {
            ("name", row.Name), ("source", row.Source), ("detail", row.Detail), ("description", row.Description),
            ("spoken", row.RowName), ("where", row.WhereLinkName), ("uninstall", row.UninstallName),
        })
            Assert.False(text.Contains("qx", StringComparison.OrdinalIgnoreCase), $"{what}: \"{text}\"");
        Assert.False(row.HasDescription);
        Assert.True(row.CanShowRealName);
    });

    [Fact]
    public void A_pack_made_here_is_masked_and_a_catalog_pack_keeps_its_name() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var mine = InstalledRowViewModel.ForTests(Pack("Qxsprite", madeHere: true));
        Assert.DoesNotContain("Qxsprite", mine.Name + mine.SwitchName + mine.UseOwnArtName + mine.RowName);
        var catalog = InstalledRowViewModel.ForTests(Pack("Hourglass", madeHere: false));
        Assert.Equal("Hourglass", catalog.Name);
        Assert.False(catalog.CanShowRealName);
    });

    [Fact]
    public void Sanduhrs_own_mod_keeps_its_name() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        Assert.Equal("sanduhr-meters@inline", StreamerMode.Mask.AddOn("sanduhr-meters@inline"));
    });

    [Fact]
    public void A_rows_quick_route_shows_its_names() => AppHost.Run(() =>
    {
        var book = CodenameBook.New();
        StreamerMode.Use(new StreamerMask(true, book, Profile));
        IReadOnlyList<string>? shown = null;
        var row = InstalledRowViewModel.ForTests(Plugin("qxplugin", "qxmarket"), names => shown = names);
        row.ShowRealNameCommand.Execute(null);
        Assert.Equal(["qxplugin", "qxmarket"], shown);
    });

    [Fact]
    public void The_companion_picker_masks_sprites_made_here() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.AddOn, "Qxsprite"), DeskCompanionViewModel.ChoiceName("Qxsprite", madeHere: true));
        Assert.Equal("Hourglass", DeskCompanionViewModel.ChoiceName("Hourglass", madeHere: false));
    });

    [Fact]
    public void A_meters_status_line_shows_the_folder_by_codename() => AppHost.Run(() =>
    {
        Home(".claude-qxwork");
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
            var page = new ModsPageViewModel(host: ClaudeCodeHost.For(widget));
            var line = page.ShortForTests($@"Couldn't update {Profile}\.claude-qxwork\settings.json", "fallback");
            Assert.DoesNotContain("qx", line, StringComparison.OrdinalIgnoreCase);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Toggling_a_masked_packs_switch_acts_on_its_real_id_and_the_status_line_keeps_the_codename() => AppHost.Run(() =>
    {
        var paths = new Paths(Path.Combine(Root, "appdata"), Path.Combine(Root, "home"), Path.Combine(Root, "local"));
        var svc = new WidgetCatalogService(paths, new OfflineHandler(), WidgetKit.V380);
        var files = WidgetKit.Pack("good-palette");
        Assert.True(svc.Store.Install(files, PackValidator.Validate(files, "good-palette", strict: true).Manifest!, WidgetSource.Local).Ok);
        var page = new ModsPageViewModel(() => svc);
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        page.ReloadLocal();
        var row = Assert.Single(page.Installed);
        Assert.Equal("good-palette", row.Id);
        Assert.DoesNotContain("Fixture", row.Name);
        bool before = svc.Store.Load().Find("good-palette")!.Enabled;
        row.ToggleCommand.Execute(null);
        Assert.Equal(!before, svc.Store.Load().Find("good-palette")!.Enabled);
        Assert.DoesNotContain("Fixture", page.Status);
        Assert.DoesNotContain("Fixture", row.RowName + row.SwitchName + row.UninstallName + row.UseOwnArtName);
    });
    /// <summary>The filter box matches what the row shows (final review M7): while on, typing the codename finds the row, and typing the
    /// real name still does (the person's own typing, on their own list).</summary>
    [Fact]
    public void The_filter_also_matches_the_codename_the_row_shows() => AppHost.Run(() =>
    {
        var paths = new Paths(Path.Combine(Root, "appdata"), Path.Combine(Root, "home"), Path.Combine(Root, "local"));
        var svc = new WidgetCatalogService(paths, new OfflineHandler(), WidgetKit.V380);
        var files = WidgetKit.Pack("good-palette");
        Assert.True(svc.Store.Install(files, PackValidator.Validate(files, "good-palette", strict: true).Manifest!, WidgetSource.Local).Ok);
        var page = new ModsPageViewModel(() => svc);
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        page.ReloadLocal();
        var shown = Assert.Single(page.Installed).Name;
        Assert.DoesNotContain("Fixture", shown);

        page.FilterText = shown;
        Assert.Single(page.Installed);
        page.FilterText = shown.ToUpperInvariant();
        Assert.Single(page.Installed);
        page.FilterText = "good-palette";
        Assert.Single(page.Installed);
        page.FilterText = "nothing is called this";
        Assert.Empty(page.Installed);
    });
}
