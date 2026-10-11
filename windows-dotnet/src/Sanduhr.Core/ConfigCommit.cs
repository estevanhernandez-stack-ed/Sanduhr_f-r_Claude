using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sanduhr.Core;

/// <summary>How a <see cref="ConfigCommit.RetryCommit"/> ended.</summary>
public enum CommitOutcome
{
    /// <summary>The planned bytes were written.</summary>
    Written,
    /// <summary>The plan returned null: nothing was written.</summary>
    NothingToDo,
    /// <summary>The file changed under every try: nothing was written.</summary>
    Conflict,
}

/// <summary>
/// Compare-and-swap writes to someone else's config file (<c>settings.json</c>, <c>.claude.json</c>).
/// Port of the Mac installer's <c>read</c> and <c>commit</c>.
///
/// Claude Code may write the file while Sanduhr plans an edit, so a commit re-reads the file and
/// aborts when its bytes are no longer the ones the edit was planned against. The window between that
/// re-read and the rename is not closed (Windows has no cross-process compare-and-rename); it is a few
/// microseconds wide, and the two writers that matter replace the file whole.
///
/// Rules: a file that isn't a JSON object is never written (and neither are bytes that aren't one);
/// the first change keeps a copy of the ORIGINAL bytes in the backups folder
/// (<c>&lt;12 hex of SHA-256 of the lowercased full path&gt;-&lt;yyyyMMddHHmmssfff&gt;.json</c>), never beside the file;
/// the new bytes go to a hidden temp sibling that is moved over the target. A symlink at the target is
/// not followed: the move replaces the link with a regular file.
/// </summary>
public sealed class ConfigCommit(string backupsFolder)
{
    public string BackupsFolder { get; } = backupsFolder;

    /// <summary>The file's bytes, or null when it doesn't exist.</summary>
    public byte[]? Read(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> (null deletes the file) unless the file changed since
    /// <paramref name="original"/> was read (null: the file didn't exist); returns false then, for the
    /// caller to start over. With <paramref name="backup"/> and an existing file, copies the original
    /// bytes to the backups folder first. Throws <see cref="JsonEditException"/> when the existing file
    /// or the new bytes aren't a JSON object, and <see cref="IOException"/> when the write fails.
    /// </summary>
    public bool Commit(string path, byte[]? original, byte[]? bytes, bool backup)
    {
        path = Path.GetFullPath(path);
        if (original is not null && !JsonEdit.IsObject(original)) throw new JsonEditException(JsonEditFailure.Malformed);
        if (bytes is not null && !JsonEdit.IsObject(bytes)) throw new JsonEditException(JsonEditFailure.Malformed);

        // The re-read: Claude Code may have written meanwhile.
        var now = Read(path);
        if (!SameBytes(now, original)) return false;

        if (backup && original is not null) WriteBackup(path, original);

        if (bytes is null)
        {
            if (now is not null) File.Delete(path);
            return true;
        }

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.sanduhr-tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.SetAttributes(tmp, FileAttributes.Hidden);
            // The target keeps the attributes it had (the temp must not leave it hidden).
            var attrs = now is null ? FileAttributes.Normal : File.GetAttributes(path);
            File.SetAttributes(tmp, attrs);
            if (now is null)
            {
                // Created: a file that appeared meanwhile makes this a conflict, not an overwrite.
                try { File.Move(tmp, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { return false; }
            }
            else
            {
                File.Move(tmp, path, overwrite: true);
            }
            return true;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Reads the file, asks <paramref name="plan"/> for the bytes to write (null: leave the file alone),
    /// and commits them; starts over when the file changed in between, up to <paramref name="tries"/> times.
    /// </summary>
    public CommitOutcome RetryCommit(string path, Func<byte[]?, byte[]?> plan, bool backup, int tries = 3)
    {
        for (int n = 0; n < tries; n++)
        {
            var original = Read(path);
            var bytes = plan(original);
            if (bytes is null) return CommitOutcome.NothingToDo;
            if (Commit(path, original, bytes, backup)) return CommitOutcome.Written;
        }
        return CommitOutcome.Conflict;
    }

    /// <summary>The backup file name stem for <paramref name="path"/>: 12 hex characters of SHA-256 of the lowercased full path.</summary>
    public static string PathHash(string path)
    {
        var full = Path.GetFullPath(path).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
    }

    private void WriteBackup(string path, byte[] original)
    {
        Directory.CreateDirectory(BackupsFolder);
        var hash = PathHash(path);
        var stamp = DateTime.UtcNow;
        while (true)
        {
            var name = $"{hash}-{stamp.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}.json";
            var target = Path.Combine(BackupsFolder, name);
            try
            {
                using var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Write(original);
                return;
            }
            catch (IOException) when (File.Exists(target))
            {
                stamp = stamp.AddMilliseconds(1);
            }
        }
    }

    private static bool SameBytes(byte[]? a, byte[]? b)
        => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}

/// <summary>Writes a whole file through a hidden temp sibling moved over the target.</summary>
internal static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.sanduhr-tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.SetAttributes(tmp, FileAttributes.Normal);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
