using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

// A look per song (3.7.0, sub-project C): the pure half. Everything here is text, JSON, regex and math (no IO, no
// network, no credentials), because sanduhr-mcp links this one file to refuse exactly what the widget would. The widget
// owns every write: the saved-looks store, the handoff files and the current-look file are NowPlayingLookStore and
// NowPlayingLookHandoff, which stay out of the server.

/// <summary>The letter style of a look. The Desk draws bold, italic and bold italic with the Desk font's own weight and slant,
/// and small caps with the phonetic capitals (<see cref="LetterMap"/>); plain is the font as it is.</summary>
public enum LookStyle { Plain, Bold, Italic, BoldItalic, SmallCaps }

/// <summary>Names and drawing rules for <see cref="LookStyle"/>.</summary>
public static class LookStyles
{
    public static readonly IReadOnlyList<LookStyle> All = [LookStyle.Plain, LookStyle.Bold, LookStyle.Italic, LookStyle.BoldItalic, LookStyle.SmallCaps];

    /// <summary>The word a look's JSON carries: plain, bold, italic, bolditalic, smallcaps.</summary>
    public static string Word(LookStyle s) => s switch
    {
        LookStyle.Bold => "bold",
        LookStyle.Italic => "italic",
        LookStyle.BoldItalic => "bolditalic",
        LookStyle.SmallCaps => "smallcaps",
        _ => "plain",
    };

    /// <summary>As Settings names it.</summary>
    public static string Title(LookStyle s) => s switch
    {
        LookStyle.Bold => "Bold",
        LookStyle.Italic => "Italic",
        LookStyle.BoldItalic => "Bold italic",
        LookStyle.SmallCaps => "Small caps",
        _ => "Plain",
    };

    /// <summary>The words a look's JSON may use, as the tool lists them.</summary>
    public static readonly string WordList = string.Join(", ", All.Select(Word));

    /// <summary>A style by its word, ignoring case, spaces, hyphens and underscores (<c>bold-italic</c> and <c>Small Caps</c> are
    /// accepted); null for anything else.</summary>
    public static LookStyle? Parse(string? word)
    {
        if (word is null) return null;
        var sb = new StringBuilder(word.Length);
        foreach (char c in word.ToLowerInvariant())
            if (c is not (' ' or '-' or '_')) sb.Append(c);
        string key = sb.ToString();
        foreach (var s in All)
            if (Word(s) == key) return s;
        return null;
    }

    public static bool IsBold(LookStyle s) => s is LookStyle.Bold or LookStyle.BoldItalic;

    public static bool IsItalic(LookStyle s) => s is LookStyle.Italic or LookStyle.BoldItalic;

    /// <summary>The text as the style draws it in letters: small caps maps a to z to the phonetic capitals; the other styles
    /// are the font's weight and slant, so the text is as written.</summary>
    public static string Text(string text, LookStyle s) => s == LookStyle.SmallCaps ? LetterMap.Map(text, LetterStyle.SmallCaps) : text;
}

/// <summary>Where a song's look came from.</summary>
public enum LookSource { Saved, Seeded }

/// <summary>
/// A look: one to four gradient stops (<c>#rrggbb</c>, lowercase, left to right), a letter style and an optional mood of up to
/// forty characters, shown in Settings only. Equal when the stops, style and mood are.
/// </summary>
public sealed record NowPlayingLook(IReadOnlyList<string> Ink, LookStyle Style, string? Mood)
{
    public bool Equals(NowPlayingLook? other) =>
        other is not null && Style == other.Style && Mood == other.Mood && Ink.SequenceEqual(other.Ink, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Style, Mood, string.Join(",", Ink));

    /// <summary>The look as JSON, the shape of <c>now-playing-look.json</c>'s <c>look</c>: <c>{ink, style, mood?}</c>.</summary>
    public JsonObject ToJson()
    {
        var ink = new JsonArray();
        foreach (var s in Ink) ink.Add((JsonNode?)s);
        var o = new JsonObject { ["ink"] = ink, ["style"] = LookStyles.Word(Style) };
        if (Mood is not null) o["mood"] = Mood;
        return o;
    }
}

/// <summary>A look for one song. <see cref="Key"/> is <see cref="SongKey.Of"/> of the title and artist.</summary>
public sealed record SongLook(string Key, string Title, string Artist, NowPlayingLook Look)
{
    /// <summary>The entry as JSON, flat, the shape the tool, a design and the saved-looks file use: <c>{title, artist, ink, style, mood?}</c>.</summary>
    public JsonObject ToJson()
    {
        var look = Look.ToJson();
        var o = new JsonObject { ["title"] = Title, ["artist"] = Artist };
        foreach (var kv in look.ToList())
        {
            look.Remove(kv.Key);
            o[kv.Key] = kv.Value;
        }
        return o;
    }
}

