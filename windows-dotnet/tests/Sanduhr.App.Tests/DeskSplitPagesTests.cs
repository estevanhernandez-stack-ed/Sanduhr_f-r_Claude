using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 3 ("the Desk split") on a real SettingsWindow over scratch folders: the new pages, where links land, the
/// collapsed sections, the folding map, the jump links and the Done criteria. Every window is shown through <see cref="NoActivate"/>.
/// </summary>
[Collection("SettingsWindow")]
public sealed class DeskSplitPagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window));
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static FrameworkElement Marked(SettingsWindow window, SettingsPage page, string anchor) =>
        SettingsAnchor.Find((DependencyObject)TabOf(window, page).Content, anchor)
            ?? throw new Xunit.Sdk.XunitException($"{page}/{anchor} is not marked");

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void The_three_new_pages_open_from_the_sidebar() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            foreach (var page in new[] { SettingsPage.DeskLook, SettingsPage.Watchers, SettingsPage.Launcher })
            {
                window.SelectPage(page);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(page, null), window.Place);
                Assert.True(TabOf(window, page).IsSelected);
            }
        }
        finally { window.Close(); }
    });
}
