using System.Security.Cryptography;
using System.Text;

namespace Sanduhr.Core;

/// <summary>Which staleness band a snapshot's age falls in — the shared
/// vocabulary between the widget (writer), the statusline script, and any
/// future MCP consumer. Bands per the WS-E design: fresh &lt; 1.5× the fetch
/// cadence, stale up to two missed polls, dead beyond that.</summary>
public enum SnapshotBand
{
    /// <summary>Age &lt; 7.5 min — render numbers plainly.</summary>
    Fresh,
    /// <summary>7.5–15 min — numbers still render, with an age suffix.</summary>
    Stale,
    /// <summary>&gt; 15 min (two missed polls) — the widget isn't polling;
    /// consumers must say so, never render the numbers as current.</summary>
    Dead,
}

/// <summary>
/// The snapshot.json contract shared by the writer (widget) and every read-only
/// consumer (statusline script now, MCP server later). One place owns the
/// schema version, the staleness thresholds, and the account-ref hashing rule
/// so the script/server can never drift from the writer silently — the
/// statusline script embeds these same constants and a test pins them together.
/// Spec: docs/superpowers/specs/2026-07-12-statusline-mcp-design.md.
/// </summary>
public static class SnapshotContract
{
    /// <summary>Schema major version. Readers seeing a HIGHER major must refuse
    /// (render "update"), never best-effort parse — a lenient stale reader
    /// silently returning wrong numbers is the failure mode the spec names.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Fresh/stale boundary: 1.5× the widget's 5-minute fetch cadence.</summary>
    public const int FreshSeconds = 450;

    /// <summary>Stale/dead boundary: two missed polls.</summary>
    public const int DeadSeconds = 900;

    /// <summary>The error_kind vocabulary (closed set) written when a fetch
    /// throws — maps 1:1 to the widget's typed exception catches.</summary>
    public const string ErrorSessionExpired = "session_expired";
    public const string ErrorCloudflare = "cloudflare";
    public const string ErrorNetwork = "network";

    /// <summary>Band for a snapshot captured at <paramref name="capturedAt"/>
    /// observed at <paramref name="now"/>. Negative ages (clock skew) clamp to
    /// zero — a snapshot from the "future" is fresh, not broken.</summary>
    public static SnapshotBand Band(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        double age = AgeSeconds(capturedAt, now);
        if (age < FreshSeconds) return SnapshotBand.Fresh;
        if (age <= DeadSeconds) return SnapshotBand.Stale;
        return SnapshotBand.Dead;
    }

    /// <summary>Age in whole seconds, clamped at zero.</summary>
    public static double AgeSeconds(DateTimeOffset capturedAt, DateTimeOffset now)
        => Math.Max(0, (now - capturedAt).TotalSeconds);

    /// <summary>Privacy rule: the snapshot never carries a raw account label —
    /// only this short SHA-256 prefix, sufficient for switch-detection and
    /// delete-targeting (PRIVACY.md's log promise extends to snapshot.json).
    /// Null/empty label → null ref.</summary>
    public static string? AccountRef(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return null;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(label));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
