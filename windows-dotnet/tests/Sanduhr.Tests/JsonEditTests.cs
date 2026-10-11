using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of the Mac's JSONEditTests plus CRLF, byte-exact undo and unicode cases.</summary>
public class JsonEditTests
{
    private static readonly JsonEdit.Value StatusLine = JsonEdit.Value.Obj(
        new JsonEdit.Pair("type", JsonEdit.Value.Str("command")),
        new JsonEdit.Pair("command", JsonEdit.Value.Str("/usr/bin/python3 '/A B/s.py'")));

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    /// <summary>Sets then removes; asserts the original comes back exactly and the inserted text parses. Returns the edited text.</summary>
    private static string RoundTrip(string text, string key = "statusLine", JsonEdit.Value? value = null)
    {
        var b = B(text);
        var set = JsonEdit.Set(b, JsonEdit.TopObject(b), key, value ?? StatusLine);
        using (var doc = JsonEdit.Root(set.Bytes))
            Assert.True(doc.RootElement.TryGetProperty(key, out _));
        var back = JsonEdit.Remove(set.Bytes, JsonEdit.TopObject(set.Bytes), key, set.EmptyInner);
        Assert.Equal(text, S(back));
        Assert.Equal(b, back);
        return S(set.Bytes);
    }

    [Fact]
    public void Appends_last_in_the_files_style_and_removes_back_to_the_same_bytes()
    {
        var two = RoundTrip("{\n  \"a\": 1,\n  \"b\": [true, null]\n}\n");
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [true, null],\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"/usr/bin/python3 '/A B/s.py'\"\n  }\n}\n", two);
        var four = RoundTrip("{\n    \"model\": \"x\"\n}");
        Assert.Contains("\n    \"statusLine\": {\n        \"type\"", four);
        var compact = RoundTrip("{\"a\":{\"b\":\"}\"},\"c\":\"\\\"q\"}");
        Assert.Equal("{\"a\":{\"b\":\"}\"},\"c\":\"\\\"q\",\"statusLine\":{\"type\":\"command\",\"command\":\"/usr/bin/python3 '/A B/s.py'\"}}", compact);
        RoundTrip("{}");
        RoundTrip("{ \n\t }\n");
        RoundTrip("{\"é\": \"ü\", \"k\\u0041\": 2}");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\n}")]
    [InlineData("{ }")]
    [InlineData("{\n}\n")]
    [InlineData("{\r\n}\r\n")]
    [InlineData("{ \r\n\t }\r\n")]
    [InlineData("{\n  \"a\": {}\n}\n")]
    [InlineData("{\n  \"a\": {\n  }\n}\n")]
    [InlineData("{\n  \"only\": 1\n}\n")]
    [InlineData("{\r\n  \"only\": 1\r\n}\r\n")]
    [InlineData("{\n  \"first\": 1,\n  \"mid\": [1,\n    2],\n  \"last\": \"z\"\n}")]
    [InlineData("{\r\n  \"first\": 1,\r\n  \"last\": \"z\"\r\n}\r\n")]
    [InlineData("{\t\"tabbed\":\t1\t}")]
    [InlineData("{\n\t\"tabs\": {\n\t\t\"deep\": true\n\t}\n}\n")]
    [InlineData("{\"é\":\"ü\",\"日本語\":\"値 🎈\"}")]
    [InlineData("{\n  \"日本語\": \"値 🎈\",\n  \"é\": 1\n}\n")]
    public void Set_then_remove_returns_the_original_bytes_exactly(string text)
    {
        RoundTrip(text);
        RoundTrip(text, "zz", JsonEdit.Value.Int(7));
        RoundTrip(text, "unicodé→", JsonEdit.Value.Arr(JsonEdit.Value.Str("v"), JsonEdit.Value.Bool(true)));
    }

    [Fact]
    public void Set_then_remove_works_in_a_nested_object()
    {
        foreach (var text in new[]
        {
            "{\n  \"mcpServers\": {\n    \"other\": {\n      \"command\": \"x\"\n    }\n  },\n  \"n\": 1\n}\n",
            "{\r\n  \"mcpServers\": {\r\n    \"other\": {\r\n      \"command\": \"x\"\r\n    }\r\n  },\r\n  \"n\": 1\r\n}\r\n",
            "{\n  \"mcpServers\": {}\n}\n",
            "{\n  \"mcpServers\": {\n  }\n}\n",
            "{\r\n  \"mcpServers\": {\r\n  }\r\n}\r\n",
        })
        {
            var b = B(text);
            var top = JsonEdit.TopObject(b);
            var inner = JsonEdit.Object(b, top.Member("mcpServers")!.Value.ValueStart);
            var set = JsonEdit.Set(b, inner, "sanduhr", StatusLine);
            using (var doc = JsonEdit.Root(set.Bytes))
                Assert.Equal("command", doc.RootElement.GetProperty("mcpServers").GetProperty("sanduhr").GetProperty("type").GetString());
            var top2 = JsonEdit.TopObject(set.Bytes);
            var inner2 = JsonEdit.Object(set.Bytes, top2.Member("mcpServers")!.Value.ValueStart);
            Assert.Equal(b, JsonEdit.Remove(set.Bytes, inner2, "sanduhr", set.EmptyInner));
        }
    }

    [Fact]
    public void Insert_uses_the_files_line_ending()
    {
        var crlf = B("{\r\n  \"a\": 1\r\n}\r\n");
        var set = JsonEdit.Set(crlf, JsonEdit.TopObject(crlf), "statusLine", StatusLine);
        var text = S(set.Bytes);
        Assert.Equal("{\r\n  \"a\": 1,\r\n  \"statusLine\": {\r\n    \"type\": \"command\",\r\n    \"command\": \"/usr/bin/python3 '/A B/s.py'\"\r\n  }\r\n}\r\n", text);
        // No bare LF anywhere: every line break is CRLF.
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));

        var lf = B("{\n  \"a\": 1\n}\n");
        var set2 = JsonEdit.Set(lf, JsonEdit.TopObject(lf), "statusLine", StatusLine);
        Assert.DoesNotContain("\r", S(set2.Bytes));

        var emptyCrlf = B("{\r\n}\r\n");
        var set3 = JsonEdit.Set(emptyCrlf, JsonEdit.TopObject(emptyCrlf), "k", JsonEdit.Value.Bool(true));
        Assert.Equal("{\r\n  \"k\": true\r\n}\r\n", S(set3.Bytes));
    }

    [Fact]
    public void Replaces_and_restores_a_value_in_place()
    {
        var text = "{\n  \"statusLine\": {\"type\": \"command\", \"command\": \"mine.sh\"},\n  \"z\": 0\n}";
        var b = B(text);
        var set = JsonEdit.Set(b, JsonEdit.TopObject(b), "statusLine", StatusLine);
        Assert.Equal("{\"type\": \"command\", \"command\": \"mine.sh\"}", S(set.Previous!));
        Assert.EndsWith("\n  },\n  \"z\": 0\n}", S(set.Bytes));
        var back = JsonEdit.Restore(set.Bytes, JsonEdit.TopObject(set.Bytes), "statusLine", set.Previous!);
        Assert.Equal(b, back);
    }

    [Fact]
    public void Replaces_and_restores_in_a_crlf_file()
    {
        var text = "{\r\n  \"statusLine\": {\"type\": \"x\"},\r\n  \"z\": 0\r\n}\r\n";
        var b = B(text);
        var set = JsonEdit.Set(b, JsonEdit.TopObject(b), "statusLine", StatusLine);
        Assert.DoesNotContain("\n", S(set.Bytes).Replace("\r\n", ""));
        Assert.Equal(b, JsonEdit.Restore(set.Bytes, JsonEdit.TopObject(set.Bytes), "statusLine", set.Previous!));
    }

    [Fact]
    public void Removes_a_member_that_isnt_last()
    {
        var b = B("{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}");
        Assert.Equal("{\n  \"a\": 1,\n  \"c\": 3\n}", S(JsonEdit.Remove(b, JsonEdit.TopObject(b), "b")));
        Assert.Equal("{\n  \"b\": 2,\n  \"c\": 3\n}", S(JsonEdit.Remove(b, JsonEdit.TopObject(b), "a")));
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}", S(JsonEdit.Remove(b, JsonEdit.TopObject(b), "c")));
        Assert.Same(b, JsonEdit.Remove(b, JsonEdit.TopObject(b), "missing"));
    }

    [Fact]
    public void Finds_nested_objects()
    {
        var b = B("{\"mcpServers\": {\"other\": {\"command\": \"x\"}}, \"n\": 1}");
        var top = JsonEdit.TopObject(b);
        var servers = JsonEdit.Object(b, top.Member("mcpServers")!.Value.ValueStart);
        Assert.Equal(new[] { "other" }, servers.Members.Select(m => m.Key));
        var ex = Assert.Throws<JsonEditException>(() => JsonEdit.Object(b, top.Member("n")!.Value.ValueStart));
        Assert.Equal(JsonEditFailure.NotAnObject, ex.Failure);
    }

    [Fact]
    public void Decodes_escaped_keys_and_the_last_duplicate_wins()
    {
        var b = B("{\"k\\u0041\": 1, \"dup\": 1, \"dup\": 2}");
        var top = JsonEdit.TopObject(b);
        Assert.Equal("kA", top.Members[0].Key);
        Assert.Equal("2", S(JsonEdit.Text(b, top.Member("dup")!.Value)));
    }

    [Fact]
    public void Reads_arrays_and_edits_their_elements()
    {
        var b = B("{\n  \"hooks\": [\n    {\"a\": 1},\n    {\"b\": 2}\n  ]\n}\n");
        var top = JsonEdit.TopObject(b);
        var arr = JsonEdit.Array(b, top.Member("hooks")!.Value.ValueStart);
        Assert.Equal(2, arr.Elements.Count);

        var added = JsonEdit.Append(b, arr, JsonEdit.Value.Obj(new JsonEdit.Pair("c", JsonEdit.Value.Int(3))));
        using (var doc = JsonEdit.Root(added.Bytes))
            Assert.Equal(3, doc.RootElement.GetProperty("hooks").GetArrayLength());
        var arr2 = JsonEdit.Array(added.Bytes, JsonEdit.TopObject(added.Bytes).Member("hooks")!.Value.ValueStart);
        Assert.Equal(b, JsonEdit.RemoveElement(added.Bytes, arr2, 2));
    }

    [Fact]
    public void Appends_to_an_empty_array_and_undoes_it()
    {
        foreach (var text in new[] { "{\n  \"l\": []\n}\n", "{\r\n  \"l\": [ ]\r\n}\r\n" })
        {
            var b = B(text);
            var arr = JsonEdit.Array(b, JsonEdit.TopObject(b).Member("l")!.Value.ValueStart);
            var added = JsonEdit.Append(b, arr, JsonEdit.Value.Str("x"));
            var arr2 = JsonEdit.Array(added.Bytes, JsonEdit.TopObject(added.Bytes).Member("l")!.Value.ValueStart);
            Assert.Equal(b, JsonEdit.RemoveElement(added.Bytes, arr2, 0, added.EmptyInner));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"a\": 1,}")]
    [InlineData("{\"a\": 1} trailing")]
    [InlineData("// c\n{}")]
    [InlineData("\"s\"")]
    [InlineData("{\"a\": ")]
    public void Refuses_what_isnt_a_json_object(string text)
    {
        var ex = Assert.Throws<JsonEditException>(() => JsonEdit.Root(B(text)));
        Assert.Equal(JsonEditFailure.Malformed, ex.Failure);
        Assert.Throws<JsonEditException>(() => JsonEdit.TopObject(B(text)));
        Assert.False(JsonEdit.IsObject(B(text)));
    }

    [Fact]
    public void Accepts_a_byte_order_mark_and_keeps_it()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var b = bom.Concat(B("{\n  \"a\": 1\n}\n")).ToArray();
        var set = JsonEdit.Set(b, JsonEdit.TopObject(b), "k", JsonEdit.Value.Bool(true));
        Assert.Equal(bom, set.Bytes.Take(3));
        Assert.Equal(b, JsonEdit.Remove(set.Bytes, JsonEdit.TopObject(set.Bytes), "k"));
    }

    [Fact]
    public void Quotes_like_a_json_writer_but_leaves_slashes_and_unicode()
    {
        Assert.Equal("\"a/b \\\"q\\\" \\\\ \\n é 🎈\"", S(JsonEdit.Quoted("a/b \"q\" \\ \n é 🎈")));
        Assert.Equal("\"\\u0001\"", S(JsonEdit.Quoted("\u0001")));
        using var doc = JsonDocument.Parse(JsonEdit.Quoted("x\ty\u001f"));
        Assert.Equal("x\ty\u001f", doc.RootElement.GetString());
    }
}
