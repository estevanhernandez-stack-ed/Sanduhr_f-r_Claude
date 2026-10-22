using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>
/// A look per song, the Desk side: the Desk draws the playing song in its look (a gradient over the words, the weight and slant of the Desk
/// font, small caps as letters; the play mark stays plain; the position bar and the bars take the gradient; the white cap reaches the
/// stops), and a design that brings looks saves them when it is applied. Over a scratch AppData.
/// </summary>
public sealed class NowPlayingLooksDeskTests : NowPlayingLooksFixture
{
    // ---- what the Desk draws in ----

    [Fact]
    public void WithStyleOffNothingIsDrawnInALook()
    {
        using var s = Service();
        Assert.False(s.StyleOn);
        Assert.Null(s.DrawFor(Track(), capWhite: false));
    }

    [Fact]
    public void WithStyleOnASavedLookWinsAndAnUnseenSongWearsItsSeededOne()
    {
        Style(true);
        using var s = Service();
        s.Handoff.Save([Song("Blinding Lights", ink: ["#ff0000", "#00ff00"], style: LookStyle.Italic)]);

        var saved = s.DrawFor(Track(" BLINDING lights - Remastered 2011", "the weeknd"), capWhite: false)!;
        Assert.Equal(["ff0000", "00ff00"], saved.Stops);
        Assert.Equal(LookStyle.Italic, saved.Style);

        var other = Track("Starboy");
        var seeded = s.DrawFor(other, capWhite: false)!;
        var expected = NowPlayingLooks.Seeded(SongKey.Of("Starboy", "The Weeknd")!);
        Assert.Equal(NowPlayingLooks.DrawStops(expected, false), seeded.Stops);
        Assert.Equal(expected.Style, seeded.Style);

        Assert.Null(s.DrawFor(null, false));
        Assert.Null(s.DrawFor(new NowPlayingInfo(), false));   // nothing with a title
    }

    [Fact]
    public void OledCaresWhiteCapReachesTheLooksStops()
    {
        Style(true);
        using var s = Service();
        s.Handoff.Save([Song("Blinding Lights", ink: ["#ffffff", "#336699"])]);
        Assert.Equal(["ffffff", "336699"], s.DrawFor(Track(), capWhite: false)!.Stops);
        var capped = s.DrawFor(Track(), capWhite: true)!.Stops;
        Assert.NotEqual("ffffff", capped[0]);
        Assert.Equal("336699", capped[1]);
    }

