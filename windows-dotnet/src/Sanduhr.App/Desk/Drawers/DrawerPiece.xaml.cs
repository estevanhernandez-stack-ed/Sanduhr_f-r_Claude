using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>The drawer's own menu items under Explorer's (spec section 2).</summary>
internal static class DrawerPieceCopy
{
    public const string RenameDrawer = "Rename drawer";
    public const string Fold = "Fold";
    public const string Open = "Open";
    public const string Hide = "Hide";
    /// <summary>Opens the Drawers page: "Drawers settings…", a page name like every Settings entry point.</summary>
    public static readonly string Settings = SettingsNames.LinkTitle(SettingsPage.Drawers);
    public const string UndoLost = " Undo is not available for this move.";
}

/// <summary>
/// A drawer on the Desk (5.0 drawers spec, section 2): a themed frame with the drawer's name, and open, a grid of Shell icons and names
/// that scrolls inside it; folded, the title and a count; for a folder that is gone, one line. Its state lives in
/// <see cref="DrawerService"/>, so a redraw builds a new piece over the same <see cref="DrawerState"/>, selection included.
/// </summary>
internal sealed partial class DrawerPiece : UserControl, IDisposable
{
    public const double FrameWidth = 344;
    public const double ListHeight = 264;
    private readonly DrawerState _state;
    private readonly DrawerService _service;

