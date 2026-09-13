using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App-side owner of "Publish to 626 Labs": consent state (settings.json
/// <c>publish_626</c>), the agent key (Credential Manager via
/// <see cref="PublishTokenStore"/>), the daily scheduler hook, the MCP handoff
/// (request file in, result file out), and the manual "Publish now". Every
/// upload runs off the UI thread and is single-flight; the tick entry point
/// never blocks. Mirrors <see cref="VaultService"/>'s shape on purpose.
///
/// What leaves the machine, and only when the master toggle is on: for each
/// home the user ticked, one token count per project BASENAME for one closed
/// local day, the tier split, and the widget's current quota percentages (or
/// an explicit stale/no_data marker). No paths, no session content, no
/// account labels, no key echo.
/// </summary>
public sealed class UsagePublishService
{
    private readonly Paths _paths;
    private readonly SettingsStore _settings;
    private readonly CcLogReader _reader;
    private readonly PublishTokenStore _tokens;
    private int _publishRunning;
    private int _tickRunning;

    /// <summary>Raised after any attempt (scheduled, manual, or MCP-queued)
    /// records its outcome — ON A WORKER THREAD; UI subscribers marshal.</summary>
    public event Action? StatusChanged;

    public UsagePublishService(SettingsStore settings, CcLogReader reader, Paths paths, PublishTokenStore tokens)
    {
        _settings = settings;
        _reader = reader;
        _paths = paths;
        _tokens = tokens;
    }

    // -- state ------------------------------------------------------------------

    public PublishSettings Load() => _settings.LoadPublishSettings();

    /// <summary>Basenames of the CC homes on this machine — the same list the
    /// vault consent offers, so both share maps speak the same vocabulary.</summary>
    public IReadOnlyList<string> DetectedRootNames()
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).ToList();

    /// <summary>Detected ∩ shared — the only roots the publisher ever reads.</summary>
    public IReadOnlyList<string> SharedRootNames()
    {
        var map = _settings.LoadPublishSettings().Roots;
        return DetectedRootNames().Where(r => map.GetValueOrDefault(r)).ToList();
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

    public bool HasToken
    {
        get
        {
            try { return _tokens.HasToken; }
            catch { return false; }
        }
    }

    /// <summary>Store the key in Credential Manager and mirror ONLY its
    /// presence into settings (for sanduhr-mcp's no_key refusal).</summary>
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
        string reqPath = Path.Combine(_paths.AppDataDir, PublishHandoff.RequestFileName);
        string resPath = Path.Combine(_paths.AppDataDir, PublishHandoff.ResultFileName);
        if (PublishHandoff.TryReadRequest(reqPath, nowLocal) is not { } request)
            return;
        if (Volatile.Read(ref _publishRunning) != 0)
            return;

        var s = _settings.LoadPublishSettings();
        JsonObject payload;
        if (!s.Enabled)
        {
            payload = Typed("disabled", "publishing_off",
                "Publishing is off. Turn on 'Publish to 626 Labs' in the Sanduhr widget (Settings > 626 Labs).");
        }
        else if (!HasToken)
        {
            payload = Typed("no_key", "no_agent_key",
                "No 626 Labs agent key is stored. Add one in the Sanduhr widget (Settings > 626 Labs > Set agent key).");
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
                    ["remedy"] = outcome.Result.Ok ? null : outcome.Result.Message,
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
                var shared = SharedRootNames();
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var burn = new List<RootDayBurn>(shared.Count);
                foreach (var root in shared)
                    burn.Add(_reader.BurnForLocalDay(root, Path.Combine(home, root), date));

                var settings = _settings.LoadPublishSettings();
                var quota = UsagePublisher.ReadQuota(_paths.SnapshotFile, DateTimeOffset.UtcNow);
                var payload = UsagePublisher.BuildPayload(
                    date, Environment.MachineName, burn,
                    new HashSet<string>(shared, StringComparer.Ordinal), quota, settings.BodyFormat);

                // The publisher refuses (status 0, no network) on a missing endpoint
                // or a missing token for a scheme that needs one.
                var result = await UsagePublisher.PublishAsync(payload, settings.Target, _tokens.Load()).ConfigureAwait(false);

                Record(result, date);
                return new PublishOutcome(result, shared, burn.Sum(b => b.Total),
                    ((JsonArray)payload["byProject"]!).Count, quota.Status);
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
            _settings.SavePublishAttempt(result.At, result.Message, result.Ok ? date : null);
            // PRIVACY.md contract for sanduhr.log: status codes only — never the payload, never the key.
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} publish 626 {date:yyyy-MM-dd} -> http {result.StatusCode} ({(result.Ok ? "ok" : "failed")}){Environment.NewLine}");
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
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} publish {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
        }
    }
}
