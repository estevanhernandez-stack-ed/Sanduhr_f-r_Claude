using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: About, Version carries "Don't show What's New after updates" (WF24); the What's New window keeps its own box
/// on the same key (decision 2), and About reads the key again on each visit and when the window is activated (P14).</summary>
[Collection("SettingsWindow")]
public sealed class AboutWhatsNewSwitchTests : AppWindowFixture
{
    private static IEnumerable<T> Below<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void About_and_the_What_s_New_window_share_one_key() => AppHost.Run(() =>
    {
        // SettingsViewModel reads settings through new Paths(), which this fixture points at a scratch profile.
        var paths = new Paths();
        Assert.StartsWith(Path.GetTempPath(), paths.SettingsFile, StringComparison.OrdinalIgnoreCase);
        var store = new SettingsStore(paths);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
            var window = new SettingsWindow(svm);
            var guard = NoActivate.Show(window);
            try
            {
                window.SelectPage(SettingsPage.About, SettingsIndex.Version);
                window.UpdateLayout();
                Pump();
                guard.Check();
                var box = Below<CheckBox>(window).Single(c =>
                    System.Windows.Automation.AutomationProperties.GetName(c) == SettingsNames.HideWhatsNewAfterUpdates);

                box.IsChecked = true;
                Assert.True(store.LoadWhatsNewHideAfterUpdates());
                Assert.True(new WhatsNewViewModel([], "Version 4.0.0", store).HideAfterUpdates);

                // The What's New window turns it back off; About shows that on its next visit.
                new WhatsNewViewModel([], "Version 4.0.0", store).HideAfterUpdates = false;
                window.SelectPage(SettingsPage.General);
                window.SelectPage(SettingsPage.About, SettingsIndex.Version);
                window.UpdateLayout();
                Pump();
                Assert.False(svm.AboutVersion.HideWhatsNewAfterUpdates);
                Assert.False(box.IsChecked);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_value_written_behind_the_page_s_back_shows_after_Reload() => AppHost.Run(() =>
    {
        var store = new SettingsStore(new Paths());
        var vm = new AboutVersionViewModel(store);
        Assert.False(vm.HideWhatsNewAfterUpdates);

        store.SaveWhatsNewHideAfterUpdates(true);
        Assert.False(vm.HideWhatsNewAfterUpdates); // not yet: the page reads on visit and activation
        vm.Reload();
        Assert.True(vm.HideWhatsNewAfterUpdates);

        // Reload is a read, not a write: it does not send the value back.
        store.SaveWhatsNewHideAfterUpdates(false);
        vm.Reload();
        Assert.False(vm.HideWhatsNewAfterUpdates);
        Assert.False(store.LoadWhatsNewHideAfterUpdates());
    });
}
