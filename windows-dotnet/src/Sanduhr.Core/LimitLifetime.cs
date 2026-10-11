using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What a limit read when it was hidden until its reset.</summary>
public sealed record HiddenLimitRecord(string? ResetsAt, double? Utilization);

/// <summary>Persisted lifetime bookkeeping: first-seen dates and the hidden-until-reset map.</summary>
public sealed record LimitState(
    IReadOnlyDictionary<string, DateTimeOffset> FirstSeen,
    IReadOnlyDictionary<string, HiddenLimitRecord> HiddenUntilReset)
{
    public static LimitState Empty { get; } = new(
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, HiddenLimitRecord>());
}

/// <summary>New state plus the tiers that came back this pass (tier to reason).</summary>
public sealed record ReconcileResult(LimitState State, IReadOnlyDictionary<string, string> Returned);

/// <summary>
/// Which limits are believed temporary, and when a hidden one comes back. Port of
/// LimitLifetime.swift. Pure: numbers, clock and first-seen date come in as values.
/// Only a temporary limit can be hidden until reset; session and weekly never are.
/// </summary>
public static class LimitLifetime
{
    public static readonly TimeSpan LongReset = TimeSpan.FromDays(8);
    public static readonly TimeSpan NewWindow = TimeSpan.FromDays(14);
    public static readonly TimeSpan NewResetSlack = TimeSpan.FromHours(1);
    public const double RefillPoints = 10;

    public const string ReasonNewWindow = "new_window";
    public const string ReasonRefill = "refill";
    public const string ReasonNotTemporary = "not_temporary";

    public static bool IsPermanent(string tier) =>
        tier == TierModel.FiveHour || tier == TierModel.SevenDay;

    public static bool IsKnown(string tier) =>
        IsPermanent(tier) ||
        tier is TierModel.SevenDaySonnet or TierModel.SevenDayOpus or TierModel.SevenDayFable
            or TierModel.SevenDayCowork or TierModel.SevenDayOmelette or TierModel.SevenDayOauthApps
            or TierModel.ExtraUsage or TierModel.Routines;

    public static bool IsTemporary(string tier, string? resetsAt, DateTimeOffset now, DateTimeOffset? firstSeen)
    {
        if (IsPermanent(tier)) return false;
        if (tier == TierModel.IguanaNecktie) return true;
        if (ParseIso(resetsAt) is { } reset && reset - now > LongReset) return true;
        if (firstSeen is { } seen && now - seen < NewWindow && !IsKnown(tier)) return true;
        return false;
    }

    public static bool CanHide(string tier, string? resetsAt, DateTimeOffset now, DateTimeOffset? firstSeen) =>
        IsTemporary(tier, resetsAt, now, firstSeen);

    /// <summary>"new_window", "refill", or null while the limit stays hidden. Missing data decides nothing.</summary>
    public static string? ShouldReturn(HiddenLimitRecord rec, string? resetsAt, double? util)
    {
        if (ParseIso(rec.ResetsAt) is { } then && ParseIso(resetsAt) is { } current && current - then > NewResetSlack)
            return ReasonNewWindow;
        if (rec.Utilization is { } before && util is { } after && before - after >= RefillPoints)
            return ReasonRefill;
        return null;
    }

    public static LimitState Hide(LimitState state, string tier, string? resetsAt, double? util, DateTimeOffset now)
    {
        DateTimeOffset? seen = state.FirstSeen.TryGetValue(tier, out var s) ? s : null;
        if (!CanHide(tier, resetsAt, now, seen)) return state;
        var hidden = new Dictionary<string, HiddenLimitRecord>(state.HiddenUntilReset)
        {
            [tier] = new HiddenLimitRecord(resetsAt, util),
        };
        return state with { HiddenUntilReset = hidden };
    }

    public static LimitState Show(LimitState state, string tier)
    {
        if (!state.HiddenUntilReset.ContainsKey(tier)) return state;
        var hidden = new Dictionary<string, HiddenLimitRecord>(state.HiddenUntilReset);
        hidden.Remove(tier);
        return state with { HiddenUntilReset = hidden };
    }

    public static ReconcileResult Reconcile(
        LimitState state,
        IReadOnlyDictionary<string, (string? ResetsAt, double? Util)> present,
        DateTimeOffset now)
    {
        var firstSeen = new Dictionary<string, DateTimeOffset>(state.FirstSeen);
        foreach (var tier in present.Keys)
            firstSeen.TryAdd(tier, now);

        var hidden = new Dictionary<string, HiddenLimitRecord>(state.HiddenUntilReset);
        var returned = new Dictionary<string, string>();
        foreach (var (tier, rec) in state.HiddenUntilReset)
        {
            if (!present.TryGetValue(tier, out var reading)) continue;
            DateTimeOffset? seen = firstSeen.TryGetValue(tier, out var s) ? s : null;
            string? reason = !IsTemporary(tier, reading.ResetsAt, now, seen)
                ? ReasonNotTemporary
                : ShouldReturn(rec, reading.ResetsAt, reading.Util);
            if (reason is null) continue;
            returned[tier] = reason;
            hidden.Remove(tier);
        }
        return new ReconcileResult(new LimitState(firstSeen, hidden), returned);
    }

    // -- settings JSON ---------------------------------------------------------

    public static JsonObject FirstSeenToJson(IReadOnlyDictionary<string, DateTimeOffset> firstSeen)
    {
        var obj = new JsonObject();
        foreach (var (tier, at) in firstSeen)
            obj[tier] = at.ToString("o", CultureInfo.InvariantCulture);
        return obj;
    }

    public static IReadOnlyDictionary<string, DateTimeOffset> FirstSeenFromJson(JsonNode? node)
    {
        var result = new Dictionary<string, DateTimeOffset>();
        if (node is not JsonObject obj) return result;
        foreach (var (tier, value) in obj)
            if (ParseIso(StringOf(value)) is { } at) result[tier] = at;
        return result;
    }

    public static JsonObject HiddenToJson(IReadOnlyDictionary<string, HiddenLimitRecord> hidden)
    {
        var obj = new JsonObject();
        foreach (var (tier, rec) in hidden)
        {
            var entry = new JsonObject();
            if (rec.ResetsAt is not null) entry["resets_at"] = rec.ResetsAt;
            if (rec.Utilization is { } u) entry["utilization"] = u;
            obj[tier] = entry;
        }
        return obj;
    }

    public static IReadOnlyDictionary<string, HiddenLimitRecord> HiddenFromJson(JsonNode? node)
    {
        var result = new Dictionary<string, HiddenLimitRecord>();
        if (node is not JsonObject obj) return result;
        foreach (var (tier, value) in obj)
        {
            if (value is not JsonObject entry) continue;
            result[tier] = new HiddenLimitRecord(StringOf(entry["resets_at"]), NumberOf(entry["utilization"]));
        }
        return result;
    }

    // -- helpers ---------------------------------------------------------------

    private static DateTimeOffset? ParseIso(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? NumberOf(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return double.IsFinite(d) ? d : null;
        if (v.TryGetValue<string>(out var s) &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
            return parsed;
        return null;
    }
}