    // ---- the Desk's line ----

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static DeskView Build(NowPlayingLookDraw? look, bool bars = false)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "nowPlaying:bl", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame
        {
            Now = Now, Track = Track(), Look = look, Bars = bars, Audio = bars ? new AudioLevelSource() : null,
        }, DeskInkMap.Light);
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static string Hex(Color c) => $"{c.R:x2}{c.G:x2}{c.B:x2}";

    [Fact]
    public void ALookDrawsTheWordsInItsGradientAndLeavesThePlayMarkPlain() => Sta.Run(() =>
    {
        var view = Build(new NowPlayingLookDraw(["ff2a6d", "05d9e8"], LookStyle.Bold));
        var blocks = All<TextBlock>(view).ToList();

        var glyph = Assert.Single(blocks, b => b.Text == NowPlayingText.PlayingGlyph);
        Assert.IsType<SolidColorBrush>(glyph.Foreground);
        Assert.Equal("ffffff", Hex(((SolidColorBrush)glyph.Foreground).Color));   // the Desk's own ink, not the look's

        var words = Assert.Single(blocks, b => b.Text == "Blinding Lights · The Weeknd");
        var gradient = Assert.IsType<LinearGradientBrush>(words.Foreground);
        Assert.Equal(["ff2a6d", "05d9e8"], gradient.GradientStops.Select(g => Hex(g.Color)));
        Assert.Equal(FontWeights.Bold, words.FontWeight);
        Assert.Equal(FontStyles.Normal, words.FontStyle);
        Assert.Equal(TextTrimming.CharacterEllipsis, words.TextTrimming);
        Assert.Equal("Blinding Lights · The Weeknd", AutomationProperties.GetName(words));
    });

    [Theory]
    [InlineData(LookStyle.Plain, false, false)]
    [InlineData(LookStyle.Bold, true, false)]
    [InlineData(LookStyle.Italic, false, true)]
    [InlineData(LookStyle.BoldItalic, true, true)]
    public void TheLetterStyleIsTheDeskFontsWeightAndSlant(LookStyle style, bool bold, bool italic) => Sta.Run(() =>
    {
        var view = Build(new NowPlayingLookDraw(["ff2a6d", "05d9e8"], style));
        var words = Assert.Single(All<TextBlock>(view), b => b.Text == "Blinding Lights · The Weeknd");
        Assert.Equal(bold ? FontWeights.Bold : FontWeights.Normal, words.FontWeight);
        Assert.Equal(italic ? FontStyles.Italic : FontStyles.Normal, words.FontStyle);
    });

    [Fact]
    public void SmallCapsDrawsTheLettersAndASingleStopDrawsSolid() => Sta.Run(() =>
    {
        var view = Build(new NowPlayingLookDraw(["ff9a9e"], LookStyle.SmallCaps));
        var words = Assert.Single(All<TextBlock>(view), b => b.Text == LookStyles.Text("Blinding Lights · The Weeknd", LookStyle.SmallCaps));
        Assert.NotEqual("Blinding Lights · The Weeknd", words.Text);
        Assert.Equal("ff9a9e", Hex(Assert.IsType<SolidColorBrush>(words.Foreground).Color));
        // A screen reader still reads the song, not the phonetic letters.
        Assert.Equal("Blinding Lights · The Weeknd", AutomationProperties.GetName(words));
    });

    [Fact]
    public void ThePositionBarTakesTheGradientAcrossTheWholeBar() => Sta.Run(() =>
    {
        var view = Build(new NowPlayingLookDraw(["ff2a6d", "05d9e8"], LookStyle.Bold));
        var rails = All<Rectangle>(view).Where(r => r.Fill is LinearGradientBrush).ToList();
        Assert.Equal(2, rails.Count);   // the rail and the fill
        foreach (var r in rails)
        {
            var g = (LinearGradientBrush)r.Fill;
            Assert.Equal(BrushMappingMode.Absolute, g.MappingMode);   // not squeezed into the filled part
            Assert.Equal(["ff2a6d", "05d9e8"], g.GradientStops.Select(s => Hex(s.Color)));
        }
    });

    [Fact]
    public void TheBarsBesideItTakeOneColorEachAlongTheGradient() => Sta.Run(() =>
    {
        var view = Build(new NowPlayingLookDraw(["ff0000", "0000ff"], LookStyle.Bold), bars: true);
        var bars = All<Canvas>(view).Single().Children.OfType<Rectangle>().ToList();
        Assert.True(bars.Count >= 8);
        var colors = bars.Select(b => Hex(Assert.IsType<SolidColorBrush>(b.Fill).Color)).ToList();
        Assert.Equal("ff0000", colors[0]);
        Assert.Equal("0000ff", colors[^1]);
        Assert.Equal(colors.Count, colors.Distinct().Count());
    });

    [Fact]
    public void WithoutALookTheLineIsTheDesksOwnInkAsBefore() => Sta.Run(() =>
    {
        var view = Build(null, bars: true);
        var words = Assert.Single(All<TextBlock>(view), b => b.Text == NowPlayingText.Line(Track()));
        Assert.IsType<SolidColorBrush>(words.Foreground);
        Assert.DoesNotContain(All<Rectangle>(view), r => r.Fill is LinearGradientBrush);
        var bars = All<Canvas>(view).Single().Children.OfType<Rectangle>().ToList();
        Assert.Single(bars.Select(b => Hex(((SolidColorBrush)b.Fill).Color)).Distinct());
    });

    // ---- a design that brings looks ----

    [Fact]
    public void ADesignsLooksAreSavedWhenItIsAppliedAndAWarningSaysWhenStyleIsOff() => Sta.Run(() =>
    {
        // The now-playing piece is not placed, so the contrast rule has no spot to judge: the wallpaper on this PC cannot decide the test.
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "Segoe UI" });
        _store.SaveDeskDirect(true);
        using var looks = Service();
        using var service = new DeskDesignService(_paths, _store)
        {
            InstalledFonts = () => ["Segoe UI"],
            Clock = () => Now,
            Looks = looks,
        };
        string design = """{"name":"Neon","plate":true,"looks":[{"title":"Blinding Lights","artist":"The Weeknd","ink":["#ff2a6d","#05d9e8"],"style":"bold","mood":"neon"}]}""";
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(service.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"d1\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + design + "}"));
        service.Handoff.Check();

        var result = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!;
        Assert.Equal("applied", (string?)result["result"]!["status"]);
        Assert.Contains(result["result"]!["changes"]!.AsArray().Select(n => (string)n!), c => c.StartsWith("Song looks: 1 song", StringComparison.Ordinal));
        var warning = Assert.Single(result["result"]!["warnings"]!.AsArray());
        Assert.Equal("looks", (string?)warning!["field"]);
        Assert.Contains("Style what's playing is off", (string?)warning["message"]);

        Assert.Equal(1, looks.Handoff.Book.Count);
        Assert.Equal(["#ff2a6d", "#05d9e8"], looks.Handoff.Book.Find(SongKey.Of("Blinding Lights", "The Weeknd")!)!.Ink);
        Assert.True(_store.LoadDeskPlate());   // the rest of the design applied as usual
    });

    [Fact]
    public void ADesignWithLooksAppliedWhereThereIsNoServiceLeavesThemAloneAndSaysSo() => Sta.Run(() =>
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "Segoe UI" });
        _store.SaveDeskDirect(true);
        using var service = new DeskDesignService(_paths, _store) { InstalledFonts = () => ["Segoe UI"], Clock = () => Now };
        string design = """{"plate":true,"looks":[{"title":"x","ink":["#ffffff"]}]}""";
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(service.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"d2\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + design + "}"));
        service.Handoff.Check();
        var result = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!;
        Assert.Equal("applied", (string?)result["result"]!["status"]);
        Assert.Contains("left alone", (string?)result["result"]!["warnings"]![0]!["message"]);
        Assert.False(File.Exists(_paths.NowPlayingLooksFile));
    });
}
