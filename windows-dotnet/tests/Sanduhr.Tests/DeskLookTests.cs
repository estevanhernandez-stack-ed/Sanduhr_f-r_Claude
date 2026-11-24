using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>A ready-made look's file (5.0 spec 5, section 3): the wrapper, what its design must and must not carry, and the catalog
/// that skips a bad file and keeps the rest.</summary>
public class DeskLookTests
{
    /// <summary>A look that breaks no rule. The tests change one thing at a time.</summary>
    internal const string Good = """
        {
          "schema_version": 1,
          "id": "test-look",
          "name": "Test look",
          "blurb": "A look for the tests.",
          "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "tc", "size": 1.3 },
              "message": { "anchor": "tc", "size": 1 },
              "claude": { "anchor": "bl", "size": 1 },
              "meters": { "hidden": true },
              "nowPlaying": { "anchor": "bc", "size": 1 },
              "telemetry": { "hidden": true },
              "weather": { "hidden": true },
              "watchers": { "hidden": true },
              "launcher": { "anchor": "br", "size": 1 },
              "glow": { "hidden": true },
              "companion": { "hidden": true }
            },
            "font": "Bahnschrift",
            "ink": "#ffd08a",
            "plate": false,
            "shadow": false,
            "glow": 0.7,
            "follows_theme": false
          }
        }
        """;

    internal static JsonObject Doc(Action<JsonObject>? edit = null)
    {
        var o = (JsonObject)JsonNode.Parse(Good)!;
        edit?.Invoke(o);
        return o;
    }

    internal static DeskLook Look(Action<JsonObject>? edit = null)
    {
        var parsed = DeskLook.Parse(Doc(edit));
        Assert.True(parsed.Look is not null, string.Join("; ", parsed.Problems));
        return parsed.Look!;
    }

    private static string Problems(Action<JsonObject> edit) => string.Join("; ", DeskLook.Parse(Doc(edit)).Problems);

    private static JsonObject Design(JsonObject o) => (JsonObject)o["design"]!;

    [Fact]
    public void AGoodLookParses()
    {
        var look = Look();
        Assert.Equal("test-look", look.Id);
        Assert.Equal("Test look", look.Name);
        Assert.Equal("A look for the tests.", look.Blurb);
        Assert.Equal("Segoe UI", look.FontFallback);
        Assert.Equal("Bahnschrift", look.Design.Font!.Family);
        Assert.Equal(new DeskGlowChoice(true, 0.7), look.Design.Glow);
        Assert.True(look.Shows("clock"));
        Assert.False(look.Shows("meters"));
    }

    [Fact]
    public void TheLayoutKeepsTheFilesOrder()
    {
        // clock is written before message, so the clock stacks above it, whatever Settings order says.
        var shown = Look().Design.Layout!.Where(p => !p.IsHidden).Select(p => p.Piece).ToList();
        Assert.Equal(["clock", "message", "claude", "nowPlaying", "launcher"], shown);
    }

    [Fact]
    public void AnUnknownWrapperFieldIsIgnored_SoAWallpaperCanComeLater() =>
        Assert.NotNull(DeskLook.Parse(Doc(o => o["wallpaper"] = "ice.jpg")).Look);

    [Fact]
    public void AnUnknownDesignFieldIsRefused() =>
        Assert.Contains("unknown field", Problems(o => Design(o)["wallpaper"] = "ice.jpg"));

    [Theory]
    [InlineData("Marquee")]
    [InlineData("quiet_corner")]
    [InlineData("-ice")]
    [InlineData("ice-")]
    [InlineData("ice2")]
    [InlineData("")]
    public void AnIdIsLowercaseLettersAndHyphens(string id) => Assert.Contains("id", Problems(o => o["id"] = id));

    [Fact]
    public void TheWrapperIsChecked()
    {
        Assert.Contains("schema_version", Problems(o => o["schema_version"] = 2));
        Assert.Contains("name", Problems(o => o["name"] = ""));
        Assert.Contains("name", Problems(o => o["name"] = new string('x', DeskLook.NameMax + 1)));
        Assert.Contains("blurb", Problems(o => o.Remove("blurb")));
        Assert.Contains("blurb", Problems(o => o["blurb"] = new string('x', DeskLook.BlurbMax + 1)));
        Assert.Contains("font_fallback", Problems(o => o["font_fallback"] = "Segoe UI Variable Text"));   // Windows 11 only
        Assert.Contains("font_fallback", Problems(o => o.Remove("font_fallback")));
        Assert.Contains("must be an object", string.Join("; ", DeskLook.Parse(JsonValue.Create(3)).Problems));
    }

    [Fact]
    public void ABundledFontIsAFineFallbackAndAFineFont()
    {
        Assert.NotNull(DeskLook.Parse(Doc(o => o["font_fallback"] = "EsteFont Pro")).Look);
        Assert.NotNull(DeskLook.Parse(Doc(o => Design(o)["font"] = "EsteFont 26")).Look);
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("font")]
    [InlineData("ink")]
    [InlineData("plate")]
    [InlineData("shadow")]
    [InlineData("glow")]
    [InlineData("follows_theme")]
    public void ALookMustCarry(string field) => Assert.Contains("design." + field, Problems(o => Design(o).Remove(field)));

