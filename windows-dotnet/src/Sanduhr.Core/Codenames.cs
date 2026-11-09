using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// What a codename stands in for (spec, Streamer mode, Codenames: "Categories, one pool"). The spec's nine, plus <see cref="AddOn"/>: plugin,
/// marketplace and MCP server names and packs made here, which the spec's Mods &amp; Widgets row masks without naming a category (slice 7,
/// decision 6). Not on the MCP link list.
/// </summary>
public enum CodenameCategory { Account, Email, Folder, Project, Skill, Repo, Runner, Host, City, AddOn }

/// <summary>One name in the book, for the allowlist list: its codename (null for a shown name never masked), whether it is shown, and
/// whether the person picked the codename (Rename…) rather than the pool drawing it.</summary>
public sealed record CodenameEntry(CodenameCategory Category, string Real, string? Codename, bool Shown, bool Chosen = false);

/// <summary>What <see cref="CodenameBook.Rename"/> made of a picked codename. Anything but <see cref="Ok"/> and <see cref="Cleared"/> left the book as it was.</summary>
public enum CodenameRename { Ok, Cleared, TooLong, BadCharacter, Taken, RealName }

/// <summary>
/// The salt, the map per category (real name → codename) and the allowlist: <c>%APPDATA%\Sanduhr\codenames.json</c> (spec). First-seen and
/// kept: <see cref="Assign"/> picks once and the map keeps it. One used set across every category, so an account and a repo never share a
/// codename. Not thread-safe: <see cref="StreamerMask"/> locks around it.
/// </summary>
public sealed class CodenameBook
{
    private readonly Dictionary<CodenameCategory, Dictionary<string, string>> _maps = [];
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);   // a codename is one name however it is cased: "dune fox" and "Dune Fox" never both show
    private readonly HashSet<(CodenameCategory, string)> _shown = [];
    /// <summary>Names whose codename the person picked: kept through <see cref="Shuffle"/>, saved under <c>chosen</c>.</summary>
    private readonly HashSet<(CodenameCategory, string)> _chosen = [];

    /// <param name="withDefaults">Starts the allowlist with <see cref="Codenames.DefaultShown"/>. A loaded book takes the file's list instead.</param>
    public CodenameBook(byte[] salt, bool withDefaults = true)
    {
        if (salt is null || salt.Length < Codenames.MinSaltBytes)
            throw new ArgumentException("The salt is too short.", nameof(salt));
        Salt = (byte[])salt.Clone();
        if (withDefaults)
            foreach (var shown in Codenames.DefaultShown)
                _shown.Add(shown);
    }

    /// <summary>A book with a fresh random salt: two PCs give different codenames, so none can be reversed from a public list (spec).</summary>
    public static CodenameBook New() => new(RandomNumberGenerator.GetBytes(Codenames.SaltBytes));

    public byte[] Salt { get; private set; }

    /// <summary>How many codenames are in use, across every category.</summary>
    public int Count => _used.Count;

    public bool TryGet(CodenameCategory category, string real, out string codename)
    {
        codename = "";
        return _maps.TryGetValue(category, out var map) && map.TryGetValue(real, out codename!);
    }

    /// <summary>The name's codename: the one it has, else a new one, picked by <see cref="Codenames.StartIndex"/> and stepped past every
    /// codename in use; past the pool's 196, the same pairs with " 2", then " 3" (spec, overflow).</summary>
    public string Assign(CodenameCategory category, string real)
    {
        if (TryGet(category, real, out var known))
            return known;
        int start = Codenames.StartIndex(Salt, category, real);
        for (int round = 1; ; round++)
        {
            for (int k = 0; k < Codenames.Pool.Count; k++)
            {
                var name = Codenames.Pool[(start + k) % Codenames.Pool.Count]
                    + (round == 1 ? "" : " " + round.ToString(CultureInfo.InvariantCulture));
                if (_used.Contains(name) || string.Equals(name, real, StringComparison.OrdinalIgnoreCase))
                    continue;
                Map(category)[real] = name;
                _used.Add(name);
                return name;
            }
        }
    }

    public bool IsShown(CodenameCategory category, string real) => _shown.Contains((category, real));

    public bool IsChosen(CodenameCategory category, string real) => _chosen.Contains((category, real));

    /// <summary>
    /// Rename… on a row of Show the real name: <paramref name="name"/> becomes the name's codename and stays through Shuffle. Blank gives
    /// the name back to the pool: a name that had a picked codename draws a new one at once, so its row stays listed and a folder keeps
    /// its label; a name with none is left alone. Refused, and nothing changes, when it is longer than
    /// <see cref="Codenames.MaxChosenLength"/>, carries a control or formatting character, is already another name's codename in any
    /// category, or equals a real name in the book (ignoring case; a folder's leaf counts).
    /// </summary>
    public CodenameRename Rename(CodenameCategory category, string real, string? name)
    {
        var text = (name ?? "").Trim();
        TryGet(category, real, out var current);
        if (text.Length == 0)
        {
            if (current.Length > 0)
            {
                Map(category).Remove(real);
                _used.Remove(current);
                _chosen.Remove((category, real));
                Assign(category, real);
            }
            return CodenameRename.Cleared;
        }
        var verdict = Codenames.CheckChosen(text);
        if (verdict != CodenameRename.Ok)
            return verdict;
        if (_used.Contains(text) && !string.Equals(current, text, StringComparison.OrdinalIgnoreCase))
            return CodenameRename.Taken;
        if (RealNames().Any(r => Codenames.MatchesReal(text, r.Item1, r.Item2)) || Codenames.MatchesReal(text, category, real))
            return CodenameRename.RealName;
        if (current.Length > 0)
            _used.Remove(current);
        Map(category)[real] = text;
        _used.Add(text);
        _chosen.Add((category, real));
        return CodenameRename.Ok;
    }

    private IEnumerable<(CodenameCategory, string)> RealNames() => RealNamesOf(_maps.SelectMany(m => m.Value.Keys.Select(real => (m.Key, real))), _shown);

    private static IEnumerable<(CodenameCategory, string)> RealNamesOf(IEnumerable<(CodenameCategory, string)> mapped, IEnumerable<(CodenameCategory, string)> shown) =>
        mapped.Concat(shown);

    public void SetShown(CodenameCategory category, string real, bool shown)
    {
        if (shown) _shown.Add((category, real));
        else _shown.Remove((category, real));
    }

    /// <summary>Shuffle codenames… (spec): a new salt and an empty map. The allowlist is kept, and so is every codename the person
    /// picked (Rename…): they named it on purpose.</summary>
    public void Shuffle(byte[] salt)
    {
        if (salt is null || salt.Length < Codenames.MinSaltBytes)
            throw new ArgumentException("The salt is too short.", nameof(salt));
        var kept = _chosen.Select(c => (c.Item1, c.Item2, _maps[c.Item1][c.Item2])).ToList();   // every picked name has its codename in the map
        Salt = (byte[])salt.Clone();
        _maps.Clear();
        _used.Clear();
        foreach (var (category, real, code) in kept)
        {
            Map(category)[real] = code;
            _used.Add(code);
        }
    }

    /// <summary>Every mapped or shown name, by category in enum order, then by real name (ordinal, ignoring case).</summary>
    public IReadOnlyList<CodenameEntry> Entries()
    {
        var keys = new HashSet<(CodenameCategory, string)>(_shown);
        foreach (var (category, map) in _maps)
            foreach (var real in map.Keys)
                keys.Add((category, real));
        return [.. keys
            .OrderBy(k => k.Item1).ThenBy(k => k.Item2, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => new CodenameEntry(k.Item1, k.Item2, TryGet(k.Item1, k.Item2, out var c) ? c : null, _shown.Contains(k), _chosen.Contains(k)))];
    }

    public JsonObject ToJson()
    {
        // A picked codename goes under "chosen", never "maps": a build before Rename… drops a non-pool codename from maps but keeps
        // the rest of the file, and ignores "chosen" (it draws a pool name for that row instead).
        var maps = new JsonObject();
        var chosen = new JsonObject();
        foreach (var (category, map) in _maps.OrderBy(m => m.Key))
        {
            var o = new JsonObject();
            var c = new JsonObject();
            foreach (var (real, code) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                (_chosen.Contains((category, real)) ? c : o)[real] = code;
            if (o.Count > 0) maps[Codenames.Id(category)] = o;
            if (c.Count > 0) chosen[Codenames.Id(category)] = c;
        }
        var shown = new JsonObject();
        foreach (var group in _shown.GroupBy(s => s.Item1).OrderBy(g => g.Key))
            shown[Codenames.Id(group.Key)] = new JsonArray([.. group.Select(s => (JsonNode?)s.Item2).OrderBy(n => (string)n!, StringComparer.Ordinal)]);
        return new JsonObject
        {
            ["schema_version"] = Codenames.SchemaVersion,
            ["salt"] = Convert.ToBase64String(Salt),
            ["maps"] = maps,
            ["chosen"] = chosen,
            ["shown"] = shown,
        };
    }

    /// <summary>
    /// A book from <c>codenames.json</c>, or null when the file can't be trusted at all (another schema, no salt, a salt under 16 bytes).
    /// Entries it can't trust are dropped one by one: an unknown category, a codename that is not a pool name (or its numbered form), a
    /// codename equal to its real name, and every holder of a codename after the first. A picked codename (<c>chosen</c>) is read first, so
    /// it wins a clash with a drawn one, and dropped when it fails <see cref="Codenames.CheckChosen"/> or equals any real name in the file.
    /// </summary>
    public static CodenameBook? FromJson(JsonNode? node)
    {
        if (node is not JsonObject root)
            return null;
        if (root["schema_version"] is not JsonValue v || !v.TryGetValue(out int version) || version != Codenames.SchemaVersion)
            return null;
        byte[] salt;
        try { salt = Convert.FromBase64String((string?)root["salt"] ?? ""); }
        catch (Exception e) when (e is FormatException or InvalidOperationException) { return null; }
        if (salt.Length < Codenames.MinSaltBytes)
            return null;

        var book = new CodenameBook(salt, withDefaults: root["shown"] is not JsonObject);
        var chosen = Pairs(root["chosen"]);
        var drawn = Pairs(root["maps"]);
        if (root["shown"] is JsonObject shown)
        {
            foreach (var (id, value) in shown)
            {
                if (!Codenames.TryParseId(id, out var category) || value is not JsonArray list)
                    continue;
                foreach (var item in list)
                    if (item is JsonValue iv && iv.TryGetValue(out string? real) && !string.IsNullOrEmpty(real))
                        book._shown.Add((category, real));
            }
        }
        var reals = RealNamesOf(chosen.Concat(drawn).Select(p => (p.Category, p.Real)), book._shown).ToList();
        foreach (var (category, real, code) in chosen)
        {
            if (Codenames.CheckChosen(code) != CodenameRename.Ok || book._used.Contains(code)
                || reals.Any(r => Codenames.MatchesReal(code, r.Item1, r.Item2)) || book.TryGet(category, real, out _))
                continue;
            book.Map(category)[real] = code;
            book._used.Add(code);
            book._chosen.Add((category, real));
        }
        foreach (var (category, real, code) in drawn)
        {
            if (!Codenames.IsCodename(code) || code == real || book._used.Contains(code) || book.TryGet(category, real, out _))
                continue;
            book.Map(category)[real] = code;
            book._used.Add(code);
        }
        return book;
    }

    /// <summary>The (category, real name, codename) triples of a <c>maps</c> or <c>chosen</c> object, unknown categories and non-strings left out.</summary>
    private static List<(CodenameCategory Category, string Real, string Code)> Pairs(JsonNode? node)
    {
        var list = new List<(CodenameCategory, string, string)>();
        if (node is not JsonObject maps)
            return list;
        foreach (var (id, value) in maps)
        {
            if (!Codenames.TryParseId(id, out var category) || value is not JsonObject map)
                continue;
            foreach (var (real, codeNode) in map)
                if (real.Length > 0 && codeNode is JsonValue cv && cv.TryGetValue(out string? code) && code is not null)
                    list.Add((category, real, code));
        }
        return list;
    }

    private Dictionary<string, string> Map(CodenameCategory category)
    {
        if (!_maps.TryGetValue(category, out var map))
            _maps[category] = map = new Dictionary<string, string>(StringComparer.Ordinal);
        return map;
    }
}

