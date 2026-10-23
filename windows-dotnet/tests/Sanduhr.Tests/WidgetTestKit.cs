using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>One entry of a hand-built zip. Every field can lie, so the reader's refusals can be tested one at a time.</summary>
internal sealed record ZipSpec(string Name, byte[] Data)
{
    public ushort Method { get; init; }
    public ushort Flags { get; init; }
    /// <summary>Host 3 (Unix), version 2.0, as Python's zipfile writes.</summary>
    public ushort MadeBy { get; init; } = (3 << 8) | 20;
    /// <summary>A regular file, mode 0644, as build_catalog.py writes.</summary>
    public uint External { get; init; } = 0x81A4u << 16;
    public uint? DeclaredSize { get; init; }
    public uint? LocalDeclaredSize { get; init; }
    public uint? Crc { get; init; }
    public string? LocalName { get; init; }
    /// <summary>Point this entry's central record at another entry's local header (overlap).</summary>
    public int? SharesLocalOf { get; init; }
}

/// <summary>Test helpers for the widget catalog: a raw zip writer, a throwaway signing key, and catalog bytes.</summary>
internal static class WidgetTestKit
{
    public static readonly Version V380 = new(3, 8, 0, 0);

    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>A zip with exactly these entries, in this order, stored (unless a spec says otherwise).</summary>
    public static byte[] Zip(IEnumerable<ZipSpec> specs, ushort? declaredCount = null)
    {
        var list = specs.ToList();
        var body = new MemoryStream();
        var offsets = new List<uint>();
        foreach (var s in list)
        {
            offsets.Add((uint)body.Position);
            var name = Encoding.Latin1.GetBytes(s.LocalName ?? s.Name);
            uint crc = s.Crc ?? PackValidator.Crc32(s.Data, 0, s.Data.Length);
            uint size = s.LocalDeclaredSize ?? s.DeclaredSize ?? (uint)s.Data.Length;
            var h = new byte[30];
            U32(h, 0, 0x04034b50); U16(h, 4, 20); U16(h, 6, s.Flags); U16(h, 8, s.Method);
            U16(h, 10, 0); U16(h, 12, 0x21); U32(h, 14, crc); U32(h, 18, size); U32(h, 22, size);
            U16(h, 26, (ushort)name.Length); U16(h, 28, 0);
            body.Write(h); body.Write(name); body.Write(s.Data);
        }
        uint cdStart = (uint)body.Position;
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var name = Encoding.Latin1.GetBytes(s.Name);
            uint crc = s.Crc ?? PackValidator.Crc32(s.Data, 0, s.Data.Length);
            uint size = s.DeclaredSize ?? (uint)s.Data.Length;
            var c = new byte[46];
            U32(c, 0, 0x02014b50); U16(c, 4, s.MadeBy); U16(c, 6, 20); U16(c, 8, s.Flags); U16(c, 10, s.Method);
            U16(c, 12, 0); U16(c, 14, 0x21); U32(c, 16, crc); U32(c, 20, size); U32(c, 24, size);
            U16(c, 28, (ushort)name.Length); U16(c, 30, 0); U16(c, 32, 0); U16(c, 34, 0); U16(c, 36, 0);
            U32(c, 38, s.External); U32(c, 42, s.SharesLocalOf is { } j ? offsets[j] : offsets[i]);
            body.Write(c); body.Write(name);
        }
        uint cdSize = (uint)body.Position - cdStart;
        var e = new byte[22];
        ushort n = declaredCount ?? (ushort)list.Count;
        U32(e, 0, 0x06054b50); U16(e, 8, n); U16(e, 10, n); U32(e, 12, cdSize); U32(e, 16, cdStart);
        body.Write(e);
        return body.ToArray();
    }

    /// <summary>A pack folder's files as build_catalog.py zips them: sorted, stored, regular files.</summary>
    public static byte[] ZipPack(IReadOnlyDictionary<string, byte[]> files) =>
        Zip(files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new ZipSpec(f.Key, f.Value)));

    public static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string Spki(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>A signature file as sign_catalog.sh writes it: P1363 base64 and one LF.</summary>
    public static byte[] SigFile(ECDsa key, byte[] catalog) =>
        Encoding.ASCII.GetBytes(Convert.ToBase64String(
            key.SignData(catalog, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) + "\n");

    public static string Sha256(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    /// <summary>A catalog.json the way build_catalog.py writes it: sorted keys, two-space indent, one LF at the end.</summary>
    public static byte[] Catalog(string generatedAt, params WidgetCatalogEntry[] packs)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"generated_at\": \"").Append(generatedAt).Append("\",\n  \"packs\": [");
        for (int i = 0; i < packs.Length; i++)
        {
            var p = packs[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    {\n");
            sb.Append($"      \"id\": \"{p.Id}\",\n");
            sb.Append($"      \"kind\": \"{p.Kind}\",\n");
            sb.Append($"      \"name\": \"{p.Name}\",\n");
            sb.Append($"      \"sanduhr_min\": \"{p.SanduhrMin}\",\n");
            sb.Append($"      \"sha256\": \"{p.Sha256}\",\n");
            sb.Append($"      \"size\": {p.Size},\n");
            sb.Append($"      \"url\": \"{p.Url}\",\n");
            sb.Append($"      \"version\": \"{p.Version}\"\n");
            sb.Append("    }");
        }
        sb.Append(packs.Length == 0 ? "],\n" : "\n  ],\n");
        sb.Append("  \"schema_version\": 1\n}\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>The catalog entry for a zip: the right URL, size and hash.</summary>
    public static WidgetCatalogEntry EntryFor(string id, string name, string kind, string version, byte[] zip, string min = "3.8.0") =>
        new(id, name, kind, version, min, $"{WidgetCatalog.DownloadPrefix}v{version}/{id}-{version}.zip", Sha256(zip), zip.LongLength);

    /// <summary>A minimal PNG encoder for the rasterized pixel sheets (the app uses WPF's).</summary>
    public static byte[] EncodePng(PixelSheetImage image) => PackValidatorTests.Png(image.Width, image.Height, image.Rgba);

    private static void U16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
    private static void U32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
}
