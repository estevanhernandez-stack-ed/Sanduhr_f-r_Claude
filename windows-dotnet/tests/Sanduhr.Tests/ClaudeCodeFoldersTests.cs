using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The folder list behind the Claude Code page: discovered first, added second, never twice.</summary>
public class ClaudeCodeFoldersTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "sanduhr-ccf-" + Guid.NewGuid().ToString("N"));

    public ClaudeCodeFoldersTests()
    {
        Directory.CreateDirectory(Path.Combine(_profile, ".claude", "projects"));
        Directory.CreateDirectory(Path.Combine(_profile, ".claude-work", "projects"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_profile, true); } catch { }
    }

    [Fact]
    public void Discovered_folders_come_first_then_added_ones_in_order()
    {
        var extra = Path.Combine(_profile, "elsewhere", "claude-home");
        var list = ClaudeCodeFolders.Build(_profile, null, [extra]);
        Assert.Equal([".claude", ".claude-work", "claude-home"], list.Select(f => f.Name));
        Assert.Equal([true, true, false], list.Select(f => f.Discovered));
    }

    [Fact]
    public void An_added_folder_that_is_discovered_or_repeated_is_dropped()
    {
        var dup = Path.Combine(_profile, ".claude") + "\\";
        var added = Path.Combine(_profile, "x");
        var list = ClaudeCodeFolders.Build(_profile, null, [dup.ToUpperInvariant(), added, added + "\\", ""]);
        Assert.Equal(3, list.Count);
        Assert.Single(list, f => !f.Discovered);
    }

    [Fact]
    public void Display_writes_the_profile_as_a_tilde()
    {
        Assert.Equal("~\\.claude", ClaudeCodeFolders.Display(Path.Combine(_profile, ".claude"), _profile));
        Assert.Equal("~", ClaudeCodeFolders.Display(_profile, _profile));
        var outside = Path.Combine(Path.GetTempPath(), "other-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(outside, ClaudeCodeFolders.Display(outside, _profile));
    }

    [Fact]
    public void A_snapshot_consumer_is_the_statusline_or_the_meters_mod_in_any_folder()
    {
        var folders = ClaudeCodeFolders.Build(_profile, null, []);
        var meters = folders[1].Path;
        Assert.False(ClaudeCodeFolders.AnySnapshotConsumer(folders, _ => false, _ => false));
        Assert.True(ClaudeCodeFolders.AnySnapshotConsumer(folders, p => p == folders[0].Path, _ => false));
        Assert.True(ClaudeCodeFolders.AnySnapshotConsumer(folders, _ => false, p => p == meters));
    }
}
