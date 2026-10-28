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

/// <summary>One name in the book, for the allowlist list: its codename (null for a shown name never masked) and whether it is shown.</summary>
public sealed record CodenameEntry(CodenameCategory Category, string Real, string? Codename, bool Shown);

/// <summary>
/// The salt, the map per category (real name → codename) and the allowlist: <c>%APPDATA%\Sanduhr\codenames.json</c> (spec). First-seen and
/// kept: <see cref="Assign"/> picks once and the map keeps it. One used set across every category, so an account and a repo never share a
/// codename. Not thread-safe: <see cref="StreamerMask"/> locks around it.
/// </summary>
public sealed class CodenameBook
{
    private readonly Dictionary<CodenameCategory, Dictionary<string, string>> _maps = [];
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly HashSet<(CodenameCategory, string)> _shown = [];

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

    public void SetShown(CodenameCategory category, string real, bool shown)
    {
        if (shown) _shown.Add((category, real));
        else _shown.Remove((category, real));
    }

    /// <summary>Shuffle codenames… (spec): a new salt and an empty map. The allowlist is kept.</summary>
    public void Shuffle(byte[] salt)
    {
        if (salt is null || salt.Length < Codenames.MinSaltBytes)
            throw new ArgumentException("The salt is too short.", nameof(salt));
        Salt = (byte[])salt.Clone();
        _maps.Clear();
        _used.Clear();
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
            .Select(k => new CodenameEntry(k.Item1, k.Item2, TryGet(k.Item1, k.Item2, out var c) ? c : null, _shown.Contains(k)))];
    }

    public JsonObject ToJson()
    {
        var maps = new JsonObject();
        foreach (var (category, map) in _maps.OrderBy(m => m.Key))
        {
            var o = new JsonObject();
            foreach (var (real, code) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                o[real] = code;
            maps[Codenames.Id(category)] = o;
        }
        var shown = new JsonObject();
        foreach (var group in _shown.GroupBy(s => s.Item1).OrderBy(g => g.Key))
            shown[Codenames.Id(group.Key)] = new JsonArray([.. group.Select(s => (JsonNode?)s.Item2).OrderBy(n => (string)n!, StringComparer.Ordinal)]);
        return new JsonObject
        {
            ["schema_version"] = Codenames.SchemaVersion,
            ["salt"] = Convert.ToBase64String(Salt),
            ["maps"] = maps,
            ["shown"] = shown,
        };
    }

    /// <summary>
    /// A book from <c>codenames.json</c>, or null when the file can't be trusted at all (another schema, no salt, a salt under 16 bytes).
    /// Entries it can't trust are dropped one by one: an unknown category, a codename that is not a pool name (or its numbered form), a
    /// codename equal to its real name, and every holder of a codename after the first.
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
        if (root["maps"] is JsonObject maps)
        {
            foreach (var (id, value) in maps)
            {
                if (!Codenames.TryParseId(id, out var category) || value is not JsonObject map)
                    continue;
                foreach (var (real, codeNode) in map)
                {
                    if (codeNode is not JsonValue cv || !cv.TryGetValue(out string? code) || code is null)
                        continue;
                    if (real.Length == 0 || !Codenames.IsCodename(code) || code == real || book._used.Contains(code))
                        continue;
                    book.Map(category)[real] = code;
                    book._used.Add(code);
                }
            }
        }
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
        return book;
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
