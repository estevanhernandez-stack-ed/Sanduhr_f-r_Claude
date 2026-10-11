using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>
/// Finds the Claude Code homes on this machine: <c>~/.claude</c>, every
/// <c>~/.claude-*</c> folder (a <c>CLAUDE_CONFIG_DIR</c> home such as
/// <c>~/.claude-work</c>), and the folder <c>CLAUDE_CONFIG_DIR</c> names when it
/// lives in the profile. A folder counts only when it looks like a home: it holds
/// <c>projects/</c> or its <c>.claude.json</c>. Port of the discovery half of
/// <c>mac/.../Services/ClaudeCodeFolders.swift</c>.
///
/// <para><b>Placement of <c>.claude.json</c>.</b> Claude Code keeps it inside the
/// home when the home is selected through <c>CLAUDE_CONFIG_DIR</c>, and beside the
/// default <c>~/.claude</c> (at <c>~/.claude.json</c>) when the variable is unset.
/// A launch with <c>CLAUDE_CONFIG_DIR</c> naming <c>~/.claude</c> itself uses
/// <c>~/.claude/.claude.json</c> and creates a fresh one (no sign-in) when it is
/// missing, which is how a stub appears beside the live file. So for
/// <c>~/.claude</c> with both files, the signed-in one wins, then the strictly newer
/// inner one, and a tie goes to <c>~/.claude.json</c>.</para>
///
/// <para><b>What is read.</b> Only whether <c>oauthAccount</c> is an object.
/// Nothing inside it is read, kept or logged.</para>
///
/// <para>Every IO failure reads as "not a home"; nothing here throws.</para>
/// </summary>
public static class ClaudeCodeHomes
{
    /// <summary>The default home's folder name.</summary>
    public const string DefaultName = ".claude";

    private const string Prefix = ".claude-";
    private const string ConfigName = ".claude.json";
    private const string BackupMarker = ".config-backup-";
    private const string BackupPrefix = ".claude-backup-";

    /// <summary><c>.claude</c>, or <c>.claude-</c> followed by at least one character.</summary>
    public static bool IsCandidateName(string name)
        => name == DefaultName
           || (name.StartsWith(Prefix, StringComparison.Ordinal) && name.Length > Prefix.Length);

    /// <summary>A copy a config tool left beside a home (<c>.claude-work.config-backup-20261001</c>,
    /// or <c>.claude-backup-20261001</c> from a seat migration): it looks like a home but
    /// nothing loads it.</summary>
    public static bool IsBackup(string name)
        => name.Contains(BackupMarker, StringComparison.Ordinal)
           || name.StartsWith(BackupPrefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="dir"/> is a directory holding a <c>projects</c>
    /// folder, or whose <c>.claude.json</c> (by the placement rule) exists.</summary>
    public static bool LooksLikeHome(string dir, string profileDir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return false;
            if (Directory.Exists(Path.Combine(dir, "projects")))
                return true;
            return File.Exists(ConfigFile(dir, profileDir));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The <c>.claude.json</c> Claude Code reads for the home at
    /// <paramref name="dir"/>, by the placement rule in the type summary. Any home but
    /// <c>~/.claude</c> keeps it inside the home.</summary>
    public static string ConfigFile(string dir, string profileDir)
    {
        var inner = Path.Combine(dir, ConfigName);
        try
        {
            if (!SamePath(dir, Path.Combine(profileDir, DefaultName)))
                return inner;

            var outer = Path.Combine(profileDir, ConfigName);
            var outerExists = File.Exists(outer);
            var innerExists = File.Exists(inner);

            bool innerWins;
            if (!outerExists || !innerExists)
            {
                innerWins = innerExists && !outerExists;
            }
            else
            {
                var outerSigned = IsSignedIn(outer);
                var innerSigned = IsSignedIn(inner);
                if (outerSigned != innerSigned)
                    innerWins = innerSigned;
                else
                    innerWins = File.GetLastWriteTimeUtc(inner) > File.GetLastWriteTimeUtc(outer);
            }
            return innerWins ? inner : outer;
        }
        catch
        {
            return inner;
        }
    }

    /// <summary>The home folder names under <paramref name="profileDir"/>: <c>.claude</c>
    /// first, then the <c>.claude-*</c> folders in natural order (never a backup copy),
    /// then the <c>CLAUDE_CONFIG_DIR</c> folder when it sits in the profile, is not already
    /// listed and looks like a home. Basenames only.</summary>
    public static IReadOnlyList<string> Discover(string profileDir, string? claudeConfigDir)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(profileDir))
            return found;

        try
        {
            var names = new List<string>();
            foreach (var path in Directory.EnumerateDirectories(profileDir))
            {
                var name = Path.GetFileName(path);
                if (IsCandidateName(name) && !IsBackup(name))
                    names.Add(name);
            }
            names.Sort(CompareNames);

            foreach (var name in names)
                if (LooksLikeHome(Path.Combine(profileDir, name), profileDir))
                    found.Add(name);
        }
        catch
        {
            // Unreadable profile: keep whatever was found.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(claudeConfigDir))
            {
                var expanded = Environment.ExpandEnvironmentVariables(claudeConfigDir.Trim());
                if (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal)
                    || expanded.StartsWith("~\\", StringComparison.Ordinal))
                    expanded = Path.Combine(profileDir, expanded.Substring(1).TrimStart('/', '\\'));

                var full = TrimEnd(Path.GetFullPath(expanded));
                var parent = Path.GetDirectoryName(full);
                var name = Path.GetFileName(full);
                if (parent is not null && !string.IsNullOrEmpty(name)
                    && SamePath(parent, profileDir)
                    && !found.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && LooksLikeHome(full, profileDir))
                    found.Add(name);
            }
        }
        catch
        {
            // Malformed CLAUDE_CONFIG_DIR: skip it.
        }
        return found;
    }

    /// <summary>The file parses as a JSON object with an object-valued <c>oauthAccount</c>.</summary>
    private static bool IsSignedIn(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("oauthAccount", out var acct)
                   && acct.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static string TrimEnd(string p)
    {
        var root = Path.GetPathRoot(p) ?? "";
        return p.Length > root.Length ? p.TrimEnd('\\', '/') : p;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(TrimEnd(Path.GetFullPath(a)), TrimEnd(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary><c>.claude</c> first, then natural order.</summary>
    private static int CompareNames(string a, string b)
    {
        if (a == DefaultName) return b == DefaultName ? 0 : -1;
        if (b == DefaultName) return 1;
        return NaturalCompare(a, b);
    }

    /// <summary>Digit runs compare as numbers, everything else case-insensitively.</summary>
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var ra = a.AsSpan(si, i - si).TrimStart('0');
                var rb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (ra.Length != rb.Length) return ra.Length < rb.Length ? -1 : 1;
                var c = ra.CompareTo(rb, StringComparison.Ordinal);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        var rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }
}
