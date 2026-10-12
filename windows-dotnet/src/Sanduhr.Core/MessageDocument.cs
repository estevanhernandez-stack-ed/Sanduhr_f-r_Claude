using System.Globalization;

namespace Sanduhr.Core;

// Settings, Message's line editor, the pure half: messages.txt read into rows and written back.
// Ported from the Mac's Desk/MessageLineModel.swift. A message line the editor can represent becomes
// a styled row (when it shows, its text, its look); one it can't (an unknown or bad tag, two motions
// on one line, a date that isn't one) stays a raw row, kept verbatim; comments and blank lines keep
// their place. Each row remembers the line it was read from and writes that line, byte for byte,
// until it is changed, so an unchanged file saves back exactly and a changed row rewrites only its
// own line. The controls emit the existing tags only (MessageEffects.cs); there is no new grammar.
//
// The Mac's value types become immutable records here; an edit makes a copy with `with`, and the
// look's mutating helpers (SetInkMode, AddStop, ...) return the changed look.

/// <summary>Which prefix a line has.</summary>
public enum MessageWhenKind { EveryDay, Weekday, Date }

/// <summary>When a line shows: the <c>Mon:</c> and <c>10-31:</c> prefixes. Build one with the factories; the default value is every day.</summary>
public readonly record struct MessageWhen(MessageWhenKind Kind, string? Day, int Month, int DayOfMonth)
{
    public static MessageWhen EveryDay => default;

    /// <summary>"Mon" to "Sun", as <see cref="DeskMessages.Weekdays"/> writes them.</summary>
    public static MessageWhen OnWeekday(string day) => new(MessageWhenKind.Weekday, day, 0, 0);

    public static MessageWhen OnDate(int month, int day) => new(MessageWhenKind.Date, null, month, day);

    /// <summary>The When menu's order: Monday first.</summary>
    public static readonly IReadOnlyList<string> Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static readonly IReadOnlyDictionary<string, string> WeekdayNames = new Dictionary<string, string>
    {
        ["Mon"] = "Monday", ["Tue"] = "Tuesday", ["Wed"] = "Wednesday", ["Thu"] = "Thursday",
        ["Fri"] = "Friday", ["Sat"] = "Saturday", ["Sun"] = "Sunday",
    };

    public static readonly IReadOnlyList<string> MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>What goes before the text: "", "Mon: " or "10-31: ".</summary>
    public string Prefix => Kind switch
    {
        MessageWhenKind.EveryDay => "",
        MessageWhenKind.Weekday => $"{Day}: ",
        _ => string.Create(CultureInfo.InvariantCulture, $"{Month:00}-{DayOfMonth:00}: "),
    };

    /// <summary>"Every day", "Fridays", "October 31".</summary>
    public string Label => Kind switch
    {
        MessageWhenKind.EveryDay => "Every day",
        MessageWhenKind.Weekday => $"{(Day is not null && WeekdayNames.TryGetValue(Day, out var n) ? n : Day)}s",
        _ => $"{MonthNames[Math.Max(0, Math.Min(11, Month - 1))]} {DayOfMonth}",
    };

    /// <summary>Days in a month, February with the 29th (a leap day's line is a fine line).</summary>
    public static int DaysInMonth(int month) => month switch
    {
        2 => 29,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    /// <summary>"10-31" as a date, null for anything that isn't one ("13-01", "02-30").</summary>
    public static MessageWhen? ParseDate(string tag)
    {
        var parts = tag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int m)
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int d)
            || m is < 1 or > 12 || d < 1 || d > DaysInMonth(m))
            return null;
        return OnDate(m, d);
    }
}

/// <summary>The Motion choice: one per line.</summary>
public enum MessageMotionKind { None, Write, Shimmer, Sweep }

/// <summary>Titles for <see cref="MessageMotionKind"/>.</summary>
public static class MessageMotionKinds
{
    public static readonly IReadOnlyList<MessageMotionKind> All =
        [MessageMotionKind.None, MessageMotionKind.Write, MessageMotionKind.Shimmer, MessageMotionKind.Sweep];

    public static string Title(MessageMotionKind k) => k switch
    {
        MessageMotionKind.Write => "Write in",
        MessageMotionKind.Shimmer => "Shimmer",
        MessageMotionKind.Sweep => "Sweep",
        _ => "None",
    };
}

