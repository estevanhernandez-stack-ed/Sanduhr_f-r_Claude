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
