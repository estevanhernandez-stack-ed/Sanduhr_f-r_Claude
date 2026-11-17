using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.App.Services;

/// <summary>
/// Antigravity CLI's conversation list, read-only through Windows' own SQLite (<c>winsqlite3.dll</c>, no package), for
/// spec 3's project lookup. Selects only <c>conversation_id</c> and <c>workspace_uris</c>: never titles, previews or
/// summaries. Null when the file is missing or SQLite refuses it (locked past the busy timeout, damaged, no table), so the
/// Antigravity source lists nothing that pass.
/// </summary>
internal static class AntigravitySummaries
{
    public const string Query = "SELECT conversation_id, workspace_uris FROM conversation_summaries";

    private const int SqliteOk = 0, SqliteRow = 100, SqliteDone = 101;
    private const int OpenReadOnly = 0x1, OpenUri = 0x40;

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_close(IntPtr db);

    private static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    /// <summary>SQLite file URI built by hand: <c>Uri.AbsoluteUri</c> decodes a literal <c>%XX</c> in the path and opens the wrong file.</summary>
    internal static string FileUri(string path)
    {
        var p = path.Replace('\\', '/').Replace("%", "%25").Replace("#", "%23").Replace("?", "%3F").Replace(" ", "%20");
        return p.StartsWith("//", StringComparison.Ordinal) ? "file:" + p : "file:///" + p;
    }

    public static IReadOnlyDictionary<string, string?>? Read(string dbPath, int busyTimeoutMs = 2000)
    {
        if (!File.Exists(dbPath)) return null;   // streamer: notname - a file location used for IO, never drawn
        IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
        try
        {
            var uri = FileUri(dbPath) + "?mode=ro";   // streamer: notname - the SQLite open URI, never drawn
            if (sqlite3_open_v2(Z(uri), out db, OpenReadOnly | OpenUri, IntPtr.Zero) != SqliteOk) return null;
            sqlite3_busy_timeout(db, busyTimeoutMs);
            if (sqlite3_prepare_v2(db, Z(Query), -1, out stmt, IntPtr.Zero) != SqliteOk) return null;
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            int rc;
            while ((rc = sqlite3_step(stmt)) == SqliteRow)
            {
                var id = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, 0));
                if (string.IsNullOrEmpty(id)) continue;
                map[id] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, 1));
            }
            return rc == SqliteDone ? map : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }
}
