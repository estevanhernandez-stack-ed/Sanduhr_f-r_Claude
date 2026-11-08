using System.Buffers.Binary;

namespace Sanduhr.Core;

/// <summary>
/// Reads a widget pack zip into the in-memory map <see cref="PackValidator"/> takes (relative POSIX path to bytes), or
/// refuses it. FORMAT 7: release zips hold the pack's files at the root, <b>stored without compression</b>, with no
/// directory entries. So this reader never inflates anything: it walks the central directory itself, checks every
/// entry's declared sizes against FORMAT 3's limits before reading a byte of data, then slices each stored entry out
/// and checks it again (local header agrees with the central one, length, CRC-32).
///
/// Refused: compressed or encrypted entries, data descriptors, ZIP64, multi-disk archives, directory entries,
/// symlinks or any non-regular file, names that are empty, absolute, use a backslash or a drive colon, have an empty,
/// <c>.</c> or <c>..</c> segment or a non-ASCII or control character, two names that differ only in case
/// (<c>Sprite.json</c> and <c>sprite.json</c>), more than 67 entries (64 listed files plus <c>pack.json</c>,
/// <c>LICENSE</c> and the preview), an entry over 5 MB (the largest file FORMAT allows, a font) or a total over 10 MB,
/// and entries whose data overlap. The validator then applies the precise per-kind rules. Pure: bytes in, map out.
///
/// A zip the user picks (<c>fromUser</c>, Install widget from file, #459) may also be what Explorer's Send to,
/// Compressed folder, Finder's Compress and zip tools write: deflated entries (inflated here, never past the declared
/// size, which the 5 MB and 10 MB limits bound before any inflating, then CRC-checked), data descriptors, folder entries,
/// <c>__MACOSX/</c> and <c>.DS_Store</c> (skipped), and the pack's files under one shared top folder (that folder is
/// dropped). The catalog's own zips stay on the strict path: they are signed artifacts in FORMAT 7's form.
/// </summary>
public static class WidgetZip
{
    /// <summary>Raw entries a user's zip may list: Finder adds a <c>__MACOSX/</c> twin per file and a folder entry per folder,
    /// so the kept files (still at most <see cref="MaxEntries"/>) can come with as many again of entries that are skipped.</summary>
    public const int MaxUserRawEntries = MaxEntries * 4;

    private const ushort DescriptorFlag = 0x0008;
    private const ushort DeflateOptionFlags = 0x0006;
    private const ushort Deflate = 8;

    /// <summary>64 files in <c>files</c> plus pack.json, LICENSE and the preview.</summary>
    public const int MaxEntries = PackValidator.MaxFiles + 3;

    /// <summary>The largest single file FORMAT 3 allows (a font, 5 MB; images and pixel sheets are 2 MB).</summary>
    public const long MaxEntryBytes = PackValidator.MaxFontBytes;

    public const long MaxTotalBytes = PackValidator.MaxPackBytes;

    private const uint EocdSig = 0x06054b50;
    private const uint CentralSig = 0x02014b50;
    private const uint LocalSig = 0x04034b50;
    private const uint Zip64LocatorSig = 0x07064b50;
    private const int EocdSize = 22;
    private const int CentralSize = 46;
    private const int LocalSize = 30;
    private const ushort Utf8NameFlag = 0x0800;

    private sealed record Entry(string RawName, uint Crc, long Size, long CompressedSize, ushort Method, long LocalOffset)
    {
        public string Name { get; set; } = RawName;
    }

    /// <summary>The pack's files, or null and the reason. Never throws on bad input.</summary>
    /// <param name="fromUser">A zip the user picked from disk: also accept what ordinary zip tools write (see the class
    /// summary). False, the catalog path, keeps FORMAT 7's stored-only form.</param>
    public static IReadOnlyDictionary<string, byte[]>? Read(byte[] zip, out string error, bool fromUser = false)
    {
        try
        {
            return ReadCore(zip, out error, fromUser);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            error = "the zip is malformed";
            return null;
        }
    }

