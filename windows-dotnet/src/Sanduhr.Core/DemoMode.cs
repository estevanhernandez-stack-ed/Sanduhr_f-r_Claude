using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Demo mode's sample data: made-up accounts, usage numbers and sparkline history for
/// screenshots that show no real account. Pure data, session-only: nothing here is written
/// anywhere. Ported from the Mac's DemoMode.swift.
/// </summary>
public static class DemoMode
{
    /// <summary>The sample accounts, the active one first. Names only.</summary>
    public static IReadOnlyList<string> Accounts { get; } = ["Work", "Personal"];

    /// <summary>The account demo mode starts on.</summary>
    public static string Active => Accounts[0];

    /// <summary>The widget's demo banner.</summary>
    public const string Banner = "Demo mode: sample numbers, not a real account.";

    /// <summary>The command-line switch that starts demo mode.</summary>
    public const string Switch = "--demo";

    /// <summary>The environment variable that starts demo mode when "1" or "true" (case-insensitive).</summary>
    public const string EnvironmentVariable = "SANDUHR_DEMO";

    private static readonly TimeSpan HistoryStep = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when any argument is the demo switch, or the environment value is "1" or "true".
    /// </summary>
    public static bool IsRequested(IEnumerable<string> args, string? environmentValue)
    {
        if (args is not null)
        {
            foreach (var arg in args)
            {
                if (arg is not null && string.Equals(arg.Trim(), Switch, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        var env = environmentValue?.Trim();
        return string.Equals(env, "1", StringComparison.Ordinal)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Made-up claude.ai usage payload for a sample account: Work busy, Personal light.
    /// Reset times count from <paramref name="now"/>. A fresh object on every call.
    /// </summary>
    public static JsonObject Usage(string account, DateTimeOffset now)
    {
        var obj = new JsonObject();
        foreach (var (key, util, resetsIn) in Samples(account))
        {
            obj[key] = new JsonObject
            {
                ["utilization"] = util,
                ["resets_at"] = now.Add(resetsIn).ToString("o", CultureInfo.InvariantCulture),
            };
        }
        return obj;
    }

    /// <summary>
    /// A made-up sparkline for one tier of a sample account: <paramref name="count"/> values
    /// (0..100) oldest first, one every 30 minutes, ending at the tier's sample utilization.
    /// Empty when the account has no sample for that tier or count is not positive.
    /// </summary>
    public static IReadOnlyList<int> History(string account, string tierKey, DateTimeOffset now, int count = 48)
    {
        if (count <= 0) return [];
        var sample = Samples(account).FirstOrDefault(s => s.Key == tierKey);
        if (sample.Key is null) return [];

        var period = tierKey == TierModel.FiveHour ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
        var amplitude = account == Accounts[1] ? 2.0 : 6.0;
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            var steps = count - 1 - i;
            var age = TimeSpan.FromTicks(HistoryStep.Ticks * steps);
            var phase = (age / period) % 1.0;
            var wiggle = amplitude * Math.Sin(steps / 3.0);
            var value = Math.Clamp(sample.Util * (1 - phase) + wiggle, 0, 100);
            values[i] = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
        return values;
    }

    /// <summary>The sample account after <paramref name="active"/>, wrapping; null or unknown gives the first.</summary>
    public static string Next(string? active)
    {
        for (var i = 0; i < Accounts.Count; i++)
        {
            if (Accounts[i] == active) return Accounts[(i + 1) % Accounts.Count];
        }
        return Accounts[0];
    }

    /// <summary>The widget's account chip text while demo mode is on.</summary>
    public static string ChipText(string account) => $"{account} (demo)";

    /// <summary>The tray tooltip label while demo mode is on.</summary>
    public static string TrayLabel(string label, int percent) => $"Demo · {label} {percent}%";

    private static (string Key, double Util, TimeSpan ResetsIn)[] Samples(string account) =>
        account == Accounts[1]
            ? [(TierModel.FiveHour, 12.0, TimeSpan.FromHours(4)),
               (TierModel.SevenDay, 31.0, TimeSpan.FromDays(5))]
            : [(TierModel.FiveHour, 42.0, TimeSpan.FromHours(2)),
               (TierModel.SevenDay, 63.0, TimeSpan.FromDays(4)),
               (TierModel.SevenDaySonnet, 18.0, TimeSpan.FromDays(4))];
}
