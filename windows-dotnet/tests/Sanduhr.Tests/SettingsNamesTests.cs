using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The names table: sidebar, page titles, links, retired names, and that the cards and tour use them.</summary>
public class SettingsNamesTests
{
    private static readonly SettingsPage[] SidebarPages =
        [.. Enum.GetValues<SettingsPage>().Where(p => p != SettingsPage.Widget)];

    private static IEnumerable<string> CopyOf(WhatsNewCard c) => [c.Title, c.Body];

    [Fact]
    public void Sidebar_has_every_page_but_Widget_exactly_once()
    {
        foreach (var page in SidebarPages)
            Assert.Single(SettingsNames.Pages, p => p == page);
        Assert.DoesNotContain(SettingsPage.Widget, SettingsNames.Pages);
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
        foreach (var d in all)
            Assert.True(d == SettingsPage.Widget || SettingsNames.Pages.Contains(d), d.ToString());
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
        Assert.Equal(["Privacy", "Desk Look", "Watchers", "Launcher", "Mods & Widgets"], SettingsNames.TitlesToCome);
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
        // "Desk Look" is not a page yet: the known title "Desk" must not let it through.
        Assert.Contains("no page \"Desk Look\" after \"Settings, \"", SettingsNames.PathProblems("Settings, Desk Look, Font"));
        // "Look" is a section of Desk, so the comma form is a real place.
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Look"));
        // Lowercase prose after a page and a comma is not a title.
        Assert.Empty(SettingsNames.PathProblems("Settings, About, or the tray menu"));
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
    public void The_agent_switches_say_Claude()
    {
        Assert.Equal("Let Claude show watchers", SettingsNames.WatchersSwitch);
        Assert.Equal(["Let agents show watchers"], SettingsNames.RetiredNames("turned on Let agents show watchers"));
    }

    [Fact]
    public void History_and_archive_names_are_pinned()
    {
        Assert.Equal("Export meter history…", SettingsNames.ExportMeterHistory);
        Assert.Equal(SettingsNames.ExportMeterHistory, SettingsNames.ExportCsv);
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
}
