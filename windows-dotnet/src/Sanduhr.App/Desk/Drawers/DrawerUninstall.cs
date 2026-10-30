using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Setup.exe uninstall's return (spec section 4), from Velopack's <c>OnBeforeUninstallFastCallback</c>: the same return as turning off,
/// best-effort and inside a time budget, because that callback must be quick. A file still locked keeps its home, in plain sight on the
/// Desktop. Never throws: an uninstall must not fail on drawers. A Store package cannot run this; Settings and the listing say so.
/// </summary>
internal static class DrawerUninstall
{
    /// <summary>What the uninstall callback spends on the return before it stops and leaves the rest in plain sight.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    public static void Run(Paths paths, TimeSpan budget) => Run(paths, DrawerPlaces.ForThisUser(), budget);

    public static DrawerReturnRun? Run(Paths paths, DrawerPlaces places, TimeSpan budget)
    {
        try
        {
            var s = new SettingsStore(paths).LoadDrawers();
            if (s.Drawers.Count == 0) return null;
            var plan = DrawerReturn.Plan(s.Drawers, s.RootOr(places.Desktop), places.Desktop, DrawerService.ChildrenOf, DrawerMover.Exists,
                removeRoot: s.RootIsDefault(places.Desktop));
            var until = DateTime.UtcNow + budget;
            return new DrawerMover(places.Desktop).Return(plan, [.. s.Drawers.Select(d => d.Home)], () => DateTime.UtcNow >= until);
        }
        catch (Exception) { return null; }   // includes a clash name that ran out: silent, the files stay in plain sight
    }
}
