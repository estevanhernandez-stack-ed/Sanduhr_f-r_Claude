using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of LimitLifetimeTests.swift: which limits are temporary, when hidden ones return.</summary>
public class LimitLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static string In(TimeSpan span) => (Now + span).ToString("o");

    [Theory]
    [InlineData("five_hour")]
    [InlineData("seven_day")]
    public void Permanent_tiers_are_never_temporary(string tier)
    {
        Assert.False(LimitLifetime.IsTemporary(tier, In(TimeSpan.FromDays(30)), Now, Now.AddDays(-1)));
    }

    [Fact]
    public void Iguana_is_always_temporary() =>
        Assert.True(LimitLifetime.IsTemporary("iguana_necktie", null, Now, null));

    [Fact]
    public void Reset_exactly_eight_days_is_not_temporary() =>
        Assert.False(LimitLifetime.IsTemporary("seven_day_sonnet", In(TimeSpan.FromDays(8)), Now, null));

    [Fact]
    public void Reset_past_eight_days_is_temporary() =>
        Assert.True(LimitLifetime.IsTemporary("seven_day_sonnet", In(TimeSpan.FromDays(8) + TimeSpan.FromSeconds(60)), Now, null));

    [Fact]
    public void Missing_reset_is_not_temporary() =>
        Assert.False(LimitLifetime.IsTemporary("seven_day_sonnet", null, Now, null));

    [Fact]
    public void Unknown_tier_is_temporary_inside_the_new_window()
    {
        Assert.True(LimitLifetime.IsTemporary("seven_day_newthing", null, Now, Now.AddDays(-13)));
        Assert.False(LimitLifetime.IsTemporary("seven_day_newthing", null, Now, Now.AddDays(-14)));
        Assert.False(LimitLifetime.IsTemporary("seven_day_newthing", null, Now, null));
    }

    [Fact]
    public void Known_model_limit_with_near_reset_is_not_temporary()
    {
        Assert.False(LimitLifetime.IsTemporary("seven_day_opus", In(TimeSpan.FromDays(3)), Now, Now.AddDays(-2)));
        Assert.False(LimitLifetime.IsTemporary("seven_day_fable", In(TimeSpan.FromDays(3)), Now, Now.AddDays(-2)));
        Assert.False(LimitLifetime.IsTemporary("routines", null, Now, Now.AddDays(-2)));
    }

    [Fact]
    public void Garbage_reset_decides_nothing() =>
        Assert.False(LimitLifetime.IsTemporary("seven_day_sonnet", "not a date", Now, null));

    // -- ShouldReturn ----------------------------------------------------------

    private static readonly HiddenLimitRecord Rec = new(In(TimeSpan.FromDays(10)), 80);

    [Fact]
    public void New_window_needs_more_than_an_hour()
    {
        Assert.Null(LimitLifetime.ShouldReturn(Rec, In(TimeSpan.FromDays(10) + TimeSpan.FromSeconds(3600)), 80));
        Assert.Equal("new_window", LimitLifetime.ShouldReturn(Rec, In(TimeSpan.FromDays(10) + TimeSpan.FromSeconds(3601)), 80));
        Assert.Null(LimitLifetime.ShouldReturn(Rec, In(TimeSpan.FromDays(9)), 80));
    }

    [Theory]
    [InlineData(70.5, null)]
    [InlineData(70.0, "refill")]
    [InlineData(0.0, "refill")]
    [InlineData(100.0, null)]
    public void Refill_needs_ten_points(double now, string? expected) =>
        Assert.Equal(expected, LimitLifetime.ShouldReturn(Rec, Rec.ResetsAt, now));

    [Fact]
    public void Missing_data_decides_nothing()
    {
        Assert.Null(LimitLifetime.ShouldReturn(new HiddenLimitRecord(null, null), null, null));
        Assert.Null(LimitLifetime.ShouldReturn(new HiddenLimitRecord(null, 80), null, null));
        Assert.Null(LimitLifetime.ShouldReturn(Rec, null, null));
    }

    // -- Hide / Show -----------------------------------------------------------

    [Fact]
    public void Hide_refuses_a_permanent_tier()
    {
        var state = LimitLifetime.Hide(LimitState.Empty, "five_hour", In(TimeSpan.FromHours(2)), 50, Now);
        Assert.Same(LimitState.Empty, state);
    }

    [Fact]
    public void Hide_stores_the_record_for_a_temporary_tier_and_show_removes_it()
    {
        var reset = In(TimeSpan.FromDays(10));
        var hidden = LimitLifetime.Hide(LimitState.Empty, "iguana_necktie", reset, 42, Now);
        Assert.Equal(new HiddenLimitRecord(reset, 42), hidden.HiddenUntilReset["iguana_necktie"]);
        Assert.Empty(LimitState.Empty.HiddenUntilReset);

        var shown = LimitLifetime.Show(hidden, "iguana_necktie");
        Assert.Empty(shown.HiddenUntilReset);
    }

    // -- Reconcile -------------------------------------------------------------

    private static IReadOnlyDictionary<string, (string? ResetsAt, double? Util)> Present(
        params (string tier, string? reset, double? util)[] rows) =>
        rows.ToDictionary(r => r.tier, r => (r.reset, r.util));

    [Fact]
    public void Reconcile_records_first_sightings_once()
    {
        var first = LimitLifetime.Reconcile(LimitState.Empty, Present(("seven_day_newthing", null, 5)), Now);
        Assert.Equal(Now, first.State.FirstSeen["seven_day_newthing"]);

        var second = LimitLifetime.Reconcile(first.State, Present(("seven_day_newthing", null, 6)), Now.AddDays(2));
        Assert.Equal(Now, second.State.FirstSeen["seven_day_newthing"]);
    }

    [Fact]
    public void Reconcile_returns_a_tier_that_stopped_being_temporary()
    {
        var state = new LimitState(
            new Dictionary<string, DateTimeOffset> { ["seven_day_newthing"] = Now.AddDays(-20) },
            new Dictionary<string, HiddenLimitRecord> { ["seven_day_newthing"] = new(null, 50) });
        var result = LimitLifetime.Reconcile(state, Present(("seven_day_newthing", null, 50)), Now);
        Assert.Equal("not_temporary", result.Returned["seven_day_newthing"]);
        Assert.Empty(result.State.HiddenUntilReset);
    }

    [Fact]
    public void Reconcile_returns_on_refill_and_new_window_and_keeps_steady_ones()
    {
        var reset = In(TimeSpan.FromDays(10));
        var state = new LimitState(
            new Dictionary<string, DateTimeOffset>(),
            new Dictionary<string, HiddenLimitRecord> { ["iguana_necktie"] = new(reset, 80) });

        var refill = LimitLifetime.Reconcile(state, Present(("iguana_necktie", reset, 60)), Now);
        Assert.Equal("refill", refill.Returned["iguana_necktie"]);
        Assert.False(refill.State.HiddenUntilReset.ContainsKey("iguana_necktie"));

        var steady = LimitLifetime.Reconcile(state, Present(("iguana_necktie", reset, 79)), Now);
        Assert.Empty(steady.Returned);
        Assert.True(steady.State.HiddenUntilReset.ContainsKey("iguana_necktie"));

        var moved = LimitLifetime.Reconcile(state, Present(("iguana_necktie", In(TimeSpan.FromDays(17)), 80)), Now);
        Assert.Equal("new_window", moved.Returned["iguana_necktie"]);
    }

    [Fact]
    public void Reconcile_leaves_absent_tiers_hidden()
    {
        var state = new LimitState(
            new Dictionary<string, DateTimeOffset>(),
            new Dictionary<string, HiddenLimitRecord> { ["iguana_necktie"] = new(null, 80) });
        var result = LimitLifetime.Reconcile(state, Present(("seven_day", null, 10)), Now);
        Assert.Empty(result.Returned);
        Assert.True(result.State.HiddenUntilReset.ContainsKey("iguana_necktie"));
    }

    // -- JSON ------------------------------------------------------------------

    [Fact]
    public void First_seen_round_trips()
    {
        var map = new Dictionary<string, DateTimeOffset> { ["a"] = Now, ["b"] = Now.AddDays(-3) };
        var back = LimitLifetime.FirstSeenFromJson(LimitLifetime.FirstSeenToJson(map));
        Assert.Equal(map, back);
    }

    [Fact]
    public void Hidden_round_trips_including_nulls()
    {
        var map = new Dictionary<string, HiddenLimitRecord>
        {
            ["a"] = new("2026-10-20T00:00:00Z", 61.5),
            ["b"] = new(null, null),
        };
        var back = LimitLifetime.HiddenFromJson(LimitLifetime.HiddenToJson(map));
        Assert.Equal(map, back);
    }

    [Fact]
    public void Json_readers_tolerate_odd_values()
    {
        Assert.Empty(LimitLifetime.FirstSeenFromJson(null));
        Assert.Empty(LimitLifetime.HiddenFromJson(JsonNode.Parse("[1,2]")));

        var first = LimitLifetime.FirstSeenFromJson(JsonNode.Parse("{\"a\":\"nope\",\"b\":5,\"c\":\"2026-10-01T00:00:00Z\"}"));
        Assert.Single(first);
        Assert.True(first.ContainsKey("c"));

        var hidden = LimitLifetime.HiddenFromJson(JsonNode.Parse(
            "{\"a\":{\"resets_at\":7,\"utilization\":\"42.5\"},\"b\":\"x\",\"c\":{\"utilization\":\"abc\"},\"d\":{\"utilization\":12}}"));
        Assert.Equal(new HiddenLimitRecord(null, 42.5), hidden["a"]);
        Assert.False(hidden.ContainsKey("b"));
        Assert.Equal(new HiddenLimitRecord(null, null), hidden["c"]);
        Assert.Equal(12, hidden["d"].Utilization);
    }
}
