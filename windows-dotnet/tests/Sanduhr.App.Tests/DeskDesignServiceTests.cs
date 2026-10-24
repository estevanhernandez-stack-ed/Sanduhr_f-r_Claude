using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The app side of the agent-designed Desk, phase 1 (spec, the agent-designed Desk): desk-state.json for get_desk, written from
/// the settings and the active theme only while the MCP integration is installed, and the request handoff whose answer is
/// still <c>not_ready</c>. Over a scratch AppData; the wallpaper and the installed fonts are this PC's own, so the tests
/// assert their shape, not their contents.
/// </summary>
public sealed class DeskDesignServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-design-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskDesignServiceTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string StatePath => Path.Combine(_paths.ExternalDir, DeskDesign.StateFile);

    private JsonObject ReadState() => (JsonObject)JsonNode.Parse(File.ReadAllText(StatePath))!;

    private void WithService(Action<DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store);
        body(service);
    });

    // ---- the gate: only while the MCP integration is installed ----

    [Fact]
    public void NoStateIsWrittenWhileTheMcpIntegrationIsOff()
    {
        WithService(s =>
        {
            s.Publish();
            Assert.False(File.Exists(StatePath));
        });
    }

    [Fact]
    public void TurningTheIntegrationOffRemovesTheState()
    {
        _store.SaveMcpEnabled(true);
        WithService(s =>
        {
            s.Publish();
            Assert.True(File.Exists(StatePath));
            _store.SaveMcpEnabled(false);
            s.Publish();
            Assert.False(File.Exists(StatePath));
        });
    }

    // ---- the document ----

    [Fact]
    public void TheStateHasTheShapeGetDeskPromises()
    {
        _store.SaveMcpEnabled(true);
        WithService(s => s.Publish());
        var state = ReadState();

        Assert.Equal(1, (int)state["schema_version"]!);
        Assert.Matches(@"^\d+\.\d+\.\d+$|^unknown$", (string?)state["writer_version"]);
        Assert.Equal(DeskArrangement.Known, state["pieces"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(8, state["anchors"]!.AsArray().Count);
        Assert.Contains("EsteFont Pro", state["fonts"]!["bundled"]!.AsArray().Select(n => (string)n!));
        Assert.Contains("Segoe UI", state["fonts"]!["installed"]!.AsArray().Select(n => (string)n!));
        Assert.NotNull(state["wallpaper"]);
        Assert.Empty(state["designs"]!.AsArray());   // saved designs arrive with phase 2
        Assert.False((bool)state["direct"]!);
        Assert.Null(state["start_menu_apps"]);       // no Start-menu read has arrived in this process yet
        // Whatever the wallpaper is, its summary (when there is one) is well formed.
        if (state["wallpaper"]!["summary"] is JsonObject summary)
        {
            Assert.NotNull(WallpaperOverview.FromJson(summary));
            Assert.InRange((double)summary["brightness"]!, 0, 1);
        }
    }

    [Fact]
    public void TheCurrentDesignIsBuiltFromTheSettingsAndTheTheme()
    {
        _store.SaveMcpEnabled(true);
        _store.SaveDesk(DeskSettings.Default with { Layout = "clock:tr:1.4 claude:bl", Font = "Segoe UI" });
        _store.SaveDeskPlate(true);
        _store.SaveDeskInk(new DeskInkSettings(DeskInkMode.Custom, "ffd08a,ff7e5f", Shadow: false));
        _store.SaveLauncher(new LauncherModel(LauncherOrientation.Column, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]));
        WithService(s => s.Publish());

        var design = (JsonObject)ReadState()["design"]!;
        Assert.Equal("tr", (string?)design["layout"]!["clock"]!["anchor"]);
        Assert.Equal(1.4, (double)design["layout"]!["clock"]!["size"]!);
        Assert.Equal("bl", (string?)design["layout"]!["claude"]!["anchor"]);
        Assert.True((bool)design["layout"]!["meters"]!["hidden"]!);
        Assert.Equal("Segoe UI", (string?)design["font"]);
        Assert.Equal(["#ffd08a", "#ff7e5f"], design["ink"]!.AsArray().Select(n => (string)n!));
        Assert.True((bool)design["plate"]!);
        Assert.False((bool)design["shadow"]!);
        Assert.Equal("column", (string?)design["launcher"]!["orientation"]);
        Assert.Equal("https://example.com", (string?)design["launcher"]!["buttons"]![0]!["target"]);
        Assert.Equal("Home", (string?)design["launcher"]!["buttons"]![0]!["label"]);
        Assert.Equal("Obsidian", (string?)design["theme"]!["name"]);   // the default theme, from the built-ins
        // The design the widget reports is itself a design the contract accepts.
        Assert.Empty(DeskDesign.Parse(design).Reasons);
    }

    [Fact]
    public void ASavedUserThemeIsReportedByItsPalette()
    {
        _store.SaveMcpEnabled(true);
        Directory.CreateDirectory(_paths.ThemesDir);
        var dusk = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        dusk["name"] = "Dusk";
        dusk["accent"] = "#ff7e5f";
        File.WriteAllText(Path.Combine(_paths.ThemesDir, "dusk.json"), dusk.ToJsonString());
        _store.SaveTheme("dusk");
        WithService(s => s.Publish());

        var theme = (JsonObject)ReadState()["design"]!["theme"]!;
        Assert.Equal("Dusk", (string?)theme["name"]);
        Assert.Equal("#ff7e5f", (string?)theme["accent"]);
    }

    [Fact]
    public void AnInstalledThemePacksThemeIsReportedByItsPalette()
    {
        _store.SaveMcpEnabled(true);
        InstalledPackKit.Install(_paths, "good-theme");
        _store.SaveTheme("good-theme:fixture-dark");
        WithService(s => s.Publish());

        var theme = (JsonObject)ReadState()["design"]!["theme"]!;
        Assert.Equal("Fixture Dark", (string?)theme["name"]);
        Assert.Equal("#ff2a6d", (string?)theme["accent"]);
    }

    [Fact]
    public void EveryBuiltInThemeAppearsAsAThemeTheContractAccepts()
    {
        _store.SaveMcpEnabled(true);
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            _store.SaveTheme(key);
            WithService(s => s.Publish());
            var design = (JsonObject)ReadState()["design"]!;
            var parsed = DeskDesign.Parse(design);
            Assert.True(parsed.Design is not null, $"{key}: {string.Join("; ", parsed.Reasons)}");
            Assert.Empty(parsed.Warnings);
        }
    }

    // ---- when it is rewritten ----

    [Fact]
    public void ThePublishedStateIsRewrittenOnlyWhenItChanged()
    {
        _store.SaveMcpEnabled(true);
        WithService(s =>
        {
            s.Publish();
            var first = File.GetLastWriteTimeUtc(StatePath);
            Thread.Sleep(40);
            s.Publish();
            Assert.Equal(first, File.GetLastWriteTimeUtc(StatePath));

            _store.SaveDeskPlate(true);
            s.Publish();
            Assert.True((bool)ReadState()["design"]!["plate"]!);
        });
        Assert.Empty(Directory.GetFiles(_paths.ExternalDir, "*.tmp"));
    }

    [Fact]
    public void EverySettingsWriteIsAnnounced()
    {
        int seen = 0;
        void Count() => Interlocked.Increment(ref seen);
        SettingsStore.AnyWritten += Count;
        try
        {
            _store.SaveDeskPlate(true);
            _store.SaveMcpEnabled(true);
        }
        finally { SettingsStore.AnyWritten -= Count; }
        Assert.True(seen >= 2);
    }

    [Fact]
    public void ASubscriberThatThrowsNeverFailsASave()
    {
        void Boom() => throw new InvalidOperationException("subscriber");
        SettingsStore.AnyWritten += Boom;
        try { _store.SaveDeskPlate(true); }
        finally { SettingsStore.AnyWritten -= Boom; }
        Assert.True(_store.LoadDeskPlate());
    }

    // ---- the request handoff (phase 1: every good design is answered not_ready) ----

    private static byte[] Request(string id, string designJson) => Encoding.UTF8.GetBytes(
        "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
        + "\",\"design\":" + designJson + "}");

    private JsonObject ReadResult() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!;

    [Fact]
    public void AGoodDesignIsAnsweredRejectedNotReady()
    {
        WithService(s =>
        {
            Directory.CreateDirectory(_paths.ExternalDir);
            File.WriteAllBytes(s.Handoff.Paths.Request, Request("g1", """{"plate":true,"ink":"light"}"""));
            s.Handoff.Check();
        });
        var result = ReadResult();
        Assert.Equal("g1", (string?)result["id"]);
        Assert.Equal("rejected", (string?)result["result"]!["status"]);
        Assert.Equal("not_ready", (string?)result["result"]!["reason"]);
        Assert.False(File.Exists(Path.Combine(_paths.ExternalDir, DeskDesign.RequestFile)));
    }

    [Fact]
    public void ABadDesignIsAnsweredWithItsReasonsNotWithNotReady()
    {
        WithService(s =>
        {
            Directory.CreateDirectory(_paths.ExternalDir);
            File.WriteAllBytes(s.Handoff.Paths.Request, Request("b1", """{"launcher":{"buttons":[{"kind":"app","target":"cmd.exe /c calc"}]}}"""));
            s.Handoff.Check();
        });
        var result = ReadResult()["result"]!;
        Assert.Equal("rejected", (string?)result["status"]);
        Assert.Null(result["reason"]);
        Assert.Equal("launcher.buttons[0].target", (string?)result["reasons"]![0]!["field"]);
    }

    [Fact]
    public void TheWidgetChecksTheMachineRulesWithWhatItKnowsOfThisPc()
    {
        // A font this PC does not have is valid but needs the font; the stub still answers not_ready, never applied.
        WithService(s =>
        {
            Directory.CreateDirectory(_paths.ExternalDir);
            File.WriteAllBytes(s.Handoff.Paths.Request, Request("f1", """{"font":"Definitely Not A Real Font 7"}"""));
            s.Handoff.Check();
        });
        Assert.Equal("not_ready", (string?)ReadResult()["result"]!["reason"]);
    }

    [Fact]
    public void StartingTheServiceTakesAWaitingRequest()
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(Path.Combine(_paths.ExternalDir, DeskDesign.RequestFile), Request("s1", "{}"));
        Sta.Run(() =>
        {
            using var service = new DeskDesignService(_paths, _store);
            service.Start();
            // Start posts the check to the dispatcher; run it.
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        });
        Assert.Equal("s1", (string?)ReadResult()["id"]);
    }
}
