using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>propose_widget's shared rules (spec 3.8 section 5): the pack built from the tool's arguments, the refusals, the
/// request file and the widget's own check of it, and the handoff's result file. Temp folders only.</summary>
public sealed class WidgetProposalTests : IDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wprop-" + Guid.NewGuid().ToString("N")[..8]);

    public WidgetProposalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string pack, string file) =>
        File.ReadAllText(Path.Combine(WidgetTestKit.Fixtures, "widget-packs", "good", pack, file));

    /// <summary>A sprite proposal as an agent would send it: the fixture's pixel sprite (8x8, four frames).</summary>
    internal static JsonObject SpriteArgs(string id = "neon-blob", string name = "Neon blob")
    {
        var sprite = (JsonObject)JsonNode.Parse(Fixture("good-sprite-pixel", "sprite.json"))!;
        var sheet = JsonNode.Parse(Fixture("good-sprite-pixel", "sheet.json"))!;
        return new JsonObject
        {
            ["kind"] = "sprite",
            ["pack"] = new JsonObject { ["id"] = id, ["name"] = name, ["description"] = "A small neon blob that walks the taskbar." },
            ["sprite"] = sprite,
            ["sheets"] = new JsonObject { ["sheet.json"] = sheet },
            ["note"] = "A tiny friend in your Desk's colors.",
        };
    }

    internal static JsonObject PaletteArgs() => new()
    {
        ["kind"] = "palette",
        ["pack"] = new JsonObject { ["id"] = "dusk", ["name"] = "Dusk", ["description"] = "Two evening gradients." },
        ["palettes"] = JsonNode.Parse("""[{"name":"dusk","colors":["#ff2a6d","#d16ba5","#05d9e8"],"mood":"late drive"},{"name":"ember","colors":["#ff8a3d","#c2410c"]}]"""),
    };

    internal static JsonObject EffectsArgs() => new()
    {
        ["kind"] = "effects",
        ["pack"] = new JsonObject { ["id"] = "calm", ["name"] = "Calm", ["description"] = "A slow breath." },
        ["effects"] = JsonNode.Parse("""{"effects":[{"name":"breathe","type":"pulse","intensity":0.35,"period_ms":4000,"color":"ink"}]}"""),
    };

    private static IEnumerable<string> Messages(WidgetBuild b) => b.Reasons.Select(r => r.ToString());

    [Fact]
    public void ASpriteIsBuiltAsAWholePackWithItsIdNamespacedAndPassesTheStrictValidator()
    {
        var build = WidgetProposal.Build(SpriteArgs(), Now);
        Assert.True(build.Ok, string.Join("\n", Messages(build)));
        var pack = build.Pack!;
        Assert.Equal("my-neon-blob", pack.Id);
        Assert.Equal("sprite", pack.Kind);
        Assert.Equal(["LICENSE", "pack.json", "sheet.json", "sprite.json"], pack.Files.Keys.Order(StringComparer.Ordinal));
        var manifest = JsonNode.Parse(pack.Files["pack.json"])!;
        Assert.Equal("my-neon-blob", (string?)manifest["id"]);
        Assert.Equal("CC0-1.0", (string?)manifest["license"]);
        Assert.Equal("3.8.0", (string?)manifest["sanduhr_min"]);
        Assert.Equal("Made here", (string?)manifest["author"]);
        Assert.Equal(["sheet.json", "sprite.json"], manifest["files"]!.AsArray().Select(n => (string?)n));
        Assert.Contains("CC0 1.0 Universal", Encoding.UTF8.GetString(pack.Files["LICENSE"]));
        Assert.True(PackValidator.Validate(pack.Files, "my-neon-blob", strict: true).Ok);
    }

    [Fact]
    public void AnIdAlreadyNamespacedIsNotPrefixedTwice()
    {
        Assert.Equal("my-blob", WidgetProposal.NamespacedId("my-blob"));
        Assert.Equal("my-blob", WidgetProposal.NamespacedId("blob"));
        Assert.Equal("my-neon", WidgetProposal.Build(SpriteArgs("my-neon"), Now).Pack!.Id);
    }

    [Theory]
    [InlineData("CC0-1.0")]
    [InlineData("CC-BY-4.0")]
    [InlineData("MIT")]
    [InlineData("Apache-2.0")]
    [InlineData("BSD-3-Clause")]
    [InlineData("ISC")]
    public void EveryLicenseOnOfferWritesALicenseTheValidatorAccepts(string license)
    {
        var args = PaletteArgs();
        args["pack"]!["license"] = license;
        var build = WidgetProposal.Build(args, Now);
        Assert.True(build.Ok, string.Join("\n", Messages(build)));
        Assert.Equal(license, build.Pack!.Manifest.License);
    }

    [Fact]
    public void PalettesAndEffectsBuildFromTheListAloneOrTheWholeFile()
    {
        var palette = WidgetProposal.Build(PaletteArgs(), Now);
        Assert.True(palette.Ok, string.Join("\n", Messages(palette)));
        Assert.Equal(["LICENSE", "pack.json", "palettes.json"], palette.Pack!.Files.Keys.Order(StringComparer.Ordinal));
        var effects = WidgetProposal.Build(EffectsArgs(), Now);
        Assert.True(effects.Ok, string.Join("\n", Messages(effects)));
        Assert.Equal("my-calm", effects.Pack!.Id);
    }

    [Theory]
    [InlineData("theme", "propose_theme")]
    [InlineData("font", "fonts are binaries")]
    [InlineData("music", "must be one of sprite, palette, effects")]
    public void ThemesFontsAndUnknownKindsAreRefused(string kind, string expected)
    {
        var args = PaletteArgs();
        args["kind"] = kind;
        var build = WidgetProposal.Build(args, Now);
        Assert.False(build.Ok);
        Assert.Contains(build.Reasons, r => r.Field == "kind" && r.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void APngSheetIsRefusedWhereverItIsNamed()
    {
        var args = SpriteArgs();
        args["sprite"]!["sheet"] = "sprite.png";
        var build = WidgetProposal.Build(args, Now);
        Assert.Contains(build.Reasons, r => r.Field == "sprite" && r.Message.Contains("never supplies an image", StringComparison.Ordinal));

        var keyed = SpriteArgs();
        keyed["sheets"] = new JsonObject { ["sheet.png"] = new JsonObject() };
        Assert.Contains(WidgetProposal.Build(keyed, Now).Reasons, r => r.Field == "sheets" && r.Message.Contains("no PNG", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesOrBinariesCannotBeSmuggledInAsArguments()
    {
        var args = PaletteArgs();
        args["files"] = new JsonObject { ["art.png"] = "iVBORw0KGgo=" };
        args["font"] = "x.ttf";
        var build = WidgetProposal.Build(args, Now);
        Assert.False(build.Ok);
        Assert.Contains(build.Reasons, r => r.Field == "arguments" && r.Message.Contains("files", StringComparison.Ordinal));

        var pack = PaletteArgs();
        pack["pack"]!["files"] = new JsonArray("palettes.json", "evil.png");
        Assert.Contains(WidgetProposal.Build(pack, Now).Reasons, r => r.Field == "pack" && r.Message.Contains("files", StringComparison.Ordinal));
    }

    [Fact]
    public void AnotherKindsFilesDoNotBelong()
    {
        var args = PaletteArgs();
        args["effects"] = new JsonArray();
        Assert.Contains(WidgetProposal.Build(args, Now).Reasons, r => r.Field == "effects");
    }

    [Theory]
    [InlineData("name", "Claude's buddy")]
    [InlineData("description", "An Anthropic mascot")]
    [InlineData("author", "Opus")]
    [InlineData("id", "haiku-cat")]
    public void TheTrademarkRuleRefusesThePack(string field, string value)
    {
        var args = SpriteArgs();
        args["pack"]![field] = value;
        var build = WidgetProposal.Build(args, Now);
        Assert.False(build.Ok);
        Assert.Contains(build.Reasons, r => r.Message.Contains("trademark", StringComparison.Ordinal));
    }

    [Fact]
    public void ATrademarkInsideTheArtsJsonIsRefusedToo()
    {
        var args = SpriteArgs();
        args["sprite"]!["animations"]!["claude_wave"] = new JsonArray(0);
        Assert.Contains(WidgetProposal.Build(args, Now).Reasons, r => r.Message.Contains("trademark", StringComparison.Ordinal));
    }

    [Fact]
    public void APixelSheetThatBreaksTheGrammarIsRefusedWithTheValidatorsSentence()
    {
        var white = SpriteArgs();
        white["sheets"]!["sheet.json"]!["palette"]![0] = "#FFFFFF";
        Assert.Contains(WidgetProposal.Build(white, Now).Reasons, r => r.Message.Contains("pure white", StringComparison.Ordinal));

        var shortRow = SpriteArgs();
        shortRow["sheets"]!["sheet.json"]!["frames"]![0]![0] = "..00";
        Assert.Contains(WidgetProposal.Build(shortRow, Now).Reasons, r => r.Message.Contains("expected 8 (frame_w)", StringComparison.Ordinal));

        var badIndex = SpriteArgs();
        badIndex["sheets"]!["sheet.json"]!["frames"]![0]![0] = "..9999..";
        Assert.Contains(WidgetProposal.Build(badIndex, Now).Reasons, r => r.Message.Contains("palette has only 3 colors", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownLicenseAndAMissingPackAreRefused()
    {
        var args = PaletteArgs();
        args["pack"]!["license"] = "GPL-3.0";
        Assert.Contains(WidgetProposal.Build(args, Now).Reasons, r => r.Field == "pack.license");
        Assert.Contains(WidgetProposal.Build(new JsonObject { ["kind"] = "palette" }, Now).Reasons, r => r.Field == "pack");
        Assert.False(WidgetProposal.Build(null, Now).Ok);
    }

    // ---- the request file and the widget's own check ----

    [Fact]
    public void TheRequestRoundTripsAndTheWidgetChecksThePackAgain()
    {
        var pack = WidgetProposal.Build(SpriteArgs(), Now).Pack!;
        var json = WidgetProposal.RequestJson("r1", pack, "a note", Now).ToJsonString();
        var request = WidgetProposal.Decode(Encoding.UTF8.GetBytes(json), Now.AddMinutes(1));
        Assert.Equal(WidgetRequestKind.Proposal, request.Kind);
        Assert.Equal("r1", request.Id);
        Assert.Equal("a note", request.Note);
        Assert.Equal(pack.Id, request.Pack!.Id);
        foreach (var (rel, bytes) in pack.Files) Assert.Equal(bytes, request.Pack.Files[rel]);
    }

    [Fact]
    public void TheWidgetRefusesARequestTheServerShouldNeverHaveWritten()
    {
        var pack = WidgetProposal.Build(SpriteArgs(), Now).Pack!;
        // A PNG slipped into the files.
        var withPng = WidgetProposal.RequestJson("r2", pack, null, Now);
        withPng["files"]!["preview.png"] = "\u0089PNG";
        var a = WidgetProposal.Decode(Encoding.UTF8.GetBytes(withPng.ToJsonString()), Now);
        Assert.Equal(WidgetRequestKind.Refused, a.Kind);
        Assert.Contains(a.Reasons, r => r.Message.Contains("JSON only", StringComparison.Ordinal));

        // A trademark edited in after the server's check.
        var edited = WidgetProposal.RequestJson("r3", pack, null, Now);
        edited["files"]!["pack.json"] = ((string)edited["files"]!["pack.json"]!).Replace("Neon blob", "Claude blob");
        var b = WidgetProposal.Decode(Encoding.UTF8.GetBytes(edited.ToJsonString()), Now);
        Assert.Equal(WidgetRequestKind.Refused, b.Kind);
        Assert.Contains(b.Reasons, r => r.Message.Contains("trademark", StringComparison.Ordinal));

        // An id outside the my- namespace (a catalog pack's).
        var hijack = WidgetProposal.RequestJson("r4", pack, null, Now);
        hijack["pack_id"] = "hourglass";
        hijack["files"]!["pack.json"] = ((string)hijack["files"]!["pack.json"]!).Replace("my-neon-blob", "hourglass");
        var c = WidgetProposal.Decode(Encoding.UTF8.GetBytes(hijack.ToJsonString()), Now);
        Assert.Equal(WidgetRequestKind.Refused, c.Kind);
        Assert.Contains(c.Reasons, r => r.Field == "pack.id");

        // A file that is not text.
        var binary = WidgetProposal.RequestJson("r5", pack, null, Now);
        binary["files"]!["sheet.json"] = new JsonArray(1, 2, 3);
        Assert.Equal(WidgetRequestKind.Refused, WidgetProposal.Decode(Encoding.UTF8.GetBytes(binary.ToJsonString()), Now).Kind);
    }

    [Fact]
    public void StaleUnreadableOrIdlessRequestsAreDroppedUnanswered()
    {
        var pack = WidgetProposal.Build(PaletteArgs(), Now).Pack!;
        var json = Encoding.UTF8.GetBytes(WidgetProposal.RequestJson("r6", pack, null, Now).ToJsonString());
        Assert.Equal(WidgetRequestKind.Discard, WidgetProposal.Decode(json, Now.AddMinutes(11)).Kind);
        Assert.Equal(WidgetRequestKind.Discard, WidgetProposal.Decode("not json"u8.ToArray(), Now).Kind);
        Assert.Equal(WidgetRequestKind.Discard, WidgetProposal.Decode("{\"schema_version\":1}"u8.ToArray(), Now).Kind);
        var v2 = WidgetProposal.RequestJson("r7", pack, null, Now);
        v2["schema_version"] = 2;
        Assert.Equal(WidgetRequestKind.Refused, WidgetProposal.Decode(Encoding.UTF8.GetBytes(v2.ToJsonString()), Now).Kind);
    }

    [Fact]
    public void TheResultFileCarriesTheStatusReasonsPackAndCompanion()
    {
        var json = JsonNode.Parse(WidgetProposal.ResultJson("r8", new WidgetAnswer(WidgetAnswer.Applied, PackId: "my-blob", Companion: true), Now))!;
        Assert.Equal("r8", (string?)json["id"]);
        Assert.Equal("applied", (string?)json["result"]!["status"]);
        Assert.Equal("my-blob", (string?)json["result"]!["pack_id"]);
        Assert.True((bool)json["result"]!["companion"]!);
        var refused = JsonNode.Parse(WidgetProposal.ResultJson("r9", WidgetAnswer.Refused([new DeskReason("widget", "dismissed by the user")]), Now))!;
        Assert.Equal("dismissed by the user", (string?)refused["result"]!["reasons"]![0]!["message"]);
    }

    // ---- the handoff ----

    [Fact]
    public void TheHandoffTakesTheRequestAnswersWithTheHandlersVerdictAndNotReadyWithoutOne()
    {
        var paths = new WidgetHandoffPaths(_root);
        var handoff = new WidgetHandoff(paths, () => Now);
        var pack = WidgetProposal.Build(SpriteArgs(), Now).Pack!;
        File.WriteAllText(paths.Request, WidgetProposal.RequestJson("h1", pack, "hi", Now).ToJsonString());
        handoff.Check();
        Assert.False(File.Exists(paths.Request));
        var first = JsonNode.Parse(File.ReadAllText(paths.Result))!;
        Assert.Equal("h1", (string?)first["id"]);
        Assert.Equal("not_ready", (string?)first["result"]!["reason"]);

        WidgetProposalPack? seen = null;
        handoff.Handler = (id, p, note) => { seen = p; return new WidgetAnswer(WidgetAnswer.Previewing, PackId: p.Id); };
        File.WriteAllText(paths.Request, WidgetProposal.RequestJson("h2", pack, null, Now).ToJsonString());
        handoff.Check();
        Assert.Equal("my-neon-blob", seen!.Id);
        Assert.Equal("previewing", (string?)JsonNode.Parse(File.ReadAllText(paths.Result))!["result"]!["status"]);
    }

    // ---- the review bar's words ----

    [Fact]
    public void TheReviewSaysWhatTheWidgetIsAndOffersTheCompanionForASprite()
    {
        var sprite = WidgetProposal.Build(SpriteArgs(), Now).Pack!;
        var model = WidgetReview.Model(sprite, "A tiny friend.", deskOn: true);
        Assert.Equal("Claude suggests a widget · Neon blob", model.Heading);
        Assert.Equal("What this widget is", model.ChangesTitle);
        Assert.Equal("Make it the companion", model.Option);
        Assert.Equal("A tiny friend.", model.Note);
        Assert.True(model.CanApply);
        Assert.Contains(model.Changes, l => l.StartsWith("A Desk companion: 1 stage", StringComparison.Ordinal));
        Assert.Contains(model.Changes, l => l.Contains("4 frames of 8x8 pixels, 3 colors", StringComparison.Ordinal));
        Assert.Contains(model.Changes, l => l.StartsWith("Files: LICENSE, pack.json, sheet.json, sprite.json", StringComparison.Ordinal));
        Assert.Contains(WidgetReviewCopy.LocalOnly, model.Changes);

        var palette = WidgetProposal.Build(PaletteArgs(), Now).Pack!;
        var pm = WidgetReview.Model(palette, null, deskOn: false);
        Assert.Null(pm.Option);
        Assert.True(pm.DeskOff);
        Assert.Equal("Two evening gradients.", pm.Note);
        Assert.Contains(pm.Changes, l => l.StartsWith("dusk: 3 colors", StringComparison.Ordinal));
        Assert.Equal("ff2a6d,d16ba5,05d9e8", WidgetReview.PreviewInk(palette));
        Assert.Null(WidgetReview.PreviewInk(sprite));

        var effects = WidgetProposal.Build(EffectsArgs(), Now).Pack!;
        Assert.Contains(WidgetReview.Describe(effects), l => l == "breathe: pulse, 35% intensity, every 4 s, in the ink");
    }
}
