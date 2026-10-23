using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The 3.8 ride-alongs (spec 3.8, section 7): E, the weather nickname (#315); F, the Desk fold (#314, the affine half: a stack's tilt
/// in the layout string, the design contract, the review and the geometry Arrange Desk hit-tests); G, the livelier bars (smoothing,
/// falloff, peak caps, the calm mode) and the gradient-bars switch.
/// </summary>
public sealed class DeskRideAlongsTests
{
    private static DeskParse Parse(string json) => DeskDesign.Parse(JsonNode.Parse(json));

    private static DeskDesign D(string json) => Parse(json).Design!;

    // ---- F. the fold: the layout string ----

    [Fact]
    public void ATiltIsTheFourthFieldOfAWordAndRoundTrips()
    {
        var a = new DeskArrangement("clock:bl:1:-18 claude:bl:1.2:-18 meters:tr");
        Assert.Equal(new DeskPlacement("clock", DeskAnchor.BL, 1, null, -18), a.Placement("clock"));
        Assert.Equal(new DeskPlacement("claude", DeskAnchor.BL, 1.2, null, -18), a.Placement("claude"));
        Assert.Equal(0, a.Placement("meters")!.Tilt);
        Assert.Equal("clock:bl:1:-18 claude:bl:1.2:-18 meters:tr", a.Layout);
    }

    [Fact]
    public void AnUprightLayoutReadsAndWritesExactlyAsBeforeTheFold()
    {
        const string old = "clock:bl claude:bl:1.4 meters:tr@main telemetry:mr:0.8@main";
        var a = new DeskArrangement(old);
        Assert.All(a.Pieces, p => Assert.Equal(0, p.Tilt));
        Assert.Equal(old, a.Layout);
    }

    [Fact]
    public void ATiltWithAMonitorKeepsBothAndABuildBeforeTheFoldStillReadsTheSize()
    {
        var a = new DeskArrangement("clock:tl:1.4:12@main");
        Assert.Equal(new DeskPlacement("clock", DeskAnchor.TL, 1.4, DeskMonitors.Main, 12), a.Placement("clock"));
        Assert.Equal("clock:tl:1.4:12@main", a.Layout);
        // What an older build does with the word: the third field is still the size, and the fourth is never read.
        var bits = "clock:tl:1.4:12".Split(':');
        Assert.Equal("1.4", bits[2]);
    }

    [Theory]
    [InlineData("33", 20)]
    [InlineData("-90", -20)]
    [InlineData("7.6", 8)]
    [InlineData("-0.2", 0)]
    [InlineData("NaN", 0)]
    [InlineData("tilted", 0)]
    public void AnOddTiltIsClampedToWholeDegreesOrUpright(string word, double expected) =>
        Assert.Equal(expected, new DeskArrangement($"clock:bl:1:{word}").Placement("clock")!.Tilt);

    [Fact]
    public void SetTiltSlantsTheWholeStackAndOnlyIt()
    {
        var a = new DeskArrangement("clock:bl claude:bl meters:tr");
        a.SetTilt("claude", -18);
        Assert.Equal(-18, a.Placement("clock")!.Tilt);
        Assert.Equal(-18, a.Placement("claude")!.Tilt);
        Assert.Equal(0, a.Placement("meters")!.Tilt);
        Assert.Equal(-18, a.StackTilt(DeskAnchor.BL));
        Assert.Null(a.StackTilt(DeskAnchor.TC));
        a.SetTilt("watchers", 10);   // not placed: nothing changes
        Assert.Equal("clock:bl:1:-18 claude:bl:1:-18 meters:tr", a.Layout);
    }

    [Fact]
    public void APieceThatJoinsAStackTakesItsSlantAndOneAloneKeepsItsOwn()
    {
        var a = new DeskArrangement("clock:bl:1:-18 claude:bl:1:-18 meters:tr:1:12");
        a.Place("meters", DeskAnchor.BL);
        Assert.Equal(-18, a.Placement("meters")!.Tilt);
        a.Place("meters", DeskAnchor.TC);   // an empty anchor: it keeps the slant it had
        Assert.Equal(-18, a.Placement("meters")!.Tilt);

        var b = new DeskArrangement("clock:bl claude:tr:1:15");
        b.Put("clock", DeskAnchor.TR, "claude");
        Assert.Equal(15, b.Placement("clock")!.Tilt);

        var c = new DeskArrangement("clock:bl claude:tr:1:15");
        c.Move("clock", "claude");
        Assert.Equal(15, c.Placement("clock")!.Tilt);

        var d = new DeskArrangement("clock:bl:1:9");
        d.Show("weather");
        Assert.Equal(0, d.Placement("weather")!.Tilt);
    }

    [Fact]
    public void AReviewCountsAChangedSlantAsAChangedPiece()
    {
        var pieces = DeskMonitors.ReviewPieces(new DeskArrangement("clock:bl claude:tr"), new DeskArrangement("clock:bl:1:10 claude:tr"));
        Assert.Equal(["clock"], pieces.Select(p => p.Widget));
    }

    // ---- F. the fold: the design contract ----

    [Fact]
    public void ADesignCarriesATiltPerPieceAndMergesItOverTheStack()
    {
        var p = D("""{"layout":{"clock":{"anchor":"bl","tilt":-18.4}}}""");
        Assert.Equal(-18, p.Layout!.Single().Tilt);
        Assert.Equal(-18, (double?)p.ToJson()["layout"]!["clock"]!["tilt"]);

        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl claude:bl meters:tr" }, DeskInkSettings.Default, LauncherModel.Empty, null);
        Assert.All(current.Layout!.Where(x => !x.IsHidden), x => Assert.Equal(0, x.Tilt));
        var merged = DeskDesign.Merge(current, p);
        Assert.Equal(-18, merged.Layout!.Single(x => x.Piece == "claude").Tilt);   // the stack slants as one
        Assert.Equal(0, merged.Layout!.Single(x => x.Piece == "meters").Tilt);

        var layout = new DeskArrangement(DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, merged).Desk.Layout);
        Assert.Equal(-18, layout.StackTilt(DeskAnchor.BL));
    }

    [Theory]
    [InlineData("""{"layout":{"clock":{"tilt":21}}}""", "layout.clock.tilt")]
    [InlineData("""{"layout":{"clock":{"tilt":"18"}}}""", "layout.clock.tilt")]
    [InlineData("""{"layout":{"clock":{"anchor":"bl","tilt":5},"claude":{"anchor":"bl","tilt":6}}}""", "layout.clock.tilt")]
    public void ATiltOutOfRangeOrTwoSlantsForOneStackAreRefused(string json, string field)
    {
        var parsed = Parse(json);
        Assert.Null(parsed.Design);
        Assert.Contains(parsed.Reasons, r => r.Field == field);
    }

    [Fact]
    public void AHiddenPieceHasNoTiltAndTheSameSlantTwiceIsFine()
    {
        Assert.Null(D("""{"layout":{"clock":{"hidden":true,"tilt":10}}}""").Layout!.Single().Tilt);
        Assert.NotNull(Parse("""{"layout":{"clock":{"anchor":"bl","tilt":10},"claude":{"anchor":"bl","tilt":10}}}""").Design);
    }

    [Fact]
    public void TheReviewNamesASlantChange()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl" }, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("""{"layout":{"clock":{"tilt":-18},"meters":{"anchor":"tr","tilt":18}}}"""));
        var lines = DeskDesign.Diff(current, merged);
        Assert.Contains("clock: slant 0° to -18°", lines);
        Assert.Contains("meters: shown at top right, slanted 18°", lines);
    }

    [Fact]
    public void AnUprightDesignIsTheSameLookAsOneSavedBeforeTheFoldAndNicknames()
    {
        var before = D("""{"layout":{"clock":{"anchor":"bl","size":1,"monitor":"all"}},"font":"Segoe UI"}""");
        var now = before with
        {
            Layout = [new DeskPieceLayout("clock", DeskAnchor.BL, 1, false, "all", 0)],
            Weather = new DeskWeatherChange(""),
        };
        Assert.Equal(DeskDesigns.Canonical(before), DeskDesigns.Canonical(now));
        Assert.NotEqual(DeskDesigns.Canonical(before), DeskDesigns.Canonical(now with { Weather = new DeskWeatherChange("Funky Town") }));
        Assert.NotEqual(DeskDesigns.Canonical(before),
            DeskDesigns.Canonical(now with { Layout = [new DeskPieceLayout("clock", DeskAnchor.BL, 1, false, "all", 6)] }));
    }

    // ---- F. the fold: the geometry Arrange Desk hit-tests ----

    [Fact]
    public void AShearLiftsTheRightEdgeAndKeepsTheSidesUpright()
    {
        var q = DeskFold.Shear(new DeskRect(100, 100, 200, 50), -20);
        double dy = 200 * Math.Tan(-20 * Math.PI / 180);
        Assert.Equal(new DeskPoint(100, 100), q.TopLeft);
        Assert.Equal(300, q.TopRight.X);
        Assert.Equal(100 + dy, q.TopRight.Y, 6);
        Assert.Equal(q.TopRight.X, q.BottomRight.X);
        Assert.False(q.IsUpright);
        Assert.True(DeskFold.Shear(new DeskRect(0, 0, 10, 10), 0).IsUpright);
    }

    [Fact]
    public void APressFollowsTheSlantNotTheRectangleAroundIt()
    {
        var q = DeskFold.Shear(new DeskRect(0, 100, 200, 40), -20);   // the right end rises about 73 DIPs
        var b = q.Bounds;
        // Inside the box around it, but under the slanted bottom on the right: the bottom right corner of the box is empty.
        Assert.True(195 >= b.Left && 195 < b.Right && 135 >= b.Top && 135 < b.Bottom);
        Assert.False(DeskFold.Contains(q, 195, 135));
        // On the slanted face itself.
        Assert.True(DeskFold.Contains(q, 195, 50));
        Assert.True(DeskFold.Contains(q, 5, 120));
        Assert.False(DeskFold.Contains(q, 5, 60));
        // Growing keeps the slant and takes the pad on every side.
        var g = DeskFold.Grow(q, 6);
        Assert.True(DeskFold.Contains(g, -4, 120));
        Assert.Equal(q.TopRight.Y - q.TopLeft.Y, g.TopRight.Y - g.TopLeft.Y, 6);
        Assert.True(DeskFold.Area(g) > DeskFold.Area(q));
    }

    [Fact]
    public void AnUprightQuadIsItsRectangle()
    {
        var q = DeskQuad.Of(new DeskRect(10, 20, 30, 40));
        Assert.True(q.IsUpright);
        Assert.Equal(new DeskRect(10, 20, 30, 40), q.Bounds);
        Assert.True(DeskFold.Contains(q, 10, 20));
        Assert.True(DeskFold.Contains(q, 39.9, 59.9));
        Assert.False(DeskFold.Contains(q, 41, 30));
        Assert.Equal(1200, DeskFold.Area(q), 6);
    }

    // ---- E. the weather nickname ----

    [Fact]
    public void ADesignCanSetOrClearTheNickname()
    {
        Assert.Equal("Funky Town", D("""{"weather":{"nickname":"  Funky Town  "}}""").Weather!.Nickname);
        Assert.Equal("", D("""{"weather":{"nickname":""}}""").Weather!.Nickname);
        Assert.Null(D("{}").Weather);
        Assert.Equal("""{"weather":{"nickname":"Funky Town"}}""", D("""{"weather":{"nickname":"Funky Town"}}""").ToJson().ToJsonString());
        // 24 characters is the limit, counted as characters (an emoji is one).
        Assert.NotNull(Parse("""{"weather":{"nickname":"123456789012345678901234"}}""").Design);
        Assert.NotNull(Parse("""{"weather":{"nickname":"Funky Town 🎸🎸🎸🎸🎸🎸🎸🎸🎸🎸🎸🎸🎸"}}""").Design);
    }

    [Theory]
    [InlineData("""{"weather":{"nickname":"1234567890123456789012345"}}""", "weather.nickname")]
    [InlineData("""{"weather":{"nickname":"tab\there"}}""", "weather.nickname")]
    [InlineData("""{"weather":{"nickname":5}}""", "weather.nickname")]
    [InlineData("""{"weather":{}}""", "weather.nickname")]
    [InlineData("""{"weather":{"nickname":"x","lat":1}}""", "weather")]
    [InlineData("""{"weather":[]}""", "weather")]
    public void ABadNicknameIsRefused(string json, string field)
    {
        var parsed = Parse(json);
        Assert.Null(parsed.Design);
        Assert.Contains(parsed.Reasons, r => r.Field == field);
    }

    [Fact]
    public void TheNicknameMergesAndTheReviewSaysSo()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        Assert.Equal("", current.Weather!.Nickname);
        var merged = DeskDesign.Merge(current, D("""{"weather":{"nickname":"Funky Town"}}"""));
        Assert.Equal("Funky Town", merged.Weather!.Nickname);
        Assert.Contains("Weather: shown as “Funky Town”", DeskDesign.Diff(current, merged));
        var named = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null, "Funky Town");
        Assert.Contains("Weather: shows the place's own name", DeskDesign.Diff(named, DeskDesign.Merge(named, D("""{"weather":{"nickname":""}}"""))));
        Assert.Empty(DeskDesign.Diff(named, DeskDesign.Merge(named, D("""{"name":"x"}"""))));

        Assert.Equal("Funky Town", DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, merged).WeatherNickname);
        Assert.Null(DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, D("{}")).WeatherNickname);
    }

    [Fact]
    public void GetDeskReportsTheNicknameTheTiltAndTheirLimits()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl:1:-12" }, DeskInkSettings.Default, LauncherModel.Empty, null, "Funky Town");
        var state = DeskState.Build(current, [], null, null, null, [], false, "3.8.0");
        Assert.Equal("Funky Town", (string?)state["design"]!["weather"]!["nickname"]);
        Assert.Equal(-12, (double?)state["design"]!["layout"]!["clock"]!["tilt"]);
        Assert.Equal(24, (int?)state["limits"]!["weather_nickname_chars"]);
        Assert.Equal(-20, (double?)state["limits"]!["piece_tilt"]!["min"]);
        Assert.Equal(20, (double?)state["limits"]!["piece_tilt"]!["max"]);
    }

    [Fact]
    public void TheNicknameIsSavedOnlyWhenSetAndReadBackTidy()
    {
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);
        var plain = new DeskWeatherSettings(place, WeatherUnit.Fahrenheit);
        Assert.Null(plain.Encode()["nickname"]);
        var named = plain with { Nickname = "Funky Town" };
        var json = named.Encode();
        Assert.Equal("Funky Town", (string?)json["nickname"]);
        Assert.Equal(named, DeskWeatherSettings.Decode(json));
        // The place and its coordinates are untouched by a nickname: the forecast still asks for Fort Worth.
        Assert.Equal(place, DeskWeatherSettings.Decode(json).Place);

        var odd = DeskWeatherSettings.Decode(JsonNode.Parse("""{"nickname":"  Funky\nTown and a name far too long to keep  "}"""));
        Assert.Equal("FunkyTown and a name far", odd.Nickname);
        Assert.Equal(24, odd.Nickname.Length);
        Assert.Equal("", DeskWeatherSettings.NormalizeNickname(null));
        Assert.Equal("", DeskWeatherSettings.NormalizeNickname(" \t "));
    }

    [Fact]
    public void TheWeatherHeadingIsTheNicknameWhenThereIsOne()
    {
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);
        var reading = new WeatherReading(72, 2, 80, 61, 80, 75, 60, DateTimeOffset.UnixEpoch);
        Assert.Equal("Fort Worth", WeatherLines.For(place, reading).Heading);
        Assert.Equal("Fort Worth", WeatherLines.For(place, reading, "  ").Heading);
        Assert.Equal("Funky Town", WeatherLines.For(place, reading, "Funky Town").Heading);
        Assert.Equal("Funky Town", new DeskWeatherSettings(place, null, "Funky Town").Shown(place));
        Assert.Equal("Fort Worth", new DeskWeatherSettings(place, null).Shown(place));
    }

    // ---- G. livelier bars ----

    [Fact]
    public void ABarRisesByAShareOfTheGapAndFallsNoFasterThanTheFalloff()
    {
        var m = new AudioBarMotion(2);
        m.Step([1f, 0f]);
        Assert.Equal(AudioBarMotion.Attack, m.Heights[0], 5);
        for (int i = 0; i < 30; i++) m.Step([1f, 0f]);
        Assert.Equal(1f, m.Heights[0], 3);
        m.Step([0f, 0f]);
        Assert.Equal(1f - AudioBarMotion.Falloff, m.Heights[0], 4);   // a share of 1 is 0.35; the falloff limits it
        for (int i = 0; i < 60; i++) m.Step([0f, 0f]);
        Assert.Equal(0f, m.Heights[0]);
        // A small drop decays by its share, under the falloff.
        var s = new AudioBarMotion(1);
        for (int i = 0; i < 40; i++) s.Step([0.5f]);
        s.Step([0.46f]);
        Assert.Equal(0.5f - (0.5f - 0.46f) * AudioBarMotion.Decay, s.Heights[0], 3);
    }

    [Fact]
    public void ACapHoldsAtThePeakThenDropsFasterAndFasterOntoTheBar()
    {
        var m = new AudioBarMotion(1);
        for (int i = 0; i < 30; i++) m.Step([0.9f]);
        float peak = m.Caps[0];
        Assert.Equal(m.Heights[0], peak, 5);
        for (int i = 0; i < AudioBarMotion.CapHoldFrames; i++)
        {
            m.Step([0f]);
            Assert.Equal(peak, m.Caps[0], 5);
            Assert.True(m.Heights[0] < peak);
        }
        m.Step([0f]);
        float first = peak - m.Caps[0];
        m.Step([0f]);
        float second = peak - first - m.Caps[0];
        Assert.True(first > 0 && second > first, $"the cap accelerates ({first}, {second})");
        for (int i = 0; i < 200; i++) m.Step([0f]);
        Assert.Equal(0f, m.Caps[0]);
        Assert.All(Enumerable.Range(0, 1), i => Assert.True(m.Caps[i] >= m.Heights[i]));
    }

    [Fact]
    public void CalmBarsEaseGentlyAndDrawNoCaps()
    {
        var calm = new AudioBarMotion(1) { Calm = true };
        var lively = new AudioBarMotion(1);
        calm.Step([1f]);
        lively.Step([1f]);
        Assert.True(calm.Heights[0] < lively.Heights[0]);
        Assert.False(calm.ShowsCaps);
        Assert.True(lively.ShowsCaps);
        for (int i = 0; i < 40; i++) calm.Step([1f]);
        calm.Step([0f]);
        Assert.Equal(calm.Heights[0], calm.Caps[0]);   // the cap sits on the bar: no hold, no drop
        Assert.True(1f - calm.Heights[0] <= AudioBarMotion.CalmFalloff + 1e-6);
        Assert.True(AudioBarMotion.CalmFrameInterval > AudioBars.FrameInterval);
    }

    [Fact]
    public void TheMotionTakesOddLevelsAndResets()
    {
        var m = new AudioBarMotion(3);
        m.Step([float.NaN, 7f]);   // NaN is 0, 7 is 1, the missing third is 0
        Assert.Equal(0f, m.Heights[0]);
        Assert.Equal(AudioBarMotion.Attack, m.Heights[1], 5);
        Assert.Equal(0f, m.Heights[2]);
        m.Reset();
        Assert.All(m.Heights.Concat(m.Caps), v => Assert.Equal(0f, v));
        Assert.Single(new AudioBarMotion(0).Heights);
    }

    [Fact]
    public void GradientBarsIsAnOptInSwitchWrittenOnlyWhenOn()
    {
        Assert.False(NowPlayingSettings.Default.BarsGradient);
        Assert.Null(NowPlayingSettings.Default.Encode()["bars_gradient"]);
        var on = NowPlayingSettings.Default with { BarsGradient = true };
        Assert.True((bool)on.Encode()["bars_gradient"]!);
        Assert.True(NowPlayingSettings.Decode(on.Encode()).BarsGradient);
    }
}
