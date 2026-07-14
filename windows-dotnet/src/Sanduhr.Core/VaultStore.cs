using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>
/// Per-root vault file IO (spec: Storage). One directory per CC home under
/// <see cref="Paths.VaultDir"/> — tenant separation is structural: per-root
/// purge is a folder delete, audit handover is a folder handover.
///
/// Writes serialize to a .tmp sibling then atomically replace (2–3 retries).
/// A session-shard save that still fails THROWS — the ingester must abort that
/// root's cycle before rollups/checkpoints (write-ordering invariant).
/// Quarantine (ingest-side only) renames a corrupt session shard to a
/// timestamped .bad — never overwritten, never auto-deleted — and atomically
/// deletes that root's checkpoints so the next cycle's full re-ingest rebuilds
/// everything still on disk.
/// </summary>
public sealed class VaultStore
{
    private const int WriteRetries = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    private readonly string _vaultDir;
    private readonly string? _logFile;

    public VaultStore(string vaultDir, string? logFile = null)
    {
        _vaultDir = vaultDir;
        _logFile = logFile;
    }

    public string RootDir(string rootName) => Path.Combine(_vaultDir, rootName);

    private string SessionShardPath(string rootName, string month)
        => Path.Combine(RootDir(rootName), $"sessions-{month}.json");

    private string RollupShardPath(string rootName, string month)
        => Path.Combine(RootDir(rootName), $"rollups-{month}.json");

    private string CheckpointsPath(string rootName)
        => Path.Combine(RootDir(rootName), "checkpoints.json");

    private string MetaPath(string rootName)
        => Path.Combine(RootDir(rootName), "meta.json");

