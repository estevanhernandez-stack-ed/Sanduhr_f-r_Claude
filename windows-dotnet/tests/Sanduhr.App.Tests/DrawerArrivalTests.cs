using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Arrival places are saved once (drawers look and placement spec, section 3: an upgrader's drawers look and sit exactly as before, and
/// icons that move later do not push drawers). A drawer with no saved place arrives, is drawn there and is saved there after the render; a
/// pre-4.5 file (no <c>tidy</c> key) arrives in today's column, a 4.5 file through Tidy's slots off the icons. The Shell is never read.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerArrivalTests
{
    /// <summary>1920x1080 with a 48 px taskbar: a 1032 DIP work area, where three open drawers in a column overhang by 6.</summary>
    private static IReadOnlyList<DeskScreen> TaskbarScreen()
    {
        var b = new PxRect(0, 0, 1920, 1080);
        var w = new PxRect(0, 0, 1920, 1032);
        return [new DeskScreen("ONE", b, w, 1.0, true, TaskbarClearance.Insets(b, w, [], 1.0), @"\\?\DISPLAY#MAIN#0")];
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>The file as 4.0.1 wrote it: no <c>tidy</c> key.</summary>
    private static void MakeLegacy(Paths paths, DrawerService s)
    {
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(paths.SettingsFile))!;
        ((JsonObject)root["drawers"]!).Remove("tidy");
        File.WriteAllText(paths.SettingsFile, root.ToJsonString());
        s.Reload();
    }

    private static (DeskController Desk, DrawerService S, SettingsStore Store, DrawerFreePanelTests.TwoScreenHost Host) Start(
        DrawerSandbox box, bool legacy, params string[] names)
    {
        var (s, store, paths) = DrawerFreePanelTests.Make(box, null, names);
        store.SaveDesk(store.LoadDesk() with { Layout = "" });
        if (legacy)
        {
            MakeLegacy(paths, s);
            Assert.True(s.Settings.LegacyArrival);
        }
        var (desk, host) = DrawerFreePanelTests.Start(store, paths, s, TaskbarScreen());
        return (desk, s, store, host);
    }

    private static Dictionary<string, System.Windows.Point> Anchors(DrawerFreePanelTests.TwoScreenHost host) =>
        DrawerFreePanelTests.DrawerIslands(host).ToDictionary(d => DrawerFreePanelTests.IdOf(d.Island), d => d.Island.Anchor, StringComparer.Ordinal);

    [Fact]
    public void A_file_without_a_tidy_key_is_a_pre_4_5_file_and_a_saved_one_is_not() => Sta.Run(() =>
    {
        Assert.True(DrawerSettings.Decode(JsonNode.Parse("""{"enabled":true,"drawers":[]}""")).LegacyArrival);
        Assert.False(DrawerSettings.Decode(JsonNode.Parse("""{"enabled":true,"tidy":{"corner":"tr","direction":"column"}}""")).LegacyArrival);
        Assert.False(DrawerSettings.Default.LegacyArrival);
        Assert.DoesNotContain("legacy", DrawerSettings.Default.Encode().ToJsonString(), StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void An_upgraders_unplaced_drawers_are_saved_where_4_0_1_drew_them_and_the_third_still_hangs_at_708() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, host) = Start(box, legacy: true, "Images", "Documents", "Music");
        try
        {
            double x = 1920 - DrawerPlacement.Margin - DrawerPlacement.Width;
            var drawn = Anchors(host);
            Assert.Equal(new System.Windows.Point(x, 24), drawn["images"]);
            Assert.Equal(new System.Windows.Point(x, 366), drawn["documents"]);
            Assert.Equal(new System.Windows.Point(x, 708), drawn["music"]);
            Assert.Empty(store.LoadDrawers().Places);   // saved after the render, not during it

            Pump();
            var saved = store.LoadDrawers();
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, x, 24), saved.Places["images"]);
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, x, 366), saved.Places["documents"]);
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, x, 708), saved.Places["music"]);
            Assert.False(saved.LegacyArrival);   // the file now carries a tidy key
            Assert.Equal(drawn, Anchors(host));  // and nothing moved when the save redrew
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_later_icon_change_does_not_move_a_saved_arrival() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, host) = Start(box, legacy: true, "Images", "Documents", "Music");
        try
        {
            Pump();
            var before = Anchors(host);
            var places = store.LoadDrawers().Places;

            desk.IconAreaOverride = [new DrawerRect(1300, 0, 620, 1032)];   // icons now fill the drawers' column
            desk.Reload();
            Pump();
            Assert.Equal(before, Anchors(host));
            Assert.Equal(places, store.LoadDrawers().Places);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drawer_added_after_arrives_through_Tidys_slots_and_is_saved_once_placed() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, host) = Start(box, legacy: true, "Images", "Documents", "Music");
        try
        {
            Pump();
            var first = store.LoadDrawers().Places;
            s.Save(s.Settings with { Drawers = [.. s.Settings.Drawers, new DrawerDef("videos", "Videos", box.Dir(@"Desktop\Drawers\Videos"), true)] });
            Pump();

            var after = store.LoadDrawers().Places;
            Assert.Equal(4, after.Count);
            foreach (var (id, p) in first) Assert.Equal(p, after[id]);   // the three did not move
            var v = after["videos"];
            Assert.Equal(DeskMonitors.Main, v.Monitor);
            // Tidy's slots, not the column: the column at x=1536 is full (708 + 330 is past 1032), so it wraps beside it.
            Assert.True(v.X < 1536, $"x {v.X}");
            Assert.Equal(DrawerPlacement.Margin, v.Y);
            var rect = new DrawerRect(v.X, v.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
            foreach (var (id, p) in first)
                Assert.False(rect.Overlaps(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight)), id);
            Assert.Equal(new System.Windows.Point(v.X, v.Y), Anchors(host)["videos"]);

            // Saved once placed: moving the icons does not move it.
            desk.IconAreaOverride = [new DrawerRect(0, 0, 1900, 1032)];
            desk.Reload();
            Pump();
            Assert.Equal(v, store.LoadDrawers().Places["videos"]);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_4_5_file_arrives_through_Tidys_slots_off_the_icons_and_saves_what_it_drew() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = DrawerFreePanelTests.Make(box, null, "Images");
        store.SaveDesk(store.LoadDesk() with { Layout = "" });
        var host = new DrawerFreePanelTests.TwoScreenHost(TaskbarScreen());
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths), paths: paths, drawers: s);
        desk.SpacingReader = () => null;
        desk.IconAreaOverride = [new DrawerRect(1400, 0, 520, 400)];   // the top right is icons
        try
        {
            Assert.False(s.Settings.LegacyArrival);
            desk.Reload();
            var drawn = Anchors(host)["images"];
            Assert.False(new DrawerRect(drawn.X, drawn.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight).Overlaps(new DrawerRect(1400, 0, 520, 400)));
            Pump();
            Assert.Equal(new DrawerPlace(DeskMonitors.Main, drawn.X, drawn.Y), store.LoadDrawers().Places["images"]);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_drawer_the_user_placed_before_the_save_ran_keeps_its_place() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, _) = Start(box, legacy: true, "Images", "Documents");
        try
        {
            var mine = new DrawerPlace(DeskMonitors.Main, 100, 100);
            s.Save(s.Settings with { Places = new Dictionary<string, DrawerPlace> { ["images"] = mine } });   // before the queued save runs
            Pump();
            var after = store.LoadDrawers().Places;
            Assert.Equal(mine, after["images"]);
            Assert.True(after.ContainsKey("documents"));
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void Drawers_off_read_no_icons_at_all() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = DrawerFreePanelTests.Make(box, d => d with { Enabled = false, KeepClear = true }, "Images");
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl" });
        var host = new DrawerFreePanelTests.TwoScreenHost(TaskbarScreen());
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths), paths: paths, drawers: s);
        int reads = 0;
        desk.IconReader = () => { reads++; return []; };
        desk.SpacingReader = () => null;
        try
        {
            desk.Reload();
            Pump();
            Assert.Equal(0, reads);

            s.Save(s.Settings with { Enabled = true });   // on again: the icons are read for the arrival
            Assert.True(reads > 0);
        }
        finally { desk.Dispose(); s.Dispose(); }
    });

    [Fact]
    public void A_settings_file_that_cannot_be_written_gets_one_save_attempt_and_the_drawer_stays_where_it_arrived() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (desk, s, store, host) = Start(box, legacy: false, "Images");
        int saves = 0;
        s.Changed += () => saves++;
        // Open for reading with writers shut out: reads still work, SettingsStore.Write swallows the IOException, and Save still redraws.
        using var lockFile = new FileStream(new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local")).SettingsFile,
            FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var arrived = Anchors(host)["images"];
            for (int i = 0; i < 5; i++) Pump();
            Assert.Equal(1, saves);                        // not asked again, however many renders follow
            Assert.Empty(store.LoadDrawers().Places);      // and nothing was written

            desk.IconAreaOverride = [new DrawerRect(1300, 0, 620, 1032)];   // icons now fill the arrival corner
            desk.Reload();
            for (int i = 0; i < 5; i++) Pump();
            Assert.Equal(1, saves);
            Assert.Equal(arrived, Anchors(host)["images"]);   // it stays put for the session
        }
        finally { desk.Dispose(); s.Dispose(); }
    });
}
