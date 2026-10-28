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
        new(folder, true, SettingsState.Readable, null, [], mods.Where(m => m.Key is not null && m.State != ModState.Missing).ToDictionary(m => m.Key!, m => m.State == ModState.On), mods,
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

    private static ModItem WithTouches(ModItem m, ModTouches t) => m with { Touches = t };

    [Fact]
    public void A_claude_code_row_carries_what_it_touches_across_its_folders_and_says_it_in_one_line()
    {
        var band = new ModTouches([ModSurface.Band], [ModCapability.Process], []);
        var net = new ModTouches([ModSurface.Toast, ModSurface.Band], [ModCapability.Http], ["hi"]);
        var items = InstalledItems.Build(
        [
            Folder(".claude", mods: WithTouches(Plugin("alpha", ModState.On, isMod: true), band)),
            Folder(".claude-work", mods: WithTouches(Plugin("alpha", ModState.On, isMod: true), net)),
        ], WidgetRegistry.Empty, Info, null);

        var alpha = Assert.Single(items);
        Assert.Equal([ModSurface.Band, ModSurface.Toast], alpha.Touches!.Surfaces);
        Assert.Equal([ModCapability.Process, ModCapability.Http], alpha.Touches.Capabilities);
        Assert.Equal("Draws a band, toasts; can run programs, use the network.", alpha.TouchesLine);
    }

    [Fact]
    public void The_touches_line_is_the_old_pages_wording()
    {
        Assert.Equal("Draws a band; can run programs.", ModsPageText.DescribeTouches(new([ModSurface.Band], [ModCapability.Process], [])));
        Assert.Equal("Draws a pane, the status line, slash commands.",
            ModsPageText.DescribeTouches(new([ModSurface.Pane, ModSurface.Status, ModSurface.Commands], [], [])));
        Assert.Equal("Can read and write files.", ModsPageText.DescribeTouches(new([], [ModCapability.Files], [])));
        Assert.Equal("", ModsPageText.DescribeTouches(ModTouches.None));
    }

    [Fact]
    public void No_touches_line_for_a_pack_the_meters_mod_or_a_row_that_touches_nothing()
    {
        var band = new ModTouches([ModSurface.Band], [], []);
        var items = InstalledItems.Build(
        [
            Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, WithTouches(Meters(), band), Plugin("quiet", ModState.On)),
        ], Packs(Pack("good-palette")), Info, null);

        Assert.Equal(["", "", ""], items.Select(i => i.TouchesLine));
        Assert.Equal(3, items.Count);
    }

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
    public void A_plugin_on_in_settings_json_whose_files_are_gone_reads_files_missing_not_on()
    {
        // Task 12 review I1: ModInventory reports such a row On with no path (enabledPlugins says true, nothing is installed). The scope
        // agrees with the note under the list (ModsPageText.HasFiles): it is not loaded, so it is not on.
        var ghost = new ModItem("ghost", null, "", null, ModOriginKind.Installed, "m", "ghost@m", false, ModState.On, ModTouches.None, false);
        var items = InstalledItems.Build(
        [
            Folder(".claude", mods: ghost),
            Folder(".claude-work", mods: Plugin("ghost", ModState.On)),
        ], WidgetRegistry.Empty, Info, null);

        var row = Assert.Single(items);
        Assert.Equal([FolderState.Missing, FolderState.On], row.Scope!.Select(s => s.State));
        Assert.Equal("On in 1 of 2 folders", InstalledItems.ScopeSummary(row));

        var alone = InstalledItems.Build([Folder(".claude", mods: ghost)], WidgetRegistry.Empty, Info, null).Single();
        Assert.Equal(SettingsNames.FolderMissing, InstalledItems.ScopeSummary(alone));
        Assert.False(alone.Enabled);
        Assert.Equal(SettingsNames.NoFilesNote(1), ModsPageText.PlainNote([Folder(".claude", mods: ghost)]));
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

    // ---- the catalog merged by id, where it shows, the filters ----

    private static readonly Version Running = new(4, 0, 0, 0);

    private static WidgetCatalogDocument Catalog(params WidgetCatalogEntry[] entries) => new(At, "2026-10-25T12:00:00Z", entries);

    private static WidgetCatalogEntry Entry(string id, string kind, string version = "1.0.0", string min = "3.8.0") =>
        new(id, "Name of " + id, kind, version, min, $"https://x/{id}.zip", new string('a', 64), 10);

    [Fact]
    public void A_catalog_pack_appears_once_with_install_update_or_the_switch()
    {
        var catalog = Catalog(Entry("good-palette", "palette", "1.1.0"), Entry("good-theme", "theme"), Entry("good-font", "font"));
        var items = InstalledItems.Build([], Packs(Pack("good-palette"), Pack("good-theme", on: false)), Info, catalog);

        var rows = InstalledItems.CatalogRows(catalog, items, Running);

        Assert.Equal(["good-palette", "good-theme", "good-font"], rows.Select(r => r.Entry.Id));
        Assert.Equal((CatalogAction.Update, "Version 1.0.0 is installed."), (rows[0].Action, rows[0].State));
        Assert.Equal((CatalogAction.Switch, "Installed."), (rows[1].Action, rows[1].State));
        Assert.False(rows[1].Installed!.Enabled);
        Assert.Equal((CatalogAction.Install, ""), (rows[2].Action, rows[2].State));
        Assert.Null(rows[2].Installed);
    }

    [Fact]
    public void A_pack_above_the_running_version_offers_nothing_but_says_why()
    {
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-font", "font", min: "9.0.0")), [], Running);
        Assert.Equal((CatalogAction.None, "Needs Sanduhr 9.0.0 or later."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void An_update_above_the_running_version_keeps_the_switch_and_says_why()
    {
        var items = InstalledItems.Build([], Packs(Pack("good-palette")), Info, null);
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-palette", "palette", "2.0.0", min: "9.0.0")), items, Running);
        Assert.Equal((CatalogAction.Switch, "Version 1.0.0 is installed. 2.0.0 needs Sanduhr 9.0.0 or later."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void A_catalog_id_that_a_pack_made_here_holds_offers_nothing()
    {
        var items = InstalledItems.Build([], Packs(Pack("my-sprite", source: WidgetSource.Claude)), Info, null);
        var rows = InstalledItems.CatalogRows(Catalog(Entry("my-sprite", "sprite")), items, Running);
        Assert.Equal((CatalogAction.None, $"A widget {SettingsNames.WidgetMadeHere} already uses this id."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void The_catalog_can_show_one_kind()
    {
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-sprite", "sprite"), Entry("good-font", "font")), [], Running, kind: "sprite");
        Assert.Equal(["good-sprite"], rows.Select(r => r.Entry.Id));
    }

    [Theory]
    [InlineData("theme", "In Themes", "themes#gallery", "Themes settings…")]
    [InlineData("palette", "In Desk Look, Ink and color", "desklook#ink", "Desk Look settings…")]
    [InlineData("font", "In Desk Look, Fonts", "desklook#look", "Desk Look settings…")]
    [InlineData("sprite", "As the Desk companion", "desk#pieces", "Desk settings…")]
    public void Each_kind_says_where_it_shows_and_links_there(string kind, string text, string link, string title)
    {
        var where = InstalledItems.Where(kind);
        Assert.Equal((text, link, title), (where.Text, where.Link, where.LinkTitle));
        var place = SettingsIndex.ParseLink(where.Link)!;
        Assert.NotNull(place.Anchor);   // the link lands on its section, not a page top
    }

    [Fact]
    public void Effects_say_they_are_unused_and_link_nowhere()
    {
        Assert.Equal(new WhereItShows(SettingsNames.EffectsUnused, null, null), InstalledItems.Where("effects"));
        Assert.Equal(new WhereItShows("", null, null), InstalledItems.Where(null));
    }

    [Fact]
    public void The_filters_split_Claude_Code_from_widgets_and_the_text_matches_name_kind_or_source()
    {
        var items = InstalledItems.Build(
            [Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters(), Plugin("alpha", ModState.On))],
            Packs(Pack("good-palette"), Pack("my-sprite", source: WidgetSource.Claude)), Info, null);

        string[] Names(InstalledFilter f, string? text = null) =>
            [.. items.Where(i => InstalledItems.Matches(i, f, text)).Select(i => i.Name)];

        Assert.Equal(4, Names(InstalledFilter.All).Length);
        Assert.Equal([SettingsNames.MetersSection, "alpha"], Names(InstalledFilter.ClaudeCode));
        Assert.Equal(["Fixture palette", "My sprite"], Names(InstalledFilter.Widgets));
        Assert.Equal(["My sprite"], Names(InstalledFilter.All, "SPRITE"));
        Assert.Equal(["My sprite"], Names(InstalledFilter.All, "made here"));
        Assert.Equal(["alpha"], Names(InstalledFilter.ClaudeCode, "alp"));
        Assert.Empty(Names(InstalledFilter.Widgets, "alpha"));
        Assert.Equal(4, Names(InstalledFilter.All, "   ").Length);
    }

    [Fact]
    public void In_a_folder_whose_settings_cannot_be_read_a_plugin_is_unreadable_there_not_absent()
    {
        var unreadable = new FolderMods(@"C:\home\.claude-work", @"~\.claude-work",
            Report(@"C:\home\.claude-work") with { Settings = new SettingsState(SettingsKind.Unreadable, 3, 7, "bad") },
            NoOwn(".claude-work"), IntegrationStatus.NotInstalled);
        var items = InstalledItems.Build([Folder(".claude", mods: Plugin("alpha", ModState.On)), unreadable], WidgetRegistry.Empty, Info, null);

        var alpha = Assert.Single(items, i => i.Id == "alpha@m");
        Assert.Equal([FolderState.On, FolderState.Unreadable], alpha.Scope!.Select(s => s.State));
    }

    [Fact]
    public void A_plugin_in_a_folder_whose_settings_cant_be_read_reads_Unreadable_not_On()
    {
        // Slice 4 row 13: with settings.json unreadable, "no entry" means "unknown", not "on".
        var alpha = Plugin("a", ModState.On);
        var unreadable = new FolderMods(@"C:\home\.claude-work", @"~\.claude-work",
            Report(@"C:\home\.claude-work", alpha) with { Settings = new SettingsState(SettingsKind.Unreadable, 3, 7, "bad") },
            NoOwn(".claude-work"), IntegrationStatus.NotInstalled);
        var item = InstalledItems.Build([unreadable], WidgetRegistry.Empty, Info, null).Single(i => i.Id == "a@m");
        Assert.Equal(FolderState.Unreadable, item.Scope!.Single().State);
    }

    [Theory]
    [InlineData("mod", "alpha", true, true)]          // a mod is found by the word "mod"
    [InlineData("mo", "alpha", true, false)]          // but not by a piece of it
    [InlineData("mod", "model-tools", false, true)]   // a plugin is found by its own name
    [InlineData("mod", "beta", false, false)]
    public void Mod_is_a_word_not_a_substring(string filter, string name, bool isMod, bool matches)
    {
        var item = InstalledItems.Build([Folder(".claude", mods: Plugin(name, ModState.On, isMod: isMod))], WidgetRegistry.Empty, Info, null)
            .Single(i => i.Id == name + "@m");
        Assert.Equal(matches, InstalledItems.Matches(item, InstalledFilter.All, filter));
    }
}
