using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public sealed class ConfigCommitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-cc-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "home", "settings.json");
    private string Backups => Path.Combine(_root, "backups");
    private ConfigCommit Sut => new(Backups);

    public ConfigCommitTests() => Directory.CreateDirectory(Path.Combine(_root, "home"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private IEnumerable<string> HomeFiles() => Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(Config)!).Select(Path.GetFileName)!;

    [Fact]
    public void Read_returns_null_for_a_missing_file()
    {
        Assert.Null(Sut.Read(Config));
        File.WriteAllBytes(Config, B("{}"));
        Assert.Equal(B("{}"), Sut.Read(Config));
    }

    [Fact]
    public void Commit_replaces_the_file_and_leaves_no_temp_behind()
    {
        File.WriteAllBytes(Config, B("{\n  \"a\": 1\n}"));
        var original = Sut.Read(Config);
        Assert.True(Sut.Commit(Config, original, B("{\n  \"a\": 2\n}"), backup: false));
        Assert.Equal(B("{\n  \"a\": 2\n}"), File.ReadAllBytes(Config));
        Assert.Equal(new[] { "settings.json" }, HomeFiles());
        Assert.False(File.GetAttributes(Config).HasFlag(FileAttributes.Hidden));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Commit_creates_the_file_when_the_original_was_null()
    {
        var path = Path.Combine(_root, "fresh", "deeper", ".claude.json");
        Assert.True(Sut.Commit(path, null, B("{}"), backup: true));
        Assert.Equal(B("{}"), File.ReadAllBytes(path));
        Assert.False(Directory.Exists(Backups));   // nothing to back up
    }

    [Fact]
    public void Commit_aborts_when_the_file_changed_since_it_was_read()
    {
        File.WriteAllBytes(Config, B("{\"a\": 1}"));
        var original = Sut.Read(Config);
        File.WriteAllBytes(Config, B("{\"a\": 1, \"claude\": true}"));     // Claude Code wrote meanwhile
        Assert.False(Sut.Commit(Config, original, B("{\"a\": 2}"), backup: true));
        Assert.Equal(B("{\"a\": 1, \"claude\": true}"), File.ReadAllBytes(Config));
        Assert.False(Directory.Exists(Backups));
        Assert.Equal(new[] { "settings.json" }, HomeFiles());
    }

    [Fact]
    public void Commit_aborts_when_a_file_appeared_while_creating_and_when_it_vanished()
    {
        File.WriteAllBytes(Config, B("{\"x\": 1}"));
        Assert.False(Sut.Commit(Config, null, B("{}"), backup: false));
        Assert.Equal(B("{\"x\": 1}"), File.ReadAllBytes(Config));

        File.Delete(Config);
        Assert.False(Sut.Commit(Config, B("{\"x\": 1}"), B("{}"), backup: false));
        Assert.False(File.Exists(Config));
    }

    [Fact]
    public void Backup_holds_the_original_bytes_in_the_backups_folder_and_never_beside_the_file()
    {
        var original = B("{\r\n  \"a\": 1\r\n}\r\n");
        File.WriteAllBytes(Config, original);
        Assert.True(Sut.Commit(Config, original, B("{\r\n  \"a\": 2\r\n}\r\n"), backup: true));

        var backups = Directory.GetFiles(Backups);
        var one = Assert.Single(backups);
        Assert.Equal(original, File.ReadAllBytes(one));
        Assert.Matches("^[0-9a-f]{12}-[0-9]{17}\\.json$", Path.GetFileName(one));
        Assert.StartsWith(ConfigCommit.PathHash(Config) + "-", Path.GetFileName(one));
        Assert.Equal(new[] { "settings.json" }, HomeFiles());
    }

    [Fact]
    public void Two_backups_in_the_same_millisecond_get_distinct_names()
    {
        for (int i = 0; i < 5; i++)
        {
            var original = Sut.Read(Config) ?? B("{\"n\": -1}");
            if (i == 0) File.WriteAllBytes(Config, original);
            Assert.True(Sut.Commit(Config, original, B($"{{\"n\": {i}}}"), backup: true));
        }
        Assert.Equal(5, Directory.GetFiles(Backups).Length);
    }

    [Fact]
    public void Path_hash_is_twelve_lowercase_hex_and_ignores_case()
    {
        var h = ConfigCommit.PathHash(@"C:\Users\Me\.claude\settings.json");
        Assert.Matches("^[0-9a-f]{12}$", h);
        Assert.Equal(h, ConfigCommit.PathHash(@"c:\users\me\.claude\SETTINGS.JSON"));
        Assert.NotEqual(h, ConfigCommit.PathHash(@"C:\Users\Me\.claude-work\settings.json"));
    }

    [Fact]
    public void A_file_that_isnt_a_json_object_is_never_written()
    {
        foreach (var bad in new[] { "", "[]", "{\"a\": 1,}", "// c\n{}", "not json" })
        {
            File.WriteAllBytes(Config, B(bad));
            var original = Sut.Read(Config);
            Assert.Throws<JsonEditException>(() => Sut.Commit(Config, original, B("{}"), backup: true));
            Assert.Equal(B(bad), File.ReadAllBytes(Config));
            Assert.Equal("refused", CommitOutcomeSafe(() => Sut.RetryCommit(Config, _ => B("{}"), backup: true)));
            Assert.Equal(B(bad), File.ReadAllBytes(Config));
        }
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Bytes_that_arent_a_json_object_are_never_written()
    {
        File.WriteAllBytes(Config, B("{}"));
        var dup = B("{\"env\": {}, \"env\": {}}");
        Assert.Throws<JsonEditException>(() => Sut.Commit(Config, dup, B("{}"), backup: true));
        Assert.Throws<JsonEditException>(() => Sut.Commit(Config, B("{}"), dup, backup: false));
        Assert.Throws<JsonEditException>(() => Sut.Commit(Config, B("{}"), B("[1]"), backup: false));
        Assert.Throws<JsonEditException>(() => Sut.Commit(Config, B("{}"), B("{"), backup: false));
        Assert.Equal(B("{}"), File.ReadAllBytes(Config));
    }

    private static string CommitOutcomeSafe(Func<CommitOutcome> f)
    {
        try { return f().ToString(); }
        catch (JsonEditException) { return "refused"; }
    }

    [Fact]
    public void Commit_with_null_bytes_deletes_the_file_it_read()
    {
        File.WriteAllBytes(Config, B("{}"));
        Assert.True(Sut.Commit(Config, B("{}"), null, backup: false));
        Assert.False(File.Exists(Config));
    }

    [Fact]
    public void RetryCommit_retries_after_a_concurrent_change_and_plans_against_the_new_bytes()
    {
        File.WriteAllBytes(Config, B("{\"a\": 1}"));
        var seen = new List<string>();
        var outcome = Sut.RetryCommit(Config, current =>
        {
            var text = Encoding.UTF8.GetString(current!);
            seen.Add(text);
            if (seen.Count == 1) File.WriteAllBytes(Config, B("{\"a\": 1, \"b\": 2}"));   // changes between read and commit
            return B(text.TrimEnd('}') + ", \"sanduhr\": true}");
        }, backup: false);

        Assert.Equal(CommitOutcome.Written, outcome);
        Assert.Equal(new[] { "{\"a\": 1}", "{\"a\": 1, \"b\": 2}" }, seen);
        Assert.Equal(B("{\"a\": 1, \"b\": 2, \"sanduhr\": true}"), File.ReadAllBytes(Config));
    }

    [Fact]
    public void RetryCommit_gives_up_after_the_tries_and_writes_nothing()
    {
        File.WriteAllBytes(Config, B("{\"n\": 0}"));
        int calls = 0;
        var outcome = Sut.RetryCommit(Config, current =>
        {
            calls++;
            File.WriteAllBytes(Config, B($"{{\"n\": {calls}}}"));      // always loses the race
            return B("{\"mine\": true}");
        }, backup: false, tries: 3);

        Assert.Equal(CommitOutcome.Conflict, outcome);
        Assert.Equal(3, calls);
        Assert.Equal(B("{\"n\": 3}"), File.ReadAllBytes(Config));
        Assert.Equal(new[] { "settings.json" }, HomeFiles());
    }

    [Fact]
    public void RetryCommit_never_writes_when_the_plan_returns_null()
    {
        File.WriteAllBytes(Config, B("{\"a\": 1}"));
        var stamp = File.GetLastWriteTimeUtc(Config);
        Assert.Equal(CommitOutcome.NothingToDo, Sut.RetryCommit(Config, _ => null, backup: true));
        Assert.Equal(B("{\"a\": 1}"), File.ReadAllBytes(Config));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Config));
        Assert.False(Directory.Exists(Backups));

        var missing = Path.Combine(_root, "home", "nope.json");
        Assert.Equal(CommitOutcome.NothingToDo, Sut.RetryCommit(missing, _ => null, backup: true));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void RetryCommit_creates_a_missing_file_and_backs_up_an_existing_one_once()
    {
        var path = Path.Combine(_root, "home", "new.json");
        Assert.Equal(CommitOutcome.Written, Sut.RetryCommit(path, cur => { Assert.Null(cur); return B("{}"); }, backup: true));
        Assert.False(Directory.Exists(Backups));

        Assert.Equal(CommitOutcome.Written, Sut.RetryCommit(path, cur => B("{\"a\": 1}"), backup: true));
        Assert.Single(Directory.GetFiles(Backups));
        Assert.Equal(B("{}"), File.ReadAllBytes(Directory.GetFiles(Backups)[0]));
    }

    [Fact]
    public void Commit_keeps_the_target_attributes()
    {
        File.WriteAllBytes(Config, B("{}"));
        File.SetAttributes(Config, FileAttributes.Archive);
        Assert.True(Sut.Commit(Config, B("{}"), B("{\"a\": 1}"), backup: false));
        Assert.False(File.GetAttributes(Config).HasFlag(FileAttributes.Hidden));
    }
}
