using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>OLED care (spec 3.7.0): the drift orbit and its clamp, the idle state machine, and the white cap on the Desk ink.</summary>
public class OledCareTests
{
    // ---- The switch ----

    [Fact]
    public void TheSwitchIsOffUntilTheKeyIsTrue()
    {
        Assert.Equal("desk_oled_care", OledCare.Key);
        Assert.False(OledCare.Read(null));
        Assert.False(OledCare.Read(new JsonObject()));
        Assert.False(OledCare.Read(new JsonObject { ["desk_oled_care"] = "yes" }));
        Assert.True(OledCare.Read(new JsonObject { ["desk_oled_care"] = true }));
        Assert.Equal("OLED care", OledCare.Title);
        Assert.Equal("For OLED screens: the Desk drifts a few pixels every few minutes, dims when you're away, and never holds pure white.", OledCare.Caption);
    }

    // ---- Drift ----

    [Fact]
    public void TheOrbitIsSixteenPointsAFigureEightWithinSixPixels()
    {
        var points = Enumerable.Range(0, OledDrift.Steps).Select(OledDrift.Offset).ToList();
        Assert.Equal(16, points.Count);
        Assert.All(points, p => { Assert.InRange(p.X, -6, 6); Assert.InRange(p.Y, -6, 6); });
        Assert.Contains((6, 4), points);
        Assert.Contains((-6, -4), points);
        // x = 6 sin(k pi / 8), y = 6 sin(k pi / 4), rounded.
        for (int k = 0; k < 16; k++)
        {
            Assert.Equal((int)Math.Round(6 * Math.Sin(k * Math.PI / 8), MidpointRounding.AwayFromZero), points[k].X);
            Assert.Equal((int)Math.Round(6 * Math.Sin(k * Math.PI / 4), MidpointRounding.AwayFromZero), points[k].Y);
        }
    }

    [Fact]
    public void SuccessivePointsDifferAndTheLoopAveragesOnTheAnchor()
    {
        for (int k = 0; k < 40; k++)
            Assert.NotEqual(OledDrift.Offset(k), OledDrift.Offset(k + 1));
        Assert.Equal(0, Enumerable.Range(0, 16).Sum(k => OledDrift.Offset(k).X));
        Assert.Equal(0, Enumerable.Range(0, 16).Sum(k => OledDrift.Offset(k).Y));
    }

    [Fact]
    public void TheLoopRepeatsAndNegativeStepsWrap()
    {
        for (int k = 0; k < 16; k++)
        {
            Assert.Equal(OledDrift.Offset(k), OledDrift.Offset(k + 16));
            Assert.Equal(OledDrift.Offset(k), OledDrift.Offset(k - 16));
        }
        Assert.Equal(OledDrift.Offset(15), OledDrift.Offset(-1));
        Assert.Equal(TimeSpan.FromMinutes(3), OledDrift.Every);
    }

    private static readonly PxRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void AClampLeavesAPieceInsideItsAreaWhereItIs()
    {
        var r = new PxRect(100, 200, 300, 120);
        Assert.Equal(r, OledDrift.Clamp(r, Work, DeskInsets.None));
    }

    [Fact]
    public void AClampPullsAPieceBackFromEveryEdgeAndFromTheTaskbarsReach()
    {
        var clearance = new DeskInsets(0, 0, 0, 48);
        // Past the bottom: held 48 above the work area's edge.
        Assert.Equal(1032 - 48 - 120, OledDrift.Clamp(new PxRect(100, 990, 300, 120), Work, clearance).Y);
        Assert.Equal(0, OledDrift.Clamp(new PxRect(-5, -9, 300, 120), Work, DeskInsets.None).X);
        Assert.Equal(0, OledDrift.Clamp(new PxRect(-5, -9, 300, 120), Work, DeskInsets.None).Y);
        Assert.Equal(1920 - 300, OledDrift.Clamp(new PxRect(1700, 10, 300, 120), Work, DeskInsets.None).X);
        // A taskbar on the left.
        Assert.Equal(60, OledDrift.Clamp(new PxRect(40, 10, 300, 120), Work, new DeskInsets(60, 0, 0, 0)).X);
    }

    [Fact]
    public void ARectBiggerThanTheRegionSitsAtItsTopLeft()
    {
        var c = OledDrift.Clamp(new PxRect(500, 500, 3000, 2000), Work, DeskInsets.None);
        Assert.Equal((0, 0), (c.X, c.Y));
    }

