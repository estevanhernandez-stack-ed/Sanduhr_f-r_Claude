using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One Codex plan window from a rollout's <c>rate_limits</c>.</summary>
public sealed record CodexWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);

/// <summary>The newest Codex limits Sanduhr could read, and when Codex wrote them.</summary>
public sealed record CodexLimitsReading(DateTimeOffset AsOf, CodexWindow? Primary, CodexWindow? Secondary, string? PlanType);

/// <summary>
/// Parses Codex's <c>token_count</c> rollout events (shape confirmed 2026-11-09, checklist "Codex limits
/// check") and turns a reading into the same tier object Claude's fetch produces. Reads only percents,
/// window lengths, reset times, the event timestamp and the plan name; never message text or ids.
/// </summary>
public static class CodexLimits
{
    public const string FiveHour = TierModel.FiveHour;
    public const string SevenDay = TierModel.SevenDay;
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    public static bool TryParseLine(string line, out CodexLimitsReading reading)
    {
        reading = null!;
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return false;
            if (!p.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() != "token_count") return false;
            if (!p.TryGetProperty("rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var asOf))
                return false;
            var primary = Window(rl, "primary");
            var secondary = Window(rl, "secondary");
            if (primary is null && secondary is null) return false;
            string? plan = rl.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
            reading = new CodexLimitsReading(asOf, primary, secondary, plan);
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static CodexWindow? Window(JsonElement rl, string name)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("used_percent", out var up) || up.ValueKind != JsonValueKind.Number) return null;
        if (!up.TryGetDouble(out var used) || !double.IsFinite(used)) return null;
        if (!w.TryGetProperty("window_minutes", out var wm) || wm.ValueKind != JsonValueKind.Number || !wm.TryGetInt32(out var minutes)) return null;
        DateTimeOffset? resets = null;
        if (w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number && ra.TryGetInt64(out var epoch)
            && epoch >= DateTimeOffset.MinValue.ToUnixTimeSeconds() && epoch <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            resets = DateTimeOffset.FromUnixTimeSeconds(epoch);
        return new CodexWindow(Math.Clamp(used, 0, 100), minutes, resets);
    }

    public static JsonObject ToTiers(CodexLimitsReading reading, DateTimeOffset now, Action<int>? unknownWindow = null)
    {
        var tiers = new JsonObject();
        foreach (var w in new[] { reading.Primary, reading.Secondary })
        {
            if (w is null) continue;
            string? key = w.WindowMinutes switch { 300 => FiveHour, 10080 => SevenDay, _ => null };
            if (key is null) { unknownWindow?.Invoke(w.WindowMinutes); continue; }
            bool passed = w.ResetsAt is { } r && r <= now;
            tiers[key] = new JsonObject
            {
                ["utilization"] = passed ? 0d : w.UsedPercent,
                ["resets_at"] = w.ResetsAt?.ToString("o", CultureInfo.InvariantCulture),
            };
        }
        return tiers;
    }

    public static bool IsFresh(CodexLimitsReading reading, DateTimeOffset now)
    {
        if (now - reading.AsOf <= FreshFor) return true;
        return Passed(reading.Primary, now) || Passed(reading.Secondary, now);
    }

    private static bool Passed(CodexWindow? w, DateTimeOffset now) => w?.ResetsAt is { } r && r <= now;
}
