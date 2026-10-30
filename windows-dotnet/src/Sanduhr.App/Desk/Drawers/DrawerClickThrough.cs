using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The empty frame lets clicks through (drawers amendment 1): a drawer's window takes the mouse only over an item, the title bar or its
/// status or missing line, so a click on the empty frame reaches the desktop icon underneath. A layered window cannot say that per pixel
/// (the frame is drawn), so a 50 ms cursor poll, running only while a drawer is shown, sets or clears <c>WS_EX_TRANSPARENT</c> on the
/// drawer under the cursor. A button held down (a file dragged from Explorer may drop on empty space), a busy drawer (a move, a menu, a
/// rename) and a drawer with the keyboard keep taking clicks. A press that went through the frame to the desktop is the desktop's until
/// the button is let go, so the window stays click-through for the rest of that press (the icon's own drag, and a double-click's second
/// press, still reach it).
/// </summary>
internal sealed class DrawerClickThrough : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(50);
    private readonly DrawerService _service;
    private readonly DispatcherTimer _poll;
    private IReadOnlyList<DrawerPiece> _pieces = [];
    private bool _wasDown;
    /// <summary>The drawer window a press went through to the desktop, while that press lasts.</summary>
    private IntPtr _through;

    public DrawerClickThrough(DrawerService service, Dispatcher? ui = null)
    {
        _service = service;
        _poll = new DispatcherTimer(DispatcherPriority.Input, ui ?? Dispatcher.CurrentDispatcher) { Interval = Every };
        _poll.Tick += (_, _) => Poll();
    }

    /// <summary>The drawers on the Desk now; the poll runs only while there is one.</summary>
    public void Follow(IReadOnlyList<DrawerPiece> pieces)
    {
        _pieces = pieces;
        bool run = pieces.Count > 0;
        if (run != _poll.IsEnabled)
        {
            // A press seen before the poll stopped or started says nothing about the windows now.
            _wasDown = false;
            _through = IntPtr.Zero;
        }
        if (run) _poll.Start(); else _poll.Stop();
    }

    /// <summary>Whether a drawer's window takes the mouse with the cursor at <paramref name="inPiece"/> (the piece's own coordinates).</summary>
    public static bool TakesClicks(DrawerPiece piece, Point inPiece, bool buttonDown, bool busy, bool focused)
    {
        if (buttonDown || busy || focused) return true;
        if (inPiece.X < 0 || inPiece.Y < 0 || inPiece.X >= piece.ActualWidth || inPiece.Y >= piece.ActualHeight) return false;
        // The title and its lines take their whole row, the text's gaps included; an item takes its chip.
        if (Over(piece.TitleBar) || Over(piece.Status) || Over(piece.MissingText)) return true;
        for (var d = VisualTreeHelper.HitTest(piece, inPiece)?.VisualHit; d is not null && !ReferenceEquals(d, piece); d = Parent(d))
            if (d is ListBoxItem) return true;
        return false;

        bool Over(FrameworkElement e) =>
            e.Visibility == Visibility.Visible && e.ActualWidth > 0 && piece.IsAncestorOf(e) && e.TransformToAncestor(piece).TransformBounds(new Rect(e.RenderSize)).Contains(inPiece);
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private void Poll()
    {
        if (_pieces.Count == 0) { _poll.Stop(); _wasDown = false; _through = IntPtr.Zero; return; }
        var cursor = DeskNative.CursorPx();
        bool down = DeskNative.AnyMouseButtonDown();
        bool pressed = down && !_wasDown;
        _wasDown = down;
        if (!down) _through = IntPtr.Zero;
        bool busy = _service.Busy;
        var fg = DeskNative.GetForegroundWindow();
        foreach (var piece in _pieces)
        {
            if (PresentationSource.FromVisual(piece) is not HwndSource { IsDisposed: false, RootVisual: Visual root } src) continue;
            var h = src.Handle;
            if (DeskNative.RectPx(h) is not { } r || cursor.X < r.X || cursor.Y < r.Y || cursor.X >= r.Right || cursor.Y >= r.Bottom) continue;
            // The window's pixels to the root's own units (the root's layout scale counts), then into the piece.
            var inRoot = DrawerScreens.WindowToRoot(new PxPoint(cursor.X - r.X, cursor.Y - r.Y), DrawerScreens.PixelsPerUnit(src));
            var inPiece = root.TransformToDescendant(piece)?.Transform(inRoot) ?? new Point(-1, -1);
            if (pressed && DesktopLayerHost.IsClickThrough(h)) _through = h;
            DesktopLayerHost.SetClickThrough(h, !TakesClicks(piece, inPiece, down && _through != h, busy, fg == h));
        }
    }

    public void Dispose()
    {
        _poll.Stop();
        _pieces = [];
    }
}
