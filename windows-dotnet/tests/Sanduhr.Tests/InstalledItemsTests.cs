using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 4: the one list (spec, Mods &amp; Widgets; mods-parity §4). Built from data the App has already read, so these need no
/// disk: merge by id, the scope map, and the switch route per kind.
/// </summary>
public sealed class InstalledItemsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ModItem Plugin(string name, ModState state, string market = "m", bool isMod = false, string? version = "1.0.0") =>
        new(name, version, "", state == ModState.Missing ? null : @"C:\plugins\" + name, ModOriginKind.Installed, market,
            $"{name}@{market}", isMod, state, ModTouches.None, false);

    private static ModItem Meters(ModState state = ModState.On) =>
        new(ModInventory.SanduhrModName, "0.3.0", "", @"C:\x\sanduhr-meters", ModOriginKind.PluginDirs, null,
            ModInventory.SanduhrModName + "@inline", true, state, ModTouches.None, true);

    private static FolderReport Report(string folder, params ModItem[] mods) =>
        new(folder, true, SettingsState.Readable, null, [], new Dictionary<string, bool>(), mods,
            new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(30, false, null));

    private static OwnModState NoOwn(string name) => new(@"C:\home\" + name, true, null, null, null, null, false);
    private static OwnModState OwnOn(string name) => new(@"C:\home\" + name, true, @"C:\x\sanduhr-meters", "20261025", null, null, true);
    private static OwnModState OwnOff(string name) => new(@"C:\home\" + name, true, @"C:\x\sanduhr-meters", "20261025", false, true, true);

    private static FolderMods Folder(string name, OwnModState? own = null, IntegrationStatus meters = IntegrationStatus.NotInstalled,
        params ModItem[] mods) =>
        new(@"C:\home\" + name, @"~\" + name, Report(@"C:\home\" + name, mods), own ?? NoOwn(name), meters);

    private static WidgetRegistry Packs(params InstalledWidget[] rows) => new(null, rows);

    private static InstalledWidget Pack(string id, bool on = true, WidgetSource source = WidgetSource.Catalog, string version = "1.0.0") =>
        new(id, version, source, At, on);

    private static InstalledWidgetInfo? Info(string id) => id switch
    {
        "good-palette" => new("Fixture palette", "palette", "", null),
        "good-theme" => new("Fixture theme", "theme", "", null),
        "good-font" => new("Fixture font", "font", "", null),
        "my-sprite" => new("My sprite", "sprite", "", null),
        "good-effects" => new("Fixture effects", "effects", "", null),
        _ => null,
    };

    [Fact]
    public void A_plugin_in_two_folders_is_one_row_with_every_folder_in_its_scope()
    {
        var items = InstalledItems.Build(
        [
            Folder(".claude", mods: Plugin("alpha", ModState.On)),
            Folder(".claude-work", mods: Plugin("alpha", ModState.Off)),
            Folder(".claude-personal"),
        ], WidgetRegistry.Empty, Info, null);

        var alpha = Assert.Single(items);
        Assert.Equal(("alpha@m", "alpha", InstalledKind.ClaudeCodePlugin), (alpha.Id, alpha.Name, alpha.Kind));
        Assert.Equal(
            [(@"~\.claude", FolderState.On), (@"~\.claude-work", FolderState.Off), (@"~\.claude-personal", FolderState.NotInstalled)],
            alpha.Scope!.Select(s => (s.Display, s.State)));
        Assert.Equal("On in 1 of 3 folders", InstalledItems.ScopeSummary(alpha));
    }

    [Fact]
    public void A_plugin_switched_on_without_files_reads_files_missing_there()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: Plugin("ghost", ModState.Missing))], WidgetRegistry.Empty, Info, null);
        Assert.Equal(FolderState.Missing, Assert.Single(Assert.Single(items).Scope!).State);
        Assert.Equal("Files missing", InstalledItems.ScopeSummary(items[0]));
    }

    [Fact]
    public void Other_Claude_Code_items_are_read_only_and_say_why()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: [Plugin("alpha", ModState.On), Plugin("beta-mod", ModState.Off, isMod: true)])],
            WidgetRegistry.Empty, Info, null);

        Assert.All(items, i =>
        {
            Assert.False(i.CanSwitch);
            Assert.Equal(SwitchRoute.None, i.Route);
            Assert.Equal(SettingsNames.ReadOnlyClaudeCode, i.ReadOnlyReason);
        });
        Assert.Equal(InstalledKind.ClaudeCodeMod, items.Single(i => i.Name == "beta-mod").Kind);
        Assert.True(items.Single(i => i.Name == "beta-mod").IsMod);
    }

    [Fact]
    public void Sanduhrs_meters_mod_is_one_switchable_row_scoped_by_its_own_state()
    {
        var items = InstalledItems.Build(
        [
            Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters()),
            Folder(".claude-work", OwnOff(".claude-work"), IntegrationStatus.Installed, Meters(ModState.Off)),
            Folder(".claude-personal"),
        ], WidgetRegistry.Empty, Info, null);

        var meters = Assert.Single(items);
        Assert.Equal((InstalledItems.SanduhrModId, InstalledKind.SanduhrIntegration, SwitchRoute.OwnModReceipt),
            (meters.Id, meters.Kind, meters.Route));
        Assert.True(meters.CanSwitch);
        Assert.Null(meters.ReadOnlyReason);
        Assert.Equal(SettingsNames.MetersSection, meters.Name);
        Assert.Equal([FolderState.On, FolderState.Off, FolderState.NotInstalled], meters.Scope!.Select(s => s.State));
        Assert.Equal("On in 1 of 3 folders", InstalledItems.ScopeSummary(meters));
    }

    [Fact]
    public void Sanduhrs_mod_is_not_listed_while_no_folder_has_it()
    {
        Assert.Empty(InstalledItems.Build([Folder(".claude"), Folder(".claude-work")], WidgetRegistry.Empty, Info, null));
    }

    [Fact]
    public void A_folder_whose_settings_json_cant_be_read_says_so_in_the_scope()
    {
        var unreadable = new FolderMods(@"C:\home\.claude", @"~\.claude",
            Report(@"C:\home\.claude", Meters()) with { Settings = new SettingsState(SettingsKind.Unreadable, 3, 7, "bad") },
            new OwnModState(@"C:\home\.claude", false, null, null, null, null, true), IntegrationStatus.Unreadable);

        Assert.Equal(FolderState.Unreadable, InstalledItems.MetersState(unreadable));
        var meters = Assert.Single(InstalledItems.Build(
            [unreadable, Folder(".claude-work", OwnOn(".claude-work"), IntegrationStatus.Installed, Meters())], WidgetRegistry.Empty, Info, null));
        Assert.Equal([FolderState.Unreadable, FolderState.On], meters.Scope!.Select(s => s.State));
        Assert.Equal("Can't be read", InstalledItems.StateWord(FolderState.Unreadable));
    }

    [Fact]
    public void Meters_whose_files_are_gone_read_files_missing()
    {
        Assert.Equal(FolderState.Missing,
            InstalledItems.MetersState(Folder(".claude", OwnOn(".claude"), IntegrationStatus.Outdated, Meters(ModState.Missing))));
    }

    [Fact]
    public void Widget_packs_are_app_scoped_and_switch_through_their_enabled_flag()
    {
        var items = InstalledItems.Build([Folder(".claude")],
            Packs(Pack("good-palette"), Pack("my-sprite", on: false, source: WidgetSource.Claude)), Info, null);

        var palette = items.Single(i => i.Id == "good-palette");
        Assert.Equal((InstalledKind.WidgetPack, "palette", SwitchRoute.PackEnabled, true, false),
            (palette.Kind, palette.PackKind, palette.Route, palette.Enabled, palette.MadeHere));
        Assert.True(palette.AppScoped);
        Assert.Equal("", InstalledItems.ScopeSummary(palette));

        var sprite = items.Single(i => i.Id == "my-sprite");
        Assert.Equal((false, true, SettingsNames.WidgetMadeHere), (sprite.Enabled, sprite.MadeHere, sprite.Source));
        Assert.True(sprite.CanSwitch);
    }

    [Theory]
    [InlineData("good-palette", "palette")]
    [InlineData("good-theme", "theme")]
    [InlineData("good-font", "font")]
    [InlineData("my-sprite", "sprite")]
    [InlineData("good-effects", "effects")]
    public void Every_pack_kind_switches_through_enabled(string id, string kind)
    {
        var item = Assert.Single(InstalledItems.Build([], Packs(Pack(id)), Info, null));
        Assert.Equal((kind, SwitchRoute.PackEnabled), (item.PackKind, item.Route));
    }

    [Fact]
    public void A_pack_whose_pack_json_cant_be_read_is_listed_by_id_and_can_still_switch()
    {
        var item = Assert.Single(InstalledItems.Build([], Packs(Pack("broken-pack")), Info, null));
        Assert.Equal(("broken-pack", "", true), (item.Name, item.PackKind, item.CanSwitch));
    }

    [Fact]
    public void A_newer_catalog_version_is_merged_onto_the_installed_row_by_id()
    {
        var catalog = new WidgetCatalogDocument(At, "2026-10-25T12:00:00Z",
        [
            new WidgetCatalogEntry("good-palette", "Fixture palette", "palette", "1.1.0", "3.8.0", "https://x/p.zip", new string('a', 64), 10),
            new WidgetCatalogEntry("good-theme", "Fixture theme", "theme", "1.0.0", "3.8.0", "https://x/t.zip", new string('b', 64), 10),
            new WidgetCatalogEntry("my-sprite", "Not mine", "sprite", "9.0.0", "3.8.0", "https://x/s.zip", new string('c', 64), 10),
        ]);
        var items = InstalledItems.Build([], Packs(Pack("good-palette"), Pack("good-theme"), Pack("my-sprite", source: WidgetSource.Claude)),
            Info, catalog);

        Assert.Equal("1.1.0", items.Single(i => i.Id == "good-palette").Update);
        Assert.Null(items.Single(i => i.Id == "good-theme").Update);
        Assert.Null(items.Single(i => i.Id == "my-sprite").Update);
        Assert.Equal(3, items.Count);   // the catalog adds no row to the installed list
    }

    [Fact]
    public void The_list_reads_Sanduhrs_mod_then_packs_then_Claude_Code_each_by_name()
    {
        var items = InstalledItems.Build(
            [Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters(), Plugin("zeta", ModState.On), Plugin("alpha", ModState.On))],
            Packs(Pack("good-theme"), Pack("good-palette")), Info, null);
        Assert.Equal([SettingsNames.MetersSection, "Fixture palette", "Fixture theme", "alpha", "zeta"], items.Select(i => i.Name));
    }

    [Fact]
    public void With_one_folder_a_scope_reads_its_state_alone()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: Plugin("alpha", ModState.Off))], WidgetRegistry.Empty, Info, null);
        Assert.Equal("Off", InstalledItems.ScopeSummary(Assert.Single(items)));
    }
}
