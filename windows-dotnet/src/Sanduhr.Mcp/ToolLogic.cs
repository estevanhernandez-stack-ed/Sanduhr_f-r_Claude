using System.Reflection;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Mcp;

/// <summary>
/// The three tool responses, built as data (the protocol layer serializes).
/// Every failure is a typed result with <c>status</c>/<c>reason</c>/<c>remedy</c>
/// — never an MCP protocol error (a protocol error reads as "server broken" and
/// poisons the healthy tools; design review must-fix #4). The server computes
/// <c>age_seconds</c> and <c>resets_in_seconds</c> — the agent never does clock
/// math. Reset-crossing is checked before serving any tier: a fresh-by-age
/// snapshot is arbitrarily wrong across a boundary, and five_hour crosses daily.
/// </summary>
public sealed class ToolLogic
{
    private readonly McpConfig _config;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CcLogReader _reader = new();

    public const string DataLagNote = "claude.ai's own numbers lag consumption by several minutes";
    public const string BurnCaveat =
        "token-count proxy from local Claude Code logs (input+output only, cache excluded); " +
        "NOT convertible to utilization %. CC deletes session logs after ~30 days; totals are lower bounds.";

    public static string ServerVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public ToolLogic(McpConfig config, Func<DateTimeOffset>? clock = null)
    {
        _config = config;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // -- ping -----------------------------------------------------------------

    public JsonObject BuildPing()
    {
        var (outcome, snap) = SnapshotReader.Read(_config.SnapshotPath);
        double? age = null;
        if (outcome == SnapshotReadOutcome.Ok
            && Pacing.Parse((string?)snap!["captured_at"]) is { } captured)
        {
            age = Math.Round(SnapshotContract.AgeSeconds(captured, _clock()));
        }
        return new JsonObject
        {
            ["server_version"] = ServerVersion,
            ["snapshot_schema_supported"] = SnapshotContract.SchemaVersion,
            ["snapshot_path"] = _config.SnapshotPath,
            ["snapshot_found"] = outcome != SnapshotReadOutcome.Missing,
            ["snapshot_age_seconds"] = age,
            ["cc_roots_found"] = ToArray(_config.RootsFound),
            ["cc_roots_consented"] = ToArray(_config.ConsentedRoots.Select(r => r.Name)),
        };
    }

    // -- get_usage ------------------------------------------------------------

    public JsonObject BuildUsage()
    {
        var now = _clock();
        var (outcome, snap) = SnapshotReader.Read(_config.SnapshotPath);

        if (outcome == SnapshotReadOutcome.Missing)
            return NoData("missing",
                "No usage snapshot. Start the Sanduhr widget and enable its Claude Code integration (Settings > Claude Usage).");
        if (outcome == SnapshotReadOutcome.Malformed)
            return NoData("malformed",
                "Usage snapshot unreadable. Restart the Sanduhr widget; if it persists, delete %APPDATA%\\Sanduhr\\snapshot.json.");

        int schemaVersion;
        try { schemaVersion = (int)(snap!["schema_version"]?.GetValue<double>() ?? snap["schema_version"]!.GetValue<int>()); }
        catch { try { schemaVersion = snap!["schema_version"]!.GetValue<int>(); } catch { return NoData("malformed", "snapshot carries no schema_version"); } }
        if (schemaVersion > SnapshotContract.SchemaVersion)
            return NoData("schema_unsupported",
                $"Snapshot schema v{schemaVersion} is newer than this server supports (v{SnapshotContract.SchemaVersion}). Update Sanduhr.");

        if (Pacing.Parse((string?)snap!["captured_at"]) is not { } captured)
            return NoData("malformed", "snapshot captured_at is unparseable");

        double ageSeconds = Math.Round(SnapshotContract.AgeSeconds(captured, now));
        var band = SnapshotContract.Band(captured, now);
        string? fileStatus = (string?)snap["status"];
        string? errorKind = (string?)snap["error_kind"];

        string status = band == SnapshotBand.Fresh && fileStatus != "error" ? "ok" : "stale";
        string? reason = band == SnapshotBand.Dead ? "widget_not_polling" : null;
        string? remedy = band == SnapshotBand.Dead
            ? "The Sanduhr widget has not polled for over 15 minutes - start (or restart) the widget."
            : fileStatus == "error"
                ? errorKind switch
                {
                    SnapshotContract.ErrorSessionExpired => "The widget's claude.ai session expired - re-authenticate in the Sanduhr widget. Tiers below are last-good, not current.",
                    SnapshotContract.ErrorCloudflare => "The widget is blocked by a Cloudflare challenge - re-authenticate in the Sanduhr widget. Tiers below are last-good, not current.",
                    _ => "The widget's last fetch failed (network). Tiers below are last-good, not current.",
                }
                : null;

        var tiers = new JsonArray();
        foreach (var node in snap["tiers"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject t || (string?)t["key"] is not { Length: > 0 } key)
                continue;
            tiers.Add(BuildTier(key, t, now));
        }

        var result = new JsonObject
        {
            ["status"] = status,
            ["reason"] = reason,
            ["remedy"] = remedy,
            ["fetch_error"] = fileStatus == "error" ? errorKind : null,
            ["as_of"] = (string?)snap["captured_at"],
            ["age_seconds"] = ageSeconds,
            ["scope"] = "active_account_only",
            ["account"] = new JsonObject
            {
                ["ref"] = (string?)snap["account_ref"],
                ["plan"] = (string?)snap["plan"],
            },
            ["data_lag_note"] = DataLagNote,
            ["schema_version"] = SnapshotContract.SchemaVersion,
            ["tiers"] = tiers,
            ["local_burn_since_snapshot"] = BuildLocalBurnSince(captured),
        };
        return result;
    }

    private JsonObject BuildTier(string key, JsonObject t, DateTimeOffset now)
    {
        int? util = TryInt(t["utilization"]);
        string? resetsAt = (string?)t["resets_at"];
        var resetInstant = Pacing.Parse(resetsAt);
        bool crossed = resetInstant is { } r && r <= now;

        var tier = new JsonObject
        {
            ["key"] = key,
            ["label"] = TierModel.IsKnown(key) ? TierModel.Label(key) : key,
            ["utilization_pct"] = crossed ? null : util,
            ["headroom_pct"] = crossed || util is null ? null : Math.Max(0, 100 - util.Value),
            ["resets_at"] = resetsAt,
            ["resets_in_seconds"] = resetInstant is { } ri
                ? Math.Round(Math.Max(0, (ri - now).TotalSeconds))
                : null,
            ["reset_crossed"] = crossed,
            ["used"] = TryInt(t["used"]),
            ["limit"] = TryInt(t["limit"]),
        };

        // Pace + projection: nullable by design — routines carries resets_at null
        // ("unknown" must stay distinct from "none"); crossed tiers get neither.
        double? frac = crossed ? null : Pacing.PaceFrac(resetsAt, key, now);
        if (frac is { } f && util is { } u && f > 0)
        {
            double delta = u - f * 100;
            tier["pace"] = new JsonObject
            {
                ["verdict"] = Math.Abs(delta) < 5 ? "on_pace" : delta > 0 ? "ahead" : "under",
                ["delta_pct"] = Math.Round(Math.Abs(delta)),
            };
            // Mirrors Pacing.BurnProjection's formula with raw numbers instead
            // of display strings: at the current rate, does 100% land before reset?
            long totalSecs = key == "five_hour" ? 5L * 3600 : 7L * 86400;
            double ratePerFrac = u / f;
            if (u > 0 && ratePerFrac > 100 && resetInstant is { } rr)
            {
                double secsUntilReset = Math.Max(0, (rr - now).TotalSeconds);
                double secsUntil100 = Math.Max(0, (100 / ratePerFrac - f) * totalSecs);
                bool before = secsUntil100 < secsUntilReset;
                tier["projection"] = new JsonObject
                {
                    ["expires_before_reset"] = before,
                    ["expires_in_seconds"] = before ? Math.Round(secsUntil100) : null,
                };
            }
            else
            {
                tier["projection"] = new JsonObject
                {
                    ["expires_before_reset"] = false,
                    ["expires_in_seconds"] = null,
                };
            }
        }
        else
        {
            tier["pace"] = null;
            tier["projection"] = null;
        }
        return tier;
    }

    /// <summary>Local CC burn since the snapshot was captured — the staleness
    /// compensator for the double lag (endpoint minutes + poll cadence). Scoped
    /// to consented roots only; null when nothing is consented.</summary>
    private JsonObject? BuildLocalBurnSince(DateTimeOffset asOf)
    {
        if (_config.ConsentedRoots.Count == 0)
            return null;
        long total = 0;
        var byTier = new Dictionary<string, long>();
        foreach (var (_, rootPath) in _config.ConsentedRoots)
        {
            foreach (var ev in EventsSince(rootPath, asOf))
            {
                long tokens = ev.Usage.InputTokens + ev.Usage.OutputTokens;
                if (tokens <= 0)
                    continue;
                total += tokens;
                if (CcLogReader.TierForModel(ev.Model) is { } tierKey)
                    byTier[tierKey] = byTier.GetValueOrDefault(tierKey) + tokens;
            }
        }
        var byTierJson = new JsonObject();
        foreach (var (k, v) in byTier.OrderByDescending(p => p.Value))
            byTierJson[k] = v;
        return new JsonObject
        {
            ["total_tokens"] = total,
            ["by_tier"] = byTierJson,
            ["caveat"] = BurnCaveat,
        };
    }

    // -- get_local_burn_by_project --------------------------------------------

    public JsonObject BuildBurn(int windowDays, bool fullPaths)
    {
        if (windowDays is not (1 or 7 or 30))
            return NoData("invalid_params", "window_days must be 1, 7, or 30");
        if (_config.ConsentedRoots.Count == 0)
            return NoData("disabled",
                "No Claude Code homes are consented for MCP reads. Enable them in the Sanduhr widget's settings (mcp_roots).");

        var now = _clock();
        var since = now.AddDays(-windowDays);
        int filesScanned = 0;
        var roots = new JsonArray();

        foreach (var (name, rootPath) in _config.ConsentedRoots)
        {
            long rootTotal = 0;
            var byProject = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var file in LogFilesUnder(rootPath))
            {
                if (!MtimeAfter(file, since))
                    continue;
                filesScanned++;
                foreach (var ev in _reader.IterUsageEvents(file))
                {
                    if (ev.Timestamp is null || ev.Timestamp.Value < since)
                        continue;
                    long tokens = ev.Usage.InputTokens + ev.Usage.OutputTokens;
                    if (tokens <= 0)
                        continue;
                    // Unattributable events stay visible — never silently dropped.
                    string project = ev.Cwd is { Length: > 0 } cwd
                        ? (fullPaths ? cwd : CcLogReader.ProjectDisplayName(cwd))
                        : "(unknown)";
                    rootTotal += tokens;
                    byProject[project] = byProject.GetValueOrDefault(project) + tokens;
                }
            }
            var projects = new JsonArray();
            foreach (var (proj, tokens) in byProject.OrderByDescending(p => p.Value))
                projects.Add(new JsonObject { ["name"] = proj, ["tokens"] = tokens });
            roots.Add(new JsonObject
            {
                ["root"] = name,
                ["total_tokens"] = rootTotal,
                ["projects"] = projects,
            });
        }

        return new JsonObject
        {
            ["status"] = "ok",
            ["reason"] = null,
            ["remedy"] = null,
            ["window_days"] = windowDays,
            ["since"] = since.ToString("o"),
            ["full_paths"] = fullPaths,
            ["roots_scanned"] = ToArray(_config.ConsentedRoots.Select(r => r.Name)),
            ["roots"] = roots,
            ["files_scanned"] = filesScanned,
            ["caveat"] = BurnCaveat,
        };
    }