    [Fact]
    public void ALookMustSwitchFollowingOff() =>
        Assert.Contains("follows_theme", Problems(o => Design(o)["follows_theme"] = true));

    [Theory]
    [InlineData("launcher", """{"orientation":"row","buttons":[]}""")]
    [InlineData("messages", """{"lines":["hello"],"mode":"add"}""")]
    [InlineData("weather", """{"nickname":"Home"}""")]
    [InlineData("name", "\"Mine\"")]
    [InlineData("note", "\"A note.\"")]
    public void ALookMustNotCarryTheUsersContent(string field, string json) =>
        Assert.Contains($"design.{field} is the user's", Problems(o => Design(o)[field] = JsonNode.Parse(json)));

    [Fact]
    public void EveryPieceWindowsDrawsIsNamed()
    {
        Assert.Contains("must name weather", Problems(o => ((JsonObject)Design(o)["layout"]!).Remove("weather")));
        foreach (var piece in SetupPrompt.Pieces) Assert.Contains(Look().Design.Layout!, p => p.Piece == piece);
    }

    [Fact]
    public void ALookNamesNoPieceWindowsDoesNotDrawAndNoMonitor()
    {
        Assert.Contains("layout.meetings", Problems(o => ((JsonObject)Design(o)["layout"]!)["meetings"] = JsonNode.Parse("""{"hidden":true}""")));
        Assert.Contains("layout.clock must not name a monitor", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"anchor":"tc","size":1.3,"monitor":"main"}""")));
    }

    [Fact]
    public void AShownPieceGivesItsAnchorAndItsSize()
    {
        Assert.Contains("layout.clock must give its anchor and size", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"anchor":"tc"}""")));
        Assert.Contains("layout.clock must give its anchor and size", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"hidden":false,"size":1}""")));
    }

    [Fact]
    public void ALookShowsSomething()
    {
        var doc = Doc(o =>
        {
            var layout = (JsonObject)Design(o)["layout"]!;
            foreach (var key in layout.Select(kv => kv.Key).ToList()) layout[key] = JsonNode.Parse("""{"hidden":true}""");
        });
        Assert.Contains("shows nothing", string.Join("; ", DeskLook.Parse(doc).Problems));
    }

    [Fact]
    public void ALooksFontShipsWithEverySupportedWindowsAndBringsNoLink()
    {
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = "Segoe UI Variable Display"));
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = JsonNode.Parse("""{"family":"Georgia","link":"https://example.com/font"}""")));
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = ""));
        Assert.All(DeskLook.WindowsFonts, f => Assert.True(DeskLook.ShipsWithWindows(f.ToUpperInvariant())));
    }

    [Fact]
    public void TheDesignsOwnRulesStillApply()
    {
        Assert.Contains("design.glow", Problems(o => Design(o)["shadow"] = true));   // glow on with shadow true
        Assert.Contains("design.ink", Problems(o => Design(o)["ink"] = "amber"));
    }

    // ---- the catalog ----

    private static string Named(string id) => Doc(o => o["id"] = id).ToJsonString();

    [Fact]
    public void TheCatalogListsTheLooksInTheGallerysOrder()
    {
        var catalog = DeskLookCatalog.Load([("terminal", Named("terminal")), ("zebra", Named("zebra")), ("ice", Named("ice")), ("quiet-corner", Named("quiet-corner"))]);
        Assert.Equal(["quiet-corner", "ice", "terminal", "zebra"], catalog.Looks.Select(l => l.Id));
        Assert.Empty(catalog.Skipped);
        Assert.Equal("ice", catalog.Find("ice")!.Id);
        Assert.Null(catalog.Find("nope"));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void ABadFileIsSkippedAndTheOthersStay()
    {
        var catalog = DeskLookCatalog.Load([
            ("ice", Named("ice")),
            ("broken", "{ not json"),
            ("marquee", Doc(o => { o["id"] = "marquee"; Design(o)["follows_theme"] = true; }).ToJsonString()),
            ("terminal", Named("editorial")),   // the id is not the file's name
        ]);
        Assert.Equal(["ice"], catalog.Looks.Select(l => l.Id));
        Assert.Equal(3, catalog.Skipped.Count);
        Assert.Contains(catalog.Skipped, s => s.StartsWith("broken: "));
        Assert.Contains(catalog.Skipped, s => s.StartsWith("marquee: ") && s.Contains("follows_theme"));
        Assert.Contains(catalog.Skipped, s => s.StartsWith("terminal: ") && s.Contains("editorial"));
    }

    [Fact]
    public void TheGalleryHasSixNames() =>
        Assert.Equal(["quiet-corner", "ice", "marquee", "terminal", "editorial", "dashboard"], DeskLookCatalog.Order);
}
