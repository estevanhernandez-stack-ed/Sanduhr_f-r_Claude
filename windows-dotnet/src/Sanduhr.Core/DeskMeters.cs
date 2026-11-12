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
/// accounts, else null. <paramref name="LineTiers"/> feeds the Claude line: every tier with data,
/// hidden or not (the line reads session and week whatever the cards show); null uses
/// <paramref name="Tiers"/>. <paramref name="SignInReminder"/> is the Desk's sign-in reminder switch
/// (default on): with it off a needed sign-in draws nothing on the Claude line.
/// Agent layer: <paramref name="SourceTiers"/> holds the tiers of the sources other than Claude (Codex's session and weekly), null with
/// none; <paramref name="SourceStaleTiers"/> names, per source, the tier keys whose numbers are stale (freshness is per tier, so a
/// per-source flag could not say it); <paramref name="SourcePrimary"/> is the primary source and <paramref name="SourceColors"/> each
/// source's <c>#RRGGBB</c> (<see cref="SourceSettings.ColorOf"/>), for the combined fill.
/// </summary>
public sealed record DeskUsage(
    IReadOnlyList<DeskTier> Tiers,
    DateTimeOffset? FetchedAt,
    bool SignInNeeded,
    bool FirstSignIn,
    string? Account,
    IReadOnlyList<DeskTier>? LineTiers = null,
    bool SignInReminder = true,
    IReadOnlyDictionary<string, IReadOnlyList<DeskTier>>? SourceTiers = null,
    IReadOnlyDictionary<string, IReadOnlySet<string>>? SourceStaleTiers = null,
    string SourcePrimary = AgentSources.Claude,
    IReadOnlyDictionary<string, string>? SourceColors = null)
{
    /// <summary>No tiers, never fetched, nothing needed.</summary>
    public static DeskUsage Empty { get; } = new([], null, false, false, null);

    /// <summary>Numbers older than this are drawn dimmed.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>The tiers of a source other than Claude, in session, weekly order; empty with none.</summary>
    public IReadOnlyList<DeskTier> TiersOf(string source) =>
        SourceTiers is not null && SourceTiers.TryGetValue(source, out var t) ? t : [];

    /// <summary>True when <paramref name="source"/>'s <paramref name="tierKey"/> is stale (not in the fresh set the widget knows).</summary>
    public bool IsTierStale(string source, string tierKey) =>
        SourceStaleTiers is not null && SourceStaleTiers.TryGetValue(source, out var s) && s.Contains(tierKey);

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
/// <param name="Level">How far the limit is toward its end by the alert thresholds (<see cref="MeterSignal"/>): past Warn at the bar
/// takes a yellow middle stop, past Urgent at a red one.</param>
/// <param name="Stale">The reading is stale (a Codex tier that has not been fresh); it draws dimmed. Claude's rows are never stale here: the
/// whole piece dims by <see cref="DeskUsage.IsStale"/>.</param>
public sealed record DeskMeterRow(string Key, string Label, int Percent, double Fill, double? Pace, string Reset, MeterLevel Level, bool Stale = false);

/// <summary>The Desk meters' rows (the Mac's <c>DeskMeterRow.rows</c>).</summary>
public static class DeskMeters
{
    /// <summary>True when the Desk draws its sign-in line: a sign-in is needed and the reminder is on.</summary>
    public static bool SignInLineShown(DeskUsage u) => u.SignInNeeded && u.SignInReminder;

    /// <summary>One row per tier, in the given order; none when a sign-in is needed.
    /// <paramref name="zone"/> converts reset times (default: local), for deterministic tests. <paramref name="warnAt"/> and
    /// <paramref name="urgentAt"/> are the alert thresholds (settings.json <c>alert_warn_pct</c>, <c>alert_urgent_pct</c>).</summary>
    public static IReadOnlyList<DeskMeterRow> Rows(DeskUsage usage, DateTimeOffset now, TimeZoneInfo? zone = null,
        int warnAt = MeterSignal.DefaultWarnAt, int urgentAt = MeterSignal.DefaultUrgentAt)
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
                MeterSignal.Level(t.Utilization, warnAt, urgentAt)));
        }
        return rows;
    }

    /// <summary>The windows the combined fill joins sources on, with the label a joined window carries.</summary>
    private static readonly (string Key, string Label)[] CombinedWindows = [(TierModel.FiveHour, "Session (5hr)"), (TierModel.SevenDay, "Weekly")];

    private static string? CombinedLabel(string key) => CombinedWindows.FirstOrDefault(w => w.Key == key).Label;

    /// <summary>The label Codex's tiers carry: "Session (5hr)" and "Weekly".</summary>
    public static string CodexLabel(string key) => CombinedLabel(key) ?? TierModel.Label(key);

    /// <summary>The label of a row on a Codex-only piece, so it cannot be taken for Claude's: "Codex session (5hr)", "Codex weekly".</summary>
    public static string CodexRowLabel(string key)
    {
        var label = CodexLabel(key);
        return "Codex " + char.ToLowerInvariant(label[0]) + label[1..];
    }

    /// <summary>True when Claude has something to draw: tiers, or a sign-in line.</summary>
    private static bool ClaudePresent(DeskUsage u) => u.Tiers.Count > 0 || u.SignInNeeded;

    /// <summary>What a piece set to <paramref name="source"/> draws: itself, except that combined with no Claude to draw and a Codex that has
    /// tiers draws Codex alone (a Codex-only setup). Combined with one source is otherwise today's piece.</summary>
    public static string Resolve(DeskUsage u, string source) =>
        source == DeskSources.Combined && !ClaudePresent(u) && u.TiersOf(AgentSources.Codex).Count > 0 ? AgentSources.Codex : source;

    /// <summary>True when two or more sources have numbers to put in one bar: Claude's tiers and Codex's.</summary>
    public static bool HasSeveralSources(DeskUsage u) => !u.SignInNeeded && u.Tiers.Count > 0 && u.TiersOf(AgentSources.Codex).Count > 0;

    /// <summary>
    /// <paramref name="u"/> as one source alone: for Codex, its tiers (the line's too) in place of Claude's, named "Codex", never a sign-in,
    /// stale when none of them is fresh; for any other source, <paramref name="u"/> itself. Lets the Claude pieces' drawing serve Codex unchanged.
    /// </summary>
    public static DeskUsage ForSource(DeskUsage u, string source, DateTimeOffset now)
    {
        if (source != AgentSources.Codex) return u;
        var tiers = u.TiersOf(AgentSources.Codex);
        bool anyFresh = tiers.Any(t => !u.IsTierStale(AgentSources.Codex, t.Key));
        return u with
        {
            Tiers = tiers, LineTiers = tiers, SignInNeeded = false, FirstSignIn = false, Account = "Codex",
            FetchedAt = anyFresh ? now : null,
        };
    }

    /// <summary>One row per tier of <paramref name="source"/>: Claude's are <see cref="Rows"/>; Codex's carry no pace mark (the pace is Claude's)
    /// and are stale per tier. None for a source that reports nothing.</summary>
    public static IReadOnlyList<DeskMeterRow> SourceRows(DeskUsage usage, string source, DateTimeOffset now, TimeZoneInfo? zone = null,
        int warnAt = MeterSignal.DefaultWarnAt, int urgentAt = MeterSignal.DefaultUrgentAt)
    {
        if (source != AgentSources.Codex) return Rows(usage, now, zone, warnAt, urgentAt);
        return [.. usage.TiersOf(source).Select(t => Row(t, CodexRowLabel(t.Key), null, now, zone, warnAt, urgentAt, usage.IsTierStale(source, t.Key)))];
    }

    private static DeskMeterRow Row(DeskTier t, string label, double? pace, DateTimeOffset now, TimeZoneInfo? zone,
        int warnAt, int urgentAt, bool stale = false) =>
        new(t.Key, label, (int)t.Utilization, Math.Min(1.0, Math.Max(0.0, t.Utilization / 100.0)), pace, ResetDateTime(t.ResetsAt, now, zone),
            MeterSignal.Level(t.Utilization, warnAt, urgentAt), stale);

    /// <summary>
    /// The rows for a combined piece: <see cref="Rows"/> (Claude's tiers, in order), each with the layout of its two-tone fill. A session or
    /// weekly row that two or more sources report is one joined row: the label "Session (5hr)" or "Weekly", the number, level and reset of the
    /// <paramref name="primary"/> source (the pace mark only when Claude is primary) and a <see cref="SourceBar.Layout"/> of every source's
    /// percent, a source with a stale tier marked so. Any other row, and every row with one source, keeps today's row and a single-segment layout.
    /// </summary>
    public static IReadOnlyList<(DeskMeterRow Row, SourceBarLayout Layout)> CombinedRows(DeskUsage usage, string primary, DateTimeOffset now,
        TimeZoneInfo? zone = null, int warnAt = MeterSignal.DefaultWarnAt, int urgentAt = MeterSignal.DefaultUrgentAt)
    {
        var rows = Rows(usage, now, zone, warnAt, urgentAt);
        var result = new List<(DeskMeterRow, SourceBarLayout)>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var claude = new SourcePercent(AgentSources.Claude, usage.Tiers[i].Utilization, false);
            var points = new List<SourcePercent> { claude };
            DeskTier? lead = null;
            if (CombinedLabel(row.Key) is { } label)
            {
                foreach (var source in AgentSources.All)
                {
                    if (source == AgentSources.Claude) continue;
                    if (usage.TiersOf(source).FirstOrDefault(t => t.Key == row.Key) is { } t)
                    {
                        points.Add(new SourcePercent(source, t.Utilization, usage.IsTierStale(source, row.Key)));
                        if (source == primary) lead = t;
                    }
                }
                if (points.Count >= 2)
                {
                    row = lead is { } l
                        ? Row(l, label, null, now, zone, warnAt, urgentAt, usage.IsTierStale(primary, row.Key))
                        : row with { Label = label };
                    result.Add((row, SourceBar.Layout(primary, points)));
                    continue;
                }
            }
            result.Add((row, SourceBar.Layout(AgentSources.Claude, [claude])));
        }
        return result;
    }

    /// <summary>The accessible name of a combined row: "Weekly. Claude 40%. Codex 70%, as of earlier." (<see cref="SourceBar.AccessibleName"/>), read
    /// back from the layout: the primary segment and every tick are the sources.</summary>
    public static string CombinedName(string label, SourceBarLayout layout)
    {
        var points = new List<SourcePercent>();
        if (layout.Primary is { } p) points.Add(new SourcePercent(p.Source, p.To * 100, p.Stale));
        points.AddRange(layout.Ticks.Select(t => new SourcePercent(t.Source, t.At * 100, t.Stale)));
        return SourceBar.AccessibleName(label, points);
    }

    /// <summary>The cells a <see cref="DeskMeterStyle.Ticks"/> meter has.</summary>
    public const int Ticks = 10;

    /// <summary>How many of <paramref name="cells"/> tick cells a fill of 0 to 1 lights (#336): to the nearest cell, but any reading
    /// above zero lights at least one, so 3% never reads as nothing.</summary>
    public static int LitTicks(double fill, int cells = Ticks)
    {
        if (!double.IsFinite(fill) || fill <= 0) return 0;
        return Math.Clamp((int)Math.Round(Math.Min(1, fill) * cells, MidpointRounding.AwayFromZero), 1, cells);
    }

    /// <summary>The short name a ring carries under it (#336): "Session" for the five-hour limit, "Week" for all models, the model
    /// for another weekly limit ("Sonnet"), the card's label for anything else.</summary>
    public static string ShortLabel(string key)
    {
        if (key == TierModel.FiveHour) return "Session";
        if (key == TierModel.SevenDay) return "Week";
        var label = TierModel.Label(key);
        const string weekly = "Weekly - ";
        return label.StartsWith(weekly, StringComparison.Ordinal) ? label[weekly.Length..] : label;
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
        // A passed reset says when it happened, as the card does (TierCardViewModel.PassedResetStr).
        if (rd.Value <= now)
            return days == 0 ? $"Reset at {t}" : $"Reset {loc.ToString("MMM d", CultureInfo.InvariantCulture)}, {t}";
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

    /// <summary>"7% session, resets 3:00   63% week", or the sign-in line (null when the reminder is
    /// off), or null with no numbers.
    /// <paramref name="zone"/> converts the session reset time (default: local).</summary>
    public static Parts? GetParts(DeskUsage u, TimeZoneInfo? zone = null)
    {
        if (u.SignInNeeded) return u.SignInReminder ? new Parts(u.Account, SignInLine(u.FirstSignIn)) : null;
        var parts = new List<string>();
        var tiers = u.LineTiers ?? u.Tiers;
        var session = tiers.FirstOrDefault(t => t.Key == TierModel.FiveHour);
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
        var week = tiers.FirstOrDefault(t => t.Key == TierModel.SevenDay);
        if (week is not null) parts.Add($"{(int)week.Utilization}% week");
        return parts.Count == 0 ? null : new Parts(u.Account, string.Join("   ", parts));
    }

    /// <summary>The Codex line: "Codex 5h 40% · wk 12%", with only the windows Codex reports; null with none.</summary>
    public static string? CodexLine(DeskUsage u)
    {
        var tiers = u.TiersOf(AgentSources.Codex);
        var parts = new List<string>();
        if (tiers.FirstOrDefault(t => t.Key == TierModel.FiveHour) is { } five) parts.Add($"5h {(int)five.Utilization}%");
        if (tiers.FirstOrDefault(t => t.Key == TierModel.SevenDay) is { } week) parts.Add($"wk {(int)week.Utilization}%");
        return parts.Count == 0 ? null : "Codex " + string.Join(" · ", parts);
    }

    /// <summary>"claude   7% session, resets 3:00   63% week", or null with nothing to say. With two
    /// or more accounts the line starts with the active label in place of "claude".</summary>
    public static string? Line(DeskUsage u, TimeZoneInfo? zone = null) =>
        GetParts(u, zone) is { } p ? $"{p.Account ?? "claude"}   {p.Rest}" : null;

    /// <summary>
    /// The line's compact two-row form (#336), so it no longer runs wide on a narrow or slanted stack: "claude   7% session" over
    /// "resets 3:00   63% week". The sign-in line goes under the name. Null with nothing to say; one row when only the name and
    /// the session fit on it.
    /// </summary>
    public static IReadOnlyList<string>? Compact(DeskUsage u, TimeZoneInfo? zone = null)
    {
        string name = u.Account ?? "claude";
        if (u.SignInNeeded) return u.SignInReminder ? [name, SignInLine(u.FirstSignIn)] : null;
        var tiers = u.LineTiers ?? u.Tiers;
        var session = tiers.FirstOrDefault(t => t.Key == TierModel.FiveHour);
        var week = tiers.FirstOrDefault(t => t.Key == TierModel.SevenDay);
        if (session is null && week is null) return null;
        string top = session is null ? name : $"{name}   {(int)session.Utilization}% session";
        var bottom = new List<string>();
        if (session is not null && Pacing.Parse(session.ResetsAt) is { } reset)
            bottom.Add($"resets {TimeZoneInfo.ConvertTime(reset, zone ?? TimeZoneInfo.Local).ToString("h:mm", CultureInfo.InvariantCulture)}");
        if (week is not null) bottom.Add($"{(int)week.Utilization}% week");
        return bottom.Count == 0 ? [top] : [top, string.Join("   ", bottom)];
    }
}