    [Fact]
    public void DriftMovesTheRectByTheOrbitAndNeverChangesItsSize()
    {
        var home = new PxRect(800, 400, 300, 120);
        for (int k = 0; k < 16; k++)
        {
            var d = OledDrift.Drifted(home, k, Work, DeskInsets.None);
            var (ox, oy) = OledDrift.Offset(k);
            Assert.Equal((home.X + ox, home.Y + oy, 300, 120), (d.X, d.Y, d.Width, d.Height));
        }
    }

    [Fact]
    public void DriftNeverCrossesTheTaskbarClearanceOrTheWorkAreaWhateverTheStep()
    {
        var clearance = new DeskInsets(0, 0, 0, 56);
        // Sitting right on the clearance's edge, and in the top left corner.
        foreach (var home in new[] { new PxRect(100, 1032 - 56 - 120, 300, 120), new PxRect(0, 0, 300, 120), new PxRect(1920 - 300, 400, 300, 120) })
            for (int k = 0; k < 16; k++)
            {
                var d = OledDrift.Drifted(home, k, Work, clearance);
                Assert.True(d.X >= 0 && d.Y >= 0 && d.Right <= 1920 && d.Bottom <= 1032 - 56, $"{home} step {k} -> {d}");
            }
    }

    [Fact]
    public void ThePieceTheLayoutPutOutsideTheRegionIsNeverPulledIn()
    {
        // Already 3 px into the clearance: drift may move it along the orbit but never farther in than where it started.
        var clearance = new DeskInsets(0, 0, 0, 56);
        var home = new PxRect(100, 1032 - 56 - 120 + 3, 300, 120);
        for (int k = 0; k < 16; k++)
        {
            var d = OledDrift.Drifted(home, k, Work, clearance);
            Assert.True(d.Y <= home.Y, $"step {k} -> {d}");
        }
        Assert.Equal(home.Y - 4, OledDrift.Drifted(home, 5, Work, clearance).Y); // step 5 moves up by 4: allowed
    }

    // ---- Idle ----

