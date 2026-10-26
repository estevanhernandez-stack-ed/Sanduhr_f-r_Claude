using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: the Privacy page sits after Alerts, shows every row of What leaves this PC and What Claude can change, and
/// its links land where they say, the catalog's never opening the catalog.</summary>
[Collection("SettingsWindow")]
public sealed class PrivacyPageTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
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

    private static DependencyObject PageOf(SettingsWindow window, SettingsPage page) =>
        (DependencyObject)((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString()).Content;

    private static Button Link(DependencyObject root, string spoken) => Below<Button>(root).Single(b => b.IsVisible && Spoken(b) == spoken);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void Privacy_sits_after_Alerts_and_lands_on_its_sections() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var tags = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Select(t => (string)t.Tag).ToList();
            Assert.Equal(tags.IndexOf(nameof(SettingsPage.Alerts)) + 1, tags.IndexOf(nameof(SettingsPage.Privacy)));
            foreach (var anchor in new[] { SettingsIndex.Network, SettingsIndex.Agents })
            {
                window.SelectPage(SettingsPage.Privacy, anchor);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(SettingsPage.Privacy, anchor), window.Place);
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Every_destination_is_on_the_page_with_its_link() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            var page = PageOf(window, SettingsPage.Privacy);
            var texts = Below<TextBlock>(page).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            foreach (var row in NetworkDisclosure.All)
            {
                Assert.Contains(row.Destination, texts);
                Assert.NotNull(Link(page, row.OffLinkName));
            }
            Assert.Contains(SettingsNames.NothingElseLeaves, texts);
            Assert.Contains(SettingsNames.NetWebView2Note, texts);
            Assert.Contains(SettingsNames.NetYourBrowserNote, texts);

            Click(Link(page, NetworkDisclosure.Find("open-meteo")!.OffLinkName));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, SettingsIndex.WeatherPlace), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_catalog_row_lands_on_the_installed_list_and_fetches_nothing() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            Click(Link(PageOf(window, SettingsPage.Privacy), NetworkDisclosure.Find("widget-catalog")!.OffLinkName));
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);
            Assert.False(svm.ModsPage.CatalogOpen);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_consent_changed_on_its_page_shows_on_the_next_visit() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);
            Assert.Equal(SettingsNames.AgentOff, svm.Privacy.Agents[0].State);

            window.SelectPage(SettingsPage.Watchers, SettingsIndex.Watchers);
            Settle(window, guard);
            svm.Desk.WatchersAgents = true;   // the Watchers page's switch
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);

            Assert.Equal(SettingsNames.AgentOn, svm.Privacy.Agents[0].State);
            Assert.Contains(Below<TextBlock>(PageOf(window, SettingsPage.Privacy)), t => t.IsVisible && t.Text == SettingsNames.AgentOn);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Every_consent_is_on_the_page_and_the_foot_links_to_About() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);
            var page = PageOf(window, SettingsPage.Privacy);
            var texts = Below<TextBlock>(page).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            foreach (var row in svm.Privacy.Agents)
            {
                Assert.Contains(row.Name, texts);
                Assert.NotNull(Link(page, row.LinkName));
            }
            Assert.Contains(svm.Privacy.McpStatus, texts);
            Assert.NotNull(Link(page, svm.Privacy.McpStatusLinkName));

            Click(Link(page, svm.Privacy.AboutLinkName));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.About, SettingsIndex.Where), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_privacy_policy_opens_through_the_windows_one_link_command() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            var policy = Link(PageOf(window, SettingsPage.Privacy), SettingsNames.ReadPrivacyPolicy);
            // Never executed here: it would open a browser.
            Assert.Same(svm.OpenLinkCommand, policy.Command);
            Assert.Equal(NetworkDisclosure.PrivacyPolicyUrl, policy.CommandParameter);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Hosts_are_in_a_code_font_only_when_they_are_host_names_and_the_MCP_line_is_not_a_blank_before_Reload() => AppHost.Run(() =>
    {
        var (window, svm, widget, _) = Open();
        try
        {
            var page = new PrivacyPage { DataContext = svm.Privacy };   // never visited: Reload has not run
            page.Measure(new Size(600, 4000));
            page.Arrange(new Rect(0, 0, 600, 4000));
            page.UpdateLayout();
            var blocks = Below<TextBlock>(page).ToList();

            Assert.Equal("", svm.Privacy.McpStatus);
            var blank = blocks.Where(t => t.Text == "" && t.Inlines.Count == 0).ToList();
            Assert.Single(blank);   // the MCP status, still empty
            Assert.All(blank, t => Assert.Equal(Visibility.Collapsed, t.Visibility));
            foreach (var row in NetworkDisclosure.All)
            {
                var hosts = blocks.FirstOrDefault(t => t.Text == row.Hosts && t.FontSize == 11);
                Assert.True(hosts is not null, $"no Hosts block for {row.Id}: [{row.Hosts}] of {blocks.Count} blocks");
                Assert.Equal(row.HostsAreNames, hosts.FontFamily.Source.Contains("Cascadia"));
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