/// <summary>
/// The codename pool and the file (Settings v2, slice 7; spec, Streamer mode, Codenames). The words come from sand, time, desert, sky and
/// glass, with no people, brands or trademarks. App-side Core: not on the MCP link list.
/// </summary>
public static partial class Codenames
{
    public const int SaltBytes = 32;
    public const int MinSaltBytes = 16;
    public const int SchemaVersion = 1;
    public const string FileName = "codenames.json";
    public const int MaxFileBytes = 4 * 1024 * 1024;
    /// <summary>The longest codename Rename… takes: two pool words with a numeral are under 20, and a line on stream has little room.</summary>
    public const int MaxChosenLength = 32;

    public static IReadOnlyList<string> FirstWords { get; } =
        ["Dune", "Amber", "Mesa", "Dusk", "Ember", "Quartz", "Ochre", "Mirage", "Saffron", "Drift", "Tawny", "Opal", "Cinder", "Zenith"];

    public static IReadOnlyList<string> SecondWords { get; } =
        ["Fox", "Wren", "Heron", "Lark", "Moth", "Ibis", "Gecko", "Hare", "Kestrel", "Owl", "Oryx", "Swift", "Finch", "Plover"];

    /// <summary>The 196 pairs, first word major.</summary>
    public static IReadOnlyList<string> Pool { get; } = [.. FirstWords.SelectMany(f => SecondWords.Select(s => f + " " + s))];

