# Drawers, plan 1 of 2: the spike and the Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove a Desk-style window can host drawers (the spike), then build every pure rule that drawers run
on: plan validation, the rule engine, the move journal and undo, turning off, settings and the layout word.

**Architecture:** Task 0 is a throwaway WPF spike with a written verdict; nothing after it may assume a
result it did not show. Tasks 1 to 7 are `Sanduhr.Core` only: pure functions over paths, listings and JSON,
with the file system passed in as delegates so every rule is tested without touching a disk. The App side
(the drawer island, `IFileOperation` moves, review, the front doors, the MCP tools) is plan 2, written
after the spike's verdict.

**Tech Stack:** .NET 10, C# 13, xUnit, System.Text.Json.Nodes; the spike adds WPF and Win32 COM interop.

**Spec:** `docs/superpowers/specs/2026-10-28-drawers-design.md`

## Global Constraints

- Core files added here do no IO: no `File.`, `Directory.`, `FileSystemWatcher`, network or process calls. The file system arrives as `Func<>` delegates.
- Every type lives in `namespace Sanduhr.Core;` (file-scoped), with `/// <summary>` docs in the house style (say what and why, one or two sentences).
- Moves are the only plan action: no delete, no overwrite, no rename of a user's file except the Explorer-style clash name `name (2).ext`.
- Allowed sources: the user's Desktop, the Public Desktop (listed, never moved: "shared, needs an administrator") and drawer homes. Allowed targets: drawer homes only.
- Plan drawers made by rules or agents live directly in the Drawers root, which by default is a folder on the Desktop itself (`<Desktop>\Drawers`, decision `KNT1mDtN3kQ0rlz2sePq`), so after any uninstall the files sit in one visible folder. The root folder itself is never a plan source. Drawers pointed at existing folders are made only by the user in Settings (plan 2).
- User-facing strings: no em dash, end sentences with a period.
- The working tree is CRLF with an LF index: edit with the Edit tool, never `sed -i`.
- Build and test from `windows-dotnet/` with `--disable-build-servers -nodeReuse:false` (an MSBuild node holding `obj` otherwise fails the next build with MSB3061).
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Feature name in code and copy is **Drawers**. Never "fence" or "Fences" (a Stardock product).
- The layout word for a drawer piece is `drawer.<id>`, for example `drawer.screenshots:tl:1`. This replaces the spec's `drawer:<name>`, which would have broken the grammar's `widget:anchor` order (Task 7 updates the spec).

## Review Focus

1. **Paths that look inside but are not:** `..` segments, forward slashes, trailing separators, mixed case, UNC (`\\server\share`) and `\\?\` device paths. Expected: normalized before any comparison; anything that resolves outside the roots refuses the whole plan. Pinned in Task 2.
2. **Two files with one name heading to one place:** two drawers each holding `a.txt` returning to a Desktop that already has `a.txt`. Expected: `a (2).txt` and `a (3).txt`, nothing overwritten. Pinned in Tasks 1 and 6.
3. **Damaged JSON:** a truncated settings object, a journal file that is not JSON, a rule with an unknown kind. Expected: defaults or the readable part, never an exception. Pinned in Tasks 5 and 7.
4. **Names an agent can send that Windows rejects:** `CON`, `nul.txt` style device names, `a/b`, `..`, trailing dots or spaces, 41+ characters. Expected: the plan is refused with the name in the error. Pinned in Tasks 1 and 2.
5. **A move that is already done or would swallow a drawer:** a source already inside its target drawer (a no-op, dropped), a source that is itself a drawer home (refused), and the Drawers root, which sits on the Desktop by default and so looks like any desktop folder (refused). Pinned in Task 2.

---

### Task 0: The drawer-island spike

Throwaway. Not in `Sanduhr.slnx`, not in CI, no reference to `Sanduhr.*`. It answers five questions on a
window styled like the Desk's islands (layered, never activated, owned by `Progman` at the bottom of the
z-order, as the under-icons spike's V3 click layer was). The App's real host is checked again in plan 2;
this spike decides whether plan 2 is approach 1 or an escalation.

**Files:**
- Create: `windows-dotnet/spikes/drawer-island/DrawerIsland.csproj`
- Create: `windows-dotnet/spikes/drawer-island/Program.cs`
- Create: `windows-dotnet/spikes/drawer-island/ShellMenu.cs`
- Create: `windows-dotnet/spikes/drawer-island/VERDICT.md`
- Modify: `windows-dotnet/docs/checklist.md` (append the verdict line under the Desk spikes)

**Interfaces:**
- Consumes: nothing.
- Produces: `VERDICT.md` with PASS or FAIL for each of the five checks. Plan 2 reads it.

- [ ] **Step 1: Create the project**

`windows-dotnet/spikes/drawer-island/DrawerIsland.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- THROWAWAY SPIKE (5.0 drawers, plan 1 task 0). Not in Sanduhr.slnx, not in CI, no reference to
       Sanduhr.*. A WPF window styled like a Desk island, testing OLE drag and drop, the Shell context
       menu and focus on a window that never activates. -->
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Write the island window**

`windows-dotnet/spikes/drawer-island/Program.cs`:

```csharp
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

    private readonly ObservableCollection<string> _items = [];
    private readonly ListBox _list = new();
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private IntPtr _hwnd;
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
        Refresh();
        new FileSystemWatcher(Program.Sandbox) { EnableRaisingEvents = true }.Changed += (_, _) => Dispatcher.Invoke(Refresh);

        Drop += OnDrop;                                   // check 1
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        _list.PreviewMouseMove += OnMaybeDrag;            // check 2
        _list.MouseRightButtonUp += OnRightClick;         // check 3
        _list.KeyDown += (_, e) => Program.Log($"key: {e.Key} (focus held: {_list.IsKeyboardFocusWithin})"); // check 4
        GotKeyboardFocus += (_, _) => Program.Log($"focus: got (foreground {GetForegroundWindow() == _hwnd})");
        LostKeyboardFocus += (_, _) => Program.Log("focus: lost");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
        Own();
        HwndSource.FromHwnd(_hwnd).AddHook(Hook);
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
        if (msg == (int)_taskbarCreated) { Program.Log("explorer: restarted, re-owning"); Own(); } // check 5
        return IntPtr.Zero;
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
```

- [ ] **Step 3: Write the Shell context menu interop**

`windows-dotnet/spikes/drawer-island/ShellMenu.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace DrawerIsland;

/// <summary>THROWAWAY SPIKE. Explorer's own context menu for one file: IShellItem, BHID_SFUIObject, IContextMenu.</summary>
internal static class ShellMenu
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, StringBuilder commandstring, int cch);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow; public uint dwHotKey; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW; public string? lpParametersW; public string? lpDirectoryW; public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    private const uint CMF_NORMAL = 0, CMF_EXTENDEDVERBS = 0x100, TPM_RETURNCMD = 0x100, CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint First = 1, Last = 0x7FFF;

    /// <summary>Shows the menu at a screen point and runs the chosen verb. Returns the verb's id, or null when dismissed.</summary>
    public static string? Show(IntPtr owner, string path, int x, int y)
    {
        var iidItem = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidItem, out var item);
        var bhid = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"); // BHID_SFUIObject
        var iidMenu = typeof(IContextMenu).GUID;
        item.BindToHandler(IntPtr.Zero, ref bhid, ref iidMenu, out var o);
        var menu = (IContextMenu)o;
        var h = CreatePopupMenu();
        try
        {
            menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL);
            uint cmd = TrackPopupMenuEx(h, TPM_RETURNCMD, x, y, owner, IntPtr.Zero);
            if (cmd == 0) return null;
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = owner,
                lpVerb = (IntPtr)(cmd - First),
                lpVerbW = (IntPtr)(cmd - First),
                nShow = 1,
                ptInvoke = new POINT { X = x, Y = y },
            };
            menu.InvokeCommand(ref info);
            return $"command {cmd - First}";
        }
        finally { DestroyMenu(h); }
    }
}
```

- [ ] **Step 4: Build it**

Run: `dotnet build spikes/drawer-island/DrawerIsland.csproj --disable-build-servers -nodeReuse:false`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Run the five checks by hand**

Run: `dotnet run --project spikes/drawer-island/DrawerIsland.csproj` and, with the island showing at the top left:

1. **Drop in:** drag a file from an Explorer window onto the island. PASS when `drop: moved <name> in` is logged and the file is gone from its source folder.
2. **Drag out:** drag `alpha.txt` from the island onto an Explorer folder window. PASS when the file lands there and leaves the sandbox (`drag out: alpha.txt -> Move`).
3. **Context menu:** right-click `beta.txt`. PASS when Explorer's own menu appears (Open with, Copy as path, and so on) and choosing Rename or Properties works.
4. **Focus:** click `gamma.txt`, press Down and F2. PASS when `key:` lines are logged. Then click the desktop. PASS when `focus: lost` is logged and the window that had the foreground before still has it.
5. **Explorer restart:** in an elevated terminal run `taskkill /f /im explorer.exe` and then `start explorer`. PASS when `explorer: restarted, re-owning` is logged and checks 1 and 3 pass again.

Repeat checks 1 and 2 with the island dragged onto a second monitor at a different scale, if one is attached.

- [ ] **Step 6: Write the verdict**

`windows-dotnet/spikes/drawer-island/VERDICT.md`, filled in from `drawer-island.log` (quote the lines; replace nothing with a guess):

```markdown
# Drawer-island spike verdict

Windows build: <winver output>. Monitors: <count, scales>. Date: <YYYY-MM-DD>.

| # | Check | Result | Evidence (log line) |
|---|---|---|---|
| 1 | OLE drop from Explorer | PASS or FAIL | `...` |
| 2 | OLE drag out, accepted as a move | PASS or FAIL | `...` |
| 3 | Explorer's context menu | PASS or FAIL | `...` |
| 4 | Focus on click, given back on click away | PASS or FAIL | `...` |
| 5 | Survives an Explorer restart | PASS or FAIL | `...` |

Verdict: approach 1 holds, or escalate with: <routes>.
```

