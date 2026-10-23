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
/// </summary>
public static class WidgetZip
{
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

    private sealed record Entry(string Name, uint Crc, long Size, long LocalOffset);

    /// <summary>The pack's files, or null and the reason. Never throws on bad input.</summary>
    public static IReadOnlyDictionary<string, byte[]>? Read(byte[] zip, out string error)
    {
        try
        {
            return ReadCore(zip, out error);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            error = "the zip is malformed";
            return null;
        }
    }

    private static IReadOnlyDictionary<string, byte[]>? ReadCore(byte[] zip, out string error)
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
        if (total > MaxEntries)
            return Fail(out error, $"the zip has {total} entries, a pack has at most {MaxEntries} files");
        if ((long)cdOffset + cdSize != eocd)
            return Fail(out error, "the zip's directory is not where its end record says");

        // -- central directory: every check that needs no data, before any data is read --------------------------------
        var entries = new List<Entry>(total);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            string? name = Name(zip, p + CentralSize, nameLen);
            if (name is null)
                return Fail(out error, $"entry {i} has a name that is not plain ASCII");
            if (name.EndsWith('/'))
                return Fail(out error, $"{name}: directory entries are not allowed in a pack zip");
            if (UnsafeName(name) is { } why)
                return Fail(out error, $"{name}: {why}");
            if ((flags & ~Utf8NameFlag) != 0)
                return Fail(out error, $"{name}: encrypted entries and data descriptors are not allowed");
            if (method != 0)
                return Fail(out error, $"{name}: compressed entries are not allowed (pack zips are stored)");
            if (csize != usize)
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
            if (!seen.Add(name))
                return Fail(out error, $"{name}: the zip has this name twice (names may not differ only in case)");
            entries.Add(new Entry(name, crc, usize, local));
            p = next;
        }
        if (p != eocd)
            return Fail(out error, "the zip's directory is malformed");

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
            string? name = Name(zip, a + LocalSize, nameLen);
            if (name != e.Name || (flags & ~Utf8NameFlag) != 0 || method != 0 || crc != e.Crc || csize != e.Size || usize != e.Size)
                return Fail(out error, $"{e.Name}: the entry's header disagrees with the zip's directory");
            long start = at + LocalSize + nameLen + extraLen;
            long end = start + e.Size;
            if (end > cdOffset)
                return Fail(out error, $"{e.Name}: the entry runs past the end of the data");
            var data = zip.AsSpan((int)start, (int)e.Size).ToArray();
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

    private static string? Name(byte[] zip, int at, int length)
    {
        if (length == 0 || at + length > zip.Length)
            return length == 0 ? "" : null;
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte b = zip[at + i];
            if (b < 0x21 || b > 0x7e)
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
