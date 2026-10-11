using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The pure rules behind per-seat fetching (port of the rule half of the Mac's SeatMeters.swift and
/// SeatSnapshots.swift): which accounts may be fetched for a seat, when one was read too recently,
/// when a seat needs a first reading, and whether Claude Code is working in a folder (file times
/// only, no log is opened).
/// </summary>
public static class SeatRules
{
    /// <summary>Time between two seat fetches in one round.</summary>
    public static readonly TimeSpan Stagger = TimeSpan.FromSeconds(5);

    /// <summary>A session log written this recently means Claude Code is working in the folder.</summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(10);

    /// <summary>A seat is fetched for its meters while linked, signed in and not the active account.</summary>
    public static bool Fetchable(string label, IReadOnlyDictionary<string, string> linked, IReadOnlySet<string> signedIn, string? active)
        => linked.ContainsKey(label) && signedIn.Contains(label) && label != active;

    /// <summary>Read less than 90% of the interval ago: a tenth absorbs timer slop and the requests' own time.</summary>
    public static bool Throttled(DateTimeOffset? lastFetch, DateTimeOffset now, TimeSpan interval)
        => lastFetch is { } last && now - last < TimeSpan.FromTicks((long)(interval.Ticks * 0.9));

    /// <summary>The seat's file is missing or at least one interval old.</summary>
    public static bool NeedsReading(DateTimeOffset? capturedAt, DateTimeOffset now, TimeSpan interval)
        => capturedAt is not { } at || now - at >= interval;

    /// <summary>
    /// Whether any session log under <c>&lt;folder&gt;\projects</c> was modified within
    /// <paramref name="window"/> of <paramref name="now"/> (default <see cref="ActivityWindow"/>).
    /// File times only. A missing folder, or any unreadable entry, reads as no activity.
    /// </summary>
    public static bool RecentActivity(string folder, DateTime nowUtc, TimeSpan? window = null)
    {
        var within = window ?? ActivityWindow;
        try
        {
            var projects = Path.Combine(folder, "projects");
            if (!Directory.Exists(projects)) return false;
            foreach (var file in Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    if (nowUtc - File.GetLastWriteTimeUtc(file) <= within) return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* next file */ }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        return false;
    }

    /// <summary>The five-hour session meter's utilization in a usage payload; null when absent.</summary>
    public static double? SessionUtilization(JsonObject? usage)
    {
        if (usage?[TierModel.FiveHour] is not JsonObject tier || tier["utilization"] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    /// <summary>The snapshot error kind a failed fetch writes: an expired key, a Cloudflare challenge, or the network.</summary>
    public static string ErrorKind(Exception e) => e switch
    {
        SessionExpiredException => SnapshotContract.ErrorSessionExpired,
        CloudflareBlockedException => SnapshotContract.ErrorCloudflare,
        _ => SnapshotContract.ErrorNetwork,
    };

    /// <summary>A refused key or a challenge: the account is skipped until a different key is saved.</summary>
    public static bool IsAuthKind(string errorKind) => errorKind != SnapshotContract.ErrorNetwork;
}