Append one line to `windows-dotnet/docs/checklist.md` under the Desk spikes: `- [x] Drawers spike (5.0 plan 1 task 0): <PASS or FAIL per check>; spikes/drawer-island/VERDICT.md.`

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/spikes/drawer-island/DrawerIsland.csproj windows-dotnet/spikes/drawer-island/Program.cs windows-dotnet/spikes/drawer-island/ShellMenu.cs windows-dotnet/spikes/drawer-island/VERDICT.md windows-dotnet/docs/checklist.md
git commit -m "spike(drawers): drawer island on a never-activating Desk window, with verdict

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

**On any FAIL:** stop here and tell the owner. Tasks 1 to 7 are pure Core and still hold whatever the verdict, so
they may continue, but plan 2 is not written until the escalation is settled.

---

### Task 1: The drawer model, ids, names and clash names

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerModel.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum DrawerPlanSource { Rules, Agent, User }`
  - `record DrawerDef(string Id, string Name, string Home, bool Owned)`
  - `record DrawerMove(string Source, string DrawerId)`
  - `record DrawerPlan(DrawerPlanSource Source, string? Agent, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves)`
  - `record DesktopItem(string Path, bool IsFolder, DateTime WrittenUtc)`
  - `static class DrawerIds { bool IsValid(string? id); string From(string name); }`
  - `static class DrawerNames { const int MaxLength = 40; bool IsValid(string? name); string Free(string dir, string name, bool isFolder, Func<string, bool> taken); }`

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerModelTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerModelTests
{
    [Theory]
    [InlineData("screenshots", true)]
    [InlineData("trail-notes-2", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Screenshots", false)]
    [InlineData("-lead", false)]
    [InlineData("has space", false)]
    [InlineData("has:colon", false)]
    [InlineData("has@at", false)]
    [InlineData("has.dot", false)]
    public void Ids_are_lowercase_slugs_safe_in_a_layout_word(string? id, bool valid) =>
        Assert.Equal(valid, DrawerIds.IsValid(id));

    [Fact]
    public void Ids_longer_than_forty_are_refused() =>
        Assert.False(DrawerIds.IsValid(new string('a', 41)));

    [Theory]
    [InlineData("Screenshots", "screenshots")]
    [InlineData("Older files", "older-files")]
    [InlineData("  Trail  Notes!! ", "trail-notes")]
    [InlineData("Ünïcødé", "n-c-d")]
    [InlineData("!!!", "drawer")]
    public void From_turns_a_name_into_a_valid_id(string name, string id)
    {
        Assert.Equal(id, DrawerIds.From(name));
        Assert.True(DrawerIds.IsValid(DrawerIds.From(name)));
    }

    [Theory]
    [InlineData("Screenshots", true)]
    [InlineData("Older files", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("CON", false)]
    [InlineData("nul", false)]
    [InlineData("com1", false)]
    [InlineData("LPT9", false)]
    [InlineData("what?", false)]
    public void Names_are_names_Windows_accepts_as_a_folder(string? name, bool valid) =>
        Assert.Equal(valid, DrawerNames.IsValid(name));

    [Fact]
    public void Names_longer_than_forty_are_refused() =>
        Assert.False(DrawerNames.IsValid(new string('a', 41)));

    [Fact]
    public void Free_keeps_the_name_when_nothing_is_there() =>
        Assert.Equal(@"C:\d\report.docx", DrawerNames.Free(@"C:\d", "report.docx", false, _ => false));

    [Fact]
    public void Free_numbers_a_clash_the_way_Explorer_does()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\report.docx", @"C:\d\report (2).docx" };
        Assert.Equal(@"C:\d\report (3).docx", DrawerNames.Free(@"C:\d", "report.docx", false, taken.Contains));
    }

    [Fact]
    public void Free_numbers_a_folder_after_its_whole_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\my.project" };
        Assert.Equal(@"C:\d\my.project (2)", DrawerNames.Free(@"C:\d", "my.project", true, taken.Contains));
    }

    [Fact]
    public void Free_compares_without_case()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\A.TXT" };
        Assert.Equal(@"C:\d\a (2).txt", DrawerNames.Free(@"C:\d", "a.txt", false, taken.Contains));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerModelTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The name 'DrawerIds' does not exist in the current context`.

- [ ] **Step 3: Write the model**

`windows-dotnet/src/Sanduhr.Core/DrawerModel.cs`:

