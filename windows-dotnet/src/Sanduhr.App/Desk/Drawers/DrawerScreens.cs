using System.Windows;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Where a free drawer is drawn (drawers amendment 1): a saved <see cref="DrawerPlace"/> is a monitor word and an offset from that monitor's
/// work area in its DIPs; the Desk's windows take a screen and an anchor in that screen's DIPs, and a drag ends at a point in screen pixels.
/// Pure over the screens it is handed, so two monitors at different scales are testable without either.
/// </summary>
internal static class DrawerScreens
{
    /// <summary>The screen a drawer saved on <paramref name="monitor"/> draws on: the one with that id, else the primary ("main", a monitor
    /// not connected, or one Windows would not name). Null only with no screens.</summary>
    public static DeskScreen? Find(string monitor, IReadOnlyList<DeskScreen> screens)
    {
        if (screens.Count == 0) return null;
        if (DeskMonitors.IsId(monitor) && screens.FirstOrDefault(s => s.Id is { } id && DeskMonitors.Same(id, monitor)) is { } on) return on;
        return Primary(screens);
    }

    public static DeskScreen Primary(IReadOnlyList<DeskScreen> screens) => screens.FirstOrDefault(s => s.Primary) ?? screens[0];

    /// <summary>A screen's work area size in its DIPs, what <see cref="DrawerPlacement"/> measures in.</summary>
    public static Size WorkDips(DeskScreen s) => new(s.Work.Width / s.Scale, s.Work.Height / s.Scale);

    /// <summary>The screen a place draws on and the drawer's top-left there, in that screen's DIPs from its bounds (the island's anchor),
    /// clamped so its title bar stays in the work area.</summary>
    public static (DeskScreen Screen, Point Anchor) ToAnchor(DrawerPlace place, IReadOnlyList<DeskScreen> screens)
    {
        var screen = Find(place.Monitor, screens) ?? throw new ArgumentException("No screens to place a drawer on.", nameof(screens));
        var work = WorkDips(screen);
        var p = DrawerPlacement.Clamp(place, work.Width, work.Height);
        return (screen, new Point((screen.Work.X - screen.Bounds.X) / screen.Scale + p.X, (screen.Work.Y - screen.Bounds.Y) / screen.Scale + p.Y));
    }

    /// <summary>The place for a drawer whose top-left was dropped at <paramref name="topLeft"/>: the screen holding the point (else the
    /// nearest), saved as "main" for the primary and by id otherwise, offset from its work area in its DIPs and clamped.</summary>
    public static DrawerPlace ToPlace(PxPoint topLeft, IReadOnlyList<DeskScreen> screens)
    {
        if (screens.Count == 0) throw new ArgumentException("No screens to place a drawer on.", nameof(screens));
        var screen = screens.FirstOrDefault(s => Contains(s.Bounds, topLeft)) ?? screens.MinBy(s => Distance(s.Bounds, topLeft))!;
        // A secondary screen Windows would not name has no identity to save: the place is kept on the primary.
        string monitor = screen.Primary || screen.Id is null ? DeskMonitors.Main : screen.Id;
        var work = WorkDips(screen);
        var place = new DrawerPlace(monitor, (topLeft.X - screen.Work.X) / screen.Scale, (topLeft.Y - screen.Work.Y) / screen.Scale);
        return DrawerPlacement.Clamp(place, work.Width, work.Height);
    }

    private static bool Contains(PxRect r, PxPoint p) => p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom;

    private static double Distance(PxRect r, PxPoint p)
    {
        double dx = Math.Max(Math.Max(r.X - p.X, 0), p.X - (r.Right - 1));
        double dy = Math.Max(Math.Max(r.Y - p.Y, 0), p.Y - (r.Bottom - 1));
        return dx * dx + dy * dy;
    }
}
