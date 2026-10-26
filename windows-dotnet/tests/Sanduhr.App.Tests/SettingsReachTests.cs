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

    /// <summary>Layout, then the dispatcher's idle work, then layout again (a visibility flip queued by a binding lands on the
    /// second pass), then the focus guard.</summary>
    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    /// <summary>Where a focus-routing failure happened: the selected page and the focused element (type and name).</summary>
    private static string Where(SettingsWindow window)
    {
        var tabs = (TabControl)window.FindName("Tabs");
        var page = (tabs.SelectedItem as TabItem)?.Tag?.ToString() ?? "?";
        return $"page [{page}], place {window.Place}, focused {Describe(Keyboard.FocusedElement)}";
    }

    private static string Describe(IInputElement? element) =>
        element is null ? "nothing"
            : element.GetType().Name + (element is FrameworkElement { Name.Length: > 0 } fe ? " '" + fe.Name + "'" : "")
              + (element is ContentControl { Content: string text } ? " \"" + text + "\"" : "");

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

    [Fact]
    public void Quit_from_Settings_asks_first_and_Cancel_keeps_Sanduhr_running() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        int quits = 0, asked = 0;
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask)
        {
            QuitRequested = () => quits++,
            ConfirmQuitHook = () => { asked++; return false; },
        };

        svm.QuitCommand.Execute(null);
        Assert.Equal((1, 0), (asked, quits));

        svm.ConfirmQuitHook = () => { asked++; return true; };
        svm.QuitCommand.Execute(null);
        Assert.Equal((2, 1), (asked, quits));
        Assert.Equal("Quit Sanduhr? The widget and the Desk close.", SettingsViewModel.QuitQuestion);
    });

    private static Control? FirstTabStop(DependencyObject root)
    {
        if (root is Control { Focusable: true, IsTabStop: true, IsVisible: true, IsEnabled: true } control)
            return control;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FirstTabStop(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        return null;
    }

    private static bool Inside(object? element, DependencyObject root)
    {
        for (var d = element as DependencyObject; d is not null; d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root))
                return true;
        return false;
    }

    private static void Next(IInputElement element)
    {
        var request = new TraversalRequest(FocusNavigationDirection.Next);
        if (element is UIElement ui) ui.MoveFocus(request);
        else if (element is ContentElement content) content.MoveFocus(request);
    }

    /// <summary>WF33: one Tab past a page's last control used to land on Quit. Keyboard focus is routed inside the
    /// never-activating window, as the harness does; on GitHub's runner this is skipped. Written, deferred to Tier 2.</summary>
    [DesktopFact]
    public void Tab_from_the_end_of_every_page_goes_to_the_search_box_never_to_Quit() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        var bad = new List<string>();
        var skipped = new List<string>();
        var tabs = (TabControl)window.FindName("Tabs");
        try
        {
            guard.RouteKeyboard();
            var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
            var search = (TextBox)tabs.Template.FindName("SearchBox", tabs);
            var quit = (Button)tabs.Template.FindName("QuitButton", tabs);
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = item;
                window.UpdateLayout();
                Pump();
                if (FirstTabStop(host) is not { } first)
                {
                    skipped.Add(item.Tag?.ToString() ?? "?");
                    continue;
                }
                Keyboard.Focus(first);
                guard.Check();
                Assert.True(ReferenceEquals(Keyboard.FocusedElement, first), $"keyboard focus could not be routed to [{item.Tag}] ({Where(window)})");
                IInputElement? at = first;
                for (int step = 0; step < 400 && at is not null && Inside(at, host); step++)
                {
                    Next(at);
                    Pump();
                    guard.Check();
                    at = Keyboard.FocusedElement;
                }
                if (ReferenceEquals(at, quit))
                    bad.Add($"[{item.Tag}] Tab from the page reached Quit ({Where(window)})");
                else if (!ReferenceEquals(at, search))
                    bad.Add($"[{item.Tag}] Tab from the page reached {Describe(at)}, not the search box ({Where(window)})");
            }
        }
        finally { window.Close(); }
        var note = skipped.Count == 0 ? "" : $"{Environment.NewLine}pages with no tab stop, not walked: {string.Join(", ", skipped)}";
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad) + note);
        Assert.True(skipped.Count < tabs.Items.Count, "no page had a tab stop, so nothing was walked" + note);
    });

    private static ScrollViewer? ScrollerAbove(DependencyObject element)
    {
        for (var p = VisualTreeHelper.GetParent(element); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer scroller)
                return scroller;
        return null;
    }

    [Fact]
    public void Alerts_off_dims_everything_under_the_switch_and_says_why() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.Alerts, SettingsIndex.Notifications);
            var content = (DependencyObject)TabOf(window, SettingsPage.Alerts).Content;
            var off = (FrameworkElement)window.FindName("AlertsOffLine");

            svm.Alerts.AlertsEnabled = false;
            window.UpdateLayout();
            Pump();
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.Thresholds)!.IsEnabled);
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.Sound)!.IsEnabled);
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.TestAlert)!.IsEnabled);
            Assert.True(off.IsVisible);
            Assert.Equal(0.45, ((FrameworkElement)window.FindName("AlertsDependents")).Opacity, 2);

            svm.Alerts.AlertsEnabled = true;
            window.UpdateLayout();
            Pump();
            Assert.True(SettingsAnchor.Find(content, SettingsIndex.Sound)!.IsEnabled);
            Assert.False(off.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_meter_history_says_why_it_is_empty() => AppHost.Run(() =>
    {
        var (signedOut, _) = ScratchWidget();
        var tab = new HistoryTabViewModel(signedOut);
        tab.BuildRows();
        Assert.Equal(SettingsNames.MeterHistorySignedOut, tab.EmptyText);
        Assert.True(tab.HasEmptyText);

        var (work, _) = ScratchWidget("Work");
        var workTab = new HistoryTabViewModel(work);
        workTab.BuildRows();
        Assert.Equal(SettingsNames.MeterHistoryEmpty, workTab.EmptyText);

        work.History.AppendForAccount("five_hour", 41, "Work");
        Assert.NotEmpty(workTab.BuildRows());
        Assert.Equal("", workTab.EmptyText);
        Assert.False(workTab.HasEmptyText);
    });

    [Fact]
    public void Signed_out_the_Meters_view_reads_its_empty_line() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open();
        try
        {
            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory);
            var line = (TextBlock)window.FindName("HistoryEmptyText");
            Assert.True(line.IsVisible);
            Assert.Equal(SettingsNames.MeterHistorySignedOut, line.Text);
            Assert.Equal(Visibility.Hidden, ((FrameworkElement)window.FindName("Chart")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Alerts_and_Publish_usage_scroll_on_a_short_window() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.Height = 520;
            foreach (var (page, anchor) in new[] { (SettingsPage.Alerts, SettingsIndex.TestAlert), (SettingsPage.Publish, SettingsIndex.Schedule) })
            {
                var scroller = ScrollerAbove(Land(window, guard, page, anchor));
                Assert.True(scroller is { VerticalScrollBarVisibility: ScrollBarVisibility.Auto, Focusable: false },
                    $"{page} has no page scroll viewer");
            }
        }
        finally { window.Close(); }
    });

    /// <summary>The Now playing apps list reads the app's one hub, whose service fills it from the PC's live media
    /// session. A hub that outlives its test (or a media player on the PC) must never reach a test's Settings window.</summary>
    [Fact]
    public void A_live_media_hub_never_reaches_the_Settings_window() => AppHost.Run(() =>
    {
        var leaked = new Sanduhr.App.Desk.NowPlayingHub();
        leaked.Service.SeenApps = ["Spotify.exe"];
        Sanduhr.App.Desk.NowPlayingHub.Current = leaked;
        var (window, svm, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.NowPlaying, SettingsIndex.NowPlayingApps);
            Assert.Empty(svm.NowPlayingPage.Apps);
            Assert.True(((FrameworkElement)window.FindName("NowPlayingAppsEmpty")).IsVisible, "NowPlayingAppsEmpty is not shown");
        }
        finally { window.Close(); leaked.Dispose(); }
    });

    [Fact]
    public void Empty_lists_say_so_and_are_not_tab_stops() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), recursive: true); // no Claude Code folder at all
        var (window, svm, guard) = Open("Personal");
        try
        {
            void Shows(string line, string list)
            {
                Settle(window, guard);
                Assert.True(((FrameworkElement)window.FindName(line)).IsVisible, $"{line} is not shown ({Where(window)})");
                Assert.False(((Control)window.FindName(list)).Focusable, $"{list} is a tab stop ({Where(window)})");
            }

            Land(window, guard, SettingsPage.Publish, SettingsIndex.Homes);
            Shows("PublishHomesEmpty", "PublishHomesList");

            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.Vault);
            Shows("VaultRootsEmpty", "VaultRootsList");
            svm.ClaudeCode.Overview.ShowBreakdowns = true;
            LoadOverview(svm);
            Shows("OverviewProjectsEmpty", "OverviewProjectsList");
            Shows("OverviewSkillsEmpty", "OverviewSkillsList");

            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.Trends);
            Shows("TrendsProjectsEmpty", "TrendsProjectsList");

            Land(window, guard, SettingsPage.NowPlaying, SettingsIndex.NowPlayingApps);
            Shows("NowPlayingAppsEmpty", "NowPlayingAppsList");

            // The link in the folder line lands on the Claude Code folders.
            Land(window, guard, SettingsPage.Publish, SettingsIndex.Homes);
            var link = ((Panel)window.FindName("PublishHomesEmpty")).Children.OfType<Button>().Single();
            Assert.Equal(SettingsNames.LinkTitle(SettingsPage.ClaudeCode), link.Content);
            link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            guard.Check();
            Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), window.Place);
        }
        finally { window.Close(); }
    });

    /// <summary>Runs the Overview's first scan to its end, as the window does when the page opens.</summary>
    private static void LoadOverview(SettingsViewModel svm)
    {
        var scan = svm.ClaudeCode.Overview.RefreshAsync();
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!scan.IsCompleted && DateTime.UtcNow < until)
        {
            Pump();
            Thread.Sleep(15);
        }
        Pump();
        Assert.True(svm.ClaudeCode.Overview.HasLoaded, "the first scan did not finish");
    }

    [Fact]
    public void The_Overview_breakdown_says_none_only_after_its_first_scan() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), recursive: true);
        var (window, svm, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.Vault);
            svm.ClaudeCode.Overview.ShowBreakdowns = true;
            LoadOverview(svm);
            // Back to the moment before the first scan presents (landing on the page starts the scan, which is quick against a scratch profile).
            svm.ClaudeCode.Overview.HasLoaded = false;
            window.UpdateLayout();
            Pump();
            Assert.False(((FrameworkElement)window.FindName("OverviewProjectsEmpty")).IsVisible, "the projects line shows before the first scan");
            Assert.False(((FrameworkElement)window.FindName("OverviewSkillsEmpty")).IsVisible, "the skills line shows before the first scan");

            LoadOverview(svm);
            window.UpdateLayout();
            Pump();
            Assert.True(((FrameworkElement)window.FindName("OverviewProjectsEmpty")).IsVisible, "the projects line is not shown after the scan");
            Assert.True(((FrameworkElement)window.FindName("OverviewSkillsEmpty")).IsVisible, "the skills line is not shown after the scan");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void An_empty_line_hides_when_its_list_fills() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.Publish, SettingsIndex.Homes);
            svm.Publish.Roots.Clear();
            window.UpdateLayout();
            Pump();
            Assert.True(((FrameworkElement)window.FindName("PublishHomesEmpty")).IsVisible, "the line is not shown while the list is empty");

            svm.Publish.Roots.Add(new PublishRootToggleViewModel(svm.Publish, "scratch-home", false));
            window.UpdateLayout();
            Pump();
            guard.Check();
            Assert.False(((FrameworkElement)window.FindName("PublishHomesEmpty")).IsVisible, "the line stays shown with an item in the list");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Desk_page_scrolls_sideways_only_below_its_rows_width() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.Width = 680;
            window.Height = 800;
            Land(window, guard, SettingsPage.Desk, SettingsIndex.DeskPieces);
            window.UpdateLayout();
            Pump();
            var scroller = (ScrollViewer)window.FindName("DeskPageScroller");
            Assert.True(scroller.ScrollableWidth > 0,
                $"at 680 wide the Desk page clips instead of scrolling (extent {scroller.ExtentWidth:0}, viewport {scroller.ViewportWidth:0})");

            window.Width = 780;
            window.UpdateLayout();
            Pump();
            Assert.True(scroller.ScrollableWidth < 1,
                $"at the default 780 wide the Desk page scrolls sideways (extent {scroller.ExtentWidth:0}, viewport {scroller.ViewportWidth:0})");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Buttons_pills_links_and_boxes_show_focus_and_draw_disabled() => AppHost.Run(() =>
    {
        var (window, _, _) = Open("Personal");
        try
        {
            IEnumerable<Trigger> TemplateTriggers(string key) =>
                ((Style)window.FindResource(key)).Setters.OfType<Setter>()
                    .Where(s => s.Property == Control.TemplateProperty)
                    .SelectMany(s => ((ControlTemplate)s.Value).Triggers.OfType<Trigger>());
            bool Shows(string key, DependencyProperty property, bool value) =>
                TemplateTriggers(key).Any(t => t.Property == property && Equals(t.Value, value));

            foreach (var key in new[] { "FlatButton", "AccentButton", "SettingsLinkButton", "ThemedCheckBox" })
                Assert.True(Shows(key, UIElement.IsKeyboardFocusedProperty, true), $"{key} draws no focus ring");
            foreach (var key in new[] { "FlatButton", "ThemedComboBox", "ThemedCheckBox" })
                Assert.True(Shows(key, UIElement.IsEnabledProperty, false), $"{key} does not draw disabled");
            // The History and Claude Usage pills are FlatButtons with the same template.
            Assert.Same(window.FindResource("FlatButton"), ((Style)window.FindResource("RangePillWeek")).BasedOn);
            Assert.Same(window.FindResource("FlatButton"), ((Style)window.FindResource("CcNavOverview")).BasedOn);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Alerts_has_its_page_title_and_percent_after_the_thresholds() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            var thresholds = Land(window, guard, SettingsPage.Alerts, SettingsIndex.Thresholds);
            var page = (Panel)SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.Alerts).Content, SettingsIndex.Notifications)!;
            Assert.Equal("Alerts", ((TextBlock)page.Children[0]).Text);
            var grid = (Grid)thresholds;
            Assert.Equal(2, grid.Children.OfType<TextBlock>().Count(x => x.Text == "%"));
        }
        finally { window.Close(); }
    });
}
