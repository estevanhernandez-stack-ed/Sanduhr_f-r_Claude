using System.Text;

namespace Sanduhr.Core;

/// <summary>Who made a move plan (5.0 drawers spec, section 1): the built-in rules, an agent through the MCP, or the user's own hand.</summary>
public enum DrawerPlanSource { Rules, Agent, User }

/// <summary>A drawer: its id (the layout word's tail), the name it shows, the folder it shows, and whether Sanduhr owns that folder
/// (one it made under the Drawers root, which turning drawers off empties back onto the Desktop) or only shows it (a view).</summary>
public sealed record DrawerDef(string Id, string Name, string Home, bool Owned);

/// <summary>One file or folder going into a drawer.</summary>
public sealed record DrawerMove(string Source, string DrawerId);

/// <summary>A move plan: the drawers it needs and the moves, with who made it. Moves are the only action a plan can hold.</summary>
public sealed record DrawerPlan(DrawerPlanSource Source, string? Agent, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves);

/// <summary>One entry in a desktop or drawer listing, as the App reads it from disk.</summary>
public sealed record DesktopItem(string Path, bool IsFolder, DateTime WrittenUtc);

/// <summary>Drawer ids: lowercase letters, digits and hyphens, a letter or digit first, at most 40. No colon, at-sign, dot or space,
/// so <c>drawer.&lt;id&gt;</c> is always one safe layout word.</summary>
public static class DrawerIds
{
    public const int MaxLength = 40;

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxLength || !IsSlugChar(id[0]) || id[0] == '-') return false;
        foreach (var c in id)
            if (!IsSlugChar(c)) return false;
        return true;
    }

    /// <summary>The id a name gets: lowercased, every run of other characters one hyphen, trimmed of hyphens, cut to 40.
    /// A name with no ASCII letter or digit gets "drawer".</summary>
    public static string From(string name)
    {
        var sb = new StringBuilder();
        foreach (var raw in name.ToLowerInvariant())
        {
            if (raw is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(raw);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var id = sb.ToString().Trim('-');
        if (id.Length > MaxLength) id = id[..MaxLength].TrimEnd('-');
        return id.Length == 0 ? "drawer" : id;
    }

    private static bool IsSlugChar(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
}

/// <summary>Drawer names, which are also their folder names, and the Explorer-style name a clash gets.</summary>
public static class DrawerNames
{
    public const int MaxLength = 40;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A name Windows takes as a folder name: 1 to 40 characters, none of the forbidden ones, not "." or "..", no trailing dot
    /// or space, and not a device name (CON, NUL, COM1 and the rest, with or without an extension).</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength) return false;
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var stem = name.Split('.')[0].TrimEnd();
        return !Reserved.Contains(stem);
    }

    /// <summary>The path <paramref name="name"/> can take in <paramref name="dir"/>: itself when free, otherwise "name (2).ext", "(3)" and on,
    /// as Explorer names a clash. A folder is numbered after its whole name. Never returns a taken path, so nothing is overwritten.</summary>
    public static string Free(string dir, string name, bool isFolder, Func<string, bool> taken)
    {
        var first = Path.Combine(dir, name);
        if (!taken(first)) return first;
        var stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isFolder ? "" : Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!taken(candidate)) return candidate;
        }
    }
}
