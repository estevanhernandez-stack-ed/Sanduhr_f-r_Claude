using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's MessageEffectsTests.swift and the sweep suite in MessageStyleTests.swift:
/// the effects markup, strict for Claude's proposals and lenient on the Desk, and what each effect means
/// on screen (animations off, out of sight). Made-up lines only.
/// </summary>
public class MessageEffectsTests
{
    private static MessageParsed Strict(string body) => MessageMarkup.ParseStrict(body).Value!;

    [Fact]
    public void Tags_InAnyOrder()
    {
        var p = Strict("{ink:#ff2a6d,#05d9e8} {glow}{size:1.5}  {write} {shimmer} hello there");
        Assert.Equal("hello there", p.Text);
        Assert.Equal(["ff2a6d", "05d9e8"], p.Effects.Ink);
        Assert.True(p.Effects.Glow);
        Assert.Equal(1.5, p.Effects.Size);
        Assert.True(p.Effects.Write && p.Effects.Shimmer);
        var q = Strict("{SHIMMER} {noglow} {ink:fff} x");
        Assert.False(q.Effects.Glow);
        Assert.True(q.Effects.Shimmer);
        Assert.Equal(["fff"], q.Effects.Ink);
    }

    [Fact]
    public void PlainLines_AndMidLineBraces()
    {
        Assert.Equal(new MessageParsed("keep building."), MessageMarkup.Parse("keep building."));
        Assert.Equal("hello {glow} there", MessageMarkup.Parse("hello {glow} there").Text);
        Assert.Equal(new MessageEffects(), MessageMarkup.Parse("hello {glow} there").Effects);
    }

    [Fact]
    public void StrictFailures_AreNamed()
    {
        (string Line, MessageFailure Want)[] cases =
        [
            ("{blink} hi", MessageFailure.Unknown("blink")),
            ("{glow hi", MessageFailure.Unclosed),
            ("{glow:yes} hi", MessageFailure.TakesNoValue("glow")),
            ("{ink:} hi", MessageFailure.InkMissing),
            ("{ink} hi", MessageFailure.InkMissing),
            ("{ink:#1,#2} hi", MessageFailure.InkNotHex),
            ("{ink:#111,#222,#333,#444,#555} hi", MessageFailure.InkTooMany),
            ("{size:3} hi", MessageFailure.Size),
            ("{size:0.4} hi", MessageFailure.Size),
            ("{size:big} hi", MessageFailure.Size),
            ("{size} hi", MessageFailure.Size),
            ("{glow} {write}", MessageFailure.NoText),
        ];
        foreach (var (line, want) in cases)
            Assert.True(want == MessageMarkup.ParseStrict(line).Failure, line);
        Assert.Equal(0.5, Strict("{size:0.5} a").Effects.Size);
        Assert.Equal(2, Strict("{size:2} a").Effects.Size);
    }

    /// <summary>The Mac's exact wording, which the MCP server mirrors.</summary>
    [Fact]
    public void FailureReasons_UseTheServersWording()
    {
        Assert.Equal("an effect tag is not closed with }", MessageFailure.Unclosed.Reason);
        Assert.Equal("unknown effect {blink}; known: ink, glow, noglow, size, write, shimmer, sweep, font", MessageFailure.Unknown("blink").Reason);
        Assert.Equal("{glow} takes no value", MessageFailure.TakesNoValue("glow").Reason);
        Assert.Equal("{ink:...} needs 1 to 4 hex colors, like {ink:#ff2a6d,#05d9e8}", MessageFailure.InkMissing.Reason);
        Assert.Equal("{ink:...} takes at most 4 colors", MessageFailure.InkTooMany.Reason);
        Assert.Equal("{ink:...} has a color that is not hex (#rgb or #rrggbb)", MessageFailure.InkNotHex.Reason);
        Assert.Equal("{size:...} needs a number from 0.5 to 2, like {size:1.3}", MessageFailure.Size.Reason);
        Assert.Equal("{font:...} needs a letter style: bold, italic, bold-italic, sans, mono, double-struck, script, fraktur, small-caps", MessageFailure.Font.Reason);
        Assert.Equal("{sweep:...} takes a period in seconds from 2 to 3600, like {sweep:20}", MessageFailure.Sweep.Reason);
        Assert.Equal("the line has effects but no text", MessageFailure.NoText.Reason);
        // The unknown name is cut at 20 characters.
        Assert.StartsWith("unknown effect {" + new string('a', 20) + "};", MessageFailure.Unknown(new string('a', 30)).Reason);
    }