/// <summary>The Color choice.</summary>
public enum MessageInkMode { Desk, Solid, Gradient }

/// <summary>
/// A line's look, as the controls set it. Every field maps to one existing tag. Immutable: the
/// editing helpers return a changed copy.
/// </summary>
public sealed record MessageLook
{
    /// <summary>Hex colors without "#", 1 to 4; null keeps the message color from Settings.</summary>
    public IReadOnlyList<string>? Ink { get; init; }
    /// <summary>Null is as the Desk (the Look page's glow switch).</summary>
    public bool? Glow { get; init; }
    /// <summary>0.5 to 2; null is 1.</summary>
    public double? Size { get; init; }
    public LetterStyle? Font { get; init; }
    public MessageMotionKind Motion { get; init; } = MessageMotionKind.None;
    /// <summary><c>{sweep:seconds}</c> as read; kept while Motion stays Sweep.</summary>
    public double? SweepPeriod { get; init; }

    public const int MaxInk = MessageMarkup.MaxInkColors;

    /// <summary>The tags in the editor's order: ink, glow, size, font, motion.</summary>
    public IReadOnlyList<string> Tags
    {
        get
        {
            var o = new List<string>();
            if (Ink is { Count: > 0 }) o.Add("{ink:" + string.Join(",", Ink.Select(c => "#" + c)) + "}");
            if (Glow is { } g) o.Add(g ? "{glow}" : "{noglow}");
            if (Size is { } s && s != 1) o.Add("{size:" + Number(s) + "}");
            if (Font is { } f) o.Add("{font:" + LetterStyles.Word(f) + "}");
            switch (Motion)
            {
                case MessageMotionKind.Write: o.Add("{write}"); break;
                case MessageMotionKind.Shimmer: o.Add("{shimmer}"); break;
                case MessageMotionKind.Sweep: o.Add(SweepPeriod is { } p ? "{sweep:" + Number(p) + "}" : "{sweep}"); break;
            }
            return o;
        }
    }

    /// <summary>"1.25", "0.5", "2": at most two decimals, no trailing zeros.</summary>
    public static string Number(double v)
    {
        string s = v.ToString("F2", CultureInfo.InvariantCulture);
        while (s.EndsWith('0')) s = s[..^1];
        if (s.EndsWith('.')) s = s[..^1];
        return s;
    }

    // Color

    public MessageInkMode InkMode =>
        Ink is not { Count: > 0 } ? MessageInkMode.Desk : Ink.Count == 1 ? MessageInkMode.Solid : MessageInkMode.Gradient;

    public static string InkModeTitle(MessageInkMode m) => m switch
    {
        MessageInkMode.Solid => "One color",
        MessageInkMode.Gradient => "Gradient",
        _ => "As the Desk",
    };

    public MessageLook SetInkMode(MessageInkMode mode)
    {
        switch (mode)
        {
            case MessageInkMode.Desk:
                return this with { Ink = null };
            case MessageInkMode.Solid:
                return this with { Ink = [Ink is { Count: > 0 } ? Ink[0] : "ff2a6d"] };
            default:
            {
                string first = Ink is { Count: > 0 } ? Ink[0] : "ff2a6d";
                return this with { Ink = Ink is { Count: > 1 } ? Ink : [first, "05d9e8"] };
            }
        }
    }

    public MessageLook AddStop()
    {
        if (Ink is not { Count: >= 2 } stops || stops.Count >= MaxInk) return this;
        return this with { Ink = [.. stops, stops[^1]] };
    }

    public MessageLook RemoveStop(int i)
    {
        if (Ink is not { Count: > 2 } stops || i < 0 || i >= stops.Count) return this;
        return this with { Ink = stops.Where((_, k) => k != i).ToArray() };
    }

    public MessageLook SetStop(int i, string hex)
    {
        if (Ink is not { } stops || i < 0 || i >= stops.Count || !MessageMarkup.IsHex(hex)) return this;
        var copy = stops.ToArray();
        copy[i] = (hex.StartsWith('#') ? hex[1..] : hex).ToLowerInvariant();
        return this with { Ink = copy };
    }

    public MessageLook SetMotion(MessageMotionKind kind) =>
        this with { SweepPeriod = kind != Motion ? null : SweepPeriod, Motion = kind };

    public bool Equals(MessageLook? other) =>
        other is not null && MessageText.SequenceEqual(Ink, other.Ink) && Glow == other.Glow && Size == other.Size
        && Font == other.Font && Motion == other.Motion && SweepPeriod == other.SweepPeriod;

