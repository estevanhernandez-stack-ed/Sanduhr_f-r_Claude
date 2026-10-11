using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>Why <see cref="JsonEdit"/> refused a file.</summary>
public enum JsonEditFailure
{
    /// <summary>Not JSON, or not an object at the top.</summary>
    Malformed,
    /// <summary>The value to edit inside holds something other than an object (or array).</summary>
    NotAnObject,
}

public sealed class JsonEditException : Exception
{
    public JsonEditFailure Failure { get; }

    public JsonEditException(JsonEditFailure failure)
        : base(failure == JsonEditFailure.Malformed ? "The text is not a JSON object." : "The value is not an object or array.")
        => Failure = failure;
}

/// <summary>
/// Minimal edits to someone else's JSON file: set or remove one member of an object and leave every
/// other byte where it was. Claude Code's <c>.claude.json</c> and <c>settings.json</c> are its files,
/// not Sanduhr's; re-serializing them would reorder keys and reformat the whole file, so the
/// installers splice the text instead. Port of the Mac's <c>JSONEdit.swift</c>.
///
/// The text is checked with <see cref="JsonDocument"/> before it is scanned, so the scanner only ever
/// walks valid JSON. Offsets are UTF-8 byte offsets. An insert appends the member last in the object,
/// copying the indentation and the <c>": "</c> spacing of the member before it (or two spaces more
/// than the object's own line when it was empty) and the file's own line ending (LF or CRLF), so
/// removing that member again gives back the original bytes.
/// </summary>
public static class JsonEdit
{
    // ---------------------------------------------------------------- values

    /// <summary>A value Sanduhr writes: strings, booleans, integers, arrays and objects with their keys in order.</summary>
    public abstract record Value
    {
        public static Value Str(string s) => new StringValue(s);
        public static Value Bool(bool b) => new BoolValue(b);
        public static Value Int(long n) => new IntValue(n);
        public static Value Arr(params Value[] items) => new ArrayValue(items);
        public static Value Obj(params Pair[] pairs) => new ObjectValue(pairs);
    }

    public sealed record StringValue(string Text) : Value;
    public sealed record BoolValue(bool Flag) : Value;
    public sealed record IntValue(long Number) : Value;

    public sealed record ArrayValue(IReadOnlyList<Value> Items) : Value
    {
        public bool Equals(ArrayValue? other) => other is not null && Items.SequenceEqual(other.Items);
        public override int GetHashCode() => Items.Count;
    }

    public sealed record ObjectValue(IReadOnlyList<Pair> Pairs) : Value
    {
        public bool Equals(ObjectValue? other) => other is not null && Pairs.SequenceEqual(other.Pairs);
        public override int GetHashCode() => Pairs.Count;
    }

    public sealed record Pair(string Key, Value Value);

    // ---------------------------------------------------------------- spans

    /// <summary>One <c>"key": value</c> member of an object.</summary>
    /// <param name="Key">The key with its escapes resolved.</param>
    /// <param name="KeyStart">The opening quote of the key.</param>
    /// <param name="KeyEnd">The byte after the key's closing quote.</param>
    /// <param name="ValueStart">The first byte of the value.</param>
    /// <param name="ValueEnd">The byte after the value.</param>
    public readonly record struct Member(string Key, int KeyStart, int KeyEnd, int ValueStart, int ValueEnd);

    /// <summary>One element of an array: its value's bytes.</summary>
    public readonly record struct Element(int Start, int End);

    /// <summary>An object: its braces and members in file order.</summary>
    public sealed class ObjectSpan(int open, int close, IReadOnlyList<Member> members)
    {
        public int Open { get; } = open;
        public int Close { get; } = close;
        public IReadOnlyList<Member> Members { get; } = members;

        /// <summary>The last member with <paramref name="key"/> (the one a JSON reader keeps).</summary>
        public Member? Member(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : Members[i];
        }

        public int IndexOf(string key)
        {
            for (int i = Members.Count - 1; i >= 0; i--)
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    /// <summary>An array: its brackets and elements in file order.</summary>
    public sealed class ArraySpan(int open, int close, IReadOnlyList<Element> elements)
    {
        public int Open { get; } = open;
        public int Close { get; } = close;
        public IReadOnlyList<Element> Elements { get; } = elements;
    }

    /// <summary>What <see cref="Set"/> did, for undoing it.</summary>
    /// <param name="Bytes">The edited file.</param>
    /// <param name="Previous">The value that was replaced, byte for byte; null when the member was added.</param>
    /// <param name="EmptyInner">What sat between the braces when the object was empty and the member became its only one; null otherwise.</param>
    public sealed record SetResult(byte[] Bytes, byte[]? Previous, byte[]? EmptyInner);

    // ---------------------------------------------------------------- checking and reading

    /// <summary>
    /// Parse options for every settings read: depth 256 and NO duplicate property names. A file with two
    /// <c>"env"</c> keys parses under the defaults but throws ArgumentException on first JsonObject
    /// indexing; refusing it here makes it Malformed/Unreadable everywhere, and never written.
    /// </summary>
    public static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 256, AllowDuplicateProperties = false };

