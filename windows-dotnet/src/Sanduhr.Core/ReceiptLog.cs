using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>
/// A receipts file read record by record, so one record that doesn't decode (written by a newer
/// Sanduhr, cut short, hand-edited) is reported and set aside instead of blinding every undo.
///
/// Every record keeps the bytes it was read from. On save, a record that didn't decode is written back
/// byte for byte after the others, and a record that decoded and didn't change is written back from its
/// own bytes too, so fields this version doesn't know (a newer Sanduhr's) survive an older app's save.
/// A file whose top level isn't a JSON array is unreadable as a whole: the next save moves it aside
/// (<c>&lt;name&gt;.unreadable</c>), never over it. Generic over the record type, so later receipt kinds use
/// the same reader. Port of the Mac's <c>ReceiptLog.swift</c>.
/// </summary>
public static class ReceiptLog
{
    public const string AsideSuffix = ".unreadable";

    public sealed class Decoded<T>
    {
        public List<T> Records { get; } = [];
        /// <summary>Each decoded record's bytes as read, parallel to <see cref="Records"/>.</summary>
        public List<byte[]> Raws { get; } = [];
        /// <summary>Each record that didn't decode, byte for byte, in file order.</summary>
        public List<byte[]> Unreadable { get; } = [];
        /// <summary>The file is there but its top level isn't a JSON array.</summary>
        public bool FileUnreadable { get; set; }

        /// <summary>Records reported and skipped (a whole unreadable file counts as one).</summary>
        public int Skipped => Unreadable.Count + (FileUnreadable ? 1 : 0);
    }

    public static Decoded<T> Decode<T>(byte[] data, JsonSerializerOptions? options = null)
    {
        options ??= ReceiptJson.Options;
        var outp = new Decoded<T>();
        JsonEdit.ArraySpan array;
        try
        {
            using (var doc = JsonDocument.Parse(data))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) { outp.FileUnreadable = true; return outp; }
            }
            array = JsonEdit.Array(data, 0);
        }
        catch (Exception e) when (e is JsonException or JsonEditException)
        {
            outp.FileUnreadable = true;
            return outp;
        }
        foreach (var el in array.Elements)
        {
            var one = data[el.Start..el.End];
            T? record;
            try { record = JsonSerializer.Deserialize<T>(one, options); }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { record = default; }
            if (record is null)
            {
                outp.Unreadable.Add(one);
                continue;
            }
            outp.Records.Add(record);
            outp.Raws.Add(one);
        }
        return outp;
    }

    /// <summary>
    /// The file's bytes for <paramref name="records"/>, followed by <paramref name="previous"/>'s unreadable
    /// records as they were. A record whose serialized form equals one <paramref name="previous"/> decoded is
    /// written from that record's own bytes. Null when the result somehow isn't a JSON array.
    /// </summary>
    public static byte[]? Encode<T>(IReadOnlyList<T> records, Decoded<T>? previous = null, JsonSerializerOptions? options = null)
    {
        options ??= ReceiptJson.Options;
        previous ??= new Decoded<T>();
        var previousForms = previous.Records.Select(r => JsonSerializer.SerializeToUtf8Bytes(r, options)).ToList();
        var used = new HashSet<int>();
        var parts = new List<byte[]>();
        foreach (var record in records)
        {
            var form = JsonSerializer.SerializeToUtf8Bytes(record, options);
            int hit = -1;
            for (int i = 0; i < previousForms.Count; i++)
            {
                if (used.Contains(i) || i >= previous.Raws.Count) continue;
                if (previousForms[i].AsSpan().SequenceEqual(form)) { hit = i; break; }
            }
            if (hit >= 0)
            {
                used.Add(hit);
                parts.Add(previous.Raws[hit]);
            }
            else parts.Add(form);
        }
        parts.AddRange(previous.Unreadable);
        using var ms = new MemoryStream();
        ms.Write("[\n"u8);
        for (int i = 0; i < parts.Count; i++)
        {
            ms.Write(parts[i]);
            ms.Write(i == parts.Count - 1 ? "\n"u8 : ",\n"u8);
        }
        ms.Write("]\n"u8);
        var outp = ms.ToArray();
        try
        {
            using var doc = JsonDocument.Parse(outp);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        }
        catch (JsonException) { return null; }
        return outp;
    }

    /// <summary>Where an unreadable file goes before a save writes a new one.</summary>
    public static string AsidePath(string path) => path + AsideSuffix;

    /// <summary>
    /// Writes <paramref name="records"/> to <paramref name="path"/>, keeping every record of the current file
    /// that didn't decode after them. A file that isn't a JSON array at all is moved aside first (to
    /// <c>.unreadable</c>, or a numbered name when that exists), never written over. No records and nothing
    /// unreadable removes the file. Returns false when the new bytes could not be produced (nothing written).
    /// </summary>
    public static bool Save<T>(string path, IReadOnlyList<T> records, JsonSerializerOptions? options = null)
    {
        var current = Load<T>(path, options);
        if (current.FileUnreadable) MoveAside(path);
        if (records.Count == 0 && current.Unreadable.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        var data = Encode(records, current, options);
        if (data is null) return false;
        AtomicFile.Write(path, data);
        return true;
    }

    /// <summary>The file read record by record; an absent file is an empty log.</summary>
    public static Decoded<T> Load<T>(string path, JsonSerializerOptions? options = null)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return new Decoded<T>(); }
        catch (DirectoryNotFoundException) { return new Decoded<T>(); }
        return Decode<T>(data, options);
    }

    private static void MoveAside(string path)
    {
        if (!File.Exists(path)) return;
        var aside = AsidePath(path);
        if (File.Exists(aside))
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            aside = $"{path}.{stamp}{AsideSuffix}";
            for (int n = 1; File.Exists(aside); n++) aside = $"{path}.{stamp}-{n}{AsideSuffix}";
        }
        File.Move(path, aside, overwrite: false);
    }
}

/// <summary>
/// The integration receipts at one path (the app passes <c>%APPDATA%\Sanduhr\integrations\installs.json</c>).
/// </summary>
public sealed class ReceiptStore(string path)
{
    public string Path { get; } = path;

    /// <summary>The receipts file record by record: the readable ones, and the ones set aside.</summary>
    public ReceiptLog.Decoded<IntegrationReceipt> Load() => ReceiptLog.Load<IntegrationReceipt>(Path);

    /// <summary>Writes <paramref name="receipts"/>; see <see cref="ReceiptLog.Save{T}"/> for what is kept and moved aside.</summary>
    public bool Save(IReadOnlyList<IntegrationReceipt> receipts) => ReceiptLog.Save(Path, receipts);
}