    private sealed class Clock { public DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero); }

    [Fact]
    public void DimsOnceAfterFiveMinutesWithoutInputAndWakesOnceOnInput()
    {
        var clock = new Clock();
        var idle = new OledIdle(() => clock.Now);
        var lastInput = clock.Now;

        Assert.Equal(OledIdleEdge.None, idle.Observe(lastInput));
        clock.Now += TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(45);
        Assert.Equal(OledIdleEdge.None, idle.Observe(lastInput));
        Assert.False(idle.Dimmed);
        clock.Now += TimeSpan.FromSeconds(15); // exactly five minutes
        Assert.Equal(OledIdleEdge.Dim, idle.Observe(lastInput));
        Assert.True(idle.Dimmed);
        // Still away: nothing more is asked for.
        clock.Now += TimeSpan.FromMinutes(20);
        Assert.Equal(OledIdleEdge.None, idle.Observe(lastInput));

        lastInput = clock.Now - TimeSpan.FromSeconds(2); // a key press
        Assert.Equal(OledIdleEdge.Wake, idle.Observe(lastInput));
        Assert.False(idle.Dimmed);
        Assert.Equal(OledIdleEdge.None, idle.Observe(lastInput));
    }

    [Fact]
    public void AnUnreadableLastInputChangesNothing()
    {
        var clock = new Clock();
        var idle = new OledIdle(() => clock.Now);
        Assert.Equal(OledIdleEdge.None, idle.Observe(null));
        idle.Observe(clock.Now - TimeSpan.FromHours(1));
        Assert.True(idle.Dimmed);
        Assert.Equal(OledIdleEdge.None, idle.Observe(null));
        Assert.True(idle.Dimmed);
    }

    [Fact]
    public void ResetWakesADimmedStateAndIsQuietOtherwise()
    {
        var clock = new Clock();
        var idle = new OledIdle(() => clock.Now);
        Assert.Equal(OledIdleEdge.None, idle.Reset());
        idle.Observe(clock.Now - TimeSpan.FromHours(1));
        Assert.Equal(OledIdleEdge.Wake, idle.Reset());
        Assert.False(idle.Dimmed);
        Assert.Equal(OledIdleEdge.None, idle.Reset());
    }

    [Fact]
    public void TheThresholdCanBeChangedAndTheNumbersAreTheSpecs()
    {
        var clock = new Clock();
        var idle = new OledIdle(() => clock.Now, TimeSpan.FromSeconds(30));
        Assert.Equal(OledIdleEdge.Dim, idle.Observe(clock.Now - TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromMinutes(5), OledIdle.DefaultThreshold);
        Assert.Equal(TimeSpan.FromSeconds(15), OledIdle.PollEvery);
        Assert.Equal(0.40, OledIdle.DeskDimmedOpacity);
        Assert.Equal(0.70, OledIdle.WidgetDimmedOpacity);
        Assert.Equal(TimeSpan.FromSeconds(2), OledIdle.EaseFor(OledIdleEdge.Dim, animate: true));
        Assert.Equal(TimeSpan.FromMilliseconds(300), OledIdle.EaseFor(OledIdleEdge.Wake, animate: true));
    }

    [Theory]
    [InlineData(OledIdleEdge.Dim)]
    [InlineData(OledIdleEdge.Wake)]
    public void ShowAnimationsOffSnapsBothWays(OledIdleEdge edge) =>
        Assert.Equal(TimeSpan.Zero, OledIdle.EaseFor(edge, animate: false));

    // ---- The white cap ----

    [Fact]
    public void PureWhiteIsDrawnAsE6E6E6()
    {
        Assert.Equal("e6e6e6", InkHex.CapLuminance("ffffff", DeskInk.WhiteCapLuminance));
        Assert.Equal(InkHex.Luminance("e6e6e6"), DeskInk.WhiteCapLuminance, 9);
    }

    [Fact]
    public void ColorsUnderTheCapAreUntouchedAndAnInvalidOneReadsAsWhite()
    {
        foreach (var hex in new[] { "9ad7ff", "ffd08a", "000000", "e6e6e6", "1b1f27", "33fdff" })
            Assert.Equal(hex, InkHex.CapLuminance(hex, DeskInk.WhiteCapLuminance));
        Assert.Equal("e6e6e6", InkHex.CapLuminance("nope", DeskInk.WhiteCapLuminance));
        Assert.Equal("e6e6e6", InkHex.CapLuminance("#FFF", DeskInk.WhiteCapLuminance));
    }

    [Fact]
    public void ANearWhiteKeepsItsHueAndLandsAtTheCap()
    {
        var capped = InkHex.CapLuminance("fff4e0", DeskInk.WhiteCapLuminance);
        Assert.InRange(InkHex.Luminance(capped), DeskInk.WhiteCapLuminance - 0.01, DeskInk.WhiteCapLuminance + 0.01);
        int r = Convert.ToInt32(capped[..2], 16), g = Convert.ToInt32(capped.Substring(2, 2), 16), b = Convert.ToInt32(capped.Substring(4, 2), 16);
        Assert.True(r >= g && g >= b, capped); // still warm
        Assert.True(r < 0xff);
    }

    [Fact]
    public void TheCapScalesWhiteLightInkAndEveryGradientStopAboveItAndNothingElse()
    {
        var light = DeskInk.Resolve(DeskInkSettings.Default with { Mode = DeskInkMode.Light }, null, null);
        Assert.Equal(["ffffff", "ffffff"], light.Stops);
        var capped = DeskInk.CapWhite(light);
        Assert.Equal(["e6e6e6", "e6e6e6"], capped.Stops);
        Assert.Equal(light.DarkInk, capped.DarkInk);
        Assert.Equal(light.TextOpacity, capped.TextOpacity);
        Assert.Equal(light.ShadowHex, capped.ShadowHex);

        var gradient = DeskInk.Resolve(DeskInkSettings.Default with { Mode = DeskInkMode.Custom, Color = "ffffff,3a63e0,fdfdfd" }, null, null);
        var g = DeskInk.CapWhite(gradient);
        Assert.Equal("e6e6e6", g.Stops[0]);
        Assert.Equal("3a63e0", g.Stops[1]);
        Assert.InRange(InkHex.Luminance(g.Stops[2]), 0, DeskInk.WhiteCapLuminance + 0.01);
        Assert.NotEqual("fdfdfd", g.Stops[2]);
    }

    [Fact]
    public void DarkInkKeepsItsColorAndItsWhiteShadowIsCappedToo()
    {
        var dark = DeskInk.Resolve(DeskInkSettings.Default with { Mode = DeskInkMode.Dark }, null, null);
        var capped = DeskInk.CapWhite(dark);
        Assert.Equal(dark.Stops, capped.Stops);
        Assert.Equal("ffffff", dark.ShadowHex);
        Assert.Equal("e6e6e6", capped.ShadowHex);
    }

    [Fact]
    public void ResolveStillHandsBackPureWhiteTheCapIsARenderTimeRule()
    {
        var ink = new JsonObject { ["mode"] = "custom", ["color"] = "ffffff" };
        Assert.Equal("ffffff", DeskInkSettings.Decode(ink).Color);
        Assert.Equal(["ffffff", "ffffff"], DeskInk.Resolve(DeskInkSettings.Decode(ink), null, null).Stops);
    }
}
