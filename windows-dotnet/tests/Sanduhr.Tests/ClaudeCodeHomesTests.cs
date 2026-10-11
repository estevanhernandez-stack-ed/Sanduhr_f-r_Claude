using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Home discovery, ported from the Mac ClaudeCodeFoldersTests. Every case builds a
/// throwaway profile folder.</summary>
public class ClaudeCodeHomesTests
{
    private const string Signed = "{\"oauthAccount\":{\"organizationUuid\":\"x\"}}";
    private const string Unsigned = "{\"theme\":\"dark\"}";

    private static string Dir(string profile, string name)
    {
        var d = Path.Combine(profile, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void ProjectsHome(string profile, string name)
        => Directory.CreateDirectory(Path.Combine(profile, name, "projects"));

    private static string Write(string path, string text, DateTime? utc = null)
    {
        File.WriteAllText(path, text);
        if (utc is not null) File.SetLastWriteTimeUtc(path, utc.Value);
        return path;
    }

    [Fact]
    public void Candidate_names_and_backups()
    {
        Assert.True(ClaudeCodeHomes.IsCandidateName(".claude"));
        Assert.True(ClaudeCodeHomes.IsCandidateName(".claude-work"));
        Assert.False(ClaudeCodeHomes.IsCandidateName(".claude-"));
        Assert.False(ClaudeCodeHomes.IsCandidateName(".claudette"));
        Assert.False(ClaudeCodeHomes.IsCandidateName("claude"));
        Assert.True(ClaudeCodeHomes.IsBackup(".claude.config-backup-1"));
        Assert.True(ClaudeCodeHomes.IsBackup(".claude-work.config-backup-2026"));
        Assert.True(ClaudeCodeHomes.IsBackup(".claude-backup-2"));
        Assert.False(ClaudeCodeHomes.IsBackup(".claude-work"));
        Assert.False(ClaudeCodeHomes.IsBackup(".claude"));
    }

    [Fact]
    public void Finds_projects_only_and_config_only_homes()
    {
        using var t = new TempDir();
        ProjectsHome(t.Path, ".claude-a");
        Write(Path.Combine(Dir(t.Path, ".claude-b"), ".claude.json"), Unsigned);
        Assert.Equal(new[] { ".claude-a", ".claude-b" }, ClaudeCodeHomes.Discover(t.Path, null));
    }

    [Fact]
    public void Decoys_and_backups_are_dropped()
    {
        using var t = new TempDir();
        ProjectsHome(t.Path, ".claude-real");
        ProjectsHome(t.Path, ".claude-");
        ProjectsHome(t.Path, ".claudette");
        File.WriteAllText(Path.Combine(t.Path, ".claude-file"), "x");
        Directory.CreateDirectory(Path.Combine(t.Path, ".claude-notdir"));
        File.WriteAllText(Path.Combine(t.Path, ".claude-notdir", "projects"), "x");
        Directory.CreateDirectory(Path.Combine(t.Path, ".claude-empty"));
        ProjectsHome(t.Path, ".claude.config-backup-1");
        ProjectsHome(t.Path, ".claude-backup-2");
        Assert.Equal(new[] { ".claude-real" }, ClaudeCodeHomes.Discover(t.Path, null));
    }

    [Fact]
    public void Default_home_first_then_natural_order()
    {
        using var t = new TempDir();
        foreach (var n in new[] { ".claude-a10", ".claude-a2", ".claude-B", ".claude" })
            ProjectsHome(t.Path, n);
        Assert.Equal(new[] { ".claude", ".claude-a2", ".claude-a10", ".claude-B" },
            ClaudeCodeHomes.Discover(t.Path, null));
    }

    [Fact]
    public void Outer_claude_json_belongs_only_to_the_default_home()
    {
        using var t = new TempDir();
        Write(Path.Combine(t.Path, ".claude.json"), Signed);
        Dir(t.Path, ".claude");
        Dir(t.Path, ".claude-work");
        Assert.True(ClaudeCodeHomes.LooksLikeHome(Path.Combine(t.Path, ".claude"), t.Path));
        Assert.False(ClaudeCodeHomes.LooksLikeHome(Path.Combine(t.Path, ".claude-work"), t.Path));
        Assert.Equal(new[] { ".claude" }, ClaudeCodeHomes.Discover(t.Path, null));
    }

    [Fact]
    public void Placement_inner_only()
    {
        using var t = new TempDir();
        var inner = Write(Path.Combine(Dir(t.Path, ".claude"), ".claude.json"), Unsigned);
        Assert.Equal(inner, ClaudeCodeHomes.ConfigFile(Path.Combine(t.Path, ".claude"), t.Path));
    }

    [Fact]
    public void Placement_non_default_home_uses_inner()
    {
        using var t = new TempDir();
        Write(Path.Combine(t.Path, ".claude.json"), Signed);
        var dir = Dir(t.Path, ".claude-work");
        Assert.Equal(Path.Combine(dir, ".claude.json"), ClaudeCodeHomes.ConfigFile(dir, t.Path));
    }

    [Fact]
    public void Placement_both_files_signed_in_one_wins()
    {
        using var t = new TempDir();
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = old.AddDays(30);
        var dir = Dir(t.Path, ".claude");

        var outer = Write(Path.Combine(t.Path, ".claude.json"), Signed, old);
        var inner = Write(Path.Combine(dir, ".claude.json"), Unsigned, now);
        Assert.Equal(outer, ClaudeCodeHomes.ConfigFile(dir, t.Path));

        Write(outer, Unsigned, old);
        Write(inner, Signed, old);
        Assert.Equal(inner, ClaudeCodeHomes.ConfigFile(dir, t.Path));
    }

    [Fact]
    public void Placement_same_status_newer_inner_wins_strictly_tie_goes_outer()
    {
        using var t = new TempDir();
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dir = Dir(t.Path, ".claude");
        var outer = Write(Path.Combine(t.Path, ".claude.json"), Signed, old);
        var inner = Write(Path.Combine(dir, ".claude.json"), Signed, old.AddMinutes(1));
        Assert.Equal(inner, ClaudeCodeHomes.ConfigFile(dir, t.Path));

        File.SetLastWriteTimeUtc(inner, old);
        Assert.Equal(outer, ClaudeCodeHomes.ConfigFile(dir, t.Path));

        File.SetLastWriteTimeUtc(inner, old.AddMinutes(-1));
        Assert.Equal(outer, ClaudeCodeHomes.ConfigFile(dir, t.Path));
    }

    [Fact]
    public void Signed_in_needs_an_object_valued_oauthAccount()
    {
        using var t = new TempDir();
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dir = Dir(t.Path, ".claude");
        var outer = Write(Path.Combine(t.Path, ".claude.json"), "{\"oauthAccount\":\"nope\"}", old);
        var inner = Write(Path.Combine(dir, ".claude.json"), "not json", old.AddDays(1));
        // Neither is signed in, so the newer inner wins.
        Assert.Equal(inner, ClaudeCodeHomes.ConfigFile(dir, t.Path));
        Write(inner, Signed, old);
        Assert.Equal(inner, ClaudeCodeHomes.ConfigFile(dir, t.Path));
        Assert.NotEqual(outer, ClaudeCodeHomes.ConfigFile(dir, t.Path));
    }

    [Fact]
    public void ClaudeConfigDir_in_profile_is_appended_not_duplicated()
    {
        using var t = new TempDir();
        ProjectsHome(t.Path, ".claude");
        ProjectsHome(t.Path, "seat-work");
        Assert.Equal(new[] { ".claude", "seat-work" },
            ClaudeCodeHomes.Discover(t.Path, Path.Combine(t.Path, "seat-work")));
        Assert.Equal(new[] { ".claude", "seat-work" },
            ClaudeCodeHomes.Discover(t.Path, Path.Combine(t.Path, "seat-work") + Path.DirectorySeparatorChar));

        // Already listed by name: no duplicate.
        Assert.Equal(new[] { ".claude" }, ClaudeCodeHomes.Discover(t.Path, Path.Combine(t.Path, ".claude")));

        // Not a home: skipped.
        Dir(t.Path, "plain");
        Assert.Equal(new[] { ".claude" },
            ClaudeCodeHomes.Discover(t.Path, Path.Combine(t.Path, "plain")));
    }

    [Fact]
    public void ClaudeConfigDir_outside_the_profile_is_skipped()
    {
        using var t = new TempDir();
        using var other = new TempDir();
        ProjectsHome(t.Path, ".claude");
        ProjectsHome(other.Path, "elsewhere");
        Assert.Equal(new[] { ".claude" },
            ClaudeCodeHomes.Discover(t.Path, Path.Combine(other.Path, "elsewhere")));
    }

    [Fact]
    public void Missing_profile_and_garbage_config_dir_never_throw()
    {
        using var t = new TempDir();
        Assert.Empty(ClaudeCodeHomes.Discover(Path.Combine(t.Path, "nope"), "\0bad"));
        Assert.Empty(ClaudeCodeHomes.Discover("", null));
        Assert.False(ClaudeCodeHomes.LooksLikeHome("\0", t.Path));
    }

    [Fact]
    public void CcLogReader_provider_overload_honors_the_provider()
    {
        using var t = new TempDir();
        Dir(t.Path, ".claude");
        Dir(t.Path, ".claude-personal");
        Dir(t.Path, ".claude-work");
        var names = new List<string> { ".claude-work" };
        var reader = new CcLogReader(t.Path, () => names);
        Assert.Equal(new[] { Path.Combine(t.Path, ".claude-work") }, reader.SearchRoots());

        names.Add(".claude-missing");
        names.Add(".claude");
        Assert.Equal(new[] { Path.Combine(t.Path, ".claude-work"), Path.Combine(t.Path, ".claude") },
            reader.SearchRoots());
    }

    [Fact]
    public void CcLogReader_one_arg_constructor_still_returns_the_two_fixed_names()
    {
        using var t = new TempDir();
        Dir(t.Path, ".claude");
        Dir(t.Path, ".claude-personal");
        Dir(t.Path, ".claude-work");
        Assert.Equal(new[] { Path.Combine(t.Path, ".claude"), Path.Combine(t.Path, ".claude-personal") },
            new CcLogReader(t.Path).SearchRoots());
    }
}