/// <summary>
/// The song key: the same song is the same key in every app and on both surfaces (the Desk and the Claude Code now-playing
/// mod). It is the FNV-1a 64-bit hash of the UTF-8 bytes of <c>normalize(title) + "\n" + normalize(artist)</c>, as 16 lowercase
/// hex digits.
///
/// <para><b>normalize(s)</b>: (1) lowercase every Unicode scalar on its own with its simple case mapping (a scalar whose
/// lowercase is more than one character, U+0130, stays as written); (2) a run of whitespace (JavaScript's <c>\s</c> set:
/// <c>\t \n \v \f \r</c>, space, U+00A0, U+1680, U+2000 to U+200A, U+2028, U+2029, U+202F, U+205F, U+3000, U+FEFF) becomes one
/// space; (3) trim whitespace at both ends; (4) drop one trailing remaster mark, the first match of the regular expression
/// <c> - remaster.*$| \(remaster[^)]*\)$</c> (leftmost, applied once, to the whole text); (5) trim again. A title that normalizes
/// to nothing has no key. The artist may be empty.</para>
///
/// <para><b>FNV-1a 64</b>: <c>h = 0xcbf29ce484222325</c>; for each byte <c>h ^= byte; h = (h * 0x100000001b3) mod 2^64</c>.
/// Raw vectors: <c>""</c> is <c>cbf29ce484222325</c>, <c>"a"</c> is <c>af63dc4c8601ec8c</c>, <c>"foobar"</c> is <c>85944171f73967e8</c>.</para>
///
/// <para><b>Song vectors</b> (title, artist, key):
/// <c>("Blinding Lights", "The Weeknd")</c> is <c>ad75f4c29077a9de</c>, and so are <c>("  Blinding   Lights  ", "THE WEEKND")</c>;
/// <c>("Song", "Artist")</c> is <c>d2a7aa06c42e57bd</c>, and so are <c>("Song - Remastered 2011", "Artist")</c> and
/// <c>("Song (Remastered 2009)", "Artist")</c>;
/// <c>("Café del Mar", "Energy 52")</c> is <c>245e2ded93cf9f31</c>, and so is <c>("CAFÉ DEL MAR", "energy 52")</c>;
/// <c>("Untitled", "")</c> is <c>5f31c64688d31616</c>;
/// <c>("Hey Jude - Remastered 2015", "The Beatles")</c> is <c>b0ccc496e92995e4</c>;
/// <c>("Remaster Me", "X")</c> is <c>a31dfc67c5b9c43a</c> (not a suffix);
/// <c>("A - Remastered", "B")</c> is <c>e5beb1190415e670</c>; <c>("x", "y")</c> is <c>be74b5197ee0cbea</c>.</para>
/// </summary>
public static class SongKey
{
    private const ulong Offset = 0xcbf29ce484222325UL;
    private const ulong Prime = 0x100000001b3UL;

    private static readonly Regex RemasterMark = new(@" - remaster.*$| \(remaster[^)]*\)$", RegexOptions.CultureInvariant);

    /// <summary>FNV-1a, 64 bits.</summary>
    public static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        ulong h = Offset;
        foreach (byte b in bytes)
        {
            h ^= b;
            h = unchecked(h * Prime);
        }
        return h;
    }

    /// <summary>The 16-digit lowercase hex of <see cref="Fnv1a64"/> over the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static string Hash(string text) => Fnv1a64(Encoding.UTF8.GetBytes(text)).ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>JavaScript's <c>\s</c> set, so the mod and the widget agree on what a space is.</summary>
    public static bool IsSpace(char c) =>
        c is (char)9 or (char)10 or (char)11 or (char)12 or (char)13 or (char)32
            || c is (char)0x00A0 or (char)0x1680 or (char)0x2028 or (char)0x2029 or (char)0x202F or (char)0x205F or (char)0x3000 or (char)0xFEFF
            || (c >= (char)0x2000 && c <= (char)0x200A);

    /// <summary>The normalized text (see the type's description).</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (var rune in s.EnumerateRunes())
        {
            if (rune.IsBmp && IsSpace((char)rune.Value))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            var lower = rune.Value == 0x130 ? rune : Rune.ToLowerInvariant(rune);
            sb.Append(lower.ToString());
        }
        string text = RemasterMark.Replace(sb.ToString(), "", 1);
        // The mark can leave a space behind only if the text before it ended in one, which the collapse above rules out; trim anyway.
        return text.Trim(' ');
    }

    /// <summary>The key of a song, or null when the title is empty once normalized.</summary>
    public static string? Of(string? title, string? artist)
    {
        string t = Normalize(title);
        return t.Length == 0 ? null : Hash(t + "\n" + Normalize(artist));
    }
}

