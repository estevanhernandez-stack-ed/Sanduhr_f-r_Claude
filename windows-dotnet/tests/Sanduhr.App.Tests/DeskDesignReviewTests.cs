using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The live review and saved designs, the app side (spec, the agent-designed Desk): a proposal is previewed from memory with
/// nothing written and answered <c>previewing</c> at once; Dismiss answers rejected, Apply saves the Desk it replaces and writes the
/// design, a newer proposal replaces a preview, a missing font keeps Apply waiting, and "Let Claude change the Desk directly" applies
/// without the bar. Over a scratch AppData and a fake host.
/// </summary>
public sealed class DeskDesignReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-review-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private DateTimeOffset _now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    public DeskDesignReviewTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root);
        _store = new SettingsStore(_paths);
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl", Font = "Segoe UI" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>What the app would do: records the calls and keeps the callbacks the service gave.</summary>
    private sealed class FakeHost : IDeskDesignHost
    {
        public List<string> Calls { get; } = [];
        public DeskDesignSettings? Previewed { get; private set; }
        public ThemeDefinition? Theme { get; private set; }
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply { get; private set; }
        public Action? Dismiss { get; private set; }
        public string? AppliedTheme { get; private set; }

        public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss)
        {
            Calls.Add("show");
            Previewed = settings;
            Theme = theme;
            Model = model;
            Apply = apply;
            Dismiss = dismiss;
        }

        public void EndPreview() => Calls.Add("end");

        public void Applied(string? themeKey)
        {
            Calls.Add("applied");
            AppliedTheme = themeKey;
        }
    }

    private static readonly string[] Installed = ["Segoe UI", "Georgia", "Cascadia Mono"];

    private void WithService(Action<DeskDesignService, FakeHost> body, bool host = true) => Sta.Run(() =>
    {
        var fake = new FakeHost();
        using var service = new DeskDesignService(_paths, _store)
        {
            Host = host ? fake : null,
            InstalledFonts = () => Installed,
            Clock = () => _now,
        };
        body(service, fake);
    });

    private static byte[] Request(string id, string designJson) => Encoding.UTF8.GetBytes(
        "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
        + "\",\"design\":" + designJson + "}");

    private void Send(DeskDesignService s, string id, string designJson)
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(s.Handoff.Paths.Request, Request(id, designJson));
        s.Handoff.Check();
    }

    private JsonObject Result() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!;

    private string ResultStatus => (string)Result()["result"]!["status"]!;

    /// <summary>Two layouts place the same pieces in the same corners and sizes (their order in the string does not matter).</summary>
    private static void SameLayout(string expected, string actual)
    {
        static string Key(string layout) => string.Join(" ", new DeskArrangement(layout).Pieces.OrderBy(p => p.Widget, StringComparer.Ordinal).Select(p => $"{p.Widget}:{DeskAnchors.Word(p.Anchor)}:{p.Scale:0.0}"));
        Assert.Equal(Key(expected), Key(actual));
    }

    private const string Dusk = """
        {"name":"Dusk","note":"Warm ink for the sunset wallpaper.","plate":true,"shadow":false,
         "layout":{"clock":{"anchor":"tr","size":1.4}}}
        """;

    // ---- previewing: nothing is written ----

    [Fact]
    public void AGoodDesignIsPreviewedAnsweredPreviewingAtOnceAndNothingIsWritten()
    {
        string before = File.ReadAllText(_paths.SettingsFile);
        WithService((s, host) =>
        {
            Send(s, "p1", Dusk);
            Assert.True(s.Reviewing);
            Assert.Equal("previewing", ResultStatus);
            Assert.Equal("p1", (string?)Result()["id"]);
            Assert.Contains(Result()["result"]!["changes"]!.AsArray().Select(n => (string)n!), c => c.Contains("Plate behind the text: on"));

            Assert.Equal(["show"], host.Calls);
            SameLayout("clock:tr:1.4 claude:bl", host.Previewed!.Desk.Layout);
            Assert.True(host.Previewed.Desk.Plate);
            Assert.False(host.Previewed.Ink.Shadow);
            Assert.Equal("Dusk", host.Model!.Title);
            Assert.Equal("Warm ink for the sunset wallpaper.", host.Model.Note);
            Assert.True(host.Model.CanApply);
            Assert.Empty(s.Designs());
        });
        Assert.Equal(before, File.ReadAllText(_paths.SettingsFile));   // the review wrote nothing
        SameLayout("clock:bl claude:bl", _store.LoadDesk().Layout);
        Assert.False(_store.LoadDeskPlate());
    }

    [Fact]
    public void DismissRestoresAtOnceAndAnswersRejectedDismissedByTheUser()
    {
        WithService((s, host) =>
        {
            Send(s, "d1", Dusk);
            host.Dismiss!();
            Assert.False(s.Reviewing);
            Assert.Equal(["show", "end"], host.Calls);
            var result = Result();
            Assert.Equal("d1", (string?)result["id"]);
            Assert.Equal("rejected", (string?)result["result"]!["status"]);
            Assert.Equal("dismissed", (string?)result["result"]!["reason"]);
            Assert.Contains("dismissed by the user", result["result"]!["reasons"]![0]!["message"]!.ToString());
            Assert.Empty(s.Designs());
            host.Dismiss!();   // a second Dismiss (Escape twice) does nothing
            Assert.Equal(["show", "end"], host.Calls);
        });
        SameLayout("clock:bl claude:bl", _store.LoadDesk().Layout);
    }

    [Fact]
    public void ApplySavesTheDeskItReplacesWritesTheDesignAndAnswersApplied()
    {
        WithService((s, host) =>
        {
            Send(s, "a1", Dusk);
            host.Apply!();

            Assert.False(s.Reviewing);
            Assert.Equal("applied", ResultStatus);
            Assert.Equal("a1", (string?)Result()["id"]);
            Assert.Contains("end", host.Calls);
            Assert.Contains("applied", host.Calls);

            // The settings carry the design.
            var desk = _store.LoadDesk();
            SameLayout("clock:tr:1.4 claude:bl", desk.Layout);
            Assert.Equal("Segoe UI", desk.Font);   // untouched
            Assert.True(_store.LoadDeskPlate());
            Assert.Equal(new DeskInkSettings(DeskInkMode.Auto, "ffffff", false), _store.LoadDeskInk());

            // The Desk it replaced is saved as "Before Dusk", the new look as "Dusk", and Put my Desk back points at the first.
            var designs = s.Designs();
            Assert.Equal(["Dusk", "Before Dusk"], designs.Select(d => d.Name).ToArray());
            var before = designs.Single(d => d.Name == "Before Dusk");
            Assert.Equal("bl", DeskAnchors.Word(before.Design.Layout!.Single(p => p.Piece == "clock").Anchor!.Value));
            Assert.Equal(before.Id, _store.LoadDeskPutBack());
            Assert.True(s.CanPutBack);
            Assert.False(before.Pinned);
        });
    }

    [Fact]
    public void ApplyWithAThemeSavesTheThemeFileAndSelectsIt()
    {
        var theme = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        theme["name"] = "Dusk Rose";
        theme["accent"] = "#ff7e5f";
        WithService((s, host) =>
        {
            Send(s, "t1", "{\"name\":\"Rose\",\"theme\":" + theme.ToJsonString() + "}");
            Assert.NotNull(host.Theme);
            Assert.Equal("Dusk Rose", host.Theme!.Name);
            Assert.Equal("obsidian", _store.LoadTheme());   // previewed from memory only

            host.Apply!();
            Assert.Equal("dusk-rose", _store.LoadTheme());
            Assert.Equal("dusk-rose", host.AppliedTheme);
            Assert.True(File.Exists(Path.Combine(_paths.ThemesDir, "dusk-rose.json")));
            // The Desk before it kept the theme it had.
            var before = s.Designs().Single(d => d.Name == "Before Rose");
            Assert.Equal("Obsidian", (string?)before.Design.Theme!["name"]);
        });
    }

    [Fact]
    public void ANewProposalWhilePreviewingReplacesThePreviewAndTheOldOneIsAnsweredReplaced()
    {
        WithService((s, host) =>
        {
            Send(s, "first", Dusk);
            Send(s, "second", """{"name":"Mono","font":"Cascadia Mono","shadow":false}""");

            Assert.True(s.Reviewing);
            Assert.Equal("second", (string?)Result()["id"]);
            Assert.Equal("previewing", ResultStatus);
            Assert.Equal("Cascadia Mono", host.Previewed!.Desk.Font);
            SameLayout("clock:bl claude:bl", host.Previewed.Desk.Layout);   // the first design is gone from the preview
            Assert.Equal(["show", "end", "show"], host.Calls);

            // Applying now writes the second design only.
            host.Apply!();
            Assert.Equal("Cascadia Mono", _store.LoadDesk().Font);
            Assert.False(_store.LoadDeskPlate());
        });
    }

    [Fact]
    public void TheReplacedProposalIsAnsweredRejectedReplaced()
    {
        // The result file holds the latest answer, so look at it between the two requests: the first is answered in step with the second.
        string? replaced = null;
        WithService((s, host) =>
        {
            Send(s, "first", Dusk);
            var handoff = s.Handoff;
            // A proposal arrives through the handler directly so the replaced answer can be read before previewing overwrites it.
            var verdict = DeskDesign.Validate(JsonNode.Parse("""{"name":"Next","plate":false}"""), DeskDesignContext.Unknown);
            handoff.Handler("second", verdict);
            replaced = File.ReadAllText(handoff.Paths.Result);
        });
        Assert.NotNull(replaced);
        var r = (JsonObject)JsonNode.Parse(replaced!)!;
        Assert.Equal("first", (string?)r["id"]);
        Assert.Equal("rejected", (string?)r["result"]!["status"]);
        Assert.Equal("replaced", (string?)r["result"]!["reason"]);
    }

    // ---- fonts ----

    [Fact]
    public void AMissingFontPreviewsInTheNearestFontAndApplyWaitsUntilItIsInstalled()
    {
        string[] fonts = [.. Installed];
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var s = new DeskDesignService(_paths, _store) { Host = host, InstalledFonts = () => fonts, Clock = () => _now };
            Send(s, "f1", """{"name":"Zed","font":{"family":"Zed Mono","link":"https://fonts.example/zed"},"plate":true}""");

            Assert.Equal("previewing", ResultStatus);
            Assert.Equal("Zed Mono", (string?)Result()["result"]!["fonts"]![0]!["family"]);
            Assert.Equal("Cascadia Mono", host.Previewed!.Desk.Font);   // the nearest installed look
            Assert.False(host.Model!.CanApply);
            Assert.Equal("https://fonts.example/zed", Assert.Single(host.Model.MissingFonts).Link);

            host.Apply!();                       // Apply is disabled: pressing it does nothing
            Assert.True(s.Reviewing);
            Assert.Equal("Segoe UI", _store.LoadDesk().Font);
            Assert.Empty(s.Designs());

            // The font is installed: the preview draws with it and Apply wakes up.
            fonts = [.. Installed, "Zed Mono"];
            s.FontsChanged();
            Assert.Equal("Zed Mono", host.Previewed.Desk.Font);
            Assert.True(host.Model.CanApply);

            host.Apply!();
            Assert.False(s.Reviewing);
            Assert.Equal("Zed Mono", _store.LoadDesk().Font);
            Assert.Equal("applied", ResultStatus);
        });
    }

    // ---- no host, and the direct opt-in ----

    [Fact]
    public void WithNoHostAGoodDesignWaitsAsNotReadyAndNothingIsWritten()
    {
        WithService((s, _) =>
        {
            Send(s, "n1", Dusk);
            Assert.Equal("rejected", ResultStatus);
            Assert.Equal("not_ready", (string?)Result()["result"]!["reason"]);
            Assert.False(s.Reviewing);
        }, host: false);
        SameLayout("clock:bl claude:bl", _store.LoadDesk().Layout);
    }

    [Fact]
    public void DirectApplyIsOffByDefaultAndTheSwitchSurvivesInSettings()
    {
        Assert.False(_store.LoadDeskDirect());
        _store.SaveDeskDirect(true);
        Assert.True(_store.LoadDeskDirect());
        Assert.True(new SettingsStore(_paths).LoadDeskDirect());
        _store.SaveDeskDirect(false);
        Assert.False(_store.LoadDeskDirect());
        Assert.DoesNotContain("desk_direct", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void WithTheDirectSwitchOnADesignIsAppliedWithoutTheBarAfterSavingThePreviousDesk()
    {
        _store.SaveDeskDirect(true);
        WithService((s, host) =>
        {
            Send(s, "x1", Dusk);

            Assert.Equal("applied", ResultStatus);
            Assert.Equal("x1", (string?)Result()["id"]);
            Assert.DoesNotContain("show", host.Calls);   // no bar
            Assert.False(s.Reviewing);
            SameLayout("clock:tr:1.4 claude:bl", _store.LoadDesk().Layout);
            Assert.True(_store.LoadDeskPlate());
            Assert.Equal(["Dusk", "Before Dusk"], s.Designs().Select(d => d.Name).ToArray());
            Assert.True(s.CanPutBack);
        });
    }

    [Fact]
    public void DirectApplyWorksWithNoHostAtAll()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "x2", """{"name":"Quiet","shadow":false,"plate":true}""");
            Assert.Equal("applied", ResultStatus);
            Assert.False(_store.LoadDeskInk().Shadow);
        }, host: false);
    }

    [Fact]
    public void DirectApplyStillWaitsForAMissingFontByShowingTheBar()
    {
        _store.SaveDeskDirect(true);
        WithService((s, host) =>
        {
            Send(s, "x3", """{"font":"Zed Mono","plate":true}""");
            Assert.Equal("previewing", ResultStatus);
            Assert.True(s.Reviewing);
            Assert.False(host.Model!.CanApply);
            Assert.False(_store.LoadDeskPlate());
        });
    }

    [Fact]
    public void ADirectApplyReportsWhatChangedAndAnyMessageTroubleAsWarnings()
    {
        _store.SaveDeskDirect(true);
        Directory.CreateDirectory(_paths.AppDataDir);
        File.WriteAllBytes(Path.Combine(_paths.AppDataDir, "messages.txt"), [0xFF, 0xFE, 0x00, 0x41]);   // not UTF-8
        WithService((s, _) =>
        {
            Send(s, "m1", """{"name":"Words","plate":true,"messages":{"lines":["Ship it today"],"mode":"add"}}""");
            Assert.Equal("applied", ResultStatus);
            var warnings = Result()["result"]!["warnings"]!.AsArray();
            Assert.Equal("messages", (string?)warnings[0]!["field"]);
            Assert.True(_store.LoadDeskPlate());
        }, host: false);
    }

    [Fact]
    public void ADesignsMessagesAreAddedToMessagesTxtWithTheOldFileKept()
    {
        _store.SaveDeskDirect(true);
        Directory.CreateDirectory(_paths.AppDataDir);
        string path = Path.Combine(_paths.AppDataDir, "messages.txt");
        File.WriteAllText(path, "# my notes\nHello world\n");
        WithService((s, _) =>
        {
            Send(s, "m2", """{"name":"Words","messages":{"lines":["Ship it today"],"mode":"add"}}""");
            Assert.Equal("applied", ResultStatus);
        }, host: false);
        Assert.Contains("Hello world", File.ReadAllText(path));
        Assert.Contains("Ship it today", File.ReadAllText(path));
        Assert.Equal("# my notes\nHello world\n", File.ReadAllText(path + ".previous"));
    }

    [Fact]
    public void ALauncherImageThatIsNotOnThisPcIsRefusedBeforeAnyPreview()
    {
        string image = new string('a', 64) + ".png";
        WithService((s, host) =>
        {
            Send(s, "i1", "{\"launcher\":{\"orientation\":\"row\",\"buttons\":[{\"kind\":\"link\",\"target\":\"https://example.com\",\"image\":\"" + image + "\"}]}}");
            Assert.Equal("rejected", ResultStatus);
            Assert.Equal("launcher", (string?)Result()["result"]!["reasons"]![0]!["field"]);
            Assert.Empty(host.Calls);
        });
    }

    // ---- saved designs and Put my Desk back ----

    [Fact]
    public void PutMyDeskBackRestoresTheDeskAndTogglesWithTheDesignItLeft()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            var original = (_store.LoadDesk(), _store.LoadDeskPlate(), _store.LoadDeskInk());
            Send(s, "u1", Dusk);
            Assert.NotEqual(original.Item1.Layout, _store.LoadDesk().Layout);

            _now = _now.AddMinutes(5);
            var back = s.PutBack();
            Assert.True(back.Ok, back.Message);
            SameLayout(original.Item1.Layout, _store.LoadDesk().Layout);
            Assert.Equal(original.Item2, _store.LoadDeskPlate());
            Assert.Equal(original.Item3, _store.LoadDeskInk());

            // Pressing it again goes forward to the look just left: nothing is lost.
            _now = _now.AddMinutes(5);
            Assert.True(s.PutBack().Ok);
            SameLayout("clock:tr:1.4 claude:bl", _store.LoadDesk().Layout);
            Assert.True(_store.LoadDeskPlate());
            // Two designs only: the Desk before, and Dusk. Switching never piles up copies of looks already kept.
            Assert.Equal(2, s.Designs().Count);
        }, host: false);
    }

    [Fact]
    public void PutMyDeskBackSaysSoWhenThereIsNothingToPutBack()
    {
        WithService((s, _) =>
        {
            Assert.False(s.CanPutBack);
            var r = s.PutBack();
            Assert.False(r.Ok);
            Assert.Contains("nothing to put back", r.Message);
        }, host: false);
    }

    [Fact]
    public void SwitchingToADesignSavesTheCurrentDeskFirstAndKeepsTheOrder()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "s1", Dusk);                                   // Dusk (+ Before Dusk)
            _now = _now.AddMinutes(10);
            _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:tl claude:tl" });   // the user changes the Desk by hand
            var dusk = s.Designs().Single(d => d.Name == "Dusk");

            _now = _now.AddMinutes(10);
            var r = s.SwitchTo(dusk.Id);
            Assert.True(r.Ok, r.Message);
            SameLayout("clock:tr:1.4 claude:bl", _store.LoadDesk().Layout);
            // The hand-made Desk was saved first, so it can be switched back to.
            var hand = s.Designs().Single(d => d.Name == "Before Dusk" && d.Design.Layout!.Single(p => p.Piece == "clock").Anchor == DeskAnchor.TL);
            Assert.Equal(hand.Id, _store.LoadDeskPutBack());
            Assert.Equal(dusk.Id, s.Designs()[0].Id);              // the one switched to is the newest

            Assert.True(s.PutBack().Ok);
            SameLayout("clock:tl claude:tl", _store.LoadDesk().Layout);
        }, host: false);
    }

    [Fact]
    public void SwitchingToTheDeskYouAlreadyHaveChangesNothing()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "s2", Dusk);
            var dusk = s.Designs().Single(d => d.Name == "Dusk");
            int count = s.Designs().Count;
            var r = s.SwitchTo(dusk.Id);
            Assert.True(r.Ok);
            Assert.Contains("already", r.Message);
            Assert.Equal(count, s.Designs().Count);
        }, host: false);
    }

    [Fact]
    public void ARenamePinAndDeleteChangeTheKeptDesigns()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "r1", Dusk);
            var before = s.Designs().Single(d => d.Name == "Before Dusk");

            Assert.True(s.Rename(before.Id, "  My old Desk \u0007").Ok);
            Assert.Equal("My old Desk", s.Designs().Single(d => d.Id == before.Id).Name);
            Assert.False(s.Rename(before.Id, "   ").Ok);

            Assert.True(s.Pin(before.Id, true).Ok);
            Assert.True(s.Designs().Single(d => d.Id == before.Id).Pinned);
            Assert.True(s.Pin(before.Id, false).Ok);
            Assert.False(s.Designs().Single(d => d.Id == before.Id).Pinned);

            Assert.Equal(before.Id, _store.LoadDeskPutBack());
            Assert.True(s.Delete(before.Id).Ok);
            Assert.DoesNotContain(s.Designs(), d => d.Id == before.Id);
            Assert.Null(_store.LoadDeskPutBack());                  // nothing to put back to any more
            Assert.False(s.CanPutBack);
            Assert.False(s.Delete(before.Id).Ok);
            Assert.False(s.SwitchTo("0123456789abcdef0123456789abcdef").Ok);
        }, host: false);
    }

    [Fact]
    public void TenDesignsAreKeptAndAPinnedOneIsNeverPruned()
    {
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "k0", """{"name":"Look 0","plate":true}""");
            var first = s.Designs().Single(d => d.Name == "Look 0");
            s.Pin(first.Id, true);
            for (int i = 1; i <= 12; i++)
            {
                _now = _now.AddMinutes(1);
                // Each look differs from every earlier one, so each Apply keeps two more designs.
                Send(s, "k" + i, "{\"name\":\"Look " + i + "\",\"layout\":{\"clock\":{\"anchor\":\"" + DeskAnchors.Word(DeskAnchors.All[i % 8]) + "\",\"size\":" + (0.6 + (i % 11) * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}}}");
            }
            var kept = s.Designs();
            Assert.Equal(10, kept.Count);
            Assert.Contains(kept, d => d.Id == first.Id && d.Pinned);
            Assert.Contains(kept, d => d.Name == "Look 12");       // the newest stays
            Assert.DoesNotContain(kept, d => d.Name == "Before Look 0");   // the oldest that is not pinned went
        }, host: false);
    }

    // ---- the state file carries the designs and the switch ----

    [Fact]
    public void TheStateListsTheSavedDesignsAndTheDirectSwitch()
    {
        _store.SaveMcpEnabled(true);
        _store.SaveDeskDirect(true);
        WithService((s, _) =>
        {
            Send(s, "st1", Dusk);
            s.Publish();
            var state = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.StateFile)))!;
            Assert.True((bool)state["direct"]!);
            var names = state["designs"]!.AsArray().Select(d => (string)d!["name"]!).ToList();
            Assert.Equal(["Dusk", "Before Dusk"], names);
            Assert.All(state["designs"]!.AsArray(), d => Assert.Equal(32, ((string)d!["id"]!).Length));
        }, host: false);
    }

    [Fact]
    public void AReviewInFlightIsDroppedWhenTheServiceCloses()
    {
        var host = new FakeHost();
        Sta.Run(() =>
        {
            using var s = new DeskDesignService(_paths, _store) { Host = host, InstalledFonts = () => Installed, Clock = () => _now };
            Send(s, "z1", Dusk);
        });
        Assert.Equal("end", host.Calls[^1]);
        SameLayout("clock:bl claude:bl", _store.LoadDesk().Layout);
    }

    // ---- 3.8 ride-alongs: the weather nickname (#315) and the fold (#314) go through the same review ----

    [Fact]
    public void ANicknameAndASlantArePreviewedFromMemoryAndWrittenOnlyOnApply()
    {
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);
        _store.SaveDeskWeather(new DeskWeatherSettings(place, WeatherUnit.Fahrenheit));
        WithService((s, host) =>
        {
            Send(s, "f1", """{"name":"Neon Column","layout":{"clock":{"tilt":-18}},"weather":{"nickname":"Funky Town"}}""");
            Assert.Equal("previewing", ResultStatus);
            var changes = Result()["result"]!["changes"]!.AsArray().Select(n => (string)n!).ToList();
            Assert.Contains("Weather: shown as “Funky Town”", changes);
            Assert.Contains(changes, c => c.EndsWith("slant 0° to -18°", StringComparison.Ordinal));
            Assert.Equal("Funky Town", host.Previewed!.WeatherNickname);
            Assert.Equal(-18, new DeskArrangement(host.Previewed.Desk.Layout).StackTilt(DeskAnchor.BL));
            Assert.Equal("", _store.LoadDeskWeather().Nickname);   // the review wrote nothing
            Assert.Equal(0, new DeskArrangement(_store.LoadDesk().Layout).StackTilt(DeskAnchor.BL));

            host.Apply!();
            Assert.Equal("applied", ResultStatus);
            var weather = _store.LoadDeskWeather();
            Assert.Equal("Funky Town", weather.Nickname);
            Assert.Equal(place, weather.Place);   // the forecast still asks for the picked city
            var layout = new DeskArrangement(_store.LoadDesk().Layout);
            Assert.Equal(-18, layout.Placement("clock")!.Tilt);
            Assert.Equal(-18, layout.Placement("claude")!.Tilt);   // one slant per stack
            Assert.Equal("Funky Town", (string?)s.CurrentDesign().ToJson()["weather"]!["nickname"]);
        });
    }
}
