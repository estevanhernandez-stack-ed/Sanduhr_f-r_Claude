using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>A JSON number as the widget pack format sees it. Python's <c>json</c> module, which the reference validator
/// (<c>sanduhr-widgets/tools/validate.py</c>) uses, keeps <c>8</c> and <c>8.0</c> apart: an integer field rejects
/// <c>8.0</c>. <see cref="Repr"/> is the value as Python prints it, so messages match the reference byte for byte.</summary>
internal sealed record PackNumber(double Value, bool IsInt, string Repr)
{
    public bool IsFinite => double.IsFinite(Value);
}

/// <summary>A JSON object with its keys in document order (messages and the trademark scan walk them in order).</summary>
internal sealed class PackObject
{
    private readonly Dictionary<string, object?> _map = new(StringComparer.Ordinal);

    public List<string> Keys { get; } = new();
    public int Count => Keys.Count;
    public bool Has(string key) => _map.ContainsKey(key);
    /// <summary>The value, or null when the key is absent (use <see cref="Has"/> to tell a JSON null from a missing key).</summary>
    public object? this[string key] => _map.TryGetValue(key, out var v) ? v : null;

    internal bool TryAdd(string key, object? value)
    {
        if (!_map.TryAdd(key, value))
            return false;
        Keys.Add(key);
        return true;
    }
}

/// <summary>
/// The strict JSON reader behind <see cref="PackValidator"/> and <see cref="PixelSheet"/>, plus the Python-style
/// formatting the validator's messages use. Pure: bytes in, a tree out, no IO.
///
/// Values are <c>null</c> (JSON null), <see cref="bool"/>, <see cref="PackNumber"/>, <see cref="string"/>,
/// <see cref="List{T}"/> of values, or <see cref="PackObject"/>. A duplicate key is a parse error, as in the
/// reference (FORMAT section 2).
/// </summary>
internal static class PackJson
{
    /// <summary>Nesting limit. A real pack nests four deep; Python's own limit sits near 1,000.</summary>
    public const int MaxDepth = 256;

    private sealed class DuplicateKeyException(string key) : Exception(key)
    {
        public string Key { get; } = key;
    }

    /// <summary>Parse UTF-8 JSON text (no byte-order mark handling: the caller rejects a BOM first). Returns false with a
    /// reason for anything that is not exactly one JSON value.</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out object? value, out string error)
    {
        value = null;
        error = "";
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            MaxDepth = MaxDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        try
        {
            if (!reader.Read())
            {
                error = "Expecting value: the document is empty";
                return false;
            }
            value = ReadValue(ref reader);
            if (reader.Read())
            {
                error = "Extra data after the JSON value";
                value = null;
                return false;
            }
            return true;
        }
        catch (DuplicateKeyException e)
        {
            error = "duplicate key " + PyStr(e.Key);
        }
        catch (JsonException e)
        {
            error = e.Message;
        }
        catch (InvalidOperationException e)
        {
            // GetString on an escaped lone surrogate (Python would accept it; a pack never needs one).
            error = e.Message;
        }
        catch (FormatException e)
        {
            error = e.Message;
        }
        value = null;
        return false;
    }

