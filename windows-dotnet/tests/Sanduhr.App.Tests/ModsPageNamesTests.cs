using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
            PumpUntil(() => !svm.ModsPage.Busy && svm.ModsPage.Catalog.Count == 2);
            Assert.StartsWith("The catalog is verified: 2 widgets", svm.ModsPage.Status);
            var all = NamesOf(window);

            var buttons = SetNames(window).Select(n => n.Name).ToList();
            Assert.Contains("Install Fixture effects", buttons);
            Assert.Contains("Uninstall Fixture palette", buttons);
            Assert.Contains(SettingsNames.WidgetCatalogCheck, buttons);
            Assert.DoesNotContain(SettingsNames.WidgetInstall, buttons);
            Assert.DoesNotContain(SettingsNames.WidgetUninstall.TrimEnd('…'), buttons);
            Assert.Equal(buttons.Count, buttons.Distinct().Count());
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
            PumpUntil(() => svm.ModsPage.Status == WidgetCatalogClient.Offline);
            Assert.Equal(WidgetCatalogClient.Offline, svm.ModsPage.Status);
            Assert.Empty(svm.ModsPage.Catalog);
            // The real widgets folder was never asked for: the service sits in the test's dev profile.
            Assert.StartsWith(Root, svm.ModsPage.Service.Store.Root);
        }
        finally { window.Close(); }
    });
}
