using System.IO;
using System.Windows;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The windows outside Settings, read the way Narrator reads them: the widget with its tier cards, What's New and the
/// welcome tour, the Desk review bar and the small dialogs. Sample data only. Nothing may be named for a type
/// ("Sanduhr.App.ViewModels.TierCardViewModel") or a record's text ("MeterPreviewRow { Label = ... }").
/// </summary>
[Collection("SettingsWindow")]
public sealed class AppWindowNamesTests : AppWindowFixture
{
    private static void NoLeaks(string where, IEnumerable<string> names)
    {
        var leaks = LeaksIn(names);
        Assert.True(leaks.Count == 0, where + ": " + string.Join(" | ", leaks));
    }

    [Fact]
    public void TheWidgetsTierCardsReadInWords() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        Assert.NotEmpty(widget.Tiers);
        var window = new MainWindow { DataContext = widget };
        OffScreen(window);
        window.Show();
        try
        {
            var names = NamesOf(window);
            NoLeaks("MainWindow", names);
            foreach (var tier in widget.Tiers)
            {
                Assert.Contains(names, n => n == tier.AutomationName);
                Assert.StartsWith(tier.Label + ", ", tier.AutomationName);
            }
            Assert.Contains(names, n => n.StartsWith("Session (5hr), ") && n.Contains('%'));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void WhatsNewNamesItsCardsByTheirTitle() => AppHost.Run(() =>
    {
        var store = new SettingsStore(new Paths(appDataBase: Path.Combine(Root, "appdata")));
        var cards = WhatsNewCards.Table.Take(6).ToList();
        WhatsNewWindow.Show(cards, "What's new in 3.7.1", store, _ => { });
        var window = Application.Current.Windows.OfType<WhatsNewWindow>().Single();
        OffScreen(window);
        try
        {
            var names = NamesOf(window);
            NoLeaks("What's New", names);
            foreach (var card in cards)
                Assert.Contains(names, n => n == card.Title);
        }
        finally { window.Close(); }
    });

    [DesktopFact]
    public void TheWelcomeTourNamesItsCardsAndMeterRows() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        var steps = WelcomeTour.StepsFor(false, new HashSet<TourFeature> { TourFeature.Desk });
        var tour = new WelcomeTourViewModel(steps, widget);
        WhatsNewWindow.ShowTour(tour, _ => { }, _ => { });
        var window = Application.Current.Windows.OfType<WhatsNewWindow>().Single();
        OffScreen(window);
        try
        {
            var seen = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                tour.GoTo(i);
                var names = NamesOf(window);
                NoLeaks($"Tour step {i + 1}", names);
                seen.AddRange(names);
                foreach (var item in tour.Items)
                    Assert.Contains(names, n => n == item.Title);
            }
            // The Meters preview, drawn from the demo meters, reads "Session (5hr) 12%".
            Assert.Contains(seen, n => n.StartsWith("Session (5hr) ") && n.EndsWith('%'));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheDeskReviewBarReadsItsChanges() => AppHost.Run(() =>
    {
        var window = new DeskReviewWindow();
        OffScreen(window);
        window.SetModel(new DeskReviewModel("Dusk", "Warmer ink for the evening.", ["Clock moves to the bottom left", "Plate on"], [], false, []));
        window.Show();
        try
        {
            var names = NamesOf(window);
            NoLeaks("Desk review", names);
            Assert.Contains("Clock moves to the bottom left", names);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheKeyWindowsCloudflareHelpIsAFocusableNamedLink() => AppHost.Run(() =>
    {
        var window = new ManualKeyWindow("Work", (_, _) => "", embeddedAvailable: false);
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            var link = Assert.IsType<System.Windows.Documents.Hyperlink>(window.FindName("CloudflareLink"));
            var panel = Assert.IsType<System.Windows.Controls.StackPanel>(window.FindName("CloudflarePanel"));
            Assert.True(link.Focusable, "the Cloudflare link can't take keyboard focus");
            var name = System.Windows.Automation.AutomationProperties.GetName(link);
            Assert.StartsWith(SettingsNames.ManualKeyCloudflareToggle, name);
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Contains(NamesOf(window), n => n.StartsWith(SettingsNames.ManualKeyCloudflareToggle));

            link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            Assert.Equal(Visibility.Visible, panel.Visibility);
            link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheSmallWindowsHaveNoTypeNames() => AppHost.Run(() =>
    {
        var windows = new List<Window>
        {
            new ManualKeyWindow("Work", (_, _) => "", embeddedAvailable: false),
            new LauncherLinkWindow(),
            new PublishTokenWindow(),
            new TextPromptWindow("Rename", "New name", "Dusk"),
            new ResetWindow(),
        };
        foreach (var window in windows)
        {
            OffScreen(window);
            window.Show();
            try { NoLeaks(window.GetType().Name, NamesOf(window)); }
            finally { window.Close(); }
        }
    });

    [Fact]
    public void TheDeskArrangeBarAndTheTimerAndGameControlsHaveNoTypeNames() => AppHost.Run(() =>
    {
        foreach (UIElement control in new UIElement[] { new DeskArrangeBar(), new FocusTimer(), new CooldownGame() })
        {
            var window = new Window { Content = control };
            OffScreen(window);
            window.Show();
            try { NoLeaks(control.GetType().Name, NamesOf(window)); }
            finally { window.Close(); }
        }
    });
}