/// <summary>A palette the seeded look picks from: the eight of the now-playing mod (<c>hooks/theme.ts</c>), same names and stops.</summary>
public sealed record LookPalette(string Name, IReadOnlyList<string> Stops);

/// <summary>Seeding, colors along a gradient, and the choice between a saved look and a seeded one.</summary>
public static class NowPlayingLooks
{
    /// <summary>The eight palettes, in the mod's order.</summary>
    public static readonly IReadOnlyList<LookPalette> Palettes =
    [
        new("synthwave", ["#ff2a6d", "#d16ba5", "#05d9e8"]),
        new("sunset", ["#ff7e5f", "#feb47b", "#ffd86f"]),
        new("ocean", ["#00c6ff", "#4f8bff", "#a18cff"]),
        new("aurora", ["#00f5a0", "#00d9f5", "#a06bff"]),
        new("ember", ["#f12711", "#f5af19", "#ffe259"]),
        new("bubblegum", ["#ff9a9e", "#f6a6d8", "#a18cd1"]),
        new("toxic", ["#b6ff00", "#00ff9c", "#00c3ff"]),
        new("gold", ["#f7971e", "#ffd200", "#fff6b7"]),
    ];

    /// <summary>The styles a seeded look picks from (plain is left out: it is the least like a look).</summary>
    public static readonly IReadOnlyList<LookStyle> SeedStyles = [LookStyle.SmallCaps, LookStyle.Italic, LookStyle.Bold, LookStyle.BoldItalic];

    /// <summary>
    /// The look an unseen song wears, from its key: with <c>h</c> the key's 64-bit value, palette <c>h mod 8</c> and style
    /// <c>SeedStyles[(h div 8) mod 4]</c>. The mood is the palette's name. A key that is not 16 hex digits seeds from the hash of its text.
    /// </summary>
    public static NowPlayingLook Seeded(string key)
    {
        ulong h = key.Length == 16 && ulong.TryParse(key, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)
            ? v
            : SongKey.Fnv1a64(Encoding.UTF8.GetBytes(key));
        var palette = Palettes[(int)(h % (ulong)Palettes.Count)];
        var style = SeedStyles[(int)(h / (ulong)Palettes.Count % (ulong)SeedStyles.Count)];
        return new NowPlayingLook(palette.Stops, style, palette.Name);
    }

    /// <summary>The look a song wears: the saved one for its key, else the seeded one. Null when the title has no key.</summary>
    public static (string Key, NowPlayingLook Look, LookSource Source)? Resolve(NowPlayingLookBook book, string? title, string? artist)
    {
        if (SongKey.Of(title, artist) is not { } key) return null;
        return book.Find(key) is { } saved ? (key, saved, LookSource.Saved) : (key, Seeded(key), LookSource.Seeded);
    }

    public static string Word(LookSource s) => s == LookSource.Saved ? "saved" : "seeded";

