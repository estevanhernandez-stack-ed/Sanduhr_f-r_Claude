using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// A Unicode letter style, by the statusline runner's name: the Mathematical Alphanumeric styles
/// and small caps. Ported from <c>LetterStyle</c> in the Mac's Services/StatuslineLook.swift; the
/// statusline-only types in that file (segment kinds and styles) do not come over.
/// </summary>
public enum LetterStyle
{
    Bold,
    Italic,
    BoldItalic,
    Sans,
    Mono,
    DoubleStruck,
    Script,
    Fraktur,
    SmallCaps,
}

/// <summary>Names, titles and tag parsing for <see cref="LetterStyle"/>.</summary>
public static class LetterStyles
{
    /// <summary>Every style, in the Mac's declaration order (the order <see cref="TagNames"/> lists them).</summary>
    public static readonly IReadOnlyList<LetterStyle> All =
    [
        LetterStyle.Bold, LetterStyle.Italic, LetterStyle.BoldItalic, LetterStyle.Sans, LetterStyle.Mono,
        LetterStyle.DoubleStruck, LetterStyle.Script, LetterStyle.Fraktur, LetterStyle.SmallCaps,
    ];

    /// <summary>The runner's name, which is what a <c>{font:...}</c> tag and the editor write.</summary>
    public static string Word(LetterStyle s) => s switch
    {
        LetterStyle.Bold => "bold",
        LetterStyle.Italic => "italic",
        LetterStyle.BoldItalic => "bold-italic",
        LetterStyle.Sans => "sans",
        LetterStyle.Mono => "mono",
        LetterStyle.DoubleStruck => "double-struck",
        LetterStyle.Script => "script",
        LetterStyle.Fraktur => "fraktur",
        _ => "small-caps",
    };

    /// <summary>As the Letters menu names it.</summary>
    public static string Title(LetterStyle s) => s switch
    {
        LetterStyle.Bold => "Bold",
        LetterStyle.Italic => "Italic",
        LetterStyle.BoldItalic => "Bold italic",
        LetterStyle.Sans => "Sans",
        LetterStyle.Mono => "Monospace",
        LetterStyle.DoubleStruck => "Double-struck",
        LetterStyle.Script => "Script",
        LetterStyle.Fraktur => "Fraktur",
        _ => "Small caps",
    };

    /// <summary>
    /// A <c>{font:...}</c> tag's style: the statusline's names, ignoring case, spaces, hyphens and
    /// underscores, so <c>smallcaps</c>, <c>Small Caps</c> and <c>small_caps</c> are all small caps.
    /// Null for a name no style has.
    /// </summary>
    public static LetterStyle? Parse(string? tag)
    {
        if (tag is null) return null;
        string key = TagKey(tag);
        foreach (var style in All)
            if (TagKey(Word(style)) == key) return style;
        return null;
    }

    /// <summary>The names a <c>{font:...}</c> tag takes, as the MCP server lists them.</summary>
    public static readonly string TagNames = string.Join(", ", All.Select(Word));

