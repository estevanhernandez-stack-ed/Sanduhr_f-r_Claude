using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One linked Claude Code folder and the account it shows.</summary>
public sealed record Seat(string Folder, string AccountRef);

/// <summary>
/// Per-seat meters files (port of the file half of the Mac's SeatSnapshots.swift). Beside
/// <c>snapshot.json</c> (still the active account's) Sanduhr writes <c>snapshot.&lt;account_ref&gt;.json</c>
/// (the same schema 1, one per linked account) and <c>seats.json</c>
/// <c>{"schema_version": 1, "seats": [{"folder", "account_ref"}]}</c>. Neither ever holds a label,
/// an email or a key. With no link, neither file exists. Every write is a sibling temp file moved
/// over the target, so a reader never sees half a file.
/// </summary>
public static class SeatFiles
{
    public const string SeatsName = "seats.json";
    public const int SchemaVersion = 1;

    /// <summary>The path of the per-account snapshot for <paramref name="accountRef"/> in <paramref name="dir"/>.</summary>
    public static string SnapshotPath(string dir, string accountRef) => Path.Combine(dir, $"snapshot.{accountRef}.json");

    public static string SeatsPath(string dir) => Path.Combine(dir, SeatsName);

    // Which accounts

    /// <summary>The listed accounts with a folder linked, label to folder.</summary>
    public static Dictionary<string, string> Linked(IReadOnlyDictionary<string, string> accountFolders, IReadOnlyList<string> labels)
    {
        var map = new Dictionary<string, string>();
        foreach (var label in labels)
            if (accountFolders.TryGetValue(label, out var folder) && !string.IsNullOrWhiteSpace(folder))
                map[label] = folder;
        return map;
    }

    /// <summary>The seats seats.json lists, sorted by folder (ordinal, ignore case): one per linked account.</summary>
    public static List<Seat> Seats(IReadOnlyDictionary<string, string> linked)
    {
        var seats = new List<Seat>();
        foreach (var (label, folder) in linked)
        {
            var accountRef = SnapshotContract.AccountRef(label);
            if (accountRef is null) continue;
            var normal = NormalizeFolder(folder);
            if (normal is null) continue;
            seats.Add(new Seat(normal, accountRef));
        }
        seats.Sort((a, b) =>
        {
            int c = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.AccountRef, b.AccountRef);
        });
        return seats;
    }

    /// <summary>Full path with trailing separators trimmed (a root like C:\ keeps its one); null when unusable.</summary>
    public static string? NormalizeFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            while (full.Length > root.Length && (full[^1] == Path.DirectorySeparatorChar || full[^1] == Path.AltDirectorySeparatorChar))
                full = full[..^1];
            return full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // Names

    /// <summary>The account ref in <c>snapshot.&lt;ref&gt;.json</c>, null for any other name (snapshot.json included).</summary>
    public static string? RefInFileName(string name)
    {
        const string prefix = "snapshot.", suffix = ".json";
        if (name.Length != prefix.Length + 8 + suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        var r = name.Substring(prefix.Length, 8);
        return IsRef(r) ? r : null;
    }

    /// <summary>Eight lowercase hex digits, as <see cref="SnapshotContract.AccountRef"/> writes them.</summary>
    public static bool IsRef(string? s)
    {
        if (s is null || s.Length != 8) return false;
        foreach (var c in s)
            if (!(c is >= '0' and <= '9' || c is >= 'a' and <= 'f')) return false;
        return true;
    }

    /// <summary>The per-account snapshot refs on disk in <paramref name="dir"/>.</summary>
    public static HashSet<string> SnapshotRefs(string dir)
    {
        var refs = new HashSet<string>();
        if (!Directory.Exists(dir)) return refs;
        foreach (var path in Directory.EnumerateFiles(dir, "snapshot.*.json"))
            if (RefInFileName(Path.GetFileName(path)) is { } r) refs.Add(r);
        return refs;
    }

    // Writes

    /// <summary>seats.json, or deleted when there are no seats.</summary>
    public static void WriteSeats(string dir, IReadOnlyList<Seat> seats)
    {
        var path = SeatsPath(dir);
        if (seats.Count == 0) { DeleteFile(path); return; }
        var rows = new JsonArray();
        foreach (var s in seats)
            rows.Add(new JsonObject { ["folder"] = s.Folder, ["account_ref"] = s.AccountRef });
        var root = new JsonObject { ["schema_version"] = SchemaVersion, ["seats"] = rows };
        WriteAtomic(path, root.ToJsonString() + "\n");
    }

    /// <summary>A good fetch of <paramref name="label"/>'s account, written to its own snapshot file in the snapshot.json shape.</summary>
    public static void WriteAccount(string dir, string label, JsonObject payload, string? plan, DateTimeOffset now,
        string? writerVersion = null, Action<string>? logBestEffort = null)
    {
        if (SnapshotContract.AccountRef(label) is not { } r) return;
        Directory.CreateDirectory(dir);
        new SnapshotWriter(SnapshotPath(dir, r), logBestEffort, writerVersion).WriteOk(payload, plan, label, now);
    }

    /// <summary>A signed-out linked account's file: an auth error with no tiers.</summary>
    public static void WriteSignedOut(string dir, string label, DateTimeOffset? now = null,
        string? writerVersion = null, Action<string>? logBestEffort = null)
    {
        if (SnapshotContract.AccountRef(label) is not { } r) return;
        Directory.CreateDirectory(dir);
        new SnapshotWriter(SnapshotPath(dir, r), logBestEffort, writerVersion)
            .WriteSignedOut(null, label, now ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Deletes <paramref name="label"/>'s per-account file.</summary>
    public static void Delete(string dir, string label)
    {
        if (SnapshotContract.AccountRef(label) is { } r) DeleteFile(SnapshotPath(dir, r));
    }

    /// <summary>A renamed account's file follows it: moved to the new ref, <c>account_ref</c> rewritten.</summary>
    public static void Rename(string dir, string oldLabel, string newLabel)
    {
        var oldRef = SnapshotContract.AccountRef(oldLabel);
        var newRef = SnapshotContract.AccountRef(newLabel);
        if (oldRef is null || newRef is null || oldRef == newRef) return;
        var from = SnapshotPath(dir, oldRef);
        JsonObject? obj;
        try { obj = JsonNode.Parse(File.ReadAllText(from)) as JsonObject; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return; }
        if (obj is null) return;
        obj["account_ref"] = newRef;
        if (WriteAtomic(SnapshotPath(dir, newRef), obj.ToJsonString()))
            DeleteFile(from);
    }

    /// <summary>Deletes every <c>snapshot.&lt;ref&gt;.json</c> whose ref is not in <paramref name="keepRefs"/>.</summary>
    public static void Prune(string dir, IEnumerable<string> keepRefs)
    {
        var keep = keepRefs.ToHashSet();
        foreach (var r in SnapshotRefs(dir))
            if (!keep.Contains(r)) DeleteFile(SnapshotPath(dir, r));
    }

    private static bool WriteAtomic(string path, string text)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            return false;
        }
    }

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }
}