    /// <summary>The Desk never hides a line and never drops text: a bad tag and the rest draw as written.</summary>
    [Fact]
    public void Lenient_DrawsBadTagsAsText()
    {
        var p = MessageMarkup.Parse("{glow} {blink} hello");
        Assert.True(p.Effects.Glow);
        Assert.Equal("{blink} hello", p.Text);
        Assert.Equal("{ink:nothex} hi", MessageMarkup.Parse("{ink:nothex} hi").Text);
        Assert.Equal("{glow} {write}", MessageMarkup.Parse("{glow} {write}").Text);
        Assert.Equal(new MessageEffects(), MessageMarkup.Parse("{glow} {write}").Effects);
        Assert.Equal("{unclosed hello", MessageMarkup.Parse("{unclosed hello").Text);
        Assert.Equal("", MessageMarkup.Parse("").Text);
    }

    [Fact]
    public void InkGlowAndSize()
    {
        var e = new MessageEffects();
        Assert.Equal("9ad7ff", MessageMotion.InkSpec(e, "9ad7ff"));
        Assert.True(MessageMotion.Glows(e, true));
        Assert.False(MessageMotion.Glows(e, false));
        Assert.Equal(84, MessageMotion.Size(e, 84));
        e = e with { Ink = ["ff2a6d", "05d9e8"], Glow = false, Size = 1.5 };
        Assert.Equal("ff2a6d,05d9e8", MessageMotion.InkSpec(e, "9ad7ff"));
        Assert.False(MessageMotion.Glows(e, true));
        Assert.Equal(126, MessageMotion.Size(e, 84));
        e = e with { Glow = true };
        Assert.True(MessageMotion.Glows(e, false));
    }

    [Fact]
    public void ReduceMotionAndPause()
    {
        var e = new MessageEffects();
        Assert.False(MessageMotion.AnimatesWrite(e, false));
        Assert.False(MessageMotion.Shimmers(e, false, false));
        e = e with { Write = true, Shimmer = true };
        Assert.True(MessageMotion.AnimatesWrite(e, false));
        Assert.False(MessageMotion.AnimatesWrite(e, true));
        Assert.True(MessageMotion.Shimmers(e, false, false));
        Assert.False(MessageMotion.Shimmers(e, true, false));
        Assert.False(MessageMotion.Shimmers(e, false, true));
    }

    [Fact]
    public void OutOfSight()
    {
        Assert.False(MessageMotion.Paused(true, false, false, false));
        Assert.True(MessageMotion.Paused(false, false, false, false));
        Assert.True(MessageMotion.Paused(true, true, false, false));
        Assert.True(MessageMotion.Paused(true, false, true, false));
        Assert.True(MessageMotion.Paused(true, false, false, true));
    }

    [Fact]
    public void Timings()
    {
        Assert.Equal(1.5, MessageMotion.WriteDuration);
        Assert.Equal(8, MessageMotion.ShimmerPeriod);
        Assert.True(MessageMotion.ShimmerSweep < MessageMotion.ShimmerPeriod);
        Assert.Equal(1.6, MessageMotion.ShimmerSweep);
        Assert.Equal(0.3, MessageMotion.ShimmerBand);
        Assert.True(DeskMessageLook.GlowDefault);
    }

    /// <summary>The Desk's pick keeps the tags; the view reads them.</summary>
    [Fact]
    public void Pick_KeepsTagsAfterThePrefix()
    {
        var sunday = DateTimeOffset.FromUnixTimeSeconds(1_785_067_200);   // 2026-07-26 12:00 UTC, a Sunday
        const string text = "{ink:#fff} plain\nSun: {glow} rest.\n";
        Assert.Equal("{glow} rest.", DeskMessages.Pick(text, sunday, false, zone: TimeZoneInfo.Utc));
        Assert.Equal(MessagePrefixKind.Plain, DeskMessages.SplitPrefix("{ink:#fff} plain").Kind);
        Assert.Equal(MessagePrefixKind.Date, DeskMessages.SplitPrefix("10-31: boo").Kind);
        Assert.Equal("Note: plain", DeskMessages.SplitPrefix("Note: plain").Body);
    }

    // The sweep

