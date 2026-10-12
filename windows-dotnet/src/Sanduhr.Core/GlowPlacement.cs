namespace Sanduhr.Core;

/// <summary>An integer rectangle in device pixels (virtual-screen coordinates).</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// What the service learned about the foreground window, as plain values so placement is testable
/// without a screen. <see cref="Frame"/> is the DWM extended frame (the visible bounds, shadow excluded);
/// <see cref="MonitorBounds"/> the full bounds of the monitor holding most of the window.
/// </summary>
public sealed record GlowTarget(
    PxRect Frame,
    PxRect MonitorBounds,
    bool IsMinimized = false,
    bool IsCloaked = false,
    string? ClassName = null,
    bool IsOwnProcess = false,
    bool HasCaption = true);

/// <summary>
/// Where the edge light goes (Stage 4.5): a band across the middle 50% of the foreground window's
/// width, anchored at its top edge and 24 px (times the DPI factor) tall, clamped inside the window's
/// monitor. No usable window means the same band on the fallback monitor's top edge.
/// </summary>
public static class GlowPlacement
{
    public const double BandHeightDip = 24;
    public const double WidthFraction = 0.5;

    /// <summary>Window classes of the desktop and shell: never a target.</summary>
    public static readonly IReadOnlySet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    /// <summary>
    /// Whether the glow may sit on <paramref name="t"/>. Not when it is minimized (iconic), cloaked (on
    /// another virtual desktop or hidden by the shell), zero or negative in size, the desktop or taskbar,
    /// Sanduhr's own window, or full-screen exclusive.
    /// Full-screen heuristic: the frame equals the monitor bounds exactly AND the window has no caption.
    /// A maximized normal window keeps its caption (and its frame is the work area, not the monitor), so
    /// it still glows; a borderless game or a video in F11 mode matches and is skipped.
    /// </summary>
    public static bool IsUsable(GlowTarget? t)
    {
        if (t is null) return false;
        if (t.IsMinimized || t.IsCloaked || t.IsOwnProcess) return false;
        if (t.Frame.Width <= 0 || t.Frame.Height <= 0) return false;
        if (t.ClassName is { } c && ShellClasses.Contains(c)) return false;
        if (!t.HasCaption && t.Frame == t.MonitorBounds) return false;
        return true;
    }

    /// <summary>The overlay rect in device pixels. <paramref name="dpiScale"/> is 1.0 at 96 dpi.</summary>
    public static PxRect Compute(GlowTarget? target, PxRect fallbackMonitor, double dpiScale)
    {
        if (!(dpiScale > 0)) dpiScale = 1.0;
        return IsUsable(target)
            ? Band(target!.Frame, target.MonitorBounds, dpiScale)
            : Band(fallbackMonitor, fallbackMonitor, dpiScale);
    }

    /// <summary>
    /// The same light along the middle half of one edge of <paramref name="monitor"/> (the Edge target, spec
    /// 6.8), the 24 DIP fade (times <paramref name="dpiScale"/>) pointing inward. Top and Bottom give a wide,
    /// short rect; Left and Right a tall, thin one. Sizing follows <see cref="Compute"/>: the thickness is
    /// rounded and at least 1 px, the length is half the edge rounded and at least 1 px, both kept inside
    /// the monitor. An empty monitor gives an empty rect at its origin.
    /// </summary>
    public static PxRect Edge(PxRect monitor, GlowEdgeSide side, double dpiScale)
    {
        if (monitor.IsEmpty) return new PxRect(monitor.X, monitor.Y, 0, 0);
        if (!(dpiScale > 0)) dpiScale = 1.0;
        int thick = Math.Max(1, (int)Math.Round(BandHeightDip * dpiScale));
        bool horizontal = side is GlowEdgeSide.Top or GlowEdgeSide.Bottom;
        int along = horizontal ? monitor.Width : monitor.Height;
        int across = horizontal ? monitor.Height : monitor.Width;
        int len = Math.Min(Math.Max(1, (int)Math.Round(along * WidthFraction)), along);
        thick = Math.Min(thick, across);
        if (horizontal)
        {
            int x = monitor.X + (monitor.Width - len) / 2;
            int y = side == GlowEdgeSide.Top ? monitor.Y : monitor.Bottom - thick;
            return new PxRect(x, y, len, thick);
        }
        else
        {
            int y = monitor.Y + (monitor.Height - len) / 2;
            int x = side == GlowEdgeSide.Left ? monitor.X : monitor.Right - thick;
            return new PxRect(x, y, thick, len);
        }
    }

    private static PxRect Band(PxRect edge, PxRect monitor, double dpiScale)
    {
        int h = Math.Max(1, (int)Math.Round(BandHeightDip * dpiScale));
        int w = Math.Max(1, (int)Math.Round(edge.Width * WidthFraction));
        int x = edge.X + (edge.Width - w) / 2;
        int y = edge.Y;
        if (monitor.IsEmpty) return new PxRect(x, y, w, h);
        w = Math.Min(w, monitor.Width);
        h = Math.Min(h, monitor.Height);
        x = Math.Clamp(x, monitor.X, monitor.Right - w);
        y = Math.Clamp(y, monitor.Y, monitor.Bottom - h);
        return new PxRect(x, y, w, h);
    }
}
