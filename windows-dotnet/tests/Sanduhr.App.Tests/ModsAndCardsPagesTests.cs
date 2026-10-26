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
    public void A_filter_that_empties_the_list_is_announced() => AppHost.Run(() =>
    {
        // Review M3: the empty lines are live regions, and a change raises LiveRegionChanged (LiveSetting alone announces nothing).
        Home(".claude-work", """{"enabledPlugins":{"a@m":true}}""");
        var announced = new List<string>();
        LiveRegion.Raised = e => announced.Add((e as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.True(svm.ModsPage.HasInstalled);
            Assert.Empty(announced);   // nothing is announced for the page's first state

            svm.ModsPage.FilterText = "nothing is called this";
            Settle(window, guard);

            Assert.Equal([SettingsNames.NothingMatches], announced);
            var page = Below<ModsPage>(window).Single();
            var line = Below<TextBlock>(page).Single(t => t.Text == SettingsNames.NothingMatches);
            Assert.True(line.IsVisible);
            Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite, System.Windows.Automation.AutomationProperties.GetLiveSetting(line));
            var empty = Below<TextBlock>(page).Single(t => t.Text == SettingsNames.InstalledEmpty);
            Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite, System.Windows.Automation.AutomationProperties.GetLiveSetting(empty));
        }
        finally
        {
            LiveRegion.Raised = null;
            window.Close();
        }
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

    private static void Press(Button button) =>
        ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();

    [Fact]
    public void The_companions_get_one_link_lands_on_the_catalog_showing_sprites() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.GetACompanion);

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);
            Assert.Equal("sprite", svm.ModsPage.CatalogKind);
            Assert.True(svm.ModsPage.CatalogOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Themes_link_lands_on_the_installed_list_showing_widget_packs() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            var paths = ScratchPaths();
            svm.Themes.Widgets = () => InstalledPackKit.Store(paths);   // never the real widgets folder
            svm.Themes.RefreshThemePacks();
            window.SelectPage(SettingsPage.Themes);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.LinkTitle(SettingsPage.Mods));
            Assert.Contains(Below<TextBlock>(window), t => t.Text == SettingsNames.ThemePacksLine(0));

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);
            Assert.Equal(InstalledFilter.Widgets, svm.ModsPage.Filter);
        }
        finally { window.Close(); }
    });

    // ---- Done (spec, slice 4) and the two pages by keyboard and screen reader (Task 14) ----

    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    private const string FixturePalette = "plain · Fixture palette";

    /// <summary>Done, part 1: an installed palette is one click from where it shows.</summary>
    [Fact]
    public void An_installed_palette_is_one_click_from_where_it_shows() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");   // the dev profile's widgets folder (SANDUHR_DEV_PROFILE, under %TEMP%)
        var (window, svm, guard) = Open("Personal");
        try
        {
            svm.Desk.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Light);
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b => b.IsVisible && NameOf(b) == SettingsNames.WhereLinkFor(SettingsNames.LinkTitle(SettingsPage.DeskLook), "Fixture palette"));

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskInk), window.Place);
            // The anchor is on the page, and the pack's palette is in the picker that page shows.
            Assert.Single(Below<ComboBox>(window), c => c.IsVisible && c.ItemsSource is IEnumerable<string> items && items.Contains(FixturePalette));
        }
        finally { window.Close(); }
    });

    /// <summary>Done, part 2: switching a pack off removes it from its picker, with no reopen.</summary>
    [Fact]
    public void Switching_a_pack_off_removes_it_from_its_picker() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, svm, guard) = Open("Personal");
        try
        {
            svm.Desk.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Light);
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.Contains(FixturePalette, svm.Desk.InkPresetNames);
            var on = Below<CheckBox>(window).Single(c => c.IsVisible && NameOf(c) == SettingsNames.PackOnFor("Fixture palette"));
            Assert.True(on.IsChecked);

            on.Command.Execute(on.CommandParameter);   // what a click runs: the bound command
            Settle(window, guard);

            Assert.False(on.IsChecked);
            Assert.DoesNotContain(FixturePalette, svm.Desk.InkPresetNames);
            Assert.False(InstalledPackKit.Store(new Paths()).Load().Find("good-palette")!.Enabled);

            // And the picker itself, on the page that shows it.
            window.SelectPage(SettingsPage.DeskLook, SettingsIndex.DeskInk);
            Settle(window, guard);
            var pickers = Below<ComboBox>(window).Where(c => c.IsVisible && c.ItemsSource is IEnumerable<string>).ToList();
            Assert.NotEmpty(pickers);
            Assert.DoesNotContain(pickers, c => ((IEnumerable<string>)c.ItemsSource).Contains(FixturePalette));
        }
        finally { window.Close(); }
    });

    /// <summary>Why a visible, enabled control cannot act, or null when it can: a command that can run, a link that resolves, or a binding
    /// to the model. A toggle in an Expander's header acts through the template.</summary>
    private static string? CantAct(Control c)
    {
        bool Bound(DependencyProperty p) => System.Windows.Data.BindingOperations.GetBindingExpressionBase(c, p) is { HasError: false };
        switch (c)
        {
            case System.Windows.Controls.Primitives.ToggleButton { TemplatedParent: Expander }:
                return null;
            case System.Windows.Controls.Primitives.ButtonBase b when b.Command is { } cmd:
                return cmd.CanExecute(b.CommandParameter) ? null : "its command can't run";
            case System.Windows.Controls.Primitives.ButtonBase b when SettingsLinkTo.GetLink(b) is { Length: > 0 } link:
                return SettingsIndex.ParseLink(link) is null ? $"its link \"{link}\" doesn't resolve" : null;
            case System.Windows.Controls.Primitives.ToggleButton:
                return Bound(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty) ? null : "it has no command and no binding";
            case System.Windows.Controls.Primitives.ButtonBase:
                return "it has no command and no link";
            case ComboBox cb:
                return !Bound(System.Windows.Controls.Primitives.Selector.SelectedItemProperty) ? "its selection is not bound"
                    : cb.Items.Count == 0 ? "it has nothing to choose" : null;
            case TextBox t:
                return t.IsReadOnly ? "it is read-only" : Bound(TextBox.TextProperty) ? null : "its text is not bound";
            default:
                return null;
        }
    }

    private static bool IsActor(Control c) => c is System.Windows.Controls.Primitives.ButtonBase or ComboBox or TextBox;

    private static IEnumerable<string> ControlsThatCantAct(DependencyObject root) =>
        Below<Control>(root).Where(c => IsActor(c) && c.IsVisible && c.IsEnabled)
            .Select(c => (Control: c, Why: CantAct(c))).Where(x => x.Why is not null)
            .Select(x => $"{x.Control.GetType().Name} \"{NameOf(x.Control)}\": {x.Why}");

    private static List<string> ShownNames(DependencyObject root) =>
        [.. Below<Control>(root).Where(c => IsActor(c) && c.IsVisible).Select(NameOf)];

    private string MissingEntry() =>
        Path.Combine(Root, "dev", "external", "mods", "v0.1.0-20200101000000", "sanduhr-meters").Replace("\\", "\\\\");

    /// <summary>Done, part 3 (Principle 5): no row shows a control that can't act. Every Installed row, every meters row (Missing and
    /// Unreadable included) and every Claude Code card: each shown, enabled control acts; read-only, Unreadable and Missing rows hide what can't.</summary>
    [Fact]
    public void No_row_shows_a_control_that_cant_act() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal", "{\"enabledPlugins\":{\"alpha@m\":true}}");
        var cache = Path.Combine(Root, "cache", "alpha");
        Directory.CreateDirectory(Path.Combine(cache, ".claude-plugin"));
        File.WriteAllText(Path.Combine(cache, ".claude-plugin", "plugin.json"), """{"name":"alpha","version":"1.0.0"}""");
        Directory.CreateDirectory(Path.Combine(personal, "plugins"));
        File.WriteAllText(Path.Combine(personal, "plugins", "installed_plugins.json"),
            "{\"version\":2,\"plugins\":{\"alpha@m\":[{\"installPath\":\"" + cache.Replace("\\", "\\\\") + "\",\"version\":\"1.0.0\"}]}}");
        Home(".claude-broken", "{ nope");
        Home(".claude-missing", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + MissingEntry() + "\"}}");
        InstalledPackKit.Install(new Paths(), "good-effects");
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var page = Below<ModsPage>(window).Single();

            // The fixture reaches every state this test is about.
            Assert.Contains(svm.ModsPage.Meters, m => m.State == FolderState.Unreadable);
            Assert.Contains(svm.ModsPage.Meters, m => m.State == FolderState.Missing);
            Assert.Contains(svm.ModsPage.Installed, r => r.IsReadOnly && !r.IsSanduhrMod);
            Assert.Contains(svm.ModsPage.Installed, r => r.IsSanduhrMod);
            Assert.Contains(svm.ModsPage.Installed, r => r.Id == "good-effects");

            // Meters rows: each control shows exactly when its row can act on it (Unreadable shows none).
            var meterRows = Below<ContentPresenter>(page).Where(p => p.Content is MetersFolderRowViewModel).ToList();
            Assert.Equal(svm.ModsPage.Meters.Count, meterRows.Count);
            foreach (var presenter in meterRows)
            {
                var vm = (MetersFolderRowViewModel)presenter.Content;
                var shown = ShownNames(presenter);
                if (vm.State == FolderState.Unreadable) Assert.Empty(shown);
                Assert.Equal(vm.CanSwitch, shown.Contains(vm.SwitchName));
                Assert.Equal(vm.CanUpdate, shown.Contains(vm.UpdateName));
                Assert.Equal(vm.CanRemove, shown.Contains(vm.RemoveName));
            }

            // Installed rows: a read-only plugin has no control; Sanduhr's mod has only its link up; a pack has its switch and Uninstall.
            var items = Below<ListBoxItem>(page).Where(i => i.Content is InstalledRowViewModel).ToList();
            Assert.Equal(svm.ModsPage.Installed.Count, items.Count);
            foreach (var item in items)
            {
                var vm = (InstalledRowViewModel)item.Content;
                var shown = ShownNames(item);
                if (vm.IsPack)
                {
                    Assert.Contains(vm.SwitchName, shown);
                    Assert.Contains(vm.UninstallName, shown);
                    Assert.Equal(vm.CanUseOwnArt, shown.Contains(vm.UseOwnArtName));
                    Assert.Equal(vm.HasWhereLink, shown.Contains(vm.WhereLinkName));
                    Assert.Equal(2 + (vm.CanUseOwnArt ? 1 : 0) + (vm.HasWhereLink ? 1 : 0), shown.Count);
                }
                else if (vm.IsSanduhrMod)
                    Assert.Equal([SettingsNames.MetersLinkUp], shown);
                else
                    Assert.True(shown.Count == 0, $"{vm.Name} is read-only and shows {string.Join(", ", shown)}");
            }
            var effects = items.Single(i => ((InstalledRowViewModel)i.Content).Id == "good-effects");
            Assert.DoesNotContain(Below<Button>(effects), b => b.IsVisible && SettingsLinkTo.GetLink(b) is { Length: > 0 });   // no link: nothing draws effects

            Assert.Empty(ControlsThatCantAct(page));

            // The Claude Code cards: every report open, every piece, then the page's own controls.
            window.SelectPage(SettingsPage.ClaudeCode);
            foreach (var card in svm.ClaudeCodePage.Folders) card.HoldsOpen = true;
            Settle(window, guard);
            Settle(window, guard);
            var cards = Below<ClaudeCodePage>(window).Single();
            Assert.Empty(ControlsThatCantAct(cards));
            foreach (var row in svm.ClaudeCodePage.Folders)
            {
                var card = CardOf(Below<Expander>(cards).Single(e => e.DataContext == row))!;
                var shown = ShownNames(card);
                if (row.Holds.SettingsUnreadable)
                    Assert.DoesNotContain(shown, n => n.Contains("Statusline", StringComparison.Ordinal) || n.Contains("hook", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(row.IsAdded, shown.Contains(row.ForgetName));
                Assert.Equal(row.Discovered, shown.Contains(row.McpReadableName));
            }
        }
        finally { window.Close(); }
    });

    /// <summary>Every button and box on the two pages is spoken with its visible label first (WF34; WCAG 2.5.3), item templates included;
    /// every Installed item is a named stop.</summary>
    [Fact]
    public void Every_control_on_the_two_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        Home(".claude-work");
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, svm, guard) = Open("Personal");
        try
        {
            var bad = new List<string>();
            foreach (var page in new[] { SettingsPage.ClaudeCode, SettingsPage.Mods })
            {
                window.SelectPage(page);
                Settle(window, guard);
                if (page == SettingsPage.ClaudeCode) foreach (var f in svm.ClaudeCodePage.Folders) f.HoldsOpen = true;
                Settle(window, guard);
                DependencyObject root = page == SettingsPage.ClaudeCode ? Below<ClaudeCodePage>(window).Single() : Below<ModsPage>(window).Single();
                var buttons = Below<System.Windows.Controls.Primitives.ButtonBase>(root).Where(b => b.IsVisible && b.Content is string).ToList();
                Assert.NotEmpty(buttons);
                foreach (var b in buttons)
                {
                    var name = NameOf(b);
                    if (name.Length == 0 || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
                if (page == SettingsPage.Mods)
                {
                    var items = Below<ListBoxItem>(root).Where(i => i.Content is InstalledRowViewModel).ToList();
                    Assert.NotEmpty(items);
                    foreach (var i in items)
                        if (NameOf(i) != ((InstalledRowViewModel)i.Content).RowName || !i.Focusable || !i.IsTabStop)
                            bad.Add($"{page}: Installed item \"{((InstalledRowViewModel)i.Content).Name}\" is spoken \"{NameOf(i)}\" (focusable {i.Focusable}, tab stop {i.IsTabStop})");
                }
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.Empty(LeaksIn(NamesOf(window)));
        }
        finally { window.Close(); }
    });

    /// <summary>A landing on Meters above the prompt puts the keyboard on the first folder's switch.</summary>
    [DesktopFact]
    public void A_landing_on_meters_focuses_the_first_folders_switch() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            var focused = Keyboard.FocusedElement as FrameworkElement;
            Assert.True(focused is CheckBox, $"focus is on {Describe(focused)} on {window.Place}");
            Assert.Equal(SettingsNames.MetersOnFor(svm.ModsPage.Meters[0].Display), NameOf(focused!));
            guard.Check();
        }
        finally { window.Close(); }
    });

    /// <summary>A landing on the Installed list puts the keyboard inside that section, never above it or on the page's top.</summary>
    [DesktopFact]
    public void A_landing_on_the_installed_list_focuses_inside_the_section() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, _, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Settle(window, guard);
            var focused = Keyboard.FocusedElement as DependencyObject;
            var page = Below<ModsPage>(window).Single();
            var section = VisualTreeHelper.GetParent(Below<TextBlock>(page).Single(t => t.Text == SettingsNames.InstalledSection));
            Assert.True(focused is not null && IsInside(focused, section),
                $"focus is on {Describe(focused)} on {window.Place}, not inside the Installed section");
            guard.Check();
        }
        finally { window.Close(); }
    });
}