    private static string TagKey(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            if (c != ' ' && c != '-' && c != '_') sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// The Unicode letter styles as characters (the statusline styles, and <c>{font:...}</c> on the Desk
/// for the styles a font has no face for): the Mathematical Alphanumeric Symbols block, its holes
/// filled from Letterlike Symbols (the letters Unicode had already encoded there, such as script B
/// or double-struck R), and small caps from the phonetic letters. Ported from the Mac's
/// Desk/LetterMap.swift with the same tables.
///
/// The results are astral (two UTF-16 units), so text is mapped by Unicode scalar
/// (<see cref="Rune"/>), never by <see cref="char"/>.
/// </summary>
public static class LetterMap
{
    /// <summary>One style's code points. <paramref name="Digit"/> is null where the block has no digits.</summary>
    public sealed record Table(uint Upper, uint Lower, uint? Digit, IReadOnlyDictionary<char, uint> Holes);

    private static readonly IReadOnlyDictionary<char, uint> NoHoles = new Dictionary<char, uint>();

    /// <summary>The tables by style; small caps has none (it uses <see cref="SmallCaps"/>).</summary>
    public static readonly IReadOnlyDictionary<LetterStyle, Table> Tables = new Dictionary<LetterStyle, Table>
    {
        [LetterStyle.Bold] = new(0x1D400, 0x1D41A, 0x1D7CE, NoHoles),
        [LetterStyle.Italic] = new(0x1D434, 0x1D44E, null, new Dictionary<char, uint> { ['h'] = 0x210E }),
        [LetterStyle.BoldItalic] = new(0x1D468, 0x1D482, null, NoHoles),
        [LetterStyle.Script] = new(0x1D49C, 0x1D4B6, null, new Dictionary<char, uint>
        {
            ['B'] = 0x212C, ['E'] = 0x2130, ['F'] = 0x2131, ['H'] = 0x210B, ['I'] = 0x2110, ['L'] = 0x2112,
            ['M'] = 0x2133, ['R'] = 0x211B, ['e'] = 0x212F, ['g'] = 0x210A, ['o'] = 0x2134,
        }),
        [LetterStyle.Fraktur] = new(0x1D504, 0x1D51E, null, new Dictionary<char, uint>
        {
            ['C'] = 0x212D, ['H'] = 0x210C, ['I'] = 0x2111, ['R'] = 0x211C, ['Z'] = 0x2128,
        }),
        [LetterStyle.DoubleStruck] = new(0x1D538, 0x1D552, 0x1D7D8, new Dictionary<char, uint>
        {
            ['C'] = 0x2102, ['H'] = 0x210D, ['N'] = 0x2115, ['P'] = 0x2119, ['Q'] = 0x211A, ['R'] = 0x211D, ['Z'] = 0x2124,
        }),
        [LetterStyle.Sans] = new(0x1D5A0, 0x1D5BA, 0x1D7E2, NoHoles),
        [LetterStyle.Mono] = new(0x1D670, 0x1D68A, 0x1D7F6, NoHoles),
    };

    /// <summary>a to z in small caps (x has no small capital and stays x).</summary>
    public const string SmallCaps = "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡxʏᴢ";

    /// <summary>
    /// <paramref name="ch"/> in <paramref name="style"/>; anything the style has no form for
    /// (accents, punctuation, other scripts, capitals in small caps) stays as it is.
    /// </summary>
    public static Rune Letter(Rune ch, LetterStyle style)
    {
        if (ch.Value > 127) return ch;
        int ascii = ch.Value;
        bool isUpper = ascii is >= 65 and <= 90, isLower = ascii is >= 97 and <= 122, isDigit = ascii is >= 48 and <= 57;
        if (!isUpper && !isLower && !isDigit) return ch;
        if (style == LetterStyle.SmallCaps)
            return isLower ? new Rune(SmallCaps[ascii - 97]) : ch;
        if (!Tables.TryGetValue(style, out var table)) return ch;
        if (table.Holes.TryGetValue((char)ascii, out uint hole)) return Scalar(hole, ch);
        if (isUpper) return Scalar(table.Upper + (uint)ascii - 65, ch);
        if (isLower) return Scalar(table.Lower + (uint)ascii - 97, ch);
        return table.Digit is { } d ? Scalar(d + (uint)ascii - 48, ch) : ch;
    }

    /// <summary><paramref name="text"/> in <paramref name="style"/>; a null style keeps it as written.</summary>
    public static string Map(string text, LetterStyle? style)
    {
        if (style is not { } s) return text;
        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            // Only ASCII has a form; every other UTF-16 unit (surrogate halves included) passes through untouched.
            if (c < 128) sb.Append(Letter(new Rune(c), s));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static Rune Scalar(uint value, Rune fallback) =>
        Rune.TryCreate((int)value, out var r) ? r : fallback;
}
