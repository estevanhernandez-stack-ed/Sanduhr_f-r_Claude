using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sanduhr.App.Interop;

/// <summary>One Shell file operation's outcome: whether it ran, where the item landed, and the HRESULT for the log.</summary>
internal sealed record ShellResult(bool Ok, string? NewPath, int HResult);

/// <summary>
/// The Shell calls drawers make (5.0 drawers spec, section 4): one item per <c>IFileOperation</c>, with no UI and
/// <c>FOF_RENAMEONCOLLISION</c> so a race can never overwrite; final paths for the root check; the app holding a file for the result
/// line; thumbnails; opening an item. Nothing here throws on a Shell failure: the caller skips that one file.
/// </summary>
internal static class ShellFiles
{
    public static ShellResult Move(string source, string targetDir, string newName) =>
        Run(op => op.MoveItem(ShellNative.Item(source), ShellNative.Item(targetDir), newName, null));

    public static ShellResult Rename(string path, string newName) =>
        Run(op => op.RenameItem(ShellNative.Item(path), newName, null));

    private static ShellResult Run(Action<ShellNative.IFileOperation> queue)
    {
        ShellNative.IFileOperation? op = null;
        try
        {
            op = (ShellNative.IFileOperation)new ShellNative.FileOperation();
            var sink = new ShellNative.Sink();
            uint cookie = op.Advise(sink);
            op.SetOperationFlags(ShellNative.MoveFlags);
            queue(op);
            op.PerformOperations();
            bool aborted = op.GetAnyOperationsAborted();
            op.Unadvise(cookie);
            return new ShellResult(sink.Hr >= 0 && !aborted, sink.NewPath, sink.Hr);
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or DirectoryNotFoundException or ArgumentException
            or UnauthorizedAccessException or InvalidCastException)
        {
            return new ShellResult(false, null, e.HResult);
        }
        finally
        {
            if (op is not null) Marshal.FinalReleaseComObject(op);
        }
    }

    /// <summary>The path after every junction, link and 8.3 name is resolved, without the <c>\\?\</c> prefix; null when it cannot be
    /// opened, or resolves to a network share. Opening with no access rights works on a file another app holds.</summary>
    public static string? FinalPath(string path)
    {
        using var h = ShellNative.CreateFileW(path, 0, ShellNative.FILE_SHARE_ALL, IntPtr.Zero, ShellNative.OPEN_EXISTING,
            ShellNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var sb = new StringBuilder(1024);
        uint n = ShellNative.GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity) return null;
        var p = sb.ToString();
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return null;
        return p.StartsWith(@"\\?\", StringComparison.Ordinal) ? p[4..] : p;
    }

    /// <summary>The app holding <paramref name="path"/> open ("Microsoft Word"), from Restart Manager; null when none or unknown.</summary>
    public static string? Holder(string path)
    {
        if (ShellNative.RmStartSession(out var session, 0, new StringBuilder(33)) != 0) return null;
        try
        {
            if (ShellNative.RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return null;
            uint count = 0;
            int r = ShellNative.RmGetList(session, out var needed, ref count, null, out _);
            if (r != 234 || needed == 0) return null;   // ERROR_MORE_DATA: someone holds it
            var info = new ShellNative.RM_PROCESS_INFO[needed];
            count = needed;
            if (ShellNative.RmGetList(session, out _, ref count, info, out _) != 0 || count == 0) return null;
            var name = info[0].strAppName;   // streamer: notname - an app's name ("Microsoft Word"), never a user's
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        finally { ShellNative.RmEndSession(session); }
    }

    /// <summary>The Shell's thumbnail or icon for an item at <paramref name="pixels"/> square, frozen so any thread may hand it on.</summary>
    public static BitmapSource? Thumbnail(string path, int pixels)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var factory = (ShellNative.IShellItemImageFactory)ShellNative.Item(path);
            factory.GetImage(new ShellNative.SIZE { cx = pixels, cy = pixels }, 0, out hbm);
            if (hbm == IntPtr.Zero || ShellNative.GetObject(hbm, Marshal.SizeOf<ShellNative.BITMAP>(), out var bmp) == 0) return null;
            int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
            var bits = new byte[w * h * 4];
            var header = new ShellNative.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var dc = ShellNative.GetDC(IntPtr.Zero);
            try { if (ShellNative.GetDIBits(dc, hbm, 0, (uint)h, bits, ref header, 0) == 0) return null; }
            finally { ShellNative.ReleaseDC(IntPtr.Zero, dc); }
            // The Shell's images carry premultiplied alpha; read as plain BGRA their transparent corners turn black.
            var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or InvalidCastException or ArgumentException) { return null; }
        finally { if (hbm != IntPtr.Zero) ShellNative.DeleteObject(hbm); }
    }

    /// <summary>Opens an item as Explorer's double-click does. False when there is nothing to open it with.</summary>
    public static bool Open(string path)
    {
        try
        {
            // network: local (an item on this PC, through the Shell)
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { return false; }
    }
}
