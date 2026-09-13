using System.Globalization;
using System.Reflection;
using System.Text.Json;
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

    /// <summary>The first widget release with a snapshot writer. Older widgets
    /// poll and append history but never touch snapshot.json (issue #63).</summary>
    public const string RequiredWidgetVersion = "3.4.0";
    public const string ReasonWidgetTooOld = "widget_too_old";
    public const string ReasonWidgetNotPolling = "widget_not_polling";
    public const string WidgetTooOldRemedy =
        "The running widget has no snapshot writer (Sanduhr " + RequiredWidgetVersion + " or later is required); update it.";
    public const string HistoryLagNote =
        "Numbers come from the widget's history file (history.<account>.json), not snapshot.json: " +
        "the running widget has no snapshot writer. History records utilization and reset time only; " +
        "used, limit and plan are null because they are not recorded, not because they are zero. " + DataLagNote;

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
        var now = _clock();
        var (outcome, snap) = SnapshotReader.Read(_config.SnapshotPath);
        double? age = null;
        SnapshotBand? band = null;
        if (outcome == SnapshotReadOutcome.Ok
            && Pacing.Parse((string?)snap!["captured_at"]) is { } captured)
        {
            age = Math.Round(SnapshotContract.AgeSeconds(captured, now));
            band = SnapshotContract.Band(captured, now);
        }
        string? writerVersion = outcome == SnapshotReadOutcome.Ok ? (string?)snap!["writer_version"] : null;
        string? accountRef = outcome == SnapshotReadOutcome.Ok ? (string?)snap!["account_ref"] : null;
        var (history, historyAge, historyFresh) = ReadHistory(accountRef, now);

        // The same classification get_usage applies, so ping alone answers
        // "why is get_usage stale" without a second call.
        string status;
        string? reason = null;
        string? remedy = null;
        if (outcome == SnapshotReadOutcome.Missing || band == SnapshotBand.Dead)
        {
            (status, reason, remedy) = DeadOrMissingDiagnosis(
                outcome != SnapshotReadOutcome.Missing, writerVersion, age, historyAge, historyFresh);
        }
        else if (outcome != SnapshotReadOutcome.Ok || band is null)
        {
            (status, reason) = ("no_data", "malformed");
        }
        else
        {
            status = band == SnapshotBand.Fresh && (string?)snap!["status"] != "error" ? "ok" : "stale";
        }

        return new JsonObject
        {
            ["server_version"] = ServerVersion,
            ["required_widget_version"] = RequiredWidgetVersion,
            ["snapshot_schema_supported"] = SnapshotContract.SchemaVersion,
            ["snapshot_path"] = _config.SnapshotPath,
            ["snapshot_found"] = outcome != SnapshotReadOutcome.Missing,
            ["snapshot_age_seconds"] = age,
            // Which build wrote the file. "1.0.0" = a dev build (Core's unversioned
            // assembly); a dead snapshot from a version the installed widget never
            // shipped is "the installed widget predates the writer", not "not polling".
            ["snapshot_writer_version"] = writerVersion,
            ["snapshot_writer_meets_floor"] = outcome == SnapshotReadOutcome.Ok ? WriterMeetsFloor(writerVersion) : null,
            // The fallback source: a fresh history point beside a missing/dead
            // snapshot is the fingerprint of a polling widget with no writer.
            ["history_found"] = history is not null,
            ["history_age_seconds"] = historyAge,
            ["history_fresh"] = historyFresh,
            ["usage_status"] = status,
            ["usage_reason"] = reason,
            ["remedy"] = remedy,
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
        {
            // No snapshot but the widget is polling (history point < 15 min old):
            // that is a pre-3.4.0 widget, not a widget that never ran. Serve the
            // history point, degraded, rather than nothing (issue #63).
            var (history, historyAge, historyFresh) = ReadHistory(null, now);
            if (historyFresh && history is not null)
            {
                var (_, _, remedyText) = DeadOrMissingDiagnosis(false, null, null, historyAge, true);
                return BuildDegraded(history, historyAge!.Value, remedyText!, null, null, now);
            }
            return NoData("missing",
                "No usage snapshot. Start the Sanduhr widget and enable its Claude Code integration (Settings > Claude Usage).");
        }
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
        string? reason = null;
        string? writerVersion = (string?)snap["writer_version"];
        string? remedy = null;
        if (band == SnapshotBand.Dead)
        {
            // Dead snapshot: is the widget dead too, or just too old to write?
            // The history file answers — it is appended by every widget version.
            var (history, historyAge, historyFresh) = ReadHistory((string?)snap["account_ref"], now);
            (status, reason, remedy) = DeadOrMissingDiagnosis(true, writerVersion, ageSeconds, historyAge, historyFresh);
            if (historyFresh && history is not null)
                return BuildDegraded(history, historyAge!.Value, remedy!, writerVersion, ageSeconds, now);
        }
        else if (fileStatus == "error")
        {
            remedy = errorKind switch
            {
                SnapshotContract.ErrorSessionExpired => "The widget's claude.ai session expired - re-authenticate in the Sanduhr widget. Tiers below are last-good, not current.",
                SnapshotContract.ErrorCloudflare => "The widget is blocked by a Cloudflare challenge - re-authenticate in the Sanduhr widget. Tiers below are last-good, not current.",
                _ => "The widget's last fetch failed (network). Tiers below are last-good, not current.",
            };
        }

        var tiers = new JsonArray();
        foreach (var node in snap["tiers"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject t || (string?)t["key"] is not { Length: > 0 } key)
                continue;
            tiers.Add((JsonNode)BuildTier(key, t, now));
        }

        var result = new JsonObject
        {
            ["status"] = status,
            ["reason"] = reason,
            ["remedy"] = remedy,
            ["fetch_error"] = fileStatus == "error" ? errorKind : null,
            ["as_of"] = (string?)snap["captured_at"],
            ["age_seconds"] = ageSeconds,
            ["writer_version"] = writerVersion,
            ["data_source"] = "snapshot",
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

    // -- stale/missing classification (issue #63) -----------------------------

    /// <summary>The widget's history file next to the snapshot, plus its
    /// freshness by the same two-missed-polls rule the snapshot uses.</summary>
    private (HistoryLatest? History, double? AgeSeconds, bool Fresh) ReadHistory(string? preferAccountRef, DateTimeOffset now)
    {
        string? dir = _config.HistoryDir ?? Path.GetDirectoryName(_config.SnapshotPath);
        var history = HistoryReader.ReadFreshest(dir, preferAccountRef, now, SnapshotContract.DeadSeconds);
        if (history is null)
            return (null, null, false);
        double age = Math.Round(SnapshotContract.AgeSeconds(history.LatestAt, now));
        return (history, age, age <= SnapshotContract.DeadSeconds);
    }

    /// <summary>True when the snapshot was written by a release that ships the
    /// writer. Missing, unparseable, or "1.0.0" (Core's unversioned dev
    /// assembly) all fail the floor.</summary>
    public static bool WriterMeetsFloor(string? writerVersion)
        => writerVersion is { Length: > 0 }
           && Version.TryParse(writerVersion, out var v)
           && v >= Version.Parse(RequiredWidgetVersion);

    /// <summary>The one table both tools share for a snapshot that is missing or
    /// dead (older than <see cref="SnapshotContract.DeadSeconds"/>):
    /// <list type="bullet">
    /// <item>history fresh → <c>degraded</c> / <c>widget_too_old</c>: the widget
    /// polls but has no writer; the history point is served.</item>
    /// <item>history not fresh, snapshot written below the floor → <c>stale</c> /
    /// <c>widget_too_old</c>: the file is a leftover the installed widget can never
    /// refresh, so "restart it" would be the wrong remedy.</item>
    /// <item>history not fresh, writer at or above the floor → <c>stale</c> /
    /// <c>widget_not_polling</c>: nothing on this machine has polled in 15 min.</item>
    /// <item>no snapshot, history not fresh → <c>no_data</c> / <c>missing</c>.</item>
    /// </list></summary>
    private static (string Status, string? Reason, string? Remedy) DeadOrMissingDiagnosis(
        bool snapshotPresent, string? writerVersion, double? snapshotAge, double? historyAge, bool historyFresh)
    {
        bool floorOk = WriterMeetsFloor(writerVersion);
        string writer = writerVersion ?? "unknown";
        if (historyFresh)
        {
            string polling = $"The widget is polling (history point {historyAge:0}s old, served below)";
            if (!snapshotPresent)
                return ("degraded", ReasonWidgetTooOld, $"{WidgetTooOldRemedy} {polling}; snapshot.json does not exist.");
            string leftover = floorOk
                ? $"snapshot.json was last written {snapshotAge:0}s ago by build {writer}. If the installed widget "
                  + "is already 3.4.0 or later, re-enable its Claude Code integration (Settings > Claude Usage)."
                : $"snapshot.json was last written {snapshotAge:0}s ago by build {writer}, a dev-build or pre-{RequiredWidgetVersion} leftover.";
            return ("degraded", ReasonWidgetTooOld, $"{WidgetTooOldRemedy} {polling}; {leftover}");
        }
        if (!snapshotPresent)
            return ("no_data", "missing", null);
        if (!floorOk)
            return ("stale", ReasonWidgetTooOld,
                $"snapshot.json was written by build {writer}, a dev-build or pre-{RequiredWidgetVersion} leftover; "
                + $"no released widget below {RequiredWidgetVersion} writes snapshots, so the installed widget cannot refresh it. "
                + $"Update Sanduhr to {RequiredWidgetVersion} or later. No history file has a point in the last 15 minutes "
                + "either, so the widget may also not be running. Tiers below are the leftover's, not current.");
        return ("stale", ReasonWidgetNotPolling,
            "The Sanduhr widget has not polled for over 15 minutes (no snapshot or history point in that window) - "
            + $"start (or restart) the widget. This snapshot was written by widget build {writer}.");
    }

    /// <summary>The <c>degraded</c> get_usage payload: the latest history point
    /// per tier, only tiers polled within the freshness window, through the same
    /// reset-crossing / pace / projection math as the snapshot path. Fields the
    /// history schema does not carry (used, limit, plan) are null, never
    /// invented. The account ref is the hash of the file's label — the label
    /// itself never appears in the payload (PRIVACY.md's snapshot rule).</summary>
    private JsonObject BuildDegraded(HistoryLatest history, double historyAge, string remedy,
        string? snapshotWriterVersion, double? snapshotAge, DateTimeOffset now)
    {
        var order = TierModel.EffectiveOrder.ToList();
        var tiers = new JsonArray();
        foreach (var p in history.Tiers
                     .Where(p => SnapshotContract.AgeSeconds(p.At, now) <= SnapshotContract.DeadSeconds)
                     .OrderBy(p => order.IndexOf(p.Key) is var i && i >= 0 ? i : int.MaxValue)
                     .ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var t = new JsonObject
            {
                ["utilization"] = p.Utilization,
                ["resets_at"] = p.ResetsAt,
                ["used"] = null,
                ["limit"] = null,
            };
            var tier = BuildTier(p.Key, t, now);
            tier["as_of"] = Iso(p.At);
            tiers.Add((JsonNode)tier);
        }

        return new JsonObject
        {
            ["status"] = "degraded",
            ["reason"] = ReasonWidgetTooOld,
            ["remedy"] = remedy,
            ["fetch_error"] = null,
            ["as_of"] = Iso(history.LatestAt),
            ["age_seconds"] = historyAge,
            ["writer_version"] = snapshotWriterVersion,
            ["snapshot_age_seconds"] = snapshotAge,
            ["required_widget_version"] = RequiredWidgetVersion,
            ["data_source"] = "history",
            ["scope"] = "active_account_only",
            ["account"] = new JsonObject
            {
                ["ref"] = history.AccountRef,
                ["plan"] = null,
            },
            ["data_lag_note"] = HistoryLagNote,
            ["schema_version"] = SnapshotContract.SchemaVersion,
            ["tiers"] = tiers,
            ["local_burn_since_snapshot"] = BuildLocalBurnSince(history.LatestAt),
        };
    }

    private static string Iso(DateTimeOffset t)
        => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

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
                projects.Add((JsonNode)new JsonObject { ["name"] = proj, ["tokens"] = tokens });
            roots.Add((JsonNode)new JsonObject
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

    // -- publish_usage --------------------------------------------------------

    /// <summary>Queue a publish request for the widget and wait (bounded) for its
    /// typed result. This server never touches the token or the network — the
    /// trust boundary (must-fix #11) holds structurally; the widget's tick loop
    /// (30s) performs the upload to the endpoint the user configured. Refusals
    /// are typed results, never protocol errors: <c>disabled</c> /
    /// <c>no_endpoint</c> / <c>no_token</c> come straight from settings.json
    /// (the app mirrors the endpoint URL and a token-present boolean there —
    /// never the token), so a refusal answers instantly instead of after a
    /// 45s wait.</summary>
    public JsonObject BuildPublish(string? dateArg)
    {
        DateOnly date;
        if (dateArg is null)
        {
            date = DateOnly.FromDateTime(_clock().ToLocalTime().DateTime).AddDays(-1);
        }
        else if (!DateOnly.TryParseExact(dateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return Refusal("no_data", "invalid_params", "date must be YYYY-MM-DD");
        }

        if (_config.SettingsPath is null || _config.PublishRequestPath is null || _config.PublishResultPath is null)
            return Refusal("no_data", "unavailable", "Publishing is not wired on this server configuration.");

        var (enabled, hasEndpoint, tokenStored) = ReadPublishSettings(_config.SettingsPath);
        if (!enabled)
            return Refusal("disabled", "publishing_off",
                "Publishing is off. Turn on 'Publish daily' in the Sanduhr widget (Settings > Publish usage) and tick the homes to share.");
        if (!hasEndpoint)
            return Refusal("no_endpoint", "no_endpoint_url",
                "No publish endpoint is configured. Set one in the Sanduhr widget (Settings > Publish usage > Endpoint URL, or pick a preset).");
        if (!tokenStored)
            return Refusal("no_token", "no_publish_token",
                "No publish token is stored for the publish endpoint. Add one in the Sanduhr widget (Settings > Publish usage > Set token), or set Auth to None if the endpoint needs no credential.");

        string id = Guid.NewGuid().ToString("N");
        var request = new JsonObject
        {
            ["id"] = id,
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["requested_at"] = _clock().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        };
        try
        {
            File.WriteAllText(_config.PublishRequestPath, request.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Refusal("error", "request_write_failed", $"Could not queue the request ({e.GetType().Name}).");
        }

        var deadline = DateTimeOffset.UtcNow + _config.PublishWaitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (TryReadResult(_config.PublishResultPath, id) is { } result)
            {
                result["date"] = request["date"]!.DeepClone();
                result["request_id"] = id;
                return result;
            }
            Thread.Sleep(_config.PublishPollInterval);
        }

        // The request file stays: the widget runs it on its next tick or next
        // launch (requests expire after 24h on the widget side).
        return new JsonObject
        {
            ["status"] = "queued",
            ["reason"] = "widget_not_responding",
            ["remedy"] = "The Sanduhr widget did not answer within the wait window - start (or restart) it. "
                         + "The request stays queued and runs on the widget's next tick; check Settings > Publish usage for the result.",
            ["date"] = request["date"]!.DeepClone(),
            ["request_id"] = id,
        };
    }

    /// <summary>The refusal inputs, read from settings.json's <c>publish</c>
    /// group (mirrors Core's PublishSettingsJson vocabulary literally — this
    /// project cannot link that file). <c>CredentialReady</c> is true when a
    /// token is stored OR the auth scheme is <c>none</c>. The pre-3.5
    /// <c>publish_626</c> group is honored read-only until the widget migrates
    /// it (it described the 626 Labs endpoint, so the endpoint counts as set).
    /// Anything unreadable fails closed.</summary>
    private static (bool Enabled, bool HasEndpoint, bool CredentialReady) ReadPublishSettings(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return (false, false, false);
            if (JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject root)
                return (false, false, false);
            if (root["publish"] is JsonObject group)
            {
                bool enabled = ReadBool(group["enabled"]);
                bool hasEndpoint = !string.IsNullOrWhiteSpace(ReadString(group["endpoint_url"]));
                bool tokenStored = ReadBool(group["token_stored"]);
                bool noAuth = string.Equals(ReadString(group["auth_scheme"])?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
                return (enabled, hasEndpoint, tokenStored || noAuth);
            }
            if (root["publish_626"] is JsonObject legacy)
                return (ReadBool(legacy["enabled"]), true, ReadBool(legacy["key_stored"]));
            return (false, false, false);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return (false, false, false);   // unreadable settings = off (fail closed)
        }
    }

    private static bool ReadBool(JsonNode? node)
    {
        try { return node?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    private static string? ReadString(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return null; }
    }

    private static JsonObject? TryReadResult(string resultPath, string id)
    {
        try
        {
            if (!File.Exists(resultPath))
                return null;
            using var fs = new FileStream(resultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            if (JsonNode.Parse(sr.ReadToEnd()) is not JsonObject o || (string?)o["id"] != id)
                return null;
            return o["result"] as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;   // mid-swap or unreadable: poll again
        }
    }

    private static JsonObject Refusal(string status, string reason, string remedy) => new()
    {
        ["status"] = status,
        ["reason"] = reason,
        ["remedy"] = remedy,
    };

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

    /// <summary>Non-generic Add only (see ToolCatalog): the generic Add&lt;T&gt;
    /// needs serializer metadata the trimmed publish does not carry.</summary>
    private static JsonArray ToArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add((JsonNode?)s);
        return arr;
    }
}
