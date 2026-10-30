using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The Drawers page on a real SettingsWindow over scratch folders (drawers plan 2, Task 11): it opens from the sidebar and from
/// the menu anchor, and every section is marked.</summary>
[Collection("SettingsWindow")]
public sealed class DrawersPageTests : AppWindowFixture
{
    [Fact]
    public void The_Drawers_page_opens_at_Your_drawers_and_marks_every_section() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Drawers, SettingsIndex.MenuAnchor(SettingsPage.Drawers));
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Drawers");
            Assert.True(tab.IsSelected);
            foreach (var anchor in new[] { SettingsIndex.Drawers, SettingsIndex.DrawersFolder, SettingsIndex.DrawersSort, SettingsIndex.DrawersRules,
                SettingsIndex.DrawersList, SettingsIndex.DrawersRecent })
                Assert.NotNull(SettingsAnchor.Find((DependencyObject)tab.Content, anchor));
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    /// <summary>The Desk setting cannot be saved (a read-only settings file): the check box ends unchecked, drawers stay off and the status says why.</summary>
    [Fact]
    public void Checking_the_switch_when_the_Desk_cannot_be_saved_leaves_the_box_unchecked() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = false });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var vm = new DrawersSettingsViewModel(s, null, store);
        var page = new DrawersPage { DataContext = vm };
        page.Measure(new Size(600, 900));
        page.Arrange(new Rect(0, 0, 600, 900));
        page.UpdateLayout();
        var check = Below<CheckBox>(page).First();
        Assert.False(check.IsChecked);
        var file = paths.SettingsFile;
        var attrs = File.GetAttributes(file);
        File.SetAttributes(file, attrs | FileAttributes.ReadOnly);
        try
        {
            check.IsChecked = true;
            Pump();
            Assert.False(check.IsChecked);
            Assert.False(vm.Enabled);
            Assert.False(store.LoadDrawers().Enabled);
            Assert.False(store.LoadDesk().Enabled);
            Assert.Equal(SettingsNames.DrawersDeskSaveFailed, vm.Status);
        }
        finally { File.SetAttributes(file, attrs); }
        vm.Dispose();
        s.Dispose();
    });

    /// <summary>The real check box, bound two-way: unchecking it and answering no leaves the box checked and drawers on.</summary>
    [Fact]
    public void Unchecking_the_switch_and_answering_no_leaves_the_box_checked() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        box.File(@"Desktop\Drawers\Images\a.png");
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var vm = new DrawersSettingsViewModel(s, null, store);
        var page = new DrawersPage { DataContext = vm };
        string? asked = null;
        vm.Confirm = q => { asked = q; return false; };   // after the page gave its dialog
        page.Measure(new Size(600, 900));
        page.Arrange(new Rect(0, 0, 600, 900));
        page.UpdateLayout();
        var check = Below<CheckBox>(page).First(c => c.IsChecked == true);
        Assert.True(check.IsChecked);
        check.IsChecked = false;   // the binding updates the view model
        Pump();
        Assert.Equal(DrawerReturn.Prompt(1), asked);
        Assert.True(check.IsChecked);
        Assert.True(vm.Enabled);
        Assert.True(store.LoadDrawers().Enabled);
        vm.Dispose();
        s.Dispose();
    });
}
