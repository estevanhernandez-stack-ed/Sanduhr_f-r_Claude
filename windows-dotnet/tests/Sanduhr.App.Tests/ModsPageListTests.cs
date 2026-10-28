using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: Mods &amp; Widgets' Installed list (spec, Mods &amp; Widgets, section 2; WF3, WF39). Over a scratch profile and widgets
/// folder, never the real ones and never the network.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ModsPageListTests : ModsPageFixture
{
    private string MetersEntry => Path.Combine(Root, "external", "mods", "v0.3.0-20261025120000", "sanduhr-meters").Replace("\\", "\\\\");

    private ModsPageViewModel Seeded()
    {
        var personal = Home(".claude-personal", "{\"enabledPlugins\":{\"alpha@m\":true,\"beta-mod@m\":false}}");
        Plugin(personal, "alpha", "1.2.0");
        Plugin(personal, "beta-mod", "0.1.0", mod: true);
        Home(".claude-work", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + MetersEntry + "\"}}");
        Put(Path.Combine(Root, "external", "mods", "v0.3.0-20261025120000", "sanduhr-meters"), ".claude-plugin/plugin.json",
            """{"name":"sanduhr-meters","version":"0.3.0"}""");   // the entry's files are there, so it reads On, not Files missing
        InstalledPackKit.Install(Paths, "good-palette");
        InstalledPackKit.Install(Paths, "good-theme");
        var page = Page();
        page.ReloadLocal();
        return page;
    }

    [Fact]
    public void One_list_holds_Sanduhrs_mod_the_packs_and_every_folders_plugins()
    {
        var page = Seeded();
        Assert.Equal([SettingsNames.MetersSection, "Fixture palette", "Fixture theme", "alpha", "beta-mod"], page.Installed.Select(r => r.Name));
        Assert.True(page.HasInstalled);
    }

    [Fact]
    public void Rows_show_only_controls_that_can_act()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.True(palette.IsPack);
        Assert.False(palette.IsReadOnly);
        Assert.True(palette.Enabled);

        var alpha = page.Installed.Single(r => r.Name == "alpha");
        Assert.True(alpha.IsReadOnly);
        Assert.False(alpha.IsPack);
        Assert.Equal(SettingsNames.ReadOnlyClaudeCode, alpha.ReadOnlyText);
        Assert.Equal("On in 1 of 2 folders", alpha.ScopeText);
        Assert.Equal("alpha, version 1.2.0, on in 1 of 2 folders, read-only", alpha.RowName);

        var meters = page.Installed.Single(r => r.IsSanduhrMod);
        Assert.False(meters.IsReadOnly);
        Assert.False(meters.IsPack);
        Assert.Equal("On in 1 of 2 folders", meters.ScopeText);
        Assert.Equal("mods#meters", meters.MetersLink);
        Assert.Equal(SettingsNames.MetersLinkUp, meters.MetersLinkTitle);
    }

    [Fact]
    public void A_pack_says_where_it_shows_and_links_there()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.Equal((SettingsNames.ShowsInInk, "desklook#ink", "Desk Look settings…"), (palette.WhereText, palette.WhereLink, palette.WhereLinkTitle));
        Assert.StartsWith(palette.WhereLinkTitle, palette.WhereLinkName);
        Assert.True(palette.HasWhereLink);
        Assert.False(page.Installed.Single(r => r.Name == "alpha").HasWhere);
    }

    [Fact]
    public void Switching_a_pack_writes_its_flag_shows_the_truth_and_tells_the_pickers()
    {
        var page = Seeded();
        int told = 0;
        page.SettingsChanged = () => told++;
        var palette = page.Installed.Single(r => r.Id == "good-palette");

        palette.ToggleCommand.Execute(null);

        Assert.False(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.False(palette.Enabled);
        Assert.EndsWith(", off", palette.RowName);
        Assert.Equal("Fixture palette is off.", page.Status);
        Assert.Equal(1, told);

        palette.ToggleCommand.Execute(null);
        Assert.True(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.Equal(2, told);
    }

    [Fact]
    public void A_switch_that_cant_write_says_why_and_shows_what_is_on_disk()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.True(InstalledPackKit.Store(Paths).Uninstall("good-palette").Ok);   // gone behind the page's back

        var notices = new List<string?>();
        palette.PropertyChanged += (_, e) => notices.Add(e.PropertyName);

        palette.ToggleCommand.Execute(null);

        Assert.Equal("That widget isn't installed.", page.Status);
        Assert.True(palette.Enabled);   // unchanged: nothing was written
        // The box already flipped under the click; this notice is what makes the OneWay binding push the true value back.
        Assert.Contains(nameof(InstalledRowViewModel.Enabled), notices);
    }

    [Fact]
    public void The_uninstall_dialog_has_no_default_button_and_escape_cancels()
    {
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var dlg = Sanduhr.App.Views.ThemedDialog.BuildConfirm("Uninstall X?", "detail", "Uninstall", "Cancel");
            Assert.False(dlg.PrimaryButton.IsDefault);   // Enter picks nothing: the primary choice deletes the person's pack
            Assert.True(dlg.SecondaryButton.IsCancel);   // Escape cancels
            Assert.Equal("Uninstall", dlg.PrimaryButton.Content);
            Assert.Equal(System.Windows.MessageBoxResult.None, dlg.Result);
        });
    }

    [Fact]
    public void The_chips_and_the_text_filter_narrow_the_list()
    {
        var page = Seeded();
        page.IsFilterClaudeCode = true;
        Assert.Equal(InstalledFilter.ClaudeCode, page.Filter);
        Assert.Equal([SettingsNames.MetersSection, "alpha", "beta-mod"], page.Installed.Select(r => r.Name));

        page.IsFilterWidgets = true;
        Assert.Equal(["Fixture palette", "Fixture theme"], page.Installed.Select(r => r.Name));

        page.FilterText = "theme";
        Assert.Equal(["Fixture theme"], page.Installed.Select(r => r.Name));

        page.FilterText = "nothing like it";
        Assert.Empty(page.Installed);
        Assert.True(page.NothingMatches);

        page.ShowWidgetPacks();
        Assert.Equal(("", InstalledFilter.Widgets), (page.FilterText, page.Filter));
        Assert.False(page.NothingMatches);
    }

    [Fact]
    public void Uninstall_asks_first_and_a_no_keeps_the_pack()
    {
        var page = Seeded();
        var asked = new List<string>();
        page.Confirm = (question, _) => { asked.Add(question); return false; };

        page.Installed.Single(r => r.Id == "good-theme").UninstallCommand.Execute(null);

        Assert.Equal(["Uninstall Fixture theme?"], asked);
        Assert.NotNull(InstalledPackKit.Store(Paths).Load().Find("good-theme"));

        page.Confirm = (_, _) => true;
        page.Installed.Single(r => r.Id == "good-theme").UninstallCommand.Execute(null);
        Assert.Null(InstalledPackKit.Store(Paths).Load().Find("good-theme"));
        Assert.DoesNotContain(page.Installed, r => r.Id == "good-theme");
    }

    [Fact]
    public void A_claude_code_row_shows_what_it_touches_in_one_line_and_speaks_it()
    {
        var home = Home(".claude", "{\"enabledPlugins\":{\"drawer@m\":true,\"quiet@m\":true}}");
        Plugin(home, "drawer", "1.0.0", mod: true);
        Put(Path.Combine(Root, "cache", "drawer"), "hooks/register.tsx", "const x = AbovePrompt; $.process.run('git');");
        Plugin(home, "quiet", "1.0.0");
        InstalledPackKit.Install(Paths, "good-palette");
        var page = Page();
        page.ReloadLocal();

        var drawer = page.Installed.Single(r => r.Name == "drawer");
        Assert.True(drawer.HasTouches);
        Assert.Equal("Draws a band; can run programs.", drawer.TouchesText);
        Assert.Equal("drawer, version 1.0.0, on, mod, draws a band; can run programs, read-only", drawer.RowName);

        var quiet = page.Installed.Single(r => r.Name == "quiet");
        Assert.False(quiet.HasTouches);
        Assert.Equal("quiet, version 1.0.0, on, read-only", quiet.RowName);   // unchanged: nothing to say
        Assert.False(page.Installed.Single(r => r.Id == "good-palette").HasTouches);
    }

    [Fact]
    public void Each_visit_starts_on_All_unless_the_link_that_brought_you_set_a_filter()
    {
        var page = Seeded();
        page.Activate();
        page.IsFilterWidgets = true;
        page.FilterText = "theme";
        Assert.Equal(["Fixture theme"], page.Installed.Select(r => r.Name));

        page.Deactivate();
        page.Activate();   // the next plain visit
        Assert.Equal((InstalledFilter.All, ""), (page.Filter, page.FilterText));
        Assert.Equal(5, page.Installed.Count);

        // The Themes link: the filter is set before the page shows, and holds for that visit only.
        page.Deactivate();
        page.ShowWidgetPacks();
        page.Activate();
        Assert.Equal((InstalledFilter.Widgets, ""), (page.Filter, page.FilterText));
        Assert.Equal(["Fixture palette", "Fixture theme"], page.Installed.Select(r => r.Name));

        page.Deactivate();
        page.Activate();
        Assert.Equal((InstalledFilter.All, ""), (page.Filter, page.FilterText));
    }

    [Fact]
    public void A_link_that_lands_while_the_page_is_already_showing_filters_for_that_visit_and_the_next_visit_is_All()
    {
        var page = Seeded();
        page.Activate();
        page.ShowWidgetPacks();   // the click landed after the page showed
        Assert.Equal(InstalledFilter.Widgets, page.Filter);

        page.Deactivate();
        page.Activate();
        Assert.Equal(InstalledFilter.All, page.Filter);
    }

    [Fact]
    public void Activate_runs_twice_for_one_visit_and_keeps_the_filter_a_link_just_set()
    {
        var page = Seeded();
        page.Deactivate();
        page.ShowWidgetPacks();
        page.Activate();   // IsVisibleChanged
        page.Activate();   // DataContextChanged, same realization
        Assert.Equal(InstalledFilter.Widgets, page.Filter);

        page.Deactivate();
        page.Activate();
        Assert.Equal(InstalledFilter.All, page.Filter);
    }

    [Fact]
    public void The_companions_sprite_catalog_link_filters_for_that_visit_only()
    {
        var page = Seeded();
        page.Deactivate();
        page.ShowSprites();
        page.Activate();
        Assert.Equal("sprite", page.CatalogKind);

        page.Deactivate();
        page.Activate();
        Assert.Null(page.CatalogKind);
    }

    [Fact]
    public void The_note_counts_plugins_without_a_switch_or_files_in_plain_words()
    {
        var personal = Home(".claude", "{\"enabledPlugins\":{\"ghost@m\":true}}");
        Plugin(personal, "alpha", "1.0.0");   // installed, never switched
        var page = Page();
        page.ReloadLocal();
        Assert.Equal(SettingsNames.NoSwitchNote(1) + " " + SettingsNames.NoFilesNote(1), page.Note);
    }

    [Fact]
    public void A_page_without_a_host_lists_the_packs_only()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = new ModsPageViewModel(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, new AppWindowFixture.OfflineHandler()));
        page.ReloadLocal();
        Assert.Equal(["Fixture palette"], page.Installed.Select(r => r.Name));
        Assert.Equal("", page.Note);
    }
}