    private static object? ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var obj = new PackObject();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string key = reader.GetString()!;
                    reader.Read();
                    object? v = ReadValue(ref reader);
                    if (!obj.TryAdd(key, v))
                        throw new DuplicateKeyException(key);
                }
                return obj;
            }
            case JsonTokenType.StartArray:
            {
                var list = new List<object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    list.Add(ReadValue(ref reader));
                return list;
            }
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return Number(Encoding.UTF8.GetString(reader.ValueSpan));
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Null:
                return null;
            default:
                throw new FormatException("unexpected token " + reader.TokenType);
        }
    }

    private static PackNumber Number(string text)
    {
        bool isInt = text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
        // Out-of-range text parses to an infinity, which no rule accepts (Python's float does the same).
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        string repr;
        if (isInt)
        {
            string digits = text.TrimStart('-');
            repr = digits.Trim('0').Length == 0 ? "0" : text;   // "-0" is 0 in Python
        }
        else
        {
            repr = PyFloat(v);
        }
        return new PackNumber(v, isInt, repr);
    }

    // -- Python semantics the reference relies on ------------------------------------------------------------------

    /// <summary>Python's <c>isinstance(v, int)</c> (booleans are not numbers here).</summary>
    public static bool IsInt(object? v) => v is PackNumber { IsInt: true } n && n.IsFinite;

    /// <summary>The reference's <c>is_num</c>: an int or a finite float, never a boolean.</summary>
    public static bool IsNum(object? v) => v is PackNumber n && n.IsFinite;

    /// <summary>Python's <c>v == 1</c> for a JSON value (1 and 1.0 both count; true does not, by the reference's guard).</summary>
    public static bool IsOne(object? v) => v is PackNumber n && n.Value == 1;

    /// <summary>Python's <c>len(str)</c>: code points, not UTF-16 units.</summary>
    public static int PyLen(string s)
    {
        int n = 0;
        foreach (var _ in s.EnumerateRunes()) n++;
        return n;
    }

    /// <summary>Python's <c>s[:count]</c>, by code points.</summary>
    public static string PySlice(string s, int count)
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (var r in s.EnumerateRunes())
        {
            if (n++ == count) break;
            sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>Python's <c>repr()</c> of a JSON value: <c>None</c>, <c>True</c>, <c>'text'</c>, <c>[1, 2]</c>, <c>{'k': 1}</c>.</summary>
    public static string PyRepr(object? v) => v switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        PackNumber n => n.Repr,
        string s => PyStr(s),
        List<object?> list => "[" + string.Join(", ", list.Select(PyRepr)) + "]",
        PackObject o => "{" + string.Join(", ", o.Keys.Select(k => PyStr(k) + ": " + PyRepr(o[k]))) + "}",
        _ => v.ToString() ?? "",
    };

    /// <summary>Python's <c>repr()</c> of a list of strings, as printed for a sorted set of keys.</summary>
    public static string PyList(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(PyStr)) + "]";

    /// <summary>Python's <c>repr()</c> of a string: single quotes unless the text has a single quote and no double one,
    /// with the usual escapes for backslashes, the quote and characters that do not print.</summary>
    public static string PyStr(string s)
    {
        char q = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder(s.Length + 2);
        sb.Append(q);
        foreach (var r in s.EnumerateRunes())
        {
            int c = r.Value;
            if (c == q || c == '\\') sb.Append('\\').Append((char)c);
            else if (c == '\t') sb.Append("\\t");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c < 0x20 || c == 0x7f) sb.Append("\\x").Append(c.ToString("x2", CultureInfo.InvariantCulture));
            else if (!Printable(r))
            {
                if (c <= 0xff) sb.Append("\\x").Append(c.ToString("x2", CultureInfo.InvariantCulture));
                else if (c <= 0xffff) sb.Append("\\u").Append(c.ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append("\\U").Append(c.ToString("x8", CultureInfo.InvariantCulture));
            }
            else sb.Append(r.ToString());
        }
        sb.Append(q);
        return sb.ToString();
    }

    private static bool Printable(Rune r)
    {
        if (r.Value == ' ') return true;
        return Rune.GetUnicodeCategory(r) switch
        {
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.SpaceSeparator => false,
            _ => true,
        };
    }

    /// <summary>Python's <c>repr(float)</c>: the shortest round-trip digits, fixed between 1e-4 and 1e16, else
    /// scientific with a two-digit exponent (<c>1.0</c>, <c>0.35</c>, <c>1e+16</c>, <c>1.5e-05</c>).</summary>
    public static string PyFloat(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        if (d == 0) return double.IsNegative(d) ? "-0.0" : "0.0";
        string r = d.ToString("R", CultureInfo.InvariantCulture);
        bool neg = r[0] == '-';
        if (neg) r = r[1..];
        int exp = 0;
        int e = r.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            exp = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }
        int point = r.IndexOf('.');
        string digits = point < 0 ? r : r.Remove(point, 1);
        int pointPos = point < 0 ? r.Length : point;
        while (digits.Length > 1 && digits[0] == '0') { digits = digits[1..]; pointPos--; }
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        int dexp = pointPos + exp - 1;   // d.ddd x 10^dexp
        string body;
        if (dexp >= -4 && dexp < 16)
        {
            if (dexp >= 0)
            {
                string whole = digits.Length > dexp + 1 ? digits[..(dexp + 1)] : digits.PadRight(dexp + 1, '0');
                string frac = digits.Length > dexp + 1 ? digits[(dexp + 1)..] : "";
                body = whole + "." + (frac.Length == 0 ? "0" : frac);
            }
            else
            {
                body = "0." + new string('0', -dexp - 1) + digits;
            }
        }
        else
        {
            body = digits[..1] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (dexp < 0 ? "-" : "+")
                   + Math.Abs(dexp).ToString("00", CultureInfo.InvariantCulture);
        }
        return neg ? "-" + body : body;
    }

    /// <summary>Python's <c>"%g" % x</c> (six significant digits, trailing zeros dropped).</summary>
    public static string PyG(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        if (x == 0) return double.IsNegative(x) ? "-0" : "0";
        string sci = x.ToString("E5", CultureInfo.InvariantCulture);   // -1.50000E+003
        int e = sci.IndexOf('E');
        int exp = int.Parse(sci[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (exp >= -4 && exp < 6)
        {
            string f = x.ToString("F" + (5 - exp).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return f.Contains('.') ? f.TrimEnd('0').TrimEnd('.') : f;
        }
        string mant = sci[..e];
        if (mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
        return mant + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Python's <c>"%.Nf" % x</c>.</summary>
    public static string PyF(double x, int decimals) => x.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