/// <summary>
/// The statusline's seat lookup rule, as C# for tests (the PowerShell script carries the same
/// rule): which snapshot file a session reads. Mirrors the Mac's <c>parseSeats</c> /
/// <c>seatSnapshotName</c> with Windows path semantics (full path, trailing separators trimmed,
/// compared ignoring case).
/// </summary>
public static class StatuslineSeatLookup
{
    public const int MaxBytes = 64 * 1024;

    /// <param name="seatsJsonText">seats.json's text, null when missing.</param>
    /// <param name="claudeConfigDir">CLAUDE_CONFIG_DIR; unset or empty means <c>%USERPROFILE%\.claude</c>.</param>
    /// <param name="userProfile">%USERPROFILE%.</param>
    public static string SeatSnapshotName(string? seatsJsonText, string? claudeConfigDir, string userProfile)
    {
        const string fallback = "snapshot.json";
        if (seatsJsonText is null || Encoding.UTF8.GetByteCount(seatsJsonText) > MaxBytes) return fallback;

        var configPath = string.IsNullOrEmpty(claudeConfigDir) ? Path.Combine(userProfile, ".claude") : claudeConfigDir;
        var folder = Key(ExpandHome(configPath, userProfile));
        if (folder is null) return fallback;

        JsonElement root;
        try { root = JsonDocument.Parse(seatsJsonText).RootElement; }
        catch (JsonException) { return fallback; }
        if (root.ValueKind != JsonValueKind.Object) return fallback;
        if (!root.TryGetProperty("schema_version", out var ver) || ver.ValueKind != JsonValueKind.Number
            || !ver.TryGetDouble(out var v) || v != SeatFiles.SchemaVersion) return fallback;
        if (!root.TryGetProperty("seats", out var rows) || rows.ValueKind != JsonValueKind.Array) return fallback;

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            if (!row.TryGetProperty("folder", out var f) || f.ValueKind != JsonValueKind.String) continue;
            if (!row.TryGetProperty("account_ref", out var r) || r.ValueKind != JsonValueKind.String) continue;
            var accountRef = r.GetString();
            if (!SeatFiles.IsRef(accountRef)) continue;
            var key = Key(ExpandHome(f.GetString()!, userProfile));
            if (key is not null && string.Equals(key, folder, StringComparison.OrdinalIgnoreCase))
                return $"snapshot.{accountRef}.json";
        }
        return fallback;
    }

    private static string ExpandHome(string path, string userProfile)
        => path == "~" || path.StartsWith("~\\", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal)
            ? userProfile + path[1..]
            : path;

    /// <summary>Full path, trailing separators trimmed; null for an empty or relative path (reads as unlinked).</summary>
    private static string? Key(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        return SeatFiles.NormalizeFolder(path);
    }
}
