namespace Sanduhr.Core;

/// <summary>The screen edge a taskbar sits on.</summary>
public enum TaskbarEdge { Left, Top, Right, Bottom }

/// <summary>
/// How far the Desk keeps clear of the taskbar: the Windows answer to the Mac DockClearance.
/// Pixel math on plain rectangles, so it tests without a screen.
/// </summary>
public static class TaskbarClearance
{
    private const int Slack = 4;

    /// <summary>What a shown taskbar reserves: the monitor less its work area, per side, in pixels.</summary>
    public static DeskInsets Reserved(PxRect monitor, PxRect work) =>
        new(Math.Max(0, work.X - monitor.X),
            Math.Max(0, work.Y - monitor.Y),
            Math.Max(0, monitor.Right - work.Right),
            Math.Max(0, monitor.Bottom - work.Bottom));

    /// <summary>
    /// The edge and thickness of a taskbar window on <paramref name="monitor"/>, or null when it is
    /// not on it (no overlap and not within 4 px). Wider than tall is Top or Bottom (whichever edge
    /// its center is nearer), else Left or Right. An auto-hidden bar keeps its size while slid off screen.
    /// </summary>
    public static (TaskbarEdge Edge, int Thickness)? ForBar(PxRect bar, PxRect monitor)
    {
        if (bar.IsEmpty || monitor.IsEmpty) return null;
        // The slack applies across the bar's thin axis (a hidden bar sits just off the edge); along its
        // long axis the bar must truly overlap, so a bar flush against a neighbouring monitor is not ours.
        var wide = bar.Width > bar.Height;
        var xSlack = wide ? 0 : Slack;
        var ySlack = wide ? Slack : 0;
        var near = bar.X < monitor.Right + xSlack && bar.Right > monitor.X - xSlack &&
                   bar.Y < monitor.Bottom + ySlack && bar.Bottom > monitor.Y - ySlack;
        if (!near) return null;
        if (wide)
        {
            var cy = bar.Y + bar.Height / 2.0;
            var mid = monitor.Y + monitor.Height / 2.0;
            return (cy < mid ? TaskbarEdge.Top : TaskbarEdge.Bottom, bar.Height);
        }
        var cx = bar.X + bar.Width / 2.0;
        var midX = monitor.X + monitor.Width / 2.0;
        return (cx < midX ? TaskbarEdge.Left : TaskbarEdge.Right, bar.Width);
    }

    /// <summary>
    /// The Desk taskbar insets in DIPs: per side the larger of the reservation and every
    /// auto-hide bar thickness on that edge, divided by <paramref name="scale"/>.
    /// </summary>
    public static DeskInsets Insets(PxRect monitor, PxRect work, IEnumerable<PxRect> autoHideBars, double scale)
    {
        var r = Reserved(monitor, work);
        double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
        foreach (var bar in autoHideBars)
        {
            if (ForBar(bar, monitor) is not { } hit) continue;
            switch (hit.Edge)
            {
                case TaskbarEdge.Left: l = Math.Max(l, hit.Thickness); break;
                case TaskbarEdge.Top: t = Math.Max(t, hit.Thickness); break;
                case TaskbarEdge.Right: rt = Math.Max(rt, hit.Thickness); break;
                default: b = Math.Max(b, hit.Thickness); break;
            }
        }
        return new DeskInsets(l, t, rt, b).Scaled(scale);
    }

    /// <summary>The Desk anchors a taskbar on <paramref name="edge"/> pushes: the three on that edge.</summary>
    public static IReadOnlyList<DeskAnchor> AnchorsFor(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Bottom => [DeskAnchor.BL, DeskAnchor.BC, DeskAnchor.BR],
        TaskbarEdge.Left => [DeskAnchor.TL, DeskAnchor.ML, DeskAnchor.BL],
        TaskbarEdge.Right => [DeskAnchor.TR, DeskAnchor.MR, DeskAnchor.BR],
        _ => [DeskAnchor.TL, DeskAnchor.TC, DeskAnchor.TR],
    };
}
