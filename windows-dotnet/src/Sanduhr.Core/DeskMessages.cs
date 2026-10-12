using System.Globalization;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What a trimmed line's prefix says about when it shows.</summary>
public enum MessagePrefixKind { Date, Weekday, Plain }

/// <summary>A line split into its prefix kind, the prefix tag ("10-31", "Mon") and the body.</summary>
public readonly record struct MessagePrefix(MessagePrefixKind Kind, string Tag, string Body);

/// <summary>The file's message lines, sorted into pools by prefix.</summary>
public sealed class MessagePools
{
    /// <summary>Date lines by "MM-DD", in file order.</summary>
    public Dictionary<string, List<string>> Dated { get; } = new(StringComparer.Ordinal);
    /// <summary>Weekday lines by "Mon" to "Sun", in file order.</summary>
    public Dictionary<string, List<string>> Weekday { get; } = new(StringComparer.Ordinal);
    public List<string> Plain { get; } = [];
}

/// <summary>
/// What the Desk draws today: the date's special lines (tags on, prefix gone) and the usual line.
/// A pinned line is the usual line; the date's lines still show above it.
/// </summary>
public sealed class MessageToday : IEquatable<MessageToday>
{
    public MessageToday(IReadOnlyList<string>? special = null, string? usual = null)
    {
        Special = special ?? [];
        Usual = usual;
    }

    /// <summary>Lines for today's date, up to <see cref="DeskMessages.MaxSpecial"/>.</summary>
    public IReadOnlyList<string> Special { get; }

    /// <summary>The day's usual line (or the pin), null when there is none.</summary>
    public string? Usual { get; }

    /// <summary>Special lines first, then the usual one.</summary>
    public IReadOnlyList<string> Lines => Usual is null ? Special : [.. Special, Usual];

    /// <summary>For places with room for one line: the first special line, else the usual one.</summary>
    public string? First => Special.Count > 0 ? Special[0] : Usual;

    public bool Equals(MessageToday? other) =>
        other is not null && MessageText.SequenceEqual(Special, other.Special) && Usual == other.Usual;

    public override bool Equals(object? obj) => Equals(obj as MessageToday);

    public override int GetHashCode() => HashCode.Combine(MessageText.SequenceHash(Special), Usual);
}

/// <summary>
/// The desktop message: the line in your handwriting that used to be baked into the wallpaper. Ported
/// from the Mac's Desk/Message.swift (<c>MessageEngine</c>), minus the file location and the settings
/// reads: the App passes the file's text and the settings in.
/// <code>
///   keep building.                 any day
///   Mon: one thing at a time.      only on Mondays (Mon Tue Wed Thu Fri Sat Sun)
///   10-31: happy halloween.        on that date (MM-DD), above the day's usual line
///   # a comment                    ignored, as are blank lines
///   {ink:#ff2a6d,#05d9e8} {glow} hi   effects at the start of the text (MessageEffects.cs)
/// </code>
/// Each day shows its usual line and, on a date with its own lines, those too:
/// the usual line is today's weekday lines if there are any, else the plain lines; with mix on, the
/// weekday lines and the plain lines take turns together. Plain lines rotate by day; a weekday's lines
/// rotate by week (that weekday comes round once a week, so stepping by day would stall on a pool whose
/// size shares a factor with 7). Special lines: every line for today's date shows, stacked above the
/// usual line, up to <see cref="MaxSpecial"/>; more than that take turns hourly, that many at a time.
/// Rotate hourly turns every pool hourly, steady within the hour. A pinned line replaces the usual line;
/// the date's lines still stack above it.
/// </summary>
public static class DeskMessages
{
    /// <summary>Lines for one date shown at once.</summary>
    public const int MaxSpecial = 3;

    /// <summary>The file the first time, so there is something to edit (the Mac writes this plus a newline).</summary>
    public const string Starter =
        "# Desk messages. One per line; Desk picks one a day.\n" +
        "#   Mon: text      only on Mondays\n" +
        "#   10-31: text    only on that date\n" +
        "#   {ink:#ff2a6d,#05d9e8} {glow} {size:1.2} {write} {shimmer} text    effects\n" +
        "keep building.\n" +
        "Mon: one thing at a time.\n" +
        "Fri: showtime.";

