using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One move as it landed. <see cref="WrittenUtc"/> is the file's last write when it moved, so undo can tell it changed since;
/// a folder carries null, since its time changes with whatever is put in it.</summary>
public sealed record JournalMove(string From, string To, bool IsFolder, DateTime? WrittenUtc);

/// <summary>One applied plan, or one hand move by the user, as a single undoable entry.</summary>
public sealed record JournalEntry(string Id, DateTime AtUtc, DrawerPlanSource Source, string? Agent, IReadOnlyList<JournalMove> Moves);

/// <summary>What the MCP may read of the journal: when, who and how many. Never a path (5.0 drawers spec, section 4).</summary>
public sealed record JournalSummaryLine(DateTime AtUtc, DrawerPlanSource Source, string? Agent, int Files);

/// <summary>
/// The move journal (5.0 drawers spec, sections 1 and 4): paths only, never contents. Keeps the last 50 plans (rules and agents) and
/// the user's own moves for 30 days. Immutable: <see cref="Add"/> returns the trimmed journal to save.
/// </summary>
public sealed class DrawerJournal
{
    public const int MaxPlans = 50;
    public static readonly TimeSpan KeepUserMoves = TimeSpan.FromDays(30);

    public IReadOnlyList<JournalEntry> Entries { get; }

    public DrawerJournal() : this([]) { }

    private DrawerJournal(IReadOnlyList<JournalEntry> entries) => Entries = entries;

    public DrawerJournal Add(JournalEntry entry, DateTime nowUtc)
    {
        var all = Entries.Append(entry)
            .Where(e => e.Source != DrawerPlanSource.User || nowUtc - e.AtUtc <= KeepUserMoves)
            .ToList();
        var plans = all.Where(e => e.Source != DrawerPlanSource.User).ToList();
        foreach (var drop in plans.Take(Math.Max(0, plans.Count - MaxPlans))) all.Remove(drop);
        return new DrawerJournal(all);
    }

    public IReadOnlyList<JournalSummaryLine> Summary() =>
        Entries.Select(e => new JournalSummaryLine(e.AtUtc, e.Source, e.Agent, e.Moves.Count)).ToArray();

    public string Encode()
    {
        var entries = new JsonArray();
        foreach (var e in Entries)
        {
            var moves = new JsonArray();
            foreach (var m in e.Moves)
                moves.Add(new JsonObject
                {
                    ["from"] = m.From,
                    ["to"] = m.To,
                    ["folder"] = m.IsFolder,
                    ["written"] = m.WrittenUtc?.ToString("O", CultureInfo.InvariantCulture),
                });
            entries.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["at"] = e.AtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["source"] = e.Source.ToString().ToLowerInvariant(),
                ["agent"] = e.Agent,
                ["moves"] = moves,
            });
        }
        return new JsonObject { ["entries"] = entries }.ToJsonString();
    }

    /// <summary>Reads a saved journal. Anything unreadable is dropped entry by entry; a file that is not a journal reads as empty.</summary>
    public static DrawerJournal Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DrawerJournal();
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return new DrawerJournal(); }
        if (root?["entries"] is not JsonArray arr) return new DrawerJournal();
        var entries = new List<JournalEntry>();
        foreach (var n in arr)
        {
            if (n is not JsonObject o) continue;
            if (Str(o, "id") is not { } id || Time(o, "at") is not { } at || o["moves"] is not JsonArray ms) continue;
            if (!Enum.TryParse<DrawerPlanSource>(Str(o, "source"), ignoreCase: true, out var source)) continue;
            var moves = new List<JournalMove>();
            foreach (var mn in ms)
                if (mn is JsonObject m && Str(m, "from") is { } from && Str(m, "to") is { } to)
                    moves.Add(new JournalMove(from, to, SettingsJson.Bool(m, "folder", false), Time(m, "written")));
            if (moves.Count > 0) entries.Add(new JournalEntry(id, at, source, Str(o, "agent"), moves));
        }
        return new DrawerJournal(entries);
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static DateTime? Time(JsonObject o, string key) =>
        Str(o, key) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;
}

/// <summary>A step back, or a file undo leaves where it is and why.</summary>
public sealed record UndoStep(string From, string To);
public sealed record UndoSkip(string Path, string Reason);
public sealed record UndoPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<UndoSkip> Skipped);

/// <summary>Undo for one journal entry (5.0 drawers spec, section 1): every file back where it came from, newest move first. A file
/// that is gone or changed since is skipped and named; a taken old place gets a clash name, so undo never overwrites.</summary>
public static class DrawerUndo
{
    public const string MovedSince = "it was moved since.";
    public const string ChangedSince = "it changed since.";

    public static UndoPlan Plan(JournalEntry entry, Func<string, DateTime?> writtenUtc, Func<string, bool> exists)
    {
        var steps = new List<UndoStep>();
        var skipped = new List<UndoSkip>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Enumerable.Reverse by name: on .NET 10 an array's .Reverse() can bind to the in-place span overload.
        foreach (var m in Enumerable.Reverse(entry.Moves))
        {
            var now = writtenUtc(m.To);
            if (now is null) { skipped.Add(new UndoSkip(m.To, MovedSince)); continue; }
            if (!m.IsFolder && m.WrittenUtc is { } was && now != was) { skipped.Add(new UndoSkip(m.To, ChangedSince)); continue; }
            var dir = Path.GetDirectoryName(m.From)!;
            var back = DrawerNames.Free(dir, Path.GetFileName(m.From), m.IsFolder, p => exists(p) || claimed.Contains(p));
            claimed.Add(back);
            steps.Add(new UndoStep(m.To, back));
        }
        return new UndoPlan(steps, skipped);
    }
}
