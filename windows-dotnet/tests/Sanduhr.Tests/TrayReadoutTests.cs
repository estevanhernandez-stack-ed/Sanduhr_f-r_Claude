using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Ported from the Mac's MenuBarTextTests, retargeted to TrayReadout.</summary>
public class TrayReadoutTests
{
    private static TrayReading? R(TrayMode m, double? s, double? w, int step = 0,
        (string, double)? highest = null) => TrayReadout.Reading(m, s, w, highest, step);

    private static void Is(TrayReading? r, int percent, string label)
    {
        Assert.NotNull(r);
        Assert.Equal(percent, r!.Percent);
        Assert.Equal(label, r.Label);
    }

    [Fact]
    public void Session_and_weekly_modes_read_their_own_value()
    {
        Is(R(TrayMode.Session, 12, 96), 12, "Session");
        Is(R(TrayMode.Weekly, 12, 96), 96, "Weekly");
    }

    [Fact]
    public void Higher_picks_the_larger_and_ties_go_to_session()
    {
        Is(R(TrayMode.Higher, 12, 96), 96, "Weekly");
        Is(R(TrayMode.Higher, 40, 20), 40, "Session");
        Is(R(TrayMode.Higher, 50, 50), 50, "Session");
    }

    [Fact]
    public void Higher_with_one_value_uses_it()
    {
        Is(R(TrayMode.Higher, null, 30), 30, "Weekly");
        Is(R(TrayMode.Higher, 30, null), 30, "Session");
    }

    [Theory]
    [InlineData(0, 12, "Session")]
    [InlineData(1, 96, "Weekly")]
    [InlineData(2, 12, "Session")]
    [InlineData(7, 96, "Weekly")]
    [InlineData(-1, 96, "Weekly")]
    [InlineData(-2, 12, "Session")]
    public void Rotate_alternates_by_step(int step, int percent, string label) =>
        Is(R(TrayMode.Rotate, 12, 96, step), percent, label);

    [Fact]
    public void Rotate_falls_back_to_the_only_value()
    {
        Is(R(TrayMode.Rotate, null, 63, 0), 63, "Weekly");
        Is(R(TrayMode.Rotate, 7, null, 1), 7, "Session");
    }

    [Fact]
    public void Percent_truncates()
    {
        Is(R(TrayMode.Weekly, null, 96.7), 96, "Weekly");
        Is(R(TrayMode.Highest, null, null, 0, ("Opus", 96.7)), 96, "Opus");
    }

    [Fact]
    public void Single_mode_falls_back_to_the_other_value()
    {
        Is(R(TrayMode.Session, null, 44), 44, "Weekly");
        Is(R(TrayMode.Weekly, 21, null), 21, "Session");
    }

    [Fact]
    public void Highest_uses_the_tuple_and_ignores_session_and_weekly()
    {
        Is(R(TrayMode.Highest, 12, 96, 0, ("Sonnet", 55)), 55, "Sonnet");
        Assert.Null(R(TrayMode.Highest, 12, 96));
    }

    [Fact]
    public void Nothing_yields_null_in_every_mode()
    {
        foreach (var m in TrayReadout.All)
            Assert.Null(R(m, null, null));
    }

    [Theory]
    [InlineData("highest", TrayMode.Highest)]
    [InlineData("session", TrayMode.Session)]
    [InlineData("weekly", TrayMode.Weekly)]
    [InlineData("higher", TrayMode.Higher)]
    [InlineData("rotate", TrayMode.Rotate)]
    [InlineData("ROTATE", TrayMode.Rotate)]
    [InlineData("Weekly", TrayMode.Weekly)]
    [InlineData("bogus", TrayMode.Highest)]
    [InlineData("", TrayMode.Highest)]
    [InlineData(null, TrayMode.Highest)]
    public void Parse_is_case_insensitive_and_defaults_to_highest(string? raw, TrayMode expected) =>
        Assert.Equal(expected, TrayReadout.Parse(raw));

    [Fact]
    public void Raw_round_trips_through_Parse_for_all_modes()
    {
        Assert.Equal(5, TrayReadout.All.Count);
        foreach (var m in TrayReadout.All)
            Assert.Equal(m, TrayReadout.Parse(TrayReadout.Raw(m)));
    }

    [Fact]
    public void Titles_and_interval()
    {
        Assert.Equal("Highest of any limit", TrayReadout.Title(TrayMode.Highest));
        Assert.Equal("Session", TrayReadout.Title(TrayMode.Session));
        Assert.Equal("Weekly", TrayReadout.Title(TrayMode.Weekly));
        Assert.Equal("Whichever is higher", TrayReadout.Title(TrayMode.Higher));
        Assert.Equal("Rotate (session and weekly)", TrayReadout.Title(TrayMode.Rotate));
        Assert.Equal(TimeSpan.FromSeconds(8), TrayReadout.RotateInterval);
    }
}