    public DrawerPiece(DrawerState state, DrawerService service, DrawerLook look, double scale)
    {
        InitializeComponent();
        _state = state;
        _service = service;
        Look = look;
        Tag = state.Word;
        FontFamily = look.Font;
        Resources["Drawer.Plate"] = Brush(look.Plate);
        Resources["Drawer.Ink"] = Brush(look.Ink);
        Resources["Drawer.Accent"] = Brush(look.Accent);
        Resources["Drawer.Hover"] = Brush(Color.FromArgb(46, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.Selected"] = Brush(Color.FromArgb(90, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.LabelEffect"] = StreamerMode.On ? Blur() : look.Shadow;   // streamer: masked - file labels blur while streamer mode is on
        Resources["Drawer.Border"] = Brush(look.Border);
        Resources["Drawer.HoverBorder"] = Brush(look.HoverBorder);
        Resources["Drawer.Edge"] = Brush(look.Edge);
        Resources["Drawer.Chip"] = look.Chips ? Brush(Color.FromArgb(178, look.Plate.R, look.Plate.G, look.Plate.B)) : Brushes.Transparent;
        Resources["Drawer.ChipPadding"] = look.Chips ? new Thickness(3, 1, 3, 1) : new Thickness(0);
        Frame.Width = FrameWidth;
        List.Height = ListHeight;
        ApplyKind(look);
        if (Math.Abs(scale - 1) > 0.001) LayoutTransform = new ScaleTransform(scale, scale);
        List.ItemsSource = state.Items;
        foreach (var path in state.Selected)
            if (state.Items.FirstOrDefault(i => i.Path == path) is { } item) List.SelectedItems.Add(item);
        List.SelectionChanged += OnSelectionChanged;
        _state.PropertyChanged += OnStateChanged;
        _state.Items.CollectionChanged += OnItemsChanged;
        // The group name is bound to SpokenName, which carries the codename while on (DrawerState.Title masks it), so it follows the count.
        BindingOperations.SetBinding(this, AutomationProperties.NameProperty, new Binding(nameof(DrawerState.SpokenName)) { Source = _state });   // streamer: masked - SpokenName is built from Title, which passes Mask.Name
        Show();
        Wire();
    }

    public DrawerState State => _state;

    /// <summary>Told by the host once a Glass drawer's window has its blur: whether DWM rounded the window's corners. Where it did not
    /// (Windows 10), the blur fills a square window, so the panel's corners go square to match.</summary>
    internal void SetGlassCorners(bool rounded)
    {
        if (rounded) return;
        Frame.CornerRadius = new CornerRadius(0);
        EdgeLine.CornerRadius = new CornerRadius(0);
    }

    /// <summary>The look this piece was drawn with.</summary>
    internal DrawerLook Look { get; }

    /// <summary>What the look changes beyond brushes: Glass's corners match DWM's round corner (the blur is clipped to it; see
    /// <see cref="SetGlassCorners"/>); Outline and Bare draw a 1 px border, taken out of the padding so the piece is the same size in every
    /// look; Bare's title is smaller; Outline and Bare have no plate behind the title, so it carries the ink's shadow as the labels do; a
    /// chip on the title keeps its own padding in place of the title's margin.</summary>
    private void ApplyKind(DrawerLook look)
    {
        if (look.Kind == DrawerLookKind.Glass)
        {
            Frame.CornerRadius = new CornerRadius(8);
            EdgeLine.CornerRadius = new CornerRadius(8);
        }
        if (look.Kind is DrawerLookKind.Outline or DrawerLookKind.Bare)
        {
            Frame.BorderThickness = new Thickness(1);
            Frame.Padding = new Thickness(7);
        }
        if (look.Kind == DrawerLookKind.Bare) TitleText.FontSize = 11;
        if (look.Kind is DrawerLookKind.Outline or DrawerLookKind.Bare) TitleText.Effect = StreamerMode.On ? null : look.Shadow;
        if (look.Chips)
        {
            TitleChip.Margin = new Thickness(0, 0, 0, 0);
            TitleChip.Padding = new Thickness(6, 1, 6, 1);
        }
    }

    private void Show()
    {
        TitleText.Text = _state.Folded ? _state.FoldedTitle : _state.Title;
        AutomationProperties.SetName(TitleText, TitleText.Text);   // streamer: masked - Title and FoldedTitle carry the codename (Mask.Name in DrawerState) while streamer mode is on
        MissingText.Text = DrawerState.MissingLine;
        bool open = !_state.Folded;
        MissingText.Visibility = open && _state.Missing ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = open && !_state.Missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _state.Selected = [.. List.SelectedItems.OfType<DrawerItem>().Select(i => i.Path)];

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Show();

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    /// <summary>A drawer is a named group (spec section 2, the 10.1.4.4 rule): "Screenshots drawer, 14 items".</summary>
    private sealed class GroupPeer(DrawerPiece owner) : UserControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static BlurEffect Blur()
    {
        var b = new BlurEffect { Radius = 6 };
        b.Freeze();
        return b;
    }

    private const int RenameId = ShellMenu.OwnFirst, FoldId = ShellMenu.OwnFirst + 1, HideId = ShellMenu.OwnFirst + 2, SettingsId = ShellMenu.OwnFirst + 3;
    private Point _dragStart;
    private DrawerItem? _pressed;
    private DrawerItem? _editing;
    private bool _disposed;
    private IDisposable? _renameHold;
    private DispatcherTimer? _statusTimer;

    private void Wire()
    {
        PreviewMouseDown += (_, _) => DeskFocus.Take(List);
        TitleBar.MouseLeftButtonDown += OnTitleDown;
        TitleBar.MouseMove += (_, _) => { if (_title is not null) TitleMove(DeskNative.CursorPx()); };
        TitleBar.MouseLeftButtonUp += OnTitleUp;
        TitleBar.LostMouseCapture += (_, _) => { if (!_releasing) TitleLost(); };
        Frame.DragEnter += OnDragOver;
        Frame.DragOver += OnDragOver;
        Frame.Drop += OnDrop;
        List.PreviewMouseLeftButtonDown += (_, e) => Press(ItemAt(e.OriginalSource), e.GetPosition(this));
        List.PreviewMouseLeftButtonUp += (_, _) => _pressed = null;
        List.PreviewMouseMove += OnMaybeDrag;
        List.MouseDoubleClick += (_, e) => { if (ItemAt(e.OriginalSource) is { Pending: false } item) ShellFiles.Open(item.Path); };
        List.KeyDown += OnKey;
        MouseRightButtonUp += OnRightClick;
    }

    // ---- the title: a click folds, a drag moves (drawers amendment 1) ----

    /// <summary>A press on the title: the cursor, the window's top-left and the piece's offset inside its window, all in screen pixels.</summary>
    private (PxPoint Cursor, PxPoint Window, PxPoint Offset)? _title;
    private PxPoint _lastCursor;
    private bool _moving;
    private bool _releasing;
    private IDisposable? _moveHold;

    private void OnTitleDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var cursor = DeskNative.CursorPx();
        PxPoint window = default, offset = default;
        if (PresentationSource.FromVisual(this) is HwndSource { IsDisposed: false } src && DeskNative.RectPx(src.Handle) is { } r)
        {
            window = new PxPoint(r.X, r.Y);
            offset = OffsetIn(src);
        }
        TitleDown(cursor, window, offset);
        TitleBar.CaptureMouse();
    }

    private void OnTitleUp(object sender, MouseButtonEventArgs e)
    {
        if (_title is null) return;
        e.Handled = true;
        var at = DeskNative.CursorPx();
        // Capture goes first, so a redraw the place or fold sets off never finds this title still holding the mouse.
        _releasing = true;
        try { TitleBar.ReleaseMouseCapture(); }
        finally { _releasing = false; }
        TitleUp(at);
    }

    /// <summary>Where this piece's top-left sits inside its window, in the window's pixels (<see cref="DrawerScreens.PixelsPerUnit(HwndSource)"/>:
    /// the root's own layout scale counts, so a drop on a monitor at another scale than WPF's saves no offset error).</summary>
    private PxPoint OffsetIn(HwndSource src)
    {
        if (src.RootVisual is not Visual root || !root.IsAncestorOf(this)) return default;
        return DrawerScreens.RootToWindow(TransformToAncestor(root).Transform(new Point()), DrawerScreens.PixelsPerUnit(src));
    }

    internal void TitleDown(PxPoint cursor, PxPoint window, PxPoint offset)
    {
        EndMove();
        _title = (cursor, window, offset);
        _lastCursor = cursor;
    }

    /// <summary>The mouse moved with the title pressed: past the system's drag distance the drawer's own window follows it (no size, no
    /// z-order, no activation), and the service is held busy so no redraw rebuilds the drawer under the hand.</summary>
    internal void TitleMove(PxPoint cursor)
    {
        if (_title is not { } t || _disposed) return;
        _lastCursor = cursor;
        if (!_moving)
        {
            if (Math.Abs(cursor.X - t.Cursor.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(cursor.Y - t.Cursor.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _moving = true;
            _moveHold ??= _service.Hold();
        }
        if (Handle is var h && h != IntPtr.Zero) DeskNative.MoveTo(h, SnappedWindowAt(t, cursor));
    }

    /// <summary>Whether this move snaps: the setting is on and Alt is not held (Alt places the drawer exactly where the hand puts it).</summary>
    private bool Snaps => _service.Settings.Snap && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;

    /// <summary>The window's top-left for the cursor, pulled to the nearest snap when snapping: the Desk answers for the piece's top-left (the
    /// panel, not the window around it), so the offset of the piece inside its window is taken off again.</summary>
    private PxPoint SnappedWindowAt((PxPoint Cursor, PxPoint Window, PxPoint Offset) t, PxPoint cursor)
    {
        var w = WindowAt(t, cursor);
        if (!Snaps || _service.Snapper is not { } snapper) return w;
        var top = snapper(_state.Def.Id, new PxPoint(w.X + t.Offset.X, w.Y + t.Offset.Y));
        return new PxPoint(top.X - t.Offset.X, top.Y - t.Offset.Y);
    }

    /// <summary>The title was let go: after a move the Desk is asked to keep the drawer where it was dropped; without one, it folds or opens.</summary>
    internal void TitleUp(PxPoint cursor)
    {
        if (_title is not { } t) return;
        TitleMove(cursor);
        bool moved = _moving;
        if (moved) Place(t, cursor);
        else
        {
            EndMove();
            _service.SetFolded(_state.Def.Id, !_state.Folded);
        }
    }

    /// <summary>The title lost the mouse without a release (another window took it): a move in progress ends where it is; a press ends
    /// with nothing.</summary>
    internal void TitleLost()
    {
        if (_title is not { } t) return;
        if (_moving) Place(t, _lastCursor);
        else EndMove();
    }

    private void Place((PxPoint Cursor, PxPoint Window, PxPoint Offset) t, PxPoint cursor)
    {
        var w = WindowAt(t, cursor);
        var hold = _moveHold;
        _moveHold = null;
        EndMove();
        // The request lands while the hold still stands, so the Desk draws the drawer once, at its new place, when the hold is released.
        try { _service.RequestPlace(_state.Def.Id, new PxPoint(w.X + t.Offset.X, w.Y + t.Offset.Y), Snaps); }
        finally { hold?.Dispose(); }
    }

    private static PxPoint WindowAt((PxPoint Cursor, PxPoint Window, PxPoint Offset) t, PxPoint cursor) =>
        new(t.Window.X + cursor.X - t.Cursor.X, t.Window.Y + cursor.Y - t.Cursor.Y);

    private void EndMove()
    {
        _title = null;
        _moving = false;
        _moveHold?.Dispose();
        _moveHold = null;
    }

    // ---- drops ----

    internal static string[]? FilesOf(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;

    /// <summary>A drop this drawer takes: files from the Desktop or another drawer, not all already here (Review Focus 4).</summary>
    internal bool Accepts(IDataObject data) => FilesOf(data) is { Length: > 0 } files && _service.Accepts(files, _state.Def.Id);

    /// <summary>Moves the dropped files in and says what was skipped or cannot be undone.</summary>
    internal void Land(IReadOnlyList<string> files)
    {
        var run = _service.MoveIn(files, _state.Def.Id);
        if (run.Skipped.Count > 0 || run.UndoLost) Say(run);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Accepts(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Accepts(e.Data) || FilesOf(e.Data) is not { } files) { e.Effects = DragDropEffects.None; return; }
        Land(files);
        // An optimized move (Shell, "Handling Optimized Move Operations"): this drawer moved the files itself, so the source is told
        // the logical effect was a move and the performed effect nothing, and never deletes anything of its own.
        e.Effects = DragDropEffects.None;
        try
        {
            e.Data.SetData("Logical Performed DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
            e.Data.SetData("Performed DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.None)));
        }
        catch (Exception x) when (x is COMException or NotSupportedException or InvalidOperationException) { /* a source that takes no answer */ }
    }

    // ---- drags out ----

    /// <summary>A press in the list: a drag may start only from a press that began on a file or folder (not the scrollbar or empty space).</summary>
    internal void Press(DrawerItem? onItem, Point at)
    {
        _dragStart = at;
        _pressed = onItem is { Pending: false, Editing: false } ? onItem : null;
    }

    /// <summary>Whether the pointer, now at <paramref name="at"/>, has moved far enough from a press on an item to start a drag.</summary>
    internal bool CanDrag(Point at)
    {
        if (_pressed is null) return false;
        var d = at - _dragStart;
        return Math.Abs(d.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(d.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void OnMaybeDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _pressed = null; return; }
        if (!CanDrag(e.GetPosition(this))) return;
        _pressed = null;
        var paths = List.SelectedItems.OfType<DrawerItem>().Where(i => !i.Pending && !i.Editing).Select(i => i.Path).ToArray();
        if (paths.Length == 0) return;
        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
        using (_service.Hold())
            // The result is never acted on (VERDICT.md): the target moved the files, or the drop landed back here and did nothing.
            DragDrop.DoDragDrop(List, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // ---- keys ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (List.SelectedItem is not DrawerItem { Pending: false, Editing: false } item) return;
        switch (e.Key)
        {
            case Key.Enter: ShellFiles.Open(item.Path); e.Handled = true; break;
            case Key.F2: BeginRename(item); e.Handled = true; break;
            case Key.Delete: ShellMenu.InvokeVerb(Handle, item.Path, "delete"); e.Handled = true; break;   // Explorer's own delete: the Recycle Bin, its confirmation
        }
    }

    // ---- the menu ----

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var item = ItemAt(e.OriginalSource);
        if (item is { Pending: false } && !List.SelectedItems.Contains(item))
        {
            List.SelectedItems.Clear();
            List.SelectedItem = item;
        }
        var at = PointToScreen(e.GetPosition(this));
        ShellMenuItem[] extras =
        [
            new(RenameId, DrawerPieceCopy.RenameDrawer),
            new(FoldId, _state.Folded ? DrawerPieceCopy.Open : DrawerPieceCopy.Fold),
            new(HideId, DrawerPieceCopy.Hide),
            new(SettingsId, DrawerPieceCopy.Settings),
        ];
        ShellMenuChoice? choice;
        using (_service.Hold())
            choice = ShellMenu.Show(Handle, item is { Pending: false } ? item.Path : null, (int)at.X, (int)at.Y, extras, verb => verb == "rename");
        if (choice is { Verb: "rename" } && item is not null) BeginRename(item);
        switch (choice?.Own)
        {
            case RenameId: RenameDrawer(); break;
            case FoldId: _service.SetFolded(_state.Def.Id, !_state.Folded); break;
            case HideId: _service.Hide(_state.Def.Id); break;
            case SettingsId: _service.RequestSettings(); break;
        }
    }

    private void RenameDrawer()
    {
        var prompt = new TextPromptWindow(DrawerPieceCopy.RenameDrawer, "Name", StreamerMode.Mask.Omit(_state.Def.Name) ?? "");   // streamer: masked - the prompt opens blank while on; the drawer name is not shown
        if (prompt.ShowDialog() != true) return;
        if (_service.RenameDrawer(_state.Def.Id, prompt.Value) is { } error) Say(error);
    }

    // ---- inline rename ----

    internal void BeginRename(DrawerItem item)
    {
        if (item.Pending || item.Editing) return;
        _renameHold ??= _service.Hold();
        _editing = item;
        item.Editing = true;
    }

    internal void CommitRename(DrawerItem item, string text)
    {
        if (_disposed || !item.Editing) return;
        item.Editing = false;
        text = text.Trim();
        // A blank box under streamer mode (it opens blank, see EditorStart) keeps the name, as Escape does.
        if (text.Length == 0 && StreamerMode.On) { EndRename(); return; }
        if (text != item.Name && _service.RenameItem(item.Path, text) is { } error) Say(error);
        EndRename();
    }

    private void CancelRename(DrawerItem item)
    {
        item.Editing = false;
        EndRename();
    }

    private void EndRename()
    {
        _editing = null;
        _renameHold?.Dispose();
        _renameHold = null;
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true || box.DataContext is not DrawerItem item) return;
        var (text, stem) = EditorStart(item);
        box.Text = text;
        box.Dispatcher.BeginInvoke(() => { box.Focus(); box.Select(0, stem); }, DispatcherPriority.Input);
    }

    /// <summary>What the inline rename box opens with and how much of it is selected: the name with its stem selected, as Explorer does;
    /// blank while streamer mode is on, as the drawer-rename prompt opens (the box is not blurred).</summary>
    internal static (string Text, int Select) EditorStart(DrawerItem item)
    {
        if (StreamerMode.Mask.Omit(item.Name) is not { } name) return ("", 0);   // streamer: masked - Mask.Omit opens the box blank while on
        int stem = item.IsFolder ? name.Length : Path.GetFileNameWithoutExtension(name).Length;   // streamer: key - a length, never drawn
        if (stem == 0) stem = name.Length;   // a dotfile (.gitignore) has no stem: select the whole name, as Explorer does
        return (name, stem);
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DrawerItem item } box) return;
        if (e.Key == Key.Enter) { CommitRename(item, box.Text); List.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(item); List.Focus(); e.Handled = true; }
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: DrawerItem item } box) CommitRename(item, box.Text);
    }

    // ---- helpers ----

    private IntPtr Handle => PresentationSource.FromVisual(this) is HwndSource src ? src.Handle : IntPtr.Zero;

    private static DrawerItem? ItemAt(object source) =>
        (source as DependencyObject) is { } d && ItemsControl.ContainerFromElement(null, d) is ListBoxItem { DataContext: DrawerItem item } ? item
        : (source as FrameworkElement)?.DataContext as DrawerItem;

    /// <summary>A drop's result: the named line, or only the counts while streamer mode is on (the line names files).</summary>
    internal void Say(DrawerRun run) => Say(run.Shown);   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on

    /// <summary>One line at the drawer's foot for six seconds: what a drop skipped, or why a rename did not happen.</summary>
    internal void Say(string line)
    {
        Status.Text = line;
        AutomationProperties.SetName(Status, line);   // streamer: notname - a rename error or the drop line, which Say(DrawerRun) already cut to counts while streamer mode is on
        Status.Visibility = Visibility.Visible;
        _statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _statusTimer.Stop();
        _statusTimer.Tick += Hide;
        _statusTimer.Start();

        void Hide(object? s, EventArgs a)
        {
            _statusTimer!.Tick -= Hide;
            _statusTimer.Stop();
            Status.Visibility = Visibility.Collapsed;
        }
    }

    public void Dispose()
    {
        BindingOperations.ClearBinding(this, AutomationProperties.NameProperty);
        List.SelectionChanged -= OnSelectionChanged;
        List.ItemsSource = null;
        _state.PropertyChanged -= OnStateChanged;
        _state.Items.CollectionChanged -= OnItemsChanged;
        _disposed = true;
        if (_editing is { } editing) editing.Editing = false;
        EndRename();
        EndMove();
        _statusTimer?.Stop();
    }
}
