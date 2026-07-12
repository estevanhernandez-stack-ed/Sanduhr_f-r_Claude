using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// One recorded utilization data point. Serializes to the exact Python schema:
/// <c>{"t": iso8601, "v": int}</c>, with an optional <c>"resets_at"</c> key
/// present only when set (parity with Python only adding the key when a reset
/// instant is known). Property declaration order matches the Python dict
/// insertion order (t, v, resets_at).
/// </summary>
public sealed class HistoryPoint
{
    [JsonPropertyName("t")]
    public string T { get; set; } = "";

    [JsonPropertyName("v")]
    public int V { get; set; }

    [JsonPropertyName("resets_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetsAt { get; set; }
}

/// <summary>
/// Per-account usage history storage, ported 1:1 from <c>history.py</c> and
/// implementing the <see cref="IUsageHistory"/> seam the
/// <see cref="UsageFetcher"/> writes to (defined in item 3).
///
/// An append-only rolling window of utilization percentages per tier, persisted
/// per account at <c>%APPDATA%\Sanduhr\history.{account}.json</c> in the SAME
/// JSON schema as the Python build — so an existing user's history carries over
/// with zero migration. Operations default to the active account; pass an
/// explicit account to target a specific one.
///
/// Legacy single-file history (<c>history.json</c> from v2.0.x / v2.1.x) is
/// renamed to <c>history.{active}.json</c> on first access — one-shot,
/// idempotent, never clobbering an existing per-account file. When no active
/// account is configured, reads return empty and writes no-op silently.
/// </summary>
public sealed class UsageHistory : IUsageHistory
{
    /// <summary>30 days × 288 five-minute ticks per day. Matches Python's
    /// <c>MAX_HISTORY</c> exactly (binding per test_history_retention.py).</summary>
    public const int MaxHistory = 8640;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AccountStore _accounts;
    private readonly Paths _paths;

    public UsageHistory(AccountStore accounts, Paths paths)
    {
        _accounts = accounts;
        _paths = paths;
    }

    private string? ResolveAccount(string? account) => account ?? _accounts.GetActive();

    /// <summary>Rename history.json → history.{active}.json on first access.
    /// No-op if there's no active account, no legacy file, or the per-account
    /// file already exists (don't clobber the active account's real data).</summary>
    private void MigrateLegacyIfNeeded(string? active)
    {
        if (active is null)
            return;
        var legacy = _paths.HistoryFile;
        var target = _paths.HistoryFileFor(active);
        if (!File.Exists(legacy) || File.Exists(target))
            return;
        File.Move(legacy, target);
    }

    private Dictionary<string, List<HistoryPoint>> ReadAccountFile(string account)
    {
        var p = _paths.HistoryFileFor(account);
        if (!File.Exists(p))
            return new Dictionary<string, List<HistoryPoint>>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<HistoryPoint>>>(File.ReadAllText(p))
                   ?? new Dictionary<string, List<HistoryPoint>>();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new Dictionary<string, List<HistoryPoint>>();
        }
    }

    private void WriteAccountFile(string account, Dictionary<string, List<HistoryPoint>> data)
        => File.WriteAllText(_paths.HistoryFileFor(account), JsonSerializer.Serialize(data, WriteOptions));

    /// <summary>Full history dict for the given (or active) account.</summary>
    public Dictionary<string, List<HistoryPoint>> LoadHistory(string? account = null)
    {
        var active = ResolveAccount(account);
        MigrateLegacyIfNeeded(active);
        return active is null ? new Dictionary<string, List<HistoryPoint>>() : ReadAccountFile(active);
    }

    /// <summary>Overwrite the history file for the given (or active) account.
    /// Internal/test helper — external callers should prefer <see cref="Append"/>.
    /// No-op when there's no active account and none was passed.</summary>
    public void SaveHistory(Dictionary<string, List<HistoryPoint>> data, string? account = null)
    {
        var active = ResolveAccount(account);
        if (active is null)
            return;
        WriteAccountFile(active, data);
    }

    /// <inheritdoc />
    public IReadOnlyList<int> Append(string tierKey, int utilization, string? resetsAt = null)
        => AppendForAccount(tierKey, utilization, null, resetsAt);

    /// <summary>
    /// Record a new utilization data point for the given (or active) account.
    /// <paramref name="resetsAt"/> (ISO-8601 from the API) is stored when
    /// present so the chart can render 'X% left · resets in Yd Zh'. Returns the
    /// current series of values, oldest-first. No-op (returns empty) when no
    /// active account is configured.
    /// </summary>
    public IReadOnlyList<int> AppendForAccount(string tierKey, int utilization, string? account, string? resetsAt = null)
    {
        var active = ResolveAccount(account);
        if (active is null)
            return Array.Empty<int>();
        MigrateLegacyIfNeeded(active);
        var data = ReadAccountFile(active);
        if (!data.TryGetValue(tierKey, out var series))
            series = new List<HistoryPoint>();
        var point = new HistoryPoint { T = NowIso(), V = utilization };
        if (!string.IsNullOrEmpty(resetsAt))
            point.ResetsAt = resetsAt;
        series.Add(point);
        if (series.Count > MaxHistory)
            series = series.GetRange(series.Count - MaxHistory, MaxHistory);
        data[tierKey] = series;
        WriteAccountFile(active, data);
        return series.Select(p => p.V).ToList();
    }

    /// <summary>List of utilization values for a tier, oldest first.</summary>
    public IReadOnlyList<int> Load(string tierKey, string? account = null)
    {
        var data = LoadHistory(account);
        return data.TryGetValue(tierKey, out var series)
            ? series.Select(p => p.V).ToList()
            : new List<int>();
    }

    /// <summary>History points for <paramref name="tierKey"/> within the last
    /// <paramref name="days"/> days, chronological. Malformed timestamps are
    /// skipped, not fatal.</summary>
    public IReadOnlyList<HistoryPoint> LoadWindow(string tierKey, int days, string? account = null)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var all = LoadHistory(account).TryGetValue(tierKey, out var s) ? s : new List<HistoryPoint>();
        var outPoints = new List<HistoryPoint>();
        foreach (var p in all)
        {
            if (TryParseTs(p.T, out var t) && t >= cutoff)
                outPoints.Add(p);
        }
        return outPoints;
    }

