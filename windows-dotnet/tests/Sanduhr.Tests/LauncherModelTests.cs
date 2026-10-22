using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The Launcher's settings key: parse, write, the eight-button limit, edits and the image bookkeeping.</summary>
public class LauncherModelTests
{
    private static readonly string Hash = new string('a', 64) + ".png";
    private static readonly string OtherHash = new string('b', 64) + ".png";

    private static LauncherButton App(string id, string? label = null, string? image = null, string? glow = null) =>
        LauncherButton.Create(LauncherKind.App, id, image, label, glow)!;

    private static LauncherModel Of(params string[] ids) =>
        new(LauncherOrientation.Row, [.. ids.Select(i => App(i))]);

    [Fact]
    public void AbsentOrOddValuesReadAsNoButtons()
    {
        Assert.True(LauncherModel.Parse(null).IsEmpty);
        Assert.True(LauncherModel.Parse(JsonValue.Create("x")).IsEmpty);
        Assert.True(LauncherModel.Parse(new JsonArray()).IsEmpty);
        Assert.True(LauncherModel.Parse(new JsonObject()).IsEmpty);
        Assert.Equal(LauncherOrientation.Row, LauncherModel.Parse(null).Orientation);
    }

    [Fact]
    public void ParsesTheDocumentedShape()
    {
        var node = JsonNode.Parse($$"""
            { "orientation": "column", "buttons": [
              { "kind": "app", "target": "Chrome._crx_abc", "image": "{{Hash}}", "label": "Dash", "glow": "#AABBCC" },
              { "kind": "link", "target": "https://example.com/x", "image": null, "label": null, "glow": null } ] }
            """);
        var m = LauncherModel.Parse(node);
        Assert.Equal(LauncherOrientation.Column, m.Orientation);
        Assert.Equal(2, m.Buttons.Count);
        Assert.Equal(new LauncherButton(LauncherKind.App, "Chrome._crx_abc", Hash, "Dash", "#aabbcc"), m.Buttons[0]);
        Assert.Equal(new LauncherButton(LauncherKind.Link, "https://example.com/x"), m.Buttons[1]);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var m = new LauncherModel(LauncherOrientation.Column,
        [
            App("Spotify", "Music", Hash, "#ff8800"),
            LauncherButton.Create(LauncherKind.Link, "https://example.com")!,
        ]);
        var json = m.ToJson();
        Assert.Equal("column", json["orientation"]!.GetValue<string>());
        var back = LauncherModel.Parse(JsonNode.Parse(json.ToJsonString()));
        Assert.Equal(m.Orientation, back.Orientation);
        Assert.Equal(m.Buttons, back.Buttons);
        // Nulls are written, so the shape matches the spec's.
        Assert.True(json["buttons"]![1]!.AsObject().ContainsKey("image"));
    }

    [Fact]
    public void BadEntriesAreDroppedAndTheRestKept()
    {
        var node = JsonNode.Parse("""
            { "buttons": [
              "not an object", 7, null,
              { "kind": "app" },
              { "kind": "exe", "target": "notepad.exe" },
              { "kind": "app", "target": "C:\\Windows\\..\\notepad.exe" },
              { "kind": "link", "target": "http://example.com" },
              { "kind": "link", "target": "javascript:alert(1)" },
              { "kind": "app", "target": 42 },
              { "kind": "app", "target": "Spotify" } ] }
            """);
        var m = LauncherModel.Parse(node);
        Assert.Equal(["Spotify"], m.Buttons.Select(b => b.Target));
    }

    [Fact]
    public void AtMostEightButtonsAreKept()
    {
        var buttons = new JsonArray();
        for (int i = 0; i < 12; i++) buttons.Add(new JsonObject { ["kind"] = "app", ["target"] = $"App{i}" });
        var m = LauncherModel.Parse(new JsonObject { ["buttons"] = buttons });
        Assert.Equal(8, m.Buttons.Count);
        Assert.Equal("App0", m.Buttons[0].Target);
        Assert.Equal("App7", m.Buttons[7].Target);
        Assert.False(m.CanAdd);
        Assert.Same(m, m.Add(App("Extra")));
    }

