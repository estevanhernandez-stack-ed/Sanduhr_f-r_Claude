using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 2 ("reach"), on a real SettingsWindow over scratch folders and an in-memory account registry:
/// where links land, what the window remembers, and the keyboard and short-window fixes. Every window is shown through
/// <see cref="NoActivate"/>, so no test takes the foreground.
/// </summary>
[Collection("SettingsWindow")]
public sealed class SettingsReachTests : AppWindowFixture
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

    /// <summary>Opens <paramref name="page"/> at <paramref name="anchor"/>, lets layout and the landing run, and returns the marked element.</summary>
    private static FrameworkElement Land(SettingsWindow window, NoActivate guard, SettingsPage page, string anchor)
    {
        window.SelectPage(page, anchor);
        window.UpdateLayout();
        Pump();
        guard.Check();
        return SettingsAnchor.Find((DependencyObject)TabOf(window, page).Content, anchor)
            ?? throw new Xunit.Sdk.XunitException($"{page}/{anchor} is not marked");
    }

    [Fact]
    public void The_window_remembers_the_place_it_last_opened() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.Desk, SettingsIndex.DeskPieces);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces), window.Place);

            // A sidebar click is a page with no section.
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = TabOf(window, SettingsPage.Weather);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, null), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_search_hit_in_a_collapsed_section_says_so()
    {
        var row = new SettingsSearchRow("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, IsPage: false, CollapsedIn: "Advanced");
        Assert.Equal("Margins (in Advanced)", row.DisplayTitle);
        Assert.Equal("Margins (in Advanced), on Desk", row.AutomationName);
        Assert.Equal("Margins", new SettingsSearchRow("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, false).DisplayTitle);
    }

    [Fact]
    public void Opening_a_hit_inside_a_collapsed_section_unfolds_it() => AppHost.Run(() =>
    {
        var target = new CheckBox();
        var inner = new StackPanel();
        inner.Children.Add(target);
        var expander = new Expander { IsExpanded = false, Content = inner };
        var page = new StackPanel();
        page.Children.Add(expander);

        Assert.True(SettingsWindow.RevealInside(target));
        Assert.True(expander.IsExpanded);
        Assert.False(SettingsWindow.RevealInside(target)); // already open: nothing to do
    });
}
