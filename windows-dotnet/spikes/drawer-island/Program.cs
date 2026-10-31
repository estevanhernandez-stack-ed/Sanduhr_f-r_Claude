using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DrawerIsland;

/// <summary>THROWAWAY SPIKE. One drawer island over a sandbox folder. Logs every event to drawer-island.log
/// beside the exe so the verdict cites lines, not memory.</summary>
public static class Program
{
    public static readonly string Sandbox = Path.Combine(Path.GetTempPath(), "drawer-island-sandbox");
    public static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "drawer-island.log");

    public static void Log(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        Debug.WriteLine(text);
        File.AppendAllText(LogFile, text + Environment.NewLine);
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();

    [STAThread]
    public static void Main()
    {
        Directory.CreateDirectory(Sandbox);
        foreach (var n in new[] { "alpha.txt", "beta.txt", "gamma.txt" })
        {
            var p = Path.Combine(Sandbox, n);
            if (!File.Exists(p)) File.WriteAllText(p, n);
        }
        Log($"start: sandbox {Sandbox}");
        // v2: Explorer's dark menus. uxtheme #135 SetPreferredAppMode (1 = AllowDark, follows the system),
        // #136 FlushMenuThemes. Undocumented, the route Notepad++ and Files use.
        try { Log($"dark: SetPreferredAppMode -> {SetPreferredAppMode(1)}"); FlushMenuThemes(); }
        catch (Exception e) { Log($"dark: {e.GetType().Name}"); }
        new Application().Run(new IslandWindow());
    }
}

internal sealed class IslandWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_LAYERED = 0x00080000;
    private const int GWLP_HWNDPARENT = -8;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr h, bool allow);

    private readonly ObservableCollection<string> _items = [];
    private readonly ListBox _list = new();
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private IntPtr _hwnd;
    private int _pinned;
    private FileSystemWatcher? _watcher; // held: a local one is collected and goes quiet
    private Point _dragStart;

    public IslandWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 32));
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 320; Height = 260; Left = 80; Top = 80;
        AllowDrop = true;
        _list.ItemsSource = _items;
        _list.Background = Brushes.Transparent;
        _list.Foreground = Brushes.White;
        Content = new DockPanel { Children = { new TextBlock { Text = "Spike drawer", Foreground = Brushes.White, Margin = new Thickness(8) }, _list } };
        DockPanel.SetDock((UIElement)((DockPanel)Content).Children[0], Dock.Top);
        // v3: the title is the move handle (for the second-monitor check).
        ((UIElement)((DockPanel)Content).Children[0]).MouseLeftButtonDown += (_, _) => { DragMove(); Program.Log($"move: at {Left:0},{Top:0}"); };
        Refresh();
        _watcher = new FileSystemWatcher(Program.Sandbox) { EnableRaisingEvents = true };
        var watcher = _watcher;
        FileSystemEventHandler again = (_, _) => Dispatcher.Invoke(Refresh);
        watcher.Created += again; watcher.Deleted += again; watcher.Changed += again;
        watcher.Renamed += (_, _) => Dispatcher.Invoke(Refresh);

        Drop += OnDrop;                                   // check 1
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        _list.PreviewMouseMove += OnMaybeDrag;            // check 2
        _list.MouseRightButtonUp += OnRightClick;         // check 3
        _list.KeyDown += (_, e) => Program.Log($"key: {e.Key} (focus held: {_list.IsKeyboardFocusWithin})"); // check 4
        GotKeyboardFocus += (_, _) => Program.Log($"focus: got (foreground {GetForegroundWindow() == _hwnd})");
        LostKeyboardFocus += (_, _) => Program.Log("focus: lost");
        // v2: activate on click (drop WS_EX_NOACTIVATE for the visit), give it back and sink on click away.
        PreviewMouseDown += (_, _) => TakeFocus();
        Deactivated += (_, _) => GiveBack();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
        Own();
        HwndSource.FromHwnd(_hwnd).AddHook(Hook);
        Program.Log($"dark: AllowDarkModeForWindow -> {AllowDarkModeForWindow(_hwnd, true)}");
    }

    /// <summary>Owned by Progman, bottom of the z-order: the Desk's V3 click-layer placement.</summary>
    private void Own()
    {
        var progman = FindWindowW("Progman", null);
        SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, progman);
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log($"own: Progman 0x{progman:X}");
    }

    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        // v4: pin the z-order. Activation raises after any SetWindowPos we make, so rewrite every z-order
        // change to HWND_BOTTOM as it happens (WM_WINDOWPOSCHANGING; WINDOWPOS.hwndInsertAfter, flags).
        if (msg == 0x46)
        {
            int flags = Marshal.ReadInt32(l, IntPtr.Size * 2 + 16);
            if ((flags & 0x4) == 0) // no SWP_NOZORDER: it wants to move in the z-order
            {
                Marshal.WriteIntPtr(l, IntPtr.Size, HWND_BOTTOM);
                if (++_pinned <= 5) Program.Log("zorder: pinned to bottom");
            }
        }
        if (msg == (int)_taskbarCreated) { Program.Log("explorer: restarted, re-owning"); Own(); } // check 5
        // v2: Open with and Send to fill in lazily; they need these messages forwarded (IContextMenu3).
        if (ShellMenu.Forward(msg, w, l, out var result)) { handled = true; return result; }
        return IntPtr.Zero;
    }

    private void TakeFocus()
    {
        if (GetForegroundWindow() == _hwnd) return;
        SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) & ~WS_EX_NOACTIVATE);
        bool ok = SetForegroundWindow(_hwnd);
        Activate();
        _list.Focus();
        // v3: active, but not raised: keys come here while the drawer stays under the apps, as desktop icons do.
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log($"activate: SetForegroundWindow {ok}, foreground now {GetForegroundWindow() == _hwnd}");
    }

    private void GiveBack()
    {
        SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Program.Log("activate: gave back, sunk to bottom");
    }

    private void Refresh()
    {
        _items.Clear();
        foreach (var f in Directory.EnumerateFileSystemEntries(Program.Sandbox)) _items.Add(Path.GetFileName(f));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) { Program.Log("drop: no file list"); return; }
        foreach (var p in paths)
        {
            var target = Path.Combine(Program.Sandbox, Path.GetFileName(p));
            if (File.Exists(target) || Directory.Exists(target)) { Program.Log($"drop: {Path.GetFileName(p)} exists, skipped"); continue; }
            if (Directory.Exists(p)) Directory.Move(p, target); else File.Move(p, target);
            Program.Log($"drop: moved {Path.GetFileName(p)} in");
        }
        Refresh();
    }

    private void OnMaybeDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _list.SelectedItem is not string name) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(Program.Sandbox, name) });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
        var result = DragDrop.DoDragDrop(_list, data, DragDropEffects.Move | DragDropEffects.Copy);
        Program.Log($"drag out: {name} -> {result}");
        Refresh();
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_list.SelectedItem is not string name) return;
        var screen = PointToScreen(e.GetPosition(this));
        var chosen = ShellMenu.Show(_hwnd, Path.Combine(Program.Sandbox, name), (int)screen.X, (int)screen.Y);
        Program.Log($"menu: {name} -> {chosen ?? "(dismissed)"}");
        Refresh();
    }
}
