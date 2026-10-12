using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class TelemetryTests
{
    [Fact]
    public void Cpu_IsBusyShareOfTheDeltas_KernelIncludesIdle()
    {
        var prev = new CpuTimes(1000, 2000, 500);
        // dIdle 600, dKernel 800, dUser 200: busy = (800-600)+200 = 400 of 1000.
        var next = new CpuTimes(1600, 2800, 700);
        Assert.Equal(40.0, Telemetry.CpuPercent(prev, next), 6);
    }

    [Fact]
    public void Cpu_FullyIdle_IsZero() =>
        Assert.Equal(0.0, Telemetry.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(100, 100, 0)), 6);

    [Fact]
    public void Cpu_FullyBusy_IsHundred() =>
        Assert.Equal(100.0, Telemetry.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(0, 50, 50)), 6);

    [Fact]
    public void Cpu_ZeroTotal_IsZero()
    {
        var t = new CpuTimes(5, 10, 5);
        Assert.Equal(0.0, Telemetry.CpuPercent(t, t));
    }

    [Fact]
    public void Cpu_NegativeTotal_IsZero() =>
        Assert.Equal(0.0, Telemetry.CpuPercent(new CpuTimes(100, 200, 100), new CpuTimes(100, 150, 100)));

    [Fact]
    public void Cpu_IdleAheadOfKernel_ClampsToZero() =>
        Assert.Equal(0.0, Telemetry.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(500, 100, 0)));

    [Fact]
    public void Cpu_HandlesLargeTicks()
    {
        ulong big = 10_000_000_000_000UL;
        double p = Telemetry.CpuPercent(new CpuTimes(big, big * 2, big), new CpuTimes(big + 50, big * 2 + 100, big));
        Assert.Equal(50.0, p, 6);
    }

    [Theory]
    [InlineData(0u, 0.0)]
    [InlineData(42u, 42.0)]
    [InlineData(100u, 100.0)]
    [InlineData(250u, 100.0)]
    public void Memory_IsClamped(uint load, double expected) =>
        Assert.Equal(expected, Telemetry.MemoryPercent(load));

    [Fact]
    public void Disk_IsUsedShare() =>
        Assert.Equal(75.0, Telemetry.DiskUsedPercent(1000, 250), 6);

    [Fact]
    public void Disk_ZeroTotal_IsZero() =>
        Assert.Equal(0.0, Telemetry.DiskUsedPercent(0, 0));

    [Fact]
    public void Disk_FreeAboveTotal_IsZero() =>
        Assert.Equal(0.0, Telemetry.DiskUsedPercent(100, 500));

    [Fact]
    public void Rows_AreCpuRamDiskInOrder_RoundedHalfAwayFromZero()
    {
        var rows = Telemetry.Rows(new TelemetrySample(12.5, 49.4, 100, DateTimeOffset.UnixEpoch));
        Assert.Equal(["CPU", "RAM", "Disk"], rows.Select(r => r.Label).ToArray());
        Assert.Equal(["CPU", "RAM", "Disk"], rows.Select(r => r.Key).ToArray());
        Assert.Equal(13, rows[0].Percent);
        Assert.Equal(0.13, rows[0].Fill, 9);
        Assert.Equal(49, rows[1].Percent);
        Assert.Equal(100, rows[2].Percent);
        Assert.Equal(1.0, rows[2].Fill, 9);
    }

    [Fact]
    public void Rows_ClampAndTreatNaNAsZero()
    {
        var rows = Telemetry.Rows(new TelemetrySample(double.NaN, 140, -5, DateTimeOffset.UnixEpoch));
        Assert.Equal([0, 100, 0], rows.Select(r => r.Percent).ToArray());
    }

    [Fact]
    public void Interval_IsTwoSeconds() => Assert.Equal(TimeSpan.FromSeconds(2), Telemetry.Interval);
}
