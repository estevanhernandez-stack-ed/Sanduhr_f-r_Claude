using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AudioBarsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartsAsZerosOfTheRequestedLength()
    {
        Assert.Equal(16, new AudioBars().Levels.Count);
        var bars = new AudioBars(5);
        Assert.Equal(5, bars.Levels.Count);
        Assert.All(bars.Levels, l => Assert.Equal(0f, l));
    }

    [Fact]
    public void Push_KeepsLengthAndShiftsNewestToTheRight()
    {
        var bars = new AudioBars(4);
        bars.Push(0.25f, T0);
        Assert.Equal(4, bars.Levels.Count);
        Assert.Equal(0.5f, bars.Levels[3], 5);
        Assert.Equal(0f, bars.Levels[2]);
        bars.Push(1f, T0);
        Assert.Equal(0.5f, bars.Levels[2], 5);
        Assert.Equal(1f, bars.Levels[3], 5);
    }

    [Fact]
    public void Attack_TakesTheHigherValueImmediately()
    {
        var bars = new AudioBars(3);
        bars.Push(0.1f, T0);
        bars.Push(0.64f, T0);
        Assert.Equal(0.8f, bars.Levels[2], 5);
    }

    [Fact]
    public void Decay_IsPreviousTimesPoint82_ThenSquareRooted()
    {
        var bars = new AudioBars(3);
        bars.Push(1f, T0);
        bars.Push(0f, T0);
        Assert.Equal(MathF.Sqrt(0.82f), bars.Levels[2], 5);
        bars.Push(0f, T0);
        Assert.Equal(MathF.Sqrt(0.82f * 0.82f), bars.Levels[2], 5);
    }

    [Fact]
    public void Decay_WithALowerNonZeroPeak_StillDecaysFromPrevious()
    {
        var bars = new AudioBars(3);
        bars.Push(1f, T0);
        bars.Push(0.2f, T0);
        Assert.Equal(MathF.Sqrt(0.82f), bars.Levels[2], 5);
    }

    [Fact]
    public void NaN_CountsAsZero_AndOutOfRangeIsClamped()
    {
        var bars = new AudioBars(3);
        bars.Push(float.NaN, T0);
        Assert.Equal(0f, bars.Levels[2]);
        bars.Push(7f, T0);
        Assert.Equal(1f, bars.Levels[2], 5);
        bars.Push(-3f, T0);
        Assert.InRange(bars.Levels[2], 0f, 1f);
    }

    [Fact]
    public void Idle_AfterFiveSecondsOfQuiet()
    {
        var bars = new AudioBars();
        bars.Push(0.001f, T0);
        Assert.False(bars.IsIdle(T0.AddSeconds(4.9)));
        Assert.True(bars.IsIdle(T0.AddSeconds(5)));
    }

    [Fact]
    public void Idle_ARealPeakResetsTheClock_AndRecoveryIsImmediate()
    {
        var bars = new AudioBars();
        bars.Push(0f, T0);
        Assert.True(bars.IsIdle(T0.AddSeconds(6)));
        bars.Push(0.2f, T0.AddSeconds(6));
        Assert.False(bars.IsIdle(T0.AddSeconds(6)));
        bars.Push(0f, T0.AddSeconds(7));
        Assert.False(bars.IsIdle(T0.AddSeconds(11.9)));
        Assert.True(bars.IsIdle(T0.AddSeconds(12)));
    }

    [Fact]
    public void Idle_PeakAtTheThresholdCountsAsSound()
    {
        var bars = new AudioBars();
        bars.Push(0.004f, T0);
        Assert.False(bars.IsIdle(T0.AddSeconds(60)));
    }

    [Fact]
    public void Idle_BeforeAnyPush_IsTrue() => Assert.True(new AudioBars().IsIdle(T0));

    [Fact]
    public void Reset_ZeroesLevelsAndGoesIdle()
    {
        var bars = new AudioBars(3);
        bars.Push(1f, T0);
        bars.Reset();
        Assert.All(bars.Levels, l => Assert.Equal(0f, l));
        Assert.True(bars.IsIdle(T0));
        bars.Push(0f, T0);
        Assert.Equal(0f, bars.Levels[2]);
    }

    [Fact]
    public void FrameInterval_IsFiftyMilliseconds() =>
        Assert.Equal(TimeSpan.FromMilliseconds(50), AudioBars.FrameInterval);
}
