using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.Core;

namespace Sanduhr.App.Interop;

/// <summary>One Start-menu app as <c>shell:AppsFolder</c> lists it.</summary>
/// <param name="Name">The name the Start menu shows.</param>
/// <param name="Aumid">The AppUserModelID, the part after <c>shell:AppsFolder\</c>.</param>
internal sealed record LauncherAppInfo(string Name, string Aumid);

/// <summary>What the Launcher asks of the Start menu: the app list, whether an app is still there, and its icon.
/// Every call may block on the shell, so callers run them off the UI thread.</summary>
internal interface ILauncherAppCatalog
{
    /// <summary>The apps in <c>shell:AppsFolder</c> whose IDs the Launcher accepts (<see cref="LauncherTarget"/>), by name.</summary>
    IReadOnlyList<LauncherAppInfo> Enumerate();

    /// <summary>True when the Start menu still has this app.</summary>
    bool Exists(string aumid);

    /// <summary>The app's own icon as a frozen bitmap at about <paramref name="px"/> pixels; null when the shell has none.</summary>
    BitmapSource? Icon(string aumid, int px);

    /// <summary>The name the Start menu shows for this app; null when it does not list it.</summary>
    string? Name(string aumid) => Enumerate().FirstOrDefault(a => string.Equals(a.Aumid, aumid, StringComparison.OrdinalIgnoreCase))?.Name;
}

