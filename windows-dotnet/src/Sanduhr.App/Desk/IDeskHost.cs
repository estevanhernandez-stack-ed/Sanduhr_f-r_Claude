using System.Windows;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// One monitor as the Desk lays out on it: its device name, bounds and work area in physical pixels,
/// its DPI scale (1.0 at 96 dpi), whether it is the primary one, the room its taskbar takes on
/// each side, in DIPs (<see cref="TaskbarClearance.Insets"/>), and its device path (<paramref name="Id"/>, null when
/// Windows would not say), the identity a piece placed on one monitor is kept by.
/// </summary>
internal sealed record DeskScreen(string Device, PxRect Bounds, PxRect Work, double Scale, bool Primary, DeskInsets Taskbar, string? Id = null)
{
    /// <summary>This screen as monitor placement sees it.</summary>
    public DeskScreenRef Ref => new(Id, Primary);

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

    /// <summary>The island holds a piece that takes clicks (spec 7.4): its window drops
    /// <c>WS_EX_TRANSPARENT</c>, so its drawn pixels take the mouse while fully transparent ones still pass
    /// it to the desktop. False keeps the whole window click-through, as before.</summary>
    public bool TakesClicks { get; init; }

    /// <summary>The island holds a drawer (5.0 drawers spec, section 2): a click into it may make its window active so the keyboard
    /// reaches the drawer (<see cref="DeskFocus"/>), and activation is given back on a click away. False never activates, as before.</summary>
    public bool TakesFocus { get; init; }

    /// <summary>The island is a free drawer (drawers amendment 1): the host keeps its window above every widget window and below every app,
    /// and leaves where it sits to the drawer while the user moves it.</summary>
    public bool Drawer { get; init; }

    /// <summary>The island is a Glass drawer (drawers look and placement spec, section 1): the host blurs the wallpaper behind its window and
    /// rounds the window's corners. The blur fills the whole window rectangle, so the window is the panel itself, with no room around it
    /// for a shadow (the panel keeps the place it was given). Only meaningful with <see cref="Drawer"/>.</summary>
    public bool Glass { get; init; }

    /// <summary>The blur's gradient color (AABBGGRR); near-clear, as the spike ran it, because the panel's own brush does the tint.</summary>
    public int GlassTint { get; init; } = 0x01000000;

    /// <summary>Called by the host with whether DWM rounded the window's corners, each time it lays out a Glass drawer whose blur took.
    /// Never called when the blur was refused (the tinted plate keeps its rounded corners in a transparent window).</summary>
    public Action<bool>? GlassCorners { get; init; }

    /// <summary>A roaming island (the companion walking its edges): the host keeps the window's aligned edge on the motion's
    /// anchor as it moves, without laying the island out again. Null for an island that stays where it hangs.</summary>
    public DeskIslandMotion? Motion { get; init; }
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

    /// <summary>OLED care's drift (spec 3.7.0): each island's window sits at this point of <see cref="OledDrift"/>'s orbit,
    /// moved as a whole (its content is not touched) and kept inside its monitor's work area and clear of the taskbar. Null
    /// puts every window at its true anchor. Kept for windows made later.</summary>
    void SetDrift(int? step);

    /// <summary>OLED care's idle dim: the islands ease to <paramref name="opacity"/> (1 is full) over <paramref name="ease"/>
    /// (zero snaps). Kept for windows made later.</summary>
    void SetDim(double opacity, TimeSpan ease);
}