    public override int GetHashCode() =>
        HashCode.Combine(MessageText.SequenceHash(Ink), Glow, Size, Font, Motion, SweepPeriod);
}

/// <summary>The owner's now-playing mod's named palettes, a gradient each.</summary>
public static class MessagePalette
{
    public sealed record Entry(string Name, IReadOnlyList<string> Colors);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("synthwave", ["ff2a6d", "d16ba5", "05d9e8"]),
        new("sunset", ["ff7e5f", "feb47b", "ffd86f"]),
        new("ocean", ["00c6ff", "4f8bff", "a18cff"]),
        new("aurora", ["00f5a0", "00d9f5", "a06bff"]),
        new("ember", ["f12711", "f5af19", "ffe259"]),
        new("bubblegum", ["ff9a9e", "f6a6d8", "a18cd1"]),
        new("toxic", ["b6ff00", "00ff9c", "00c3ff"]),
        new("gold", ["f7971e", "ffd200", "fff6b7"]),
    ];

    public static IReadOnlyList<string>? Colors(string name) => All.FirstOrDefault(p => p.Name == name)?.Colors;

    /// <summary>The palette these colors are, if they are one.</summary>
    public static string? NameOf(IReadOnlyList<string>? ink)
    {
        if (ink is null) return null;
        var lower = ink.Select(c => c.ToLowerInvariant()).ToArray();
        return All.FirstOrDefault(p => MessageText.SequenceEqual(p.Colors, lower))?.Name;
    }
}

/// <summary>The Letters menu's order: the statusline's letter styles.</summary>
public static class MessageLetters
{
    public static readonly IReadOnlyList<LetterStyle> Order =
    [
        LetterStyle.Bold, LetterStyle.Italic, LetterStyle.BoldItalic, LetterStyle.SmallCaps, LetterStyle.Script,
        LetterStyle.Fraktur, LetterStyle.DoubleStruck, LetterStyle.Sans, LetterStyle.Mono,
    ];
}

/// <summary>A line the editor represents: when, text, look.</summary>
public sealed record MessageLine
{
    public MessageWhen When { get; init; } = MessageWhen.EveryDay;
    /// <summary>Plain text, no tags.</summary>
    public string Text { get; init; } = "";
    public MessageLook Look { get; init; } = new();

    /// <summary>The text as it can sit on one line.</summary>
    public string CleanText =>
        MessageText.Trim(Text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " "));

    /// <summary>The text with its tags, no prefix: what the Desk parses, and what Pin holds.</summary>
    public string Body => string.Join(" ", [.. Look.Tags, CleanText]);

    /// <summary>The line as messages.txt holds it; null for a line with no text (left out when saved).</summary>
    public string? Written => CleanText.Length == 0 ? null : When.Prefix + Body;

    /// <summary>Why the Desk would read the written line otherwise, or null when it reads back as this.</summary>
    public string? Problem
    {
        get
        {
            if (Written is not { } written) return null;
            var back = MessageLineModel.ContentOf(written);
            if (back.Kind == MessageContentKind.Line && back.Line == Normalized) return null;
            string t = CleanText;
            if (t.StartsWith('{')) return "Text can't start with {: the Desk reads that as an effect.";
            if (t.StartsWith('#')) return "A line starting with # is a note; the Desk skips it. Add a look or a day, or start with another character.";
            if (When.Kind == MessageWhenKind.EveryDay && DeskMessages.SplitPrefix(t).Kind != MessagePrefixKind.Plain)
                return "Text that starts like a day (\"Mon:\" or \"10-31:\") shows only on that day.";
            return "The Desk would read this line differently. Switch to Text to see it.";
        }
    }

    /// <summary>As it reads back: text cleaned, size 1 as none.</summary>
    public MessageLine Normalized
    {
        get
        {
            var look = Look;
            if (look.Size == 1) look = look with { Size = null };
            if (look.Motion != MessageMotionKind.Sweep) look = look with { SweepPeriod = null };
            return this with { Text = CleanText, Look = look };
        }
    }
}

/// <summary>What one row of messages.txt is.</summary>
public enum MessageContentKind { Line, Raw, Comment, Blank }

