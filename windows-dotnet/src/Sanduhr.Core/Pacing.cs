namespace Sanduhr.Core;

/// <summary>
/// Pure pacing + projection math, ported 1:1 from the Python build's
/// <c>pacing.py</c>. No UI, no IO. This is the highest-fidelity parity port
/// and the proof of the C# test harness. The optional <c>now</c> parameter
/// (default: UtcNow) exists only to make the time-dependent functions
/// deterministically testable; behaviour matches the Python original when
/// omitted.
/// </summary>
public static class Pacing
{
    private const long FiveHourSecs = 5 * 3600;
    private const long SevenDaySecs = 7 * 86400;

    /// <summary>Parse an ISO-8601 instant; null on bad/empty input (matches Python _parse).</summary>
    public static DateTimeOffset? Parse(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        return DateTimeOffset.TryParse(
            iso, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;
    }

    private static long TierTotalSecs(string tierKey) =>
        tierKey == "five_hour" ? FiveHourSecs : SevenDaySecs;

    private static DateTimeOffset Now(DateTimeOffset? now) => now ?? DateTimeOffset.UtcNow;

    private static string FormatDuration(long secs)
    {
        long d = secs / 86400;
        long r = secs % 86400;
        long h = r / 3600;
        r %= 3600;
        long m = r / 60;
        var parts = new List<string>();
        if (d > 0) parts.Add($"{d}d");
        if (h > 0) parts.Add($"{h}h");
        if (m > 0 || parts.Count == 0) parts.Add($"{m}m");
        return string.Join(" ", parts);
    }

    /// <summary>Human countdown like "3d 5h 30m" / "45m" / "now" / "--".</summary>
    public static string TimeUntil(string? resetsAt, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(resetsAt)) return "--";
        var rd = Parse(resetsAt);
        if (rd is null) return "--";
        long secs = (long)Math.Max(0, (rd.Value - Now(now)).TotalSeconds);
        return secs <= 0 ? "now" : FormatDuration(secs);
    }

    /// <summary>How far through the period we are, in [0,1]. Null on bad input.</summary>
    public static double? PaceFrac(string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var rd = Parse(resetsAt);
        if (rd is null) return null;
        double rem = Math.Max(0, (rd.Value - Now(now)).TotalSeconds);
        long total = TierTotalSecs(tierKey);
        if (total <= 0) return null;
        return Math.Min(1.0, Math.Max(0.0, (total - rem) / total));
    }

    /// <summary>(label, color) describing on / ahead / under pace. Null on bad input.</summary>
    public static (string Label, string Color)? PaceInfo(
        double? util, string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var frac = PaceFrac(resetsAt, tierKey, now);
        if (frac is null || util is null) return null;
        double diff = util.Value - frac.Value * 100;
        if (Math.Abs(diff) < 5) return ("On pace", "#4ade80");
        if (diff > 0) return ($"{Math.Round(Math.Abs(diff))}% ahead", "#fb923c");
        return ($"{Math.Round(Math.Abs(diff))}% under", "#60a5fa");
    }

    /// <summary>"45m" worth of cooldown if ahead of pace, else null.</summary>
    public static string? CalculateCooldown(
        double? util, string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var frac = PaceFrac(resetsAt, tierKey, now);
        if (frac is null || util is null) return null;
        double waitFrac = util.Value / 100.0 - frac.Value;
        if (waitFrac <= 0) return null;
        long secs = (long)(waitFrac * TierTotalSecs(tierKey));
        return FormatDuration(secs);
    }

    /// <summary>Surplus integer percentage if under pace, else null.</summary>
    public static int? CalculateSurplus(
        double? util, string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var frac = PaceFrac(resetsAt, tierKey, now);
        if (frac is null || util is null) return null;
        double surplus = frac.Value * 100.0 - util.Value;
        if (surplus <= 0) return null;
        return (int)surplus;
    }

    /// <summary>(message, color) when current pace will hit 100% before reset, else null.</summary>
    public static (string Message, string Color)? BurnProjection(
        double? util, string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var frac = PaceFrac(resetsAt, tierKey, now);
        if (frac is null || util is null || util <= 0 || frac <= 0) return null;
        double ratePerFrac = util.Value / frac.Value;
        if (ratePerFrac <= 100) return null;

        var rd = Parse(resetsAt);
        if (rd is null) return null;
        double secsUntilReset = Math.Max(0, (rd.Value - Now(now)).TotalSeconds);
        long total = TierTotalSecs(tierKey);

        double fracAt100 = 100 / ratePerFrac;
        double secsUntil100 = Math.Max(0, (fracAt100 - frac.Value) * total);

        if (secsUntil100 <= 0) return ("Limit reached", "#f87171");
        if (secsUntil100 >= secsUntilReset) return null;
        return ($"At current pace, expires in {FormatDuration((long)secsUntil100)}", "#f87171");
    }

    /// <summary>Projected final utilization at reset if momentum continues, capped [0,200]. Null if insufficient.</summary>
    public static double? VelocityProjection(
        double? util, string? resetsAt, string tierKey, DateTimeOffset? now = null)
    {
        var frac = PaceFrac(resetsAt, tierKey, now);
        if (frac is null || util is null || util <= 0 || frac <= 0.01) return null;
        return Math.Min(200.0, util.Value / frac.Value);
    }
}