    /// <summary>
    /// The parsed top-level object, or <see cref="JsonEditFailure.Malformed"/>. Strict JSON, as Claude
    /// Code's own <c>JSON.parse</c> reads it: comments and trailing commas are refused, and nothing but
    /// whitespace may follow the object. The caller disposes the document.
    /// </summary>
    public static JsonDocument Root(byte[] bytes)
    {
        JsonDocument doc;
        var body = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes.AsMemory();
        try { doc = JsonDocument.Parse(body, StrictOptions); }
        catch (JsonException) { throw new JsonEditException(JsonEditFailure.Malformed); }
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new JsonEditException(JsonEditFailure.Malformed);
        }
        return doc;
    }

    /// <summary>True when <paramref name="bytes"/> is a strict JSON object.</summary>
    public static bool IsObject(byte[] bytes)
    {
        try { Root(bytes).Dispose(); return true; }
        catch (JsonEditException) { return false; }
    }

    /// <summary>The object whose <c>{</c> is the first non-space byte at or after <paramref name="start"/>.</summary>
    public static ObjectSpan Object(byte[] b, int start)
    {
        var s = new Scanner(b, start);
        s.SkipSpace();
        if (s.Peek != '{') throw new JsonEditException(JsonEditFailure.NotAnObject);
        return s.ReadObject();
    }

    /// <summary>The array whose <c>[</c> is the first non-space byte at or after <paramref name="start"/>.</summary>
    public static ArraySpan Array(byte[] b, int start)
    {
        var s = new Scanner(b, start);
        s.SkipSpace();
        if (s.Peek != '[') throw new JsonEditException(JsonEditFailure.NotAnObject);
        return s.ReadArray();
    }

    /// <summary>The top-level object of a valid file (validated first).</summary>
    public static ObjectSpan TopObject(byte[] b)
    {
        Root(b).Dispose();
        return Object(b, 0);
    }

    /// <summary>The bytes of a member's value.</summary>
    public static byte[] Text(byte[] b, Member m) => b[m.ValueStart..m.ValueEnd];

    // ---------------------------------------------------------------- editing objects

    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="value"/> in <paramref name="obj"/> of
    /// <paramref name="b"/>: replaces the value of an existing member in place, or appends a new member last.
    /// </summary>
    public static SetResult Set(byte[] b, ObjectSpan obj, string key, Value value)
    {
        var eol = LineEnding(b);
        if (obj.Member(key) is { } m)
        {
            var indent = LineIndent(b, m.KeyStart);
            var rendered = Render(value, indent, IndentUnit(b), IsCompact(b, obj), eol);
            var o = new Buf().Add(b.AsSpan(0, m.ValueStart)).Add(rendered).Add(b.AsSpan(m.ValueEnd));
            return new SetResult(o.ToArray(), b[m.ValueStart..m.ValueEnd], null);
        }
        var unit = IndentUnit(b);
        if (obj.Members.Count > 0)
        {
            var last = obj.Members[^1];
            bool compact = IsCompact(b, obj);
            var colon = b[last.KeyEnd..last.ValueStart];
            var ins = new Buf().Add(","u8);
            if (compact)
            {
                ins.Add(Quoted(key)).Add(colon).Add(Render(value, [], unit, true, eol));
            }
            else
            {
                var indent = LineIndent(b, last.KeyStart);
                ins.Add(eol).Add(indent).Add(Quoted(key)).Add(colon).Add(Render(value, indent, unit, false, eol));
            }
            var o = new Buf().Add(b.AsSpan(0, last.ValueEnd)).Add(ins.ToArray()).Add(b.AsSpan(last.ValueEnd));
            return new SetResult(o.ToArray(), null, null);
        }
        // Empty object: one member on its own line, the closing brace back at the object's indent.
        var outer = LineIndent(b, obj.Open);
        var inner = Concat(outer, unit);
        var text = new Buf().Add(eol).Add(inner).Add(Quoted(key)).Add(": "u8)
            .Add(Render(value, inner, unit, false, eol)).Add(eol).Add(outer);
        var res = new Buf().Add(b.AsSpan(0, obj.Open + 1)).Add(text.ToArray()).Add(b.AsSpan(obj.Close));
        return new SetResult(res.ToArray(), null, b[(obj.Open + 1)..obj.Close]);
    }

