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

    [Fact]
    public void A_pieces_row_is_named_and_its_page_link_opens_the_page() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            var pieces = Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces);
            var show = Below<CheckBox>(pieces).Single(c => (string)c.Content == SettingsNames.DeskWeather);
            Assert.Equal("Weather, on the Desk", System.Windows.Automation.AutomationProperties.GetName(show));
            var link = Below<Button>(pieces).Single(b => (b.Content as string) == "Weather settings…");
            Assert.Equal("weather", SettingsLinkTo.GetLink(link));
            link.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, null), window.Place);
        }
        finally { window.Close(); }
    });

    private static Sanduhr.App.Services.SettingsStore Store() => new(new Paths());   // the fixture's scratch profile

    [Fact]
    public void Turning_a_feature_on_from_its_page_places_its_piece() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        string Layout() => Store().LoadDesk().Layout;
        Assert.DoesNotContain("nowPlaying", Layout());
        Assert.DoesNotContain("weather", Layout());
        Assert.DoesNotContain("message", Layout());

        svm.NowPlayingPage.Bars = false;   // the page loads with Bars on: turning it on is a change
        Assert.DoesNotContain("nowPlaying", Layout());
        svm.NowPlayingPage.Bars = true;
        Assert.Contains("nowPlaying:br", Layout());
        svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
        Assert.Contains("weather:tr", Layout());
        svm.MessagePage.AddLineCommand.Execute(null);
        Assert.Contains("message:bl", Layout());

        Assert.Equal(SettingsNames.PieceStatus("Top right", svm.Desk.Enabled), svm.WeatherPage.Piece!.Text);
        Assert.True(svm.NowPlayingPage.Piece!.IsPlaced);
        Assert.True(svm.MessagePage.Piece!.IsPlaced);
    });

    [Fact]
    public void Turning_Style_on_places_a_hidden_Now_playing_piece() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.DoesNotContain("nowPlaying", Store().LoadDesk().Layout);
        svm.NowPlayingPage.Style = false;
        Assert.DoesNotContain("nowPlaying", Store().LoadDesk().Layout);
        svm.NowPlayingPage.Style = true;
        Assert.Contains("nowPlaying:br", Store().LoadDesk().Layout);
    });

    [Fact]
    public void An_on_action_leaves_a_placed_piece_where_it_is() => AppHost.Run(() =>
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl weather:bl nowPlaying:tl message:tc" });
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);

        svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
        svm.NowPlayingPage.Bars = true;
        svm.NowPlayingPage.Bars = false;
        svm.NowPlayingPage.Style = true;
        svm.MessagePage.AddLineCommand.Execute(null);
        Assert.Equal("clock:bl weather:bl nowPlaying:tl message:tc", Store().LoadDesk().Layout);
    });

    [Fact]
    public void Now_playing_and_Weather_show_their_line_at_the_top() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            foreach (var page in new[] { SettingsPage.NowPlaying, SettingsPage.Weather, SettingsPage.DeskMessage })
            {
                window.SelectPage(page);
                Settle(window, guard);
                var status = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, page).Content).Single();
                var place = (Button)status.FindName("PlaceButton");
                Assert.True(place.IsVisible);
                Assert.Equal(SettingsNames.PlaceOnDesk, System.Windows.Automation.AutomationProperties.GetName(place));
                // Execute the button's own command: a raised Click would not run it (P13).
                Assert.True(place.Command.CanExecute(place.CommandParameter));
                place.Command.Execute(place.CommandParameter);
                Settle(window, guard);
                Assert.False(place.IsVisible);
                var link = (Button)status.FindName("DeskLink");
                Assert.True(link.IsVisible);
                Assert.Equal("desk#pieces", SettingsLinkTo.GetLink(link));
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Desk_Look_reads_the_message_settings_fresh_when_shown() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            // Changed behind the view model while Settings is open (a design switch, a Claude proposal).
            var store = Store();
            store.SaveDeskMessage(store.LoadDeskMessage() with { Size = 77, Glow = false });
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);
            Assert.Equal(77, svm.MessagePage.Size);
            // An edit made here keeps what changed behind it.
            svm.MessagePage.Glow = true;
            var saved = store.LoadDeskMessage();
            Assert.True(saved.Glow);
            Assert.Equal(77, saved.Size);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Desk_Look_holds_both_fonts_both_sizes_and_both_colors() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.DeskLook, SettingsIndex.DeskLook);
            Settle(window, guard);
            string Names<T>(string anchor) where T : FrameworkElement => string.Join("|",
                Below<T>(Marked(window, SettingsPage.DeskLook, anchor)).Select(System.Windows.Automation.AutomationProperties.GetName));
            Assert.Equal($"{SettingsNames.DeskFont}|{SettingsNames.MessageFont}", Names<ComboBox>(SettingsIndex.DeskLook));
            Assert.Equal($"{SettingsNames.ClockSize}|{SettingsNames.MessageSize}", Names<Slider>(SettingsIndex.DeskSizes));
            Assert.Contains(SettingsNames.MessageColor, Names<ComboBox>(SettingsIndex.DeskInk));
            Assert.Contains(SettingsNames.DeskInkPicker, Names<ComboBox>(SettingsIndex.DeskInk));

            // Old links land here.
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskInk);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskInk), window.Place);
            window.SelectPage(SettingsPage.DeskMessage, SettingsIndex.MessageLook);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskSizes), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Watchers_page_holds_both_switches_and_the_pieces_line() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskWatchers);   // an old link
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Watchers, SettingsIndex.Watchers), window.Place);
            var ci = Below<CheckBox>(Marked(window, SettingsPage.Watchers, SettingsIndex.CiRunners)).Single();
            Assert.Equal(SettingsNames.CiRunnersSwitch, System.Windows.Automation.AutomationProperties.GetName(ci));
            Assert.Single(Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.Watchers, SettingsIndex.WatchersPlace)));
            Assert.Null(SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.Desk).Content, SettingsIndex.DeskWatchers));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Launcher_page_holds_its_buttons_and_old_links_land_there() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherButtons), window.Place);
            Assert.Contains(Below<Button>(Marked(window, SettingsPage.Launcher, SettingsIndex.LauncherButtons)),
                b => (b.Content as string) == SettingsNames.LauncherAddApp);
            Assert.Single(Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.Launcher, SettingsIndex.LauncherPlace)));

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskLauncher);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Designs_and_Advanced_start_closed_and_a_hit_inside_opens_them() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var designs = (Expander)window.FindName("DeskDesignsSection");
            var advanced = (Expander)window.FindName("DeskAdvancedSection");
            Assert.False(designs.IsExpanded);
            Assert.False(advanced.IsExpanded);
            Assert.Contains(Below<TextBlock>(designs), t => t.Text == svm.Desk.Designs.Summary);

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskMargins);
            Settle(window, guard);
            Assert.True(advanced.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskMargins), window.Place);

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskDirect);   // an old link
            Settle(window, guard);
            Assert.True(designs.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskDesigns), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_hit_inside_Advanced_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskOled);
            Settle(window, guard);
            Assert.True(((Expander)window.FindName("DeskAdvancedSection")).IsExpanded);
            Assert.True(Marked(window, SettingsPage.Desk, SettingsIndex.DeskOled).IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Sign_in_reminders_live_on_Desk_Behavior() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.SignInReminders);   // an old link
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.SignInReminders), window.Place);
            var box = Below<CheckBox>(Marked(window, SettingsPage.Desk, SettingsIndex.SignInReminders)).Single();
            Assert.Equal(SettingsNames.SignInReminders, box.Content);
            Assert.Equal(SettingsNames.SignInReminders, System.Windows.Automation.AutomationProperties.GetName(box));
            Assert.True(box.IsChecked);
            box.IsChecked = false;
            Assert.False(svm.Alerts.DeskSignInReminder);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_section_toggles_speak_as_expanders_once_with_their_summary() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            foreach (var (name, title) in new[] { ("DeskDesignsSection", SettingsNames.DeskDesigns), ("DeskAdvancedSection", SettingsNames.DeskAdvanced) })
            {
                var expander = (Expander)window.FindName(name);
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(expander);
                var provider = (System.Windows.Automation.Provider.IExpandCollapseProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse);
                Assert.Equal(title, peer.GetName());
                Assert.Equal(System.Windows.Automation.ExpandCollapseState.Collapsed, provider.ExpandCollapseState);

                var toggle = Below<ToggleButton>(expander).First();
                Assert.Equal(title, System.Windows.Automation.AutomationProperties.GetName(toggle));
                toggle.Focus();
                Settle(window, guard);
                Assert.True(toggle.IsKeyboardFocused);
                Assert.True(peer.HasKeyboardFocus(), title + ": the Expander's peer reports the focus");

                // The header text is drawn by the toggle's own template: none of it is a control element beside the toggle.
                foreach (var text in Below<TextBlock>(toggle))
                {
                    var tp = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text);
                    Assert.False(tp is not null && tp.IsControlElement(), title + ": header text '" + text.Text + "' is a control element");
                }

                expander.IsExpanded = true;
                Settle(window, guard);
                Assert.Equal(System.Windows.Automation.ExpandCollapseState.Expanded, provider.ExpandCollapseState);
                expander.IsExpanded = false;
            }
            var designs = (Expander)window.FindName("DeskDesignsSection");
            Assert.Equal(svm.Desk.Designs.Summary, System.Windows.Automation.AutomationProperties.GetHelpText(Below<ToggleButton>(designs).First()));
            Assert.Equal(System.Windows.Automation.AutomationHeadingLevel.Level2,
                System.Windows.Automation.AutomationProperties.GetHeadingLevel(designs));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_map_folds_below_600_and_a_choice_holds_for_the_window() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            window.Height = 560;
            Settle(window, guard);
            var fold = (Expander)window.FindName("DeskMapFold");
            Assert.False(fold.IsExpanded);
            Assert.InRange(Below<ToggleButton>(fold).First().ActualHeight, 43.5, 46);   // a 44 DIP strip
            window.Height = 800;
            Settle(window, guard);
            Assert.True(fold.IsExpanded);
            fold.IsExpanded = false;                                                       // the person folds it
            window.Height = 820;
            Settle(window, guard);
            Assert.False(fold.IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_window_that_opens_under_600_opens_with_the_map_folded() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm) { Height = 560 };
        var guard = NoActivate.Show(window);
        try
        {
            Settle(window, guard);
            Assert.False(((Expander)window.FindName("DeskMapFold")).IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_link_to_the_folded_map_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            window.Height = 560;
            Settle(window, guard);
            var fold = (Expander)window.FindName("DeskMapFold");
            Assert.False(fold.IsExpanded);
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskMap);
            Settle(window, guard);
            Assert.True(fold.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskMap), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_map_toggle_speaks_as_an_expander_with_the_map_title() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var fold = (Expander)window.FindName("DeskMapFold");
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(fold);
            var provider = (System.Windows.Automation.Provider.IExpandCollapseProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse);
            Assert.Equal(SettingsNames.DeskLayoutMap, peer.GetName());
            Assert.Equal(System.Windows.Automation.ExpandCollapseState.Expanded, provider.ExpandCollapseState);
            var toggle = Below<ToggleButton>(fold).First();
            Assert.Equal("HeaderSite", toggle.Name);
            Assert.Equal(SettingsNames.DeskLayoutMap, System.Windows.Automation.AutomationProperties.GetName(toggle));
            Assert.Equal(System.Windows.Automation.AutomationHeadingLevel.Level2, System.Windows.Automation.AutomationProperties.GetHeadingLevel(fold));
            foreach (var text in Below<TextBlock>(toggle))
            {
                var tp = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text);
                Assert.False(tp is not null && tp.IsControlElement(), "header text '" + text.Text + "' is a control element");
            }
            fold.IsExpanded = false;
            Settle(window, guard);
            Assert.Equal(System.Windows.Automation.ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
        }
        finally { window.Close(); }
    });
}
