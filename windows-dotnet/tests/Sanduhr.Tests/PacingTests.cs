using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/Pacing.cs — first port from the Python build's
/// pacing.py + test_pacing intents. Clock is fixed at exactly half-way
/// through a 7-day period (frac = 0.5) so every case is deterministic.
/// </summary>
public class PacingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string HalfwayReset = "2026-01-04T12:00:00Z"; // Now + 3.5 days
    private const string Seven = "seven_day";

    [Fact]
    public void PaceFrac_halfway_is_one_half()
        => Assert.Equal(0.5, Pacing.PaceFrac(HalfwayReset, Seven, Now)!.Value, 6);

    [Fact]
    public void PaceFrac_null_when_resets_missing()
        => Assert.Null(Pacing.PaceFrac(null, Seven, Now));

    [Fact]
    public void PaceInfo_on_pace_at_50()
    {
        var r = Pacing.PaceInfo(50d, HalfwayReset, Seven, Now);
        Assert.NotNull(r);
        Assert.Equal("On pace", r!.Value.Label);
        Assert.Equal("#4ade80", r.Value.Color);
    }

    [Fact]
    public void PaceInfo_ahead_at_80()
    {
        var r = Pacing.PaceInfo(80d, HalfwayReset, Seven, Now);
        Assert.NotNull(r);
        Assert.Equal("30% ahead", r!.Value.Label);
        Assert.Equal("#fb923c", r.Value.Color);
    }

    [Fact]
    public void PaceInfo_under_at_20()
    {
        var r = Pacing.PaceInfo(20d, HalfwayReset, Seven, Now);
        Assert.NotNull(r);
        Assert.Equal("30% under", r!.Value.Label);
        Assert.Equal("#60a5fa", r.Value.Color);
    }

    [Fact]
    public void PaceInfo_null_when_util_missing()
        => Assert.Null(Pacing.PaceInfo(null, HalfwayReset, Seven, Now));

    [Fact]
    public void Cooldown_when_ahead()
        => Assert.Equal("2d 2h 24m", Pacing.CalculateCooldown(80d, HalfwayReset, Seven, Now));

    [Fact]
    public void Cooldown_null_when_under()
        => Assert.Null(Pacing.CalculateCooldown(20d, HalfwayReset, Seven, Now));

    [Fact]
    public void Surplus_when_under()
        => Assert.Equal(30, Pacing.CalculateSurplus(20d, HalfwayReset, Seven, Now));

    [Fact]
    public void Surplus_null_when_ahead()
        => Assert.Null(Pacing.CalculateSurplus(80d, HalfwayReset, Seven, Now));

    [Fact]
    public void Velocity_projects_linear()
        => Assert.Equal(160d, Pacing.VelocityProjection(80d, HalfwayReset, Seven, Now)!.Value, 6);

    [Fact]
    public void Burn_projects_expiry_before_reset()
    {
        var r = Pacing.BurnProjection(80d, HalfwayReset, Seven, Now);
        Assert.NotNull(r);
        Assert.Equal("At current pace, expires in 21h", r!.Value.Message);
        Assert.Equal("#f87171", r.Value.Color);
    }

    [Fact]
    public void Burn_null_when_on_pace()
        => Assert.Null(Pacing.BurnProjection(50d, HalfwayReset, Seven, Now));

    [Fact]
    public void TimeUntil_formats_countdown()
        => Assert.Equal("3d 12h", Pacing.TimeUntil(HalfwayReset, Now));

    [Fact]
    public void TimeUntil_dashes_when_missing()
        => Assert.Equal("--", Pacing.TimeUntil(null, Now));
}
