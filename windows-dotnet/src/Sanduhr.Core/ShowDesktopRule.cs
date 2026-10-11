namespace Sanduhr.Core;

/// <summary>What the Desk host does about Show desktop at one check.</summary>
public enum ShowDesktopAction
{
    /// <summary>Leave the Desk windows where they are.</summary>
    Stay,
    /// <summary>Show desktop came on: lift the Desk over the raised desktop.</summary>
    Lift,
    /// <summary>Show desktop is over (or an app is in front): back to the bottom.</summary>
    Drop,
}

/// <summary>
/// The Desk's Show-desktop decision (spec 5.1, Rainmeter's mechanism), as plain inputs so it is tested
/// without a desktop. <paramref name="hostAboveSentinel"/>: the desktop icons host sits above the
/// sentinel the host keeps just above it at rest, which only Show desktop does.
/// <paramref name="desktopForeground"/>: the desktop (the shell window or one of its WorkerWs) is the
/// foreground window. <paramref name="appInFront"/>: some other visible, non-tool window is the
/// foreground (any app, Sanduhr's own Settings included; never a Desk window).
/// <para>The Desk lifts only when all three say Show desktop: the host raised, the desktop foreground,
/// no app in front. A lifted Desk drops the moment an app is in front or the host is back down, so it
/// can never stay over an app the person is using, and a z-order that reads "raised" at rest (an app in
/// front) never lifts it, so it cannot flip-flop.</para>
/// </summary>
public static class ShowDesktopRule
{
    public static ShowDesktopAction Decide(bool hostAboveSentinel, bool desktopForeground, bool appInFront, bool lifted)
    {
        if (lifted)
            return appInFront || !hostAboveSentinel ? ShowDesktopAction.Drop : ShowDesktopAction.Stay;
        return hostAboveSentinel && desktopForeground && !appInFront ? ShowDesktopAction.Lift : ShowDesktopAction.Stay;
    }

    /// <summary>When the host puts the sentinel back just above the icons host: only at rest (not
    /// lifted, the desktop not the foreground), never while Show desktop may be on, which would hide it.</summary>
    public static bool ShouldReseatSentinel(bool lifted, bool desktopForeground) => !lifted && !desktopForeground;
}
