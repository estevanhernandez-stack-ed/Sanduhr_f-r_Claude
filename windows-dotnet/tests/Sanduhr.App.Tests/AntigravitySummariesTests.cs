using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class AntigravitySummariesTests
{
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, IntPtr err);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_close(IntPtr db);

    private static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static IntPtr Create(string path, string sql)
    {
        Assert.Equal(0, sqlite3_open_v2(Z(path), out var db, 0x2 | 0x4, IntPtr.Zero));   // READWRITE | CREATE
        Assert.Equal(0, sqlite3_exec(db, Z(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        return db;
    }

    private const string Schema =
        "CREATE TABLE conversation_summaries (conversation_id TEXT, title TEXT, preview TEXT, workspace_uris TEXT, raw_summary TEXT, last_modified_time TEXT);" +
        "INSERT INTO conversation_summaries VALUES ('c1','t','p','[\"file:///C:/x/api\"]','r','2026-11-10');" +
        "INSERT INTO conversation_summaries VALUES ('c2','t','p',NULL,'r','2026-11-10');";

    [Fact]
    public void The_query_selects_only_the_id_and_the_workspace() =>
        Assert.Equal("SELECT conversation_id, workspace_uris FROM conversation_summaries", AntigravitySummaries.Query);

    [Fact]
    public void Reads_each_conversation_and_its_workspace_json()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "conversation_summaries.db");
            sqlite3_close(Create(path, Schema));

            var map = AntigravitySummaries.Read(path);

            Assert.NotNull(map);
            Assert.Equal("[\"file:///C:/x/api\"]", map!["c1"]);
            Assert.Null(map["c2"]);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_missing_file_is_null() =>
        Assert.Null(AntigravitySummaries.Read(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".db")));

    [Fact]
    public void A_database_without_the_table_is_null()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "x.db");
            sqlite3_close(Create(path, "CREATE TABLE other (a TEXT);"));
            Assert.Null(AntigravitySummaries.Read(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_locked_database_is_null_after_the_busy_timeout()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "conversation_summaries.db");
            var writer = Create(path, Schema + "BEGIN EXCLUSIVE;");
            try { Assert.Null(AntigravitySummaries.Read(path, busyTimeoutMs: 50)); }
            finally { sqlite3_close(writer); }
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void FileUri_escapes_percent_first_and_leaves_non_ascii_raw() =>
        Assert.Equal("file:///C:/a%20b/c%23d%2541%20é.db", AntigravitySummaries.FileUri(@"C:\a b\c#d%41 é.db"));

    [Fact]
    public void Reads_a_database_whose_folder_has_space_hash_and_percent_escapes()
    {
        var root = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(root.FullName, "a b#c%41"));
            var path = Path.Combine(sub.FullName, "conversation_summaries.db");
            sqlite3_close(Create(path, Schema));

            var map = AntigravitySummaries.Read(path);

            Assert.NotNull(map);
            Assert.Equal("[\"file:///C:/x/api\"]", map!["c1"]);
        }
        finally { root.Delete(true); }
    }
}
