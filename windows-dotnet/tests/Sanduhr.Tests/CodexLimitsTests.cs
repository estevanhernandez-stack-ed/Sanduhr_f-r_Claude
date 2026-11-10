using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexLimitsTests
{
    private const string Good = """{"timestamp":"2026-10-22T23:48:09.060Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","limit_name":null,"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":1792721664},"secondary":{"used_percent":16.0,"window_minutes":10080,"resets_at":1793308464},"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"plan_type":"plus"}}}""";
    private const string Premium = """{"timestamp":"2026-10-22T23:47:55.788Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null,"plan_type":"plus"}}}""";

    [Fact]
    public void Parses_both_windows_and_the_plan()
    {
        Assert.True(CodexLimits.TryParseLine(Good, out var r));
        Assert.Equal(DateTimeOffset.Parse("2026-10-22T23:48:09.060Z"), r.AsOf);
        Assert.Equal(99.0, r.Primary!.UsedPercent);
        Assert.Equal(300, r.Primary.WindowMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792721664), r.Primary.ResetsAt);
        Assert.Equal(10080, r.Secondary!.WindowMinutes);
        Assert.Equal("plus", r.PlanType);
    }

    [Fact]
    public void A_premium_event_with_no_windows_is_not_a_reading() =>
        Assert.False(CodexLimits.TryParseLine(Premium, out _));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"timestamp":"2026-10-22T23:48:09Z","type":"response_item","payload":{"type":"message"}}""")]
    [InlineData("""{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}""")]
    [InlineData("""{"timestamp":"not a date","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}""")]
    public void Anything_else_is_not_a_reading(string line) =>
        Assert.False(CodexLimits.TryParseLine(line, out _));

    [Fact]
    public void A_window_without_a_reset_still_parses()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300}}}}""";
        Assert.True(CodexLimits.TryParseLine(line, out var r));
        Assert.Null(r.Primary!.ResetsAt);
        Assert.Null(r.Secondary);
    }

    [Fact]
    public void ToTiers_maps_by_window_length_not_by_position()
    {
        var swapped = new CodexLimitsReading(DateTimeOffset.UnixEpoch,
            Primary: new CodexWindow(16, 10080, null), Secondary: new CodexWindow(99, 300, null), PlanType: null);
        var tiers = CodexLimits.ToTiers(swapped, DateTimeOffset.UnixEpoch);
        Assert.Equal(99, tiers[CodexLimits.FiveHour]!["utilization"]!.GetValue<double>());
        Assert.Equal(16, tiers[CodexLimits.SevenDay]!["utilization"]!.GetValue<double>());
    }

    [Fact]
    public void ToTiers_drops_an_unknown_window_and_reports_it()
    {
        var odd = new CodexLimitsReading(DateTimeOffset.UnixEpoch, new CodexWindow(40, 1440, null), null, null);
        var seen = new List<int>();
        var tiers = CodexLimits.ToTiers(odd, DateTimeOffset.UnixEpoch, seen.Add);
        Assert.Empty(tiers);
        Assert.Equal(new[] { 1440 }, seen);
    }

    [Fact]
    public void A_passed_reset_reads_zero_and_keeps_its_reset_time()
    {
        var reset = DateTimeOffset.Parse("2026-10-23T02:00:00Z");
        var r = new CodexLimitsReading(DateTimeOffset.Parse("2026-10-22T23:00:00Z"), new CodexWindow(99, 300, reset), null, null);
        var tiers = CodexLimits.ToTiers(r, reset.AddMinutes(1));
        Assert.Equal(0, tiers[CodexLimits.FiveHour]!["utilization"]!.GetValue<double>());
        Assert.Equal(reset.ToString("o"), tiers[CodexLimits.FiveHour]!["resets_at"]!.GetValue<string>());
    }

    [Fact]
    public void Fresh_for_thirty_minutes_or_after_any_passed_reset()
    {
        var asOf = DateTimeOffset.Parse("2026-10-22T23:00:00Z");
        var r = new CodexLimitsReading(asOf, new CodexWindow(10, 300, asOf.AddHours(3)), null, null);
        Assert.True(CodexLimits.IsFresh(r, asOf.AddMinutes(30)));
        Assert.False(CodexLimits.IsFresh(r, asOf.AddMinutes(31)));
        Assert.True(CodexLimits.IsFresh(r, asOf.AddHours(3).AddSeconds(1)));
    }

    [Fact]
    public void A_null_reset_parses_with_no_reset()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":null}}}}""";
        Assert.True(CodexLimits.TryParseLine(line, out var r));
        Assert.Null(r.Primary!.ResetsAt);
    }

    [Fact]
    public void A_window_minutes_that_is_not_a_number_drops_that_window()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":"300"}}}}""";
        Assert.False(CodexLimits.TryParseLine(line, out _));
    }

    [Fact]
    public void An_out_of_range_reset_parses_with_no_reset()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":99999999999999}}}}""";
        Assert.True(CodexLimits.TryParseLine(line, out var r));
        Assert.Null(r.Primary!.ResetsAt);
    }

    [Fact]
    public void A_used_percent_that_overflows_drops_that_window()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":1e999,"window_minutes":300}}}}""";
        Assert.False(CodexLimits.TryParseLine(line, out _));
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    public void Used_percent_clamps_to_zero_through_one_hundred(int raw, double expected)
    {
        var line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":""" + raw + ""","window_minutes":300}}}}""";
        Assert.True(CodexLimits.TryParseLine(line, out var r));
        Assert.Equal(expected, r.Primary!.UsedPercent);
    }
}
