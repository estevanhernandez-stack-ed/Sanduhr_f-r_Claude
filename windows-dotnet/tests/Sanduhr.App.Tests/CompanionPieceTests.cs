using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The companion on the Desk (spec 3.8 section 4), real <see cref="DeskView"/> layouts on an STA thread over a sprite pack
/// built in the test: a pixel-sheet stage of 8 px cells and a PNG stage of 16 px cells, every frame one solid color so a
/// rendered pixel says which frame is drawn. Installed through <see cref="WidgetStore"/> into a scratch folder (the
/// rasterized pixel sheet lands in <c>.raster</c>); no binary assets, no real Sanduhr folder, no network.
/// </summary>
[Collection("SettingsWindow")]
public sealed class CompanionPieceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string PackId = "tiny-sprite";

    private static readonly string[] SmallColors = ["#22d3ee", "#a855f7", "#ec4899", "#f59e0b"];
    private static readonly string[] BigColors = ["#10b981", "#3b82f6", "#ef4444", "#eab308"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-companion-app-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public CompanionPieceTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"), Path.Combine(_root, "home", ".sanduhr"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A pixel sheet of four frames, frame i filled with palette color i.</summary>
    private static string Sheet(string[] colors, int size)
    {
        var frames = Enumerable.Range(0, 4).Select(i =>
            "[" + string.Join(",", Enumerable.Repeat("\"" + new string((char)('0' + i), size) + "\"", size)) + "]");
        return $$"""{"palette":[{{string.Join(",", colors.Select(c => "\"" + c + "\""))}}],"frame_w":{{size}},"frame_h":{{size}},"frames":[{{string.Join(",", frames)}}]}""";
    }

    private const string Sprite = """
        {
          "schema_version": 2, "frame_w": 8, "frame_h": 8, "scale": 2, "fps": 8,
          "stages": [
            { "id": "small", "name": "Small", "min_growth": 0, "sheet": "sprites/small.json", "frame_w": 8, "frame_h": 8 },
            { "id": "big", "name": "Big", "min_growth": 0.5, "sheet": "sprites/big.png", "frame_w": 16, "frame_h": 16 }
          ],
          "animations": { "walk": [0, 1], "sit": [0], "nap": [1], "pop": [2], "cheer": [3], "worry": [1], "focus": [2], "sleep": [3], "celebrate": [3] },
          "moods": { "content": "sit", "focused": "focus", "worried": "worry", "exhausted": "sleep", "celebrate": "celebrate" },
          "triggers": { "needs_you": "pop", "job_passed": "cheer", "near_limit": "worry", "idle": "nap" },
          "roam": { "edges": ["bottom"], "speed_px_s": 60 }
        }
        """;

    /// <summary>Builds, validates and installs the test pack; lays the Desk out with <paramref name="layout"/>.</summary>
    private void Install(string layout = "companion:bl", bool roam = false)
    {
        var big = PixelSheet.Rasterize(Encoding.UTF8.GetBytes(Sheet(BigColors, 16)), out var why) ?? throw new InvalidOperationException(why);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["pack.json"] = Encoding.UTF8.GetBytes($$"""
                {"schema_version": 1, "id": "{{PackId}}", "name": "Tiny sprite", "version": "1.0.0", "author": "Test Author", "license": "MIT",
                 "kind": "sprite", "sanduhr_min": "3.8.0", "description": "A test companion.",
                 "files": ["sprite.json", "sprites/small.json", "sprites/big.png"]}
                """),
            ["LICENSE"] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", "good-sprite-pixel-stages", "LICENSE")),
            ["sprite.json"] = Encoding.UTF8.GetBytes(Sprite),
            ["sprites/small.json"] = Encoding.UTF8.GetBytes(Sheet(SmallColors, 8)),
            ["sprites/big.png"] = WidgetCatalogService.EncodePng(big),
        };
        var verdict = PackValidator.Validate(files, folderName: PackId);
        Assert.True(verdict.Ok, string.Join("; ", verdict.Errors));
        var result = new WidgetStore(_paths.WidgetsDir, WidgetCatalogService.EncodePng).Install(files, verdict.Manifest!, WidgetSource.Local);
        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(_paths.WidgetsDir, PackId, ".raster", "sprites", "small.json.png")));
        _store.SaveDesk((DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI" }).Normalized());
        _store.SaveCompanion(new CompanionSettings(PackId, roam));
    }

    private CompanionService Service(bool reduceMotion = false, DateTimeOffset? lastInput = null)
    {
        var s = new CompanionService(_paths, _store, claudeCodeDays: () => [], lastInput: () => lastInput ?? Now, clock: () => Now,
            reduceMotion: () => reduceMotion);
        s.Reload();
        return s;
    }

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private DeskView View(CompanionService? service, bool arranging = false)
    {
        var view = new DeskView(Screen(), _store.LoadDesk(), new DeskFonts(["Segoe UI"])) { Arranging = arranging };
        view.Update(new DeskFrame { Now = Now, Companion = service }, DeskInkMap.Light);
        return view;
    }

    private static CompanionPieceVisual? Find(DependencyObject root)
    {
        if (root is CompanionPieceVisual p) return p;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (Find(child) is { } found) return found;
        return null;
    }

    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The island drawn as the host would draw it (in a root border), read back at one pixel.</summary>
    private static Color Rendered(FrameworkElement island, int x, int y)
    {
        var content = island.Parent as System.Windows.Controls.Border ?? new System.Windows.Controls.Border { Child = island };
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(content);
        return Pixel(rtb, x, y);
    }

    [Fact]
    public void The_piece_renders_the_moods_frame_cut_from_the_rasterized_pixel_sheet() => Sta.Run(() =>
    {
        Install();
        using var service = Service();
        Assert.True(service.Ready);
        Assert.Equal("small", service.Stage!.Id);
        Assert.Equal(new Size(16, 16), service.CellSize);   // 8 px cells at the pack's scale 2

        using var view = View(service);
        var island = view.TakeIslands().Single(i => i.Widgets.Contains(CompanionSettings.Widget));
        Assert.Null(island.Motion);
        var piece = Find(island.Content)!;
        Assert.NotNull(piece.Shown);
        Assert.Equal(16, piece.Width);
        Assert.False(piece.IsHitTestVisible);
        Assert.Equal(new CompanionPose("sit", 0, false), service.Pose);

        // Nearest-neighbour: the 8 px frame fills the 16 px piece in its one color, edge to edge.
        Assert.Equal(Hex(SmallColors[0]), Rendered(island.Content, 0, 15));
        Assert.Equal(Hex(SmallColors[0]), Rendered(island.Content, 15, 0));
        Assert.Equal(Hex(SmallColors[0]), Rendered(island.Content, 8, 8));
    });

    [Fact]
    public void The_piece_size_is_the_cell_times_the_packs_scale_times_the_piece_size() => Sta.Run(() =>
    {
        Install("companion:bl:1.5");
        using var service = Service();
        using var view = View(service);
        var piece = Find(view.TakeIslands().Single(i => i.Widgets.Contains(CompanionSettings.Widget)).Content)!;
        Assert.Equal(24, piece.Width);
        Assert.Equal(24, piece.Height);
    });

    [Fact]
    public void Growth_moves_it_to_the_larger_png_stage_and_never_back() => Sta.Run(() =>
    {
        Install();
        using var service = Service();
        int changed = 0;
        service.Changed += () => changed++;
        service.ApplyGrowth(new CompanionGrowthState(0.6, new HashSet<DateOnly>(), TimeSpan.Zero));
        Assert.Equal(1, changed);
        Assert.Equal("big", service.Stage!.Id);
        Assert.Equal(new Size(32, 32), service.CellSize);
        service.ObserveUsage(new DeskUsage([new DeskTier(TierModel.FiveHour, 100, "2026-10-22T14:00:00Z")], Now, false, false, null));
        service.Step(Now);
        Assert.Equal(new CompanionPose("sleep", 3, false), service.Pose);
        var frame = (BitmapSource)service.CurrentImage!;
        Assert.Equal((16, 16), (frame.PixelWidth, frame.PixelHeight));
        Assert.Equal(Hex(BigColors[3]), Pixel(frame, 8, 8));

        service.ApplyGrowth(new CompanionGrowthState(0.1, new HashSet<DateOnly>(), TimeSpan.Zero));
        Assert.Equal(0.6, service.Growth);
        Assert.Equal("big", service.Stage!.Id);
    });

    [Fact]
    public void A_watcher_waiting_plays_needs_you_once_then_the_mood() => Sta.Run(() =>
    {
        Install();
        using var service = Service();
        Watcher W(WatcherState s) => new() { Id = "a:1", Source = WatcherSource.Agent, Title = "deploy", State = s, Started = Now, Touched = Now };
        service.ObserveWatchers([W(WatcherState.Running)]);
        service.ObserveWatchers([W(WatcherState.Waiting)]);
        service.Step(Now);
        Assert.Equal(new CompanionPose("pop", 2, true), service.Pose);
        Assert.Equal(Hex(SmallColors[2]), Pixel((BitmapSource)service.CurrentImage!, 4, 4));
        service.Step(Now + TimeSpan.FromSeconds(0.2));
        Assert.Equal(new CompanionPose("sit", 0, false), service.Pose);
    });

    [Fact]
    public void Twenty_minutes_without_input_plays_the_nap() => Sta.Run(() =>
    {
        Install();
        using var service = Service(lastInput: Now - TimeSpan.FromMinutes(21));
        service.Step(Now);
        Assert.Equal(new CompanionPose("nap", 1, true), service.Pose);
    });

    [Fact]
    public void Roaming_it_is_its_own_island_that_moves_along_the_bottom_and_takes_no_clicks() => Sta.Run(() =>
    {
        Install("companion:bl", roam: true);
        using var service = Service();
        Assert.True(service.Roaming);
        using var view = View(service);
        var islands = DeskClicks.Prepare(view.TakeIslands(), true, new NoClicks());
        var island = islands.Single(i => i.Widgets.Contains(CompanionSettings.Widget));
        Assert.NotNull(island.Motion);
        Assert.False(island.TakesClicks);
        Assert.Equal(HorizontalAlignment.Center, island.Horizontal);
        Assert.Equal(VerticalAlignment.Bottom, island.Vertical);
        // It starts at the bottom left, nearest its anchor (the content area's corner, inside the margins), standing on the
        // work area's bottom edge.
        var start = island.Motion!.Anchor;
        Assert.Equal(1032, start.Y, 3);
        Assert.InRange(start.X, 9, 120);

        service.Want(this, true);
        service.Step(Now + TimeSpan.FromSeconds(0.5));
        Assert.True(service.Walking);
        Assert.Equal("walk", service.Pose.Animation);
        service.Step(Now + TimeSpan.FromSeconds(1.0));
        Assert.True(island.Motion.Anchor.X > start.X, $"{island.Motion.Anchor.X} after walking from {start.X}");
        Assert.Equal(1032, island.Motion.Anchor.Y, 3);
        service.Want(this, false);
    });

    [Fact]
    public void Reduce_motion_sits_in_its_stack_and_draws_at_one_frame_a_second() => Sta.Run(() =>
    {
        Install("companion:bl", roam: true);
        using var service = Service(reduceMotion: true);
        Assert.False(service.Roaming);
        using var view = View(service);
        var island = view.TakeIslands().Single(i => i.Widgets.Contains(CompanionSettings.Widget));
        Assert.Null(island.Motion);

        // The service's clock reads Now: the trigger starts there.
        service.ObserveWatchers([]);
        service.ObserveWatchers([new Watcher { Id = "a:1", Source = WatcherSource.Agent, Title = "ci", State = WatcherState.Passed, Started = Now, Touched = Now }]);
        service.Step(Now);
        Assert.Equal(new CompanionPose("cheer", 3, true), service.Pose);
        service.Step(Now + TimeSpan.FromSeconds(0.9));
        Assert.Equal("cheer", service.Pose.Animation);   // one frame takes a whole second
        service.Step(Now + TimeSpan.FromSeconds(1));
        Assert.Equal("sit", service.Pose.Animation);
        Assert.False(service.Walking);
        Assert.Equal(0, service.Distance);
    });

    [Fact]
    public void Arranging_draws_the_companion_at_its_anchor_even_when_it_roams() => Sta.Run(() =>
    {
        Install("companion:br", roam: true);
        using var service = Service();
        using var view = View(service, arranging: true);
        var island = view.TakeIslands().Single(i => i.Widgets.Contains(CompanionSettings.Widget));
        Assert.Null(island.Motion);
        Assert.NotNull(Find(island.Content));
    });

    [Fact]
    public void Without_an_installed_sprite_nothing_draws_and_arrange_shows_its_name() => Sta.Run(() =>
    {
        _store.SaveDesk((DeskSettings.Default with { Enabled = true, Layout = "companion:bl", Font = "Segoe UI" }).Normalized());
        using var service = Service();
        Assert.False(service.Ready);
        using var view = View(null);
        Assert.DoesNotContain(view.TakeIslands(), i => i.Widgets.Contains(CompanionSettings.Widget));
        using var arranging = View(null, arranging: true);
        var island = arranging.TakeIslands().Single(i => i.Widgets.Contains(CompanionSettings.Widget));
        Assert.Contains(SettingsNames.DeskCompanion, Texts(island.Content));
    });

    [Fact]
    public void Desk_pieces_companion_row_picks_the_sprite_shows_or_hides_it_and_lets_it_roam()
    {
        Install("clock:bl companion:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        int changed = 0;
        desk.Changed = () => changed++;
        var row = desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget);
        Assert.True(row.IsCompanion);
        var companion = Assert.IsType<DeskCompanionViewModel>(row.Companion);
        Assert.Null(desk.Pieces.Single(p => p.Widget == "clock").Companion);

        var choice = Assert.Single(companion.Companions);
        Assert.Equal((PackId, "Tiny sprite"), (choice.Id, choice.Name));
        Assert.True(companion.HasCompanions);
        Assert.Equal(choice, companion.SelectedCompanion);
        Assert.True(row.Shown);
        Assert.False(companion.Roams);
        Assert.Equal("Stage 1 of 2, Small. 0 coding days so far.", companion.Status);
        Assert.Equal(0, changed);

        companion.Roams = true;
        Assert.True(_store.LoadCompanion().Roam);
        row.Shown = false;
        Assert.Equal("clock:bl", _store.LoadDesk().Layout);
        Assert.Contains("hidden", companion.Status);
        row.Shown = true;
        Assert.Equal("clock:bl companion:bl", _store.LoadDesk().Layout);   // back where it was in this window
        Assert.Equal(3, changed);

        // Gone from disk: the row lists no sprite and says how to get one.
        Assert.True(svc.Store.Uninstall(PackId).Ok);
        desk.Reload();
        Assert.False(companion.HasCompanions);
        Assert.Null(companion.SelectedCompanion);
        Assert.Equal("mods#catalog", companion.GetOneLink);
    }

    [Fact]
    public void Sprites_installed_but_all_switched_off_say_so_and_link_to_the_installed_list_not_no_companion_yet()
    {
        Install("companion:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        var companion = Assert.IsType<DeskCompanionViewModel>(desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget).Companion);
        int turnedOn = 0;
        companion.TurnOneOn = () => turnedOn++;

        Assert.True(svc.Store.SetEnabled(PackId, false).Ok);
        desk.Reload();

        Assert.False(companion.HasCompanions);
        Assert.True(companion.SpritesAreOff);
        Assert.False(companion.NoSprites);
        Assert.Equal("Your sprites are switched off.", companion.SpritesOffLine);
        Assert.Equal(SettingsNames.SpritesSwitchedOff, companion.SpritesOffLine);
        Assert.Equal("mods#mods", companion.TurnOneOnLink);
        Assert.Equal(SettingsNames.TurnASpriteOn, companion.TurnOneOnTitle);
        Assert.Empty(SettingsNames.PathProblems(SettingsNames.SpritesSwitchedOff + " " + SettingsNames.TurnASpriteOn));
        companion.TurnOneOnClickedCommand.Execute(null);
        Assert.Equal(1, turnedOn);

        // Switched back on: the row lists the sprite and neither line shows.
        Assert.True(svc.Store.SetEnabled(PackId, true).Ok);
        desk.Reload();
        Assert.True(companion.HasCompanions);
        Assert.False(companion.SpritesAreOff);
        Assert.False(companion.NoSprites);
    }

    [Fact]
    public void With_no_sprite_installed_the_row_still_says_no_companion_yet_and_gets_one_from_the_catalog()
    {
        Install("companion:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        var companion = Assert.IsType<DeskCompanionViewModel>(desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget).Companion);
        Assert.True(svc.Store.Uninstall(PackId).Ok);
        desk.Reload();

        Assert.False(companion.HasCompanions);
        Assert.False(companion.SpritesAreOff);
        Assert.True(companion.NoSprites);
        Assert.Equal("No companion yet.", companion.NoneYet);
        Assert.Equal("mods#catalog", companion.GetOneLink);
    }

    [Fact]
    public void A_fresh_companion_is_shown_at_its_default_place()
    {
        Install("clock:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget).Shown = true;
        Assert.Equal("clock:bl companion:bc", _store.LoadDesk().Layout);   // DeskArrangement.DefaultPlaceFor("companion")
    }

    [Fact]
    public void The_companion_controls_carry_their_names_and_live_on_the_Desk_page()
    {
        var panel = File.ReadAllText(Path.Combine(AppDir(), "Views", "DeskPiecesPanel.xaml"));
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.WidgetCompanionPick}\"", panel);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.WidgetCompanionRoam}\"", panel);
        Assert.Contains("views:SettingsLinkTo.Link=\"{Binding GetOneLink}\"", panel);
        Assert.DoesNotContain("widgets-companion", File.ReadAllText(Path.Combine(AppDir(), "Views", "ModsPage.xaml")));
        Assert.Contains((SettingsNames.WidgetCompanionPick, SettingsPage.Desk), SettingsNames.Table);
        Assert.Contains((SettingsNames.WidgetCompanionRoam, SettingsPage.Desk), SettingsNames.Table);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static List<string> Texts(DependencyObject root)
    {
        var all = new List<string>();
        if (root is System.Windows.Controls.TextBlock t) all.Add(t.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) all.AddRange(Texts(child));
        return all;
    }

    private sealed class NoClicks : IDeskClickActions
    {
        public void PlayPause() { }
        public void OpenWeatherCredit() { }
        public void Menu(DeskMenuAction action) { }
        public void LaunchButton(int index) { }
    }
}
