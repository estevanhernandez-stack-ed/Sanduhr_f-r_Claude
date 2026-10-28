using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: the Done line, and Privacy and About, Updates read and land the way the spec says.</summary>
[Collection("SettingsWindow")]
public sealed class Slice6PagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        var window = new SettingsWindow(svm);
        return (window, svm, widget, NoActivate.Show(window));
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
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

    private static string Spoken(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? "";

    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    private sealed class CountingProbe : IUpdateProbe
    {
        public int Asked;
        public Task<UpdateProbeResult> FindAsync()
        {
            Asked++;
            return Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        }
    }

    /// <summary>Done (spec, slice 6): "privacy" in search opens the Privacy page, typed and opened with Enter, as a person does.</summary>
    [Fact]
    public void Privacy_in_search_opens_the_Privacy_page() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            Settle(window, guard);
            var tabs = (TabControl)window.FindName("Tabs");
            var box = (TextBox)tabs.Template.FindName("SearchBox", tabs);
            svm.Search.Query = "privacy";
            Settle(window, guard);
            Assert.Equal((SettingsPage.Privacy, SettingsIndex.Network), (svm.Search.First!.Page, svm.Search.First.Anchor));

            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Privacy, SettingsIndex.Network), window.Place);
            Assert.Equal(nameof(SettingsPage.Privacy), (string)((TabItem)tabs.SelectedItem).Tag);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Done (spec, slice 6): with the switch off, no update probe runs. It is unticked on About, then the checker App builds (over
    /// the same store) runs at launch and on the tick two days later.</summary>
    [Fact]
    public void With_the_switch_off_no_update_probe_runs() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.About, SettingsIndex.Updates);
            Settle(window, guard);
            Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.CheckForUpdatesDaily).IsChecked = false;

            var onDisk = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(new Sanduhr.Core.Paths().SettingsFile))!;
            Assert.False((bool?)onDisk["update_check"]);   // the key a person's click wrote, read back from the file

            var probe = new CountingProbe();
            var now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
            var stamp = Path.Combine(Root, "local", "last-update-check.txt");
            var checker = new UpdateChecker(new SettingsStore(new Paths()).LoadUpdateCheck, probe, stamp, (_, _) => { }, () => now);
            Assert.True(checker.RunIfDueAsync().IsCompleted);   // off: it returns before asking anything
            now = now.AddDays(2);
            checker.Tick();

            Assert.Equal(0, probe.Asked);
            Assert.Null(checker.Running);
            Assert.False(File.Exists(stamp));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Every button and box on Privacy and About, Updates is spoken with its visible label first (WF34), item templates included,
    /// and no name on either page leaks a type or a record (decision 21). Leaks are collected page by page (slice 5, P19).</summary>
    [Fact]
    public void Every_control_on_the_slice_6_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var bad = new List<string>();
            var leaks = new List<string>();
            foreach (var (page, anchor, wholePage) in new (SettingsPage, string, bool)[]
            {
                (SettingsPage.Privacy, SettingsIndex.Network, true), (SettingsPage.About, SettingsIndex.Updates, false),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());
                DependencyObject root = wholePage ? (DependencyObject)tab.Content : SettingsAnchor.Find((DependencyObject)tab.Content, anchor)!;
                foreach (var b in Below<ButtonBase>(root).Where(b => b.IsVisible && b.Content is string))
                {
                    var name = Spoken(b);
                    if (name.Length == 0 || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
                leaks.AddRange(LeaksIn(NamesOf(window)).Select(l => $"{page}: {l}"));
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    /// <summary>Done (spec, slice 6): every link on Privacy lands on its section, and the widget catalog row lands on the installed list,
    /// never opening the Catalog.</summary>
    [Fact]
    public void Every_Privacy_link_lands_on_its_section_and_the_catalog_link_never_opens_the_Catalog() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            var wrong = new List<string>();
            foreach (var row in NetworkDisclosure.All)
            {
                window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
                Settle(window, guard);
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == nameof(SettingsPage.Privacy));
                var link = Below<Button>((DependencyObject)tab.Content).Single(b => b.IsVisible && Spoken(b) == row.OffLinkName);
                Click(link);
                Settle(window, guard);
                if (window.Place != new SettingsLink(row.OffPage, row.OffAnchor))
                    wrong.Add($"{row.Id}: landed on {window.Place}, not {row.OffPage}#{row.OffAnchor}");
                Assert.False(svm.ModsPage.CatalogOpen, $"{row.Id}'s link opened the Catalog");
            }
            Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>A landing on What leaves this PC puts the keyboard on the first row's link; on About, Updates, on the switch.</summary>
    [DesktopFact]
    public void A_landing_puts_the_keyboard_on_the_sections_first_control() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            guard.RouteKeyboard();
            foreach (var (page, anchor, expected) in new[]
            {
                (SettingsPage.Privacy, SettingsIndex.Network, NetworkDisclosure.All[0].OffLinkName),
                (SettingsPage.About, SettingsIndex.Updates, SettingsNames.CheckForUpdatesDaily),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var focused = Keyboard.FocusedElement as UIElement;
                var name = focused is null ? "" : Spoken(focused);
                Assert.True(name == expected,
                    $"focus is on {focused?.GetType().Name ?? "nothing"} \"{name}\" on {window.Place}, not \"{expected}\"");
                guard.Check();
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });
}