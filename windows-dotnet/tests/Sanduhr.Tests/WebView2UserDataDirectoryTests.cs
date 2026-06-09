using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for the per-capture WebView2 user-data folder allocator (ported
/// from ROROROblox). Proves a fresh dir per sign-in + best-effort sweep of the
/// rest — the mechanism that stops a second "Add account" from re-reading the
/// first account's session state.
/// </summary>
public class WebView2UserDataDirectoryTests
{
    [Fact]
    public void AllocateNew_creates_a_fresh_subdir_under_root()
    {
        using var temp = new TempDir();
        var root = Path.Combine(temp.Path, "webview2");
        var dir = new WebView2UserDataDirectory(root);

        var allocated = dir.AllocateNew();

        Assert.True(Directory.Exists(allocated));
        Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(Directory.GetParent(allocated)!.FullName));
    }

    [Fact]
    public void AllocateNew_returns_a_distinct_dir_each_call()
    {
        using var temp = new TempDir();
        var dir = new WebView2UserDataDirectory(Path.Combine(temp.Path, "webview2"));

        var a = dir.AllocateNew();
        var b = dir.AllocateNew();

        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
    }

    [Fact]
    public void SweepStale_deletes_siblings_but_keeps_the_excluded_dir()
    {
        using var temp = new TempDir();
        var dir = new WebView2UserDataDirectory(Path.Combine(temp.Path, "webview2"));

        var stale1 = dir.AllocateNew();
        var stale2 = dir.AllocateNew();
        var current = dir.AllocateNew();

        dir.SweepStale(exclude: current);

        Assert.False(Directory.Exists(stale1));
        Assert.False(Directory.Exists(stale2));
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public void SweepStale_with_no_exclude_clears_all_siblings()
    {
        using var temp = new TempDir();
        var dir = new WebView2UserDataDirectory(Path.Combine(temp.Path, "webview2"));
        dir.AllocateNew();
        dir.AllocateNew();

        dir.SweepStale();

        Assert.Empty(Directory.EnumerateDirectories(dir.Root));
    }

    [Fact]
    public void SweepStale_is_a_noop_when_root_is_absent()
    {
        using var temp = new TempDir();
        var dir = new WebView2UserDataDirectory(Path.Combine(temp.Path, "does-not-exist"));

        // Must not throw.
        dir.SweepStale();
        dir.SweepStale(exclude: Path.Combine(temp.Path, "whatever"));
    }
}
