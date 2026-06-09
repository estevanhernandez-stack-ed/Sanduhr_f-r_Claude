using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for the focus-timer hourglass physics — ported from
/// <c>test_focus_physics.py</c>. This is the cert-load-bearing model (MS Store
/// 10.1.4.4); the throttle invariant (sand can never outpace the wall clock,
/// compared as a float not a truncated int) is the regression these guard.
/// </summary>
public class HourglassSimulationTests
{
    [Fact]
    public void Reset_spawns_top_half_sand_and_clears_passed()
    {
        // Ports test_focus_widget_constructs: the mask must spawn some sand.
        var sim = new HourglassSimulation(new Random(0));
        Assert.True(sim.TotalSand > 0, "Hourglass mask should spawn some sand");
        Assert.Equal(0, sim.SandPassed);
        Assert.Equal(31, sim.Width);
        Assert.Equal(31, sim.Height);
    }

    [Fact]
    public void Bowtie_mask_matches_dx_le_dy_plus_1()
    {
        var sim = new HourglassSimulation(new Random(0));
        int cx = sim.CenterX, cy = sim.CenterY;
        for (int y = 0; y < sim.Height; y++)
        {
            for (int x = 0; x < sim.Width; x++)
            {
                bool expected = Math.Abs(x - cx) <= Math.Abs(y - cy) + 1;
                Assert.Equal(expected, sim.IsCell(x, y));
            }
        }
    }

    [Fact]
    public void Zero_duration_does_not_crash_and_is_a_noop()
    {
        // Ports test_zero_duration_does_not_crash: a 0-minute start must bail out,
        // not divide by zero.
        var sim = new HourglassSimulation(new Random(0));
        bool moved = sim.Step(elapsedMs: 1000, durationMs: 0);
        Assert.False(moved);
        Assert.Equal(0, sim.SandPassed);
    }

    [Fact]
    public void Step_before_start_is_safe()
    {
        // Ports test_physics_bails_when_not_started: physics with no elapsed time
        // and a real duration is a safe no-op for crossing (nothing has elapsed).
        var sim = new HourglassSimulation(new Random(0));
        sim.Step(elapsedMs: 0, durationMs: 10 * 60 * 1000);
        Assert.Equal(0, sim.SandPassed);
    }

    [Fact]
    public void Halfway_through_some_sand_has_passed_the_throat()
    {
        // Ports test_expected_passed_is_float_not_truncated (the >0 assertion):
        // at 50% elapsed of a 10-minute block, at least one grain must cross.
        var sim = new HourglassSimulation(new Random(0));
        long durationMs = 10 * 60 * 1000;
        sim.Step(elapsedMs: 300 * 1000, durationMs: durationMs);
        Assert.True(sim.SandPassed > 0,
            "After 50% elapsed time, at least one grain should have passed the bottleneck.");
    }

    [Fact]
    public void Throttle_compares_as_float_not_truncated_int()
    {
        // The load-bearing regression. Choose elapsed/duration so expected_passed
        // is exactly 0.5 — between 0 and 1. With the correct FLOAT comparison the
        // first entering grain crosses (0 >= 0.5 is false), then SandPassed == 1
        // blocks the rest of the sweep (1 >= 0.5). With the OLD truncated-int bug,
        // int(0.5) == 0 so 0 >= 0 holds immediately and NO grain crosses (0).
        var sim = new HourglassSimulation(new Random(0));
        long total = sim.TotalSand;
        long durationMs = 2L * total * 1000L; // expected = (1000/duration)*total = 0.5
        sim.Step(elapsedMs: 1000, durationMs: durationMs);
        Assert.Equal(1, sim.SandPassed);
    }

    [Fact]
    public void Sand_never_outpaces_the_wall_clock()
    {
        // Drive many ticks at a fixed early elapsed; SandPassed must stay capped by
        // the float expected_passed ceiling for that elapsed fraction.
        var sim = new HourglassSimulation(new Random(1));
        long durationMs = 60 * 1000;
        long elapsedMs = 6 * 1000; // 10% elapsed
        double expectedCeiling = ((double)elapsedMs / durationMs) * sim.TotalSand;
        for (int i = 0; i < 200; i++)
            sim.Step(elapsedMs, durationMs);
        Assert.True(sim.SandPassed <= Math.Ceiling(expectedCeiling),
            $"SandPassed {sim.SandPassed} should not exceed the wall-clock ceiling {expectedCeiling}.");
    }
}