    /// <summary>Indexed by the weekday number 0 (Sunday) to 6, as <see cref="DayOfWeek"/> counts.</summary>
    public static readonly IReadOnlyList<string> Weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly Regex DateTag = new(@"^\d{2}-\d{2}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Foundation's Gregorian calendar counts days in the era from 1 January of year 1 on the Julian
    /// calendar (it switches to Julian before 15 October 1582), which is 30 December of year 0 on the
    /// proleptic Gregorian calendar .NET uses, and numbers that day 1. So the Mac's
    /// <c>ordinality(of: .day, in: .era)</c> is <see cref="DateOnly.DayNumber"/> (days from 0001-01-01
    /// Gregorian, zero based) plus this. Derived from the Julian day number (2000-01-01 is 2451545; the
    /// Julian calendar's 0001-01-01 is 1721424, so 2000-01-01 is day 730122 of the era) and pinned by a test.
    /// </summary>
    public const int EraDayOffset = 3;

    /// <summary>The Mac's day index (<c>ordinality(of: .day, in: .era)</c>) for a calendar date.</summary>
    public static int DayIndex(DateOnly date) => date.DayNumber + EraDayOffset;

    /// <summary>
    /// A trimmed line's prefix: "10-31: text" is a date line, "Mon: text" a weekday line, anything else
    /// (a colon after other words included) is plain and its body is the whole line. The body keeps its
    /// effect tags; the Desk reads them.
    /// </summary>
    public static MessagePrefix SplitPrefix(string line)
    {
        int colon = line.IndexOf(':');
        if (colon >= 0)
        {
            string tag = MessageText.Trim(line[..colon]);
            string body = MessageText.Trim(line[(colon + 1)..]);
            if (DateTag.IsMatch(tag)) return new MessagePrefix(MessagePrefixKind.Date, tag, body);
            if (Weekdays.Contains(tag)) return new MessagePrefix(MessagePrefixKind.Weekday, tag, body);
        }
        return new MessagePrefix(MessagePrefixKind.Plain, "", line);
    }

    /// <summary>The file's message lines, sorted into pools by prefix.</summary>
    public static MessagePools Pools(string text)
    {
        var p = new MessagePools();
        foreach (var raw in MessageText.SplitNewlines(text))
        {
            string line = MessageText.Trim(raw);
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var split = SplitPrefix(line);
            switch (split.Kind)
            {
                case MessagePrefixKind.Date:
                    if (split.Body.Length > 0) Add(p.Dated, split.Tag, split.Body);
                    break;
                case MessagePrefixKind.Weekday:
                    if (split.Body.Length > 0) Add(p.Weekday, split.Tag, split.Body);
                    break;
                default:
                    p.Plain.Add(line);
                    break;
            }
        }
        return p;
    }

    private static void Add(Dictionary<string, List<string>> pool, string key, string line)
    {
        if (!pool.TryGetValue(key, out var list)) pool[key] = list = [];
        list.Add(line);
    }

    /// <summary>
    /// What the Desk draws at <paramref name="now"/>, read in <paramref name="zone"/> (the machine's zone
    /// when null). A null <paramref name="text"/> is a messages file that could not be read: the pin alone.
    /// <paramref name="pinned"/> replaces the usual line only; the date's lines still stack above it.
    /// </summary>
    public static MessageToday Today(string? text, DateTimeOffset now, bool hourly, bool mix = false,
        string? pinned = null, TimeZoneInfo? zone = null)
    {
        if (string.IsNullOrEmpty(pinned)) pinned = null;
        if (text is null) return new MessageToday(usual: pinned);

        var local = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local).DateTime;
        var p = Pools(text);
        string date = string.Create(CultureInfo.InvariantCulture, $"{local.Month:00}-{local.Day:00}");
        string weekday = Weekdays[(int)local.DayOfWeek];
        int hour = local.Hour;
        int dayIndex = DayIndex(DateOnly.FromDateTime(local));
        int weekIndex = dayIndex / 7;

        string? Turn(List<string> pool, int step)
        {
            if (pool.Count == 0) return null;
            int slot = hourly ? step * 24 + hour : step;
            return pool[slot % pool.Count];
        }

        var days = p.Weekday.TryGetValue(weekday, out var d) ? d : [];
        string? usual;
        if (pinned is not null) usual = pinned;
        else if (days.Count == 0) usual = Turn(p.Plain, dayIndex);
        else usual = Turn(mix ? [.. days, .. p.Plain] : days, weekIndex);

        var dated = p.Dated.TryGetValue(date, out var dl) ? dl : [];
        return new MessageToday(Special(dated, local.Year, hour), usual);
    }

    /// <summary>
    /// A date's lines: all of them up to <see cref="MaxSpecial"/>; past that, that many at a time, the
    /// window moving on each hour (and each year), wrapping round the list.
    /// </summary>
    public static IReadOnlyList<string> Special(IReadOnlyList<string> lines, int year, int hour)
    {
        if (lines.Count <= MaxSpecial) return lines;
        int start = (year * 24 + hour) * MaxSpecial % lines.Count;
        var picked = new string[MaxSpecial];
        for (int i = 0; i < MaxSpecial; i++) picked[i] = lines[(start + i) % lines.Count];
        return picked;
    }

