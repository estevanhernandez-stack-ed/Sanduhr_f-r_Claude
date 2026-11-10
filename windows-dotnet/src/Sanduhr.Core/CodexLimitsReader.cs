using System.Text;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

public static class CodexHomes
{
    public static IReadOnlyList<string> Discover(string? codexHomeEnv, string userProfile)
    {
        var found = new List<string>();
        foreach (var candidate in new[] { codexHomeEnv, Path.Combine(userProfile, ".codex") })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string full;
            try { full = Path.GetFullPath(candidate); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!Directory.Exists(full)) continue;
            if (found.Any(f => string.Equals(f.TrimEnd('\\', '/'), full.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))) continue;
            found.Add(full);
        }
        return found;
    }
}

public enum CodexReadStatus { NotInstalled, NoLimits, Ok, Unreadable }

public sealed record CodexReadResult(CodexReadStatus Status, CodexLimitsReading? Reading);

/// <summary>
/// Finds the newest Codex limits in the rollouts under every Codex home. Plain <c>.jsonl</c> is read from its
/// tail; <c>.jsonl.zst</c> goes through the injected decompressor (Core has no package references). Reads
/// nothing outside <c>sessions</c>. Never throws.
/// File mtime is not trusted: Codex rewrites old rollouts when it compresses them, so the candidates are the
/// newest files by mtime and by the session timestamp in the file name, and the reading with the newest
/// event time wins. Per-file results are cached by (path, length, mtime).
/// </summary>
public sealed class CodexLimitsReader
{
    public const long MaxDecompressedBytes = 64L * 1024 * 1024;
    public const int MaxTailBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 20;

    private static readonly Regex SessionStamp = new(@"^rollout-(\d{4})-(\d{2})-(\d{2})T(\d{2})-(\d{2})-(\d{2})", RegexOptions.Compiled);

    private readonly Func<IReadOnlyList<string>> _homes;
    private readonly Func<Stream, Stream>? _decompressZst;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, (long Length, DateTime Mtime, CodexLimitsReading? Reading)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CodexLimitsReader(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst, Action<string>? log = null)
    {
        _homes = homes;
        _decompressZst = decompressZst;
        _log = log;
    }

    public CodexReadResult Read()
    {
        try { return ReadCore(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log?.Invoke($"codex: read failed ({e.GetType().Name})");
            return new(CodexReadStatus.Unreadable, null);
        }
    }

    private CodexReadResult ReadCore()
    {
        IReadOnlyList<string> homes;
        try { homes = _homes(); } catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"codex: homes failed ({e.GetType().Name})"); return new(CodexReadStatus.NotInstalled, null); }
        if (homes.Count == 0) return new(CodexReadStatus.NotInstalled, null);

        var files = new List<FileInfo>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var home in homes)
        {
            try
            {
                var sessions = Path.Combine(home, "sessions");
                if (!Directory.Exists(sessions)) continue;
                foreach (var path in Directory.EnumerateFiles(sessions, "rollout-*", options))
                    if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase))
                        files.Add(new FileInfo(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"codex: a sessions folder could not be listed ({e.GetType().Name})");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _log?.Invoke($"codex: a sessions folder could not be listed ({e.GetType().Name})");
            }
        }

        var live = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);
        foreach (var key in _cache.Keys.Where(k => !live.Contains(k)).ToList()) _cache.Remove(key);
        if (files.Count == 0) return new(CodexReadStatus.NoLimits, null);

        var byMtime = files.OrderByDescending(f => SafeMtime(f)).Take(MaxFiles);
        var byName = files.OrderByDescending(f => NameStamp(f.Name) ?? DateTime.MinValue).ThenByDescending(f => SafeMtime(f)).Take(MaxFiles);
        var candidates = byMtime.Concat(byName).DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        int tried = 0, failed = 0;
        CodexLimitsReading? best = null;
        foreach (var file in candidates)
        {
            tried++;
            CodexLimitsReading? reading;
            try
            {
                var length = file.Length;
                var mtime = file.LastWriteTimeUtc;
                if (_cache.TryGetValue(file.FullName, out var hit) && hit.Length == length && hit.Mtime == mtime)
                    reading = hit.Reading;
                else
                {
                    var text = ReadText(file, length);
                    if (text is null) continue;
                    reading = Newest(text);
                    _cache[file.FullName] = (length, mtime, reading);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                failed++;
                _log?.Invoke($"codex: a rollout could not be read ({e.GetType().Name})");
                continue;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed++;
                _log?.Invoke($"codex: a rollout could not be read ({e.GetType().Name})");
                continue;
            }
            if (reading is not null && (best is null || reading.AsOf > best.AsOf)) best = reading;
        }
        if (best is not null) return new(CodexReadStatus.Ok, best);
        return new(tried > 0 && failed == tried ? CodexReadStatus.Unreadable : CodexReadStatus.NoLimits, null);
    }

    private static CodexLimitsReading? Newest(string text)
    {
        var lines = text.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
            if (CodexLimits.TryParseLine(lines[i].TrimEnd('\r'), out var reading))
                return reading;
        return null;
    }

    private static DateTime SafeMtime(FileInfo f)
    {
        try { return f.LastWriteTimeUtc; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static DateTime? NameStamp(string name)
    {
        var m = SessionStamp.Match(name);
        if (!m.Success) return null;
        try
        {
            return new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private string? ReadText(FileInfo file, long length)
    {
        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
        {
            if (_decompressZst is null) return null;
            using var inflated = _decompressZst(fs);
            var tail = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int n;
            while ((n = inflated.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += n;
                if (total > MaxDecompressedBytes) throw new InvalidDataException("rollout over the size cap");
                tail.Write(chunk, 0, n);
                if (tail.Length > 2L * MaxTailBytes)
                {
                    var keep = new MemoryStream();
                    keep.Write(tail.GetBuffer(), (int)(tail.Length - MaxTailBytes), MaxTailBytes);
                    tail = keep;
                }
            }
            var take = (int)Math.Min(tail.Length, MaxTailBytes);
            return Encoding.UTF8.GetString(tail.GetBuffer(), (int)tail.Length - take, take);
        }
        long start = Math.Max(0, length - MaxTailBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[length - start];
        fs.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