    /// <summary>Puts <paramref name="previous"/> back as the value of <paramref name="key"/> (undoing a replacement).</summary>
    public static byte[] Restore(byte[] b, ObjectSpan obj, string key, byte[] previous)
    {
        if (obj.Member(key) is not { } m) return b;
        return new Buf().Add(b.AsSpan(0, m.ValueStart)).Add(previous).Add(b.AsSpan(m.ValueEnd)).ToArray();
    }

    /// <summary>
    /// Removes the member <paramref name="key"/> and its separator. The only member leaves the object
    /// holding <paramref name="emptyInner"/> (what was between the braces before it was added), or nothing.
    /// </summary>
    public static byte[] Remove(byte[] b, ObjectSpan obj, string key, byte[]? emptyInner = null)
    {
        int i = obj.IndexOf(key);
        if (i < 0) return b;
        var ms = obj.Members;
        var m = ms[i];
        int from, to;
        byte[] replacement = [];
        if (ms.Count == 1)
        {
            from = obj.Open + 1;
            to = obj.Close;
            replacement = emptyInner ?? [];
        }
        else if (i == ms.Count - 1) { from = ms[i - 1].ValueEnd; to = m.ValueEnd; }
        else { from = m.KeyStart; to = ms[i + 1].KeyStart; }
        return new Buf().Add(b.AsSpan(0, from)).Add(replacement).Add(b.AsSpan(to)).ToArray();
    }

    // ---------------------------------------------------------------- editing arrays

    /// <summary>
    /// Appends <paramref name="value"/> as the last element: on its own line at the last element's indent
    /// (or the array's own line plus one step when it was empty), or inline in an array written on one line.
    /// </summary>
    public static SetResult Append(byte[] b, ArraySpan arr, Value value)
    {
        var unit = IndentUnit(b);
        var eol = LineEnding(b);
        if (arr.Elements.Count > 0)
        {
            var last = arr.Elements[^1];
            bool compact = IsOneLine(b, arr.Open, arr.Close);
            var ins = new Buf();
            if (arr.Elements.Count >= 2)
            {
                var prev = arr.Elements[^2];
                ins.Add(b.AsSpan(prev.End, last.Start - prev.End));
            }
            else if (compact) ins.Add(","u8);
            else ins.Add(","u8).Add(eol).Add(LineIndent(b, last.Start));
            var indent = compact ? [] : LineIndent(b, last.Start);
            ins.Add(Render(value, indent, unit, compact, eol));
            var o = new Buf().Add(b.AsSpan(0, last.End)).Add(ins.ToArray()).Add(b.AsSpan(last.End));
            return new SetResult(o.ToArray(), null, null);
        }
        var outer = LineIndent(b, arr.Open);
        var inner = Concat(outer, unit);
        var text = new Buf().Add(eol).Add(inner).Add(Render(value, inner, unit, false, eol)).Add(eol).Add(outer);
        var res = new Buf().Add(b.AsSpan(0, arr.Open + 1)).Add(text.ToArray()).Add(b.AsSpan(arr.Close));
        return new SetResult(res.ToArray(), null, b[(arr.Open + 1)..arr.Close]);
    }

    /// <summary>Replaces element <paramref name="index"/> with <paramref name="value"/>, rendered at that element's indent.</summary>
    public static byte[] ReplaceElement(byte[] b, ArraySpan arr, int index, Value value)
    {
        var e = arr.Elements[index];
        bool compact = IsOneLine(b, arr.Open, arr.Close);
        var r = Render(value, compact ? [] : LineIndent(b, e.Start), IndentUnit(b), compact, LineEnding(b));
        return new Buf().Add(b.AsSpan(0, e.Start)).Add(r).Add(b.AsSpan(e.End)).ToArray();
    }

