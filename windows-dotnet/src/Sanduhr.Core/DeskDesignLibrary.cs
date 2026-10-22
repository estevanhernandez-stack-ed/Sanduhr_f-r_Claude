using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// A design the Desk has kept (spec, the agent-designed Desk, saved designs): the full design with a name, an id, when it
/// was applied and whether the user pinned it. One JSON document each, <c>{schema_version, id, name, pinned, applied_at,
/// design}</c>. Pure data; the App stores the documents one per file under <c>%APPDATA%\Sanduhr\designs\</c>.
/// </summary>
public sealed partial record SavedDesign(string Id, string Name, bool Pinned, DateTimeOffset AppliedAt, DeskDesign Design)
{
    public const int SchemaVersion = 1;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>A design id is a short lowercase file-safe key (it names the design's file), never a path.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>A new id: 32 hex digits.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    public JsonObject ToJson() => new()
    {
        ["schema_version"] = SchemaVersion,
        ["id"] = Id,
        ["name"] = Name,
        ["pinned"] = Pinned,
        ["applied_at"] = AppliedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["design"] = Design.ToJson(),
    };

    /// <summary>The row <c>get_desk</c> lists: <c>{id, name, pinned, applied_at}</c>.</summary>
    public JsonObject SummaryJson() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["pinned"] = Pinned,
        ["applied_at"] = AppliedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    /// <summary>Reads a stored design; null when the document is not one (wrong schema, bad id, a design that no longer parses).</summary>
    public static SavedDesign? Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        if (!(o["schema_version"] is JsonValue sv && sv.TryGetValue<int>(out int version) && version == SchemaVersion)) return null;
        if (!(o["id"] is JsonValue iv && iv.TryGetValue<string>(out var id) && IsValidId(id))) return null;
        if (!(o["name"] is JsonValue nv && nv.TryGetValue<string>(out var name) && name.Length is > 0 and <= DeskDesign.NameMax * 4)) return null;
        bool pinned = o["pinned"] is JsonValue pv && pv.TryGetValue<bool>(out var p) && p;
        if (!(o["applied_at"] is JsonValue av && av.TryGetValue<string>(out var stamp)
              && DateTimeOffset.TryParseExact(stamp, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))) return null;
        var parsed = DeskDesign.Parse(o["design"]);
        return parsed.Design is { } design ? new SavedDesign(id, name, pinned, at, design) : null;
    }
}

/// <summary>What <see cref="DeskDesignLibrary.Prune"/> decided: the designs to keep (newest first) and those to delete.</summary>
public sealed record DeskPrune(IReadOnlyList<SavedDesign> Kept, IReadOnlyList<SavedDesign> Removed);

/// <summary>The rule for how many saved designs are kept.</summary>
public static class DeskDesignLibrary
{
    /// <summary>Ten designs are kept.</summary>
    public const int Keep = 10;

    /// <summary>
    /// While there are more than <paramref name="keep"/> designs, the oldest design that is not pinned goes (by when it was
    /// applied; the id breaks a tie). A pinned design is never pruned, so a library of pins alone may hold more than
    /// <paramref name="keep"/>. The kept designs come back newest first.
    /// </summary>
    public static DeskPrune Prune(IEnumerable<SavedDesign> designs, int keep = Keep)
    {
        var all = designs.OrderByDescending(d => d.AppliedAt).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        var removed = new List<SavedDesign>();
        while (all.Count > keep)
        {
            int oldest = all.FindLastIndex(d => !d.Pinned);
            if (oldest < 0) break;
            removed.Add(all[oldest]);
            all.RemoveAt(oldest);
        }
        return new DeskPrune(all, removed);
    }

    /// <summary>The library with <paramref name="added"/> in it (replacing a design of the same id), pruned.</summary>
    public static DeskPrune Add(IEnumerable<SavedDesign> existing, SavedDesign added, int keep = Keep) =>
        Prune(existing.Where(d => d.Id != added.Id).Append(added), keep);
}
