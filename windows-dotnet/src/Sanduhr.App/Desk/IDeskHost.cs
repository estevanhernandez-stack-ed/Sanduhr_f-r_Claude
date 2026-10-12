using System.Windows;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// One monitor as the Desk lays out on it: its device name, bounds and work area in physical pixels,
/// its DPI scale (1.0 at 96 dpi), whether it is the primary one, and the room its taskbar takes on
/// each side, in DIPs (<see cref="TaskbarClearance.Insets"/>).
/// </summary>
internal sealed record DeskScreen(string Device, PxRect Bounds, PxRect Work, double Scale, bool Primary, DeskInsets Taskbar)
{
    /// <summary>The screen's size in DIPs, what a Desk view lays out in.</summary>
    public Size SizeDips => new(Bounds.Width / Scale, Bounds.Height / Scale);
}

/// <summary>One stack of Desk pieces and where it hangs: <paramref name="Anchor"/> is a point in the
/// screen's DIPs, and the alignments say which of the island's edges (or its middle) sits on it.</summary>
internal sealed record DeskIsland(FrameworkElement Content, Point Anchor, HorizontalAlignment Horizontal, VerticalAlignment Vertical)
{
    /// <summary>The gate the stack's live pieces watch: paused while the island is covered (S7, 7.1).</summary>
    public DeskPause Pause { get; init; } = new();

    /// <summary>The widget words of the pieces in this stack ("telemetry", "nowPlaying", ...).</summary>
    public IReadOnlyList<string> Widgets { get; init; } = [];
}

/// <summary>What the Desk draws on one screen: its islands, each drawn in a window just its size, so
/// a redraw repaints a few small surfaces rather than a full-screen one (spec 5.1, cost).</summary>
internal sealed record DeskSurface(DeskScreen Screen, IReadOnlyList<DeskIsland> Islands);

/// <summary>
/// The seam between the Desk and the windows it draws in (spec 5.1). The host owns the HWNDs, their
/// placement and their z-order; everything else (the controller, the views, Settings) talks to this.
/// <see cref="DesktopLayerHost"/> is the shipped route (V3, Rainmeter's "On desktop"); tests and the
/// render harness can stand in their own.
/// </summary>
internal interface IDeskHost : IDisposable
{
    /// <summary>The monitors now, read fresh.</summary>
    IReadOnlyList<DeskScreen> Screens();

    /// <summary>Monitors, DPI, the work area or Explorer changed: read <see cref="Screens"/> again and
    /// hand the host new surfaces. Raised once per burst of changes.</summary>
    event Action? ScreensChanged;

    /// <summary>Show these surfaces, one window per island, replacing what was shown. Windows are
    /// reused when the islands line up with the last call (the minute's redraw), so the steady state
    /// creates no windows.</summary>
    void Show(IReadOnlyList<DeskSurface> surfaces);

    /// <summary>Close every Desk window. The host keeps listening until disposed.</summary>
    void Hide();

    /// <summary>True while Desk windows are up.</summary>
    bool IsShowing { get; }

    /// <summary>One flag per island in the last <see cref="Show"/> order: true when other windows hide
    /// the island completely. All false when unknown (nothing shown, or a host that cannot tell).</summary>
    IReadOnlyList<bool> Coverage { get; }

    /// <summary>The <see cref="Coverage"/> flags changed. Nothing is laid out again on this event.</summary>
    event Action? CoverageChanged;
}
