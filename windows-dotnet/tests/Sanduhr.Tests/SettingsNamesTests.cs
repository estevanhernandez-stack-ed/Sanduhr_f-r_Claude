using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The names table: sidebar, page titles, links, retired names, and that the cards and tour use them.</summary>
public class SettingsNamesTests
{
    // Retired pages stay in the enum for their aliases (old cards, agent text, --settings links) but leave the sidebar: Widgets in
    // slice 4 (P4), History in slice 5. Widget is a page since slice 5 (the widget itself is Widget with no anchor).
    private static readonly SettingsPage[] SidebarPages =
        [.. Enum.GetValues<SettingsPage>().Where(p => p is not SettingsPage.Widgets and not SettingsPage.History)];

    private static IEnumerable<string> CopyOf(WhatsNewCard c) => [c.Title, c.Body];

    [Fact]
    public void Sidebar_has_every_live_page_exactly_once()
    {
        foreach (var page in SidebarPages)
            Assert.Single(SettingsNames.Pages, p => p == page);
        Assert.Contains(SettingsPage.Widget, SettingsNames.Pages);
        Assert.Equal(SidebarPages.Length, SettingsNames.Pages.Count);
    }

    [Fact]
    public void Group_titles_first_is_null_rest_non_empty()
    {
        Assert.Null(SettingsNames.Sidebar[0].Title);
        foreach (var g in SettingsNames.Sidebar.Skip(1))
            Assert.False(string.IsNullOrWhiteSpace(g.Title));
    }

