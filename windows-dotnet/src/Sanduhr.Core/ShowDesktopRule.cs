namespace Sanduhr.Core;

/// <summary>What the Desk host does about Show desktop at one check.</summary>
public enum ShowDesktopAction
{
    /// <summary>Leave the Desk windows where they are.</summary>
    Stay,
    /// <summary>The raised desktop covers the Desk: re-seat it just above the icons host (never topmost).</summary>
    Lift,
    /// <summary>An app is in front again: back to the bottom.</summary>
    Drop,
}

/// <summary>
/// The Desk's Show-desktop decision (spec 5.1), as plain inputs so it is tested without a desktop.
/// <paramref name="deskBelowHost"/>: a Desk window was measured below the desktop icons host in the
/// z-order, the one state in which the raised desktop hides it. <paramref name="desktopForeground"/>: the
/// desktop (the shell window or one of its WorkerWs) is the foreground window.
/// <paramref name="appInFront"/>: some other visible, non-tool window is the foreground (any app,
/// Sanduhr's own Settings included; never a Desk window). <paramref name="lifted"/>: the host re-seated
/// the Desk earlier and has not dropped it yet.
/// <para>Ownership by the shell window normally carries the Desk up with the raised desktop (Este's
/// live run, 2026-10-11: Win+D left the Desk visible with no help), so the rule acts only on the
/// measured failure: the Desk below the icons host while the desktop is the foreground and no app is in
/// front. It then re-seats the Desk just above the host, not topmost (Windows hides topmost windows in
/// Show desktop, and a topmost Desk leaves its owner's band). A re-seated Desk drops back to the bottom
/// as soon as an app is in front, so it never stays over an app, and a Desk sitting above the host, at
/// rest or carried up, is never touched, so it cannot flip-flop.</para>
/// </summary>
public static class ShowDesktopRule
{
    public static ShowDesktopAction Decide(bool deskBelowHost, bool desktopForeground, bool appInFront, bool lifted)
    {
        if (appInFront)
            return lifted ? ShowDesktopAction.Drop : ShowDesktopAction.Stay;
        return deskBelowHost && desktopForeground ? ShowDesktopAction.Lift : ShowDesktopAction.Stay;
    }

    /// <summary>When the host puts its sentinel back just above the icons host: only at rest (not
    /// re-seated, the desktop not the foreground). The sentinel tells the z-order guard when the desktop
    /// is raised, so the owner may carry the Desk up.</summary>
    public static bool ShouldReseatSentinel(bool lifted, bool desktopForeground) => !lifted && !desktopForeground;
}