    /// <summary>SHA-256 hex (lowercase) of the LOWERCASED absolute path — the
    /// checkpoint key. Case-folded because NTFS paths are case-insensitive.</summary>
    public static string PathKey(string absolutePath)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(absolutePath.ToLowerInvariant())))
            .ToLowerInvariant();

    // -- session shards --------------------------------------------------------

    public ShardLoadResult TryLoadSessionShard(string rootName, string month, out VaultSessionShard shard)
        => TryLoad(SessionShardPath(rootName, month), out shard);

    /// <summary>Throws (IOException or UnauthorizedAccessException) when the
    /// atomic replace still fails after retries — the caller aborts the root's
    /// cycle (checkpoints must not advance past a shard that never landed).</summary>
    public void SaveSessionShard(string rootName, string month, VaultSessionShard shard)
        => WriteAtomic(SessionShardPath(rootName, month), JsonSerializer.Serialize(shard, JsonOpts), throwOnFailure: true);

    // -- rollup shards (derived cache) -----------------------------------------

    public ShardLoadResult TryLoadRollupShard(string rootName, string month, out VaultRollupShard shard)
        => TryLoad(RollupShardPath(rootName, month), out shard);

    public void SaveRollupShard(string rootName, string month, VaultRollupShard shard)
        => WriteAtomic(RollupShardPath(rootName, month), JsonSerializer.Serialize(shard, JsonOpts), throwOnFailure: false);

    public void DeleteRollupShard(string rootName, string month)
    {
        try { File.Delete(RollupShardPath(rootName, month)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("rollup-delete", e);
        }
    }

    // -- checkpoints ------------------------------------------------------------

    /// <summary>Corrupt checkpoints are disposable: delete the file, return empty —
    /// the resulting full re-ingest converges (idempotent by design).</summary>
    public VaultCheckpointFile LoadCheckpoints(string rootName)
    {
        var p = CheckpointsPath(rootName);
        if (!File.Exists(p))
            return new VaultCheckpointFile();
        try
        {
            return JsonSerializer.Deserialize<VaultCheckpointFile>(File.ReadAllText(p))
                   ?? new VaultCheckpointFile();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("checkpoints-load", e);
            try { File.Delete(p); }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException)
            {
                LogBestEffort("checkpoints-delete", e2);
            }
            return new VaultCheckpointFile();
        }
    }

    /// <summary>Best-effort standalone checkpoint invalidation — the
    /// walk_version upgrade's tool. The next cycle full-re-ingests everything
    /// still on disk (idempotent by design).</summary>
    public void DeleteCheckpoints(string rootName)
    {
        try
        {
            File.Delete(CheckpointsPath(rootName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("checkpoints-delete", e);
        }
    }

    public void SaveCheckpoints(string rootName, VaultCheckpointFile file)
        => WriteAtomic(CheckpointsPath(rootName), JsonSerializer.Serialize(file, JsonOpts), throwOnFailure: false);

    // -- meta --------------------------------------------------------------------

    public VaultRootMeta? LoadMeta(string rootName)
    {
        var p = MetaPath(rootName);
        if (!File.Exists(p))
            return null;
        try
        {
            return JsonSerializer.Deserialize<VaultRootMeta>(File.ReadAllText(p));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("meta-load", e);
            return null;
        }
    }

    public void SaveMeta(string rootName, VaultRootMeta meta)
        => WriteAtomic(MetaPath(rootName), JsonSerializer.Serialize(meta, JsonOpts), throwOnFailure: false);

    // -- discovery / quarantine / purge ------------------------------------------

    /// <summary>Months (yyyy-MM, ascending) that have a session shard on disk.
    /// Ignores rollups, quarantined .bad files, and anything misnamed.</summary>
    public IReadOnlyList<string> ListSessionShardMonths(string rootName)
    {
        var dir = RootDir(rootName);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        var months = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "sessions-*.json"))
        {
            var name = Path.GetFileName(f);
            // Exactly "sessions-YYYY-MM.json" (quarantine adds a suffix after .json).
            if (name.Length == "sessions-YYYY-MM.json".Length
                && name.EndsWith(".json", StringComparison.Ordinal))
            {
                months.Add(name.Substring("sessions-".Length, 7));
            }
        }
        months.Sort(StringComparer.Ordinal);
        return months;
    }

    /// <summary>Ingest-side only (the read path never mutates). Rename to a
    /// timestamped .bad (uniquified, never overwritten — for months older than
    /// JSONL retention the .bad IS the archive) and delete checkpoints.json so
    /// the next cycle re-ingests everything still on disk. Checkpoints are deleted
    /// FIRST — a crash between the two steps then leaves the corrupt shard in place
    /// with no checkpoints, which the next cycle re-quarantines; the reverse order
    /// would strand stale checkpoints pointing at a vanished shard.</summary>
    public void QuarantineSessionShard(string rootName, string month, DateTimeOffset nowUtc)
    {
        var src = SessionShardPath(rootName, month);
        var stamp = nowUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var dest = $"{src}.{stamp}.bad";
        int n = 1;
        while (File.Exists(dest))
            dest = $"{src}.{stamp}-{n++}.bad";
        try
        {
            File.Delete(CheckpointsPath(rootName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("quarantine-checkpoints", e);
        }
        try
        {
            File.Move(src, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("quarantine", e);
        }
    }

    public void PurgeRoot(string rootName)
    {
        try
        {
            if (Directory.Exists(RootDir(rootName)))
                Directory.Delete(RootDir(rootName), recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("purge-root", e);
        }
    }

    public void PurgeAll()
    {
        try
        {
            if (Directory.Exists(_vaultDir))
                Directory.Delete(_vaultDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("purge-all", e);
        }
    }

    // -- plumbing -----------------------------------------------------------------

    private ShardLoadResult TryLoad<T>(string path, out T value) where T : new()
    {
        value = new T();
        if (!File.Exists(path))
            return ShardLoadResult.Missing;
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            if (parsed is null)
                return ShardLoadResult.Corrupt;
            value = parsed;
            return ShardLoadResult.Ok;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("shard-load", e);
            return ShardLoadResult.Corrupt;
        }
    }

    private void WriteAtomic(string path, string json, bool throwOnFailure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        Exception? last = null;
        for (int attempt = 0; attempt < WriteRetries; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                last = e;
                Thread.Sleep(25);
            }
        }
        try { File.Delete(tmp); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LogBestEffort("tmp-cleanup", e); }
        LogBestEffort("write", last!);
        if (throwOnFailure)
            throw last!;
    }

    // PRIVACY.md contract: operation + exception TYPE only. Never e.Message
    // (file-op messages embed paths), never file contents, never root names
    // beyond the fixed ".claude"/".claude-personal" vocabulary — and we skip
    // even those to keep the rule mechanical.
    private void LogBestEffort(string operation, Exception e)
    {
        if (_logFile is null)
            return;
        try
        {
            File.AppendAllText(_logFile,
                $"{DateTime.UtcNow:o} vault {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
            // Swallow — logging must never break vault IO.
        }
    }
}
