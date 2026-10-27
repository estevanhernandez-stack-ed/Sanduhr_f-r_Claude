using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Item 15 (Settings v2, S3 T7): a click on a missing app's Launcher button opens Settings at its row, and the row scrolls into
/// view, whether the page was open already or is realized only after the row was selected.
/// </summary>
[Collection("SettingsWindow")]
public sealed class LauncherPageScrollTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard) Open()
    {
        // The Launcher view model loads from the scratch profile (SANDUHR_DEV_PROFILE), so the buttons are seeded there.
        var buttons = Enumerable.Range(0, LauncherModel.MaxButtons)
            .Select(i => LauncherButton.Create(LauncherKind.App, "App" + i, null, "App " + i, null)!)
            .ToList();
        new SettingsStore(new Paths()).SaveLauncher(new LauncherModel(LauncherOrientation.Column, buttons));
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm) { Height = 420 };
        return (window, svm, NoActivate.Show(window));
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
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

    private static void AssertRowInView(SettingsWindow window, int index)
    {
        var page = Below<LauncherPage>(window).FirstOrDefault() ?? throw new Xunit.Sdk.XunitException("the Launcher page is not realized");
        var rows = (ItemsControl)page.FindName("ButtonRows");
        var scroller = Below<ScrollViewer>(page).First();
        var container = (FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(index);
        Assert.NotNull(container);
        Assert.True(scroller.ScrollableHeight > 0, "the window is tall enough to show every row; lower its height");
        var top = container.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
        Assert.True(top >= 0 && top <= scroller.ViewportHeight - container.ActualHeight,
            $"row {index} lies at {top}, viewport {scroller.ViewportHeight}, row height {container.ActualHeight}, offset {scroller.VerticalOffset}");
    }

    [DesktopFact]
    public void A_selected_row_on_the_open_page_scrolls_into_view() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Launcher);
            Settle(window, guard);
            svm.Desk.Launcher.Reload();
            svm.Desk.Launcher.Select(7);
            Settle(window, guard);
            AssertRowInView(window, 7);
        }
        finally { window.Close(); }
    });

    [DesktopFact]
    public void A_row_selected_before_the_page_opens_scrolls_into_view_when_it_does() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.General);
            Settle(window, guard);
            svm.Desk.Launcher.Reload();
            svm.Desk.Launcher.Select(7);
            window.SelectPage(SettingsPage.Launcher);
            Settle(window, guard);
            AssertRowInView(window, 7);
        }
        finally { window.Close(); }
    });
}
