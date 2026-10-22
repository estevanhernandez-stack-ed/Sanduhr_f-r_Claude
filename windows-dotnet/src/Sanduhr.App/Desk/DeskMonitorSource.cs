using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The monitors as the Desk places pieces on them (spec, Desk pieces per monitor): each one's device path (the id
/// <c>IDesktopWallpaper</c> reports, which survives restarts and resolution changes), its size and position in physical pixels, and
/// whether it is the main one, named by <see cref="DeskMonitorNames"/>. Matched to the Desk's screens by their bounds. Where
/// Windows will not say (no wallpaper object) there are no ids: every piece placed on a particular monitor then draws on the main one.
/// </summary>
internal static class DeskMonitorSource
{
    /// <summary>The connected monitors with names, in screen order. Never throws; empty when the shell would not answer.</summary>
    public static IReadOnlyList<DeskMonitorInfo> Read()
    {
        try
        {
            var shell = DeskSensorsNative.Wallpaper();
            if (shell is not { } info || info.Monitors.Count == 0) return [];
            var primaries = DeskNative.Monitors().Where(m => m.Primary).Select(m => m.Bounds).ToList();
            var raw = info.Monitors
                .Where(m => m.Id is not null)
                .Select(m =>
                {
                    var bounds = new PxRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
                    bool primary = primaries.Count > 0 ? primaries.Contains(bounds) : m.Left == 0 && m.Top == 0;
                    return new DeskMonitorInfo(m.Id!, "", bounds.Width, bounds.Height, primary, m.Left, m.Top);
                })
                .ToList();
            return DeskMonitorNames.Named(raw);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Each monitor's device path by its bounds, for matching the Desk's screens.</summary>
    public static IReadOnlyList<(PxRect Bounds, string Id)> IdsByBounds()
    {
        try
        {
            return DeskSensorsNative.Wallpaper() is { } info
                ? [.. info.Monitors.Where(m => m.Id is not null).Select(m => (new PxRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top), m.Id!))]
                : [];
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>The device path of the monitor with these bounds, null when none matches.</summary>
    public static string? IdFor(IReadOnlyList<(PxRect Bounds, string Id)> ids, PxRect bounds)
    {
        foreach (var (b, id) in ids)
            if (b == bounds) return id;
        return null;
    }
}
