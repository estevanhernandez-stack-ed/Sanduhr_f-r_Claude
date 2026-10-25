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
    public void An_old_link_and_an_unknown_anchor_land_without_throwing() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            // Every old place the aliases name, in the build as it is: none throws, and each lands where Check says. A
            // page this build doesn't show (a later slice's) is ignored, so the place stays where the last link put it.
            foreach (var alias in SettingsIndex.Aliases)
            {
                var expected = SettingsIndex.Check(alias.FromPage, alias.FromAnchor);
                var before = window.Place;
                window.SelectPage(alias.FromPage, alias.FromAnchor);
                window.UpdateLayout();
                Pump();
                if (SettingsNames.Pages.Contains(expected.Page))
                    Assert.Equal(expected, window.Place);
                else
                    Assert.Equal(before, window.Place);
            }

            // An anchor the page doesn't have, and one that names nothing at all: the page, at its top.
            foreach (var anchor in new[] { "no-such-section", "", "  ", "DESK-PIECES-OLD" })
            {
                window.SelectPage(SettingsPage.Desk, anchor);
                window.UpdateLayout();
                Pump();
                Assert.Equal(new SettingsLink(SettingsPage.Desk, null), window.Place);
            }
            guard.Check();
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_search_hit_in_a_collapsed_section_says_so()
    {
        var row = new SettingsSearchRow("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, IsPage: false, CollapsedIn: "Advanced");
        Assert.Equal("Margins (in Advanced)", row.DisplayTitle);
        Assert.Equal("Margins, in Advanced, on Desk", row.AutomationName);
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
        var outer = new Expander { IsExpanded = false, Content = page };   // above the page: never ours to open

        Assert.True(SettingsWindow.RevealInside(target, page));
        Assert.True(expander.IsExpanded);
        Assert.False(outer.IsExpanded);
        Assert.False(SettingsWindow.RevealInside(target, page)); // already open: nothing to do
    });

    [Fact]
    public void Settings_opens_inside_the_work_area() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var window = new SettingsWindow(new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        try
        {
            window.FitToWorkArea(null); // no owner: the primary monitor's work area; the window is never shown
            var work = SystemParameters.WorkArea;
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            Assert.InRange(window.Height, window.MinHeight, Math.Max(window.MinHeight, work.Height));
            Assert.InRange(window.Width, window.MinWidth, Math.Max(window.MinWidth, 780));
            Assert.True(window.Top >= work.Top - 0.5, $"top {window.Top} is above the work area's {work.Top}");
            Assert.True(window.Height == window.MinHeight || window.Top + window.Height <= work.Bottom + 0.5,
                $"the foot at {window.Top + window.Height} is below the work area's {work.Bottom}");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_work_area_in_pixels_becomes_DIPs_by_the_scale_and_a_broken_one_becomes_nothing()
    {
        // 150%: 2880 x 1560 px of work area is 1920 x 1040 DIPs, from (0, 0).
        Assert.Equal(new WindowFit.Area(0, 0, 1920, 1040), SettingsWindow.WorkAreaDips(0, 0, 2880, 1560, 1.5));
        // A second monitor left of the primary keeps its negative origin.
        Assert.Equal(new WindowFit.Area(-1920, 0, 1920, 1080), SettingsWindow.WorkAreaDips(-1920, 0, 0, 1080, 1.0));
        Assert.Null(SettingsWindow.WorkAreaDips(0, 0, 0, 0, 1.0));          // empty
        Assert.Null(SettingsWindow.WorkAreaDips(100, 100, 50, 50, 1.0));    // inverted
        Assert.Null(SettingsWindow.WorkAreaDips(0, 0, 1920, 1080, 0));      // no scale
        Assert.Null(SettingsWindow.WorkAreaDips(0, 0, 1920, 1080, -1));
        Assert.Null(SettingsWindow.WorkAreaDips(0, 0, 1920, 1080, double.NaN));
    }

    [Fact]
    public void A_shown_test_window_stays_off_screen() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal"); // FitToWorkArea is App's call, never the window's own
        try
        {
            Assert.True(window.Left <= -10000 && window.Top <= -10000, $"the test window sits at {window.Left}, {window.Top}");
            guard.Check();
        }
        finally { window.Close(); }
    });
}