/// <summary>
/// The Start menu through the shell (spec, Desk Launcher): <c>shell:AppsFolder</c> holds every app the Start menu
/// lists, Win32 programs, Store apps and Chrome or Edge web apps alike, each with a name, an AppUserModelID and an
/// icon. Enumeration walks the folder with <c>IEnumShellItems</c>, taking the display name and the parsing name (which
/// for this folder is the ID); an icon comes from <c>IShellItemImageFactory</c>; existence is
/// <c>SHCreateItemFromParsingName("shell:AppsFolder\&lt;id&gt;")</c>. Items whose ID the Launcher would refuse
/// (a malformed one) are left out of the list, so nothing offered can fail the gate. Classic desktop programs with no
/// AppUserModelID are listed under their path form, <c>{GUID}\Folder\File.exe</c>; such an ID is used only while this
/// listing contains it (membership), and it is opened through <c>explorer.exe shell:AppsFolder\&lt;id&gt;</c>, never by its exe path.
/// All shell work runs on one private STA thread, one job at a time, so the COM objects never cross threads.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LauncherApps : ILauncherAppCatalog, IDisposable
{
    private readonly BlockingCollection<Action> _jobs = [];
    private readonly Thread _worker;
    private bool _disposed;

    public LauncherApps()
    {
        _worker = new Thread(Pump) { IsBackground = true, Name = "Sanduhr.LauncherApps" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private void Pump()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try { job(); }
            catch { /* a job reports its own failure through its result */ }
        }
    }

    /// <summary>Runs <paramref name="work"/> on the shell thread and waits for its answer; <paramref name="fallback"/> if the shell throws or the catalog is gone.</summary>
    private T Run<T>(Func<T> work, T fallback)
    {
        if (_disposed) return fallback;
        T result = fallback;
        using var done = new ManualResetEventSlim();
        try
        {
            _jobs.Add(() =>
            {
                try { result = work(); }
                catch (Exception e) when (e is COMException or InvalidCastException or Win32Exception or ArgumentException or IOException) { result = fallback; }
                finally { done.Set(); }
            });
        }
        catch (InvalidOperationException) { return fallback; }   // completed: disposed meanwhile
        done.Wait();
        return result;
    }

    public IReadOnlyList<LauncherAppInfo> Enumerate() => Run<IReadOnlyList<LauncherAppInfo>>(EnumerateCore, []);

    /// <summary>True when the Start menu still has this app. A path-form ID (<c>{GUID}\Folder\File.exe</c>) counts only if the
    /// live <c>shell:AppsFolder</c> listing itself contains it: membership, not just something the shell can resolve.</summary>
    public bool Exists(string aumid) => LauncherTarget.AppShellPath(aumid) is { } path
        && Run(() => ItemExists(path) && (!LauncherTarget.IsPathForm(aumid) || EnumerateCore().Any(a => a.Aumid == aumid)), false);

    public BitmapSource? Icon(string aumid, int px) =>
        LauncherTarget.AppShellPath(aumid) is { } path ? Run(() => IconCore(path, Math.Clamp(px, 16, 512)), null) : null;

    /// <summary>The Start menu's name for one app, asked of <c>shell:AppsFolder\&lt;id&gt;</c> directly (no full listing).</summary>
    public string? Name(string aumid) =>
        LauncherTarget.AppShellPath(aumid) is { } path ? Run(() => NameCore(path), null) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _jobs.CompleteAdding();
    }

    // ---- shell work, on the STA thread ----

    private static IReadOnlyList<LauncherAppInfo> EnumerateCore()
    {
        var list = new List<LauncherAppInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Guid iid = Native.IID_IShellItem;
        if (Native.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iid, out var folder) != 0 || folder is null)
            return list;
        object? enumObj = null;
        try
        {
            Guid bhid = Native.BHID_EnumItems;
            Guid iidEnum = Native.IID_IEnumShellItems;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out enumObj) != 0 || enumObj is not Native.IEnumShellItems items)
                return list;
            while (items.Next(1, out var item, out uint fetched) == 0 && fetched == 1 && item is not null)
            {
                try
                {
                    string? name = DisplayName(item, Native.SIGDN_NORMALDISPLAY);
                    string? id = DisplayName(item, Native.SIGDN_PARENTRELATIVEPARSING);
                    if (string.IsNullOrWhiteSpace(name) || id is null) continue;
                    if (!LauncherTarget.Validate(LauncherKind.App, id).Ok || !seen.Add(id)) continue;
                    list.Add(new LauncherAppInfo(name.Trim(), id));
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            if (enumObj is not null) Marshal.ReleaseComObject(enumObj);
            Marshal.ReleaseComObject(folder);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    private static string? DisplayName(Native.IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static string? NameCore(string shellPath)
    {
        Guid iid = Native.IID_IShellItem;
        if (Native.SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref iid, out var item) != 0 || item is null) return null;
        try
        {
            var name = DisplayName(item, Native.SIGDN_NORMALDISPLAY)?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    private static bool ItemExists(string shellPath)
    {
        Guid iid = Native.IID_IShellItem;
        if (Native.SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref iid, out var item) != 0 || item is null) return false;
        Marshal.ReleaseComObject(item);
        return true;
    }

    private static BitmapSource? IconCore(string shellPath, int px)
    {
        Guid iid = Native.IID_IShellItem;
        if (Native.SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref iid, out var item) != 0 || item is null) return null;
        try
        {
            if (item is not Native.IShellItemImageFactory factory) return null;
            var size = new Native.SIZE { cx = px, cy = px };
            if (factory.GetImage(size, Native.SIIGBF_BIGGERSIZEOK | Native.SIIGBF_ICONONLY, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try { return Bitmaps.FromHBitmap(hbm); }
            finally { Native.DeleteObject(hbm); }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    // ---- opening ----

    /// <summary>How an open ended.</summary>
    internal enum OpenResult { Opened, Refused, Missing, Failed }

    /// <summary>Opens a button's target: an app through <c>explorer.exe shell:AppsFolder\&lt;id&gt;</c>, a link in the default
    /// browser. Nothing is elevated and nothing waits. A target that fails <see cref="LauncherTarget"/> is refused
    /// without touching the shell; an app the Start menu no longer has (when <paramref name="exists"/> says so) is Missing.</summary>
    internal static OpenResult Open(LauncherButton button, Func<string, bool>? exists = null)
    {
        try
        {
            if (button.Kind == LauncherKind.App)
            {
                if (LauncherTarget.AppShellPath(button.Target) is not { } shellPath) return OpenResult.Refused;
                if (exists is not null && !exists(button.Target)) return OpenResult.Missing;
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                var start = new ProcessStartInfo(explorer) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add(shellPath);   // one argument, quoted for us when the path has spaces; never the exe itself
                using var p = Process.Start(start);
                return OpenResult.Opened;
            }
            if (LauncherTarget.LinkUrl(button.Target) is not { } url) return OpenResult.Refused;
            using var q = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return OpenResult.Opened;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return OpenResult.Failed;
        }
    }

    /// <summary>HBITMAP to a frozen WPF bitmap, keeping the alpha channel the shell drew.</summary>
    private static class Bitmaps
    {
        public static BitmapSource? FromHBitmap(IntPtr hbm)
        {
            if (Native.GetObject(hbm, Marshal.SizeOf<Native.DIBSECTION>(), out Native.DIBSECTION ds) == 0) return null;
            var bm = ds.dsBm;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0 || bm.bmBitsPixel != 32 || bm.bmBits == IntPtr.Zero) return null;
            int stride = bm.bmWidthBytes;
            var pixels = new byte[stride * h];
            Marshal.Copy(bm.bmBits, pixels, 0, pixels.Length);
            // Some providers hand back a bottom-up DIB (a positive height in its header), others top-down (negative):
            // the web apps' icons came back upside down until the rows were turned over.
            if (ds.dsBmih.biHeight > 0) FlipRows(pixels, stride, h);
            bool premultiplied = IsPremultiplied(pixels, w, h, stride);
            var source = BitmapSource.Create(w, h, 96, 96, premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            return source;
        }

        private static void FlipRows(byte[] px, int stride, int h)
        {
            var row = new byte[stride];
            for (int top = 0, bottom = h - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(px, top * stride, row, 0, stride);
                Buffer.BlockCopy(px, bottom * stride, px, top * stride, stride);
                Buffer.BlockCopy(row, 0, px, bottom * stride, stride);
            }
        }

        /// <summary>True when no pixel has a color channel above its alpha, which only premultiplied data guarantees
        /// (checked on the partly transparent pixels; opaque and clear ones read the same either way).</summary>
        private static bool IsPremultiplied(byte[] px, int w, int h, int stride)
        {
            bool sawPartial = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    byte a = px[i + 3];
                    if (a == 0 || a == 255) continue;
                    sawPartial = true;
                    if (px[i] > a || px[i + 1] > a || px[i + 2] > a) return false;
                }
            return sawPartial;
        }
    }

    private static class Native
    {
        public const uint SIGDN_NORMALDISPLAY = 0x00000000;
        public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
        public const int SIIGBF_BIGGERSIZEOK = 0x1;
        public const int SIIGBF_ICONONLY = 0x4;

        public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
        public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IEnumShellItems
        {
            [PreserveSig] int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);
            [PreserveSig] int Skip(uint celt);
            [PreserveSig] int Reset();
            [PreserveSig] int Clone(out IEnumShellItems ppenum);
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DIBSECTION
        {
            public BITMAP dsBm;
            public BITMAPINFOHEADER dsBmih;
            public uint dsBitfields0, dsBitfields1, dsBitfields2;
            public IntPtr dshSection;
            public uint dsOffset;
        }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        public static extern int GetObject(IntPtr hObject, int cbBuffer, out DIBSECTION lpvObject);
    }
}
