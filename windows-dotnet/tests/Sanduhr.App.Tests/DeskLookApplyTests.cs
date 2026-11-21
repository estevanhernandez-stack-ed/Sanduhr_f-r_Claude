using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// One click applies a ready-made look (5.0 spec 5, section 5): the Desk is written, the Desk it replaced is saved once however many
/// looks are tried, a built-in look takes no saved-design slot, Put my Desk back returns to the Desk from before the first look, and
/// the user's content is never touched. Over a scratch AppData, with the looks, the fonts and the wallpaper set by the test.
/// </summary>
public sealed class DeskLookApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-looks-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private double _wall = 0.03;

    private const string UsersLayout = "clock:tr:1.4 claude:bl";

    public DeskLookApplyTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool>()));
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = UsersLayout, Font = "Segoe UI" });
        _store.SaveLauncher(new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string LookJson(string id, string name, string clockAnchor, bool glow) => $$"""
        {
          "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "{{clockAnchor}}", "size": 1.3 },
              "message": { "anchor": "{{clockAnchor}}", "size": 1 },
              "launcher": { "anchor": "br", "size": 1 },
              "claude": { "hidden": true }, "meters": { "hidden": true }, "nowPlaying": { "hidden": true },
              "telemetry": { "hidden": true }, "weather": { "hidden": true }, "watchers": { "hidden": true },
              "glow": { "hidden": true }, "companion": { "hidden": true }
            },
            "font": "Georgia", "ink": "#ffd08a", "plate": false,
            "shadow": {{(glow ? "false" : "true")}}, "glow": {{(glow ? "0.7" : "false")}}, "follows_theme": false
          }
        }
        """;

    private static readonly DeskLookCatalog Catalog = DeskLookCatalog.Load([
        ("alpha", LookJson("alpha", "Alpha", "tc", glow: true)),
        ("beta", LookJson("beta", "Beta", "tl", glow: true)),
        ("gamma", LookJson("gamma", "Gamma", "bc", glow: false)),
    ]);

    private static WallpaperOverview Flat(double brightness)
    {
        var readings = DeskAnchors.All.Select(a => new AnchorReading(a, brightness, 0)).ToList();
        var summary = new WallpaperSummary([new DominantColor("#808080", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private void WithService(Action<DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = Catalog,
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => Flat(_wall);
        body(service);
    });

    private string LayoutOf(string id) => DeskDesignApply.Layout(Catalog.Find(id)!.Design.Layout!);

    /// <summary>A layout as its placements, whatever order the string lists them in: Put my Desk back merges a saved design in, and
    /// a merge orders a stack by Settings order. A look's own order is asserted on the raw string instead.</summary>
    private static string Shape(string layout) => string.Join(" ", new DeskArrangement(layout).Pieces
        .OrderBy(p => p.Widget, StringComparer.Ordinal)
        .Select(p => $"{p.Widget}:{p.Anchor}:{p.Scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"));

    [Fact]
    public void The_test_looks_are_good_looks() => Assert.Empty(Catalog.Skipped);

    [Fact]
    public void A_click_writes_the_look_saves_the_Desk_it_replaced_and_points_put_back_at_it() => WithService(s =>
    {
        var launcher = _store.LoadLauncher().ToJson().ToJsonString();

        var result = s.ApplyLook("alpha");

        Assert.True(result.Ok);
        Assert.Equal("Alpha is on your Desk.", result.Message);
        Assert.Equal(LayoutOf("alpha"), _store.LoadDesk().Layout);
        Assert.Equal("Georgia", _store.LoadDesk().Font);
        var ink = _store.LoadDeskInk();
        Assert.Equal(DeskInkMode.Custom, ink.Mode);
        Assert.Equal("ffd08a", ink.Color);
        Assert.True(ink.Glow);
        Assert.Equal(0.7, ink.GlowStrength);
        Assert.False(ink.Shadow);
        Assert.False(_store.LoadDeskFollowsTheme());
        // The user's content is theirs.
        Assert.Equal(launcher, _store.LoadLauncher().ToJson().ToJsonString());

        var saved = Assert.Single(s.Designs());
        Assert.Equal("Before Alpha", saved.Name);
        Assert.True(s.CanPutBack);
    });

    [Fact]
    public void Flipping_through_looks_keeps_one_backup_and_put_back_returns_to_the_first_Desk() => WithService(s =>
    {
        Assert.True(s.ApplyLook("alpha").Ok);
        Assert.True(s.ApplyLook("beta").Ok);
        Assert.True(s.ApplyLook("gamma").Ok);
        Assert.Equal(LayoutOf("gamma"), _store.LoadDesk().Layout);
        // Rule A: one backup. Rule B: no built-in look in the saved designs.
        Assert.Equal(["Before Alpha"], s.Designs().Select(d => d.Name));

        var back = s.PutBack();
        Assert.True(back.Ok);
        Assert.Equal("Your Desk is back as it was.", back.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Segoe UI", _store.LoadDesk().Font);
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.Single(s.Designs());

        // A second press no longer toggles: it came from a look, which was never saved.
        var again = s.PutBack();
        Assert.True(again.Ok);
        Assert.Equal("Your Desk is already as it was.", again.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void A_look_the_user_changed_is_their_own_Desk_and_is_saved_when_the_next_one_replaces_it() => WithService(s =>
    {
        s.ApplyLook("alpha");
        var desk = _store.LoadDesk();
        var moved = new DeskArrangement(desk.Layout);
        moved.Place("clock", DeskAnchor.BR);
        _store.SaveDesk(desk with { Layout = moved.Layout });

        s.ApplyLook("beta");

        Assert.Equal(["Before Alpha", "Before Beta"], s.Designs().Select(d => d.Name).Order());
        // Put my Desk back now returns to the changed Alpha, the Desk that was just replaced.
        s.PutBack();
        Assert.Equal(Shape(moved.Layout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void Changing_only_the_users_own_content_leaves_the_look_untouched() => WithService(s =>
    {
        s.ApplyLook("alpha");
        _store.SaveLauncher(LauncherModel.Empty);
        s.ApplyLook("beta");
        Assert.Single(s.Designs());
    });

    [Fact]
    public void The_same_look_twice_changes_nothing_and_says_so() => WithService(s =>
    {
        s.ApplyLook("alpha");
        var again = s.ApplyLook("alpha");
        Assert.True(again.Ok);
        Assert.Equal("Alpha is your Desk already.", again.Message);
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_click_switches_the_Desk_on() => WithService(s =>
    {
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk. The Desk was switched on.", result.Message);
        Assert.True(_store.LoadDesk().Enabled);
        Assert.Equal(LayoutOf("alpha"), _store.LoadDesk().Layout);
        s.PutBack();
        Assert.True(_store.LoadDesk().Enabled);   // Put my Desk back does not switch the Desk off again
    });

    [Fact]
    public void The_cards_are_told_once_things_settle_not_at_each_event() => WithService(s =>
    {
        int told = 0;
        s.LooksChanged += () => told++;
        s.FontsChanged();
        Assert.Equal(0, told);   // a font event only asks for a refresh
        s.Settled();
        Assert.Equal(1, told);   // the settled refresh tells the gallery once
    });

    [Fact]
    public void A_click_ticks_a_nicer_desktop_again_when_setup_left_only_drawers() => WithService(s =>
    {
        _store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool>()));
        var result = s.ApplyLook("alpha");
        Assert.EndsWith("The Desk was switched on.", result.Message);
        Assert.True(_store.LoadSetup()!.Desktop);
    });

    [Fact]
    public void A_Desk_that_already_is_the_look_but_is_off_comes_on() => WithService(s =>
    {
        s.ApplyLook("alpha");
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk. The Desk was switched on.", result.Message);
        Assert.True(_store.LoadDesk().Enabled);
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_Desk_that_cannot_be_saved_first_is_left_as_it_is() => WithService(s =>
    {
        File.WriteAllText(_paths.DesignsDir, "not a folder");   // streamer: notname - a scratch path in a test
        var result = s.ApplyLook("alpha");
        Assert.False(result.Ok);
        Assert.Equal("The Desk could not be saved first, so nothing was changed.", result.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.False(s.CanPutBack);
    });

    [Fact]
    public void A_drawers_review_refuses_the_click_and_changes_nothing() => WithService(s =>
    {
        s.DrawersReviewing = () => true;
        var result = s.ApplyLook("alpha");
        Assert.False(result.Ok);
        Assert.Equal("Finish the drawers review on your Desk first.", result.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Empty(s.Designs());
    });

    private sealed class FakeHost : IDeskDesignHost
    {
        public int Shown, Ended, Applied;
        public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss) => Shown++;
        public void EndPreview() => Ended++;
        void IDeskDesignHost.Applied(string? themeKey) => Applied++;
    }

    [Fact]
    public void A_design_under_review_is_dismissed_by_the_click() => WithService(s =>
    {
        var host = new FakeHost();
        s.Host = host;
        s.ProposePasted(s.ValidatePasted(JsonNode.Parse("""{"plate":true}""")!));
        Assert.True(s.Reviewing);

        var result = s.ApplyLook("alpha");

        Assert.True(result.Ok);
        Assert.False(s.Reviewing);
        Assert.True(host.Ended >= 1);
        Assert.Equal(1, host.Applied);
        Assert.False(_store.LoadDesk().Plate);   // the reviewed design was never written
        s.Host = null;
    });

    [Fact]
    public void A_piece_on_an_unplugged_monitor_keeps_its_monitor() => WithService(s =>
    {
        const string gone = @"\\?\DISPLAY#GONE#1";
        _store.SaveDesk(_store.LoadDesk() with { Layout = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.TR, 1.4, gone)]).Layout });
        s.Monitors = () => [new DeskMonitorInfo(@"\\?\DISPLAY#HERE#1", "1920×1080, main", 1920, 1080, true, 0, 0)];

        Assert.True(s.ApplyLook("alpha").Ok);

        var clock = new DeskArrangement(_store.LoadDesk().Layout).Placement("clock")!;
        Assert.Equal(DeskAnchor.TC, clock.Anchor);
        Assert.True(DeskMonitors.Same(gone, clock.Monitor));
    });

    [Fact]
    public void After_the_wallpaper_changes_the_same_look_is_fitted_again_and_nothing_more_is_saved() => WithService(s =>
    {
        s.ApplyLook("alpha");
        Assert.Equal(DeskInkMode.Custom, _store.LoadDeskInk().Mode);

        _wall = 0.98;
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk.", result.Message);
        Assert.Equal(DeskInkMode.Auto, _store.LoadDeskInk().Mode);
        Assert.True(_store.LoadDeskInk().Glow);

        // Still that look: the next one saves nothing new.
        s.ApplyLook("beta");
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_missing_font_takes_the_fallback() => WithService(s =>
    {
        s.InstalledFonts = () => ["Segoe UI"];   // before the service first reads the list
        s.ApplyLook("alpha");
        Assert.Equal("Segoe UI", _store.LoadDesk().Font);
    });

    [Fact]
    public void A_look_that_is_gone_and_a_service_that_is_closing_say_so()
    {
        WithService(s => Assert.Equal("That look is gone.", s.ApplyLook("nope").Message));
        Sta.Run(() =>
        {
            var service = new DeskDesignService(_paths, _store) { LookCatalog = Catalog };
            service.Dispose();
            var result = service.ApplyLook("alpha");
            Assert.False(result.Ok);
            Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        });
    }

    [Fact]
    public void Between_two_of_the_users_own_designs_put_back_still_toggles() => WithService(s =>
    {
        Assert.True(s.SaveBeforeChange("a change"));
        string first = s.Designs().Single().Id;
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl" });
        Assert.True(s.SaveBeforeChange("another"));
        Assert.Equal(2, s.Designs().Count);

        Assert.True(s.SwitchTo(first).Ok);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Your Desk is back as it was.", s.PutBack().Message);
        Assert.Equal(Shape("clock:bl"), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Your Desk is back as it was.", s.PutBack().Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void Leaving_an_untouched_look_for_a_saved_design_saves_nothing_and_keeps_the_pointer() => WithService(s =>
    {
        Assert.True(s.SaveBeforeChange("a change"));   // the user's Desk, saved
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl" });
        s.ApplyLook("alpha");                          // saves "Before Alpha" (clock:bl) and points at it
        string usersFirst = s.Designs().Single(d => d.Name != "Before Alpha").Id;

        Assert.True(s.SwitchTo(usersFirst).Ok);        // leaving Alpha, untouched

        Assert.Equal(2, s.Designs().Count);
        s.PutBack();
        Assert.Equal(Shape("clock:bl"), Shape(_store.LoadDesk().Layout));   // still the Desk from before the look
    });

    [Fact]
    public void The_gallerys_data_marks_the_look_on_the_Desk_and_says_what_a_piece_needs() => WithService(s =>
    {
        _store.SaveLauncher(LauncherModel.Empty);
        var before = s.LooksSnapshot();
        Assert.Equal(["alpha", "beta", "gamma"], before.Cards.Select(c => c.Look.Id));
        Assert.DoesNotContain(before.Cards, c => c.Current);
        Assert.All(before.Cards, c => Assert.Equal(["The Launcher needs buttons."], c.Needs.Select(n => n.Line)));
        Assert.Equal(0.03, before.Wallpaper!.Brightness);

        s.ApplyLook("beta");
        Assert.Equal(["beta"], s.LooksSnapshot().Cards.Where(c => c.Current).Select(c => c.Look.Id));

        _wall = 0.98;
        var bright = s.LooksSnapshot().Cards.Single(c => c.Look.Id == "alpha");
        Assert.Equal([DeskLookFit.AutoInkNote], bright.Fit.Notes);

        // A Desk that is off shows no look as on it.
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        Assert.DoesNotContain(s.LooksSnapshot().Cards, c => c.Current);
    });

    [Fact]
    public void A_look_file_that_cannot_be_used_is_named_in_the_log() => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = DeskLookCatalog.Load([("alpha", LookJson("alpha", "Alpha", "tc", glow: true)), ("broken", "{ not json")]),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LogFile)!);
        service.LogSkippedLooks();
        Assert.Contains("desk look skipped: broken: is not JSON", File.ReadAllText(_paths.LogFile));
    });
}
