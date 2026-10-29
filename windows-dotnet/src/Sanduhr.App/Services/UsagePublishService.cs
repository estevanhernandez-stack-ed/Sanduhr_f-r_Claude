using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App-side owner of "Publish usage": consent state and destination
/// (settings.json <c>publish</c>), the publish token (Credential Manager via
/// <see cref="PublishTokenStore"/>), the daily scheduler hook, the MCP handoff
/// (request file in, result file out), and the manual "Publish now". Every
/// upload runs off the UI thread and is single-flight; the tick entry point
/// never blocks. Mirrors <see cref="VaultService"/>'s shape on purpose.
///
/// Nothing about usage comes back to 626 Labs; the user sends it to an
/// endpoint they chose (626 Labs is one preset). What leaves the machine,
/// and only when the master toggle is on: for each home the user ticked, one
/// token count per project BASENAME for one closed local day, the tier
/// split, and the widget's current quota percentages (or an explicit
/// stale/no_data marker). No paths, no session content, no account labels,
/// no token echo.
/// </summary>
public sealed class UsagePublishService
{
    private readonly Paths _paths;
    private readonly SettingsStore _settings;
    private readonly CcLogReader _reader;
    private readonly PublishTokenStore _tokens;
    private readonly Func<string> _home;
    private int _publishRunning;
    private int _tickRunning;

    /// <summary>Raised after any attempt (scheduled, manual, or MCP-queued)
    /// records its outcome — ON A WORKER THREAD; UI subscribers marshal.</summary>
    public event Action? StatusChanged;

