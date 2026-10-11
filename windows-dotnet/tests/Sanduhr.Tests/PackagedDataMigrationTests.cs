using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// The one-shot Store migration: files an older Store build wrote while MSIX AppData
/// virtualization was on live in the package's LocalCache. With virtualization off the app
/// reads the real %APPDATA% / %LOCALAPPDATA%, so those files are copied across once. Every
/// root is injected; nothing here touches the real AppData or a real package.
/// </summary>
public sealed class PackagedDataMigrationTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly List<string> _log = new();

    private string LocalCache => Path.Combine(_tmp.Path, "LocalCache");
    private string CacheRoaming => Path.Combine(LocalCache, "Roaming", "Sanduhr");
    private string CacheLocal => Path.Combine(LocalCache, "Local", "Sanduhr");
    private string RoamingBase => Path.Combine(_tmp.Path, "Roaming");
    private string LocalBase => Path.Combine(_tmp.Path, "Local");
    private string RealRoaming => Path.Combine(RoamingBase, "Sanduhr");
    private string RealLocal => Path.Combine(LocalBase, "Sanduhr");
    private string Marker => Path.Combine(RealRoaming, PackagedDataMigration.MarkerFileName);

    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _tmp.Dispose();

    private PackagedDataMigration Create(int maxAttempts = PackagedDataMigration.DefaultMaxAttempts)
        => new(LocalCache, RoamingBase, LocalBase, _log.Add, maxAttempts);

    private static void Put(string path, string content, DateTime mtimeUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
    }

    [Fact]
    public void Copies_a_file_missing_at_the_real_path()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);
        Put(Path.Combine(CacheLocal, "vault", "home-a", "2026-09.json"), "vault", T0);

        var result = Create().Run();

        Assert.Equal("cached", File.ReadAllText(Path.Combine(RealRoaming, "settings.json")));
        Assert.Equal("vault", File.ReadAllText(Path.Combine(RealLocal, "vault", "home-a", "2026-09.json")));
        Assert.Equal(T0, File.GetLastWriteTimeUtc(Path.Combine(RealRoaming, "settings.json")));
        Assert.Equal(2, result.Copied);
        Assert.True(result.Completed);
    }

    [Fact]
    public void Overwrites_an_older_real_file()
    {
        Put(Path.Combine(CacheRoaming, "history.a.json"), "cached-newer", T0.AddHours(1));
        Put(Path.Combine(RealRoaming, "history.a.json"), "real-older", T0);

        var result = Create().Run();

        Assert.Equal("cached-newer", File.ReadAllText(Path.Combine(RealRoaming, "history.a.json")));
        Assert.Equal(1, result.Copied);
    }

    [Theory]
    [InlineData(1)]  // real file newer
    [InlineData(0)]  // same timestamp
    public void Keeps_a_real_file_that_is_newer_or_the_same_age(int realOffsetHours)
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);
        Put(Path.Combine(RealRoaming, "settings.json"), "real", T0.AddHours(realOffsetHours));

        var result = Create().Run();

        Assert.Equal("real", File.ReadAllText(Path.Combine(RealRoaming, "settings.json")));
        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.UpToDate);
        Assert.True(result.Completed);
    }

    [Fact]
    public void A_locked_file_fails_alone_and_the_next_launch_retries_it()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);
        Put(Path.Combine(CacheRoaming, "history.a.json"), "hist", T0);

        MigrationResult first;
        using (new FileStream(Path.Combine(CacheRoaming, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            first = Create().Run();

        Assert.Equal(1, first.Failed);
        Assert.Equal(1, first.Copied);
        Assert.False(first.Completed);
        Assert.False(File.Exists(Path.Combine(RealRoaming, "settings.json")));
        Assert.Contains(_log, l => l.Contains("IOException"));

        var second = Create().Run();

        Assert.True(second.Completed);
        Assert.Equal("cached", File.ReadAllText(Path.Combine(RealRoaming, "settings.json")));
        Assert.Equal("done", File.ReadAllText(Marker).Trim());
    }

    [Fact]
    public void Gives_up_after_the_attempt_cap_so_a_stuck_file_cannot_retry_forever()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);
        using var _ = new FileStream(Path.Combine(CacheRoaming, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(Create(maxAttempts: 2).Run().ShortCircuited);
        Assert.False(Create(maxAttempts: 2).Run().ShortCircuited);
        Assert.True(Create(maxAttempts: 2).Run().ShortCircuited);
    }

    [Fact]
    public void A_done_marker_short_circuits_without_touching_files()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);
        Put(Marker, "done", T0);

        var result = Create().Run();

        Assert.True(result.ShortCircuited);
        Assert.False(File.Exists(Path.Combine(RealRoaming, "settings.json")));
    }

    [Fact]
    public void Writes_the_marker_on_success_and_leaves_the_originals_as_a_rollback()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "cached", T0);

        Create().Run();

        Assert.Equal("done", File.ReadAllText(Marker).Trim());
        Assert.True(File.Exists(Path.Combine(CacheRoaming, "settings.json")));
        Assert.True(Create().Run().ShortCircuited);
    }

    [Fact]
    public void Nothing_to_migrate_still_marks_done()
    {
        var result = Create().Run();

        Assert.True(result.Completed);
        Assert.Equal(0, result.Copied);
        Assert.Equal("done", File.ReadAllText(Marker).Trim());
    }

    [Fact]
    public void Skips_the_bin_and_mcp_launcher_pair()
    {
        Put(Path.Combine(CacheRoaming, "bin", "sanduhr-mcp.cmd"), "launcher", T0);
        Put(Path.Combine(CacheRoaming, "mcp", "v3.4.2", "sanduhr-mcp.exe"), "exe", T0);
        Put(Path.Combine(CacheRoaming, "themes", "mine.json"), "{}", T0);

        var result = Create().Run();

        Assert.False(Directory.Exists(Path.Combine(RealRoaming, "bin")));
        Assert.False(Directory.Exists(Path.Combine(RealRoaming, "mcp")));
        Assert.True(File.Exists(Path.Combine(RealRoaming, "themes", "mine.json")));
        Assert.Equal(1, result.Copied);
        Assert.True(result.Completed);
    }

    [Theory]
    [InlineData("webview2")]
    [InlineData("webview2-fetch")]
    public void Copies_a_WebView2_profile_whole_when_the_real_one_is_absent(string profile)
    {
        Put(Path.Combine(CacheRoaming, profile, "EBWebView", "Local State"), "state", T0);
        Put(Path.Combine(CacheRoaming, profile, "EBWebView", "Default", "Cookies"), "cookies", T0);

        var result = Create().Run();

        Assert.Equal("cookies", File.ReadAllText(Path.Combine(RealRoaming, profile, "EBWebView", "Default", "Cookies")));
        Assert.False(Directory.Exists(Path.Combine(RealRoaming, profile + PackagedDataMigration.StagingSuffix)));
        Assert.True(result.Completed);
    }

    [Fact]
    public void Never_merges_into_an_existing_WebView2_profile()
    {
        Put(Path.Combine(CacheRoaming, "webview2", "EBWebView", "Default", "Cookies"), "cached", T0.AddHours(1));
        Put(Path.Combine(RealRoaming, "webview2", "EBWebView", "Default", "Cookies"), "real", T0);

        var result = Create().Run();

        Assert.Equal("real", File.ReadAllText(Path.Combine(RealRoaming, "webview2", "EBWebView", "Default", "Cookies")));
        Assert.Contains(_log, l => l.Contains("webview2") && l.Contains("skipped"));
        Assert.True(result.Completed);
    }

    [Fact]
    public void A_locked_WebView2_profile_is_skipped_with_a_log_line_and_leaves_no_half_copy()
    {
        var cookies = Path.Combine(CacheRoaming, "webview2", "EBWebView", "Default", "Cookies");
        Put(Path.Combine(CacheRoaming, "webview2", "EBWebView", "Local State"), "state", T0);
        Put(cookies, "cached", T0);

        MigrationResult result;
        using (new FileStream(cookies, FileMode.Open, FileAccess.Read, FileShare.None))
            result = Create().Run();

        Assert.False(Directory.Exists(Path.Combine(RealRoaming, "webview2")));
        Assert.False(Directory.Exists(Path.Combine(RealRoaming, "webview2" + PackagedDataMigration.StagingSuffix)));
        Assert.Contains(_log, l => l.Contains("webview2") && l.Contains("skipped"));
        Assert.True(result.Completed);
    }

    [Fact]
    public void Writes_only_inside_the_two_Sanduhr_folders()
    {
        // Velopack installs to %LOCALAPPDATA%\626Labs.Sanduhr; the migration must never touch
        // anything under the AppData bases except the two Sanduhr folders.
        Put(Path.Combine(CacheRoaming, "settings.json"), "s", T0);
        Put(Path.Combine(CacheLocal, "vault", "x.json"), "v", T0);
        Put(Path.Combine(LocalCache, "Local", "626Labs.Sanduhr", "current", "Sanduhr.exe"), "x", T0);
        Put(Path.Combine(LocalCache, "Roaming", "Other", "f.txt"), "x", T0);

        Create().Run();

        Assert.Equal(new[] { "Sanduhr" }, Directory.GetFileSystemEntries(RoamingBase).Select(Path.GetFileName));
        Assert.Equal(new[] { "Sanduhr" }, Directory.GetFileSystemEntries(LocalBase).Select(Path.GetFileName));
    }

    [Fact]
    public void Never_throws_even_when_a_root_is_unusable()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "s", T0);
        // The roaming base is a FILE, so creating the Sanduhr folder under it must fail.
        Directory.CreateDirectory(_tmp.Path);
        File.WriteAllText(RoamingBase, "not a folder");

        var result = Create().Run();

        Assert.False(result.Completed);
        Assert.NotEmpty(_log);
    }

    [Fact]
    public void Log_lines_carry_counts_and_exception_types_never_paths()
    {
        Put(Path.Combine(CacheRoaming, "settings.json"), "s", T0);
        Put(Path.Combine(CacheLocal, "vault", "secret-project", "x.json"), "v", T0);
        using (new FileStream(Path.Combine(CacheRoaming, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Create().Run();

        Assert.NotEmpty(_log);
        Assert.DoesNotContain(_log, l => l.Contains(_tmp.Path, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_log, l => l.Contains("secret-project"));
    }
}