    // -- helpers --------------------------------------------------------------

    private IEnumerable<UsageEvent> EventsSince(string rootPath, DateTimeOffset since)
    {
        foreach (var file in LogFilesUnder(rootPath))
        {
            if (!MtimeAfter(file, since))
                continue;
            foreach (var ev in _reader.IterUsageEvents(file))
            {
                if (ev.Timestamp is { } ts && ts >= since)
                    yield return ev;
            }
        }
    }

    /// <summary>Session JSONLs under ONE root (per-root keying is the tenant
    /// wall — CcLogReader's own discovery merges homes and must not be used here).</summary>
    private static IEnumerable<string> LogFilesUnder(string rootPath)
    {
        string projects = Path.Combine(rootPath, "projects");
        if (!Directory.Exists(projects))
            yield break;
        foreach (var projectDir in Directory.GetDirectories(projects))
        {
            foreach (var f in Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories))
                yield return f;
        }
    }

    private static bool MtimeAfter(string path, DateTimeOffset cutoff)
    {
        try { return File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime; }
        catch { return true; } // unreadable mtime: scan the file rather than skip data
    }

    private static JsonObject NoData(string reason, string remedy) => new()
    {
        ["status"] = "no_data",
        ["reason"] = reason,
        ["remedy"] = remedy,
    };

    private static int? TryInt(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out int i)) return i;
        if (v.TryGetValue<double>(out double d)) return (int)d;
        return null;
    }

    private static JsonArray ToArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr;
    }
}