```csharp
using System.Text;

namespace Sanduhr.Core;

/// <summary>Who made a move plan (5.0 drawers spec, section 1): the built-in rules, an agent through the MCP, or the user's own hand.</summary>
public enum DrawerPlanSource { Rules, Agent, User }

/// <summary>A drawer: its id (the layout word's tail), the name it shows, the folder it shows, and whether Sanduhr owns that folder
/// (one it made under the Drawers root, which turning drawers off empties back onto the Desktop) or only shows it (a view).</summary>
public sealed record DrawerDef(string Id, string Name, string Home, bool Owned);

/// <summary>One file or folder going into a drawer.</summary>
public sealed record DrawerMove(string Source, string DrawerId);

/// <summary>A move plan: the drawers it needs and the moves, with who made it. Moves are the only action a plan can hold.</summary>
public sealed record DrawerPlan(DrawerPlanSource Source, string? Agent, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves);

/// <summary>One entry in a desktop or drawer listing, as the App reads it from disk.</summary>
public sealed record DesktopItem(string Path, bool IsFolder, DateTime WrittenUtc);

/// <summary>Drawer ids: lowercase letters, digits and hyphens, a letter or digit first, at most 40. No colon, at-sign, dot or space,
/// so <c>drawer.&lt;id&gt;</c> is always one safe layout word.</summary>
public static class DrawerIds
{
    public const int MaxLength = 40;

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxLength || !IsSlugChar(id[0]) || id[0] == '-') return false;
        foreach (var c in id)
            if (!IsSlugChar(c)) return false;
        return true;
    }

    /// <summary>The id a name gets: lowercased, every run of other characters one hyphen, trimmed of hyphens, cut to 40.
    /// A name with no ASCII letter or digit gets "drawer".</summary>
    public static string From(string name)
    {
        var sb = new StringBuilder();
        foreach (var raw in name.ToLowerInvariant())
        {
            if (raw is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(raw);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var id = sb.ToString().Trim('-');
        if (id.Length > MaxLength) id = id[..MaxLength].TrimEnd('-');
        return id.Length == 0 ? "drawer" : id;
    }

    private static bool IsSlugChar(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
}

/// <summary>Drawer names, which are also their folder names, and the Explorer-style name a clash gets.</summary>
public static class DrawerNames
{
    public const int MaxLength = 40;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A name Windows takes as a folder name: 1 to 40 characters, none of the forbidden ones, not "." or "..", no trailing dot
    /// or space, and not a device name (CON, NUL, COM1 and the rest, with or without an extension).</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength) return false;
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var stem = name.Split('.')[0].TrimEnd();
        return !Reserved.Contains(stem);
    }

    /// <summary>The path <paramref name="name"/> can take in <paramref name="dir"/>: itself when free, otherwise "name (2).ext", "(3)" and on,
    /// as Explorer names a clash. A folder is numbered after its whole name. Never returns a taken path, so nothing is overwritten.</summary>
    public static string Free(string dir, string name, bool isFolder, Func<string, bool> taken)
    {
        var first = Path.Combine(dir, name);
        if (!taken(first)) return first;
        var stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isFolder ? "" : Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!taken(candidate)) return candidate;
        }
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerModelTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerModel.cs windows-dotnet/tests/Sanduhr.Tests/DrawerModelTests.cs
git commit -m "feat(drawers): model, ids, names and Explorer-style clash names

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Allowed roots and the plan validator

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerPlanValidator.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerPlanValidatorTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerDef`, `DrawerMove`, `DrawerPlan`, `DrawerPlanSource`, `DrawerIds.IsValid`, `DrawerNames.IsValid`.
- Produces:
  - `record DrawerRoots(string Desktop, string PublicDesktop, string DrawersRoot)`
  - `static class DrawerPaths { string? Normalize(string? path); bool IsChildOf(string path, string dir); bool IsInside(string path, string root); }`
  - `record DrawerPlanCheck(bool Ok, string? Error, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves, IReadOnlyList<string> Shared)`
  - `static class DrawerPlanValidator { const int MaxDrawers = 24; const int MaxMoves = 2000; DrawerPlanCheck Check(DrawerPlan plan, DrawerRoots roots, IReadOnlyList<DrawerDef> existing); }`

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerPlanValidatorTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerPlanValidatorTests
{
    private static readonly DrawerRoots Roots = new(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", @"C:\Users\pat\Drawers");
    private static readonly DrawerDef Shots = new("screenshots", "Screenshots", @"C:\Users\pat\Drawers\Screenshots", true);
    private static readonly DrawerDef Downloads = new("downloads", "Downloads", @"C:\Users\pat\Downloads", false);

    private static DrawerPlan Agent(IReadOnlyList<DrawerDef> drawers, params DrawerMove[] moves) =>
        new(DrawerPlanSource.Agent, "codex", drawers, moves);

    [Fact]
    public void A_desktop_file_into_a_new_owned_drawer_passes()
    {
        var c = DrawerPlanValidator.Check(Agent([Shots], new DrawerMove(@"C:\Users\pat\Desktop\shot.png", "screenshots")), Roots, []);
        Assert.True(c.Ok, c.Error);
        Assert.Single(c.Moves);
        Assert.Equal([Shots], c.Drawers);
    }

    [Fact]
    public void A_move_into_an_existing_drawer_needs_no_definition()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.zip", "downloads")), Roots, [Downloads]);
        Assert.True(c.Ok, c.Error);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Users\pat\Desktop\..\..\other\Desktop\x.txt")]
    [InlineData(@"C:/Users/pat/Desktop/../Documents/secret.txt")]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData(@"\\?\C:\Users\pat\Desktop\x.txt")]
    [InlineData(@"Desktop\x.txt")]
    [InlineData(@"C:\Users\pat\Desktop\sub\deep.txt")]
    public void A_source_outside_the_roots_refuses_the_whole_plan(string source)
    {
        var plan = Agent([Shots],
            new DrawerMove(@"C:\Users\pat\Desktop\fine.png", "screenshots"),
            new DrawerMove(source, "screenshots"));
        var c = DrawerPlanValidator.Check(plan, Roots, []);
        Assert.False(c.Ok);
        Assert.Empty(c.Moves);
        Assert.Contains("outside", c.Error);
    }

    [Theory]
    [InlineData(@"c:\users\PAT\desktop\shot.png")]
    [InlineData(@"C:/Users/pat/Desktop/shot.png")]
    [InlineData(@"C:\Users\pat\Desktop\.\shot.png")]
    public void Case_slashes_and_dot_segments_still_count_as_the_Desktop(string source)
    {
        var c = DrawerPlanValidator.Check(Agent([Shots], new DrawerMove(source, "screenshots")), Roots, []);
        Assert.True(c.Ok, c.Error);
    }

    [Fact]
    public void Public_desktop_items_are_listed_as_shared_and_never_moved()
    {
        var plan = Agent([Shots],
            new DrawerMove(@"C:\Users\Public\Desktop\Game.lnk", "screenshots"),
            new DrawerMove(@"C:\Users\pat\Desktop\shot.png", "screenshots"));
        var c = DrawerPlanValidator.Check(plan, Roots, []);
        Assert.True(c.Ok, c.Error);
        Assert.Equal([@"C:\Users\Public\Desktop\Game.lnk"], c.Shared);
        Assert.Equal([@"C:\Users\pat\Desktop\shot.png"], c.Moves.Select(m => m.Source));
    }

    [Fact]
    public void A_source_already_in_its_drawer_is_dropped_as_done()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots\shot.png", "screenshots")), Roots, [Shots]);
        Assert.True(c.Ok, c.Error);
        Assert.Empty(c.Moves);
    }

    [Fact]
    public void A_file_moves_from_one_drawer_to_another()
    {
        var docs = new DrawerDef("documents", "Documents", @"C:\Users\pat\Drawers\Documents", true);
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots\a.pdf", "documents")), Roots, [Shots, docs]);
        Assert.True(c.Ok, c.Error);
        Assert.Single(c.Moves);
    }

    [Fact]
    public void A_drawer_home_itself_cannot_be_moved()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots", "downloads")), Roots, [Shots, Downloads]);
        Assert.False(c.Ok);
    }

    [Fact]
    public void With_the_root_on_the_Desktop_the_root_is_refused_and_its_drawers_are_sources()
    {
        var onDesk = new DrawerRoots(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", @"C:\Users\pat\Desktop\Drawers");
        var shots = new DrawerDef("screenshots", "Screenshots", @"C:\Users\pat\Desktop\Drawers\Screenshots", true);
        var docs = new DrawerDef("documents", "Documents", @"C:\Users\pat\Desktop\Drawers\Documents", true);

        var root = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\Drawers", "screenshots")), onDesk, [shots]);
        Assert.False(root.Ok);
        Assert.Contains("Drawers folder", root.Error);

        var between = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\Drawers\Screenshots\a.pdf", "documents")), onDesk, [shots, docs]);
        Assert.True(between.Ok, between.Error);
        Assert.Single(between.Moves);

        var fresh = DrawerPlanValidator.Check(Agent([docs], new DrawerMove(@"C:\Users\pat\Desktop\a.pdf", "documents")), onDesk, []);
        Assert.True(fresh.Ok, fresh.Error);
    }

    [Fact]
    public void A_view_drawer_on_the_Desktop_makes_a_desktop_source_a_no_op()
    {
        var desk = new DrawerDef("desk", "Desk", @"C:\Users\pat\Desktop", false);
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.txt", "desk")), Roots, [desk]);
        Assert.True(c.Ok, c.Error);
        Assert.Empty(c.Moves);
    }

    [Fact]
    public void An_unknown_drawer_refuses_the_plan()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.txt", "nowhere")), Roots, []);
        Assert.False(c.Ok);
        Assert.Contains("nowhere", c.Error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("dots.")]
    public void A_drawer_name_Windows_rejects_refuses_the_plan(string name)
    {
        var bad = new DrawerDef("bad", name, @"C:\Users\pat\Drawers\bad", true);
        var c = DrawerPlanValidator.Check(Agent([bad]), Roots, []);
        Assert.False(c.Ok);
        Assert.Contains(name, c.Error);
    }

    [Fact]
    public void A_plan_drawer_must_be_owned_and_live_directly_under_the_Drawers_root()
    {
        foreach (var d in new[]
        {
            new DrawerDef("x", "X", @"C:\Users\pat\Documents\X", true),
            new DrawerDef("x", "X", @"C:\Users\pat\Drawers\deeper\X", true),
            new DrawerDef("x", "X", @"C:\Users\pat\Drawers\X", false),
        })
            Assert.False(DrawerPlanValidator.Check(Agent([d]), Roots, []).Ok);
    }

    [Fact]
    public void A_plan_cannot_move_an_existing_drawer_to_another_home()
    {
        var moved = Shots with { Home = @"C:\Users\pat\Drawers\Elsewhere" };
        Assert.False(DrawerPlanValidator.Check(Agent([moved]), Roots, [Shots]).Ok);
    }

    [Fact]
    public void The_same_source_twice_refuses_the_plan()
    {
        var c = DrawerPlanValidator.Check(Agent([Shots],
            new DrawerMove(@"C:\Users\pat\Desktop\a.png", "screenshots"),
            new DrawerMove(@"c:\users\pat\desktop\A.png", "screenshots")), Roots, []);
        Assert.False(c.Ok);
    }

    [Fact]
    public void Too_many_drawers_or_moves_refuse_the_plan()
    {
        var many = Enumerable.Range(0, DrawerPlanValidator.MaxDrawers + 1)
            .Select(i => new DrawerDef($"d{i}", $"D{i}", $@"C:\Users\pat\Drawers\D{i}", true)).ToArray();
        Assert.False(DrawerPlanValidator.Check(Agent(many), Roots, []).Ok);
        var moves = Enumerable.Range(0, DrawerPlanValidator.MaxMoves + 1)
            .Select(i => new DrawerMove($@"C:\Users\pat\Desktop\f{i}.txt", "screenshots")).ToArray();
        Assert.False(DrawerPlanValidator.Check(Agent([Shots], moves), Roots, []).Ok);
    }

    [Fact]
    public void An_agent_name_is_required_and_short()
    {
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Agent, null, [], []), Roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Agent, new string('a', 41), [], []), Roots, []).Ok);
        Assert.True(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [], []), Roots, []).Ok);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerPlanValidatorTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The type or namespace name 'DrawerRoots' could not be found`.

- [ ] **Step 3: Write the validator**

`windows-dotnet/src/Sanduhr.Core/DrawerPlanValidator.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>Where drawer plans may reach (5.0 drawers spec, section 1): the user's Desktop and drawer homes as sources, the Public
/// Desktop listed but never moved (writing it needs an administrator, and Sanduhr never elevates), and the Drawers root for new homes.</summary>
public sealed record DrawerRoots(string Desktop, string PublicDesktop, string DrawersRoot);

