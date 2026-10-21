using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using Sanduhr.App.Controls;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Settings sidebar's search box, results list and Quit button sit inside the TabControl's
/// template, which WPF's stock TabControlAutomationPeer does not walk. These build a real
/// SidebarTabControl with a template of the same shape, ask UI Automation for its children the way
/// Narrator does, and check the named parts are there, in screen order, with their names.
/// </summary>
public class SidebarTabControlAutomationTests
{
    private const string Template = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="TabControl">
          <Grid>
            <DockPanel>
              <Button x:Name="QuitButton" DockPanel.Dock="Bottom" Content="Quit Sanduhr"
                      AutomationProperties.Name="Quit Sanduhr" />
              <TextBox x:Name="SearchBox" DockPanel.Dock="Top" AutomationProperties.Name="Search settings" />
              <Grid>
                <StackPanel IsItemsHost="True" />
                <Grid>
                  <ListBox x:Name="SearchResults" AutomationProperties.Name="Search results" />
                  <TextBlock x:Name="NoResultsText" Text="No setting by that name." />
                </Grid>
              </Grid>
            </DockPanel>
            <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" />
          </Grid>
        </ControlTemplate>
        """;

    private static SidebarTabControl Build()
    {
        var tabs = new SidebarTabControl
        {
            Template = (ControlTemplate)XamlReader.Parse(Template),
        };
        tabs.Items.Add(new TabItem { Header = "General", Content = new CheckBox { Content = "Page content" } });
        tabs.Items.Add(new TabItem { Header = "About", Content = new Border() });
        tabs.SelectedIndex = 0;
        tabs.ApplyTemplate();
        return tabs;
    }

    private static TabControl BuildStock()
    {
        var tabs = new TabControl { Template = (ControlTemplate)XamlReader.Parse(Template) };
        tabs.Items.Add(new TabItem { Header = "General", Content = new CheckBox { Content = "Page content" } });
        tabs.Items.Add(new TabItem { Header = "About", Content = new Border() });
        tabs.SelectedIndex = 0;
        tabs.ApplyTemplate();
        return tabs;
    }

    [Fact]
    public void Peer_is_the_sidebar_peer()
    {
        Sta.Run(() => Assert.IsType<SidebarTabControlAutomationPeer>(
            UIElementAutomationPeer.CreatePeerForElement(Build())));
    }

    [Fact]
    public void Search_box_results_and_Quit_are_exposed_with_their_names()
    {
        Sta.Run(() =>
        {
            var children = UIElementAutomationPeer.CreatePeerForElement(Build()).GetChildren();

            var search = Assert.Single(children, c => c is TextBoxAutomationPeer);
            Assert.Equal("Search settings", search.GetName());

            var results = Assert.Single(children, c => c is ListBoxAutomationPeer);
            Assert.Equal("Search results", results.GetName());

            var quit = Assert.Single(children, c => c is ButtonAutomationPeer);
            Assert.Equal("Quit Sanduhr", quit.GetName());
        });
    }

    [Fact]
    public void The_stock_children_are_kept_and_the_order_matches_the_screen()
    {
        Sta.Run(() =>
        {
            var children = UIElementAutomationPeer.CreatePeerForElement(Build()).GetChildren();

            int search = children.FindIndex(c => c is TextBoxAutomationPeer);
            int results = children.FindIndex(c => c is ListBoxAutomationPeer);
            int firstTab = children.FindIndex(c => c is ItemAutomationPeer);
            int lastTab = children.FindLastIndex(c => c is ItemAutomationPeer);
            int quit = children.FindIndex(c => c is ButtonAutomationPeer);

            Assert.Equal(2, children.Count(c => c is ItemAutomationPeer));
            Assert.True(search >= 0 && search < results, "search box first, then the results");
            Assert.True(results < firstTab, "results above the page list");
            Assert.True(lastTab < quit, "Quit below the page list");
        });
    }

    [Fact]
    public void Everything_the_stock_peer_reports_is_still_reported()
    {
        Sta.Run(() =>
        {
            var stock = UIElementAutomationPeer.CreatePeerForElement(BuildStock()).GetChildren();
            var ours = UIElementAutomationPeer.CreatePeerForElement(Build()).GetChildren();

            // Take the four named parts out and the two lists are the same, in the same order.
            var rest = ours.Where(c => c is not (TextBoxAutomationPeer or ListBoxAutomationPeer or TextBlockAutomationPeer or ButtonAutomationPeer));
            Assert.Equal(stock.Select(c => c.GetType()), rest.Select(c => c.GetType()));
            Assert.DoesNotContain(stock, c => c is TextBoxAutomationPeer or ButtonAutomationPeer);
            Assert.True(ours.Count > stock.Count);
        });
    }

    [Fact]
    public void Template_parts_report_the_tab_control_as_their_parent()
    {
        Sta.Run(() =>
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(Build());
            foreach (var child in peer.GetChildren())
                Assert.Same(peer, child.GetParent());
        });
    }

    [Fact]
    public void A_control_without_the_parts_still_reports_the_stock_children()
    {
        Sta.Run(() =>
        {
            var tabs = new SidebarTabControl();
            tabs.Items.Add(new TabItem { Header = "General", Content = new Border() });
            var children = UIElementAutomationPeer.CreatePeerForElement(tabs).GetChildren();
            Assert.Contains(children, c => c is ItemAutomationPeer);
        });
    }

    [Fact]
    public void Settings_window_uses_the_control_and_declares_every_part_name()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));

        Assert.Contains("<controls:SidebarTabControl x:Name=\"Tabs\"", xaml);
        Assert.DoesNotContain("<TabControl ", xaml);
        foreach (var part in SidebarTabControl.LeadingParts.Concat(SidebarTabControl.TrailingParts))
            Assert.True(Regex.IsMatch(xaml, $"x:Name=\"{part}\""), $"SettingsWindow.xaml no longer declares x:Name=\"{part}\"");
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }
}