    /// <summary>
    /// Removes element <paramref name="index"/> and its separator. The only element leaves the array
    /// holding <paramref name="emptyInner"/> (what was between the brackets before it was added), or nothing.
    /// </summary>
    public static byte[] RemoveElement(byte[] b, ArraySpan arr, int index, byte[]? emptyInner = null)
    {
        var es = arr.Elements;
        if (index < 0 || index >= es.Count) return b;
        int from, to;
        byte[] replacement = [];
        if (es.Count == 1)
        {
            from = arr.Open + 1;
            to = arr.Close;
            replacement = emptyInner ?? [];
        }
        else if (index == es.Count - 1) { from = es[index - 1].End; to = es[index].End; }
        else { from = es[index].Start; to = es[index + 1].Start; }
        return new Buf().Add(b.AsSpan(0, from)).Add(replacement).Add(b.AsSpan(to)).ToArray();
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>The JSON string literal for <paramref name="s"/>: quote, backslash and control characters escaped, slashes and non-ASCII text left alone.</summary>
    public static byte[] Quoted(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return Encoding.UTF8.GetBytes(sb.Append('"').ToString());
    }

    /// <summary>
    /// <paramref name="value"/> as text. Multi-line values put each member on its own line, one
    /// <paramref name="unit"/> deeper than <paramref name="indent"/> (the line the value starts on), the
    /// closing bracket back at <paramref name="indent"/>. <paramref name="eol"/> is the file's line ending
    /// (LF when null).
    /// </summary>
    public static byte[] Render(Value value, byte[] indent, byte[] unit, bool compact, byte[]? eol = null)
    {
        eol ??= "\n"u8.ToArray();
        switch (value)
        {
            case StringValue s:
                return Quoted(s.Text);
            case BoolValue b:
                return Encoding.UTF8.GetBytes(b.Flag ? "true" : "false");
            case IntValue n:
                return Encoding.UTF8.GetBytes(n.Number.ToString(CultureInfo.InvariantCulture));
            case ArrayValue a:
            {
                if (a.Items.Count == 0) return "[]"u8.ToArray();
                var deeper = Concat(indent, unit);
                var parts = a.Items.Select(v => Render(v, deeper, unit, compact, eol)).ToList();
                return Wrap(parts, (byte)'[', (byte)']', indent, unit, compact, eol);
            }
            case ObjectValue o:
            {
                if (o.Pairs.Count == 0) return "{}"u8.ToArray();
                var deeper = Concat(indent, unit);
                var colon = compact ? ":"u8.ToArray() : ": "u8.ToArray();
                var parts = o.Pairs.Select(p => Concat(Quoted(p.Key), colon, Render(p.Value, deeper, unit, compact, eol))).ToList();
                return Wrap(parts, (byte)'{', (byte)'}', indent, unit, compact, eol);
            }
            default:
                throw new ArgumentException("Unknown value kind.", nameof(value));
        }
    }

    private static byte[] Wrap(List<byte[]> parts, byte open, byte close, byte[] indent, byte[] unit, bool compact, byte[] eol)
    {
        var o = new Buf().Add(open);
        for (int n = 0; n < parts.Count; n++)
        {
            if (n > 0) o.Add((byte)',');
            if (!compact) o.Add(eol).Add(indent).Add(unit);
            o.Add(parts[n]);
        }
        if (!compact) o.Add(eol).Add(indent);
        return o.Add(close).ToArray();
    }

    /// <summary>The spaces and tabs that start the line holding offset <paramref name="i"/>.</summary>
    public static byte[] LineIndent(byte[] b, int i)
    {
        int start = i;
        while (start > 0 && b[start - 1] != (byte)'\n') start--;
        int end = start;
        while (end < b.Length && (b[end] == (byte)' ' || b[end] == (byte)'\t')) end++;
        return b[start..end];
    }

    /// <summary>The file's line ending: CRLF when its first line break is one, else LF.</summary>
    public static byte[] LineEnding(byte[] b)
    {
        int i = b.AsSpan().IndexOf((byte)'\n');
        return i > 0 && b[i - 1] == (byte)'\r' ? "\r\n"u8.ToArray() : "\n"u8.ToArray();
    }

    /// <summary>
    /// The file's indent step: the top object's first member's indent when it starts a line, else two
    /// spaces (what Claude Code writes).
    /// </summary>
    public static byte[] IndentUnit(byte[] b)
    {
        try
        {
            var top = TopObject(b);
            if (top.Members.Count > 0)
            {
                var first = top.Members[0];
                var between = b.AsSpan(top.Open + 1, first.KeyStart - top.Open - 1);
                if (between.IndexOf((byte)'\n') >= 0)
                {
                    var indent = LineIndent(b, first.KeyStart);
                    if (indent.Length > 0) return indent;
                }
            }
        }
        catch (JsonEditException) { }
        return "  "u8.ToArray();
    }

    /// <summary>An object written on one line with members: new members go in the same compact style.</summary>
    public static bool IsCompact(byte[] b, ObjectSpan obj)
        => obj.Members.Count > 0 && IsOneLine(b, obj.Open, obj.Close);

    private static bool IsOneLine(byte[] b, int open, int close)
        => b.AsSpan(open, close - open + 1).IndexOf((byte)'\n') < 0;

    private static byte[] Concat(params byte[][] parts)
    {
        var o = new Buf();
        foreach (var p in parts) o.Add(p);
        return o.ToArray();
    }

    private sealed class Buf
    {
        private readonly MemoryStream _m = new();
        public Buf Add(ReadOnlySpan<byte> s) { _m.Write(s); return this; }
        public Buf Add(byte[] s) { _m.Write(s, 0, s.Length); return this; }
        public Buf Add(byte s) { _m.WriteByte(s); return this; }
        public byte[] ToArray() => _m.ToArray();
    }

    // ---------------------------------------------------------------- scanner

    /// <summary>Walks valid JSON (checked beforehand), recording object members and value spans.</summary>
    private struct Scanner
    {
        private readonly byte[] _b;
        private int _i;

        public Scanner(byte[] b, int i)
        {
            _b = b;
            _i = i;
            // A UTF-8 byte order mark at the very start is not part of the text.
            if (i == 0 && b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) _i = 3;
        }

        public readonly int Peek => _i < _b.Length ? _b[_i] : -1;

        public void SkipSpace()
        {
            while (_i < _b.Length && (_b[_i] == 0x20 || _b[_i] == 0x0A || _b[_i] == 0x0D || _b[_i] == 0x09)) _i++;
        }

        private static JsonEditException Bad() => new(JsonEditFailure.Malformed);

        public ObjectSpan ReadObject()
        {
            int open = _i++;
            var members = new List<Member>();
            SkipSpace();
            if (Peek == '}')
            {
                _i++;
                return new ObjectSpan(open, _i - 1, members);
            }
            while (true)
            {
                SkipSpace();
                int keyStart = _i;
                ReadString();
                int keyEnd = _i;
                var key = DecodeKey(_b.AsSpan(keyStart, keyEnd - keyStart));
                SkipSpace();
                if (Peek != ':') throw Bad();
                _i++;
                SkipSpace();
                int valueStart = _i;
                ReadValue();
                members.Add(new Member(key, keyStart, keyEnd, valueStart, _i));
                SkipSpace();
                int c = Peek;
                if (c < 0) throw Bad();
                _i++;
                if (c == '}') return new ObjectSpan(open, _i - 1, members);
                if (c != ',') throw Bad();
            }
        }

        public ArraySpan ReadArray()
        {
            int open = _i++;
            var elements = new List<Element>();
            SkipSpace();
            if (Peek == ']')
            {
                _i++;
                return new ArraySpan(open, _i - 1, elements);
            }
            while (true)
            {
                SkipSpace();
                int start = _i;
                ReadValue();
                elements.Add(new Element(start, _i));
                SkipSpace();
                int d = Peek;
                if (d < 0) throw Bad();
                _i++;
                if (d == ']') return new ArraySpan(open, _i - 1, elements);
                if (d != ',') throw Bad();
            }
        }

        private void ReadValue()
        {
            int c = Peek;
            if (c < 0) throw Bad();
            switch (c)
            {
                case '{':
                    ReadObject();
                    break;
                case '[':
                    ReadArray();
                    break;
                case '"':
                    ReadString();
                    break;
                default:
                {
                    // A number, true, false or null: up to the next delimiter.
                    int start = _i;
                    while (_i < _b.Length)
                    {
                        byte d = _b[_i];
                        if (d == ',' || d == '}' || d == ']' || d == 0x20 || d == 0x0A || d == 0x0D || d == 0x09) break;
                        _i++;
                    }
                    if (_i == start) throw Bad();
                    break;
                }
            }
        }

        private void ReadString()
        {
            if (Peek != '"') throw Bad();
            _i++;
            while (_i < _b.Length)
            {
                byte c = _b[_i];
                if (c == '\\')
                {
                    _i += 2;
                    continue;
                }
                _i++;
                if (c == '"') return;
            }
            throw Bad();
        }

        /// <summary>A key's text with its escapes resolved.</summary>
        private static string DecodeKey(ReadOnlySpan<byte> quoted)
        {
            try
            {
                var reader = new Utf8JsonReader(quoted);
                if (reader.Read() && reader.TokenType == JsonTokenType.String) return reader.GetString() ?? "";
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { }
            return Encoding.UTF8.GetString(quoted[1..^1]);
        }
    }
}
