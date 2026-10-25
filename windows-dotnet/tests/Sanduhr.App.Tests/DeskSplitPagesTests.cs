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
}