/// <summary>A row's content: a styled line, or text kept as written.</summary>
public sealed record MessageContent
{
    private MessageContent(MessageContentKind kind, MessageLine? line, string? text)
    {
        Kind = kind;
        Line = line;
        Text = text;
    }

    public MessageContentKind Kind { get; }
    /// <summary>The styled line, for <see cref="MessageContentKind.Line"/>.</summary>
    public MessageLine? Line { get; }
    /// <summary>The line as written, for the other three kinds.</summary>
    public string? Text { get; }

    /// <summary>A message line the editor represents.</summary>
    public static MessageContent FromLine(MessageLine line) => new(MessageContentKind.Line, line, null);
    /// <summary>A message line it can't; kept as written.</summary>
    public static MessageContent Raw(string text) => new(MessageContentKind.Raw, null, text);
    public static MessageContent Comment(string text) => new(MessageContentKind.Comment, null, text);
    public static MessageContent Blank(string text) => new(MessageContentKind.Blank, null, text);
}

/// <summary>One row of messages.txt.</summary>
public sealed record MessageRow
{
    public MessageRow(MessageContent content, string? source = null, bool crlf = false, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Content = content;
        Source = source;
        SourceContent = source is null ? null : content;
        Crlf = crlf;
    }

    public Guid Id { get; init; }
    public MessageContent Content { get; init; }
    /// <summary>The line as read (without a trailing \r), null for a row added here.</summary>
    public string? Source { get; init; }
    /// <summary>What <see cref="Source"/> read as: while <see cref="Content"/> is still this, the row writes <see cref="Source"/> exactly.</summary>
    public MessageContent? SourceContent { get; init; }
    /// <summary>The line ended in \r\n.</summary>
    public bool Crlf { get; init; }

    /// <summary>A message line the Desk can show (styled or raw), not a comment or blank.</summary>
    public bool IsMessage => Content.Kind is MessageContentKind.Line or MessageContentKind.Raw;

    public MessageLine? Line => Content.Line;

    /// <summary>The row as written, null when it is left out (a new line with no text).</summary>
    public string? Written
    {
        get
        {
            string? text = Source is not null && Content == SourceContent
                ? Source
                : Content.Kind == MessageContentKind.Line ? Content.Line!.Written : Content.Text;
            return text is null ? null : Crlf ? text + "\r" : text;
        }
    }

    /// <summary>The body the Desk draws for this row (prefix gone, tags on), for its preview and Pin.</summary>
    public string? DeskBody
    {
        get
        {
            switch (Content.Kind)
            {
                case MessageContentKind.Line:
                    return Content.Line!.CleanText.Length == 0 ? null : Content.Line.Body;
                case MessageContentKind.Raw:
                    return DeskMessages.SplitPrefix(MessageText.Trim(Content.Text!)).Body;
                default:
                    return null;
            }
        }
    }
}

/// <summary>messages.txt as rows. Mutable: the edits change this document, and every other row is left as it is.</summary>
public sealed class MessageDocument : IEquatable<MessageDocument>
{
    public List<MessageRow> Rows { get; private set; } = [];

    /// <summary>The file ended with a newline (a new file does).</summary>
    public bool TrailingNewline { get; set; } = true;

    public MessageDocument() { }

    public MessageDocument(IEnumerable<MessageRow> rows, bool trailingNewline = true)
    {
        Rows = [.. rows];
        TrailingNewline = trailingNewline;
    }

    /// <summary>
    /// Reads a file's text into rows. Split on "\n" only, then a trailing "\r" marks a CRLF row, so
    /// every byte comes back from <see cref="Text"/>.
    /// </summary>
    public static MessageDocument Parse(string text)
    {
        var doc = new MessageDocument();
        if (text.Length == 0) return doc;
        var pieces = text.Split('\n').ToList();
        doc.TrailingNewline = text[^1] == '\n';
        if (doc.TrailingNewline) pieces.RemoveAt(pieces.Count - 1);
        doc.Rows = pieces.Select(piece =>
        {
            bool crlf = piece.EndsWith('\r');
            string line = crlf ? piece[..^1] : piece;
            return new MessageRow(MessageLineModel.ContentOf(line), line, crlf);
        }).ToList();
        return doc;
    }

    public MessageDocument Clone() => new(Rows, TrailingNewline);