    [Fact]
    public void Page_titles_are_unique_across_the_sidebar()
    {
        var titles = SettingsNames.Pages.Select(SettingsNames.PageTitle).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    [Theory]
    [InlineData(SettingsPage.General, "General")]
    [InlineData(SettingsPage.Accounts, "Accounts")]
    [InlineData(SettingsPage.Alerts, "Alerts")]
    [InlineData(SettingsPage.History, "History")]
    [InlineData(SettingsPage.ClaudeUsage, "Claude Usage")]
    [InlineData(SettingsPage.Publish, "Publish usage")]
    [InlineData(SettingsPage.Themes, "Themes")]
    [InlineData(SettingsPage.Desk, "Desk")]
    [InlineData(SettingsPage.DeskLook, "Desk Look")]
    [InlineData(SettingsPage.Watchers, "Watchers")]
    [InlineData(SettingsPage.Launcher, "Launcher")]
    [InlineData(SettingsPage.About, "About")]
    public void PageTitle_is_pinned(SettingsPage page, string title) =>
        Assert.Equal(title, SettingsNames.PageTitle(page));

    [Fact]
    public void LinkTitle_names_the_page_with_an_ellipsis_character()
    {
        Assert.Equal("Alerts settings\u2026", SettingsNames.LinkTitle(SettingsPage.Alerts));
    }

    [Fact]
    public void Linked_round_trips_every_page_and_rejects_unknown()
    {
        foreach (var page in SettingsNames.Pages)
            Assert.Equal(page, SettingsNames.Linked(SettingsNames.LinkTitle(page)));
        Assert.Null(SettingsNames.Linked("Nope settings\u2026"));
    }

    [Fact]
    public void RetiredNames_substring_names_are_case_sensitive()
    {
        Assert.Equal(["Take the Tour"], SettingsNames.RetiredNames("Take the Tour\u2026"));
        Assert.Empty(SettingsNames.RetiredNames("Take the tour\u2026"));
    }

    [Fact]
    public void RetiredNames_whole_names_match_only_the_whole_string()
    {
        Assert.Equal(["Sign In"], SettingsNames.RetiredNames("Sign In"));
        Assert.Empty(SettingsNames.RetiredNames("Sign in"));
        Assert.Empty(SettingsNames.RetiredNames("Sign In to Claude\u2026"));
        Assert.Equal(["Add account"], SettingsNames.RetiredNames("Add account"));
        Assert.Empty(SettingsNames.RetiredNames("Add account\u2026"));
    }

    [Fact]
    public void RetiredNames_is_safe_on_empty_and_null()
    {
        Assert.Empty(SettingsNames.RetiredNames(""));
        Assert.Empty(SettingsNames.RetiredNames(null!));
    }

    [Fact]
    public void Retired_entries_point_at_live_names()
    {
        foreach (var (old, now, _) in SettingsNames.Retired)
        {
            Assert.NotEqual(old, now);
            Assert.Empty(SettingsNames.RetiredNames(now));
        }
    }

    [Theory]
    [InlineData("Alerts settings\u2026")]
    [InlineData("Claude Usage settings\u2026")]
    [InlineData("Publish usage settings\u2026")]
    [InlineData("Open Windows Startup settings\u2026")]
    [InlineData("Settings\u2026")]
    public void UnknownLinks_passes_real_pages_and_windows_own(string text) =>
        Assert.Empty(SettingsNames.UnknownLinks(text));

    [Fact]
    public void UnknownLinks_names_the_bad_link()
    {
        Assert.Equal(["Meters settings\u2026"], SettingsNames.UnknownLinks("Meters settings\u2026"));
        Assert.Equal(["Meters settings\u2026"],
            SettingsNames.UnknownLinks("Open Alerts settings\u2026 or Meters settings\u2026 for more."));
    }

    [Fact]
    public void Table_names_are_live_and_on_sidebar_pages()
    {
        foreach (var (name, page) in SettingsNames.Table)
        {
            Assert.Empty(SettingsNames.RetiredNames(name));
            Assert.Contains(page, SettingsNames.Pages);
        }
    }

    [Fact]
    public void Cards_and_tour_use_no_retired_name_and_no_unknown_link()
    {
        var texts = new List<(string Where, string Text)>();
        foreach (var c in WhatsNewCards.Table)
        {
            texts.Add((c.Id + " title", c.Title));
            texts.Add((c.Id + " body", c.Body));
        }
        foreach (var t in WelcomeTour.Table)
        {
            foreach (var card in new[] { t, WelcomeTour.SignedOut(t) })
            {
                foreach (var s in CopyOf(card.Card))
                    texts.Add(("tour " + card.Id, s));
                texts.Add(("tour " + card.Id + " show me", card.ShowMeTitle));
            }
        }
        texts.Add(("AgainLine", WelcomeTour.AgainLine));
        texts.Add(("SkipTitle", WelcomeTour.SkipTitle));

        var bad = texts
            .Where(x => SettingsNames.RetiredNames(x.Text).Count > 0 || SettingsNames.UnknownLinks(x.Text).Count > 0)
            .Select(x => $"{x.Where}: {x.Text}")
            .ToList();
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Card_destinations_are_sidebar_pages_or_Widget()
    {
        var all = WhatsNewCards.Table.Select(c => c.Destination)
            .Concat(WelcomeTour.Table.Select(t => t.Card.Destination))
            .Concat(WelcomeTour.Table.Select(t => WelcomeTour.SignedOut(t).Card.Destination));
        // A retired page passes when its alias lands on a sidebar page (P5): the three 3.8.0 cards keep Widgets and land on Mods & Widgets.
        foreach (var d in all)
            Assert.True(d == SettingsPage.Widget || SettingsNames.Pages.Contains(d) || SettingsNames.Pages.Contains(SettingsIndex.Resolve(d, null).Page),
                d.ToString());
    }

    [Fact]
    public void TrademarkNotice_is_the_standard_text()
    {
        Assert.Equal(
            "\"Claude\" and \"claude.ai\" are trademarks of Anthropic PBC, used nominatively. "
            + "Sanduhr f\u00fcr Claude is an independent third-party tool, not affiliated with, endorsed by, "
            + "or associated with Anthropic PBC.",
            SettingsNames.TrademarkNotice);
    }

    [Fact]
    public void Group_titles_follow_the_v2_sidebar()
    {
        // "Extend" holds Claude Code and Mods (it will hold widget packs too); "Desktop" no longer repeats its first page.
        Assert.Equal(new string?[] { null, "Usage", "Extend", "Widget", "Desktop", "Help" },
            SettingsNames.Sidebar.Select(g => g.Title).ToArray());
    }

    [Fact]
    public void Titles_to_come_are_pinned_and_open_no_page_yet()
    {
        Assert.Equal(["Privacy"], SettingsNames.TitlesToCome);   // Desk Look, Watchers, Launcher landed in slice 3; Mods & Widgets in slice 4
        foreach (var title in SettingsNames.TitlesToCome)
            Assert.Null(SettingsNames.Linked(title + " settings…"));
    }

    [Theory]
    [InlineData("Turn it on in Settings, Desk, then place Watchers.")]
    [InlineData("Sign in any time from Settings, Accounts.")]
    [InlineData("(Settings, Now playing, Looks)")]
    [InlineData("in the Sanduhr widget (Settings, Publish usage, Destination).")]
    [InlineData("Settings, Claude Usage, Overview")]
    [InlineData("You can take the tour again from Settings, About, or the tray menu.")]
    [InlineData("Show in Explorer, settings, history and themes")]
    [InlineData("has not turned on 'Let Claude show watchers' in Sanduhr (Settings, ")]
    [InlineData("Settings, redesigned")]
    [InlineData("")]
    public void PathProblems_passes_real_places(string text) =>
        Assert.Empty(SettingsNames.PathProblems(text));

    [Theory]
    [InlineData("(Settings, Desk, Watchers)", "no section \"Watchers\" on Desk")]
    [InlineData("in Settings ▸ Claude Usage.", "\"Settings ▸\": write \"Settings, <Page>, <Section>\"")]
    [InlineData("(Settings > Publish usage > Endpoint URL)", "\"Settings >\": write \"Settings, <Page>, <Section>\"")]
    [InlineData("Open Settings, Privacy.", "no page \"Privacy\" after \"Settings, \"")]
    [InlineData("(Settings → Accounts to add or remove)", "\"Settings →\": write \"Settings, <Page>, <Section>\"")]
    public void PathProblems_names_the_problem(string text, string problem) =>
        Assert.Contains(problem, SettingsNames.PathProblems(text));

    [Fact]
    public void PathProblems_flags_a_longer_unknown_title()
    {
        // "Desk Look" is a page since slice 3: the longer title wins over "Desk", and "Font" is none of its sections.
        Assert.Contains("no section \"Font\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Font"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Fonts"));
        // A capitalised word after a known title is still a longer, unknown title.
        Assert.Contains("no page \"Desk Looks\" after \"Settings, \"", SettingsNames.PathProblems("Settings, Desk Looks"));
        // Lowercase prose after a page and a comma is not a title.
        Assert.Empty(SettingsNames.PathProblems("Settings, About, or the tray menu"));
    }

    [Fact]
    public void PathProblems_flags_a_longer_unknown_section_title()
    {
        // The page rule, applied to sections (slice 1 review: section prefix-match): "Ink and color" is a section of Desk Look;
        // "Ink" alone, "Ink and colors" and "Back plate Extra" are not.
        Assert.Contains("no section \"Ink\" on Desk Look", SettingsNames.PathProblems("(Settings, Desk Look, Ink)"));
        Assert.Contains("no section \"Ink and colors\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Ink and colors"));
        Assert.Contains("no section \"Back plate Extra\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Back plate Extra"));
        Assert.Empty(SettingsNames.PathProblems("(Settings, Desk Look, Ink and color)"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Claude Code, Folders to share"));
    }

    [Fact]
    public void The_old_consent_copy_is_retired_and_the_shortcut_name_has_one_source()
    {
        Assert.Equal(["Keep a local usage history"], SettingsNames.RetiredNames("Keep a local usage history?"));
        Assert.Equal(["Keep history"], SettingsNames.RetiredNames("Keep history"));
        Assert.Empty(SettingsNames.RetiredNames("Keep a local session archive?"));
        // Two literals name the same control; nothing else ties them.
        Assert.Equal(SettingsNames.ShortcutToOpenSettings, HotKeys.Name(HotKeyAction.Settings));
    }

    [Fact]
    public void Names_that_match_their_labels_are_pinned()
    {
        Assert.Equal("Desk font", SettingsNames.DeskFont);
        Assert.Equal("Message font", SettingsNames.MessageFont);
        Assert.Equal("Message color", SettingsNames.MessageColor);
        Assert.Equal("Clock, date, Claude", SettingsNames.DeskInkPicker);
        Assert.Equal("Close Settings", SettingsNames.CloseSettings);
        Assert.Equal("Let Claude read this folder's project totals", SettingsNames.LetClaudeReadTotals);
        Assert.Equal(["Claude can read this folder's project totals"],
            SettingsNames.RetiredNames("Claude can read this folder's project totals (MCP)"));
    }

    [Theory]
    [InlineData(@"~\.claude")]
    [InlineData(@"D:\Work Stuff\my claude seat")]
    [InlineData(@"C:\Users\someone-with-a-very-long-profile-name\AppData\Roaming\claude-code\.claude-work")]
    public void The_folder_formatters_keep_the_whole_folder(string folder)
    {
        Assert.Equal("Forget folder " + folder, SettingsNames.ForgetFolderFor(folder));
        Assert.Equal("Let Claude read this folder's project totals, " + folder, SettingsNames.LetClaudeReadTotalsFor(folder));
    }

    [Fact]
    public void A_title_in_a_collapsed_section_is_marked_in_text_and_spoken_plainly()
    {
        Assert.Equal("Margins (in Advanced)", SettingsNames.InSection("Margins", "Advanced"));
        Assert.Equal("Margins, in Advanced", SettingsNames.InSectionSpoken("Margins", "Advanced"));
    }

    [Fact]
    public void The_agent_switches_say_Claude()
    {
        Assert.Equal("Let Claude show watchers", SettingsNames.WatchersSwitch);
        Assert.Equal(["Let agents show watchers"], SettingsNames.RetiredNames("turned on Let agents show watchers"));
    }

    [Fact]
    public void History_and_archive_names_are_pinned()
    {
        Assert.Equal("Export meter history…", SettingsNames.ExportMeterHistory);
        Assert.Equal("Export sessions…", SettingsNames.ExportSessions);
        Assert.Equal("Session archive", SettingsNames.SessionArchive);
        Assert.Equal("Erase session archive…", SettingsNames.EraseSessionArchive);
        Assert.Equal("Open archive folder", SettingsNames.OpenArchiveFolder);
        Assert.Equal("Folders to share (off until you tick them)", SettingsNames.FoldersToShare);
        Assert.Equal("Nothing archived yet.", SettingsNames.NothingArchivedYet);
        Assert.Equal("Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)", SettingsNames.ResetClaudeCodeEntries);
        Assert.Equal("Meter history and the session archive", SettingsNames.ResetMeterHistory);
        foreach (var old in new[] { "Export CSV…", "History vault", "Usage vault", "Erase archive", "Open vault folder", "Homes to share", "(statusline, MCP server)", "Usage history and the vault" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
    }

    [Fact]
    public void Page_copy_names_are_pinned_and_the_old_ones_retired()
    {
        Assert.Equal("Desk: a clock, your meters, a message and more on the desktop", SettingsNames.DeskSwitch);
        Assert.Equal("Claude Code glow hook", SettingsNames.ClaudeCodeGlowHook);
        Assert.Equal("Installed, but not the current version.", SettingsNames.PieceOutdated);
        Assert.Equal("Once the statusline or meters are installed here, they show this account's meters.", SettingsNames.LinkedAccountCaption);
        Assert.Equal("Claude Code keeps transcripts for", SettingsNames.TranscriptsKept);
        Assert.Equal("This theme's JSON is valid.", SettingsNames.ThemeJsonValid);
        Assert.Equal("Save the theme as", SettingsNames.SaveThemeAs);
        Assert.Equal("Play the cooldown snake's sound at 100%", SettingsNames.SnakeSoundAtFull);
        foreach (var old in new[] { "Desk: the clock and your Claude meters on the desktop", "Glow when Claude needs you",
                     "Install again to update it.", "History kept", "Looks good", "Save as:", "100% plays the ! (you know the one)" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
        Assert.Empty(SettingsNames.RetiredNames("Looks good to me")); // "Looks good" is whole-string only
    }

    [Fact]
    public void Menu_names_are_pinned_and_their_old_forms_retired()
    {
        Assert.Equal("Compact mode", SettingsNames.CompactMode);
        Assert.Equal("Deep Work", SettingsNames.DeepWork);
        Assert.Equal("Cooldown snake", SettingsNames.CooldownSnake);
        Assert.Equal("Refresh usage now", SettingsNames.RefreshUsageNow);
        Assert.Equal("Settings…", SettingsNames.OpenSettings);
        Assert.Equal("Quit Sanduhr", SettingsNames.QuitSanduhr);
        Assert.Equal("Hide this limit", SettingsNames.HideThisLimit);
        Assert.Equal("Message settings…", SettingsNames.LinkTitle(SettingsPage.DeskMessage));
        foreach (var old in new[] { "Compact Mode", "Expand", "Deep Work (Focus)", "Deep Work focus timer (Ctrl+P)",
                     "Cooldown Snake", "Cooldown snake game", "Hide this tier", "Edit messages…" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
        Assert.Empty(SettingsNames.RetiredNames("Expand the list")); // "Expand" is whole-string only
    }

    [Fact]
    public void The_widget_and_What_s_New_window_names_are_pinned_and_their_old_forms_retired()
    {
        Assert.Equal("Graph style", SettingsNames.GraphStyle);
        Assert.Equal("Keep the widget on top", SettingsNames.KeepWidgetOnTop);
        Assert.Equal("Don't show What's New after updates", SettingsNames.HideWhatsNewAfterUpdates);
        foreach (var old in new[] { "Cycle graph style", "Pin on top", "Pin always on top", "Don't show after updates" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.HideWhatsNewAfterUpdates));
    }

    [Fact]
    public void Message_modes_are_List_and_Text()
    {
        Assert.Equal("List", SettingsNames.MessageModeList);
        Assert.Equal("Text", SettingsNames.MessageModeText);
        Assert.NotEmpty(SettingsNames.RetiredNames("Edit as text…"));
        Assert.NotEmpty(SettingsNames.RetiredNames("Back to the list"));
    }

    [Fact]
    public void Follow_caption_says_how_long_a_manual_switch_pauses_it()
    {
        Assert.Equal(
            "Every 15 minutes Sanduhr checks your other signed-in accounts and switches to the one whose session is climbing. "
            + "A manual switch pauses it for up to 3 hours, or until that account sits idle for 30 minutes.",
            SettingsNames.FollowAccountCaption);
        Assert.Equal("Pick an account to switch, rename, update or remove it.", SettingsNames.PickAnAccount);
    }

    [Fact]
    public void Key_help_is_plain_steps_for_Edge_and_Chrome()
    {
        Assert.Contains("Edge", SettingsNames.ManualKeySteps);
        Assert.Contains("Chrome", SettingsNames.ManualKeySteps);
        Assert.Contains("sessionKey", SettingsNames.ManualKeySteps);
        Assert.DoesNotContain("→", SettingsNames.ManualKeySteps);
        Assert.DoesNotContain("DevTools", SettingsNames.ManualKeySteps);
        Assert.Equal("Seeing a Cloudflare error?", SettingsNames.ManualKeyCloudflareToggle);
        Assert.Contains("cf_clearance", SettingsNames.ManualKeyCloudflareHelp);
        Assert.Equal("cf_clearance", SettingsNames.ManualKeyCfLabel);
    }

    [Fact]
    public void Core_words_are_defined_once()
    {
        Assert.Equal(
            new (string, string)[]
            {
                ("Desk", "the clock, meters and pieces Sanduhr draws on your desktop"),
                ("Widget", "the small floating window with your meters"),
                ("Piece", "one thing on the Desk"),
                ("Watchers", "cards that show work in progress, from Claude or your CI"),
                ("Companion", "a small animated sprite on the Desk"),
                ("Mod", "an add-on that Claude Code loads"),
                ("Widget pack", "an add-on for Sanduhr: a theme, colors, a font or a companion"),
            },
            SettingsNames.CoreWords);
        Assert.Equal("The Desk is the clock, meters and pieces Sanduhr draws on your desktop.", SettingsNames.DeskLine);
        Assert.Equal("A widget pack is an add-on for Sanduhr: a theme, colors, a font or a companion. A mod is an add-on that Claude Code loads.", SettingsNames.ModsPageLine);
    }

    [Fact]
    public void The_where_question_for_a_theme_that_is_not_the_persons_names_built_in_and_pack_themes()
    {
        string q = SettingsNames.WhereQuestion("Mint", false);
        Assert.Contains("built-in or pack theme", q);
        Assert.DoesNotContain("ships with Sanduhr", q);
        Assert.Empty(SettingsNames.PathProblems(q));
    }

    [Fact]
    public void The_ink_caption_names_Alerts_as_a_place_and_passes_the_path_check()
    {
        Assert.Contains("Settings, Alerts", SettingsNames.DeskInkCaption);
        Assert.Empty(SettingsNames.PathProblems(SettingsNames.DeskInkCaption));
    }

    // ---- Settings v2, slice 4: Mods & Widgets and the Claude Code cards (spec, Mods & Widgets; Claude Code) ----

    [Fact]
    public void Mods_and_Widgets_copy_is_the_specs()
    {
        Assert.Equal("Everything installed for Sanduhr and for Claude Code, in one list. You can switch Sanduhr's mod and widget packs here; other Claude Code mods are shown read-only.",
            SettingsNames.ModsIntro);
        Assert.Equal("Off keeps the mod's entry and files; Remove takes the entry out and puts settings.json back exactly.",
            SettingsNames.MetersOffVsRemove);
        Assert.Equal("Read-only: Sanduhr can't switch this yet. Use claude plugin in a terminal.", SettingsNames.ReadOnlyClaudeCode);
        Assert.Equal("The catalog comes from GitHub. Sanduhr asks only when you open it, check it again, or install or update a pack.", SettingsNames.CatalogDisclosure);
        Assert.Equal("Couldn't reach the widget catalog. Widget packs add themes, colors, fonts and a Desk companion. Check your connection and try again.",
            WidgetCatalogClient.Offline);
        Assert.Equal(("In Themes", "In Desk Look, Ink and color", "In Desk Look, Fonts", "As the Desk companion"),
            (SettingsNames.ShowsInThemes, SettingsNames.ShowsInInk, SettingsNames.ShowsInFonts, SettingsNames.ShowsAsCompanion));
        Assert.Equal("Not used yet: Sanduhr has nothing that draws effects packs.", SettingsNames.EffectsUnused);
        Assert.Equal(("All", "Claude Code", "Widgets"), (SettingsNames.FilterAll, SettingsNames.FilterClaudeCode, SettingsNames.FilterWidgets));
        Assert.Equal(("Meters above the prompt", "Installed", "Catalog", "What this folder holds"),
            (SettingsNames.MetersSection, SettingsNames.InstalledSection, SettingsNames.CatalogSection, SettingsNames.FolderHolds));
        Assert.Equal("A widget pack is " + SettingsNames.DefineWidgetPack + ". A mod is " + SettingsNames.DefineMod + ".",
            SettingsNames.ModsPageLine);
    }

    [Fact]
    public void Counts_and_scopes_read_in_plain_words()
    {
        Assert.Equal("On in 2 of 3 folders", SettingsNames.OnInFolders(2, 3));
        Assert.Equal("1 plugin has no on/off entry in settings.json, so Claude Code treats it as on.", SettingsNames.NoSwitchNote(1));
        Assert.Equal("3 plugins have no on/off entry in settings.json, so Claude Code treats them as on.", SettingsNames.NoSwitchNote(3));
        Assert.Equal("1 plugin is switched on in settings.json, but its files aren't there.", SettingsNames.NoFilesNote(1));
        Assert.Equal("2 plugins are switched on in settings.json, but their files aren't there.", SettingsNames.NoFilesNote(2));
        Assert.Equal("Theme packs: 3 installed.", SettingsNames.ThemePacksLine(3));
        Assert.Equal("Not installed in any folder yet.", SettingsNames.GlowHookIn([]));
        Assert.Equal(@"Installed in ~\.claude.", SettingsNames.GlowHookIn([@"~\.claude"]));
        Assert.Equal(@"Installed in ~\.claude, ~\.claude-personal and ~\.claude-work.",
            SettingsNames.GlowHookIn([@"~\.claude", @"~\.claude-personal", @"~\.claude-work"]));
    }

    [Fact]
    public void Switches_are_spoken_with_their_visible_word_first()
    {
        Assert.Equal("On", SettingsNames.PackOn);
        Assert.Equal("On: Fixture palette", SettingsNames.PackOnFor("Fixture palette"));
        Assert.Equal(@"On: Meters above the prompt in ~\.claude", SettingsNames.MetersOnFor(@"~\.claude"));
        Assert.Equal("Uninstall…", SettingsNames.WidgetUninstall);
        Assert.Equal("Uninstall Fixture palette?", SettingsNames.UninstallQuestion("Fixture palette"));
    }

    [Fact]
    public void Slice_4_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.ModsIntro, SettingsNames.MetersOffVsRemove, SettingsNames.ReadOnlyClaudeCode, SettingsNames.CatalogDisclosure,
            SettingsNames.ShowsInThemes, SettingsNames.ShowsInInk, SettingsNames.ShowsInFonts, SettingsNames.ShowsAsCompanion,
            SettingsNames.EffectsUnused, SettingsNames.GlowNeedsHook, SettingsNames.AddAccountToLink, SettingsNames.MetersLinkUp,
            SettingsNames.UninstallDetail, SettingsNames.ModsPageLine, WidgetCatalogClient.Offline,
            SettingsNames.FolderUnreadableReason, SettingsNames.NoFolderInMods, SettingsNames.NoFolderInClaudeCode,
        })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
        }
    }

    [Fact]
    public void Slice_4_row_and_status_words_are_pinned()
    {
        Assert.Equal(("Sanduhr", "skills folder", "plugin folder", "from the catalog"),
            (SettingsNames.SourceSanduhr, SettingsNames.SourceSkillsFolder, SettingsNames.SourcePluginFolder, SettingsNames.SourceFromCatalog));
        Assert.Equal(("Sanduhr's mod", "Claude Code mod", "Claude Code plugin"),
            (SettingsNames.KindSanduhrMod, SettingsNames.KindClaudeCodeMod, SettingsNames.KindClaudeCodePlugin));
        Assert.Equal(("read-only", "on", "off"), (SettingsNames.RowReadOnly, SettingsNames.RowOn, SettingsNames.RowOff));
        Assert.Equal(("Installed.", "Version 1.0.0 is installed.", "Needs Sanduhr 9.0.0 or later.", "Version 1.0.0 is installed. 2.0.0 needs Sanduhr 9.0.0 or later."),
            (SettingsNames.CatalogInstalled, SettingsNames.CatalogVersionInstalled("1.0.0"), SettingsNames.CatalogNeedsSanduhr("9.0.0"),
             SettingsNames.CatalogUpdateNeedsSanduhr("1.0.0", "2.0.0", "9.0.0")));
        // An installed pack with no version still reads as a sentence, never "Version  is installed."
        Assert.Equal("An older version is installed.", SettingsNames.CatalogVersionInstalled(""));
        Assert.Equal("An older version is installed. 2.0.0 needs Sanduhr 9.0.0 or later.",
            SettingsNames.CatalogUpdateNeedsSanduhr("", "2.0.0", "9.0.0"));
        Assert.Equal("Palette settings… for Fixture", SettingsNames.WhereLinkFor("Palette settings…", "Fixture"));
        Assert.Equal("Uninstall Fixture palette", SettingsNames.UninstallFor("Fixture palette"));
        Assert.Equal("Fixture palette is on.", SettingsNames.PackIsOn("Fixture palette"));
        Assert.Equal("Fixture palette is off.", SettingsNames.PackIsOff("Fixture palette"));
        Assert.Equal("The widget couldn't be switched.", SettingsNames.PackSwitchFailed);
        Assert.Equal("This folder's settings.json can't be read, so Sanduhr changes nothing in it.", SettingsNames.FolderUnreadableReason);
        Assert.Equal(@"Update Meters above the prompt in ~\.claude", SettingsNames.UpdateMetersIn(@"~\.claude"));
        Assert.Equal(@"Remove Meters above the prompt from ~\.claude", SettingsNames.RemoveMetersFrom(@"~\.claude"));
        Assert.Equal(("Show", "Widget packs in the catalog", "Update…", "Remove…"),
            (SettingsNames.ModsShowGroup, SettingsNames.CatalogListName, SettingsNames.UpdateButton, SettingsNames.RemoveButton));
        // P20: the sentence, then a real "Claude Code settings…" link to claudecode#folders.
        Assert.Equal("No Claude Code folder found. Add one in", SettingsNames.NoFolderInMods);
        Assert.Equal("Claude Code settings…", SettingsNames.ClaudeCodeLinkTitle);
        Assert.Equal(SettingsPage.ClaudeCode, SettingsNames.Linked(SettingsNames.ClaudeCodeLinkTitle));
        Assert.Equal(("Claude Code folders", "No Claude Code folder found. Add one with Add folder…"),
            (SettingsNames.ClaudeCodeFoldersSection, SettingsNames.NoFolderInClaudeCode));
        Assert.Equal("Draw your meters above the Claude Code prompt?", SettingsNames.MetersConsentTitle);
        Assert.Equal("Sanduhr adds a mod that draws your session and weekly meters in a band above the prompt.", SettingsNames.MetersConsentIntro);
        Assert.StartsWith(@"What gets written: the mod's folder, copied under %USERPROFILE%\.sanduhr\mods, and one entry", SettingsNames.MetersConsentDetails);
        Assert.EndsWith("leaves any other folder in that list alone.", SettingsNames.MetersConsentDetails);
        Assert.Equal(("Meters installed. New Claude Code sessions show them.", "Couldn't install the meters."),
            (SettingsNames.MetersInstalled, SettingsNames.MetersInstallFailed));
    }

    [Fact]
    public void Copy_moved_out_of_Mods_and_Config_is_pinned()
    {
        // P13: Mods & Config is gone (slice 4, Task 12); what FolderHoldsViewModel and the meters rows copied from it lives here now, word for word.
        Assert.Equal(("Couldn't read this folder.", "Readable", "settings.json parses as JSON."),
            (SettingsNames.HoldsCantRead, SettingsNames.SettingsReadable, SettingsNames.SettingsReadableDetail));
        Assert.Equal("Unreadable: line 3, column 7", SettingsNames.SettingsUnreadableAt(3, 7));
        Assert.Equal("Claude Code reads settings.json strictly: no comments, no trailing commas. Sanduhr changes nothing in it while it is unreadable.",
            SettingsNames.SettingsUnreadableStrict);
        Assert.Equal(("Unreadable", "settings.json can't be opened. Sanduhr changes nothing in it."),
            (SettingsNames.SettingsUnreadable, SettingsNames.SettingsCantOpen));
        Assert.Equal(("Missing", "This folder has no settings.json yet."), (SettingsNames.SettingsMissing, SettingsNames.SettingsMissingDetail));
        Assert.Equal(("Sanduhr's statusline", "Someone else's statusline", "None"),
            (SettingsNames.StatuslineSanduhrs, SettingsNames.StatuslineSomeoneElses, SettingsNames.HoldsNone));
        Assert.Equal(("1 group", "2 groups, 1 from Sanduhr"), (SettingsNames.HookGroups(1, 0), SettingsNames.HookGroups(2, 1)));
        Assert.Equal(("1 group on 1 event", "3 groups on 2 events"), (SettingsNames.HooksOnEvents(1, 1), SettingsNames.HooksOnEvents(3, 2)));
        Assert.Equal(("1 server", "2 servers"), (SettingsNames.McpServerCount(1), SettingsNames.McpServerCount(2)));
        Assert.Equal(("1 project has their own servers.", "2 projects have their own servers."),
            (SettingsNames.McpProjectsOwn(1), SettingsNames.McpProjectsOwn(2)));
        Assert.Equal(("1 line, 512 bytes", "40 lines, 2 KB"), (SettingsNames.ClaudeMdSize(1, 512), SettingsNames.ClaudeMdSize(40, 2048)));
        Assert.Equal(("30 days.", "Unknown while settings.json is unreadable."), (SettingsNames.RetentionDays(30), SettingsNames.RetentionUnknown));

        Assert.Equal(("Meters mod turned on.", "Meters mod turned off.", "Couldn't change the switch.", "Install"),
            (SettingsNames.MetersTurnedOn, SettingsNames.MetersTurnedOff, SettingsNames.MetersSwitchFailed, SettingsNames.MetersConsentInstall));
        Assert.Equal(("Update the meters mod?", "Update", "Not now"),
            (SettingsNames.MetersUpdateTitle, SettingsNames.MetersUpdateConfirm, SettingsNames.MetersUpdateNotNow));
        Assert.Equal("This folder's meters mod is pinned to 0.1.0. Updating points it at 0.2.0, the version that ships with this Sanduhr. "
            + "The mod's files are copied again and one entry in this folder's settings.json changes. The old version's files stay until "
            + "Sanduhr prunes them.", SettingsNames.MetersUpdateBody("0.1.0", "0.2.0"));
        Assert.StartsWith("This folder's meters mod is pinned to the installed version. Updating points it at the new version,",
            SettingsNames.MetersUpdateBody(null, null));
        Assert.Equal(("Meters mod updated.", "Meters mod is already current.", "Couldn't update the meters mod."),
            (SettingsNames.MetersUpdated, SettingsNames.MetersAlreadyCurrent, SettingsNames.MetersUpdateFailed));
        Assert.Equal(("Remove the meters mod?", "Takes Sanduhr's entry out of this folder's settings.json and leaves any other mod alone.", "Remove", "Cancel"),
            (SettingsNames.MetersRemoveTitle, SettingsNames.MetersRemoveBody, SettingsNames.MetersRemoveConfirm, SettingsNames.MetersRemoveCancel));
        Assert.Equal(("Meters mod removed.", "Couldn't remove the meters mod."), (SettingsNames.MetersRemoved, SettingsNames.MetersRemoveFailed));
    }

    [Fact]
    public void The_Mods_and_Widgets_page_copy_is_pinned_and_says_only_what_the_code_does()
    {
        // Task 11 review: GitHub is asked when the catalog opens, on "Check the catalog again", and for an Install or Update download. Nothing else.
        Assert.Equal("Installed packs live in Sanduhr's own widgets folder on this PC. Sanduhr asks GitHub for the catalog only when you open it "
            + "or check it again, and Install or Update downloads that pack from GitHub. Reset Sanduhr removes the folder.", SettingsNames.InstalledPacksNote);
        Assert.Equal("The catalog comes from GitHub. Sanduhr asks only when you open it, check it again, or install or update a pack.", SettingsNames.CatalogDisclosure);   // the controller's ruling, 2026-10-25: exact, since Check again, Install and Update ask too
        Assert.Equal("Nothing installed yet. Widget packs and Claude Code plugins and mods show here, and so do widgets you make with Claude, marked made here.",
            SettingsNames.InstalledEmpty);
        Assert.StartsWith("Sprites, themes, palettes, effects and fonts from the 626 Labs widget catalog.", SettingsNames.CatalogBlurb);
        Assert.Equal("Choose a PNG sprite sheet for each stage. It is copied into the widget and never sent anywhere.", SettingsNames.UseOwnArtHelp);
        foreach (var text in new[] { SettingsNames.InstalledPacksNote, SettingsNames.InstalledEmpty, SettingsNames.CatalogBlurb, SettingsNames.UseOwnArtHelp,
                     SettingsNames.MetersUpdateBody(null, null), SettingsNames.MetersRemoveBody, SettingsNames.SettingsUnreadableStrict })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
        }
    }

    // ---- Settings v2, slice 5: Usage, Accounts, Alerts and Widget ----

    [Fact]
    public void Slice_5_copy_is_the_specs()
    {
        Assert.Equal("Claude Usage, Alerts and the meters fill in after you sign in.", SettingsNames.SignedOutAccountsLine);
        Assert.Equal("Sign in to start recording your meters. Sanduhr keeps 30 days on this PC.", SettingsNames.MeterHistorySignedOut);
        Assert.Equal("Sanduhr is in Just the Desk: the widget is hidden.", SettingsNames.JustTheDeskOn);
        Assert.Equal(("Show the widget", "Warn for this limit", "Show this limit", "Hidden until it resets"),
            (SettingsNames.ShowTheWidget, SettingsNames.WarnForThisLimit, SettingsNames.ShowThisLimit, SettingsNames.HiddenUntilItResets));
        Assert.Equal(("Clear this account's history…", "Remove account…", "Show a sample…", "Data for this account", "Linked folders"),
            (SettingsNames.ClearAccountHistory, SettingsNames.RemoveAccount, SettingsNames.ShowASample, SettingsNames.AccountDataSection,
             SettingsNames.LinkedFolders));
        Assert.Equal(("Each limit", "What gets sent", "Limit order", "Deep Work length"),
            (SettingsNames.EachLimitSection, SettingsNames.PublishSampleSection, SettingsNames.LimitOrder, SettingsNames.DeepWorkLength));
        Assert.Equal("The widget is " + SettingsNames.DefineWidget + ".", SettingsNames.WidgetLine);
        Assert.Equal(("Name this account", "Label"), (SettingsNames.NameThisAccount, SettingsNames.AccountLabelField));
        Assert.Equal("Meters", SettingsNames.MetersView);
        Assert.Contains("token counts in total, per limit and per project name, and your meters' percentages, reset times and when they were read.",
            SettingsNames.PublishRecordLine);
    }

    [Fact]
    public void Row_names_lead_with_the_visible_label()
    {
        Assert.Equal("Warn for this limit, Weekly - Opus", SettingsNames.WarnForLimit("Weekly - Opus"));
        Assert.Equal("Show this limit, Weekly - Opus", SettingsNames.ShowLimit("Weekly - Opus"));
        Assert.Equal("Move up, Weekly - Opus", SettingsNames.MoveLimitUp("Weekly - Opus"));
        Assert.Equal("Move down, Weekly - Opus", SettingsNames.MoveLimitDown("Weekly - Opus"));
        Assert.Equal("Clear this account's history, Work", SettingsNames.ClearAccountHistoryNamed("Work"));
        Assert.Equal("Remove account, Work", SettingsNames.RemoveAccountNamed("Work"));
        Assert.Equal("Deep Work length, minutes", SettingsNames.DeepWorkLengthSpoken);
    }

    [Fact]
    public void What_Remove_and_Clear_say_is_everything_they_do()
    {
        // SignOutAccountAsync removes the sign-in, the history file, the folder link, the meter files Claude Code reads and the browser
        // profile (decision 7, P2).
        Assert.Equal("Removes Work's sign-in from Windows Credential Manager, its meter history (history.Work.json), its Claude Code folder link, "
            + "the meter files Claude Code reads for it and its browser profile. It can't be undone.", SettingsNames.RemoveAccountDetail("Work"));
        Assert.Equal("Remove Work from Sanduhr?", SettingsNames.RemoveAccountQuestion("Work"));
        Assert.Equal("Clear Work's meter history?", SettingsNames.ClearHistoryQuestion("Work"));
        Assert.Equal("Work's meter history is cleared.", SettingsNames.HistoryCleared("Work"));
        Assert.Equal("Linked to ~\\.claude-work.", SettingsNames.LinkedFolderLine("~\\.claude-work"));
        Assert.Equal("No Claude Code folder is linked to this account.", SettingsNames.LinkedFolderLine(null));
    }

    [Fact]
    public void The_sample_header_says_where_the_next_record_goes_without_naming_a_button()
    {
        Assert.Equal("The next record, for 2026-10-24, goes to example.com.", SettingsNames.SampleHeader("2026-10-24", "example.com"));
        Assert.Equal("The next record, for 2026-10-24, has no endpoint yet, so it goes nowhere.", SettingsNames.SampleHeader("2026-10-24", null));
        Assert.Equal("No folder is ticked under Folders to share. Publish now waits for one; a daily or agent publish would send this record, "
            + "naming no folder or project.", SettingsNames.SampleNoFolders);
    }

    [Fact]
    public void Slice_5_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.SignedOutAccountsLine, SettingsNames.ClearHistoryDetail, SettingsNames.RemoveAccountDetail("Work"),
            SettingsNames.EachLimitCaption, SettingsNames.HiddenUntilResetCaption, SettingsNames.EachLimitSignedOut, SettingsNames.EachLimitNotYet,
            SettingsNames.MetersIntro, SettingsNames.MeterHistorySignedOut, SettingsNames.MeterHistoryEmpty, SettingsNames.WidgetLine,
            SettingsNames.CompactModeCaption, SettingsNames.DeepWorkRange, SettingsNames.JustTheDeskOn, SettingsNames.ShowTheWidgetCaption,
            SettingsNames.PublishRecordLine, SettingsNames.SampleNoFolders, SettingsNames.SampleFailed, SettingsNames.SampleHeader("2026-10-24", null),
        })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.Empty(SettingsNames.UnknownLinks(text));
        }
    }

    [Fact]
    public void The_key_windows_field_is_called_Label()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("Account name"));
        Assert.Empty(SettingsNames.RetiredNames("account name"));   // prose that says "the account name" stays
    }
}
