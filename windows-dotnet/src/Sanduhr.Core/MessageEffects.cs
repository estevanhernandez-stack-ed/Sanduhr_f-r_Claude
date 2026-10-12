using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// Small text rules shared by the message files, matched to Foundation's so a line reads the same
/// on both platforms: <c>CharacterSet.whitespaces</c> (Zs and tab, not newlines) and
/// <c>CharacterSet.newlines</c>.
/// </summary>
internal static class MessageText
{
    /// <summary>Foundation's <c>.whitespaces</c>: general category Zs and tab. Newlines are not in it.</summary>
    public static bool IsHSpace(char c) =>
        c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    /// <summary>Foundation's <c>.newlines</c>: U+000A to U+000D, U+0085, U+2028, U+2029.</summary>
    public static bool IsNewline(char c) => c is >= (char)10 and <= (char)13 or (char)0x85 or (char)0x2028 or (char)0x2029;

    /// <summary><c>trimmingCharacters(in: .whitespaces)</c>.</summary>
    public static string Trim(string s)
    {
        int start = 0, end = s.Length;
        while (start < end && IsHSpace(s[start])) start++;
        while (end > start && IsHSpace(s[end - 1])) end--;
        return s[start..end];
    }

    /// <summary><c>components(separatedBy: .newlines)</c>: a split at every newline character, so CRLF leaves an empty piece.</summary>
    public static List<string> SplitNewlines(string s)
    {
        var parts = new List<string>();
        int from = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (!IsNewline(s[i])) continue;
            parts.Add(s[from..i]);
            from = i + 1;
        }
        parts.Add(s[from..]);
        return parts;
    }

    /// <summary>
    /// Swift's <c>split(whereSeparator: \.isNewline)</c> over Characters, empty pieces kept: CRLF is one
    /// separator here (it is one Character in Swift), every other newline character is its own.
    /// </summary>
    public static List<string> SplitLines(string s)
    {
        var parts = new List<string>();
        int from = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (!IsNewline(s[i])) continue;
            parts.Add(s[from..i]);
            if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
            from = i + 1;
        }
        parts.Add(s[from..]);
        return parts;
    }

    /// <summary>The first <paramref name="max"/> text elements (Swift's <c>Character</c>s) of <paramref name="s"/>.</summary>
    public static string Prefix(string s, int max)
    {
        var e = StringInfo.GetTextElementEnumerator(s);
        int end = 0, n = 0;
        while (n < max && e.MoveNext())
        {
            end = e.ElementIndex + ((string)e.Current).Length;
            n++;
        }
        return s[..end];
    }

    public static bool SequenceEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public static int SequenceHash(IReadOnlyList<string>? a)
    {
        if (a is null) return 0;
        var h = new HashCode();
        foreach (var s in a) h.Add(s, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// Per-line effects for the Desk message: tags at the start of a line's text, after any
/// <c>Mon:</c> or <c>10-31:</c> prefix, in any order. Ported from the Mac's Desk/MessageEffects.swift.
/// <code>
/// {ink:#ff2a6d,#05d9e8}   this line's ink, 1 to 4 hex colors (two or more: a gradient)
/// {glow} / {noglow}       the soft glow on or off for this line
/// {size:1.3}              0.5 to 2 times the Desk's message size
/// {write}                 the line draws itself in, left to right, once when it appears
/// {shimmer}               a slow light sweep across the line every few seconds
/// {sweep} / {sweep:20}    a three-character light across the line: once, or every 2 to 3600 seconds
/// {font:small-caps}       a letter style (bold, italic, bold-italic and small-caps in the line's own font)
/// </code>
/// </summary>
public sealed record MessageEffects
{
    /// <summary>Hex colors without "#", 1 to 4; null keeps the message color from Settings.</summary>
    public IReadOnlyList<string>? Ink { get; init; }
    /// <summary>Null keeps the global glow.</summary>
    public bool? Glow { get; init; }
    /// <summary>A factor from 0.5 to 2; null is 1.</summary>
    public double? Size { get; init; }
    public bool Write { get; init; }
    public bool Shimmer { get; init; }
    /// <summary><c>{font:...}</c>: the line's letter style; null draws it as written.</summary>
    public LetterStyle? Font { get; init; }
    /// <summary><c>{sweep}</c>: a three-character light runs across the line.</summary>
    public bool Sweep { get; init; }
    /// <summary><c>{sweep:seconds}</c>: and again every this many seconds; null is once.</summary>
    public double? SweepPeriod { get; init; }

    public bool Equals(MessageEffects? other) =>
        other is not null && MessageText.SequenceEqual(Ink, other.Ink) && Glow == other.Glow && Size == other.Size
        && Write == other.Write && Shimmer == other.Shimmer && Font == other.Font && Sweep == other.Sweep
        && SweepPeriod == other.SweepPeriod;

    public override int GetHashCode() =>
        HashCode.Combine(MessageText.SequenceHash(Ink), Glow, Size, Write, Shimmer, Font, Sweep, SweepPeriod);
}

/// <summary>Why a strict parse failed, kept apart from its wording.</summary>
public enum MessageFailureKind
{
    Unclosed, Unknown, TakesNoValue, InkMissing, InkTooMany, InkNotHex, Size, Font, Sweep, NoText,
}

/// <summary>A strict parse failure; <see cref="Reason"/> words it as the MCP server does.</summary>
public readonly record struct MessageFailure(MessageFailureKind Kind, string? Name = null)
{
    public static MessageFailure Unclosed => new(MessageFailureKind.Unclosed);
    public static MessageFailure Unknown(string name) => new(MessageFailureKind.Unknown, name);
    public static MessageFailure TakesNoValue(string name) => new(MessageFailureKind.TakesNoValue, name);
    public static MessageFailure InkMissing => new(MessageFailureKind.InkMissing);
    public static MessageFailure InkTooMany => new(MessageFailureKind.InkTooMany);
    public static MessageFailure InkNotHex => new(MessageFailureKind.InkNotHex);
    public static MessageFailure Size => new(MessageFailureKind.Size);
    public static MessageFailure Font => new(MessageFailureKind.Font);
    public static MessageFailure Sweep => new(MessageFailureKind.Sweep);
    public static MessageFailure NoText => new(MessageFailureKind.NoText);

    /// <summary>The wording the MCP server uses (and the Mac's tests pin).</summary>
    public string Reason => Kind switch
    {
        MessageFailureKind.Unclosed => "an effect tag is not closed with }",
        MessageFailureKind.Unknown => $"unknown effect {{{MessageText.Prefix(Name ?? "", 20)}}}; known: {MessageMarkup.EffectNames}",
        MessageFailureKind.TakesNoValue => $"{{{Name}}} takes no value",
        MessageFailureKind.InkMissing => "{ink:...} needs 1 to 4 hex colors, like {ink:#ff2a6d,#05d9e8}",
        MessageFailureKind.InkTooMany => "{ink:...} takes at most 4 colors",
        MessageFailureKind.InkNotHex => "{ink:...} has a color that is not hex (#rgb or #rrggbb)",
        MessageFailureKind.Size => "{size:...} needs a number from 0.5 to 2, like {size:1.3}",
        MessageFailureKind.Font => $"{{font:...}} needs a letter style: {LetterStyles.TagNames}",
        MessageFailureKind.Sweep => "{sweep:...} takes a period in seconds from 2 to 3600, like {sweep:20}",
        _ => "the line has effects but no text",
    };
}

/// <summary>A line with its tags read off: the effects and what is drawn (the text without its tags).</summary>
public sealed record MessageParsed(MessageEffects Effects, string Text)
{
    public MessageParsed(string text) : this(new MessageEffects(), text) { }
}

/// <summary>A strict parse: either the parsed line or the failure.</summary>
public readonly record struct MessageStrictResult(MessageParsed? Value, MessageFailure? Failure)
{
    public bool IsSuccess => Failure is null;
}

/// <summary>
/// The tag grammar. The Desk is lenient (<see cref="Parse"/>): an unknown or malformed tag ends the
/// tags, and it and everything after it draw as plain text, so a typo never hides a line. A proposal
/// from Claude is strict (<see cref="ParseStrict"/>): every tag must parse and text must follow.
/// </summary>
public static class MessageMarkup
{
    public const string EffectNames = "ink, glow, noglow, size, write, shimmer, sweep, font";
    public const int MaxInkColors = 4;
    public const double SizeMin = 0.5, SizeMax = 2;
    public const double SweepPeriodMin = 2, SweepPeriodMax = 3600;

    private static readonly Regex Number = new(@"^\d+(\.\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex = new(@"^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant);

    /// <summary>Every tag must parse and text must follow.</summary>
    public static MessageStrictResult ParseStrict(string body)
    {
        var (parsed, failure) = Scan(body);
        return failure is { } f ? new MessageStrictResult(null, f) : new MessageStrictResult(parsed, null);
    }

    /// <summary>
    /// For drawing: good tags apply; from the first bad one on, the rest is text. A line that is
    /// nothing but tags draws as written, never as nothing.
    /// </summary>
    public static MessageParsed Parse(string body)
    {
        var (parsed, failure) = Scan(body);
        if (failure is { Kind: MessageFailureKind.NoText })
            return new MessageParsed(MessageText.Trim(body));
        return parsed;
    }

    private static string DropBlanks(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s[i..];
    }

    private static (MessageParsed, MessageFailure?) Scan(string body)
    {
        var effects = new MessageEffects();
        string rest = DropBlanks(body);
        while (rest.Length > 0 && rest[0] == '{')
        {
            int close = rest.IndexOf('}');
            if (close < 0)
                return (new MessageParsed(effects, rest), MessageFailure.Unclosed);
            string inside = rest[1..close];
            var failure = Apply(inside, ref effects);
            if (failure is { } f)
                return (new MessageParsed(effects, rest), f);
            rest = DropBlanks(rest[(close + 1)..]);
        }
        string text = MessageText.Trim(rest);
        return (new MessageParsed(effects, text), text.Length == 0 ? MessageFailure.NoText : null);
    }

    private static MessageFailure? Apply(string tag, ref MessageEffects effects)
    {
        int colon = tag.IndexOf(':');
        bool hasValue = colon >= 0;
        string name = MessageText.Trim(hasValue ? tag[..colon] : tag).ToLowerInvariant();
        string value = hasValue ? MessageText.Trim(tag[(colon + 1)..]) : "";
        switch (name)
        {
            case "glow" or "noglow" or "write" or "shimmer":
                if (hasValue) return MessageFailure.TakesNoValue(name);
                effects = name switch
                {
                    "glow" => effects with { Glow = true },
                    "noglow" => effects with { Glow = false },
                    "write" => effects with { Write = true },
                    _ => effects with { Shimmer = true },
                };
                return null;
            case "ink":
            {
                var colors = hasValue ? value.Split(',').Select(MessageText.Trim).ToArray() : [];
                if (colors.Length == 0 || colors.Any(c => c.Length == 0)) return MessageFailure.InkMissing;
                if (colors.Length > MaxInkColors) return MessageFailure.InkTooMany;
                if (!colors.All(IsHex)) return MessageFailure.InkNotHex;
                effects = effects with { Ink = colors.Select(c => c.StartsWith('#') ? c[1..] : c).ToArray() };
                return null;
            }
            case "size":
            {
                if (!hasValue || !TryNumber(value, out double factor) || factor < SizeMin || factor > SizeMax)
                    return MessageFailure.Size;
                effects = effects with { Size = factor };
                return null;
            }
            case "font":
            {
                if (!hasValue || LetterStyles.Parse(value) is not { } style) return MessageFailure.Font;
                effects = effects with { Font = style };
                return null;
            }
            case "sweep":
                if (hasValue)
                {
                    if (!TryNumber(value, out double period) || period < SweepPeriodMin || period > SweepPeriodMax)
                        return MessageFailure.Sweep;
                    effects = effects with { SweepPeriod = period, Sweep = true };
                }
                else
                {
                    effects = effects with { SweepPeriod = null, Sweep = true };
                }
                return null;
            default:
                return MessageFailure.Unknown(name);
        }
    }

    /// <summary>The Mac's <c>^\d+(\.\d+)?$</c> then <c>Double(value)</c>; no signs, exponents or blanks.</summary>
    private static bool TryNumber(string value, out double result)
    {
        result = 0;
        return Number.IsMatch(value)
            && double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>"#rgb", "#rrggbb", with or without the "#".</summary>
    public static bool IsHex(string s) => Hex.IsMatch(s);
}

/// <summary>
/// What the effects mean on screen, decided apart from the drawing so each rule is tested. Ported
/// from the Mac's <c>MessageMotion</c>; times are seconds.
/// </summary>
public static class MessageMotion
{
    /// <summary>How long <c>{write}</c> takes to draw a line in.</summary>
    public const double WriteDuration = 1.5;
    /// <summary>A <c>{shimmer}</c> sweep every this many seconds while the line shows...</summary>
    public const double ShimmerPeriod = 8;
    /// <summary>...taking this long to cross it; the rest of the period nothing moves.</summary>
    public const double ShimmerSweep = 1.6;
    /// <summary>The sweep's band, as a share of the line's width.</summary>
    public const double ShimmerBand = 0.3;

    /// <summary>The ink spec for a line: its own colors, else the global message color.</summary>
    public static string InkSpec(MessageEffects e, string global) =>
        e.Ink is { } ink ? string.Join(",", ink) : global;

    public static bool Glows(MessageEffects e, bool global) => e.Glow ?? global;

    public static double Size(MessageEffects e, double baseSize) => baseSize * (e.Size ?? 1);

    /// <summary><c>{write}</c> animates; with animations off the line is simply there.</summary>
    public static bool AnimatesWrite(MessageEffects e, bool reduceMotion) => e.Write && !reduceMotion;

    /// <summary>
    /// <c>{shimmer}</c> runs only while it can be seen: not with animations off, not while the Desk is
    /// covered, the screens sleep or the session is switched away (<paramref name="paused"/>).
    /// </summary>
    public static bool Shimmers(MessageEffects e, bool reduceMotion, bool paused) =>
        e.Shimmer && !reduceMotion && !paused;

    // {sweep}, the owner's now-playing mod's light (glow.ts): three characters wide, brightening
    // toward white, about a second to cross.

    /// <summary>How long one <c>{sweep}</c> takes to cross the line.</summary>
    public const double SweepRun = 1.1;
    /// <summary>How many characters the light spans on each side of its center.</summary>
    public const double SweepWidth = 3;
    /// <summary>How far toward white the center goes (0 to 1).</summary>
    public const double SweepStrength = 0.75;
    /// <summary>Frames while the light moves (none between sweeps): 30 a second.</summary>
    public const double SweepFrame = 1.0 / 30;
    /// <summary>The first sweep, after the line appears (or the Desk comes back into sight).</summary>
    public const double SweepAppearDelay = 0.6;
    /// <summary>Settings' Replay sweeps sooner.</summary>
    public const double SweepReplayDelay = 0.3;

    /// <summary>
    /// Where the light's center sits <paramref name="fraction"/> (0 to 1) of the way through a sweep of
    /// <paramref name="length"/> characters, or null outside the sweep. It starts <see cref="SweepWidth"/>
    /// before the first character and ends as far past the last, so it enters and leaves softly.
    /// </summary>
    public static double? SweepAt(double fraction, int length)
    {
        if (!(fraction >= 0 && fraction < 1) || length <= 0) return null;
        return fraction * (length + SweepWidth * 2) - SweepWidth;
    }

    /// <summary>
    /// How far character <paramref name="index"/> blends toward white with the light at <paramref name="at"/>:
    /// <see cref="SweepStrength"/> at the center, falling to 0 at <see cref="SweepWidth"/> characters away.
    /// </summary>
    public static double SweepLight(int index, double? at)
    {
        if (at is not { } center) return 0;
        double k = 1 - Math.Abs(index - center) / SweepWidth;
        return k > 0 ? k * SweepStrength : 0;
    }

    /// <summary>
    /// When <c>{sweep}</c> runs: the wait before the first sweep and, for <c>{sweep:seconds}</c>, the time
    /// from one sweep's start to the next; null when nothing moves: no <c>{sweep}</c>, animations off,
    /// out of sight (<paramref name="paused"/>, as <c>{shimmer}</c> rests), or a once-only sweep that has
    /// already run for this line. Coming back into sight starts a periodic one again, and a once-only one
    /// that had not run yet.
    /// </summary>
    public static (double First, double? Every)? SweepPlan(MessageEffects e, bool reduceMotion, bool paused, bool sweptOnce, bool replay)
    {
        if (!e.Sweep || reduceMotion || paused) return null;
        if (e.SweepPeriod is null && sweptOnce) return null;
        double? every = e.SweepPeriod is { } p ? Math.Max(p, SweepRun) : null;
        return (replay ? SweepReplayDelay : SweepAppearDelay, every);
    }

    /// <summary>
    /// The Desk is out of sight: its window fully covered, the screens asleep, the screen saver on, or
    /// this user's session switched away.
    /// </summary>
    public static bool Paused(bool deskVisible, bool screensAsleep, bool screenSaver, bool sessionAway) =>
        !deskVisible || screensAsleep || screenSaver || sessionAway;
}

/// <summary>The message's look settings that the effects override (Settings, Desk, Message).</summary>
public static class DeskMessageLook
{
    /// <summary>
    /// The soft glow around the message is on by default; <c>{glow}</c> and <c>{noglow}</c> override it per
    /// line. The Mac keeps this under a UserDefaults key; on Windows it is the <c>glow</c> member of
    /// <c>desk_message</c> and the App passes it in.
    /// </summary>
    public const bool GlowDefault = true;

    /// <summary>The glow's blur radius in device-independent pixels (spec 6.2).</summary>
    public const double GlowBlur = 18;

    /// <summary>The glow's opacity (spec 6.2).</summary>
    public const double GlowOpacity = 0.55;
}