    /// <summary>The file's text.</summary>
    public string Text
    {
        get
        {
            var lines = Rows.Select(r => r.Written).Where(w => w is not null).ToList();
            if (lines.Count == 0) return "";
            return string.Join("\n", lines) + (TrailingNewline ? "\n" : "");
        }
    }

    /// <summary>The rows the list shows, in order.</summary>
    public IReadOnlyList<MessageRow> MessageRows => Rows.Where(r => r.IsMessage).ToList();

    /// <summary>Comments and blank lines, kept where they are.</summary>
    public int NoteCount => Rows.Count - Rows.Count(r => r.IsMessage);

    public int IndexOf(Guid id) => Rows.FindIndex(r => r.Id == id);

    // Edits; every other row is left as it is.

    /// <summary>A new line after the last row; its id.</summary>
    public Guid Add(MessageLine? line = null)
    {
        var row = new MessageRow(MessageContent.FromLine(line ?? new MessageLine()), crlf: Rows.Count > 0 && Rows[^1].Crlf);
        Rows.Add(row);
        return row.Id;
    }

    /// <summary>
    /// Add Line: a new line at the top of its day's lines, before the first message row with the same
    /// When; with none, before the first message row; with no message rows, after the last row. Notes
    /// and blank lines stay where they are. Its id.
    /// </summary>
    public Guid InsertAtTopOfGroup(MessageLine? line = null)
    {
        line ??= new MessageLine();
        var row = new MessageRow(MessageContent.FromLine(line), crlf: Rows.Count > 0 && Rows[^1].Crlf);
        int at = Rows.FindIndex(r => r.IsMessage && When(r) is { } w && w == line.When);
        if (at < 0) at = Rows.FindIndex(r => r.IsMessage);
        if (at < 0) at = Rows.Count;
        Rows.Insert(at, row);
        return row.Id;
    }

    /// <summary>A copy right after the row, written as the row is; its id.</summary>
    public Guid? Duplicate(Guid id)
    {
        int i = IndexOf(id);
        if (i < 0) return null;
        var src = Rows[i];
        var copy = new MessageRow(src.Content, src.Source, src.Crlf) { SourceContent = src.SourceContent };
        Rows.Insert(i + 1, copy);
        return copy.Id;
    }

    public void Delete(Guid id) => Rows.RemoveAll(r => r.Id == id);

    /// <summary>Changes a styled row's line.</summary>
    public void Update(Guid id, Func<MessageLine, MessageLine> change)
    {
        int i = IndexOf(id);
        if (i < 0 || Rows[i].Content.Kind != MessageContentKind.Line) return;
        Rows[i] = Rows[i] with { Content = MessageContent.FromLine(change(Rows[i].Content.Line!)) };
    }

    /// <summary>Drag and drop: the row goes after <paramref name="target"/> when it came from above, before it otherwise.</summary>
    public void Move(Guid id, Guid target)
    {
        int from = IndexOf(id), to = IndexOf(target);
        if (id == target || from < 0 || to < 0) return;
        var row = Rows[from];
        Rows.RemoveAt(from);
        int t = IndexOf(target);
        if (t < 0) { Rows.Insert(from, row); return; }
        Rows.Insert(from < to ? t + 1 : t, row);
    }

    /// <summary>Move Up / Move Down: past the neighbouring message row (comments stay where they are).</summary>
    public void MoveBy(Guid id, int step)
    {
        var messages = MessageRows;
        int k = messages.ToList().FindIndex(r => r.Id == id);
        if (k < 0) return;
        int n = k + step;
        if (n < 0 || n >= messages.Count) return;
        Move(id, messages[n].Id);
    }

    /// <summary>After a save: each row's written line becomes its source; rows left out go.</summary>
    public void Rebase()
    {
        var kept = new List<MessageRow>();
        foreach (var row in Rows)
        {
            if (row.Written is not { } written) continue;
            string line = row.Crlf ? written[..^1] : written;
            kept.Add(new MessageRow(row.Content, line, row.Crlf, row.Id));
        }
        Rows = kept;
    }

    public bool Equals(MessageDocument? other) =>
        other is not null && TrailingNewline == other.TrailingNewline && Rows.SequenceEqual(other.Rows);

    public override bool Equals(object? obj) => Equals(obj as MessageDocument);

    public override int GetHashCode() => HashCode.Combine(TrailingNewline, Rows.Count);

    // What happens to a row

