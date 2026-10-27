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
                var tab = TabOf(window, page);
                Assert.True(tab.IsSelected);
                Assert.NotNull(tab.Content);
                Assert.True(((UIElement)tab.Content).IsVisible, $"{page}'s content is not shown");
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
            foreach (var (page, anchor) in new[] { (SettingsPage.NowPlaying, "Bottom right"), (SettingsPage.Weather, "Top right"), (SettingsPage.DeskMessage, "Bottom left") })
            {
                window.SelectPage(page);
                Settle(window, guard);
                var status = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, page).Content).Single();
                var line = (TextBlock)status.FindName("StatusText");
                Assert.Equal(SettingsNames.NotOnDesk, line.Text);
                var place = (Button)status.FindName("PlaceButton");
                Assert.True(place.IsVisible);
                Assert.Equal(SettingsNames.PlaceOnDesk, System.Windows.Automation.AutomationProperties.GetName(place));
                // Execute the button's own command: a raised Click would not run it (P13).
                Assert.True(place.Command.CanExecute(place.CommandParameter));
                place.Command.Execute(place.CommandParameter);
                Settle(window, guard);
                Assert.False(place.IsVisible);
                Assert.Equal(SettingsNames.PieceStatus(anchor, svm.Desk.Enabled), line.Text);
                var link = (Button)status.FindName("DeskLink");
                Assert.True(link.IsVisible);
                Assert.Equal("desk#pieces", SettingsLinkTo.GetLink(link));
            }
        }
        finally { window.Close(); }
    });

    /// <summary>A click on a button with no link falls through; one with a link is taken by the window and opens its place (11b).</summary>
    [Fact]
    public void A_click_passes_through_OnSettingsLinkClick_only_when_the_button_has_a_link() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Weather);
            Settle(window, guard);
            var status = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, SettingsPage.Weather).Content).Single();
            var place = (Button)status.FindName("PlaceButton");
            Assert.True(string.IsNullOrEmpty(SettingsLinkTo.GetLink(place)), "the Place button was meant to be link-less");
            var plain = new RoutedEventArgs(ButtonBase.ClickEvent, place);
            place.RaiseEvent(plain);
            Assert.False(plain.Handled, "a link-less button's click was taken by the window");

            var link = (Button)status.FindName("DeskLink");
            Assert.False(string.IsNullOrEmpty(SettingsLinkTo.GetLink(link)));
            var linked = new RoutedEventArgs(ButtonBase.ClickEvent, link);
            link.RaiseEvent(linked);
            Assert.True(linked.Handled, "a linked button's click was not taken by the window");
        }
        finally { window.Close(); }
    });

    /// <summary>Review I2: "Place it on the Desk" collapses while it holds the keyboard; focus moves to the "Desk settings…" link
    /// that takes its spot instead of falling to the top of the window.</summary>
    [DesktopFact]
    public void Placing_a_piece_from_its_page_keeps_the_keyboard_on_Desk_settings() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Weather);
            Settle(window, guard);
            var status = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, SettingsPage.Weather).Content).Single();
            var place = (Button)status.FindName("PlaceButton");
            var link = (Button)status.FindName("DeskLink");
            place.Focus();
            Settle(window, guard);
            Assert.True(place.IsKeyboardFocused, "the Place button took the focus; focused: " + Keyboard.FocusedElement);
            place.Command.Execute(place.CommandParameter);
            Settle(window, guard);
            Assert.False(place.IsVisible);
            Assert.True(ReferenceEquals(Keyboard.FocusedElement, link), "focus should be on the Desk settings link; focused: " + Keyboard.FocusedElement);
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

    [SkippableFact]
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
                var texts = Below<TextBlock>(toggle).ToList();
                Assert.NotEmpty(texts);
                foreach (var text in texts)
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
            var designsPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(designs);
            Assert.Equal(svm.Desk.Designs.Summary, designsPeer.GetHelpText());
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
            // The link counts as the person's choice: a taller window leaves it open.
            window.Height = 561;
            Settle(window, guard);
            Assert.True(fold.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskMap), window.Place);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(SettingsPage.Desk)]
    [InlineData(SettingsPage.DeskLook)]
    [InlineData(SettingsPage.NowPlaying)]
    [InlineData(SettingsPage.Alerts)]
    [InlineData(SettingsPage.Publish)]
    [InlineData(SettingsPage.About)]
    [InlineData(SettingsPage.Privacy)]
    public void Every_jump_link_lands_on_its_section(SettingsPage page) => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(page);
            Settle(window, guard);
            var links = Below<SectionJumpLinks>((DependencyObject)TabOf(window, page).Content).Single();
            foreach (var (title, anchor) in SettingsIndex.JumpLinks(page))
            {
                links.Children.OfType<Button>().Single(b => (string)b.Content == title).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Settle(window, guard);
                Assert.Equal(new SettingsLink(page, anchor), window.Place);
                if (Marked(window, page, anchor) is Expander expander)
                    Assert.True(expander.IsExpanded, anchor + " did not open");
                AssertLandsAtTop(Marked(window, page, anchor), anchor);
            }
        }
        finally { window.Close(); }
    });

    // A landing puts the section's top at the top of its page (owner, 2026-10-26: "as high as it can to show most of that section"),
    // or the page is scrolled as far as it goes.
    private static void AssertLandsAtTop(DependencyObject? target, string anchor)
    {
        var element = Assert.IsAssignableFrom<FrameworkElement>(target);
        ScrollViewer? scroller = null;
        for (var d = VisualTreeHelper.GetParent(element); d is not null && scroller is null; d = VisualTreeHelper.GetParent(d))
            scroller = d as ScrollViewer;
        Assert.NotNull(scroller);
        var top = element.TransformToAncestor(scroller!).Transform(new Point(0, 0)).Y;
        bool atTop = Math.Abs(top - SettingsWindow.LandingTopMargin) <= 2;
        bool atEnd = scroller!.VerticalOffset >= scroller.ScrollableHeight - 1;
        bool atStart = scroller.VerticalOffset <= 0 && top <= SettingsWindow.LandingTopMargin + 2;
        Assert.True(atTop || atEnd || atStart,
            $"{anchor}: top {top:0} px below the page top (offset {scroller.VerticalOffset:0} of {scroller.ScrollableHeight:0})");
    }

    [Fact]
    public void The_jump_links_reach_UI_Automation_as_one_named_group_of_the_links_alone() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var links = Below<SectionJumpLinks>((DependencyObject)TabOf(window, SettingsPage.Desk).Content).Single();
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(links);
            Assert.NotNull(peer);
            Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.True(peer.IsControlElement());
            Assert.Equal(SettingsNames.JumpLinksName, peer.GetName());
            var children = peer.GetChildren();
            Assert.Equal(SettingsIndex.JumpLinks(SettingsPage.Desk).Select(l => l.Title), children.Select(c => c.GetName()));
            Assert.All(children, c => Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Button, c.GetAutomationControlType()));
            // The separators read as a middle dot (U+00B7), not a mis-decoded "Â·" (the source file must stay UTF-8).
            var dots = links.Children.OfType<TextBlock>().ToList();
            Assert.Equal(3, dots.Count);
            Assert.All(dots, d => Assert.Equal("·", d.Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_jump_link_lands_on_its_section_and_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var links = Below<SectionJumpLinks>((DependencyObject)TabOf(window, SettingsPage.Desk).Content).Single();
            Assert.Equal(SettingsNames.JumpLinksName, System.Windows.Automation.AutomationProperties.GetName(links));
            var advanced = links.Children.OfType<Button>().Single(b => (string)b.Content == SettingsNames.DeskAdvanced);
            advanced.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskAdvanced), window.Place);
            Assert.True(((Expander)window.FindName("DeskAdvancedSection")).IsExpanded);
        }
        finally { window.Close(); }
    });

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

    /// <summary>Done (spec, slice 3): the Desk page reaches Pieces in under 10 Tab stops. Keyboard focus is routed inside the
    /// never-activating window, as slice 2's Tab walk does.</summary>
    [DesktopFact]
    public void From_the_Desk_pages_first_stop_Tab_reaches_Pieces_in_under_10() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            svm.Desk.Enabled = true; // Arrange is a stop only while the Desk is on: the longest walk
            Settle(window, guard);
            guard.RouteKeyboard();
            var content = (DependencyObject)TabOf(window, SettingsPage.Desk).Content;
            var pieces = Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces);
            var first = Below<Control>(content).First(c => c is { Focusable: true, IsTabStop: true, IsVisible: true, IsEnabled: true });
            Keyboard.Focus(first);
            guard.Check();
            Assert.True(ReferenceEquals(Keyboard.FocusedElement, first), "keyboard focus could not be routed to the Desk page");
            int stops = 1;
            IInputElement? at = first;
            while (stops < 20 && at is not null && !Inside(at, pieces))
            {
                Next(at);
                Pump();
                guard.Check();
                at = Keyboard.FocusedElement;
                stops++;
            }
            Assert.True(Inside(at, pieces), $"Tab never reached Pieces; it stopped on {at?.GetType().Name ?? "nothing"}");
            Assert.True(stops == 8, $"Pieces is Tab stop {stops} from the page's first stop (pinned at 8 so a creeping count shows before the spec's 10)");
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

    [Fact]
    public void Choosing_the_Desk_for_the_glow_places_its_spot_and_its_line_says_where() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode, SettingsIndex.GlowWhere);
            Settle(window, guard);
            svm.ClaudeCodePage.GlowTarget = ClaudeCodeViewModel.GlowTargets.First(t => t.Kind == GlowTargetKind.Desk);
            Settle(window, guard);
            Assert.Contains("glow:tc", Store().LoadDesk().Layout);
            Assert.True(svm.Desk.GlowPiece.IsPlaced);                     // the Desk page heard it without a visit
            var line = Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.ClaudeCode, SettingsIndex.GlowWhere)).Single();
            Assert.True(line.IsVisible);
            Assert.Equal(svm.Desk.GlowPiece.Text, ((TextBlock)line.FindName("StatusText")).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Old_desk_links_land_on_the_new_pages() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            foreach (var (from, anchor, to, toAnchor) in new[]
            {
                (SettingsPage.Desk, SettingsIndex.DeskLook, SettingsPage.DeskLook, SettingsIndex.DeskLook),
                (SettingsPage.Desk, SettingsIndex.DeskInk, SettingsPage.DeskLook, SettingsIndex.DeskInk),
                (SettingsPage.Desk, SettingsIndex.DeskPlate, SettingsPage.DeskLook, SettingsIndex.DeskPlate),
                (SettingsPage.Desk, SettingsIndex.DeskWatchers, SettingsPage.Watchers, SettingsIndex.Watchers),
                (SettingsPage.Desk, SettingsIndex.DeskLauncher, SettingsPage.Launcher, SettingsIndex.LauncherPlace),
                (SettingsPage.Desk, SettingsIndex.DeskDirect, SettingsPage.Desk, SettingsIndex.DeskDesigns),
                (SettingsPage.DeskMessage, SettingsIndex.MessageLook, SettingsPage.DeskLook, SettingsIndex.DeskSizes),
                (SettingsPage.Alerts, SettingsIndex.SignInReminders, SettingsPage.Desk, SettingsIndex.SignInReminders),
                (SettingsPage.Widgets, SettingsIndex.WidgetsCompanion, SettingsPage.Desk, SettingsIndex.DeskPieces),
            })
            {
                window.SelectPage(from, anchor);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(to, toAnchor), window.Place);
                Assert.True(Marked(window, to, toAnchor).IsVisible, $"{from}#{anchor} landed on {to}#{toAnchor}, hidden");
            }
        }
        finally { window.Close(); }
    });

    /// <summary>Done (spec, slice 3): turning on Weather, Watchers or Now playing from their pages shows the piece, with no visit
    /// to Desk.</summary>
    [Fact]
    public void Turning_on_watchers_now_playing_and_weather_from_their_pages_shows_each_piece() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        var visited = new List<SettingsPage>();
        try
        {
            void Visit(SettingsPage page)
            {
                window.SelectPage(page);
                Settle(window, guard);
                visited.Add(window.Place!.Page);
            }

            Visit(SettingsPage.Watchers);
            Below<CheckBox>(Marked(window, SettingsPage.Watchers, SettingsIndex.Watchers)).Single().IsChecked = true;
            Settle(window, guard);
            Assert.NotEqual(SettingsPage.Desk, window.Place!.Page);
            Visit(SettingsPage.NowPlaying);
            var bars = Below<CheckBox>((DependencyObject)TabOf(window, SettingsPage.NowPlaying).Content)
                .Single(c => (c.Content as string) == "Bars beside it on the Desk");
            bars.IsChecked = false;          // the page loads with Bars on: turning it on is a change
            Settle(window, guard);
            bars.IsChecked = true;
            Settle(window, guard);
            Assert.NotEqual(SettingsPage.Desk, window.Place!.Page);
            Visit(SettingsPage.Weather);
            svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
            Settle(window, guard);
            Assert.NotEqual(SettingsPage.Desk, window.Place!.Page);

            var layout = Store().LoadDesk().Layout;
            Assert.Contains("watchers:mr", layout);
            Assert.Contains("nowPlaying:br", layout);
            Assert.Contains("weather:tr", layout);
            Assert.DoesNotContain(SettingsPage.Desk, visited);
            Assert.NotEqual(SettingsPage.Desk, window.Place!.Page);
            var weatherLine = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, SettingsPage.Weather).Content).Single();
            Assert.StartsWith("On the Desk: top right.", ((TextBlock)weatherLine.FindName("StatusText")).Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Done (spec, slice 3): two pieces in one corner reorder by keyboard (WF5). Keyboard focus is routed inside the
    /// never-activating window; Space on the focused Move down button presses it.</summary>
    [DesktopFact]
    public void Two_pieces_in_one_place_reorder_from_the_keyboard() => AppHost.Run(() =>
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl claude:bl" });
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            guard.RouteKeyboard();
            var down = Below<Button>(Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces)).First(b =>
                System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.MoveDownIn(SettingsNames.DeskClock, "Bottom left"));
            Keyboard.Focus(down);
            guard.Check();
            Assert.True(down.IsKeyboardFocused, "keyboard focus could not be routed to Move down");
            var source = PresentationSource.FromVisual(down)!;
            down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Settle(window, guard);
            Assert.Equal("claude:bl clock:bl", Store().LoadDesk().Layout);
        }
        finally { window.Close(); }
    });
}