    /// <summary>The color at <paramref name="t"/> (0 to 1) along the stops, interpolated in sRGB, as <c>rrggbb</c>.
    /// The bars use it, one color per bar.</summary>
    public static string ColorAt(IReadOnlyList<string> stops, double t)
    {
        var hex = stops.Select(InkHex.Normalize).OfType<string>().ToList();
        if (hex.Count == 0) return InkHex.Fallback;
        if (hex.Count == 1) return hex[0];
        double x = Math.Clamp(t, 0, 1) * (hex.Count - 1);
        int lo = Math.Min((int)Math.Floor(x), hex.Count - 2);
        double f = x - lo;
        var sb = new StringBuilder(6);
        for (int i = 0; i < 3; i++)
        {
            int a = int.Parse(hex[lo].AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = int.Parse(hex[lo + 1].AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            sb.Append(((int)Math.Round(a + (b - a) * f)).ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The stops to draw with: the look's, with OLED care's rule on them (nothing held at pure white) when
    /// <paramref name="capWhite"/>, as six-digit hex without the #.</summary>
    public static IReadOnlyList<string> DrawStops(NowPlayingLook look, bool capWhite) =>
        [.. look.Ink.Select(h => capWhite ? InkHex.CapLuminance(h, DeskInk.WhiteCapLuminance) : InkHex.Normalize(h) ?? InkHex.Fallback)];
}

/// <summary>The saved looks, in order (oldest first), by song key. Immutable; the widget keeps one and replaces it.</summary>
public sealed class NowPlayingLookBook
{
    /// <summary>The store keeps at most this many songs; the oldest go first.</summary>
    public const int MaxStored = 500;

    public static NowPlayingLookBook Empty { get; } = new([]);

    private readonly Dictionary<string, SongLook> _byKey;

    public NowPlayingLookBook(IEnumerable<SongLook> looks)
    {
        var list = new List<SongLook>();
        _byKey = new Dictionary<string, SongLook>(StringComparer.Ordinal);
        foreach (var l in looks)
        {
            if (_byKey.ContainsKey(l.Key)) list.RemoveAll(x => x.Key == l.Key);
            _byKey[l.Key] = l;
            list.Add(l);
        }
        if (list.Count > MaxStored)
        {
            foreach (var dropped in list.Take(list.Count - MaxStored)) _byKey.Remove(dropped.Key);
            list = list.Skip(list.Count - MaxStored).ToList();
        }
        Looks = list;
    }

    public IReadOnlyList<SongLook> Looks { get; }

    public int Count => Looks.Count;

    public NowPlayingLook? Find(string key) => _byKey.TryGetValue(key, out var l) ? l.Look : null;

    /// <summary>This book with <paramref name="added"/> saved: a song already there takes the new look and moves to the end; past
    /// <see cref="MaxStored"/> the oldest go.</summary>
    public NowPlayingLookBook Merge(IEnumerable<SongLook> added) => new([.. Looks, .. added]);

    /// <summary>The file's contents: <c>{schema_version, looks: [{key, title, artist, ink, style, mood?}]}</c>.</summary>
    public byte[] ToJson()
    {
        var arr = new JsonArray();
        foreach (var l in Looks)
        {
            var o = l.ToJson();
            var withKey = new JsonObject { ["key"] = l.Key };
            foreach (var kv in o.ToList())
            {
                o.Remove(kv.Key);
                withKey[kv.Key] = kv.Value;
            }
            arr.Add((JsonNode?)withKey);
        }
        var root = new JsonObject { ["schema_version"] = 1, ["looks"] = arr };
        return new UTF8Encoding(false).GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The book a file holds. Unreadable, another schema or no list is an empty book; an entry that fails the checks is
    /// skipped; the key is worked out again from the title and artist, so an entry the key rule has moved on from still finds its song.</summary>
    public static NowPlayingLookBook Parse(byte[]? data)
    {
        if (data is null || data.Length == 0) return Empty;
        try
        {
            if (JsonNode.Parse(data) is not JsonObject root) return Empty;
            if (root["schema_version"] is JsonNode sv && !(sv is JsonValue v && v.TryGetValue<int>(out var n) && n == 1)) return Empty;
            if (root["looks"] is not JsonArray items) return Empty;
            var list = new List<SongLook>();
            foreach (var item in items)
            {
                if (item is not JsonObject o) continue;
                var copy = (JsonObject)o.DeepClone();
                copy.Remove("key");
                var one = NowPlayingLookCheck.ParseEntry(copy, 1);
                if (one.Entry is { } entry) list.Add(entry);
            }
            return new NowPlayingLookBook(list);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}

/// <summary>What <see cref="NowPlayingLookCheck.ParseEntries"/> made of a list of looks.</summary>
public sealed record LookParse(IReadOnlyList<SongLook> Looks, IReadOnlyList<string> Reasons);

/// <summary>
/// The checks on a look and on a list of them: one gate for the MCP tool, the request the widget reads again, a design's
/// <c>looks</c>, the mod's offer and the saved-looks file. Each reason starts <c>look N</c>.
/// </summary>
public static class NowPlayingLookCheck
{
    public const int MaxLooks = 20;
    public const int MaxTitle = 200;
    public const int MaxArtist = 100;
    public const int MaxMood = 40;
    public const int MaxStops = 4;
    public const int MaxNote = 200;

    /// <summary>The WCAG contrast every stop must keep against what is behind the now-playing piece.</summary>
    public const double MinContrast = DeskDesign.MinContrast;

    private static readonly string[] Fields = ["title", "artist", "ink", "style", "mood"];

    /// <summary>Reads a JSON list of looks (<c>[{title, artist, ink, style, mood}]</c>), 1 to <see cref="MaxLooks"/>. When
    /// <paramref name="context"/> knows the wallpaper, every stop must also keep <see cref="MinContrast"/> against the spot. A song
    /// listed twice keeps its last look.</summary>
    public static LookParse ParseEntries(JsonNode? node, DeskDesignContext? context = null)
    {
        if (node is not JsonArray items || items.Count is 0 or > MaxLooks)
            return new LookParse([], [$"looks must be a list of 1 to {MaxLooks} looks"]);
        var reasons = new List<string>();
        var entries = new List<SongLook>();
        var backgrounds = Backgrounds(context);
        for (int i = 0; i < items.Count; i++)
        {
            var one = ParseEntry(items[i], i + 1, backgrounds);
            reasons.AddRange(one.Reasons);
            if (one.Entry is { } entry)
            {
                entries.RemoveAll(e => e.Key == entry.Key);
                entries.Add(entry);
            }
        }
        return new LookParse(reasons.Count > 0 ? [] : entries, reasons.Take(20).ToList());
    }

    /// <summary>One look, or why not. <paramref name="backgrounds"/> are the luminances behind the piece (empty: not judged).</summary>
    public static (SongLook? Entry, IReadOnlyList<string> Reasons) ParseEntry(JsonNode? node, int number, IReadOnlyList<double>? backgrounds = null)
    {
        string who = $"look {number}";
        if (node is not JsonObject o) return (null, [$"{who} must be an object with title, artist and ink"]);
        var reasons = new List<string>();
        if (o.Select(kv => kv.Key).Where(k => !Fields.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).FirstOrDefault() is { } extra)
            reasons.Add($"{who} has an unknown field: {Clip(extra, 20)}");

        string? title = Line(o["title"], MaxTitle, allowEmpty: false);
        if (title is null) reasons.Add($"{who} title must be one line of 1 to {MaxTitle} characters");
        string? artist = o["artist"] is null ? "" : Line(o["artist"], MaxArtist, allowEmpty: true);
        if (artist is null) reasons.Add($"{who} artist must be one line of at most {MaxArtist} characters");

        var ink = new List<string>();
        if (o["ink"] is JsonArray arr && arr.Count is >= 1 and <= MaxStops
            && arr.All(n => n is JsonValue v && v.TryGetValue<string>(out var s) && InkHex.Normalize(s) is not null))
        {
            foreach (var n in arr) ink.Add("#" + InkHex.Normalize(n!.GetValue<string>()));
            if (backgrounds is { Count: > 0 })
                foreach (var h in ink)
                    if (Contrast(h, backgrounds) is { } why) reasons.Add($"{who} color {why}");
        }
        else
        {
            reasons.Add($"{who} ink must be 1 to {MaxStops} hex colors, like [\"#ff2a6d\", \"#05d9e8\"]");
        }

        var style = LookStyle.Plain;
        if (o["style"] is { } sn)
        {
            if (sn is JsonValue sv && sv.TryGetValue<string>(out var word) && LookStyles.Parse(word) is { } parsed) style = parsed;
            else reasons.Add($"{who} style must be one of: {LookStyles.WordList}");
        }

        string? mood = null;
        if (o["mood"] is { } mn)
        {
            if (mn is JsonValue mv && mv.TryGetValue<string>(out var raw) && Line(raw, MaxMood, allowEmpty: true) is { } m) mood = m.Length == 0 ? null : m;
            else reasons.Add($"{who} mood must be one line of at most {MaxMood} characters");
        }

        if (reasons.Count > 0 || title is null || artist is null) return (null, reasons);
        string? key = SongKey.Of(title, artist);
        if (key is null) return (null, [$"{who} title must have a letter or a digit in it"]);
        return (new SongLook(key, title, artist, new NowPlayingLook(ink, style, mood)), []);
    }

    private static string? Line(JsonNode? n, int max, bool allowEmpty) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? Line(s, max, allowEmpty) : null;

    private static string? Line(string raw, int max, bool allowEmpty)
    {
        int count = 0;
        foreach (var r in raw.EnumerateRunes())
        {
            if (MessageProposal.IsControl(r)) return null;
            count++;
            if (count > max * 4) return null;
        }
        string t = raw.Trim();
        int scalars = t.EnumerateRunes().Count();
        if (scalars > max) return null;
        return t.Length == 0 && !allowEmpty ? null : t;
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];

    // -- contrast ------------------------------------------------------------------------------------

    /// <summary>"#202020 is too dark for this spot (1.3:1, needs 3.0:1)" when the stop falls under <see cref="MinContrast"/> against
    /// any of the backgrounds, else null. Too light when the spot is bright and the stop is lighter still.</summary>
    public static string? Contrast(string stop, IReadOnlyList<double> backgrounds)
    {
        double l = InkHex.Luminance(stop);
        double worst = double.MaxValue, worstBg = 0;
        foreach (var bg in backgrounds)
        {
            double ratio = DeskDesign.Ratio(l, bg);
            if (ratio < worst) { worst = ratio; worstBg = bg; }
        }
        if (worst >= MinContrast) return null;
        // The ground decides which way is wrong: a stop darker than a dark spot is too dark, lighter than a light spot too light.
        string way = l <= worstBg ? "dark" : "light";
        return $"{stop} is too {way} for this spot ({worst.ToString("0.0", CultureInfo.InvariantCulture)}:1, needs {MinContrast.ToString("0.0", CultureInfo.InvariantCulture)}:1)";
    }

    /// <summary>
    /// The luminances behind the now-playing piece: the wallpaper at its anchor on each monitor it is on (the plate laid over it
    /// when the plate is on). Empty when the wallpaper, or the piece's place, is not known or the piece is not placed: then
    /// the spot is not judged here and the widget, which knows, judges it.
    /// </summary>
    public static IReadOnlyList<double> Backgrounds(DeskDesignContext? context)
    {
        if (context?.Wallpaper is not { Monitors.Count: > 0 } wall) return [];
        var current = context.Current;
        var piece = current?.Layout?.FirstOrDefault(p => p.Piece == "nowPlaying");
        if (piece is { IsHidden: true }) return [];
        var anchor = piece?.Anchor ?? DeskArrangement.DefaultAnchor("nowPlaying");
        bool plate = current?.Plate ?? false;
        var list = new List<double>();
        foreach (var m in DeskDesign.MonitorsOf(wall, piece?.Monitor))
        {
            double under = m.Summary.At(anchor)?.Brightness ?? m.Summary.Brightness;
            if (plate) under = DeskDesign.UnderPlate(under, InkIsDark(current, under));
            list.Add(under);
        }
        return list;
    }

    /// <summary>True when the Desk's ink is dark (so the plate is white). Automatic ink is dark over a bright wallpaper.</summary>
    private static bool InkIsDark(DeskDesign? current, double wallpaper)
    {
        if (current?.Ink is not { } ink) return false;
        switch (ink.Kind)
        {
            case DeskInkKind.Dark: return true;
            case DeskInkKind.Auto: return wallpaper > DeskInk.AutoThreshold;
            case DeskInkKind.Custom: return ink.Colors.Count > 0 && InkHex.Luminance(ink.Colors[0]) < 0.5;
            case DeskInkKind.Theme:
                return current.Theme?["text"] is JsonValue tv && tv.TryGetValue<string>(out var text) && InkHex.Normalize(text) is not null
                    && InkHex.Luminance(text) < 0.5;
            default: return false;
        }
    }
}

/// <summary>What <see cref="NowPlayingLookProposal.Decode"/> made of a request.</summary>
public sealed class LookDecoded
{
    private LookDecoded(MessageDecodedKind kind, NowPlayingLookProposal? proposal, string? id, IReadOnlyList<string> reasons)
    {
        Kind = kind;
        Proposal = proposal;
        Id = id;
        Reasons = reasons;
    }

    public MessageDecodedKind Kind { get; }
    public NowPlayingLookProposal? Proposal { get; }
    public string? Id { get; }
    public IReadOnlyList<string> Reasons { get; }

    public static LookDecoded Discard { get; } = new(MessageDecodedKind.Discard, null, null, []);
    public static LookDecoded Of(NowPlayingLookProposal p) => new(MessageDecodedKind.Proposal, p, p.Id, []);
    public static LookDecoded Refuse(string id, IReadOnlyList<string> reasons) => new(MessageDecodedKind.Refused, null, id, reasons);
}

/// <summary>
/// Claude's suggested looks, the pure half: the request <c>propose_now_playing_looks</c> writes
/// (<c>now-playing-looks-request.json</c>), the checks the widget runs again (it is the authority), and the result the server waits for
/// (<c>now-playing-looks-result.json</c>). <see cref="IsOffer"/> marks a suggestion that came from the now-playing mod's offer file
/// instead of a tool call: it has no id and no result.
/// </summary>
public sealed record NowPlayingLookProposal(string Id, DateTimeOffset RequestedAt, IReadOnlyList<SongLook> Looks, string? Note, bool IsOffer = false)
{
    public const string RequestFile = "now-playing-looks-request.json";
    public const string ResultFile = "now-playing-looks-result.json";
    /// <summary>The current song's look for the mod, in the external folder.</summary>
    public const string CurrentFile = "now-playing-look.json";
    /// <summary>A look the mod's model chose for a song the widget has none saved for, in the external folder.</summary>
    public const string OfferFile = "now-playing-look-offer.json";
    /// <summary>The saved looks, in the app's own folder.</summary>
    public const string StoreFile = "now-playing-looks.json";

    /// <summary>A request is interactive: one older than this (Sanduhr was not running) is dropped.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(600);

    public bool Equals(NowPlayingLookProposal? other) =>
        other is not null && Id == other.Id && RequestedAt == other.RequestedAt && Note == other.Note && IsOffer == other.IsOffer
        && Looks.SequenceEqual(other.Looks);

    public override int GetHashCode() => HashCode.Combine(Id, RequestedAt, Note, IsOffer, Looks.Count);

    /// <summary>"Claude suggested looks for 3 songs".</summary>
    public string Title => $"Claude suggested looks for {Looks.Count} song{(Looks.Count == 1 ? "" : "s")}";

    /// <summary>The request as the server writes it: <c>{schema_version, id, requested_at, looks, note}</c>.</summary>
    public static JsonObject RequestJson(string id, DateTimeOffset now, IReadOnlyList<SongLook> looks, string? note)
    {
        var arr = new JsonArray();
        foreach (var l in looks) arr.Add((JsonNode?)l.ToJson());
        return new JsonObject
        {
            ["schema_version"] = 1,
            ["id"] = id,
            ["requested_at"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["looks"] = arr,
            ["note"] = note,
        };
    }

    /// <summary>Reads a request. Unreadable, without an id or too old is dropped; a request that breaks a rule is refused with the
    /// reasons; a good one is a proposal. <paramref name="context"/> adds the contrast rule when it knows the wallpaper.</summary>
    public static LookDecoded Decode(byte[] data, DateTimeOffset now, DeskDesignContext? context = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(data); }
        catch (JsonException) { return LookDecoded.Discard; }
        if (root is not JsonObject o) return LookDecoded.Discard;
        if (!(o["id"] is JsonValue iv && iv.TryGetValue<string>(out var id) && id.Length is > 0 and <= 64)) return LookDecoded.Discard;
        if (!(o["requested_at"] is JsonValue tv && tv.TryGetValue<string>(out var stamp) && MessageProposal.ParseDate(stamp) is { } at))
            return LookDecoded.Refuse(id, ["requested_at is missing or not a date"]);
        if (now - at > MaxAge) return LookDecoded.Discard;
        if (o["schema_version"] is JsonNode sv && !(sv is JsonValue svv && svv.TryGetValue<int>(out var version) && version == 1))
            return LookDecoded.Refuse(id, ["this Sanduhr reads requests of schema 1"]);

        var reasons = new List<string>();
        string? note = null;
        if (o["note"] is { } nn)
        {
            if (nn is JsonValue nv && nv.TryGetValue<string>(out var raw) && !raw.EnumerateRunes().Any(MessageProposal.IsControl)
                && raw.EnumerateRunes().Count() <= NowPlayingLookCheck.MaxNote)
            {
                string t = raw.Trim();
                note = t.Length == 0 ? null : t;
            }
            else
            {
                reasons.Add($"note must be one line of at most {NowPlayingLookCheck.MaxNote} characters");
            }
        }
        var parsed = NowPlayingLookCheck.ParseEntries(o["looks"], context);
        reasons.AddRange(parsed.Reasons);
        if (reasons.Count > 0) return LookDecoded.Refuse(id, reasons.Take(20).ToList());
        return LookDecoded.Of(new NowPlayingLookProposal(id, at, parsed.Looks, note));
    }

    private static readonly JsonWriterOptions Compact = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The result file's contents, keys sorted: <c>{id, completed_at, result: {status, style_on, reasons?, looks_saved?}}</c>.
    /// <c>style_on</c> says whether Style what's playing is on, so the agent can tell the user where to switch it on.
    /// </summary>
    public static byte[] ResultJson(string id, MessageProposalStatus status, bool styleOn,
        IReadOnlyList<string>? reasons = null, int? saved = null, DateTimeOffset? now = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Compact))
        {
            w.WriteStartObject();
            w.WriteString("completed_at", (now ?? DateTimeOffset.UtcNow).UtcDateTime
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            w.WriteString("id", id);
            w.WriteStartObject("result");
            if (saved is { } n) w.WriteNumber("looks_saved", n);
            if (reasons is { Count: > 0 })
            {
                w.WriteStartArray("reasons");
                foreach (var r in reasons) w.WriteStringValue(r);
                w.WriteEndArray();
            }
            w.WriteString("status", MessageProposal.Word(status));
            w.WriteBoolean("style_on", styleOn);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// <c>now-playing-look.json</c>, the current song's look for the mod: <c>{schema_version, key, look: {ink, style, mood?}, source}</c>.
    /// No title and no artist: the key is enough for a mod that knows its own song.
    /// </summary>
    public static byte[] CurrentJson(string key, NowPlayingLook look, LookSource source)
    {
        var root = new JsonObject
        {
            ["schema_version"] = 1,
            ["key"] = key,
            ["look"] = look.ToJson(),
            ["source"] = NowPlayingLooks.Word(source),
        };
        return new UTF8Encoding(false).GetBytes(root.ToJsonString());
    }

    /// <summary>An offer larger than this is not one: it is removed unread.</summary>
    public const int MaxOfferBytes = 16 * 1024;

    /// <summary>The mod's engine cannot rename, so it writes the offer in place: a reader may see half of it. An unreadable offer younger than
    /// this is left for the writer to finish.</summary>
    public static readonly TimeSpan OfferSettle = TimeSpan.FromSeconds(2);

    /// <summary>What to do with the offer on disk right now (see <see cref="JudgeOffer"/>).</summary>
    public enum OfferVerdict
    {
        /// <summary>Leave it: it may be half written. Look again soon.</summary>
        Keep,
        /// <summary>It is whole JSON: hand it to the checks (which accept or drop it by their own rules) and remove it.</summary>
        Consume,
        /// <summary>It will never be one: remove it unread.</summary>
        Discard,
    }

    /// <summary>
    /// Keep, consume or discard the offer on disk. Too large: discard. JSON that reads as an object: consume (whether the checks then take or
    /// drop it is theirs to say). Anything else is a write in progress while it is younger than <see cref="OfferSettle"/> (keep), and
    /// litter after that (discard). An age below zero (clocks disagree) counts as new.
    /// </summary>
    public static OfferVerdict JudgeOffer(bool parses, TimeSpan age, long length)
    {
        if (length > MaxOfferBytes) return OfferVerdict.Discard;
        if (parses) return OfferVerdict.Consume;
        return age < OfferSettle ? OfferVerdict.Keep : OfferVerdict.Discard;
    }

    /// <summary>True when the bytes are JSON whose root is an object: the mark that the writer has finished.</summary>
    public static bool OfferIsWhole(byte[] data)
    {
        try { return JsonNode.Parse(data) is JsonObject; }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Reads the mod's offer, <c>{key, title, artist, look: {ink, style, mood?}}</c> (an optional <c>schema_version</c> must be 1). The
    /// key must be the one the title and artist make. Null (with the reasons) when it breaks a rule.
    /// </summary>
    public static (SongLook? Look, IReadOnlyList<string> Reasons) DecodeOffer(byte[] data, DeskDesignContext? context = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(data); }
        catch (JsonException) { return (null, ["the offer is not JSON"]); }
        if (root is not JsonObject o) return (null, ["the offer must be an object"]);
        if (o["schema_version"] is JsonNode sv && !(sv is JsonValue svv && svv.TryGetValue<int>(out var version) && version == 1))
            return (null, ["this Sanduhr reads offers of schema 1"]);
        if (o["look"] is not JsonObject look) return (null, ["the offer needs a look"]);
        var flat = (JsonObject)look.DeepClone();
        foreach (var field in new[] { "title", "artist" })
            if (o[field] is { } value) flat[field] = value.DeepClone();
        var parsed = NowPlayingLookCheck.ParseEntries(new JsonArray(flat), context);
        if (parsed.Looks.Count != 1) return (null, parsed.Reasons);
        var entry = parsed.Looks[0];
        if (o["key"] is not JsonValue kv || !kv.TryGetValue<string>(out var key) || !string.Equals(key, entry.Key, StringComparison.Ordinal))
            return (null, ["the offer's key is not the key of its title and artist"]);
        return (entry, []);
    }
}