    /// <summary><c>{account_label: [points]}</c> across all registered accounts —
    /// the 'All accounts' overlay. Each account with no points for the tier gets
    /// an empty list.</summary>
    public Dictionary<string, IReadOnlyList<HistoryPoint>> AggregateWindow(string tierKey, int days)
    {
        var result = new Dictionary<string, IReadOnlyList<HistoryPoint>>();
        foreach (var label in _accounts.ListAccounts())
            result[label] = LoadWindow(tierKey, days, label);
        return result;
    }

    /// <summary>Wipe the history file for the given (or active) account by
    /// writing an empty dict (preserves the file-existence invariant downstream
    /// readers depend on — never unlinks). Other accounts are untouched. No-op
    /// when there's no active account and none was passed.</summary>
    public void ClearAll(string? account = null)
    {
        var active = ResolveAccount(account);
        if (active is null)
            return;
        WriteAccountFile(active, new Dictionary<string, List<HistoryPoint>>());
    }

    /// <summary>Unlink the history file for the given (or active) account —
    /// used by full account removal, where <see cref="ClearAll"/>'s keep-the-file
    /// invariant is exactly wrong. Best-effort: a locked or missing file never
    /// throws. Other accounts are untouched.</summary>
    public void Delete(string? account = null)
    {
        var active = ResolveAccount(account);
        if (active is null)
            return;
        try
        {
            File.Delete(_paths.HistoryFileFor(active));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort — registry consistency wins over file cleanup.
        }
    }

    /// <summary>Move <c>history.{oldLabel}.json</c> to the new label so a renamed
    /// account keeps its chart. Overwrites a stale file already sitting at the
    /// target name. Best-effort no-op when there is no source file or the move
    /// fails — call only after the registry rename has succeeded.</summary>
    public void Rename(string oldLabel, string newLabel)
    {
        var src = _paths.HistoryFileFor(oldLabel);
        if (!File.Exists(src))
            return;
        try
        {
            File.Move(src, _paths.HistoryFileFor(newLabel), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort — the account keeps working, only its chart resets.
        }
    }

    // Match Python's datetime.now(timezone.utc).isoformat(): 6-digit fractional
    // seconds + a "+00:00" offset. Parseable by both Python's
    // datetime.fromisoformat and .NET's DateTimeOffset.Parse.
    private static string NowIso()
        => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    private static bool TryParseTs(string? s, out DateTimeOffset t)
    {
        t = default;
        if (string.IsNullOrEmpty(s))
            return false;
        return DateTimeOffset.TryParse(
            s.Replace("Z", "+00:00"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out t);
    }
}
