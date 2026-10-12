namespace Sanduhr.Core;

/// <summary>
/// One reading of the three <c>GetSystemTimes</c> counters, in FILETIME ticks. Kernel time
/// INCLUDES idle time, as the API reports it. New for Windows; the Mac has no Desk telemetry.
/// </summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>One sample of the three telemetry numbers, each 0 to 100.</summary>
public sealed record TelemetrySample(double Cpu, double Ram, double Disk, DateTimeOffset At);

/// <summary>One drawn row of the telemetry piece: key, label, whole percent and bar fill 0 to 1.</summary>
public sealed record TelemetryRow(string Key, string Label, int Percent, double Fill);

/// <summary>
/// The pure math behind the Desk telemetry piece (spec 6.4): CPU from two system-time readings,
/// RAM from the memory-load percent, disk as the used share of the system drive, and the rows
/// the piece draws. The App reads the counters; nothing here touches the OS.
/// </summary>
public static class Telemetry
{
    /// <summary>How often the App samples: five seconds. Each reading repaints the telemetry island, and a
    /// per-pixel-transparent window repaints in software (measured about 2% of one core at two seconds),
    /// so the Desk stays near zero idle; well inside the at-most-once-a-second ruling.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// CPU busy share between two readings: busy = (dKernel - dIdle) + dUser over
    /// total = dKernel + dUser. Zero for a zero or negative total (a counter that did not move or went
    /// backwards); clamped to 0..100.
    /// </summary>
    public static double CpuPercent(CpuTimes prev, CpuTimes next)
    {
        double dIdle = (double)next.Idle - prev.Idle;
        double dKernel = (double)next.Kernel - prev.Kernel;
        double dUser = (double)next.User - prev.User;
        double total = dKernel + dUser;
        if (total <= 0) return 0;
        double busy = (dKernel - dIdle) + dUser;
        return Math.Clamp(busy / total * 100.0, 0, 100);
    }

    /// <summary>RAM percent from <c>dwMemoryLoad</c>, clamped to 0..100.</summary>
    public static double MemoryPercent(uint memoryLoad) => Math.Clamp((double)memoryLoad, 0, 100);

    /// <summary>Disk used share from total and free bytes; 0 when the total is 0.</summary>
    public static double DiskUsedPercent(ulong total, ulong free)
    {
        if (total == 0) return 0;
        double used = free >= total ? 0 : (double)(total - free);
        return Math.Clamp(used / total * 100.0, 0, 100);
    }

    /// <summary>The rows in draw order: CPU, RAM, Disk. Percent rounds half away from zero; fill is percent / 100.</summary>
    public static IReadOnlyList<TelemetryRow> Rows(TelemetrySample sample) =>
    [
        Row("CPU", "CPU", sample.Cpu),
        Row("RAM", "RAM", sample.Ram),
        Row("Disk", "Disk", sample.Disk),
    ];

    private static TelemetryRow Row(string key, string label, double value)
    {
        double safe = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
        int percent = (int)Math.Round(safe, MidpointRounding.AwayFromZero);
        return new TelemetryRow(key, label, percent, percent / 100.0);
    }
}
