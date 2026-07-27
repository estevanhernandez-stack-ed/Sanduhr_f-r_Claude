using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The one writer of <c>%APPDATA%\Sanduhr\snapshot.json</c> — the rendezvous
/// file the Claude Code statusline (and a future MCP server) reads. Raw facts
/// only: no precomputed pace or countdowns (they decay with wall clock and are
/// wrong by read time; readers derive age and resets_in at read time).
///
/// Write discipline (spec: WS-E): serialize to a sibling <c>.tmp</c> then
/// atomically swap, 3 retries on IOException, failures logged via the
/// best-effort callback (operation + exception type only — no labels, no
/// contents, the WS-A LogBestEffortFailure convention). Never a direct
/// <c>File.WriteAllText</c> on the live path: atomicity is what makes a
/// malformed snapshot always-a-bug for readers, never a race.
/// </summary>
public sealed class SnapshotWriter
{
    private readonly string _path;
    private readonly Action<string>? _logBestEffort;

    /// <param name="snapshotPath">Full path of snapshot.json (injected for tests).</param>
    /// <param name="logBestEffort">Optional failure logger — receives one line
    /// per failed operation, never account labels or file contents.</param>
    public SnapshotWriter(string snapshotPath, Action<string>? logBestEffort = null)
    {
        _path = snapshotPath;
        _logBestEffort = logBestEffort;
    }

    /// <summary>Write a status=ok snapshot from a successful fetch payload.
    /// Tiers = every effective tier present in the payload (utilization may be
    /// null for count-based rows like routines, which carry used/limit).</summary>
    public void WriteOk(JsonObject payload, string? plan, string? accountLabel, DateTimeOffset now)
    {
        var root = BuildCommon(plan, accountLabel, now);
        root["status"] = "ok";
        root["error_kind"] = null;
        root["tiers"] = BuildTiers(payload);
        WriteAtomic(root);
    }

    /// <summary>Write a status=error snapshot when the fetch throws, keeping the
    /// last-good tiers from the existing file (if any parse) so "stale" stays
    /// actionable ("reauth needed"), not "is the widget running?". The plan and
    /// account_ref are also carried from the last-good file when present.</summary>
    public void WriteError(string errorKind, string? plan, string? accountLabel, DateTimeOffset now)
    {
        JsonArray lastGoodTiers = new();
        var existing = ReadExisting();
        if (existing?["tiers"] is JsonArray tiers)
            lastGoodTiers = (JsonArray)tiers.DeepClone();

        var root = BuildCommon(plan, accountLabel, now);
        root["status"] = "error";
        root["error_kind"] = errorKind;
        root["tiers"] = lastGoodTiers;
        WriteAtomic(root);
    }

    /// <summary>Delete the snapshot (toggle-off, account switch, sign-out).
    /// Best-effort; never throws into the caller's flow.</summary>
    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"snapshot delete failed ({e.GetType().Name})");
        }
    }

    private static JsonObject BuildCommon(string? plan, string? accountLabel, DateTimeOffset now)
        => new()
        {
            ["schema_version"] = SnapshotContract.SchemaVersion,
            ["writer_version"] = typeof(SnapshotWriter).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            // UsageHistory.NowIso precedent: 6-digit fractional seconds + explicit offset.
            ["captured_at"] = now.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture),
            ["account_ref"] = SnapshotContract.AccountRef(accountLabel),
            ["plan"] = plan,
        };

    private static JsonArray BuildTiers(JsonObject payload)
    {
        var tiers = new JsonArray();
        foreach (var key in TierModel.EffectiveOrder)
        {
            if (payload[key] is not JsonObject tier)
                continue;
            // A tier is snapshot-worthy when it has either a utilization or a
            // used/limit pair (routines) — mirrors the card render filter.
            bool hasUtil = tier["utilization"] is not null;
            bool hasCount = tier["used"] is not null && tier["limit"] is not null;
            if (!hasUtil && !hasCount)
                continue;

            tiers.Add(new JsonObject
            {
                ["key"] = key,
                ["utilization"] = TryInt(tier["utilization"]),
                ["resets_at"] = tier["resets_at"]?.DeepClone(),
                ["used"] = TryInt(tier["used"]),
                ["limit"] = TryInt(tier["limit"]),
            });
        }
        return tiers;
    }

    /// <summary>Numeric coercion across JsonValue backings — parsed payloads are
    /// element-backed (any number reads as double), test/synthesized payloads
    /// are CLR-backed (an int refuses GetValue&lt;double&gt;). Accept both.</summary>
    private static int? TryInt(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out int i))
            return i;
        if (v.TryGetValue<double>(out double d))
            return (int)d;
        return null;
    }

    private JsonObject? ReadExisting()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // malformed/unreadable = no last-good to carry
        }
    }

    private void WriteAtomic(JsonObject root)
    {
        string tmp = _path + ".tmp";
        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2)
                {
                    _logBestEffort?.Invoke($"snapshot write failed ({e.GetType().Name})");
                    TryCleanupTmp(tmp);
                }
            }
        }
    }

    private static void TryCleanupTmp(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); }
        catch { /* best-effort */ }
    }
}