    /// <summary>The usual line (the Desk's single-line pick before special days).</summary>
    public static string? Pick(string text, DateTimeOffset now, bool hourly, bool mix = false, TimeZoneInfo? zone = null) =>
        Today(text, now, hourly, mix, null, zone).Usual;
}

/// <summary>How a change of line is drawn.</summary>
public enum SpecialChange { None, Crossfade, Scroll }

/// <summary>
/// On special days: how the date's lines and the usual line share the message piece. Stack draws them
/// all, the date's lines on top; Take turns shows one at a time with a crossfade; Scroll moves them up
/// one after another like a slow ticker. The line showing is decided by the clock alone
/// (<see cref="Index"/>), so every surface agrees without talking. Ported from the Mac's
/// <c>MessageSpecialMode</c>; the saved setting is read by the App, which passes it to
/// <see cref="Parse"/> and <see cref="NormalizeSeconds"/>.
/// </summary>
public enum MessageSpecialMode { Stack, Turns, Scroll }

/// <summary>Words, titles and the clock rules for <see cref="MessageSpecialMode"/>.</summary>
public static class MessageSpecialModes
{
    public static readonly IReadOnlyList<MessageSpecialMode> All = [MessageSpecialMode.Stack, MessageSpecialMode.Turns, MessageSpecialMode.Scroll];

    public const double DefaultSeconds = 10;

    /// <summary>The timing menu's presets, in seconds.</summary>
    public static readonly IReadOnlyList<double> Presets = [5, 10, 30, 60, 300];

    /// <summary>The settings word: "stack", "turns" or "scroll".</summary>
    public static string Word(MessageSpecialMode m) => m switch
    {
        MessageSpecialMode.Turns => "turns",
        MessageSpecialMode.Scroll => "scroll",
        _ => "stack",
    };

    /// <summary>The saved choice; anything unknown (or absent) is Stack.</summary>
    public static MessageSpecialMode Parse(string? word) =>
        All.FirstOrDefault(m => Word(m) == word, MessageSpecialMode.Stack);

    /// <summary>The saved time each line shows: one of the presets, else the default (10 s).</summary>
    public static double NormalizeSeconds(double seconds) => Presets.Contains(seconds) ? seconds : DefaultSeconds;

    public static string Title(MessageSpecialMode m) => m switch
    {
        MessageSpecialMode.Turns => "Take turns",
        MessageSpecialMode.Scroll => "Scroll",
        _ => "Stack",
    };

    public static string Help(MessageSpecialMode m) => m switch
    {
        MessageSpecialMode.Turns => "One line at a time, fading from the special lines to the usual line and round again.",
        MessageSpecialMode.Scroll => "One line at a time, each gliding up out of view as the next comes in, like a slow ticker.",
        _ => "The day's special lines sit above its usual line, all at once.",
    };

    /// <summary>"5 s", "10 s", "30 s", "1 min", "5 min".</summary>
    public static string Label(double seconds) =>
        seconds >= 60
            ? $"{(int)(seconds / 60)} min"
            : $"{(int)seconds} s";

    /// <summary>With animations off, Take turns swaps at once, and Scroll does the same.</summary>
    public static SpecialChange Change(MessageSpecialMode mode, bool reduceMotion) => mode switch
    {
        MessageSpecialMode.Turns => reduceMotion ? SpecialChange.None : SpecialChange.Crossfade,
        MessageSpecialMode.Scroll => reduceMotion ? SpecialChange.None : SpecialChange.Scroll,
        _ => SpecialChange.None,
    };

    /// <summary>The crossfade's length, seconds.</summary>
    public const double Fade = 0.4;
    /// <summary>Scroll's glide: the old line up and out while the new one comes up in, seconds.</summary>
    public const double Glide = 0.8;

    /// <summary>Whether the lines cycle: Take turns or Scroll on a special day (more than one line).</summary>
    public static bool Cycles(MessageSpecialMode mode, MessageToday today) =>
        mode != MessageSpecialMode.Stack && today.Special.Count > 0 && today.Lines.Count > 1;

    /// <summary>Which of <paramref name="count"/> lines shows at <paramref name="unixSeconds"/>, each for <paramref name="seconds"/>, by the clock.</summary>
    public static int Index(double unixSeconds, int count, double seconds)
    {
        if (count <= 1 || seconds <= 0) return 0;
        long step = (long)Math.Floor(unixSeconds / seconds);
        return (int)(((step % count) + count) % count);
    }

    /// <summary>When the line next changes after <paramref name="unixSeconds"/>, in Unix seconds.</summary>
    public static double NextChange(double unixSeconds, double seconds) =>
        (Math.Floor(unixSeconds / seconds) + 1) * seconds;

    /// <summary>A moment as the Unix seconds <see cref="Index"/> and <see cref="NextChange"/> take.</summary>
    public static double ToUnixSeconds(DateTimeOffset t) => t.ToUnixTimeMilliseconds() / 1000.0;
}
