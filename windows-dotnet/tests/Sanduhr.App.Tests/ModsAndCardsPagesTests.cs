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

    private string Home(string name, string settings = "{}")
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

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
    public void An_unreadable_folders_card_shows_the_reason_and_no_install_or_switch() => AppHost.Run(() =>
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
            Assert.DoesNotContain(Below<Button>(card), b => b.IsVisible && NameOf(b) is "Install Statusline" or "Install Claude Code glow hook");
            // The one switch on the card is the MCP read switch, which is Sanduhr's own setting and not in the folder.
            Assert.Equal([row.McpReadableName], Below<CheckBox>(card).Select(NameOf).ToList());
        }
        finally { window.Close(); }
    });
}