    [Fact]
    public void Sweep_TimingFollowsTheNowPlayingLight()
    {
        Assert.Equal(1.1, MessageMotion.SweepRun);
        Assert.Equal(3, MessageMotion.SweepWidth);
        Assert.True(MessageMotion.SweepFrame < 0.1);
        // Enters before the first character and leaves past the last.
        Assert.Equal(-3, MessageMotion.SweepAt(0, 10));
        Assert.Equal(5, MessageMotion.SweepAt(0.5, 10));
        Assert.Null(MessageMotion.SweepAt(1, 10));
        Assert.Null(MessageMotion.SweepAt(-0.1, 10));
        Assert.Null(MessageMotion.SweepAt(0.5, 0));
    }

    [Fact]
    public void Sweep_ThreeCharactersBrightenTowardWhite()
    {
        Assert.Equal(MessageMotion.SweepStrength, MessageMotion.SweepLight(5, 5));
        Assert.True(MessageMotion.SweepLight(3, 5) > 0);
        Assert.True(MessageMotion.SweepLight(7, 5) > 0);
        Assert.Equal(0, MessageMotion.SweepLight(8, 5));
        Assert.Equal(0, MessageMotion.SweepLight(2, 5));
        Assert.True(MessageMotion.SweepLight(4, 5) > MessageMotion.SweepLight(3, 5));
        Assert.Equal(0, MessageMotion.SweepLight(0, null));
    }

    [Fact]
    public void Sweep_OnceOnAppearOrOnAPeriod_AndRestsUnseen()
    {
        var e = new MessageEffects();
        Assert.Null(MessageMotion.SweepPlan(e, false, false, false, false));
        e = e with { Sweep = true };
        var once = MessageMotion.SweepPlan(e, false, false, false, false)!.Value;
        Assert.Equal(MessageMotion.SweepAppearDelay, once.First);
        Assert.Null(once.Every);
        // Once means once per line: back in sight after it ran, nothing moves.
        Assert.Null(MessageMotion.SweepPlan(e, false, false, true, false));
        Assert.Null(MessageMotion.SweepPlan(e, true, false, false, false));
        Assert.Null(MessageMotion.SweepPlan(e, false, true, false, false));
        e = e with { SweepPeriod = 20 };
        var every = MessageMotion.SweepPlan(e, false, false, true, false)!.Value;
        Assert.Equal(20, every.Every);
        Assert.Null(MessageMotion.SweepPlan(e, false, true, false, false));
        // Settings' Replay sweeps sooner.
        var replay = MessageMotion.SweepPlan(e, false, false, false, true)!.Value;
        Assert.Equal(MessageMotion.SweepReplayDelay, replay.First);
        Assert.True(MessageMotion.SweepReplayDelay < MessageMotion.SweepAppearDelay);
    }

    // Windows-specific

    [Fact]
    public void Sweep_PeriodNeverBeatsTheRun()
    {
        // The lowest period the grammar takes (2 s) is above the run, so it stands; the guard keeps a shorter one from overlapping itself.
        var plan = MessageMotion.SweepPlan(new MessageEffects { Sweep = true, SweepPeriod = 2 }, false, false, false, false)!.Value;
        Assert.Equal(2, plan.Every);
        var tight = MessageMotion.SweepPlan(new MessageEffects { Sweep = true, SweepPeriod = 0.5 }, false, false, false, false)!.Value;
        Assert.Equal(MessageMotion.SweepRun, tight.Every);
    }

    [Fact]
    public void Size_RejectsSignsExponentsAndBlanksTheMacRegexRejects()
    {
        foreach (var bad in new[] { "{size:+1} a", "{size:-1} a", "{size:1e0} a", "{size:.5} a", "{size:1.} a", "{size:1,5} a" })
            Assert.True(MessageFailure.Size == MessageMarkup.ParseStrict(bad).Failure, bad);
        Assert.Equal(1, Strict("{size: 1 } a").Effects.Size);
    }

    [Fact]
    public void Ink_KeepsTheCaseAndStripsTheHash()
    {
        Assert.Equal(["FF2A6D", "05d9e8"], Strict("{ink:#FF2A6D, 05d9e8} x").Effects.Ink);
    }

    [Fact]
    public void TagsEndWithBlanksOnly()
    {
        var p = Strict("  \t{glow}\t {write}   hi  ");
        Assert.Equal("hi", p.Text);
        // A newline is not a blank: the second tag is text.
        var q = MessageMarkup.Parse("{glow}\n{write} hi");
        Assert.Equal("{write} hi", q.Text.Replace("\n", ""));
    }
}
