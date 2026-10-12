namespace Sanduhr.Core;

/// <summary>How Arrange mode ends: Done writes the edit, Cancel drops it.</summary>
public enum DeskArrangeEnd { Done, Cancel }

/// <summary>
/// Arrange mode: the Desk's pieces moved and reordered on the desktop itself. The pure answers live
/// here: which anchor a drop lands on, where in a stack, the size a handle drag snaps to, when the
/// surface takes the mouse. Ported from the Mac DeskArrange, without the notch.
/// </summary>
public static class DeskArrange
{
    /// <summary>How far around a stack's pieces a drop still counts as on that stack: the outline's pad and the name label above it.</summary>
    public const double StackReach = 26;

    /// <summary>The least width the bar needs to show its words and buttons.</summary>
    public const double BarMinimumWidth = 340;

    /// <summary>The least height the bar needs to show its words and buttons.</summary>
    public const double BarMinimumHeight = 170;

    /// <summary>
    /// The anchor a drop at (<paramref name="x"/>, <paramref name="y"/>) lands on: the nearest of the
    /// eight anchor points, measured as a share of the content's width and height, so the zones are
    /// the same on any screen shape. A tie goes to the first anchor in <see cref="DeskAnchors.All"/>.
    /// </summary>
    public static DeskAnchor Nearest(double x, double y, DeskRect content)
    {
        var w = Math.Max(1, content.Width);
        var h = Math.Max(1, content.Height);
        var best = DeskAnchor.TL;
        var bestDistance = double.PositiveInfinity;
        foreach (var anchor in DeskAnchors.All)
        {
            var (px, py) = DeskGeometry.Point(anchor, content);
            var dx = (x - px) / w;
            var dy = (y - py) / h;
            var d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                best = anchor;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Each anchor's stack as one rectangle: the union of its drawn pieces' frames (the dragged
    /// piece's own included, it stays drawn while dragged), grown by <paramref name="reach"/>. Pieces
    /// with no frame are skipped; an anchor with none drawn has no rectangle.
    /// </summary>
    public static IReadOnlyDictionary<DeskAnchor, DeskRect> StackBounds(
        DeskArrangement arrangement, IReadOnlyDictionary<string, DeskRect> frames, double reach = StackReach)
    {
        var union = new Dictionary<DeskAnchor, DeskRect>();
        foreach (var piece in arrangement.Pieces)
        {
            if (!frames.TryGetValue(piece.Widget, out var f) || IsEmpty(f)) continue;
            union[piece.Anchor] = union.TryGetValue(piece.Anchor, out var u) ? Union(u, f) : f;
        }
        return union.ToDictionary(kv => kv.Key, kv => Inflate(kv.Value, reach));
    }

    /// <summary>
    /// The anchor a drop at (<paramref name="x"/>, <paramref name="y"/>) lands on. Over a stack
    /// (<see cref="StackBounds"/>) it is that stack's anchor, the dragged piece's own stack
    /// (<paramref name="own"/>) first, so a drop anywhere on a tall stack reorders within it even where
    /// its upper pieces reach past the halfway line to the next anchor. Elsewhere it is the nearest anchor point.
    /// </summary>
    public static DeskAnchor Target(
        double x, double y, DeskRect content, IReadOnlyDictionary<DeskAnchor, DeskRect> stacks, DeskAnchor? own)
    {
        if (own is { } o && stacks.TryGetValue(o, out var mine) && Contains(mine, x, y)) return o;
        foreach (var anchor in DeskAnchors.All)
            if (stacks.TryGetValue(anchor, out var r) && Contains(r, x, y)) return anchor;
        return Nearest(x, y, content);
    }

    /// <summary>
    /// The piece a drop at height <paramref name="y"/> goes in front of, in a stack listed top to
    /// bottom (the dragged piece left out): the first drawn piece whose middle is at or below
    /// <paramref name="y"/>. Null puts it at the bottom of the stack. Pieces with no frame are skipped.
    /// </summary>
    public static string? PieceAfter(double y, IReadOnlyList<(string Widget, DeskRect Frame)> stack)
    {
        foreach (var (widget, frame) in stack)
            if (!IsEmpty(frame) && frame.MidY >= y) return widget;
        return null;
    }

    /// <summary>Which corner of a piece carries its resize handle: the one facing the middle of the screen.</summary>
    public static (bool Trailing, bool Bottom) Handle(DeskAnchor anchor) =>
        (DeskAnchors.Column(anchor) != DeskColumn.Right, DeskAnchors.Row(anchor) != DeskRow.Bottom);

    /// <summary>
    /// The size a handle drag asks for: the start size grown by the drag along the handle's outward
    /// direction, as a share of the piece's width plus height (a center piece grows both ways across,
    /// so its sideways drag counts twice), then snapped to the size steps and kept within 60% to 160%.
    /// </summary>
    public static double Scale(double start, double width, double height, double dragX, double dragY, DeskAnchor anchor)
    {
        var span = width + height;
        if (!(span > 0)) return DeskArrangement.ClampScale(start);
        var (trailing, bottom) = Handle(anchor);
        var across = (trailing ? dragX : -dragX) * (DeskAnchors.Column(anchor) == DeskColumn.Center ? 2 : 1);
        var down = bottom ? dragY : -dragY;
        return DeskArrangement.ClampScale(start * (1 + (across + down) / span));
    }

    /// <summary>
    /// Whether the surface takes the mouse at the pointer: over its whole frame while arranging,
    /// else only over a click area.
    /// </summary>
    public static bool TakesMouse(bool arranging, bool overElement) => arranging || overElement;

    /// <summary>
    /// What a key does in Arrange mode, wherever the key focus is: Return and Enter end it as Done
    /// (keeping the edit), Escape as Cancel (dropping it); null for any other key. The caller maps its
    /// key to the names "Return", "Enter" and "Escape".
    /// </summary>
    public static DeskArrangeEnd? EndKey(string? keyName) => keyName switch
    {
        "Return" or "Enter" => DeskArrangeEnd.Done,
        "Escape" => DeskArrangeEnd.Cancel,
        _ => null,
    };

    /// <summary>The bar's size: what its content fits, never smaller than the bar needs to show its words and buttons.</summary>
    public static (double Width, double Height) BarSize(double fittingWidth, double fittingHeight) =>
        (Math.Max(Finite(fittingWidth), BarMinimumWidth), Math.Max(Finite(fittingHeight), BarMinimumHeight));

    private static double Finite(double v) => double.IsFinite(v) ? v : 0;

    private static bool IsEmpty(DeskRect r) => !(r.Width > 0) || !(r.Height > 0);

    private static DeskRect Union(DeskRect a, DeskRect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        return new DeskRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private static DeskRect Inflate(DeskRect r, double by) =>
        new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    /// <summary>The left and top edges are inside, the right and bottom are not.</summary>
    private static bool Contains(DeskRect r, double x, double y) =>
        x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
}

/// <summary>Arrange mode's words, in one place for the menus, the bar, Settings and the tests.</summary>
public static class DeskArrangeCopy
{
    public const string MenuItem = "Arrange Desk…";
    public const string SettingsButton = "Arrange Desk…";
    public const string SettingsNote = "Move and reorder the pieces on the desktop itself. Return or Done keeps the new layout; Escape or Cancel puts it back as it was.";
    public const string BarTitle = "Arrange Desk";
    public const string BarHint = "Drag a piece to any of the eight places, or up and down its stack.";
    public const string BarKeys = "Return or Done keeps the new layout. Escape or Cancel puts it back.";
    public const string SettingsArranging = "Arranging on the desktop. Finish there with Done or Cancel.";
    public const string SettingsDeskOff = "Turn on the Desk to arrange it on the desktop.";

    /// <summary>The tray's entry while arranging.</summary>
    public const string TrayFinish = "Finish arranging";
}

/// <summary>
/// One Arrange mode edit: the layout string as it was saved, kept verbatim so Cancel leaves it
/// exactly so, and the arrangement being changed. Done writes the working layout, once, and only
/// when it differs from the saved one.
/// </summary>
public sealed class DeskArrangeSession
{
    /// <summary>Starts an edit of <paramref name="saved"/>; null or blank edits <see cref="DeskArrangement.Standard"/>.</summary>
    public DeskArrangeSession(string? saved)
    {
        Saved = string.IsNullOrWhiteSpace(saved) ? DeskArrangement.Standard : saved;
        Working = new DeskArrangement(Saved);
    }

    /// <summary>The saved layout string, verbatim.</summary>
    public string Saved { get; }

    /// <summary>The arrangement being edited.</summary>
    public DeskArrangement Working { get; }

    /// <summary>True when the working layout differs from the saved one (compared as canonical layout strings).</summary>
    public bool Changed => Working.Layout != new DeskArrangement(Saved).Layout;

    /// <summary>The string Done writes, null when nothing changed (an older string stays as it was).</summary>
    public string? ToWrite => Changed ? Working.Layout : null;
}

/// <summary>
/// One drag in Arrange mode, independent of the UI's mouse capture. Ending a drag releases the capture,
/// and releasing it reports the capture lost; that report must not cancel the drag being dropped, or no
/// drop ever lands (the S7 live run's r2 failure). <see cref="Up"/> takes the drag first, then lets the
/// caller release the capture, so a loss reported during the release finds no drag to cancel.
/// </summary>
/// <typeparam name="T">What the UI keeps about the drag (the widget, where it was grabbed).</typeparam>
public sealed class DeskArrangeGesture<T> where T : class
{
    /// <summary>The drag under way, or null.</summary>
    public T? Current { get; private set; }

    /// <summary>A drag starts.</summary>
    public void Down(T drag) => Current = drag;

    /// <summary>The button came up: the drag to drop (null when none), taken before
    /// <paramref name="releaseCapture"/> runs, so a capture loss it reports cancels nothing.</summary>
    public T? Up(Action releaseCapture)
    {
        var drag = Current;
        Current = null;
        releaseCapture();
        return drag;
    }

    /// <summary>The capture was taken away (another window grabbed the mouse): the drag, if any, is
    /// cancelled. Returns what was cancelled.</summary>
    public T? CaptureLost()
    {
        var drag = Current;
        Current = null;
        return drag;
    }
}
