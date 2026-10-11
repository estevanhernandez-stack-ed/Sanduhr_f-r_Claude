using System.Globalization;

namespace Sanduhr.Core;

/// <summary>One tier as Desk receives it: the tier key, its utilization (can pass 100) and the
/// ISO reset time, or null without one.</summary>
public sealed record DeskTier(string Key, double Utilization, string? ResetsAt);

/// <summary>
/// What Desk knows about Claude usage, handed over by the widget's view model after every refresh
/// (the Mac's <c>DeskUsage</c>). <paramref name="Tiers"/> keeps the widget's display order;
/// <paramref name="FetchedAt"/> is the last successful fetch, kept through failed ones;
/// <paramref name="SignInNeeded"/> means only a (new) sign-in helps; <paramref name="FirstSignIn"/>
/// means there has never been a key, so the line asks to sign in, not to sign in again;
/// <paramref name="Account"/> is the active account as the Claude line names it with two or more
/// accounts, else null.
/// </summary>
public sealed record DeskUsage(
    IReadOnlyList<DeskTier> Tiers,
    DateTimeOffset? FetchedAt,
    bool SignInNeeded,
    bool FirstSignIn,
    string? Account)
{
    /// <summary>No tiers, never fetched, nothing needed.</summary>
    public static DeskUsage Empty { get; } = new([], null, false, false, null);

    /// <summary>Numbers older than this are drawn dimmed.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>Sign-in needed, never fetched, or fetched longer ago than <see cref="StaleAfter"/>.</summary>
    public bool IsStale(DateTimeOffset now) =>
        SignInNeeded || FetchedAt is not { } at || now - at > StaleAfter;
}

/// <summary>One limit as the Desk meters draw it: label, bar fill, pace tick, percent and reset
/// time. Built from the same helpers the widget's tier cards use, so the two always agree.</summary>
/// <param name="Key">The tier key (<see cref="TierModel"/>).</param>
/// <param name="Label">The widget's label for the tier ("Session (5hr)").</param>
/// <param name="Percent">Utilization as the widget prints it (truncated); can pass 100.</param>
/// <param name="Fill">How much of the bar to fill, 0 to 1; a limit over 100% fills the bar and stops.</param>
/// <param name="Pace">Where the pace tick goes, 0 to 1, or null without a reset time.</param>
/// <param name="Reset">"Today 5:00 PM" as the card prints it, or empty without a reset time.</param>
/// <param name="Warning">Utilization at <see cref="DeskMeters.WarningAt"/> or more: the bar draws red.</param>
public sealed record DeskMeterRow(string Key, string Label, int Percent, double Fill, double? Pace, string Reset, bool Warning);

/// <summary>The Desk meters' rows (the Mac's <c>DeskMeterRow.rows</c>).</summary>
public static class DeskMeters
{
    /// <summary>The utilization at which the widget card's bar turns red
    /// (<see cref="ThemeCatalog.UsageColor"/>: under 90 is orange, 90 and up is red).</summary>
    public const double WarningAt = 90;

    /// <summary>One row per tier, in the given order; none when a sign-in is needed.
    /// <paramref name="zone"/> converts reset times (default: local), for deterministic tests.</summary>
    public static IReadOnlyList<DeskMeterRow> Rows(DeskUsage usage, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (usage.SignInNeeded) return [];
        var rows = new List<DeskMeterRow>(usage.Tiers.Count);
        foreach (var t in usage.Tiers)
        {
            rows.Add(new DeskMeterRow(
                t.Key,
                TierModel.Label(t.Key),
                (int)t.Utilization,
                Math.Min(1.0, Math.Max(0.0, t.Utilization / 100.0)),
                Pacing.PaceFrac(t.ResetsAt, t.Key, now),
                ResetDateTime(t.ResetsAt, now, zone),
                t.Utilization >= WarningAt));
        }
        return rows;
    }

    /// <summary>
    /// The card's reset text, character for character (<c>TierCardViewModel.ResetDatetimeStr</c>):
    /// "Today 1:00 AM" / "Tomorrow 1:00 AM" / "Sun 1:00 AM" (under a week) / "Wed Apr 22 1:00 AM";
    /// empty without a parseable reset time.
    /// </summary>
    public static string ResetDateTime(string? iso, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var rd = Pacing.Parse(iso);
        if (rd is null) return "";
        var z = zone ?? TimeZoneInfo.Local;
        var loc = TimeZoneInfo.ConvertTime(rd.Value, z);
        var localNow = TimeZoneInfo.ConvertTime(now, z);
        int days = (loc.Date - localNow.Date).Days;
        string t = loc.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (days <= 0) return $"Today {t}";
        if (days == 1) return $"Tomorrow {t}";
        if (days < 7) return $"{loc.ToString("ddd", CultureInfo.InvariantCulture)} {t}";
        return $"{loc.ToString("ddd MMM dd", CultureInfo.InvariantCulture)} {t}";
    }
}

/// <summary>The text forms of the same numbers: the Desk's one-line <c>claude</c> piece (the Mac's
/// <c>DeskClaudeText</c>).</summary>
public static class DeskClaudeText
{
    /// <summary>The short sign-in line.</summary>
    public const string CompactSignIn = "sign in to Sanduhr";

    /// <summary>The line in its two pieces: the account label (null with one account, where the
    /// line starts with "claude") and everything after it.</summary>
    public sealed record Parts(string? Account, string Rest);

    /// <summary>"sign in to Sanduhr" before the first key, else "sign in again in Sanduhr".</summary>
    public static string SignInLine(bool first) => first ? CompactSignIn : "sign in again in Sanduhr";

    /// <summary>"7% session, resets 3:00   63% week", or the sign-in line, or null with no numbers.
    /// <paramref name="zone"/> converts the session reset time (default: local).</summary>
    public static Parts? GetParts(DeskUsage u, TimeZoneInfo? zone = null)
    {
        if (u.SignInNeeded) return new Parts(u.Account, SignInLine(u.FirstSignIn));
        var parts = new List<string>();
        var session = u.Tiers.FirstOrDefault(t => t.Key == TierModel.FiveHour);
        if (session is not null)
        {
            var part = $"{(int)session.Utilization}% session";
            if (Pacing.Parse(session.ResetsAt) is { } reset)
            {
                var local = TimeZoneInfo.ConvertTime(reset, zone ?? TimeZoneInfo.Local);
                part += $", resets {local.ToString("h:mm", CultureInfo.InvariantCulture)}";
            }
            parts.Add(part);
        }
        var week = u.Tiers.FirstOrDefault(t => t.Key == TierModel.SevenDay);
        if (week is not null) parts.Add($"{(int)week.Utilization}% week");
        return parts.Count == 0 ? null : new Parts(u.Account, string.Join("   ", parts));
    }

    /// <summary>"claude   7% session, resets 3:00   63% week", or null with nothing to say. With two
    /// or more accounts the line starts with the active label in place of "claude".</summary>
    public static string? Line(DeskUsage u, TimeZoneInfo? zone = null) =>
        GetParts(u, zone) is { } p ? $"{p.Account ?? "claude"}   {p.Rest}" : null;
}
