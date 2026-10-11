using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// The one secret mask for Mods and Config: every view, diff, receipt display and log line that shows a
/// Claude Code config value goes through it. Port of the Mac's <c>ConfigMask.swift</c>.
///
/// By path: a value is secret when its key path runs through <c>env</c> (any variable), <c>headers</c>
/// (MCP headers), <c>oauthAccount</c>, or any segment naming a token, key, secret, password, credential,
/// auth (not "author"), authorization, bearer or cookie. The match is a case-insensitive substring, so
/// masking a harmless value costs a click and showing a secret costs the secret. Names are kept and values
/// replaced.
///
/// By value: every other string is scanned too, because secrets also sit inside commands: the userinfo of
/// a URL, a secret-named query parameter, the value after a secret-named flag (<c>--token x</c>,
/// <c>--api-key=x</c>, or the next element of an args list), <c>NAME=value</c> with a secret-named NAME, an
/// <c>Authorization:</c> or <c>Cookie:</c> header, <c>Bearer x</c>, and key-shaped strings.
///
/// A secret path is never editable, and a receipt never stores its value.
/// </summary>
public static class ConfigMask
{
    /// <summary>What a masked value shows as.</summary>
    public const string Masked = "••••••";

    private static readonly HashSet<string> SecretParents = new(StringComparer.Ordinal) { "env", "headers", "oauthaccount" };

    private static readonly string[] SecretWords =
        ["token", "key", "secret", "password", "passwd", "credential", "authorization", "bearer", "cookie"];

    /// <summary>Whether the value at <paramref name="path"/> (keys from the top of the file; array indexes as numbers) is secret.</summary>
    public static bool IsSecret(IEnumerable<string> path) => path.Any(IsSecretSegment);

    public static bool IsSecretSegment(string segment)
    {
        var s = segment.ToLowerInvariant();
        if (SecretParents.Contains(s)) return true;
        if (SecretWords.Any(w => s.Contains(w, StringComparison.Ordinal))) return true;
        return s.Contains("auth", StringComparison.Ordinal) && !s.Contains("author", StringComparison.Ordinal);
    }

    /// <summary>A flag (<c>--api-key</c>, <c>-token</c>) whose name is secret.</summary>
    public static bool IsSecretFlag(string s)
    {
        if (!s.StartsWith('-') || s.Contains('=')) return false;
        var name = s.TrimStart('-');
        return name.Length > 0 && IsSecretSegment(name);
    }

    /// <summary>Mods and Config may offer an edit at <paramref name="path"/> only when it isn't secret.</summary>
    public static bool IsEditable(IEnumerable<string> path) => !IsSecret(path);

