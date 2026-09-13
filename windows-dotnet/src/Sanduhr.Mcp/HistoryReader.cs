using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

/// <summary>The latest recorded point for one tier in a history file.</summary>
public sealed record HistoryTierPoint(string Key, DateTimeOffset At, int Utilization, string? ResetsAt);

/// <summary>The freshest per-tier points of ONE account's history file.
/// <c>AccountRef</c> is the same short SHA-256 prefix the snapshot carries —
/// the raw label from the file name never leaves this type's constructor.</summary>
public sealed record HistoryLatest(string? AccountRef, DateTimeOffset LatestAt, IReadOnlyList<HistoryTierPoint> Tiers);

/// <summary>
/// Read-only fallback source for issue #63: the widget appends every fetch to
/// <c>%APPDATA%\Sanduhr\history.&lt;account&gt;.json</c> regardless of version,
/// while <c>snapshot.json</c> exists only from 3.4.0 on. A fresh history point
/// next to a missing or dead snapshot is the fingerprint of a widget that polls
/// but has no snapshot writer. Schema (Core's UsageHistory, mirrored literally —
/// this project cannot link that file): <c>{ "&lt;tier&gt;": [ { "t": iso, "v": int,
/// "resets_at"?: iso } ] }</c>. Nothing else is recorded there: no used/limit,
/// no plan. Same read discipline as SnapshotReader (shared open, parse failure =
/// skip the file, never throw).
/// </summary>
public static class HistoryReader
{
    private const string Prefix = "history.";
    private const string Suffix = ".json";

    /// <summary>Pick the account history to serve: among files whose latest
    /// point is within <paramref name="freshWindowSeconds"/> of <paramref name="now"/>,
    /// the one whose hashed label equals <paramref name="preferAccountRef"/>
    /// when there is one, else the freshest of them; when none is fresh, the
    /// freshest overall (the caller decides what "not fresh" means). Null when
    /// no readable history exists.</summary>
    public static HistoryLatest? ReadFreshest(string? dir, string? preferAccountRef, DateTimeOffset now, double freshWindowSeconds)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;

        string[] files;
        try { files = Directory.GetFiles(dir, Prefix + "*" + Suffix); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        HistoryLatest? best = null;
        HistoryLatest? preferred = null;
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (ReadOne(file) is not { } latest)
                continue;
            bool fresh = (now - latest.LatestAt).TotalSeconds <= freshWindowSeconds;
            if (fresh && preferAccountRef is not null && latest.AccountRef == preferAccountRef)
                preferred = preferred is null || latest.LatestAt > preferred.LatestAt ? latest : preferred;
            if (best is null || latest.LatestAt > best.LatestAt)
                best = latest;
        }
        return preferred ?? best;
    }

    private static HistoryLatest? ReadOne(string path)
    {
        JsonObject root;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            if (JsonNode.Parse(sr.ReadToEnd()) is not JsonObject obj)
                return null;
            root = obj;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var tiers = new List<HistoryTierPoint>();
        DateTimeOffset? latestAt = null;
        foreach (var (key, node) in root)
        {
            if (string.IsNullOrEmpty(key) || node is not JsonArray series)
                continue;
            // Series are appended chronologically; scan from the end for the
            // last point whose timestamp parses (a torn tail must not hide the
            // point before it).
            for (int i = series.Count - 1; i >= 0; i--)
            {
                if (series[i] is not JsonObject p || Pacing.Parse((string?)p["t"]) is not { } at || TryInt(p["v"]) is not { } v)
                    continue;
                tiers.Add(new HistoryTierPoint(key, at, v, (string?)p["resets_at"]));
                if (latestAt is null || at > latestAt)
                    latestAt = at;
                break;
            }
        }
        if (latestAt is null)
            return null;
        return new HistoryLatest(SnapshotContract.AccountRef(LabelFromFileName(path)), latestAt.Value, tiers);
    }

    /// <summary><c>history.Este.json</c> → <c>Este</c>; the legacy unlabeled
    /// <c>history.json</c> → null (no account to hash).</summary>
    public static string? LabelFromFileName(string path)
    {
        string name = Path.GetFileName(path);
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            || name.Length <= Prefix.Length + Suffix.Length)
            return null;
        return name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length);
    }

    private static int? TryInt(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out int i)) return i;
        if (v.TryGetValue<double>(out double d)) return (int)d;
        return null;
    }
}
