using System.Text;

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
/// </summary>
public sealed class CodexLimitsReader
{
    public const long MaxDecompressedBytes = 64L * 1024 * 1024;
    public const int MaxTailBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 20;

    private readonly Func<IReadOnlyList<string>> _homes;
    private readonly Func<Stream, Stream>? _decompressZst;
    private readonly Action<string>? _log;

    public CodexLimitsReader(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst, Action<string>? log = null)
    {
        _homes = homes;
        _decompressZst = decompressZst;
        _log = log;
    }

    public CodexReadResult Read()
    {
        IReadOnlyList<string> homes;
        try { homes = _homes(); } catch (Exception e) { _log?.Invoke($"codex: homes failed ({e.GetType().Name})"); return new(CodexReadStatus.NotInstalled, null); }
        if (homes.Count == 0) return new(CodexReadStatus.NotInstalled, null);

        var files = new List<FileInfo>();
        foreach (var home in homes)
        {
            var sessions = Path.Combine(home, "sessions");
            if (!Directory.Exists(sessions)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(sessions, "rollout-*", SearchOption.AllDirectories))
                    if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase))
                        files.Add(new FileInfo(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"codex: a sessions folder could not be listed ({e.GetType().Name})");
            }
        }
        if (files.Count == 0) return new(CodexReadStatus.NoLimits, null);

        int tried = 0, failed = 0;
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxFiles))
        {
            tried++;
            string? text;
            try { text = ReadText(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                failed++;
                _log?.Invoke($"codex: a rollout could not be read ({e.GetType().Name})");
                continue;
            }
            if (text is null) continue;
            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (CodexLimits.TryParseLine(lines[i].TrimEnd('\r'), out var reading))
                    return new(CodexReadStatus.Ok, reading);
        }
        return new(tried > 0 && failed == tried ? CodexReadStatus.Unreadable : CodexReadStatus.NoLimits, null);
    }

    private string? ReadText(FileInfo file)
    {
        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
        {
            if (_decompressZst is null) return null;
            using var inflated = _decompressZst(fs);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            while ((n = inflated.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + n > MaxDecompressedBytes) throw new InvalidDataException("rollout over the size cap");
                buffer.Write(chunk, 0, n);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        long start = Math.Max(0, fs.Length - MaxTailBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[fs.Length - start];
        fs.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
