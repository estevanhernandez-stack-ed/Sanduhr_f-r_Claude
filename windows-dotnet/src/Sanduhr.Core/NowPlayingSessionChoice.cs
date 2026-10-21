namespace Sanduhr.Core;

/// <summary>
/// One media session as the choice sees it.
/// </summary>
/// <param name="Index">The caller's handle for the session (its place in its list).</param>
/// <param name="Info">What the session last said.</param>
/// <param name="IsCurrent">Windows calls this the current session (the one it would send media keys to).</param>
/// <param name="PlayingSince">When it last started playing; null when unknown or not playing.</param>
public sealed record NowPlayingCandidate(int Index, NowPlayingInfo Info, bool IsCurrent, DateTimeOffset? PlayingSince);

/// <summary>
/// Which media session the now-playing piece follows. Windows' "current" session can be a paused
/// browser tab while Spotify plays, so the piece does not just take the current one:
/// <list type="number">
/// <item>A session that is playing wins. With several, the current one if it is among them, else the one
/// that started playing most recently.</item>
/// <item>Otherwise, only with "Keep showing while paused", the current session if it is paused.</item>
/// <item>Otherwise none.</item>
/// </list>
/// An excluded app never wins (<see cref="NowPlayingPrefs.Visible"/> hides it from every rule). Pure.
/// </summary>
public static class NowPlayingSessionChoice
{
    public static NowPlayingCandidate? Choose(IReadOnlyList<NowPlayingCandidate> sessions, NowPlayingPrefs prefs)
    {
        NowPlayingCandidate? best = null;
        foreach (var c in sessions)
        {
            if (prefs.Visible(c.Info)?.State != NowPlayingState.Playing) continue;
            if (best is null || Beats(c, best)) best = c;
        }
        if (best is not null) return best;

        if (!prefs.KeepWhilePaused) return null;
        foreach (var c in sessions)
            if (c.IsCurrent && prefs.Visible(c.Info)?.State == NowPlayingState.Paused) return c;
        return null;
    }

    // Among playing sessions: current first, then the latest start (an unknown start is the oldest),
    // then the earlier in the list so the answer never wobbles.
    private static bool Beats(NowPlayingCandidate a, NowPlayingCandidate b)
    {
        if (a.IsCurrent != b.IsCurrent) return a.IsCurrent;
        var sa = a.PlayingSince ?? DateTimeOffset.MinValue;
        var sb = b.PlayingSince ?? DateTimeOffset.MinValue;
        return sa > sb;
    }
}
