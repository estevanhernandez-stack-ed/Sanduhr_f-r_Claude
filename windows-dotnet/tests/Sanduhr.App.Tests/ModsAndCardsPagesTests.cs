using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The Claude Code cards and Mods &amp; Widgets in a real Settings window, shown off screen and never activated (slice 4).</summary>
[Collection("SettingsWindow")]
public sealed class ModsAndCardsPagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Vm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        return (window, svm, guard);
    }

    private static void Settle(Window window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    /// <summary>A TextBlock's words, inlines (Runs) included.</summary>
    private static string TextOf(TextBlock t) => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text;

    [Fact]
    public void A_link_to_folder_config_opens_the_chosen_cards_report_in_view() => AppHost.Run(() =>
    {
        Home(".claude-personal", """{"enabledPlugins":{"a@m":true,"b@m":true}}""");
        Home(".claude-work");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Retention);   // an old Mods & Config link
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig), window.Place);
            var open = Assert.Single(svm.ClaudeCodePage.Folders, f => f.HoldsOpen);
            Assert.Equal(@"~\.claude-personal", open.Display);
            var expander = Below<Expander>(window).Single(e => e.IsExpanded && e.DataContext == open);
            Assert.True(expander.IsVisible, "the opened report is not shown");
            Assert.NotEqual("", open.Holds.SettingsSummary);
            Assert.Contains(Below<TextBlock>(expander), t => TextOf(t).Contains(open.Holds.SettingsSummary, StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Claude_Code_page_has_no_install_combo_and_each_card_shows_its_pieces() => AppHost.Run(() =>
    {
        Home(".claude-work");
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode);
            Settle(window, guard);
            var names = Below<FrameworkElement>(window).Select(System.Windows.Automation.AutomationProperties.GetName).ToList();
            Assert.DoesNotContain("Folder to install into", names);
            Assert.Contains(@"Install Statusline", names);
            Assert.Equal(2, names.Count(n => n == "Install Statusline"));   // one per card, each named for its piece
            Assert.Contains(names, n => n.StartsWith(SettingsNames.FolderHolds + " ") && n.EndsWith(@"~\.claude-work"));
        }
        finally { window.Close(); }
    });

    private static DependencyObject? CardOf(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ContentPresenter { Content: CcFolderRowViewModel })
                return p;
        return null;
    }

    private static string NameOf(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d) ?? "";

    private static string Describe(object? element) => element switch
    {
        null => "nothing",
        FrameworkElement fe => fe.GetType().Name + (NameOf(fe) is { Length: > 0 } n ? $" \"{n}\"" : ""),
        _ => element.GetType().Name,
    };

    private static bool IsInside(DependencyObject d, DependencyObject root)
    {
        for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    [DesktopFact]
    public void A_folder_config_landing_puts_keyboard_focus_in_the_opened_card() => AppHost.Run(() =>
    {
        // P7: the page's anchor is the whole cards list, so the first card's first control took focus. The opened card's report header
        // must end with it, or a keyboard or screen-reader user lands inside a different folder's card.
        Home(".claude-personal", """{"enabledPlugins":{"a@m":true,"b@m":true}}""");
        Home(".claude-work");
        var (window, svm, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Retention);
            Settle(window, guard);
            Settle(window, guard);

            var open = Assert.Single(svm.ClaudeCodePage.Folders, f => f.HoldsOpen);
            var expander = Below<Expander>(window).Single(e => e.IsExpanded && e.DataContext == open);
            var card = CardOf(expander);
            var focused = Keyboard.FocusedElement as DependencyObject;
            Assert.True(focused is not null && card is not null && IsInside(focused, card),
                $"keyboard focus is on {Describe(focused)}, not inside the opened card {open.Display} (page: {window.Place})");
            Assert.Equal("HeaderSite", (focused as FrameworkElement)?.Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_three_glow_switches_are_named_by_their_labels() => AppHost.Run(() =>
    {
        // P8: Content and the spoken name are the same words, from SettingsNames.
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode);
            Settle(window, guard);
            var boxes = Below<CheckBox>(window).ToList();
            foreach (var label in new[] { SettingsNames.GlowWhenWaiting, SettingsNames.GlowWhenDone, SettingsNames.GlowNotInTerminal })
            {
                var box = Assert.Single(boxes, b => Equals(b.Content, label));
                Assert.Equal(label, NameOf(box));
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void An_unreadable_folders_card_shows_the_reason_and_no_statusline_or_hook_install() => AppHost.Run(() =>
    {
        // Plan decision 8 / T7-5: the reason is on the card, and nothing on it can act on the broken settings.json.
        Home(".claude-broken", "{ nope");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode);
            var row = svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-broken");
            row.HoldsOpen = true;
            Settle(window, guard);

            var expander = Below<Expander>(window).Single(e => e.DataContext == row);
            var card = CardOf(expander)!;
            var texts = Below<TextBlock>(card).Select(TextOf).ToList();
            Assert.Contains(texts, t => t.StartsWith("Settings:") && t.Contains(row.Holds.SettingsSummary, StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains(row.Holds.SettingsDetail, StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains(SettingsNames.FolderUnreadable, StringComparison.Ordinal));
            Assert.Contains(texts, t => t == "This folder's settings.json can't be read, so nothing is changed.");

            // No Install, Change or Update button shows for the statusline or the glow hook: both stay out of a settings.json they can't read.
            // "Install MCP server" is deliberately not asserted: it writes .claude.json, not the unreadable settings.json.
            Assert.DoesNotContain(Below<Button>(card), b => b.IsVisible && NameOf(b) is "Install Statusline" or "Install Claude Code glow hook");
            // The one switch on the card is the MCP read switch, which is Sanduhr's own setting and not in the folder.
            Assert.Equal([row.McpReadableName], Below<CheckBox>(card).Select(NameOf).ToList());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_meters_line_on_each_card_is_spoken_with_its_state() => AppHost.Run(() =>
    {
        Home(".claude-broken", "{ nope");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode);
            Settle(window, guard);
            foreach (var row in svm.ClaudeCodePage.Folders)
            {
                var card = CardOf(Below<Expander>(window).Single(e => e.DataContext == row))!;
                var line = Below<TextBlock>(card).Single(t => TextOf(t).StartsWith(SettingsNames.MetersSection + ":"));
                var spoken = NameOf(line);
                Assert.Contains(row.MetersText, spoken, StringComparison.Ordinal);
                Assert.Contains(row.Display, spoken, StringComparison.Ordinal);
            }
            Assert.Contains(SettingsNames.FolderUnreadable, NameOf(Below<TextBlock>(window).Single(t => TextOf(t).StartsWith(SettingsNames.MetersSection + ":") && TextOf(t).Contains(SettingsNames.FolderUnreadable))));
        }
        finally { window.Close(); }
    });

    // ---- the page flip (Task 12) ----

    /// <summary>An offline network that counts what was asked of it.</summary>
    private sealed class CountingOffline : System.Net.Http.HttpMessageHandler
    {
        public int Count;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            throw new System.Net.Http.HttpRequestException("offline in tests");
        }
    }

    [Fact]
    public void Landing_on_the_page_sends_no_request() => AppHost.Run(() =>
    {
        var net = new CountingOffline();
        Sanduhr.App.Services.WidgetCatalogService.HandlerOverride = () => net;
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            svm.ModsPage.FilterText = "palette";
            Settle(window, guard);
            Assert.Equal(0, net.Count);

            window.SelectPage(SettingsPage.Mods, SettingsIndex.Catalog);
            Settle(window, guard);
            var until = DateTime.UtcNow.AddSeconds(10);
            while ((svm.ModsPage.Busy || net.Count == 0) && DateTime.UtcNow < until) { Pump(); Thread.Sleep(15); }
            Assert.True(net.Count > 0, "opening the catalog asked nothing");
            Assert.True(svm.ModsPage.CatalogOpen);
            // P9: the fetch's outcome is the catalog's own line, drawn inside the Catalog section; list actions keep Status.
            Assert.Equal(WidgetCatalogClient.Offline, svm.ModsPage.CatalogStatus);
            Settle(window, guard);
            var catalog = Below<Expander>(window).Single(e => e.Name == "CatalogSection");
            Assert.Contains(Below<TextBlock>(catalog), t => t.IsVisible && TextOf(t) == WidgetCatalogClient.Offline);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Old_widget_and_mods_links_land_on_the_new_page() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Widgets, SettingsIndex.WidgetsCatalog);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);
            Assert.True(Below<Expander>(window).Single(e => e.Name == "CatalogSection").IsExpanded);

            window.SelectPage(SettingsPage.Widgets);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);

            window.SelectPage(SettingsPage.Widgets, SettingsIndex.WidgetsInstalled);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);

            window.SelectPage(SettingsPage.ClaudeCode, SettingsIndex.Meters);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Meters), window.Place);

            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);

            var tabs = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().ToList();
            Assert.DoesNotContain(tabs, t => (string)t.Tag == "Widgets");
            var mods = Assert.Single(tabs, t => (string)t.Tag == "Mods");
            Assert.Equal(SettingsNames.ModsAndWidgetsTitle, mods.Header);
            Assert.Equal(SettingsNames.ModsAndWidgetsTitle, NameOf(mods));
            Assert.Same(svm.ModsPage, mods.DataContext);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void No_folder_says_so_and_links_to_the_Claude_Code_cards() => AppHost.Run(() =>
    {
        // P20: the empty meters line is a sentence and a real link, not prose that names a page.
        Directory.Delete(Path.Combine(Profile, ".claude"), true);
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            Assert.False(svm.ModsPage.HasMeterFolders);
            var page = Below<ModsPage>(window).Single();
            Assert.Contains(Below<TextBlock>(page), t => t.IsVisible && TextOf(t) == SettingsNames.NoFolderInMods);
            var link = Assert.Single(Below<Button>(page), b => b.IsVisible && Equals(b.Content, SettingsNames.ClaudeCodeLinkTitle));
            Assert.Equal("claudecode#folders", SettingsLinkTo.GetLink(link));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_filter_chips_are_named_radios_in_a_named_group_and_filter_the_list() => AppHost.Run(() =>
    {
        Home(".claude-work");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var page = Below<ModsPage>(window).Single();
            var chips = Below<RadioButton>(page).ToList();
            Assert.Equal([SettingsNames.FilterAll, SettingsNames.FilterClaudeCode, SettingsNames.FilterWidgets], chips.Select(NameOf));
            Assert.All(chips, c => Assert.True(c.Focusable && c.IsTabStop && c.IsVisible, NameOf(c)));
            var group = Below<Sanduhr.App.Controls.RadioGroupPanel>(page).Single();
            Assert.Equal(SettingsNames.ModsShowGroup, NameOf(group));
            Assert.True(chips[0].IsChecked);

            chips[2].IsChecked = true;   // what a click or Space does
            Settle(window, guard);
            Assert.Equal(InstalledFilter.Widgets, svm.ModsPage.Filter);
            Assert.All(svm.ModsPage.Installed, r => Assert.True(r.IsPack));

            var box = Assert.Single(Below<TextBox>(page), b => NameOf(b) == SettingsNames.FilterBoxName);
            Assert.True(box.IsTabStop);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_meters_change_reloads_the_cards_and_a_card_change_reloads_the_page() => AppHost.Run(() =>
    {
        var home = Home(".claude-work", "{}");
        var byHand = Path.Combine(Root, "elsewhere", "cc");
        Directory.CreateDirectory(byHand);
        File.WriteAllText(Path.Combine(byHand, "settings.json"), "{}");
        var (widget, _) = ScratchWidget("Personal");
        widget.SaveCcFoldersAdded([byHand]);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            svm.ModsPage.AskMetersConsent = _ => true;
            var row = svm.ModsPage.Meters.Single(m => m.Display == @"~\.claude-work");
            var card = svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-work");
            Assert.Equal(SettingsNames.FolderNotInstalled, card.MetersText);

            row.ToggleCommand.Execute(null);   // installs into that folder

            Assert.Contains("CLAUDE_CODE_PLUGIN_DIRS", File.ReadAllText(Path.Combine(home, "settings.json")));
            Assert.Equal(SettingsNames.FolderOn, svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-work").MetersText);

            // The other way: a folder the Claude Code page forgets leaves Meters above the prompt.
            Assert.Contains(svm.ModsPage.Meters, m => ClaudeCodeFolders.Same(m.Folder, byHand));
            var handRow = svm.ClaudeCodePage.Folders.Single(f => ClaudeCodeFolders.Same(f.Path, byHand));
            svm.ClaudeCodePage.ForgetFolderCommand.Execute(handRow);
            Assert.DoesNotContain(svm.ModsPage.Meters, m => ClaudeCodeFolders.Same(m.Folder, byHand));
        }
        finally { window.Close(); }
    });
}
