using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using DeskInkChoice = Sanduhr.Core.DeskInkChoice;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>The Looks section of Settings, Desk (5.0 spec 5, section 6): six cards that are buttons, their accessible names, the look
/// on the Desk marked, a live status line, and a card drawn from the fitted look.</summary>
public sealed class DeskLooksGalleryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-gallery-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private double _wall = 0.03;

    public DeskLooksGalleryTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool>()));
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:tr:1.4 claude:bl", Font = "Segoe UI" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A look that shows the clock and the message at one anchor and <paramref name="extraShown"/> bottom right.</summary>
    private static string LookJson(string id, string name, string clockAnchor, string extraShown)
    {
        string hidden = string.Join(", ", new[] { "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "glow", "companion" }
            .Where(p => p != extraShown).Select(p => "\"" + p + "\": { \"hidden\": true }"));
        return $$"""
            {
              "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
              "design": {
                "layout": {
                  "clock": { "anchor": "{{clockAnchor}}", "size": 1.3 },
                  "message": { "anchor": "{{clockAnchor}}", "size": 1 },
                  "{{extraShown}}": { "anchor": "br", "size": 1 },
                  {{hidden}}
                },
                "font": "Georgia", "ink": "#ffd08a", "plate": false, "shadow": false, "glow": 0.7, "follows_theme": false
              }
            }
            """;
    }

    private static readonly DeskLookCatalog Catalog = DeskLookCatalog.Load([
        ("alpha", LookJson("alpha", "Alpha", "tc", "launcher")),
        ("beta", LookJson("beta", "Beta", "tl", "weather")),
        ("gamma", LookJson("gamma", "Gamma", "bc", "watchers")),
    ]);

    private static WallpaperOverview Flat(double brightness)
    {
        var readings = DeskAnchors.All.Select(a => new AnchorReading(a, brightness, 0)).ToList();
        var summary = new WallpaperSummary([new DominantColor("#203040", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private void WithGallery(Action<DeskLooksViewModel, DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = Catalog,
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => Flat(_wall);
        body(new DeskLooksViewModel(() => service), service);
    });

    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var found in Visuals<T>(VisualTreeHelper.GetChild(node, i)))
                yield return found;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });

    [Fact]
    public void The_test_looks_are_good_looks() => Assert.Empty(Catalog.Skipped);

    // ---- the view model ----

    [Fact]
    public void There_is_a_card_per_look_with_what_a_screen_reader_says() => WithGallery((vm, _) =>
    {
        Assert.Equal(["Alpha", "Beta", "Gamma"], vm.Cards.Select(c => c.Name));
        var alpha = vm.Cards[0];
        Assert.Equal("A test look.", alpha.Blurb);
        Assert.False(alpha.Current);
        Assert.Empty(alpha.Notes);
        Assert.Equal(["The Launcher needs buttons."], alpha.Needs.Select(n => n.Text));
        Assert.Equal("Alpha look. A test look. The Launcher needs buttons.", alpha.AccessibleName);
        Assert.Equal("Weather needs a place.", vm.Cards[1].Needs.Single().Text);
        Assert.Equal("Watchers needs a watcher.", vm.Cards[2].Needs.Single().Text);
        Assert.All(vm.Cards.SelectMany(c => c.Needs), n => Assert.False(string.IsNullOrEmpty(n.Link)));
        Assert.Equal(SettingsIndex.PageId(SettingsPage.Launcher) + "#" + SettingsIndex.LauncherButtons, alpha.Needs.Single().Link);
        Assert.Equal("203040", alpha.BackdropHex);
        Assert.Equal(0.03, alpha.Brightness);
        Assert.Null(alpha.Wallpaper);   // a test's wallpaper has no picture
        Assert.False(vm.HasStatus);
        Assert.False(vm.CanPutBack);
    });

    [Fact]
    public void A_card_applies_its_look_marks_it_and_says_so_without_replacing_the_cards() => WithGallery((vm, _) =>
    {
        var cards = vm.Cards.ToList();
        var statuses = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Status)) statuses.Add(vm.Status); };

        vm.Cards[1].ApplyCommand.Execute(null);

        Assert.Equal("Beta is on your Desk.", vm.Status);
        Assert.Equal(["Beta is on your Desk."], statuses);   // the line was empty already, so there was nothing to clear
        Assert.True(vm.Cards[1].Current);
        Assert.False(vm.Cards[0].Current);
        Assert.Equal("Beta look. A test look. Weather needs a place. On your Desk.", vm.Cards[1].AccessibleName);
        Assert.True(cards.SequenceEqual(vm.Cards));   // the same objects: the keyboard stays on the pressed card
        Assert.True(vm.CanPutBack);
        Assert.True(vm.PutBackCommand.CanExecute(null));

        vm.Cards[1].ApplyCommand.Execute(null);
        Assert.Equal("Beta is your Desk already.", vm.Status);
        Assert.Equal(["Beta is on your Desk.", "", "Beta is your Desk already."], statuses);   // cleared first, so a repeated line is still announced

        vm.PutBackCommand.Execute(null);
        Assert.Equal("Your Desk is back as it was.", vm.Status);
        Assert.DoesNotContain(vm.Cards, c => c.Current);
    });

    [Fact]
    public void A_card_carries_at_most_two_notes_fit_notes_first() => WithGallery((vm, service) =>
    {
        service.InstalledFonts = () => ["Segoe UI"];   // no Georgia: a font note
        service.FontsChanged();                         // the service drops its cached list
        _wall = 0.98;                                   // and an ink note
        vm.Reload();
        var alpha = vm.Cards[0];
        Assert.Equal(["Uses Segoe UI on this PC.", DeskLookFit.AutoInkNote], alpha.Notes);
        Assert.Empty(alpha.Needs);   // both lines are taken
        Assert.Equal("Alpha look. A test look. Uses Segoe UI on this PC. Uses automatic ink on your wallpaper.", alpha.AccessibleName);
    });

    [Fact]
    public void With_no_service_the_gallery_is_empty_and_harmless() => Sta.Run(() =>
    {
        var vm = new DeskLooksViewModel(() => null);
        Assert.Empty(vm.Cards);
        Assert.False(vm.CanPutBack);
        vm.Reload();
    });

    // ---- the panel ----

    private static DeskLooksPanel Shown(DeskLooksViewModel vm)
    {
        var panel = new DeskLooksPanel { DataContext = vm };
        panel.Measure(new Size(700, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));
        panel.UpdateLayout();
        Pump();
        return panel;
    }

    private static List<Button> LookButtons(DeskLooksPanel panel) =>
        [.. Visuals<Button>(panel).Where(b => b.DataContext is DeskLookCardViewModel && b.Command is not null && b.Content is not string)];

    [Fact]
    public void The_cards_are_buttons_with_their_names_and_no_list_selects_them() => WithGallery((vm, _) =>
    {
        var panel = Shown(vm);
        var buttons = LookButtons(panel);

        Assert.Equal(3, buttons.Count);
        Assert.Equal(vm.Cards.Select(c => c.AccessibleName), buttons.Select(AutomationProperties.GetName));
        Assert.All(buttons, b => Assert.True(b.Focusable));
        Assert.Empty(Visuals<Selector>(panel));   // a selecting list would apply a look on every arrow key

        var row = Assert.Single(Visuals<DeskLooksRow>(panel));
        var peer = UIElementAutomationPeer.CreatePeerForElement(row);
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        Assert.Equal("Ready-made looks", peer.GetName());
        Assert.Equal(3, Visuals<DeskLookCard>(panel).Count());
    });

    [Fact]
    public void Focus_alone_changes_nothing_and_Invoke_applies() => WithGallery((vm, _) =>
    {
        var panel = Shown(vm);
        var button = LookButtons(panel)[2];
        string before = _store.LoadDesk().Layout;

        button.Focus();
        Pump();
        Assert.Equal(before, _store.LoadDesk().Layout);
        Assert.False(vm.HasStatus);

        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        Pump();
        Assert.Equal("Gamma is on your Desk.", vm.Status);
        Assert.NotEqual(before, _store.LoadDesk().Layout);
        Assert.Equal(vm.Cards[2].AccessibleName, AutomationProperties.GetName(button));   // the name follows the state
        Assert.EndsWith("On your Desk.", AutomationProperties.GetName(button));
    });

    [Fact]
    public void The_panel_has_put_back_a_live_status_line_and_need_links()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        string views = Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views");
        string panel = File.ReadAllText(Path.Combine(views, "DeskLooksPanel.xaml"));
        string window = File.ReadAllText(Path.Combine(views, "SettingsWindow.xaml"));

        Assert.Contains("Command=\"{Binding PutBackCommand}\"", panel);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.PutMyDeskBack}\"", panel);
        Assert.Contains("views:LiveRegion.Announces=\"True\"", panel);
        Assert.Contains("Text=\"{Binding Status}\"", panel);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level2\"", panel);
        Assert.Contains("views:SettingsLinkTo.Link=\"{Binding Link}\"", panel);
        Assert.Contains("{x:Static core:SettingsNames.OnYourDesk}", panel);
        Assert.DoesNotContain("<ListBox", panel);

        // On the Desk page, under the Desk switch and above Arrange and the pieces; greyed while Arrange Desk runs.
        int desk = window.IndexOf("views:SettingsAnchor.Id=\"desk\"", StringComparison.Ordinal);
        int looks = window.IndexOf("views:SettingsAnchor.Id=\"looks\"", StringComparison.Ordinal);
        int arrange = window.IndexOf("views:SettingsAnchor.Id=\"arrange\"", StringComparison.Ordinal);
        int pieces = window.IndexOf("views:SettingsAnchor.Id=\"pieces\"", StringComparison.Ordinal);
        Assert.True(desk > 0 && desk < looks && looks < arrange && arrange < pieces);
        Assert.Contains("<views:DeskLooksPanel DataContext=\"{Binding Looks}\" />", window);
        string section = window[looks..arrange];
        Assert.Contains("IsEnabled=\"{Binding NotArranging}\"", section);
    }

    // ---- a card's picture ----

    private static DeskLookCard Card(DeskDesign design, string hex = "203040", double? brightness = 0.03)
    {
        DeskLookCard.FontsForTests = new DeskFonts(["Georgia", "Segoe UI"]);
        var card = new DeskLookCard { LookName = "Alpha", BackdropHex = hex, Brightness = brightness, Design = design };
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        return card;
    }

    [Fact]
    public void A_card_draws_the_name_where_the_clock_is_and_a_bar_for_every_other_piece() => Sta.Run(() =>
    {
        var look = Catalog.Find("alpha")!;   // clock and message at tc, launcher at br
        var card = Card(look.Design);

        Assert.Equal(DeskLookCard.CardWidth, card.Width);
        Assert.Equal(DeskLookCard.CardHeight, card.Height);
        var name = Assert.Single(card.Children.OfType<TextBlock>());
        Assert.Equal("Alpha", name.Text);
        Assert.Equal("Georgia", name.FontFamily.Source);
        // Top center: the name's middle is the card's middle, in its upper half.
        double left = Canvas.GetLeft(name), top = Canvas.GetTop(name);
        Assert.Equal(DeskLookCard.CardWidth / 2, left + name.DesiredSize.Width / 2, 1);
        Assert.True(top < DeskLookCard.CardHeight / 2);
        // The amber ink, glowing.
        Assert.Equal(DeskView.Rgb("ffd08a"), Assert.IsType<SolidColorBrush>(name.Foreground).Color);
        Assert.Equal(0, Assert.IsType<DropShadowEffect>(name.Effect).ShadowDepth);

        var bars = card.Children.OfType<Rectangle>().Where(r => (r.Tag as string) == "piece").ToList();
        Assert.Equal(2, bars.Count);   // message and launcher
        foreach (var bar in bars)
        {
            Assert.InRange(Canvas.GetLeft(bar), 0, DeskLookCard.CardWidth - bar.Width);
            Assert.InRange(Canvas.GetTop(bar), 0, DeskLookCard.CardHeight - bar.Height);
        }
        // The backdrop is the wallpaper's first color when there is no picture.
        var backdrop = card.Children.OfType<Rectangle>().First(r => (r.Tag as string) == "backdrop");
        Assert.Equal(DeskView.Rgb("203040"), Assert.IsType<SolidColorBrush>(backdrop.Fill).Color);
    });

    [Fact]
    public void A_plated_look_draws_its_plates_and_an_automatic_ink_follows_the_wallpaper() => Sta.Run(() =>
    {
        var plated = Catalog.Find("alpha")!.Design with { Plate = true, Ink = DeskInkChoice.Of(DeskInkKind.Auto), Glow = DeskGlowChoice.Off, Shadow = true };

        var dark = Card(plated, brightness: 0.03);
        Assert.Equal(2, dark.Children.OfType<Rectangle>().Count(r => (r.Tag as string) == "plate"));   // one per stack: tc and br
        Assert.Equal(Colors.White, ((SolidColorBrush)dark.Children.OfType<TextBlock>().Single().Foreground).Color);

        var bright = Card(plated, brightness: 0.95);
        Assert.NotEqual(Colors.White, ((SolidColorBrush)bright.Children.OfType<TextBlock>().Single().Foreground).Color);
    });

    [Fact]
    public void A_card_with_no_design_or_no_color_still_draws() => Sta.Run(() =>
    {
        DeskLookCard.FontsForTests = new DeskFonts(["Segoe UI"]);
        var empty = new DeskLookCard { LookName = "Alpha", BackdropHex = "" };
        empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Single(empty.Children);   // the backdrop alone, in the theme's background
        Assert.Empty(empty.Children.OfType<TextBlock>());
    });
}
