using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>One local day of coding as growth counts it: the date, and the active time that day (zero when the source
/// only knows the day had activity, as the Claude Code session logs do).</summary>
public readonly record struct CodingDay(DateOnly Date, TimeSpan Active);

/// <summary>
/// The companion's growth (FORMAT 6, spec 3.8 section 4): a number from 0 to 1 computed from <b>coding days</b>, never
/// from tokens or usage spent, so a user well under pace grows exactly as fast as one at the cap. Time away pauses it;
/// it never goes backwards (<see cref="CompanionGrowthState"/> keeps the highest value reached).
///
/// <para><b>The curve</b> is a constant, piecewise linear in distinct coding days through these knots:
/// 0 days = 0, 3 days = 0.10, 10 days = 0.35, 30 days = 0.75, 60 days = 1. A growth value <c>g</c> between two knots
/// <c>(d0, g0)</c> and <c>(d1, g1)</c> is reached on day <c>d0 + (g - g0) / (g1 - g0) * (d1 - d0)</c>. The knots are the
/// first-party hourglass pack's stage thresholds (0, 0.1, 0.35, 0.75), so a daily user sees stage 2 on day 3, stage 3 on
/// day 10 and the last stage on day 30, about a month (Este's ruling, 2026-10-22). Any other pack's <c>min_growth</c>
/// values map through the same curve: a stage at 0.5 arrives at day 17.5, so on day 18; a two-stage pack with its second
/// stage at 0.1 grows up on day 3. Packs pick the moment in growth; Sanduhr owns how growth relates to days.</para>
///
/// <para><b>Active time</b> is read and shown, and gates nothing beyond "the day had some": a long day does not outgrow
/// a short one, in the same spirit as never rewarding usage.</para>
/// </summary>
public static class CompanionGrowth
{
    /// <summary>The curve's knots: (coding days, growth).</summary>
    public static readonly IReadOnlyList<(double Days, double Growth)> Knots =
        [(0, 0), (3, 0.10), (10, 0.35), (30, 0.75), (60, 1.0)];

    /// <summary>Growth after <paramref name="codingDays"/> distinct coding days.</summary>
    public static double FromDays(int codingDays)
    {
        if (codingDays <= 0) return 0;
        for (int i = 1; i < Knots.Count; i++)
        {
            var (d0, g0) = Knots[i - 1];
            var (d1, g1) = Knots[i];
            if (codingDays <= d1)
                return Math.Round(g0 + (codingDays - d0) / (d1 - d0) * (g1 - g0), 6);
        }
        return 1;
    }

    /// <summary>The fewest coding days at which growth reaches <paramref name="growth"/> (the curve's inverse, rounded up to
    /// a whole day). 0 for 0 or less; the last knot's days for 1 or more.</summary>
    public static int DaysFor(double growth)
    {
        if (!(growth > 0)) return 0;
        for (int i = 1; i < Knots.Count; i++)
        {
            var (d0, g0) = Knots[i - 1];
            var (d1, g1) = Knots[i];
            if (growth <= g1 + 1e-9)
            {
                int days = (int)Math.Ceiling(d0 + (growth - g0) / (g1 - g0) * (d1 - d0) - 1e-9);
                // Rounding up from the inverse can land a hair short of the threshold the curve itself rounds to.
                while (FromDays(days) + 1e-9 < growth) days++;
                return days;
            }
        }
        return (int)Knots[^1].Days;
    }

    /// <summary>The stage shown at <paramref name="growth"/> (FORMAT 6): the stage with the highest <c>min_growth</c> that growth
    /// has reached. The first stage's threshold is 0, so there is always one.</summary>
    public static int StageIndex(IReadOnlyList<double> minGrowth, double growth)
    {
        int at = 0;
        for (int i = 0; i < minGrowth.Count; i++)
            if (growth + 1e-9 >= minGrowth[i]) at = i;
        return at;
    }
}

/// <summary>
/// Reads Sanduhr Time's day files (<c>%USERPROFILE%\.sanduhr\time\days\&lt;yyyy-mm-dd&gt;.json</c>), read-only. A day file is
/// v1 (<c>totals.claudeMs</c>) or v2 (<c>totals.agentsMs</c>); both carry <c>totals.totalMs</c>, the union of all time,
/// which is what a coding day is measured by (v1 files written without it are summed from their parts).
/// </summary>
public static class SanduhrTimeDays
{
    /// <summary>A day file is at most this big; anything larger is skipped unread.</summary>
    public const long MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>The day one file describes, or null when it is not a day file of version 1 or 2 with a date and some time.</summary>
    public static CodingDay? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version) || version is not (1 or 2))
                return null;
            if (!root.TryGetProperty("date", out var d) || d.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return null;
            if (!root.TryGetProperty("totals", out var totals) || totals.ValueKind != JsonValueKind.Object) return null;
            double ms = Ms(totals, "totalMs");
            if (ms <= 0)
                ms = Ms(totals, "youMs") + Ms(totals, "bothMs") + Ms(totals, version == 1 ? "claudeMs" : "agentsMs");
            if (!(ms > 0)) return null;
            return new CodingDay(date, TimeSpan.FromMilliseconds(Math.Min(ms, TimeSpan.FromDays(1).TotalMilliseconds)));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Every coding day in <paramref name="daysDir"/>, one per date (the longest when a date appears twice).
    /// Empty when the folder is absent or unreadable. Reads only files named like a date.</summary>
    public static IReadOnlyList<CodingDay> Read(string daysDir)
    {
        var byDate = new Dictionary<DateOnly, CodingDay>();
        try
        {
            if (!Directory.Exists(daysDir)) return [];
            foreach (var file in Directory.EnumerateFiles(daysDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != 10 || !DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    continue;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > MaxFileBytes) continue;
                    if (Parse(File.ReadAllBytes(file)) is { } day
                        && (!byDate.TryGetValue(day.Date, out var had) || had.Active < day.Active))
                        byDate[day.Date] = day;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return [.. byDate.Values.OrderBy(d => d.Date)];
    }

    /// <summary>True when the folder exists and holds at least one day file: Sanduhr Time is installed and has written.</summary>
    public static bool Present(string daysDir)
    {
        try { return Directory.Exists(daysDir) && Directory.EnumerateFiles(daysDir, "????-??-??.json").Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    private static double Ms(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) && d > 0 ? d : 0;
}

/// <summary>
/// What the companion remembers between runs (<c>companion.json</c> in the widgets folder, so Reset removes it with the
/// packs): the highest growth reached and every coding day seen so far. Days are kept because a source forgets them
/// (Claude Code's session logs are pruned after a month); growth is kept because it never goes backwards. Nothing else:
/// no usage, no tokens, no project names.
/// </summary>
public sealed record CompanionGrowthState(double Growth, IReadOnlySet<DateOnly> Days, TimeSpan Active)
{
    public const string FileName = "companion.json";
    public const int SchemaVersion = 1;

    /// <summary>Days kept at most (a decade of daily coding); the oldest go first. Growth stays where it was.</summary>
    public const int MaxDays = 3660;

    public static CompanionGrowthState Empty { get; } = new(0, new HashSet<DateOnly>(), TimeSpan.Zero);

    /// <summary>The state after seeing <paramref name="days"/>: the days joined to those remembered, the active time taken
    /// as the larger of what was kept and what the source now totals, and growth the larger of the kept value and the curve's.</summary>
    public CompanionGrowthState Observe(IEnumerable<CodingDay> days)
    {
        var all = new HashSet<DateOnly>(Days);
        double sourceActive = 0;
        foreach (var d in days)
        {
            all.Add(d.Date);
            sourceActive += d.Active.TotalMilliseconds;
        }
        if (all.Count > MaxDays)
            all = [.. all.OrderByDescending(d => d).Take(MaxDays)];
        var active = TimeSpan.FromMilliseconds(Math.Max(Active.TotalMilliseconds, sourceActive));
        double growth = Math.Max(Growth, CompanionGrowth.FromDays(all.Count));
        return new CompanionGrowthState(Math.Clamp(growth, 0, 1), all, active);
    }

    public static CompanionGrowthState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;
            double growth = root.TryGetProperty("growth", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetDouble(out var gv) && double.IsFinite(gv)
                ? Math.Clamp(gv, 0, 1) : 0;
            var days = new HashSet<DateOnly>();
            if (root.TryGetProperty("days", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var d in list.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.String
                        && DateOnly.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        days.Add(date);
            double activeMs = root.TryGetProperty("active_ms", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out var av) && double.IsFinite(av) && av > 0
                ? av : 0;
            return new CompanionGrowthState(growth, days, TimeSpan.FromMilliseconds(activeMs));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema_version", SchemaVersion);
            w.WriteNumber("growth", Math.Round(Growth, 6));
            w.WriteNumber("active_ms", Math.Round(Active.TotalMilliseconds));
            w.WriteStartArray("days");
            foreach (var d in Days.OrderBy(d => d))
                w.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    /// <summary>Reads the state from <paramref name="file"/>; empty when it is absent or unreadable.</summary>
    public static CompanionGrowthState Load(string file)
    {
        try { return File.Exists(file) ? Parse(File.ReadAllText(file, Encoding.UTF8)) : Empty; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Empty; }
    }

    /// <summary>Writes the state through a temporary file. False when the folder could not be written.</summary>
    public bool Save(string file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