/// <summary>How far a meter is toward its limit, by the alert thresholds (theme match, the usage signal).</summary>
public enum MeterLevel { Calm, Warn, Urgent }

/// <summary>
/// The usage signal worked into a meter's gradient (theme match, spec "Meters"): under Warn at the meter is its own colors; from Warn
/// at a yellow stop sits in their middle, from Urgent at a red one; a solid meter is its color to itself, so it gets the same stop.
/// The thresholds are the alerts' (<c>alert_warn_pct</c>, default 80; <c>alert_urgent_pct</c>, default 95); the colors are the widget
/// ramp's own yellow and red (<see cref="ThemeCatalog.UsageColor"/>). Only meters carry it: the clock, date, message and Now
/// playing keep their gradient exactly. This replaces the Desk's switch to solid red at 90%.
/// </summary>
public static class MeterSignal
{
    public const int DefaultWarnAt = 80;
    public const int DefaultUrgentAt = 95;
    public const string WarnHex = "facc15";
    public const string UrgentHex = "f87171";

    public static MeterLevel Level(double percent, int warnAt, int urgentAt) =>
        percent >= urgentAt ? MeterLevel.Urgent : percent >= warnAt ? MeterLevel.Warn : MeterLevel.Calm;

    /// <summary>The signal's color, six hex digits; null when calm (the meter's text keeps its own color).</summary>
    public static string? Hex(MeterLevel level) => level switch
    {
        MeterLevel.Warn => WarnHex,
        MeterLevel.Urgent => UrgentHex,
        _ => null,
    };

    /// <summary>
    /// The stops a meter at <paramref name="level"/> draws: the given ones normalized (none usable is white); past Warn at, a solid
    /// is doubled and the signal goes in at index ceil(n / 2), so with one, two or four stops it sits exactly in the middle once the
    /// stops are spread evenly.
    /// </summary>
    public static IReadOnlyList<string> Stops(IReadOnlyList<string> stops, MeterLevel level)
    {
        var list = stops.Select(InkHex.Normalize).OfType<string>().ToList();
        if (list.Count == 0) list.Add(InkHex.Fallback);
        if (Hex(level) is not { } signal) return list;
        if (list.Count == 1) list.Add(list[0]);
        list.Insert((list.Count + 1) / 2, signal);
        return list;
    }
}