    [Fact]
    public void BadEntriesDoNotCountAgainstTheLimit()
    {
        var buttons = new JsonArray();
        for (int i = 0; i < 5; i++) buttons.Add(new JsonObject { ["kind"] = "app", ["target"] = "bad id" });
        for (int i = 0; i < 8; i++) buttons.Add(new JsonObject { ["kind"] = "app", ["target"] = $"App{i}" });
        Assert.Equal(8, LauncherModel.Parse(new JsonObject { ["buttons"] = buttons }).Buttons.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Dash", "Dash")]
    [InlineData("  Dash  ", "Dash")]
    [InlineData("Da\tsh\n", "Dash")]
    public void LabelsAreTrimmedAndCleaned(string? raw, string? expected) =>
        Assert.Equal(expected, LauncherButton.NormalizeLabel(raw));

    [Fact]
    public void ALabelIsCutToTwentyFourCharactersWithoutSplittingAPair()
    {
        Assert.Equal(new string('x', 24), LauncherButton.NormalizeLabel(new string('x', 24)));
        Assert.Equal(new string('x', 24), LauncherButton.NormalizeLabel(new string('x', 40)));
        // 23 letters then a surrogate pair: the cut would land inside the pair, so it backs off one.
        var label = LauncherButton.NormalizeLabel(new string('x', 23) + "\U0001F600" + "tail");
        Assert.Equal(new string('x', 23), label);
    }

    [Theory]
    [InlineData("#AABBCC", "#aabbcc")]
    [InlineData("aabbcc", "#aabbcc")]
    [InlineData("#abc", "#aabbcc")]
    [InlineData(" #FF8800 ", "#ff8800")]
    [InlineData("red", null)]
    [InlineData("#12345", null)]
    [InlineData("#gggggg", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GlowIsAHexColorOrNothing(string? raw, string? expected) =>
        Assert.Equal(expected, LauncherButton.NormalizeGlow(raw));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef.png", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef.png", false)]
    [InlineData("short.png", false)]
    [InlineData("0123456789abcdef.exe", false)]
    [InlineData("0123456789abcdef.PNG", false)]
    [InlineData("..\\0123456789abcdef.png", false)]
    [InlineData("sub/0123456789abcdef.png", false)]
    [InlineData("C:\\0123456789abcdef.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ImageNamesAreHashedPngNamesNeverPaths(string? name, bool ok) =>
        Assert.Equal(ok, LauncherButton.IsImageName(name));

    [Fact]
    public void ABadImageNameOrLabelOrGlowDoesNotDropTheButton()
    {
        var node = JsonNode.Parse("""
            { "buttons": [ { "kind": "app", "target": "Spotify", "image": "..\\..\\evil.png", "label": "  ", "glow": "nope" } ] }
            """);
        var b = Assert.Single(LauncherModel.Parse(node).Buttons);
        Assert.Equal(new LauncherButton(LauncherKind.App, "Spotify"), b);
    }

    [Fact]
    public void CreateRefusesATargetThatFailsValidation()
    {
        Assert.Null(LauncherButton.Create(LauncherKind.App, @"C:\..\x.exe"));
        Assert.Null(LauncherButton.Create(LauncherKind.Link, "http://example.com"));
        Assert.NotNull(LauncherButton.Create(LauncherKind.Link, "https://example.com"));
    }

    [Fact]
    public void AddRemoveAndReplaceEditACopy()
    {
        var m = Of("A", "B", "C");
        Assert.Equal(["A", "B", "C", "D"], m.Add(App("D")).Buttons.Select(b => b.Target));
        Assert.Equal(["A", "C"], m.Remove(1).Buttons.Select(b => b.Target));
        Assert.Same(m, m.Remove(3));
        Assert.Same(m, m.Remove(-1));
        Assert.Equal(["A", "X", "C"], m.Replace(1, App("X")).Buttons.Select(b => b.Target));
        Assert.Same(m, m.Replace(5, App("X")));
        Assert.Equal(["A", "B", "C"], m.Buttons.Select(b => b.Target)); // the original is untouched
    }

    [Fact]
    public void AddAndReplaceRefuseAButtonThatDoesNotPass()
    {
        var m = Of("A");
        Assert.Same(m, m.Add(new LauncherButton(LauncherKind.App, @"C:\..\evil.exe")));
        Assert.Same(m, m.Replace(0, new LauncherButton(LauncherKind.Link, "http://example.com")));
    }

    [Fact]
    public void MoveReordersAndStopsAtTheEnds()
    {
        var m = Of("A", "B", "C");
        Assert.Equal(["B", "A", "C"], m.Move(1, -1).Buttons.Select(b => b.Target));
        Assert.Equal(["A", "C", "B"], m.Move(1, 1).Buttons.Select(b => b.Target));
        Assert.Same(m, m.Move(0, -1));
        Assert.Same(m, m.Move(2, 1));
        Assert.Same(m, m.Move(9, 1));
        Assert.Equal(["C", "A", "B"], m.Move(2, -5).Buttons.Select(b => b.Target));
    }

    [Fact]
    public void OrientationIsKeptThroughEdits()
    {
        var m = Of("A").WithOrientation(LauncherOrientation.Column);
        Assert.Equal(LauncherOrientation.Column, m.Add(App("B")).Orientation);
        Assert.Equal(LauncherOrientation.Column, m.Remove(0).Orientation);
    }

    [Fact]
    public void OrphanedImagesAreThoseNoButtonUsesAnymore()
    {
        var before = new LauncherModel(LauncherOrientation.Row, [App("A", image: Hash), App("B", image: OtherHash), App("C", image: Hash)]);
        Assert.Empty(LauncherModel.Orphaned(before, before));
        // Removing one of two buttons that share an image keeps the file.
        Assert.Equal([OtherHash], LauncherModel.Orphaned(before, before.Remove(1)));
        Assert.Empty(LauncherModel.Orphaned(before, before.Remove(0)));
        Assert.Equal([Hash, OtherHash], LauncherModel.Orphaned(before, LauncherModel.Empty));
        // Switching a button back to the app's icon frees its file.
        Assert.Equal([OtherHash], LauncherModel.Orphaned(before, before.Replace(1, App("B"))));
    }

    [Fact]
    public void DisplayNameIsTheLabelThenTheAppNameThenTheTarget()
    {
        Assert.Equal("Dash", App("Chrome._crx_abc", "Dash").DisplayName("Google Chrome"));
        Assert.Equal("Google Chrome", App("Chrome._crx_abc").DisplayName(" Google Chrome "));
        Assert.Equal("Chrome._crx_abc", App("Chrome._crx_abc").DisplayName());
        Assert.Equal("example.com", LauncherButton.Create(LauncherKind.Link, "https://www.example.com/x")!.DisplayName());
    }
}