    /// <summary>
    /// When a row shows, for its note and the counts; null for one that never shows (no text yet, or a
    /// date that isn't one).
    /// </summary>
    public static MessageWhen? When(MessageRow row)
    {
        if (row.DeskBody is not { Length: > 0 }) return null;
        switch (row.Content.Kind)
        {
            case MessageContentKind.Line:
                return row.Content.Line!.When;
            case MessageContentKind.Raw:
            {
                var split = DeskMessages.SplitPrefix(MessageText.Trim(row.Content.Text!));
                return split.Kind switch
                {
                    MessagePrefixKind.Plain => MessageWhen.EveryDay,
                    MessagePrefixKind.Weekday => MessageWhen.OnWeekday(split.Tag),
                    _ => MessageWhen.ParseDate(split.Tag),
                };
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// The row's note: when it shows and what it does to the day's other lines, as
    /// <see cref="DeskMessages.Today"/> picks them. <paramref name="mix"/> is Settings, Message's mix switch.
    /// </summary>
    public string Note(MessageRow row, bool mix)
    {
        if (When(row) is not { } at)
            return row.Line is null ? "As written · never shows" : "Shows once it has text";
        var all = Rows.Select(When).Where(w => w is not null).Select(w => w!.Value).ToList();
        int others = all.Count(w => w == at) - 1;
        bool hasWeekdays = all.Any(w => w.Kind == MessageWhenKind.Weekday);
        var parts = new List<string> { at.Label };
        switch (at.Kind)
        {
            case MessageWhenKind.Date:
                parts.Add("shows above the day's message");
                if (others > 0) parts.Add($"with {Others(others)} that day");
                if (others + 1 > DeskMessages.MaxSpecial)
                    parts.Add($"{DeskMessages.MaxSpecial} at a time, taking turns hourly");
                break;
            case MessageWhenKind.Weekday:
                if (others > 0) parts.Add($"takes turns with {Others(others)}");
                if (mix) parts.Add("mixes with every-day lines");
                else if (others == 0) parts.Add("shows instead of the every-day lines");
                break;
            default:
                if (others > 0) parts.Add($"takes turns with {Others(others)}");
                if (hasWeekdays)
                    parts.Add(mix ? "mixes in on days with their own line" : "steps aside on days with their own line");
                break;
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"1 other", "3 others".</summary>
    public static string Others(int n) => n == 1 ? "1 other" : $"{n} others";
}

/// <summary>Reading one line of messages.txt as the Desk reads it.</summary>
public static class MessageLineModel
{
    /// <summary>What one line of messages.txt is, as the Desk reads it.</summary>
    public static MessageContent ContentOf(string line)
    {
        string t = MessageText.Trim(line);
        if (t.Length == 0) return MessageContent.Blank(line);
        if (t.StartsWith('#')) return MessageContent.Comment(line);
        var split = DeskMessages.SplitPrefix(t);
        MessageWhen when;
        switch (split.Kind)
        {
            case MessagePrefixKind.Plain:
                when = MessageWhen.EveryDay;
                break;
            case MessagePrefixKind.Weekday:
                when = MessageWhen.OnWeekday(split.Tag);
                break;
            default:
                if (MessageWhen.ParseDate(split.Tag) is not { } date) return MessageContent.Raw(line);
                when = date;
                break;
        }
        var strict = MessageMarkup.ParseStrict(split.Body);
        if (!strict.IsSuccess || LookOf(strict.Value!.Effects) is not { } look) return MessageContent.Raw(line);
        return MessageContent.FromLine(new MessageLine { When = when, Text = strict.Value.Text, Look = look });
    }

    /// <summary>The effects as a look, null when the controls can't hold them (two motions on one line).</summary>
    public static MessageLook? LookOf(MessageEffects e)
    {
        int motions = new[] { e.Write, e.Shimmer, e.Sweep }.Count(m => m);
        if (motions > 1) return null;
        var look = new MessageLook
        {
            Ink = e.Ink?.Select(c => c.ToLowerInvariant()).ToArray(),
            Glow = e.Glow,
            Size = e.Size == 1 ? null : e.Size,
            Font = e.Font,
        };
        if (e.Write) look = look with { Motion = MessageMotionKind.Write };
        if (e.Shimmer) look = look with { Motion = MessageMotionKind.Shimmer };
        if (e.Sweep) look = look with { Motion = MessageMotionKind.Sweep, SweepPeriod = e.SweepPeriod };
        return look;
    }
}
