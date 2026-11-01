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

/// <summary>One entry in a desktop or drawer listing, as the App reads it from disk. <see cref="Hidden"/> is true for a hidden or
/// system item, which no rule moves.</summary>
public sealed record DesktopItem(string Path, bool IsFolder, DateTime WrittenUtc, bool Hidden = false);

/// <summary>Drawer names, which are also their folder names, and the Explorer-style name a clash gets.</summary>
public static class DrawerNames
{
    public const int MaxLength = 40;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "CONIN$", "CONOUT$",
    };

    /// <summary>The extensions Explorer hides on a desktop whatever the folder options say: shortcuts, internet shortcuts and PIF files.</summary>
    private static readonly string[] HiddenExtensions = [".lnk", ".url", ".pif"];

    /// <summary>A file's name as Explorer draws it: without a ".lnk", ".url" or ".pif" ending. Display only: the file on disk and every
    /// path keep the extension. A name that is nothing but the extension (".lnk") is left as it is.</summary>
    public static string Display(string fileName)
    {
        var hidden = HiddenExtension(fileName);
        return hidden.Length > 0 && fileName.Length > hidden.Length ? fileName[..^hidden.Length] : fileName;
    }

    /// <summary>The hidden extension <paramref name="fileName"/> ends with, as written on disk (".lnk"), or empty when it shows its own.</summary>
    public static string HiddenExtension(string fileName)
    {
        foreach (var ext in HiddenExtensions)
            if (fileName.Length > ext.Length && fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return fileName[^ext.Length..];
        return "";
    }

    /// <summary>The name a rename box's text means for a file whose name was drawn without its hidden extension: the extension goes back on
    /// unless the text already ends with it, so renaming a shortcut never turns it into a file Windows no longer opens as one.</summary>
    public static string WithHiddenExtension(string originalName, string typed)
    {
        var ext = HiddenExtension(originalName);
        return ext.Length == 0 || string.IsNullOrWhiteSpace(typed) || typed.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? typed : typed + ext;
    }

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
    /// as Explorer names a clash. A folder, and a dotfile such as ".gitignore", is numbered after its whole name. Never returns a taken path,
    /// so nothing is overwritten; gives up after 9,999 tries rather than loop on a <paramref name="taken"/> that never says no.</summary>
    public static string Free(string dir, string name, bool isFolder, Func<string, bool> taken)
    {
        var first = Path.Combine(dir, name);
        if (!taken(first)) return first;
        var ext = isFolder ? "" : Path.GetExtension(name);
        var stem = name[..^ext.Length];
        if (stem.Length == 0) { stem = name; ext = ""; }
        for (var n = 2; n < 10_000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!taken(candidate)) return candidate;
        }
        throw new InvalidOperationException("No free name for " + name + ".");
    }
}

/// <summary>Why a file stayed where it was, as the end of a sentence that starts with its name: "report.docx is open in Word."</summary>
public static class DrawerSkip
{
    public const string MovedSince = "was moved since.";
    public const string ChangedSince = "changed since.";
    public const string Outside = "is outside the Desktop and the drawers.";
    public const string CouldNotMove = "could not be moved.";
    public const string NotInTime = "was not moved in time.";
    public const string Shared = "is shared with every user and needs an administrator to move.";

    public static string OpenIn(string app) => $"is open in {app}.";
}

/// <summary>A file a run left where it was: its name (never its path) and why.</summary>
public sealed record DrawerSkipped(string Name, string Reason);