    /// <summary>An entry a user's zip may carry that is not a pack file: folders, Finder's resource forks and its folder view file.</summary>
    private static bool Skippable(string name) =>
        name.EndsWith('/') || name.StartsWith("__MACOSX/", StringComparison.Ordinal)
        || name == ".DS_Store" || name.EndsWith("/.DS_Store", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, byte[]>? ReadCore(byte[] zip, out string error, bool fromUser)
    {
        error = "";
        if (zip is null || zip.Length < EocdSize)
            return Fail(out error, "the file is not a zip");
        if (zip.LongLength > WidgetCatalog.MaxZipBytes)
            return Fail(out error, "the zip is larger than a pack may be");

        // -- end of central directory: the last one, whose comment runs exactly to the end of the file ------------------
        int eocd = -1;
        for (int at = zip.Length - EocdSize; at >= 0 && at >= zip.Length - EocdSize - 0xFFFF; at--)
        {
            if (U32(zip, at) == EocdSig && at + EocdSize + U16(zip, at + 20) == zip.Length)
            {
                eocd = at;
                break;
            }
        }
        if (eocd < 0)
            return Fail(out error, "the zip has no end-of-directory record");
        if (eocd >= 20 && U32(zip, eocd - 20) == Zip64LocatorSig)
            return Fail(out error, "ZIP64 archives are not packs");
        ushort disk = U16(zip, eocd + 4), cdDisk = U16(zip, eocd + 6);
        ushort onDisk = U16(zip, eocd + 8), total = U16(zip, eocd + 10);
        uint cdSize = U32(zip, eocd + 12), cdOffset = U32(zip, eocd + 16);
        if (disk != 0 || cdDisk != 0 || onDisk != total)
            return Fail(out error, "multi-disk zips are not packs");
        if (total == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
            return Fail(out error, "ZIP64 archives are not packs");
        if (total > (fromUser ? MaxUserRawEntries : MaxEntries))
            return Fail(out error, $"the zip has {total} entries, a pack has at most {MaxEntries} files");
        if ((long)cdOffset + cdSize != eocd)
            return Fail(out error, "the zip's directory is not where its end record says");

        // -- central directory: every check that needs no data, before any data is read --------------------------------
        var entries = new List<Entry>(total);
        ushort allowedFlags = (ushort)(Utf8NameFlag | (fromUser ? DescriptorFlag | DeflateOptionFlags : 0));
        long sum = 0;
        int p = (int)cdOffset;
        for (int i = 0; i < total; i++)
        {
            if (p + CentralSize > eocd || U32(zip, p) != CentralSig)
                return Fail(out error, "the zip's directory is malformed");
            ushort madeBy = U16(zip, p + 4);
            ushort flags = U16(zip, p + 8), method = U16(zip, p + 10);
            uint crc = U32(zip, p + 16), csize = U32(zip, p + 20), usize = U32(zip, p + 24);
            ushort nameLen = U16(zip, p + 28), extraLen = U16(zip, p + 30), commentLen = U16(zip, p + 32);
            ushort startDisk = U16(zip, p + 34);
            uint external = U32(zip, p + 38), local = U32(zip, p + 42);
            int next = p + CentralSize + nameLen + extraLen + commentLen;
            if (next > eocd)
                return Fail(out error, "the zip's directory is malformed");

            string? name = Name(zip, p + CentralSize, nameLen, allowSpace: fromUser);
            if (name is null)
                return Fail(out error, $"entry {i} has a name that is not plain ASCII");
            if (fromUser && Skippable(name))
            {
                p = next;   // a folder, a resource fork or a folder view file: not part of the pack, never read
                continue;
            }
            if (name.EndsWith('/'))
                return Fail(out error, $"{name}: directory entries are not allowed in a pack zip");
            if (UnsafeName(name) is { } why)
                return Fail(out error, $"{name}: {why}");
            if ((flags & 0x0001) != 0)
                return Fail(out error, $"{name}: encrypted entries are not allowed");
            if ((flags & ~allowedFlags) != 0)
                return Fail(out error, $"{name}: encrypted entries and data descriptors are not allowed");
            if (method != 0 && !(fromUser && method == Deflate))
                return Fail(out error, fromUser
                    ? $"{name}: the entry is compressed in a way Sanduhr can't read (only deflate)"
                    : $"{name}: compressed entries are not allowed (pack zips are stored)");
            if (method == 0 && csize != usize)
                return Fail(out error, $"{name}: a stored entry's sizes must agree");
            if (startDisk != 0)
                return Fail(out error, $"{name}: multi-disk zips are not packs");
            if (!IsRegularFile(madeBy, external))
                return Fail(out error, $"{name}: only regular files are allowed (no links or folders)");
            if (usize > MaxEntryBytes)
                return Fail(out error, $"{name}: {usize} bytes is over the 5 MB a pack file may be");
            sum += usize;
            if (sum > MaxTotalBytes)
                return Fail(out error, "the zip's files add up to more than the 10 MB a pack may be");
            entries.Add(new Entry(name, crc, usize, csize, method, local));
            p = next;
        }
        if (p != eocd)
            return Fail(out error, "the zip's directory is malformed");
        if (entries.Count > MaxEntries)
            return Fail(out error, $"the zip has {entries.Count} files, a pack has at most {MaxEntries}");

        // A folder compressed whole (Explorer's Send to, Compressed folder; Finder's Compress) puts every file under it.
        if (fromUser && entries.Count > 0 && entries[0].Name.IndexOf('/') is var slash and > 0)
        {
            string top = entries[0].Name[..(slash + 1)];
            if (entries.All(e => e.Name.StartsWith(top, StringComparison.Ordinal) && e.Name.Length > top.Length))
                foreach (var e in entries) e.Name = e.Name[top.Length..];
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (!seen.Add(e.Name))
                return Fail(out error, $"{e.Name}: the zip has this name twice (names may not differ only in case)");
        }

        // -- the data: each local header agrees with its central entry, no two entries overlap, CRCs match ---------------
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long previousEnd = 0;
        foreach (var e in entries.OrderBy(e => e.LocalOffset))
        {
            long at = e.LocalOffset;
            if (at < previousEnd || at + LocalSize > cdOffset || U32(zip, (int)at) != LocalSig)
                return Fail(out error, $"{e.Name}: the entry's header is missing or overlaps another");
            int a = (int)at;
            ushort flags = U16(zip, a + 6), method = U16(zip, a + 8);
            uint crc = U32(zip, a + 14), csize = U32(zip, a + 18), usize = U32(zip, a + 22);
            ushort nameLen = U16(zip, a + 26), extraLen = U16(zip, a + 28);
            string? name = Name(zip, a + LocalSize, nameLen, allowSpace: fromUser);
            // With a data descriptor the local header's CRC and sizes may be zero: the central directory's, checked
            // above, are the ones that count, and the data is checked against them below.
            bool descriptor = (flags & DescriptorFlag) != 0;
            if (name != e.RawName || (flags & ~allowedFlags) != 0 || method != e.Method
                || (!descriptor && (crc != e.Crc || csize != e.CompressedSize || usize != e.Size)))
                return Fail(out error, $"{e.Name}: the entry's header disagrees with the zip's directory");
            long start = at + LocalSize + nameLen + extraLen;
            long end = start + e.CompressedSize;
            if (end > cdOffset)
                return Fail(out error, $"{e.Name}: the entry runs past the end of the data");
            byte[]? data = e.Method == Deflate
                ? Inflate(zip, (int)start, (int)e.CompressedSize, (int)e.Size)
                : zip.AsSpan((int)start, (int)e.Size).ToArray();
            if (data is null)
                return Fail(out error, $"{e.Name}: the entry's compressed data is damaged or not the size it declares");
            if (data.LongLength != e.Size || data.LongLength > MaxEntryBytes)
                return Fail(out error, $"{e.Name}: the entry's size is wrong");
            if (PackValidator.Crc32(data, 0, data.Length) != e.Crc)
                return Fail(out error, $"{e.Name}: CRC-32 mismatch");
            files[e.Name] = data;
            previousEnd = end;
        }
        if (files.Values.Sum(b => (long)b.Length) > MaxTotalBytes)
            return Fail(out error, "the zip's files add up to more than the 10 MB a pack may be");
        return files;
    }

    /// <summary>
    /// The entry's bytes, inflated into a buffer of exactly the declared size (already held to the 5 MB limit), or null when
    /// the data is damaged, ends early or would run past that size. Nothing is ever inflated beyond what was declared, so a
    /// small zip can't expand without bound.
    /// </summary>
    private static byte[]? Inflate(byte[] zip, int start, int compressedSize, int size)
    {
        try
        {
            using var deflate = new System.IO.Compression.DeflateStream(
                new MemoryStream(zip, start, compressedSize, writable: false), System.IO.Compression.CompressionMode.Decompress);
            var data = new byte[size];
            int filled = 0;
            while (filled < size)
            {
                int n = deflate.Read(data, filled, size - filled);
                if (n == 0) return null;   // ends before the declared size
                filled += n;
            }
            return deflate.Read(new byte[1], 0, 1) == 0 ? data : null;   // more than declared: refused
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Why a zip entry name is unsafe, or null. The validator's own path rule (FORMAT 3.5) runs after this.</summary>
    internal static string? UnsafeName(string name)
    {
        if (name.Length == 0)
            return "an empty name";
        if (name.StartsWith('/'))
            return "absolute paths are not allowed";
        if (name.Contains('\\'))
            return "backslashes are not allowed";
        if (name.Contains(':'))
            return "drive letters and colons are not allowed";
        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return "empty, . and .. path segments are not allowed";
        }
        return null;
    }

    /// <summary>
    /// A regular file by its attributes. Unix-made entries (host 3) carry a mode in the high 16 bits: it must be a
    /// regular file (or absent). The MS-DOS directory bit is refused from any host.
    /// </summary>
    private static bool IsRegularFile(ushort madeBy, uint external)
    {
        if ((external & 0x10) != 0)
            return false;
        int host = madeBy >> 8;
        if (host == 3)
        {
            uint mode = external >> 16;
            if (mode != 0 && (mode & 0xF000) != 0x8000)
                return false;
        }
        return true;
    }

    /// <param name="allowSpace">A zip the user picked: Finder and Explorer name the folder inside as the person named it ("My
    /// Companion/"), so a raw name may carry a space. That folder is dropped and Finder's extras skipped; every kept name still
    /// meets the validator's path rule (FORMAT 3.5), which allows no space, so "my file.json" is still refused, by the pack rules.
    /// The Mac reader does the same (#471).</param>
    private static string? Name(byte[] zip, int at, int length, bool allowSpace = false)
    {
        if (length == 0 || at + length > zip.Length)
            return length == 0 ? "" : null;
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte b = zip[at + i];
            if ((b < 0x21 && !(allowSpace && b == 0x20)) || b > 0x7e)
                return null;
            chars[i] = (char)b;
        }
        return new string(chars);
    }

    private static IReadOnlyDictionary<string, byte[]>? Fail(out string error, string why)
    {
        error = why;
        return null;
    }

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
}
