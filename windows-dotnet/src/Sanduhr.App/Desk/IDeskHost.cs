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

/// <summary>What the Desk draws on one screen.</summary>
internal sealed record DeskSurface(DeskScreen Screen, FrameworkElement Content);

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

    /// <summary>Show these surfaces, one window each, replacing whatever was shown.</summary>
    void Show(IReadOnlyList<DeskSurface> surfaces);

    /// <summary>Close every Desk window. The host keeps listening until disposed.</summary>
    void Hide();

    /// <summary>True while Desk windows are up.</summary>
    bool IsShowing { get; }
}
