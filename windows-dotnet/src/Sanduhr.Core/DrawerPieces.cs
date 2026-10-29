using System.Text;

namespace Sanduhr.Core;

/// <summary>A drawer's layout word (5.0 drawers spec, section 2): <c>drawer.&lt;id&gt;</c> in the widget slot, so
/// <c>drawer.screenshots:tl:1</c> keeps the grammar's widget:anchor:scale:tilt order. Drawer ids hold no colon, at-sign or space.</summary>
public static class DrawerPieces
{
    public const string Prefix = "drawer.";

    public static bool IsWord(string? widget) =>
        widget is not null && widget.StartsWith(Prefix, StringComparison.Ordinal) && DrawerIds.IsValid(widget[Prefix.Length..]);

    public static string Word(string id) => Prefix + id;

    public static string? IdOf(string widget) => IsWord(widget) ? widget[Prefix.Length..] : null;
}

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