    /// <summary><paramref name="home"/> is the folder the shared Claude Code folders sit in (the user profile; tests give a scratch one).</summary>
    public UsagePublishService(SettingsStore settings, CcLogReader reader, Paths paths, PublishTokenStore tokens, Func<string>? home = null)
    {
        _settings = settings;
        _reader = reader;
        _paths = paths;
        _tokens = tokens;
        _home = home ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));   // streamer: notname - user folder handed to the reader, never drawn
    }

    /// <summary>Tests: the network the upload goes to. Null is the real one.</summary>
    internal System.Net.Http.HttpMessageHandler? Handler { get; set; }

    // -- state ------------------------------------------------------------------

    public PublishSettings Load() => _settings.LoadPublishSettings();

    /// <summary>Basenames of the CC homes on this machine — the same list the
    /// vault consent offers, so both share maps speak the same vocabulary.</summary>
    public IReadOnlyList<string> DetectedRootNames()   // streamer: key - root names for consent keys, never drawn here
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).ToList();

    /// <summary>Detected ∩ shared — the only roots the publisher ever reads.</summary>
    public IReadOnlyList<string> SharedRootNames()
    {
        var map = _settings.LoadPublishSettings().Roots;
        return DetectedRootNames().Where(r => map.GetValueOrDefault(r)).ToList();   // streamer: key - root names for consent keys, never drawn here
    }

    public void SetEnabled(bool on) => _settings.SavePublishEnabled(on);

    public void SetRootShared(string root, bool on)
    {
        var map = new Dictionary<string, bool>(_settings.LoadPublishSettings().Roots, StringComparer.Ordinal)
        {
            [root] = on,
        };
        _settings.SavePublishRoots(map);
    }

    public void SetPublishTime(TimeOnly time) => _settings.SavePublishTime(time);

    // -- destination ------------------------------------------------------------

    public void SetEndpointUrl(string url) => _settings.SavePublishEndpointUrl(url);

    public void SetAuthScheme(PublishAuthScheme scheme) => _settings.SavePublishAuthScheme(scheme);

    public void SetAuthHeaderName(string name) => _settings.SavePublishAuthHeaderName(name);

    /// <summary>Record the preset; the 626 Labs one fills its URL, bearer auth
    /// and body format, Custom keeps the fields and uses the plain format.</summary>
    public void ApplyPreset(PublishPreset preset) => _settings.SavePublishPreset(preset);

    public bool HasToken
    {
        get
        {
            try { return _tokens.HasToken; }
            catch { return false; }
        }
    }

    /// <summary>Store the key in Credential Manager and mirror ONLY its
    /// presence into settings (for sanduhr-mcp's no_token refusal).</summary>
    public void SaveToken(string token)
    {
        _tokens.Save(token);
        _settings.SavePublishTokenStored(HasToken);
    }

    public void ClearToken()
    {
        _tokens.Clear();
        _settings.SavePublishTokenStored(false);
    }

    // -- the tick hook ------------------------------------------------------------

    /// <summary>Called from the widget's 30-second tick on the UI thread.
    /// Returns immediately; the work (handoff request, then the daily
    /// schedule) runs on the pool, one tick at a time.</summary>
    public void Tick(DateTimeOffset nowLocal)
    {
        if (ResetGuard.Done)
            return;
        if (Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleHandoffAsync(nowLocal).ConfigureAwait(false);
                await HandleScheduleAsync(nowLocal).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogBestEffort("tick", e);
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        });
    }

    private async Task HandleScheduleAsync(DateTimeOffset nowLocal)
    {
        var s = _settings.LoadPublishSettings();
        var decision = PublishScheduler.Decide(nowLocal, s.Enabled, s.PublishTime, s.LastOkDate, s.LastAttemptAt);
        if (decision.Verdict != PublishVerdict.Run)
            return;
        await PublishAsync(decision.TargetDate).ConfigureAwait(false);
    }

    /// <summary>sanduhr-mcp's publish_usage drops a request file; answer it
    /// with the same typed result shape every tool returns, then clear it.
    /// A request that arrives while an upload is in flight waits for the next tick.</summary>
    private async Task HandleHandoffAsync(DateTimeOffset nowLocal)
    {
        string reqPath = Path.Combine(_paths.ExternalDir, PublishHandoff.RequestFileName);   // streamer: notname - a file location used for IO, never drawn
        string resPath = Path.Combine(_paths.ExternalDir, PublishHandoff.ResultFileName);
        if (PublishHandoff.TryReadRequest(reqPath, nowLocal) is not { } request)
            return;
        if (Volatile.Read(ref _publishRunning) != 0)
            return;

        var s = _settings.LoadPublishSettings();
        JsonObject payload;
        if (!s.Enabled)
        {
            payload = Typed("disabled", "publishing_off",
                "Publishing is off. Turn on 'Publish daily at' in the Sanduhr widget (Settings, Publish usage, Publish daily).");
        }
        else if (!s.HasEndpoint)
        {
            payload = Typed("no_endpoint", "no_endpoint_url",
                "No publish endpoint is configured. Set the Endpoint URL, or pick a preset, in the Sanduhr widget (Settings, Publish usage, Destination).");
        }
        else if (s.AuthScheme != PublishAuthScheme.None && !HasToken)
        {
            payload = Typed("no_token", "no_publish_token",
                "No publish token is stored for the publish endpoint. Add one with Set token… in the Sanduhr widget (Settings, Publish usage, Token).");
        }
        else
        {
            var outcome = await PublishAsync(request.Date).ConfigureAwait(false);
            payload = outcome is null
                ? Typed("error", "busy", "Another publish is in flight - call again in a moment.")
                : new JsonObject
                {
                    ["status"] = outcome.Result.Status,
                    ["reason"] = outcome.Result.Ok ? null : "http_" + outcome.Result.StatusCode,
                    ["remedy"] = outcome.Result.Ok ? null : outcome.Result.Message,   // streamer: cant - the publish result goes to the MCP server through the handoff file; MCP answers are on the can't-reach list
                    ["http_status"] = outcome.Result.StatusCode,
                    ["message"] = outcome.Result.Message,
                    ["at"] = outcome.Result.At.ToUniversalTime().ToString("o"),
                    ["homes"] = new JsonArray(outcome.Homes.Select(h => (JsonNode?)h).ToArray()),
                    ["total_tokens"] = outcome.TotalTokens,
                    ["projects"] = outcome.ProjectCount,
                    ["quota_status"] = outcome.QuotaStatus,
                };
        }
        PublishHandoff.WriteResult(resPath, reqPath, request.Id, payload);
    }

    private static JsonObject Typed(string status, string reason, string remedy) => new()
    {
        ["status"] = status,
        ["reason"] = reason,
        ["remedy"] = remedy,
    };

    // -- the upload ---------------------------------------------------------------

    public sealed record PublishOutcome(
        UsagePublishResult Result, IReadOnlyList<string> Homes, long TotalTokens, int ProjectCount, string QuotaStatus);

    /// <summary>One day's record, as it would be sent: the payload, the folders in it, the token total and the quota's state.</summary>
    public sealed record PublishRecord(JsonObject Payload, IReadOnlyList<string> Homes, long TotalTokens, string QuotaStatus);

    /// <summary>
    /// The one builder (Settings v2, WF28): the scheduled publish, Publish now, the MCP's publish_usage and Settings' "Show a sample…"
    /// all read the record here. Reads the shared folders' logs for <paramref name="date"/> and the widget's snapshot; sends nothing,
    /// and never touches the publish token (only <see cref="PublishAsync"/> loads it, to post).
    /// </summary>
    public PublishRecord BuildRecord(DateOnly date)
    {
        var shared = SharedRootNames();
        var home = _home();
        var burn = new List<RootDayBurn>(shared.Count);
        foreach (var root in shared)
            burn.Add(_reader.BurnForLocalDay(root, Path.Combine(home, root), date));

        var settings = _settings.LoadPublishSettings();
        var quota = UsagePublisher.ReadQuota(_paths.SnapshotFile, DateTimeOffset.UtcNow);   // streamer: notname - a file location used for IO, never drawn
        var payload = UsagePublisher.BuildPayload(
            date, Environment.MachineName, burn,   // streamer: export - the upload record the person turned on carries the PC name; the Publish page's sample masks it (CP 20)
            new HashSet<string>(shared, StringComparer.Ordinal), quota, settings.BodyFormat);
        return new PublishRecord(payload, shared, burn.Sum(b => b.Total), quota.Status);
    }

    /// <summary>"Show a sample…": the record Publish now would send at <paramref name="nowLocal"/> (yesterday's, local), built off the
    /// UI thread. Nothing is posted, recorded or written, and no token is read.</summary>
    public Task<PublishRecord> SampleAsync(DateTimeOffset nowLocal) =>
        Task.Run(() => BuildRecord(UsagePublisher.DefaultDate(nowLocal)));

    /// <summary>Collect, build, post, record. Single-flight: returns null when
    /// another run holds the latch. Never throws — a fault records as a failed
    /// attempt so the status line says so.</summary>
    public async Task<PublishOutcome?> PublishAsync(DateOnly date)
    {
        if (Interlocked.CompareExchange(ref _publishRunning, 1, 0) != 0)
            return null;
        try
        {
            return await Task.Run(async () =>
            {
                var record = BuildRecord(date);
                var settings = _settings.LoadPublishSettings();

                // The publisher refuses (status 0, no network) on a missing endpoint
                // or a missing token for a scheme that needs one.
                var result = await UsagePublisher.PublishAsync(record.Payload, settings.Target, _tokens.Load(), Handler).ConfigureAwait(false);

                Record(result, date);
                return new PublishOutcome(result, record.Homes, record.TotalTokens,
                    ((JsonArray)record.Payload["byProject"]!).Count, record.QuotaStatus);
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogBestEffort("publish", e);
            var failed = new UsagePublishResult(0, false, $"Publish failed ({e.GetType().Name}).", DateTimeOffset.Now);
            Record(failed, date);
            return new PublishOutcome(failed, Array.Empty<string>(), 0, 0, "no_data");
        }
        finally
        {
            Interlocked.Exchange(ref _publishRunning, 0);
        }
    }

    private void Record(UsagePublishResult result, DateOnly date)
    {
        try
        {
            _settings.SavePublishAttempt(result.At, result.Message, result.Ok ? date : null);   // streamer: key - stores the attempt text; the Publish page masks hosts when it draws it (CP 19)
            // PRIVACY.md contract for sanduhr.log: status codes only — never the payload, never the key.
            File.AppendAllText(_paths.LogFile,   // streamer: notname - written to the log file, never drawn
                $"{DateTime.UtcNow:o} publish {date:yyyy-MM-dd} -> http {result.StatusCode} ({(result.Ok ? "ok" : "failed")}){Environment.NewLine}");
        }
        catch
        {
            // Recording must never turn a successful upload into a crash.
        }
        StatusChanged?.Invoke();
    }

    // PRIVACY.md contract: operation + exception type only.
    private void LogBestEffort(string operation, Exception e)
    {
        try
        {
            File.AppendAllText(_paths.LogFile,   // streamer: notname - written to the log file, never drawn
                $"{DateTime.UtcNow:o} publish {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
        }
    }
}
