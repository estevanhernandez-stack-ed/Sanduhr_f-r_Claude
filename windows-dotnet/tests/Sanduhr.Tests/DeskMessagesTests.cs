using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's DeskTests.swift (<c>MessagePickTests</c>) and the <c>MessageSpecialModeTests</c>
/// clock cases in MessageEditorTests.swift. Skipped: the cases that drive the Mac's <c>DeskModel</c>
/// (cycle order, the notch, covered-Desk rests, the preview card) and the UserDefaults round trip
/// (the App reads its own settings; <c>Parse</c> and <c>NormalizeSeconds</c> carry its rules).
/// </summary>
public class DeskMessagesTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>The Mac's test helper: Gregorian in UTC. A day past the month's end rolls over like the Mac's lenient calendar.</summary>
    private static DateTimeOffset Date(int y, int m, int d, int hour = 12) =>
        new DateTimeOffset(y, m, 1, hour, 0, 0, TimeSpan.Zero).AddDays(d - 1);

    private static string? Pick(string text, DateTimeOffset now, bool hourly = false) =>
        DeskMessages.Pick(text, now, hourly, zone: Utc);

    private static MessageToday Today(string text, DateTimeOffset now, bool hourly = false, bool mix = false, string? pinned = null) =>
        DeskMessages.Today(text, now, hourly, mix, pinned, Utc);

    // 2026-10-05 is a Monday, 2026-10-31 a Saturday.

    [Fact]
    public void PlainLines_WhenNothingMoreSpecific() =>
        Assert.Equal("keep building.", Pick("keep building.", Date(2026, 10, 6)));

    [Fact]
    public void WeekdayBeatsPlain()
    {
        const string text = "keep building.\nMon: one thing at a time.\nFri: showtime.";
        Assert.Equal("one thing at a time.", Pick(text, Date(2026, 10, 5)));
        Assert.Equal("showtime.", Pick(text, Date(2026, 10, 9)));
        Assert.Equal("keep building.", Pick(text, Date(2026, 10, 6)));
    }

    /// <summary>A date's lines add to the day; they no longer replace its usual line.</summary>
    [Fact]
    public void DateAddsAboveTheDaysLine()
    {
        const string text = "keep building.\nSat: rest.\n10-31: happy halloween.";
        Assert.Equal("rest.", Pick(text, Date(2026, 10, 31)));
        var today = Today(text, Date(2026, 10, 31));
        Assert.Equal(new MessageToday(["happy halloween."], "rest."), today);
        Assert.Equal(["happy halloween.", "rest."], today.Lines);
        Assert.Equal("happy halloween.", today.First);
        Assert.Equal("rest.", Pick(text, Date(2026, 10, 24)));
        Assert.Empty(Today(text, Date(2026, 10, 24)).Special);
        // A date line alone still shows.
        var alone = Today("10-31: boo.", Date(2026, 10, 31));
        Assert.Equal(new MessageToday(["boo."], null), alone);
        Assert.Equal("boo.", alone.First);
    }

    /// <summary>A pinned "Good vibes only" and a loved one's birthday: the pin replaces the usual line only; the date's lines still stack above it.</summary>
    [Fact]
    public void APinnedLine_KeepsSpecialDays()
    {
        const string one = "keep.\nSat: rest.\n03-14: happy birthday, Sam.";
        var t = Today(one, Date(2026, 3, 14), pinned: "Good vibes only");
        Assert.Equal(new MessageToday(["happy birthday, Sam."], "Good vibes only"), t);
        Assert.Equal(["happy birthday, Sam.", "Good vibes only"], t.Lines);
        // Other days: the pin alone, the weekday line stays hidden.
        Assert.Equal(new MessageToday(usual: "Good vibes only"), Today(one, Date(2026, 3, 21), pinned: "Good vibes only"));
        string four = "keep.\n" + string.Join("\n", Enumerable.Range(0, 4).Select(i => $"03-14: b{i}"));
        var seen = new HashSet<string>();
        for (int hour = 0; hour < 24; hour++)
        {
            var day = Today(four, Date(2026, 3, 14, hour), pinned: "Good vibes only");
            Assert.Equal(3, day.Special.Count);
            Assert.Equal("Good vibes only", day.Usual);
            seen.UnionWith(day.Special);
        }
        Assert.Equal(new HashSet<string> { "b0", "b1", "b2", "b3" }, seen);
    }

    [Fact]
    public void EveryLineForADateShowsUpToThree()
    {
        const string two = "good vibes only.\n03-14: happy birthday, Sam.\n03-14: happy birthday, Alex.";
        var t = Today(two, Date(2026, 3, 14));
        Assert.Equal(["happy birthday, Sam.", "happy birthday, Alex."], t.Special);
        Assert.Equal("good vibes only.", t.Usual);
        string three = string.Join("\n", Enumerable.Range(1, 3).Select(i => $"03-14: {i}"));
        Assert.Equal(["1", "2", "3"], Today(three, Date(2026, 3, 14)).Special);
    }

    [Fact]
    public void MoreThanThreeForADate_TakeTurnsHourly()
    {
        for (int n = 4; n <= 8; n++)
        {
            string text = string.Join("\n", Enumerable.Range(0, n).Select(i => $"03-14: {i}"));
            var seen = new HashSet<string>();
            for (int hour = 0; hour < 24; hour++)
            {
                var special = Today(text, Date(2026, 3, 14, hour)).Special;
                Assert.Equal(DeskMessages.MaxSpecial, special.Count);
                Assert.Equal(DeskMessages.MaxSpecial, special.Distinct().Count());
                seen.UnionWith(special);
                // Steady within the hour.
                Assert.Equal(special, Today(text, Date(2026, 3, 14, hour).AddMinutes(30)).Special);
            }
            Assert.Equal(n, seen.Count);
        }
    }

    /// <summary>Seven Friday lines always showed the same one when the day index stepped by 7.</summary>
    [Fact]
    public void EveryWeekdayLineComesRound()
    {
        for (int n = 1; n <= 8; n++)
        {
            string text = "plain.\n" + string.Join("\n", Enumerable.Range(0, n).Select(i => $"Fri: f{i}"));
            var fridays = Enumerable.Range(0, n).Select(i => Date(2026, 10, 9 + 7 * i));
            var picks = fridays.Select(f => Pick(text, f)!).ToHashSet();
            Assert.Equal(Enumerable.Range(0, n).Select(i => $"f{i}").ToHashSet(), picks);
            // Hourly: every line within one Friday.
            var hours = Enumerable.Range(0, 24).Select(h => Pick(text, Date(2026, 10, 9, h), hourly: true)!).ToHashSet();
            Assert.Equal(Enumerable.Range(0, n).Select(i => $"f{i}").ToHashSet(), hours);
        }
    }

    [Fact]
    public void EveryPlainLineComesRound()
    {
        for (int n = 1; n <= 8; n++)
        {
            string text = string.Join("\n", Enumerable.Range(0, n).Select(i => $"p{i}"));
            var picks = Enumerable.Range(0, n).Select(i => Pick(text, Date(2026, 10, 1 + i))).ToHashSet();
            Assert.Equal(n, picks.Count);
        }
    }

    [Fact]
    public void MixSwitch_PutsEveryDayLinesInTheWeekdaysTurns()
    {
        const string text = "a\nb\nFri: f";
        var fridays = Enumerable.Range(0, 3).Select(i => Date(2026, 10, 9 + 7 * i)).ToList();
        Assert.Equal(new HashSet<string> { "f" }, fridays.Select(f => Pick(text, f)!).ToHashSet());
        var mixed = fridays.Select(f => DeskMessages.Pick(text, f, false, mix: true, zone: Utc)).ToHashSet();
        Assert.Equal(new HashSet<string?> { "f", "a", "b" }, mixed);
        // Other days are unchanged by the switch.
        Assert.Equal(Pick(text, Date(2026, 10, 6)), DeskMessages.Pick(text, Date(2026, 10, 6), false, mix: true, zone: Utc));
    }

    [Fact]
    public void TagsForOtherDays_NeverFallThroughToPlain() =>
        // A Monday line on a Tuesday is not a plain line; with no plain lines there is no message.
        Assert.Null(Pick("Mon: one thing at a time.\n12-25: merry.", Date(2026, 10, 6)));

    [Fact]
    public void CommentsBlankLinesAndEmptyBodies_AreSkipped()
    {
        const string text = "# a comment\n\n   \nMon:\n10-05:   \nkeep building.";
        Assert.Equal("keep building.", Pick(text, Date(2026, 10, 5)));
        Assert.Null(Pick("# only a comment\n", Date(2026, 10, 5)));
        Assert.Null(Pick("", Date(2026, 10, 5)));
    }

    [Fact]
    public void ColonsThatAreNotTags_StayInTheLine()
    {
        Assert.Equal("note: ship it", Pick("note: ship it", Date(2026, 10, 5)));
        Assert.Equal("10:30 standup", Pick("10:30 standup", Date(2026, 10, 5)));
        // Tags are case sensitive: "mon:" is an ordinary line.
        Assert.Equal("mon: lower", Pick("mon: lower", Date(2026, 10, 6)));
    }

    [Fact]
    public void SurroundingWhitespace_IsTrimmed() =>
        Assert.Equal("spaced out.", Pick("  Mon :  spaced out.  ", Date(2026, 10, 5)));

    [Fact]
    public void RotatesOncePerDayThroughThePool()
    {
        const string text = "a\nb\nc";
        var picks = Enumerable.Range(5, 3).Select(d => Pick(text, Date(2026, 10, d))).ToList();
        Assert.Equal(new HashSet<string?> { "a", "b", "c" }, picks.ToHashSet());
        // Consecutive days step through the pool in order and wrap after three.
        Assert.Equal(picks[0], Pick(text, Date(2026, 10, 8)));
    }

    [Fact]
    public void SteadyWithinADay_UnlessHourly()
    {
        const string text = "a\nb\nc";
        Assert.Equal(Pick(text, Date(2026, 10, 5, 1)), Pick(text, Date(2026, 10, 5, 23)));
        Assert.NotEqual(Pick(text, Date(2026, 10, 5, 9), hourly: true), Pick(text, Date(2026, 10, 5, 10), hourly: true));
    }

    [Fact]
    public void StarterFileParses()
    {
        Assert.Equal("one thing at a time.", Pick(DeskMessages.Starter, Date(2026, 10, 5)));
        Assert.Equal("showtime.", Pick(DeskMessages.Starter, Date(2026, 10, 9)));
        Assert.Equal("keep building.", Pick(DeskMessages.Starter, Date(2026, 10, 7)));
    }

    // Windows-specific: the day index, the zone, the unreadable file

    /// <summary>Fliegel and Van Flandern's Julian day number for a Gregorian date, an independent route to the Mac's era-day ordinal.</summary>
    private static int JulianDayNumber(int y, int m, int d) =>
        (1461 * (y + 4800 + (m - 14) / 12)) / 4 + (367 * (m - 2 - 12 * ((m - 14) / 12))) / 12
        - (3 * ((y + 4900 + (m - 14) / 12) / 100)) / 4 + d - 32075;

    /// <summary>
    /// The Mac's <c>ordinality(of: .day, in: .era)</c> counts from 1 January of year 1 on the Julian calendar
    /// (day 1), whose Julian day number is 1721424. After 1582 that is the Julian day number minus 1721423.
    /// </summary>
    [Fact]
    public void DayIndex_IsTheMacsEraOrdinal()
    {
        Assert.Equal(730122, DeskMessages.DayIndex(new DateOnly(2000, 1, 1)));
        Assert.Equal(739896, DeskMessages.DayIndex(new DateOnly(2026, 10, 5)));
        foreach (var (y, m, d) in new[] { (1900, 1, 1), (1970, 1, 1), (2000, 1, 1), (2000, 2, 29), (2026, 10, 5), (2026, 12, 31), (2100, 3, 1) })
            Assert.Equal(JulianDayNumber(y, m, d) - 1721423, DeskMessages.DayIndex(new DateOnly(y, m, d)));
        Assert.Equal(3, DeskMessages.DayIndex(new DateOnly(1, 1, 1)));   // Gregorian 0001-01-01 is Julian 0001-01-03
        // The rotation the Mac picks for 5 to 7 October 2026 from three lines: 739896 mod 3 is 0.
        const string text = "a\nb\nc";
        Assert.Equal("a", Pick(text, Date(2026, 10, 5)));
        Assert.Equal("b", Pick(text, Date(2026, 10, 6)));
        Assert.Equal("c", Pick(text, Date(2026, 10, 7)));
        // A weekday's lines rotate by week: 739896 / 7 is 105699.
        const string fridays = "Fri: x\nFri: y\nFri: z";
        Assert.Equal(DeskMessages.DayIndex(new DateOnly(2026, 10, 9)) / 7 % 3, new[] { "x", "y", "z" }.ToList().IndexOf(Pick(fridays, Date(2026, 10, 9))!));
    }

    [Fact]
    public void TheDayAndHourAreReadInTheGivenZone()
    {
        const string text = "Mon: monday.\nTue: tuesday.";
        var late = new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.Zero);   // Monday in UTC
        Assert.Equal("monday.", DeskMessages.Pick(text, late, false, zone: Utc));
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(9), "t", "t");
        Assert.Equal("tuesday.", DeskMessages.Pick(text, late, false, zone: tokyo));
        // The same instant, hourly, uses the zone's hour: 08:30 in Tokyo.
        var hourlyText = string.Join("\n", Enumerable.Range(0, 5).Select(h => $"h{h}"));
        int tokyoSlot = (DeskMessages.DayIndex(new DateOnly(2026, 10, 6)) * 24 + 8) % 5;
        int utcSlot = (DeskMessages.DayIndex(new DateOnly(2026, 10, 5)) * 24 + 23) % 5;
        Assert.NotEqual(tokyoSlot, utcSlot);
        Assert.Equal($"h{utcSlot}", DeskMessages.Pick(hourlyText, late, true, zone: Utc));
        Assert.Equal($"h{tokyoSlot}", DeskMessages.Pick(hourlyText, late, true, zone: tokyo));
    }

    [Fact]
    public void ACrlfFile_ReadsLikeAnLfFile()
    {
        const string lf = "keep.\nMon: one.\n10-05: boo.\n";
        string crlf = lf.Replace("\n", "\r\n");
        var a = Today(lf, Date(2026, 10, 5));
        var b = Today(crlf, Date(2026, 10, 5));
        Assert.Equal(a, b);
        Assert.Equal(new MessageToday(["boo."], "one."), b);
    }

    [Fact]
    public void AnUnreadableFile_IsThePinAlone()
    {
        Assert.Equal(new MessageToday(usual: "pin"), DeskMessages.Today(null, Date(2026, 10, 5), false, pinned: "pin", zone: Utc));
        Assert.Equal(new MessageToday(), DeskMessages.Today(null, Date(2026, 10, 5), false, zone: Utc));
        // An empty pin is no pin.
        Assert.Null(DeskMessages.Today("", Date(2026, 10, 5), false, pinned: "", zone: Utc).Usual);
    }

    [Fact]
    public void Starter_MatchesTheMacByteForByte()
    {
        Assert.Equal(
            "# Desk messages. One per line; Desk picks one a day.\n" +
            "#   Mon: text      only on Mondays\n" +
            "#   10-31: text    only on that date\n" +
            "#   {ink:#ff2a6d,#05d9e8} {glow} {size:1.2} {write} {shimmer} text    effects\n" +
            "keep building.\n" +
            "Mon: one thing at a time.\n" +
            "Fri: showtime.", DeskMessages.Starter);
    }

    // On special days

    private const double T0 = 1_800_000_000;   // a multiple of 10, 30, 60 and 300

    [Fact]
    public void SpecialMode_CycleOrderAndTiming()
    {
        foreach (var (offset, want) in new[] { (0.0, 0), (9.9, 0), (10, 1), (19, 1), (20, 2), (29.9, 2), (30, 0), (45, 1) })
            Assert.Equal(want, MessageSpecialModes.Index(T0 + offset, 3, 10));
        Assert.Equal(1, MessageSpecialModes.Index(T0 + 61, 2, 60));
        Assert.Equal(0, MessageSpecialModes.Index(T0, 1, 10));
        Assert.Equal(T0 + 10, MessageSpecialModes.NextChange(T0 + 3, 10));
        Assert.Equal(T0 + 300, MessageSpecialModes.NextChange(T0, 300));
        // Before 1970 the step still wraps into range.
        Assert.Equal(2, MessageSpecialModes.Index(-5, 3, 10));
        Assert.Equal(0, MessageSpecialModes.Index(T0, 3, 0));
        Assert.Equal(T0 + 10, MessageSpecialModes.ToUnixSeconds(DateTimeOffset.FromUnixTimeSeconds(1_800_000_010)));
    }

    [Fact]
    public void SpecialMode_Cycles_OnlyOnASpecialDayWithMoreThanOneLine()
    {
        var many = new MessageToday(["sam.", "alex."], "keep.");
        Assert.False(MessageSpecialModes.Cycles(MessageSpecialMode.Stack, many));
        Assert.True(MessageSpecialModes.Cycles(MessageSpecialMode.Turns, many));
        Assert.True(MessageSpecialModes.Cycles(MessageSpecialMode.Scroll, many));
        // Nothing cycles without a special day, nor for a date line alone.
        Assert.False(MessageSpecialModes.Cycles(MessageSpecialMode.Turns, new MessageToday([], "keep.")));
        Assert.False(MessageSpecialModes.Cycles(MessageSpecialMode.Scroll, new MessageToday(["sam."], null)));
        // Cycle order: the date's lines, then the usual line, round again.
        var shown = Enumerable.Range(0, 4).Select(k => many.Lines[MessageSpecialModes.Index(T0 + k * 10, many.Lines.Count, 10)]).ToList();
        Assert.Equal(["sam.", "alex.", "keep.", "sam."], shown);
    }

    [Fact]
    public void SpecialMode_ReduceMotionFallsBackToASwap()
    {
        Assert.Equal(SpecialChange.Crossfade, MessageSpecialModes.Change(MessageSpecialMode.Turns, false));
        Assert.Equal(SpecialChange.Scroll, MessageSpecialModes.Change(MessageSpecialMode.Scroll, false));
        Assert.Equal(SpecialChange.None, MessageSpecialModes.Change(MessageSpecialMode.Turns, true));
        Assert.Equal(SpecialChange.None, MessageSpecialModes.Change(MessageSpecialMode.Scroll, true));
        Assert.Equal(SpecialChange.None, MessageSpecialModes.Change(MessageSpecialMode.Stack, false));
        Assert.Equal(0.4, MessageSpecialModes.Fade);
        Assert.Equal(0.8, MessageSpecialModes.Glide);
    }

    [Fact]
    public void SpecialMode_SavedValuesNormalize()
    {
        Assert.Equal(MessageSpecialMode.Stack, MessageSpecialModes.Parse(null));
        Assert.Equal(MessageSpecialMode.Scroll, MessageSpecialModes.Parse("scroll"));
        Assert.Equal(MessageSpecialMode.Stack, MessageSpecialModes.Parse("ticker"));
        Assert.Equal(10, MessageSpecialModes.NormalizeSeconds(0));
        Assert.Equal(60, MessageSpecialModes.NormalizeSeconds(60));
        Assert.Equal(10, MessageSpecialModes.NormalizeSeconds(7));
        Assert.Equal(["Stack", "Take turns", "Scroll"], MessageSpecialModes.All.Select(MessageSpecialModes.Title));
        Assert.Equal(["5 s", "10 s", "30 s", "1 min", "5 min"], MessageSpecialModes.Presets.Select(MessageSpecialModes.Label));
        Assert.All(MessageSpecialModes.All, m => Assert.False(string.IsNullOrEmpty(MessageSpecialModes.Help(m))));
    }
}
