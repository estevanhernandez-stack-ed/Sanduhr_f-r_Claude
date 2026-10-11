namespace Sanduhr.App.Services;

/// <summary>
/// Set once Reset Sanduhr… has run its steps and the app is about to quit. From then on nothing writes
/// <c>settings.json</c>, <c>snapshot.json</c>, history or the vault, so a window closing or a timer that
/// fires on the way out cannot bring back what Reset just removed. The flag is never cleared; the process
/// is exiting.
/// </summary>
public static class ResetGuard
{
    private static volatile bool _done;

    /// <summary>True after Reset has finished: write nothing.</summary>
    public static bool Done => _done;

    /// <summary>Called by Reset when its steps have run and the app will quit.</summary>
    public static void MarkDone() => _done = true;
}
