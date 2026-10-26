using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings ▸ Widgets in the real Settings window, over a fake GitHub: every row's button is named for its pack
/// ("Install Fixture effects", "Uninstall Fixture palette"), no two alike, and no list item reads as a type name.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ModsPageNamesTests : AppWindowFixture
{
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Pump();
            Thread.Sleep(15);
        }
        Pump();
    }

    private static List<string> CheckNames(DependencyObject root)
    {
        var names = new List<string>();
        void Collect(DependencyObject node)
        {
            if (node is CheckBox && AutomationProperties.GetName(node) is { Length: > 0 } name) names.Add(name);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Collect(VisualTreeHelper.GetChild(node, i));
        }
        Collect(root);
        return names;
    }

    private static List<(Type Kind, string Name)> SetNames(DependencyObject root)
    {
        var names = new List<(Type, string)>();
        void Collect(DependencyObject node)
        {
            if (node is Button && AutomationProperties.GetName(node) is { Length: > 0 } name)
                names.Add((node.GetType(), name));
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Collect(VisualTreeHelper.GetChild(node, i));
        }
        Collect(root);
        return names;
    }

    [Fact]
    public void Each_widget_row_names_its_button_for_its_pack() => AppHost.Run(() =>
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var net = new FakeGitHub();
        var palette = WidgetKit.Pack("good-palette");
        var paletteZip = WidgetKit.Zip(palette);
        var effectsZip = WidgetKit.Zip(WidgetKit.Pack("good-effects"));
        WidgetKit.Publish(net, key, "2026-10-22T02:23:13Z",
            (WidgetKit.Entry("good-effects", "Fixture effects", "effects", "1.0.0", effectsZip), effectsZip),
            (WidgetKit.Entry("good-palette", "Fixture palette", "palette", "1.0.0", paletteZip), paletteZip));

        // The dev profile's own widgets folder (SANDUHR_DEV_PROFILE), never the real one.
        var service = new WidgetCatalogService(new Paths(), net, WidgetKit.V380, [WidgetKit.Spki(key)]);
        Assert.StartsWith(Root, service.Store.Root);
        var v = PackValidator.Validate(palette, "good-palette", strict: true);
        Assert.True(service.Store.Install(palette, v.Manifest!, WidgetSource.Catalog).Ok);

        var svm = new SettingsViewModel(DemoWidget(), () => Task.CompletedTask, _ => Task.CompletedTask)
        {
            ModsPage = new ModsPageViewModel(() => service) { Confirm = (_, _) => true },
        };
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Widgets");
            svm.ModsPage.CatalogOpen = true;
            PumpUntil(() => !svm.ModsPage.Busy && svm.ModsPage.Catalog.Count == 2);
            Assert.StartsWith("The catalog is verified: 2 widgets", svm.ModsPage.CatalogStatus);
            var all = NamesOf(window);

            var buttons = SetNames(window).Select(n => n.Name).ToList();
            Assert.Contains("Install Fixture effects", buttons);
            Assert.Contains("Uninstall Fixture palette", buttons);
            Assert.Contains(SettingsNames.WidgetCatalogCheck, buttons);
            Assert.DoesNotContain(SettingsNames.WidgetInstall, buttons);
            Assert.DoesNotContain(SettingsNames.WidgetUninstall.TrimEnd('…'), buttons);
            Assert.Equal(buttons.Count, buttons.Distinct().Count());
            // The installed palette's catalog row is the same switch as its installed row: two check boxes, one pack.
            Assert.Equal(2, CheckNames(window).Count(c => c == SettingsNames.PackOnFor("Fixture palette")));
            Assert.Empty(LeaksIn(all));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Offline_the_page_says_so_and_lists_nothing_from_the_catalog() => AppHost.Run(() =>
    {
        var svm = new SettingsViewModel(DemoWidget(), () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Widgets");
            svm.ModsPage.CatalogOpen = true;
            PumpUntil(() => svm.ModsPage.CatalogStatus == WidgetCatalogClient.Offline);
            Assert.Equal(WidgetCatalogClient.Offline, svm.ModsPage.CatalogStatus);
            Assert.Empty(svm.ModsPage.Catalog);
            // The real widgets folder was never asked for: the service sits in the test's dev profile.
            Assert.StartsWith(Root, svm.ModsPage.Service.Store.Root);
        }
        finally { window.Close(); }
    });

    [DesktopFact]
    public void Every_installed_row_read_only_ones_too_is_one_named_stop_that_says_so() => AppHost.Run(() =>
    {
        var home = Home(".claude", "{\"enabledPlugins\":{\"alpha@m\":true}}");
        var cache = Path.Combine(Root, "cache", "alpha");
        Directory.CreateDirectory(Path.Combine(cache, ".claude-plugin"));
        File.WriteAllText(Path.Combine(cache, ".claude-plugin", "plugin.json"), "{\"name\":\"alpha\",\"version\":\"1.2.0\"}");
        Directory.CreateDirectory(Path.Combine(home, "plugins"));
        File.WriteAllText(Path.Combine(home, "plugins", "installed_plugins.json"),
            "{\"version\":2,\"plugins\":{\"alpha@m\":[{\"installPath\":\"" + cache.Replace("\\", "\\\\") + "\",\"version\":\"1.2.0\"}]}}");

        var svm = new SettingsViewModel(DemoWidget(), () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Widgets");
            svm.ModsPage.CatalogOpen = true;
            PumpUntil(() => !svm.ModsPage.Busy);
            svm.ModsPage.ReloadLocal();
            var alpha = svm.ModsPage.Installed.Single(r => r.Name == "alpha");
            Assert.True(alpha.IsReadOnly);
            Assert.Contains("read-only", alpha.RowName);

            var names = NamesOf(window);
            Assert.Contains(alpha.RowName, names);   // the row itself is a peer a screen reader lands on, not just the text inside it
            var rowPeer = Walk(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(window)).First(p => p.GetName() == alpha.RowName);
            Assert.True(rowPeer.IsKeyboardFocusable());   // read-only rows have no control inside, so the row itself is the stop
            // Tab walks every row and every switch in it, as the list did before it was a ListBox (whose default, Once, would
            // leave the other rows' switches to the arrow keys).
            var list = All<ListBox>(window).Single(l => AutomationProperties.GetName(l) == SettingsNames.InstalledSection);
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(list));
            Assert.Empty(LeaksIn(names));
        }
        finally { window.Close(); }
    });
}