    /// <summary>
    /// A copy of <paramref name="value"/> found at <paramref name="path"/>, safe to show: objects keep their
    /// keys (a secret key's value is masked whatever its type), arrays keep their length, every leaf under a
    /// secret path is replaced by <see cref="Masked"/>, and every other string goes through
    /// <see cref="MaskString"/>. The input is not modified.
    /// </summary>
    public static JsonNode? Mask(JsonNode? value, IReadOnlyList<string>? path = null)
    {
        path ??= [];
        if (IsSecret(path))
        {
            switch (value)
            {
                case JsonObject o:
                {
                    // Names only: which variables or headers are set, never their values.
                    var outp = new JsonObject();
                    foreach (var kv in o) outp[kv.Key] = Masked;
                    return outp;
                }
                case JsonArray a:
                    return new JsonArray(a.Select(_ => (JsonNode?)JsonValue.Create(Masked)).ToArray());
                default:
                    return JsonValue.Create(Masked);
            }
        }
        switch (value)
        {
            case JsonObject o:
            {
                var outp = new JsonObject();
                foreach (var kv in o) outp[kv.Key] = Mask(kv.Value, [.. path, kv.Key]);
                return outp;
            }
            case JsonArray a:
            {
                // An args list: the element after a secret-named flag is its value.
                var items = new List<JsonNode?>();
                bool afterFlag = false;
                for (int i = 0; i < a.Count; i++)
                {
                    var element = a[i];
                    bool isString = element is JsonValue v && v.TryGetValue<string>(out _);
                    if (afterFlag && isString)
                    {
                        items.Add(JsonValue.Create(Masked));
                        afterFlag = false;
                        continue;
                    }
                    afterFlag = isString && IsSecretFlag(element!.GetValue<string>());
                    items.Add(Mask(element, [.. path, i.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
                }
                return new JsonArray(items.ToArray());
            }
            case JsonValue sv when sv.TryGetValue<string>(out var s):
                return JsonValue.Create(MaskString(s));
            case null:
                return null;
            default:
                return value.DeepClone();
        }
    }

    // ---------------------------------------------------------------- values

    private const string Flag = "--?[A-Za-z0-9][A-Za-z0-9_.-]*";
    private const string Word = """("[^"]*"|'[^']*'|[^\s"'&]+)""";

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly (Regex Pattern, Func<string[], string?> Rewrite)[] Rules =
    [
        // URL userinfo: scheme://user:pass@host
        (new Regex("""([a-z][a-z0-9+.-]*://)[^/\s@'"]+@""", Opts), g => $"{g[1]}{Masked}@"),
        // A secret-named query parameter: ?token=x, &api_key=x
        (new Regex("""([?&;])([^=&\s#'"]+)=([^&\s#'"]*)""", Opts), g => IsSecretSegment(g[2]) ? $"{g[1]}{g[2]}={Masked}" : null),
        // An Authorization or Cookie header, whatever follows on the line.
        (new Regex("""\b(authorization|proxy-authorization|cookie|set-cookie|x-api-key)(\s*:\s*)([^'"\n\r]+)""", Opts),
            g => $"{g[1]}{g[2]}{Masked}"),
        // Bearer tokens.
        (new Regex("""\b(bearer)\s+[^\s'"]+""", Opts), g => $"{g[1]} {Masked}"),
        // --flag=value with a secret-named flag.
        (new Regex($"({Flag})={Word}", Opts), g => IsSecretFlag(g[1]) ? $"{g[1]}={Masked}" : null),
        // --flag value with a secret-named flag.
        (new Regex($@"({Flag})(\s+)(?!-){Word}", Opts), g => IsSecretFlag(g[1]) ? $"{g[1]}{g[2]}{Masked}" : null),
        // NAME=value with a secret-named NAME (shell assignments, env prefixes).
        (new Regex($@"(?<![\w-])([A-Za-z_][A-Za-z0-9_]*)={Word}", Opts), g => IsSecretSegment(g[1]) ? $"{g[1]}={Masked}" : null),
        // Key-shaped strings.
        (new Regex("""\b(sk-ant-|sk-|ghp_|gho_|ghs_|github_pat_|xox[abp]-)[A-Za-z0-9_-]{8,}""", Opts), _ => Masked),
    ];

    /// <summary><paramref name="s"/> with every secret the value rules find replaced by <see cref="Masked"/>.</summary>
    public static string MaskString(string s)
    {
        var current = s;
        foreach (var (pattern, rewrite) in Rules)
        {
            current = pattern.Replace(current, m =>
            {
                var groups = new string[m.Groups.Count];
                for (int i = 0; i < groups.Length; i++) groups[i] = m.Groups[i].Success ? m.Groups[i].Value : "";
                return rewrite(groups) ?? m.Value;
            });
        }
        return current;
    }

    /// <summary><paramref name="value"/> at <paramref name="path"/> as one line of display text: the masked JSON, keys sorted.</summary>
    public static string Display(JsonNode? value, IReadOnlyList<string>? path = null)
    {
        var safe = Mask(value, path);
        if (safe is JsonValue v && v.TryGetValue<string>(out var str)) return str;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Write(w, safe);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Write(Utf8JsonWriter w, JsonNode? node)
    {
        switch (node)
        {
            case null:
                w.WriteNullValue();
                break;
            case JsonObject o:
                w.WriteStartObject();
                foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    w.WritePropertyName(kv.Key);
                    Write(w, kv.Value);
                }
                w.WriteEndObject();
                break;
            case JsonArray a:
                w.WriteStartArray();
                foreach (var e in a) Write(w, e);
                w.WriteEndArray();
                break;
            default:
                node.WriteTo(w);
                break;
        }
    }
}
