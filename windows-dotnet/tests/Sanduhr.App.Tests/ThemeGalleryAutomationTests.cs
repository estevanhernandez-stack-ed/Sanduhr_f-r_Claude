using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// #316: Settings, Themes, through UI Automation. Selecting a theme the way a screen reader or voice access does
/// (<c>SelectionItemPattern.Select()</c>) must apply it, not only move the selection; only a mouse click did.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ThemeGalleryAutomationTests : AppWindowFixture
{
    private static IEnumerable<AutomationPeer> ThemeItems(ListBox gallery)
    {
        gallery.UpdateLayout();
        Pump();
        return UIElementAutomationPeer.CreatePeerForElement(gallery).GetChildren() ?? [];
    }

    [Fact]
    public void Selecting_a_theme_through_its_automation_peer_applies_it() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Themes");
            window.UpdateLayout();
            Pump();
            var gallery = (ListBox)window.FindName("ThemeGallery");

            var target = widget.Themes.First(t => !t.IsActive);
            // Found by its item, not its name: GitHub's runner gives list items no names (see DesktopFact).
            var peer = ThemeItems(gallery).OfType<ItemAutomationPeer>().Single(p => ReferenceEquals(p.Item, target));
            Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
            var item = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));

            item.Select();
            Pump();

            Assert.Equal(target.Key, widget.ActiveThemeKey);
            Assert.True(target.IsActive);
            Assert.True(item.IsSelected);
            Assert.Single(widget.Themes, t => t.IsActive);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_active_theme_reads_as_the_selected_item_and_follows_a_switch_made_elsewhere() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Themes");
            window.UpdateLayout();
            Pump();
            var gallery = (ListBox)window.FindName("ThemeGallery");

            Assert.Equal(widget.ActiveThemeKey, ((ThemeStripItemViewModel)gallery.SelectedItem).Key);

            var other = widget.Themes.First(t => !t.IsActive);
            widget.ApplyThemeByKey(other.Key); // the widget's own strip, or propose_theme
            Pump();

            Assert.Same(other, gallery.SelectedItem);
            var selected = ThemeItems(gallery).OfType<ItemAutomationPeer>()
                .Where(p => p.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider { IsSelected: true })
                .Select(p => p.Item);
            Assert.Equal([other], selected);
        }
        finally { window.Close(); }
    });

    [DesktopFact]
    public void Each_theme_item_is_named_for_its_theme() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        var window = new SettingsWindow(new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Themes");
            window.UpdateLayout();
            Pump();
            var names = ThemeItems((ListBox)window.FindName("ThemeGallery")).Select(p => p.GetName()).ToList();
            Assert.Equal(widget.Themes.Select(t => t.Name), names);
        }
        finally { window.Close(); }
    });
}