    /// <summary>Pairs no codename may form: known product, team and character names. A pool change that forms one fails <c>CodenamesTests</c>.
    /// Compared by letters only, ignoring case ("Fire fox" is "Firefox").</summary>
    public static IReadOnlyList<string> DenyPairs { get; } =
    [
        "Desert Eagle", "Firefox", "Fire Fox", "Red Fox", "Night Owl", "Snow Leopard", "Golden Eagle", "Black Widow", "Silver Surfer",
        "Dark Horse", "Sand Cat", "Amber Alert", "Grey Owl", "Blue Heron", "Night Hawk", "Thunderbird", "Firebird", "Mockingbird",
    ];

    /// <summary>Allowlisted by default because they identify no one (spec; slice 7 decision 9): Sanduhr's own mod, its MCP server entry and
    /// the mod's marketplace in <c>sanduhr-meters@inline</c>.</summary>
    public static IReadOnlyList<(CodenameCategory Category, string Real)> DefaultShown { get; } =
        [(CodenameCategory.AddOn, "sanduhr-meters"), (CodenameCategory.AddOn, "sanduhr"), (CodenameCategory.AddOn, "inline")];

    public static string Id(CodenameCategory category) => category switch
    {
        CodenameCategory.Account => "account",
        CodenameCategory.Email => "email",
        CodenameCategory.Folder => "folder",
        CodenameCategory.Project => "project",
        CodenameCategory.Skill => "skill",
        CodenameCategory.Repo => "repo",
        CodenameCategory.Runner => "runner",
        CodenameCategory.Host => "host",
        CodenameCategory.City => "city",
        CodenameCategory.AddOn => "addon",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static bool TryParseId(string id, out CodenameCategory category)
    {
        foreach (var c in Enum.GetValues<CodenameCategory>())
        {
            if (Id(c) == id)
            {
                category = c;
                return true;
            }
        }
        category = default;
        return false;
    }

    /// <summary>The first pick: HMAC-SHA256 of (category, real name) keyed with the per-install salt, as an index into <see cref="Pool"/>.</summary>
    public static int StartIndex(byte[] salt, CodenameCategory category, string real)
    {
        var mac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(Id(category) + "\u001f" + real));
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(mac) % (uint)Pool.Count);
    }

    [GeneratedRegex(@"^(?<pair>[A-Z][a-z]+ [A-Z][a-z]+)(?: (?<n>[2-9]|[1-9][0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CodenameShape();

    private static readonly HashSet<string> PoolSet = new(Pool, StringComparer.Ordinal);

    /// <summary>A pool pair, or a pool pair with a numeral from 2.</summary>
    public static bool IsCodename(string text) =>
        CodenameShape().Match(text ?? "") is { Success: true } m && PoolSet.Contains(m.Groups["pair"].Value);

    /// <summary>
    /// The shape rules for a codename the person picks (Rename…), before uniqueness and real names: trimmed, 1 to
    /// <see cref="MaxChosenLength"/> characters, and no control, formatting (bidi overrides, zero-width, tag), separator-line, private-use, lone surrogate or
    /// invisible filler characters (variation selectors, Hangul fillers, the braille blank), astral ones included, which would let a codename hide text or reorder a line on stream. Letters of any script, digits, spaces and punctuation are fine.
    /// </summary>
    public static CodenameRename CheckChosen(string text)
    {
        if (text.Length == 0 || text != text.Trim())
            return CodenameRename.BadCharacter;
        if (text.Length > MaxChosenLength)
            return CodenameRename.TooLong;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // The category of the whole scalar: an astral character (a tag character, a plane-15 private-use glyph) is judged like a BMP one.
            // A lone surrogate comes back as Surrogate and is refused below.
            var kind = CharUnicodeInfo.GetUnicodeCategory(text, i);
            int scalar = c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            if (kind is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return CodenameRename.BadCharacter;
            if (IsInvisible(scalar))
                return CodenameRename.BadCharacter;
            if (scalar < 0x10000 && char.IsWhiteSpace(c) && c != ' ')
                return CodenameRename.BadCharacter;
        }
        return CodenameRename.Ok;
    }

    /// <summary>
    /// A character that draws nothing but is not a control or format character: the Unicode default-ignorable blanks that sit in the
    /// letter, mark and symbol categories (variation selectors, the combining grapheme joiner, the Hangul and halfwidth Hangul fillers, the
    /// Khmer inherent vowels, the Mongolian free variation selectors) and the braille blank. A fixed list on purpose: refusing every
    /// nonspacing mark would refuse a decomposed "e" + U+0301, an ordinary accented letter.
    /// </summary>
    private static bool IsInvisible(int scalar) => scalar is
        0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or (>= 0x180B and <= 0x180D) or 0x180F or 0x2800 or 0x3164
        or (>= 0xFE00 and <= 0xFE0F) or 0xFFA0 or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>
    /// Whether a picked codename would show a real name: equal to it, ignoring case and surrounding spaces. A folder's key is its full
    /// lower-cased path, so its last segment counts too, with and without a leading dot (<c>.claude-work</c> and <c>claude-work</c>); an
    /// email's part before the @ counts as well.
    /// </summary>
    public static bool MatchesReal(string codename, CodenameCategory category, string real)
    {
        static bool Same(string a, string b) => b.Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        if (Same(codename, real))
            return true;
        if (category == CodenameCategory.Folder)
        {
            var leaf = real.TrimEnd('\\', '/');
            int cut = leaf.LastIndexOfAny(['\\', '/']);
            leaf = cut >= 0 ? leaf[(cut + 1)..] : leaf;
            return Same(codename, leaf) || Same(codename, leaf.TrimStart('.'));
        }
        if (category == CodenameCategory.Email && real.IndexOf('@') is > 0 and var at)
            return Same(codename, real[..at]);
        return false;
    }

    /// <summary>The book in <paramref name="path"/>; null when the file is missing, too large, not JSON or not a book.</summary>
    public static CodenameBook? Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
                return null;
            return CodenameBook.FromJson(JsonNode.Parse(File.ReadAllText(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the book atomically (a <c>.tmp</c> beside it, then a move). It is never sent anywhere (spec). False when it can't.</summary>
    public static bool Save(CodenameBook book, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, book.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