/// <summary>Path rules for drawers. Everything is normalized first, so case, forward slashes, "." and ".." cannot slip a path past a root.</summary>
public static class DrawerPaths
{
    /// <summary>The full, normalized form of a rooted local path with no trailing separator, or null for a relative, UNC or
    /// device (<c>\\?\</c>) path, none of which a drawer plan may use.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(p)) return null;
        try { return Path.GetFullPath(p).TrimEnd('\\'); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>True when <paramref name="path"/> sits directly in <paramref name="dir"/>. Both must already be normalized.</summary>
    public static bool IsChildOf(string path, string dir) =>
        string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or anywhere under it. Both must already be normalized.</summary>
    public static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A checked plan: when <see cref="Ok"/>, the drawers to create and the moves to run (no-ops dropped) and the shared
/// Public Desktop items the review names; otherwise one error and nothing to run.</summary>
public sealed record DrawerPlanCheck(bool Ok, string? Error, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves,
    IReadOnlyList<string> Shared);

/// <summary>
/// Checks a move plan however it arrived (5.0 drawers spec, section 1). Any entry outside the roots refuses the whole plan, so a
/// half-good plan never runs half. The App checks each path again just before its move, with the file system in hand, for reparse
/// points; this is the pure half.
/// </summary>
public static class DrawerPlanValidator
{
    public const int MaxDrawers = 24;
    public const int MaxMoves = 2000;
    public const int MaxAgentName = 40;

    public static DrawerPlanCheck Check(DrawerPlan plan, DrawerRoots roots, IReadOnlyList<DrawerDef> existing)
    {
        if (plan.Source == DrawerPlanSource.Agent && (string.IsNullOrWhiteSpace(plan.Agent) || plan.Agent.Length > MaxAgentName))
            return Refuse("An agent plan needs the agent's name, at most 40 characters.");
        if (plan.Drawers.Count > MaxDrawers) return Refuse($"A plan can hold at most {MaxDrawers} drawers.");
        if (plan.Moves.Count > MaxMoves) return Refuse($"A plan can hold at most {MaxMoves} moves.");

        var desktop = DrawerPaths.Normalize(roots.Desktop);
        var shared = DrawerPaths.Normalize(roots.PublicDesktop);
        var root = DrawerPaths.Normalize(roots.DrawersRoot);
        if (desktop is null || shared is null || root is null) return Refuse("The drawer roots are not local folders.");

        var known = existing.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var created = new List<DrawerDef>();
        foreach (var d in plan.Drawers)
        {
            if (!DrawerIds.IsValid(d.Id)) return Refuse($"\"{d.Id}\" is not a drawer id.");
            if (!DrawerNames.IsValid(d.Name)) return Refuse($"\"{d.Name}\" can't be a folder name.");
            var home = DrawerPaths.Normalize(d.Home);
            if (known.TryGetValue(d.Id, out var had))
            {
                if (!string.Equals(home, DrawerPaths.Normalize(had.Home), StringComparison.OrdinalIgnoreCase))
                    return Refuse($"The drawer \"{d.Name}\" already has a home.");
                continue;
            }
            if (!d.Owned || home is null || !DrawerPaths.IsChildOf(home, root))
                return Refuse($"The drawer \"{d.Name}\" must live directly in the Drawers folder.");
            var made = d with { Home = home };
            known[d.Id] = made;
            created.Add(made);
        }

        var homes = known.Values.Select(d => DrawerPaths.Normalize(d.Home)).OfType<string>().ToArray();
        var moves = new List<DrawerMove>();
        var sharedItems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plan.Moves)
        {
            if (!known.TryGetValue(m.DrawerId, out var target)) return Refuse($"There is no drawer \"{m.DrawerId}\".");
            var source = DrawerPaths.Normalize(m.Source);
            if (source is null) return Refuse($"\"{m.Source}\" is outside the Desktop and the drawers.");
            if (!seen.Add(source)) return Refuse($"\"{m.Source}\" is in the plan twice.");
            if (DrawerPaths.IsChildOf(source, shared)) { sharedItems.Add(source); continue; }
            // The root sits on the Desktop by default, so it would otherwise pass as a desktop folder.
            if (string.Equals(source, root, StringComparison.OrdinalIgnoreCase))
                return Refuse("The Drawers folder itself can't go into a drawer.");
            if (homes.Any(h => string.Equals(h, source, StringComparison.OrdinalIgnoreCase)))
                return Refuse($"\"{m.Source}\" is a drawer's own folder.");
            bool fromDesktop = DrawerPaths.IsChildOf(source, desktop);
            bool fromDrawer = homes.Any(h => DrawerPaths.IsChildOf(source, h));
            if (!fromDesktop && !fromDrawer) return Refuse($"\"{m.Source}\" is outside the Desktop and the drawers.");
            var targetHome = DrawerPaths.Normalize(target.Home);
            if (targetHome is not null && DrawerPaths.IsChildOf(source, targetHome)) continue; // already there
            moves.Add(new DrawerMove(source, m.DrawerId));
        }
        return new DrawerPlanCheck(true, null, created, moves, sharedItems);
    }

    private static DrawerPlanCheck Refuse(string error) => new(false, error, [], [], []);
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerPlanValidatorTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerPlanValidator.cs windows-dotnet/tests/Sanduhr.Tests/DrawerPlanValidatorTests.cs
git commit -m "feat(drawers): plan validator with normalized roots, whole-plan refusal and shared Public Desktop items

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The rule engine

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerRules.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerRulesTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerDef`, `DrawerMove`, `DrawerPlan`, `DrawerPlanSource`, `DesktopItem`.
- Produces:
  - `enum DrawerRuleKind { Screenshots, Images, Documents, Installers, Archives, Shortcuts, OlderThan, NameContains, NameStartsWith }`
  - `record DrawerRule(DrawerRuleKind Kind, string DrawerId, bool On = true, string? Text = null, int Days = 30)`
  - `static class DrawerRules { IReadOnlyList<DrawerRule> Defaults; IReadOnlyList<DrawerDef> DefaultDrawers(string drawersRoot); bool Matches(DrawerRule rule, DesktopItem item, DateTime nowUtc); DrawerPlan Plan(IEnumerable<DesktopItem> items, IReadOnlyList<DrawerRule> rules, IReadOnlyList<DrawerDef> drawers, string drawersRoot, DateTime nowUtc); }`

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerRulesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerRulesTests
{
    private const string Root = @"C:\Users\pat\Drawers";
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static DesktopItem File(string name, int daysOld = 0) =>
        new($@"C:\Users\pat\Desktop\{name}", false, Now.AddDays(-daysOld));

    [Theory]
    [InlineData("Screenshot 2026-10-29 101500.png", DrawerRuleKind.Screenshots, true)]
    [InlineData("screenshot (3).PNG", DrawerRuleKind.Screenshots, true)]
    [InlineData("holiday.png", DrawerRuleKind.Screenshots, false)]
    [InlineData("Screenshot notes.txt", DrawerRuleKind.Screenshots, false)]
    [InlineData("holiday.JPG", DrawerRuleKind.Images, true)]
    [InlineData("taxes.pdf", DrawerRuleKind.Documents, true)]
    [InlineData("setup.msix", DrawerRuleKind.Installers, true)]
    [InlineData("backup.7z", DrawerRuleKind.Archives, true)]
    [InlineData("Game.lnk", DrawerRuleKind.Shortcuts, true)]
    [InlineData("site.url", DrawerRuleKind.Shortcuts, true)]
    [InlineData("noextension", DrawerRuleKind.Documents, false)]
    public void Kind_rules_match_by_extension_and_screenshots_by_name_too(string name, DrawerRuleKind kind, bool match) =>
        Assert.Equal(match, DrawerRules.Matches(new DrawerRule(kind, "x"), File(name), Now));

    [Fact]
    public void Older_than_counts_whole_days_since_the_last_write()
    {
        var rule = new DrawerRule(DrawerRuleKind.OlderThan, "older", Days: 30);
        Assert.True(DrawerRules.Matches(rule, File("old.txt", 31), Now));
        Assert.False(DrawerRules.Matches(rule, File("new.txt", 29), Now));
    }

    [Fact]
    public void Name_rules_ignore_case_and_need_text()
    {
        Assert.True(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameContains, "r", Text: "invoice"), File("March INVOICE.pdf"), Now));
        Assert.True(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameStartsWith, "r", Text: "trail"), File("Trail-map.png"), Now));
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameStartsWith, "r", Text: "trail"), File("my trail.png"), Now));
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameContains, "r", Text: ""), File("anything.pdf"), Now));
    }

    [Fact]
    public void Folders_never_match_a_rule()
    {
        var folder = new DesktopItem(@"C:\Users\pat\Desktop\Project.zip", true, Now.AddDays(-400));
        foreach (var kind in Enum.GetValues<DrawerRuleKind>())
            Assert.False(DrawerRules.Matches(new DrawerRule(kind, "x", Text: "Project"), folder, Now));
    }

    [Fact]
    public void A_rule_that_is_off_never_matches() =>
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.Images, "x", On: false), File("a.png"), Now));

    [Fact]
    public void The_first_matching_rule_wins_and_unmatched_files_stay()
    {
        var rules = new[]
        {
            new DrawerRule(DrawerRuleKind.Screenshots, "screenshots"),
            new DrawerRule(DrawerRuleKind.Images, "images"),
        };
        var plan = DrawerRules.Plan(
            [File("Screenshot 1.png"), File("cat.png"), File("notes.txt")],
            rules, DrawerRules.DefaultDrawers(Root), Root, Now);
        Assert.Equal(DrawerPlanSource.Rules, plan.Source);
        // Moves come in path order (cat before Screenshot), each to its first matching rule's drawer.
        Assert.Equal(
            [("cat.png", "images"), ("Screenshot 1.png", "screenshots")],
            plan.Moves.Select(m => (Path.GetFileName(m.Source), m.DrawerId)));
    }

    [Fact]
    public void The_plan_carries_only_the_drawers_it_fills()
    {
        var plan = DrawerRules.Plan([File("a.pdf")], DrawerRules.Defaults, DrawerRules.DefaultDrawers(Root), Root, Now);
        Assert.Equal(["documents"], plan.Drawers.Select(d => d.Id));
        Assert.Equal(@"C:\Users\pat\Drawers\Documents", plan.Drawers[0].Home);
    }

    [Fact]
    public void A_rule_whose_drawer_is_missing_is_skipped()
    {
        var plan = DrawerRules.Plan([File("a.pdf")], [new DrawerRule(DrawerRuleKind.Documents, "gone")], [], Root, Now);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void Defaults_leave_shortcuts_alone_and_sweep_old_files_last()
    {
        Assert.False(DrawerRules.Defaults.Single(r => r.Kind == DrawerRuleKind.Shortcuts).On);
        Assert.Equal(DrawerRuleKind.OlderThan, DrawerRules.Defaults[^1].Kind);
        foreach (var r in DrawerRules.Defaults)
            Assert.Contains(DrawerRules.DefaultDrawers(Root), d => d.Id == r.DrawerId);
    }

    [Fact]
    public void Default_drawers_are_owned_and_live_directly_in_the_root()
    {
        foreach (var d in DrawerRules.DefaultDrawers(Root))
        {
            Assert.True(d.Owned);
            Assert.Equal(Root, Path.GetDirectoryName(d.Home));
            Assert.True(DrawerIds.IsValid(d.Id));
            Assert.True(DrawerNames.IsValid(d.Name));
        }
    }

    [Fact]
    public void A_rule_plan_passes_the_validator()
    {
        var roots = new DrawerRoots(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", Root);
        var plan = DrawerRules.Plan([File("a.pdf"), File("Screenshot 2.png"), File("old.txt", 90)],
            DrawerRules.Defaults, DrawerRules.DefaultDrawers(Root), Root, Now);
        var check = DrawerPlanValidator.Check(plan, roots, []);
        Assert.True(check.Ok, check.Error);
        Assert.Equal(3, check.Moves.Count);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerRulesTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The name 'DrawerRules' does not exist in the current context`.

- [ ] **Step 3: Write the rule engine**

`windows-dotnet/src/Sanduhr.Core/DrawerRules.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>What a built-in rule looks at (5.0 drawers spec, section 3): a kind (by extension, screenshots by name too), age, or the name.</summary>
public enum DrawerRuleKind { Screenshots, Images, Documents, Installers, Archives, Shortcuts, OlderThan, NameContains, NameStartsWith }

/// <summary>One rule: a filter and the drawer it fills. <see cref="Text"/> is for the name rules, <see cref="Days"/> for the age rule.</summary>
public sealed record DrawerRule(DrawerRuleKind Kind, string DrawerId, bool On = true, string? Text = null, int Days = 30);

/// <summary>
/// The built-in sorter for people with no agent (5.0 drawers spec, section 3). Rules run top to bottom and the first match wins;
/// whatever no rule takes stays on the Desktop, with no Misc bucket. Rules match files only, never folders.
/// </summary>
public static class DrawerRules
{
    private static readonly HashSet<string> ImageExt = Set(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg");
    private static readonly HashSet<string> DocumentExt = Set(".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf", ".odt", ".ods", ".odp", ".csv");
    private static readonly HashSet<string> InstallerExt = Set(".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle");
    private static readonly HashSet<string> ArchiveExt = Set(".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso");
    private static readonly HashSet<string> ShortcutExt = Set(".lnk", ".url");

    /// <summary>The built-in set, in order. Shortcuts start off (most desktops keep their app shortcuts on purpose); old files go last,
    /// so they take only what the kind rules left.</summary>
    public static IReadOnlyList<DrawerRule> Defaults { get; } =
    [
        new(DrawerRuleKind.Screenshots, "screenshots"),
        new(DrawerRuleKind.Images, "images"),
        new(DrawerRuleKind.Documents, "documents"),
        new(DrawerRuleKind.Installers, "installers"),
        new(DrawerRuleKind.Archives, "archives"),
        new(DrawerRuleKind.Shortcuts, "shortcuts", On: false),
        new(DrawerRuleKind.OlderThan, "older-files", Days: 30),
    ];

    /// <summary>The drawers the defaults fill, each owned and directly in <paramref name="drawersRoot"/>.</summary>
    public static IReadOnlyList<DrawerDef> DefaultDrawers(string drawersRoot) =>
        new[] { "Screenshots", "Images", "Documents", "Installers", "Archives", "Shortcuts", "Older files" }
            .Select(name => new DrawerDef(DrawerIds.From(name), name, Path.Combine(drawersRoot, name), true))
            .ToArray();

    public static bool Matches(DrawerRule rule, DesktopItem item, DateTime nowUtc)
    {
        if (!rule.On || item.IsFolder) return false;
        var name = Path.GetFileName(item.Path);
        var ext = Path.GetExtension(name);
        return rule.Kind switch
        {
            DrawerRuleKind.Screenshots => ImageExt.Contains(ext) && name.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase),
            DrawerRuleKind.Images => ImageExt.Contains(ext),
            DrawerRuleKind.Documents => DocumentExt.Contains(ext),
            DrawerRuleKind.Installers => InstallerExt.Contains(ext),
            DrawerRuleKind.Archives => ArchiveExt.Contains(ext),
            DrawerRuleKind.Shortcuts => ShortcutExt.Contains(ext),
            DrawerRuleKind.OlderThan => (nowUtc - item.WrittenUtc).TotalDays > rule.Days,
            DrawerRuleKind.NameContains => !string.IsNullOrEmpty(rule.Text) && name.Contains(rule.Text, StringComparison.OrdinalIgnoreCase),
            DrawerRuleKind.NameStartsWith => !string.IsNullOrEmpty(rule.Text) && name.StartsWith(rule.Text, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>The rules' plan for a listing: each file goes to the drawer of the first rule that matches it and names a drawer in
    /// <paramref name="drawers"/>. The plan carries only the drawers it fills; the validator decides which are new.</summary>
    public static DrawerPlan Plan(IEnumerable<DesktopItem> items, IReadOnlyList<DrawerRule> rules, IReadOnlyList<DrawerDef> drawers,
        string drawersRoot, DateTime nowUtc)
    {
        var byId = drawers.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var moves = new List<DrawerMove>();
        var used = new List<DrawerDef>();
        foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rule = rules.FirstOrDefault(r => byId.ContainsKey(r.DrawerId) && Matches(r, item, nowUtc));
            if (rule is null) continue;
            moves.Add(new DrawerMove(item.Path, rule.DrawerId));
            if (!used.Any(d => d.Id == rule.DrawerId)) used.Add(byId[rule.DrawerId]);
        }
        return new DrawerPlan(DrawerPlanSource.Rules, null, used, moves);
    }

    private static HashSet<string> Set(params string[] exts) => new(exts, StringComparer.OrdinalIgnoreCase);
}
```

Note: `drawersRoot` is in the signature so plan 2's caller can pass the root it read from settings without the engine guessing; the
engine uses only `drawers`. Remove the parameter if the reviewer prefers, and update the tests in the same commit.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerRulesTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerRules.cs windows-dotnet/tests/Sanduhr.Tests/DrawerRulesTests.cs
git commit -m "feat(drawers): built-in rules, first match wins, files only

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The move journal and undo

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerJournal.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerJournalTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerPlanSource`, `DrawerNames.Free`.
- Produces:
  - `record JournalMove(string From, string To, bool IsFolder, DateTime? WrittenUtc)` (`WrittenUtc` null for folders: a folder's write time changes with its contents, so undo does not compare it)
  - `record JournalEntry(string Id, DateTime AtUtc, DrawerPlanSource Source, string? Agent, IReadOnlyList<JournalMove> Moves)`
  - `sealed class DrawerJournal { const int MaxPlans = 50; static readonly TimeSpan KeepUserMoves; IReadOnlyList<JournalEntry> Entries; DrawerJournal Add(JournalEntry e, DateTime nowUtc); static DrawerJournal Decode(string? json); string Encode(); IReadOnlyList<JournalSummaryLine> Summary(); }`
  - `record JournalSummaryLine(DateTime AtUtc, DrawerPlanSource Source, string? Agent, int Files)`
  - `record UndoStep(string From, string To)`
  - `record UndoPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<UndoSkip> Skipped)`, `record UndoSkip(string Path, string Reason)`
  - `static class DrawerUndo { const string MovedSince; const string ChangedSince; UndoPlan Plan(JournalEntry e, Func<string, DateTime?> writtenUtc, Func<string, bool> exists); }`

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerJournalTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerJournalTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Written = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static JournalEntry Entry(string id, DrawerPlanSource source, DateTime at, params JournalMove[] moves) =>
        new(id, at, source, source == DrawerPlanSource.Agent ? "codex" : null, moves);

    private static JournalMove Move(string name) =>
        new($@"C:\Users\pat\Desktop\{name}", $@"C:\Users\pat\Drawers\Documents\{name}", false, Written);

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var j = new DrawerJournal()
            .Add(Entry("p1", DrawerPlanSource.Agent, Now, Move("a.pdf"), Move("b.pdf")), Now)
            .Add(Entry("u1", DrawerPlanSource.User, Now, new JournalMove(@"C:\Users\pat\Desktop\Proj", @"C:\Users\pat\Drawers\Work\Proj", true, null)), Now);
        var back = DrawerJournal.Decode(j.Encode());
        Assert.Equal(j.Entries.Count, back.Entries.Count);
        Assert.Equal(j.Entries[0].Moves, back.Entries[0].Moves);
        Assert.Equal("codex", back.Entries[0].Agent);
        Assert.Equal(DrawerPlanSource.User, back.Entries[1].Source);
        Assert.Null(back.Entries[1].Moves[0].WrittenUtc);
        Assert.True(back.Entries[1].Moves[0].IsFolder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"entries\": 5}")]
    [InlineData("{\"entries\": [{\"id\": \"x\"}]}")]
    public void Damaged_journals_read_as_empty_or_keep_what_is_whole(string? json) =>
        Assert.Empty(DrawerJournal.Decode(json).Entries);

    [Fact]
    public void Only_the_last_fifty_plans_are_kept()
    {
        var j = new DrawerJournal();
        for (var i = 0; i < 55; i++) j = j.Add(Entry($"p{i}", DrawerPlanSource.Rules, Now.AddMinutes(i), Move($"{i}.pdf")), Now.AddMinutes(i));
        Assert.Equal(DrawerJournal.MaxPlans, j.Entries.Count);
        Assert.Equal("p5", j.Entries[0].Id);
    }

    [Fact]
    public void User_moves_are_kept_thirty_days_and_do_not_count_toward_the_fifty()
    {
        var j = new DrawerJournal()
            .Add(Entry("old", DrawerPlanSource.User, Now.AddDays(-31), Move("o.pdf")), Now.AddDays(-31))
            .Add(Entry("recent", DrawerPlanSource.User, Now.AddDays(-2), Move("r.pdf")), Now.AddDays(-2));
        for (var i = 0; i < 50; i++) j = j.Add(Entry($"p{i}", DrawerPlanSource.Rules, Now, Move($"{i}.pdf")), Now);
        Assert.DoesNotContain(j.Entries, e => e.Id == "old");
        Assert.Contains(j.Entries, e => e.Id == "recent");
        Assert.Equal(50, j.Entries.Count(e => e.Source != DrawerPlanSource.User));
    }

    [Fact]
    public void The_summary_carries_counts_and_never_paths()
    {
        var j = new DrawerJournal().Add(Entry("p1", DrawerPlanSource.Agent, Now, Move("a.pdf"), Move("b.pdf")), Now);
        var line = Assert.Single(j.Summary());
        Assert.Equal(2, line.Files);
        Assert.Equal("codex", line.Agent);
        Assert.DoesNotContain(typeof(JournalSummaryLine).GetProperties(), p => p.PropertyType == typeof(string) && p.Name != nameof(JournalSummaryLine.Agent));
    }

    [Fact]
    public void Undo_puts_unchanged_files_back_in_reverse_order()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("a.pdf"), Move("b.pdf"));
        var undo = DrawerUndo.Plan(e, _ => Written, _ => false);
        Assert.Empty(undo.Skipped);
        Assert.Equal(
            [new UndoStep(@"C:\Users\pat\Drawers\Documents\b.pdf", @"C:\Users\pat\Desktop\b.pdf"),
             new UndoStep(@"C:\Users\pat\Drawers\Documents\a.pdf", @"C:\Users\pat\Desktop\a.pdf")],
            undo.Steps);
    }

    [Fact]
    public void Undo_skips_a_file_moved_or_changed_since()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("gone.pdf"), Move("edited.pdf"));
        var undo = DrawerUndo.Plan(e, p => p.EndsWith("edited.pdf") ? Written.AddHours(1) : null, _ => false);
        Assert.Empty(undo.Steps);
        Assert.Equal([DrawerUndo.ChangedSince, DrawerUndo.MovedSince], undo.Skipped.Select(s => s.Reason));
    }

    [Fact]
    public void Undo_never_overwrites_what_took_the_old_place()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("a.pdf"));
        var undo = DrawerUndo.Plan(e, _ => Written, p => p == @"C:\Users\pat\Desktop\a.pdf");
        Assert.Equal(@"C:\Users\pat\Desktop\a (2).pdf", Assert.Single(undo.Steps).To);
    }

    [Fact]
    public void Undo_moves_a_folder_back_without_comparing_its_time()
    {
        var e = Entry("u1", DrawerPlanSource.User, Now, new JournalMove(@"C:\Users\pat\Desktop\Proj", @"C:\Users\pat\Drawers\Work\Proj", true, null));
        var undo = DrawerUndo.Plan(e, _ => Written.AddDays(3), _ => false);
        Assert.Single(undo.Steps);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerJournalTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The type or namespace name 'DrawerJournal' could not be found`.

- [ ] **Step 3: Write the journal**

`windows-dotnet/src/Sanduhr.Core/DrawerJournal.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One move as it landed. <see cref="WrittenUtc"/> is the file's last write when it moved, so undo can tell it changed since;
/// a folder carries null, since its time changes with whatever is put in it.</summary>
public sealed record JournalMove(string From, string To, bool IsFolder, DateTime? WrittenUtc);

/// <summary>One applied plan, or one hand move by the user, as a single undoable entry.</summary>
public sealed record JournalEntry(string Id, DateTime AtUtc, DrawerPlanSource Source, string? Agent, IReadOnlyList<JournalMove> Moves);

/// <summary>What the MCP may read of the journal: when, who and how many. Never a path (5.0 drawers spec, section 4).</summary>
public sealed record JournalSummaryLine(DateTime AtUtc, DrawerPlanSource Source, string? Agent, int Files);

/// <summary>
/// The move journal (5.0 drawers spec, sections 1 and 4): paths only, never contents. Keeps the last 50 plans (rules and agents) and
/// the user's own moves for 30 days. Immutable: <see cref="Add"/> returns the trimmed journal to save.
/// </summary>
public sealed class DrawerJournal
{
    public const int MaxPlans = 50;
    public static readonly TimeSpan KeepUserMoves = TimeSpan.FromDays(30);

    public IReadOnlyList<JournalEntry> Entries { get; }

    public DrawerJournal() : this([]) { }

    private DrawerJournal(IReadOnlyList<JournalEntry> entries) => Entries = entries;

    public DrawerJournal Add(JournalEntry entry, DateTime nowUtc)
    {
        var all = Entries.Append(entry)
            .Where(e => e.Source != DrawerPlanSource.User || nowUtc - e.AtUtc <= KeepUserMoves)
            .ToList();
        var plans = all.Where(e => e.Source != DrawerPlanSource.User).ToList();
        foreach (var drop in plans.Take(Math.Max(0, plans.Count - MaxPlans))) all.Remove(drop);
        return new DrawerJournal(all);
    }

    public IReadOnlyList<JournalSummaryLine> Summary() =>
        Entries.Select(e => new JournalSummaryLine(e.AtUtc, e.Source, e.Agent, e.Moves.Count)).ToArray();

    public string Encode()
    {
        var entries = new JsonArray();
        foreach (var e in Entries)
        {
            var moves = new JsonArray();
            foreach (var m in e.Moves)
                moves.Add(new JsonObject
                {
                    ["from"] = m.From,
                    ["to"] = m.To,
                    ["folder"] = m.IsFolder,
                    ["written"] = m.WrittenUtc?.ToString("O", CultureInfo.InvariantCulture),
                });
            entries.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["at"] = e.AtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["source"] = e.Source.ToString().ToLowerInvariant(),
                ["agent"] = e.Agent,
                ["moves"] = moves,
            });
        }
        return new JsonObject { ["entries"] = entries }.ToJsonString();
    }

    /// <summary>Reads a saved journal. Anything unreadable is dropped entry by entry; a file that is not a journal reads as empty.</summary>
    public static DrawerJournal Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DrawerJournal();
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return new DrawerJournal(); }
        if (root?["entries"] is not JsonArray arr) return new DrawerJournal();
        var entries = new List<JournalEntry>();
        foreach (var n in arr)
        {
            if (n is not JsonObject o) continue;
            if (Str(o, "id") is not { } id || Time(o, "at") is not { } at || o["moves"] is not JsonArray ms) continue;
            if (!Enum.TryParse<DrawerPlanSource>(Str(o, "source"), ignoreCase: true, out var source)) continue;
            var moves = new List<JournalMove>();
            foreach (var mn in ms)
                if (mn is JsonObject m && Str(m, "from") is { } from && Str(m, "to") is { } to)
                    moves.Add(new JournalMove(from, to, SettingsJson.Bool(m, "folder", false), Time(m, "written")));
            if (moves.Count > 0) entries.Add(new JournalEntry(id, at, source, Str(o, "agent"), moves));
        }
        return new DrawerJournal(entries);
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static DateTime? Time(JsonObject o, string key) =>
        Str(o, key) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;
}

/// <summary>A step back, or a file undo leaves where it is and why.</summary>
public sealed record UndoStep(string From, string To);
public sealed record UndoSkip(string Path, string Reason);
public sealed record UndoPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<UndoSkip> Skipped);

/// <summary>Undo for one journal entry (5.0 drawers spec, section 1): every file back where it came from, newest move first. A file
/// that is gone or changed since is skipped and named; a taken old place gets a clash name, so undo never overwrites.</summary>
public static class DrawerUndo
{
    public const string MovedSince = "it was moved since.";
    public const string ChangedSince = "it changed since.";

    public static UndoPlan Plan(JournalEntry entry, Func<string, DateTime?> writtenUtc, Func<string, bool> exists)
    {
        var steps = new List<UndoStep>();
        var skipped = new List<UndoSkip>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Enumerable.Reverse by name: on .NET 10 an array's .Reverse() can bind to the in-place span overload.
        foreach (var m in Enumerable.Reverse(entry.Moves))
        {
            var now = writtenUtc(m.To);
            if (now is null) { skipped.Add(new UndoSkip(m.To, MovedSince)); continue; }
            if (!m.IsFolder && m.WrittenUtc is { } was && now != was) { skipped.Add(new UndoSkip(m.To, ChangedSince)); continue; }
            var dir = Path.GetDirectoryName(m.From)!;
            var back = DrawerNames.Free(dir, Path.GetFileName(m.From), m.IsFolder, p => exists(p) || claimed.Contains(p));
            claimed.Add(back);
            steps.Add(new UndoStep(m.To, back));
        }
        return new UndoPlan(steps, skipped);
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerJournalTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerJournal.cs windows-dotnet/tests/Sanduhr.Tests/DrawerJournalTests.cs
git commit -m "feat(drawers): move journal with plan and user-move trims, and undo that never overwrites

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Turning drawers off

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerReturn.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerReturnTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerDef`, `DrawerNames.Free`; Task 2's `DrawerPaths`; Task 4's `UndoStep`.
- Produces:
  - `record ReturnPlan(IReadOnlyList<UndoStep> Moves, IReadOnlyList<string> HomesToRemove)`
  - `static class DrawerReturn { ReturnPlan Plan(IReadOnlyList<DrawerDef> drawers, string drawersRoot, string desktop, Func<string, IEnumerable<DesktopItem>> childrenOf, Func<string, bool> exists); string Prompt(int count); }`

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerReturnTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerReturnTests
{
    private const string Root = @"C:\Users\pat\Drawers";
    private const string Desktop = @"C:\Users\pat\Desktop";
    private static readonly DateTime T = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DrawerDef Shots = new("screenshots", "Screenshots", $@"{Root}\Screenshots", true);
    private static readonly DrawerDef Docs = new("documents", "Documents", $@"{Root}\Documents", true);
    private static readonly DrawerDef Downloads = new("downloads", "Downloads", @"C:\Users\pat\Downloads", false);

    private static readonly Dictionary<string, DesktopItem[]> Tree = new(StringComparer.OrdinalIgnoreCase)
    {
        [$@"{Root}\Screenshots"] = [new($@"{Root}\Screenshots\a.txt", false, T), new($@"{Root}\Screenshots\Trip", true, T)],
        [$@"{Root}\Documents"] = [new($@"{Root}\Documents\a.txt", false, T)],
        [@"C:\Users\pat\Downloads"] = [new(@"C:\Users\pat\Downloads\big.iso", false, T)],
    };

    private static IEnumerable<DesktopItem> Children(string dir) => Tree.TryGetValue(dir, out var items) ? items : [];

    [Fact]
    public void Owned_homes_empty_onto_the_Desktop_files_and_folders_alike()
    {
        var plan = DrawerReturn.Plan([Shots], Root, Desktop, Children, _ => false);
        Assert.Equal(
            [new UndoStep($@"{Root}\Screenshots\a.txt", $@"{Desktop}\a.txt"), new UndoStep($@"{Root}\Screenshots\Trip", $@"{Desktop}\Trip")],
            plan.Moves);
        Assert.Equal([$@"{Root}\Screenshots", Root], plan.HomesToRemove);
    }

    [Fact]
    public void The_root_goes_last_so_it_is_removed_only_after_its_homes() =>
        Assert.Equal(Root, DrawerReturn.Plan([Shots, Docs], Root, Desktop, Children, _ => false).HomesToRemove[^1]);

    [Fact]
    public void A_view_drawer_keeps_its_contents_where_they_are()
    {
        var plan = DrawerReturn.Plan([Downloads], Root, Desktop, Children, _ => false);
        Assert.Empty(plan.Moves);
        Assert.Empty(plan.HomesToRemove);
    }

    [Fact]
    public void Same_names_from_two_drawers_and_the_Desktop_never_overwrite()
    {
        var plan = DrawerReturn.Plan([Shots, Docs], Root, Desktop, Children, p => p == $@"{Desktop}\a.txt");
        Assert.Equal(
            [$@"{Desktop}\a (2).txt", $@"{Desktop}\Trip", $@"{Desktop}\a (3).txt"],
            plan.Moves.Select(m => m.To));
    }

    [Fact]
    public void An_owned_flag_on_a_folder_outside_the_root_is_not_trusted()
    {
        var odd = new DrawerDef("odd", "Odd", @"C:\Users\pat\Documents\Odd", true);
        var plan = DrawerReturn.Plan([odd], Root, Desktop, _ => [new(@"C:\Users\pat\Documents\Odd\x.txt", false, T)], _ => false);
        Assert.Empty(plan.Moves);
        Assert.Empty(plan.HomesToRemove);
    }

    [Theory]
    [InlineData(1, "Put 1 file back on the Desktop?")]
    [InlineData(42, "Put 42 files back on the Desktop?")]
    public void The_prompt_counts_what_comes_back(int n, string text) =>
        Assert.Equal(text, DrawerReturn.Prompt(n));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerReturnTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The name 'DrawerReturn' does not exist in the current context`.

- [ ] **Step 3: Write the return plan**

`windows-dotnet/src/Sanduhr.Core/DrawerReturn.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>What turning drawers off does: the moves back to the Desktop, and the owned homes (then the root) to remove once each is empty.</summary>
public sealed record ReturnPlan(IReadOnlyList<UndoStep> Moves, IReadOnlyList<string> HomesToRemove);

/// <summary>
/// Turning drawers off puts everything back (5.0 drawers spec, section 4): every home Sanduhr owns empties onto the Desktop, files and
/// folders alike, whether the journal moved them or the user dragged them in. A home counts as owned only when it is flagged owned and
/// sits directly in the Drawers root. A view drawer (Downloads, a project folder) keeps its contents where they are. The App removes a
/// home only once it is empty, so a file that could not move keeps its home.
/// </summary>
public static class DrawerReturn
{
    public static ReturnPlan Plan(IReadOnlyList<DrawerDef> drawers, string drawersRoot, string desktop,
        Func<string, IEnumerable<DesktopItem>> childrenOf, Func<string, bool> exists)
    {
        var root = DrawerPaths.Normalize(drawersRoot);
        var desk = DrawerPaths.Normalize(desktop);
        var moves = new List<UndoStep>();
        var homes = new List<string>();
        if (root is null || desk is null) return new ReturnPlan(moves, homes);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in drawers)
        {
            var home = DrawerPaths.Normalize(d.Home);
            if (!d.Owned || home is null || !DrawerPaths.IsChildOf(home, root)) continue;
            foreach (var item in childrenOf(home).OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var to = DrawerNames.Free(desk, Path.GetFileName(item.Path), item.IsFolder, p => exists(p) || claimed.Contains(p));
                claimed.Add(to);
                moves.Add(new UndoStep(item.Path, to));
            }
            homes.Add(home);
        }
        // The root last: the App removes each folder only once it is empty, so a root holding anything else stays.
        if (homes.Count > 0) homes.Add(root);
        return new ReturnPlan(moves, homes);
    }

    /// <summary>The confirmation's question, with the count.</summary>
    public static string Prompt(int count) => count == 1 ? "Put 1 file back on the Desktop?" : $"Put {count} files back on the Desktop?";
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerReturnTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerReturn.cs windows-dotnet/tests/Sanduhr.Tests/DrawerReturnTests.cs
git commit -m "feat(drawers): turning drawers off returns every owned home to the Desktop

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Drawer settings and paths

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/Paths.cs` (add two properties after `CodenamesFile`, around line 173)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerSettingsTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerDef`, `DrawerIds`, `DrawerNames`; Task 3's `DrawerRule`, `DrawerRuleKind`, `DrawerRules.Defaults`; `SettingsJson` (in `DeskContentSettings.cs`).
- Produces:
  - `record DrawerSettings(bool Enabled, string? Root, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerRule> Rules, bool AutoTidy, bool AutoTidyUnlocked)` with `const string Key = "drawers"`, `static DrawerSettings Default`, `static DrawerSettings Decode(JsonNode?)`, `JsonObject Encode()`, `static string DefaultRoot(string desktop)` (`<desktop>\Drawers`), `string RootOr(string desktop)`.
  - `Paths.DrawerJournalFile` (`<AppDataDir>\drawers-journal.json`). The root is not on `Paths`: it hangs off the Desktop, which the App reads from `Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)` (it may be redirected, for example into OneDrive), so plan 2 passes that path in.

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerSettingsTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerSettingsTests
{
    [Fact]
    public void A_missing_key_is_off_with_the_default_rules()
    {
        var s = DrawerSettings.Decode(null);
        Assert.False(s.Enabled);
        Assert.False(s.AutoTidy);
        Assert.False(s.AutoTidyUnlocked);
        Assert.Null(s.Root);
        Assert.Empty(s.Drawers);
        Assert.Equal(DrawerRules.Defaults, s.Rules);
    }

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var s = DrawerSettings.Default with
        {
            Enabled = true,
            Root = @"D:\Sorted",
            Drawers = [new DrawerDef("screenshots", "Screenshots", @"D:\Sorted\Screenshots", true), new DrawerDef("downloads", "Downloads", @"C:\Users\pat\Downloads", false)],
            Rules = [new DrawerRule(DrawerRuleKind.NameContains, "screenshots", Text: "shot"), new DrawerRule(DrawerRuleKind.OlderThan, "screenshots", On: false, Days: 7)],
            AutoTidy = true,
            AutoTidyUnlocked = true,
        };
        var back = DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal(s.Enabled, back.Enabled);
        Assert.Equal(s.Root, back.Root);
        Assert.Equal(s.Drawers, back.Drawers);
        Assert.Equal(s.Rules, back.Rules);
        Assert.True(back.AutoTidy);
        Assert.True(back.AutoTidyUnlocked);
    }

    [Fact]
    public void Auto_tidy_stays_off_until_unlocked()
    {
        var json = JsonNode.Parse("""{"enabled": true, "auto_tidy": true, "auto_tidy_unlocked": false}""");
        Assert.False(DrawerSettings.Decode(json).AutoTidy);
    }

    [Fact]
    public void Damaged_entries_are_dropped_one_by_one()
    {
        var json = JsonNode.Parse("""
        {
          "enabled": true,
          "drawers": [
            {"id": "ok", "name": "OK", "home": "C:\\Users\\pat\\Drawers\\OK", "owned": true},
            {"id": "Bad Id", "name": "X", "home": "C:\\x", "owned": true},
            {"id": "con", "name": "CON", "home": "C:\\x", "owned": true},
            {"id": "nohome", "name": "No home"},
            "not an object"
          ],
          "rules": [
            {"kind": "images", "drawer": "ok"},
            {"kind": "teleport", "drawer": "ok"},
            {"kind": "older_than", "drawer": "ok", "days": -4},
            {"kind": "documents"}
          ]
        }
        """);
        var s = DrawerSettings.Decode(json);
        Assert.Equal(["ok"], s.Drawers.Select(d => d.Id));
        Assert.Equal([DrawerRuleKind.Images, DrawerRuleKind.OlderThan], s.Rules.Select(r => r.Kind));
        Assert.Equal(1, s.Rules[1].Days);
    }

    [Fact]
    public void An_empty_rules_array_means_the_user_deleted_them_all()
    {
        var s = DrawerSettings.Decode(JsonNode.Parse("""{"rules": []}"""));
        Assert.Empty(s.Rules);
    }

    [Fact]
    public void The_default_root_is_a_folder_on_the_Desktop() =>
        Assert.Equal(@"C:\Users\pat\OneDrive\Desktop\Drawers", DrawerSettings.DefaultRoot(@"C:\Users\pat\OneDrive\Desktop"));

    [Fact]
    public void RootOr_falls_back_to_the_Desktop_default()
    {
        Assert.Equal(@"C:\Users\pat\Desktop\Drawers", DrawerSettings.Default.RootOr(@"C:\Users\pat\Desktop"));
        Assert.Equal(@"D:\Sorted", (DrawerSettings.Default with { Root = @"D:\Sorted" }).RootOr(@"C:\Users\pat\Desktop"));
    }

    [Fact]
    public void Paths_keeps_the_journal_with_the_settings()
    {
        var p = new Paths(appDataBase: @"C:\t\appdata", homeDir: @"C:\t\home", localAppDataBase: @"C:\t\local");
        Assert.Equal(Path.Combine(p.AppDataDir, "drawers-journal.json"), p.DrawerJournalFile);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerSettingsTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The type or namespace name 'DrawerSettings' could not be found`.

- [ ] **Step 3: Write the settings**

`windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Drawers' settings (5.0 drawers spec), settings.json <c>drawers</c>: on or off, the Drawers root when the user moved it, the drawers,
/// the rules in order, and auto-tidy. Auto-tidy reads as off until a rule plan has been accepted once (<see cref="AutoTidyUnlocked"/>).
/// A missing key is off with the built-in rules, so an upgrade changes nothing.
/// </summary>
public sealed record DrawerSettings(bool Enabled, string? Root, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerRule> Rules,
    bool AutoTidy, bool AutoTidyUnlocked)
{
    public const string Key = "drawers";

    public static DrawerSettings Default { get; } = new(false, null, [], DrawerRules.Defaults, false, false);

    /// <summary>The default root (decision KNT1mDtN3kQ0rlz2sePq): a Drawers folder on the Desktop itself, so after any uninstall,
    /// the Store's included, the files sit in one visible folder; and a synced Desktop keeps them synced.</summary>
    public static string DefaultRoot(string desktop) => Path.Combine(desktop, "Drawers");

    /// <summary>The Drawers root in force: the user's, or the Desktop default.</summary>
    public string RootOr(string desktop) => string.IsNullOrWhiteSpace(Root) ? DefaultRoot(desktop) : Root;

    public static DrawerSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        bool unlocked = SettingsJson.Bool(o, "auto_tidy_unlocked", false);
        var root = SettingsJson.String(o, "root", "");
        return new DrawerSettings(
            SettingsJson.Bool(o, "enabled", false),
            root.Length == 0 ? null : root,
            o["drawers"] is JsonArray ds ? ds.OfType<JsonObject>().Select(DecodeDrawer).OfType<DrawerDef>().ToArray() : [],
            o["rules"] is JsonArray rs ? rs.OfType<JsonObject>().Select(DecodeRule).OfType<DrawerRule>().ToArray() : DrawerRules.Defaults,
            unlocked && SettingsJson.Bool(o, "auto_tidy", false),
            unlocked);
    }

    public JsonObject Encode() => new()
    {
        ["enabled"] = Enabled,
        ["root"] = Root ?? "",
        ["drawers"] = new JsonArray([.. Drawers.Select(d => (JsonNode?)new JsonObject
        {
            ["id"] = d.Id, ["name"] = d.Name, ["home"] = d.Home, ["owned"] = d.Owned,
        })]),
        ["rules"] = new JsonArray([.. Rules.Select(r => (JsonNode?)new JsonObject
        {
            ["kind"] = KindWord(r.Kind), ["drawer"] = r.DrawerId, ["on"] = r.On, ["text"] = r.Text ?? "", ["days"] = r.Days,
        })]),
        ["auto_tidy"] = AutoTidy,
        ["auto_tidy_unlocked"] = AutoTidyUnlocked,
    };

    private static DrawerDef? DecodeDrawer(JsonObject o)
    {
        var id = SettingsJson.String(o, "id", "");
        var name = SettingsJson.String(o, "name", "");
        var home = SettingsJson.String(o, "home", "");
        if (!DrawerIds.IsValid(id) || !DrawerNames.IsValid(name) || home.Length == 0) return null;
        return new DrawerDef(id, name, home, SettingsJson.Bool(o, "owned", false));
    }

    private static DrawerRule? DecodeRule(JsonObject o)
    {
        if (ParseKind(SettingsJson.String(o, "kind", "")) is not { } kind) return null;
        var drawer = SettingsJson.String(o, "drawer", "");
        if (!DrawerIds.IsValid(drawer)) return null;
        var days = SettingsJson.Number(o, "days", 30);
        var text = SettingsJson.String(o, "text", "");
        return new DrawerRule(kind, drawer, SettingsJson.Bool(o, "on", true), text.Length == 0 ? null : text,
            double.IsFinite(days) ? (int)Math.Clamp(Math.Round(days), 1, 3650) : 30);
    }

    /// <summary>snake_case words, so the file reads like the rest of settings.json.</summary>
    private static string KindWord(DrawerRuleKind k) => k switch
    {
        DrawerRuleKind.OlderThan => "older_than",
        DrawerRuleKind.NameContains => "name_contains",
        DrawerRuleKind.NameStartsWith => "name_starts_with",
        _ => k.ToString().ToLowerInvariant(),
    };

    private static DrawerRuleKind? ParseKind(string word) =>
        Enum.GetValues<DrawerRuleKind>().Cast<DrawerRuleKind?>().FirstOrDefault(k => KindWord(k!.Value) == word);
}
```

- [ ] **Step 4: Add the journal path**

In `windows-dotnet/src/Sanduhr.Core/Paths.cs`, after the `CodenamesFile` property:

```csharp
    /// <summary>The move journal: paths only, beside settings.json, apart from the Velopack install tree.</summary>
    public string DrawerJournalFile => Path.Combine(AppDataDir, "drawers-journal.json");
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerSettingsTests" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs windows-dotnet/src/Sanduhr.Core/Paths.cs windows-dotnet/tests/Sanduhr.Tests/DrawerSettingsTests.cs
git commit -m "feat(drawers): settings with entry-by-entry decoding, the Drawers root and the journal path

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The `drawer.<id>` layout word

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerPieces.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs` (the parse guard near line 317, `DefaultAnchor` near line 354)
- Modify: `docs/superpowers/specs/2026-10-28-drawers-design.md` (section 2, the layout word)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerPiecesTests.cs`

**Interfaces:**
- Consumes: Task 1's `DrawerIds.IsValid`; `DeskArrangement`, `DeskPlacement`, `DeskAnchor`.
- Produces: `static class DrawerPieces { const string Prefix = "drawer."; bool IsWord(string? widget); string Word(string id); string? IdOf(string widget); }`. `DeskArrangement` keeps drawer pieces through parse and `Layout`; their default anchor is top left.

- [ ] **Step 1: Write the failing tests**

`windows-dotnet/tests/Sanduhr.Tests/DrawerPiecesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerPiecesTests
{
    [Theory]
    [InlineData("drawer.screenshots", true)]
    [InlineData("drawer.older-files", true)]
    [InlineData("drawer.", false)]
    [InlineData("drawer.Bad", false)]
    [InlineData("drawers.x", false)]
    [InlineData("clock", false)]
    [InlineData(null, false)]
    public void A_drawer_word_is_the_prefix_and_a_valid_id(string? widget, bool ok) =>
        Assert.Equal(ok, DrawerPieces.IsWord(widget));

    [Fact]
    public void Word_and_IdOf_round_trip()
    {
        Assert.Equal("drawer.screenshots", DrawerPieces.Word("screenshots"));
        Assert.Equal("screenshots", DrawerPieces.IdOf("drawer.screenshots"));
        Assert.Null(DrawerPieces.IdOf("clock"));
    }

    [Fact]
    public void A_layout_keeps_drawer_pieces_beside_the_others()
    {
        var a = new DeskArrangement("clock:bl drawer.screenshots:tl:1.2 drawer.documents:tl");
        Assert.Equal(["clock", "drawer.screenshots", "drawer.documents"], a.Pieces.Select(p => p.Widget));
        Assert.Equal(1.2, a.Pieces[1].Scale);
        Assert.Equal("clock:bl drawer.screenshots:tl:1.2 drawer.documents:tl", a.Layout);
    }

    [Fact]
    public void A_bad_drawer_word_is_dropped_like_any_unknown_word()
    {
        var a = new DeskArrangement("clock:bl drawer.Bad:tl drawer.:tl");
        Assert.Equal(["clock"], a.Pieces.Select(p => p.Widget));
    }

    [Fact]
    public void A_drawer_with_an_unreadable_anchor_lands_top_left() =>
        Assert.Equal(DeskAnchor.TL, new DeskArrangement("drawer.screenshots:zz").Pieces.Single().Anchor);

    [Fact]
    public void Drawer_pieces_sort_after_every_known_widget() =>
        Assert.Equal(DeskArrangement.Known.Count, DeskArrangement.Rank("drawer.screenshots"));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerPiecesTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL, `The name 'DrawerPieces' does not exist in the current context`.

- [ ] **Step 3: Write `DrawerPieces`**

`windows-dotnet/src/Sanduhr.Core/DrawerPieces.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>A drawer's layout word (5.0 drawers spec, section 2): <c>drawer.&lt;id&gt;</c> in the widget slot, so
/// <c>drawer.screenshots:tl:1</c> keeps the grammar's widget:anchor:scale:tilt order. Drawer ids hold no colon, at-sign or space.</summary>
public static class DrawerPieces
{
    public const string Prefix = "drawer.";

    public static bool IsWord(string? widget) =>
        widget is not null && widget.StartsWith(Prefix, StringComparison.Ordinal) && DrawerIds.IsValid(widget[Prefix.Length..]);

    public static string Word(string id) => Prefix + id;

    public static string? IdOf(string widget) => IsWord(widget) ? widget[Prefix.Length..] : null;
}
```

- [ ] **Step 4: Let `DeskArrangement` keep drawer pieces**

In `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs`, in the `DeskArrangement(string? layout)` constructor, replace:

```csharp
            if (bits.Length < 2 || !Known.Contains(bits[0], StringComparer.Ordinal)) continue;
```

with:

```csharp
            // A drawer piece (5.0) is drawer.<id>, any number of them; every other word must be one this build knows.
            if (bits.Length < 2 || !(Known.Contains(bits[0], StringComparer.Ordinal) || DrawerPieces.IsWord(bits[0]))) continue;
```

In `DefaultAnchor`, add the drawer arm before the final `_`:

```csharp
        "companion" => DeskAnchor.BR,
        _ when DrawerPieces.IsWord(widget) => DeskAnchor.TL,
        _ => DeskAnchor.BL,
```

- [ ] **Step 5: Correct the spec's layout word**

In `docs/superpowers/specs/2026-10-28-drawers-design.md`, section 2, replace:

```markdown
**A Desk piece.** A drawer uses the existing layout grammar: `drawer:<name>:<place>:<scale>[:tilt]`, for
example `drawer:Screenshots:tl:1`.
```

with:

```markdown
**A Desk piece.** A drawer uses the existing layout grammar with its id in the widget slot:
`drawer.<id>:<place>:<scale>[:tilt]`, for example `drawer.screenshots:tl:1`. (`drawer:<name>` would have put
the name where the grammar reads the anchor.)
```

- [ ] **Step 6: Run the drawer tests and the existing layout tests**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerPiecesTests|FullyQualifiedName~DeskArrangement" --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed. The existing `DeskArrangementTests` must stay green: the change only admits words that were dropped before.

- [ ] **Step 7: Run the whole Core suite**

Run: `dotnet test tests/Sanduhr.Tests --disable-build-servers -nodeReuse:false`
Expected: PASS, 0 failed. If a focus test flakes because a window holds the foreground, re-run once (a known flake).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerPieces.cs windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs windows-dotnet/tests/Sanduhr.Tests/DrawerPiecesTests.cs docs/superpowers/specs/2026-10-28-drawers-design.md
git commit -m "feat(drawers): drawer.<id> layout word kept through parse and layout

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## What plan 2 covers (written after the spike's verdict)

The drawer island and its theming from the Desk look; moves through `IFileOperation` with the per-move root and reparse
check; the journal on disk; review on the Desk with `DeskReviewBar` and `DeskBusy`; undo, turning off and Reset's Drawers
part; the same return from Velopack's `OnBeforeUninstallFastCallback` in `Sanduhr.App/Program.cs` (Setup.exe only, best-effort);
the first-on line "Drawer files live in the Drawers folder on your Desktop."; the three front doors on top of `JustTheDesk`; the What's New card; Settings pages, index and Privacy switch;
`propose_drawers` and `get_drawers` with the one new `TrustBoundaryTests` entry (the MCP links `DrawerModel.cs`,
`DrawerPlanValidator.cs` and `DrawerJournal.cs`' summary, which are IO-free by this plan's constraint); streamer-mode
codenames; and the RenderHarness `drawers` scenario.
