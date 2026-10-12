namespace Sanduhr.Core;

// Now playing: the pure half, ported from the Mac's NowPlaying.swift. The Mac merges a diff
// stream, runs a supervisor over a helper process and falls back to Music and Spotify; Windows
// has a public API (GlobalSystemMediaTransportControlsSession) that hands over whole snapshots,
// so none of that exists here. The App's NowPlayingService reads the session and feeds
// NowPlayingInfo.FromSession into the tracker. Titles, artists and app ids live only in memory:
// never in a file, a log or settings (the excluded list stores app ids the user picked).

/// <summary>What plays, as far as Sanduhr cares.</summary>
public enum NowPlayingState
{
    /// <summary>No track, or a track without a title.</summary>
    None,
    Playing,
    Paused,
}

/// <summary>
/// Mirrors <c>GlobalSystemMediaTransportControlsSessionPlaybackStatus</c> so Core stays free of
/// WinRT. The App maps the system enum onto this one by name.
/// </summary>
public enum SessionPlaybackStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// The track as Sanduhr shows it (the Mac's <c>NowPlayingInfo</c>). Seconds are doubles;
/// <see cref="ElapsedAt"/> is the moment <see cref="Elapsed"/> was true, so the position can keep
/// moving between the session's timeline updates.
/// </summary>
public sealed record NowPlayingInfo
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }

    /// <summary>Track length in seconds.</summary>
    public double? Duration { get; init; }

    /// <summary>Seconds into the track at <see cref="ElapsedAt"/>.</summary>
    public double? Elapsed { get; init; }

    public DateTimeOffset? ElapsedAt { get; init; }

    /// <summary>Playback rate; 1 is normal speed.</summary>
    public double? Rate { get; init; }

    /// <summary>True playing, false paused, null unknown (then the rate decides).</summary>
    public bool? Playing { get; init; }

    /// <summary>The app playing it: the session's <c>SourceAppUserModelId</c>.</summary>
    public string? AppId { get; init; }

    /// <summary>None without a title; otherwise <see cref="Playing"/> decides, else a positive rate means playing.</summary>
    public NowPlayingState State
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return NowPlayingState.None;
            if (Playing is { } p) return p ? NowPlayingState.Playing : NowPlayingState.Paused;
            return (Rate ?? 0) > 0 ? NowPlayingState.Playing : NowPlayingState.Paused;
        }
    }

    /// <summary>Where playback is at <paramref name="now"/>: the last elapsed time, moved on by the
    /// wall clock times the rate while playing, within 0 and the duration. Null without an elapsed
    /// time. A clock that reads before <see cref="ElapsedAt"/> never moves it back.</summary>
    public double? Position(DateTimeOffset now)
    {
        if (Elapsed is not { } elapsed) return null;
        var p = elapsed;
        if (State == NowPlayingState.Playing && ElapsedAt is { } at)
        {
            var rate = Rate ?? 1;
            var speed = rate > 0 ? rate : 1;
            p += Math.Max(0, (now - at).TotalSeconds) * speed;
        }
        if (Duration is { } d && d > 0) p = Math.Min(p, d);
        return Math.Max(0, p);
    }

    /// <summary>The position as a fraction of the duration, null without both.</summary>
    public double? Progress(DateTimeOffset now)
    {
        if (Duration is not { } d || d <= 0 || Position(now) is not { } p) return null;
        return Math.Min(1, Math.Max(0, p / d));
    }

    /// <summary>
    /// Maps what the WinRT session gives onto a track. Stopped, Closed, Opened and Changing give no
    /// track (an empty info, State none). Duration is End minus Start when positive; Elapsed is
    /// Position minus Start; ElapsedAt is <paramref name="lastUpdated"/>, or <paramref name="now"/>
    /// when that is the default value (the session never stamped its timeline). Playing is true for
    /// Playing and false for Paused.
    /// </summary>
    public static NowPlayingInfo FromSession(
        string? title, string? artist, string? album,
        SessionPlaybackStatus status, double? rate,
        TimeSpan start, TimeSpan end, TimeSpan position, DateTimeOffset lastUpdated,
        string? appId, DateTimeOffset now)
    {
        if (status != SessionPlaybackStatus.Playing && status != SessionPlaybackStatus.Paused)
            return new NowPlayingInfo();

        var length = end - start;
        return new NowPlayingInfo
        {
            Title = title,
            Artist = artist,
            Album = album,
            Duration = length > TimeSpan.Zero ? length.TotalSeconds : null,
            Elapsed = (position - start).TotalSeconds,
            ElapsedAt = lastUpdated == default || lastUpdated.Year <= 1 ? now : lastUpdated,
            Rate = rate,
            Playing = status == SessionPlaybackStatus.Playing,
            AppId = appId,
        };
    }
}

/// <summary>
/// The track Sanduhr shows (the Mac's <c>NowPlayingTracker</c>). Players clear now playing for a
/// moment between tracks, so a change to nothing only counts after <see cref="ClearDelay"/> without
/// a track coming back; a track shows at once. Pure: the caller schedules the <see cref="Settle"/>.
/// </summary>
public sealed class NowPlayingTracker
{
    public static readonly TimeSpan ClearDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>The last snapshot received.</summary>
    public NowPlayingInfo Latest { get; private set; } = new();

    /// <summary>What shows: null when nothing plays (after the delay).</summary>
    public NowPlayingInfo? Shown { get; private set; }

    public DateTimeOffset? ClearingSince { get; private set; }

    public NowPlayingState State => Shown?.State ?? NowPlayingState.None;

    /// <summary>A whole snapshot (or nothing). Returns how long to wait before <see cref="Settle"/>,
    /// null when nothing is pending.</summary>
    public TimeSpan? Replace(NowPlayingInfo info, DateTimeOffset now)
    {
        Latest = info;
        return Settle(now);
    }

    /// <summary>Applies the clear delay. Returns the wait still left, or null when settled.</summary>
    public TimeSpan? Settle(DateTimeOffset now)
    {
        if (Latest.State != NowPlayingState.None)
        {
            Shown = Latest;
            ClearingSince = null;
            return null;
        }
        if (Shown is null) return null;
        var since = ClearingSince ?? now;
        ClearingSince = since;
        var left = ClearDelay - (now - since);
        if (left <= TimeSpan.Zero)
        {
            Shown = null;
            ClearingSince = null;
            return null;
        }
        return left;
    }

    public void Reset()
    {
        Latest = new NowPlayingInfo();
        Shown = null;
        ClearingSince = null;
    }
}

/// <summary>
/// The Now playing settings that decide whether a track shows (the Mac's <c>NowPlayingPrefs</c>,
/// less the AppleScript switch). Where it shows is the settings and Desk layout's business.
/// </summary>
public sealed record NowPlayingPrefs
{
    public bool HideWhilePaused { get; init; }

    /// <summary>App ids (AppUserModelIds) whose playback never shows. Compared ignoring case.</summary>
    public IReadOnlyCollection<string> Excluded { get; init; } = [];

    public bool IsExcluded(string? appId) =>
        appId is not null && Excluded.Contains(appId, StringComparer.OrdinalIgnoreCase);

    /// <summary>The track to draw, or null: nothing plays, its app is excluded, or it is paused and
    /// paused tracks hide.</summary>
    public NowPlayingInfo? Visible(NowPlayingInfo? info)
    {
        if (info is null) return null;
        switch (info.State)
        {
            case NowPlayingState.None: return null;
            case NowPlayingState.Paused when HideWhilePaused: return null;
        }
        return IsExcluded(info.AppId) ? null : info;
    }
}

/// <summary>The apps seen playing this session, for Settings' app list. In memory only, plus
/// whatever is excluded (so an exclusion can be undone).</summary>
public static class NowPlayingApps
{
    /// <summary>The list with <paramref name="app"/> added: first seen first, no repeats (ignoring
    /// case); nothing for a null or empty id.</summary>
    public static IReadOnlyList<string> Seen(IReadOnlyList<string> list, string? app)
    {
        if (string.IsNullOrEmpty(app) || list.Contains(app, StringComparer.OrdinalIgnoreCase)) return list;
        return [.. list, app];
    }

    /// <summary>The rows Settings lists: the apps seen, then excluded ones not seen this session.</summary>
    public static IReadOnlyList<string> Rows(IReadOnlyList<string> seen, IReadOnlyCollection<string> excluded)
    {
        var rest = excluded
            .Where(e => !seen.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);
        return [.. seen, .. rest];
    }

    /// <summary>
    /// A friendly name from an AppUserModelId, never empty. Takes the part before '!' (the package
    /// app id), drops a package family suffix after '_', a path, a trailing ".exe", keeps the last
    /// dotted segment, and capitalizes a lowercase first letter. "Spotify.exe" is "Spotify",
    /// "chrome" is "Chrome", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" is "ZuneMusic".
    /// </summary>
    public static string DisplayName(string? appId)
    {
        const string unknown = "Unknown app";
        var raw = appId?.Trim();
        if (string.IsNullOrEmpty(raw)) return unknown;

        var name = raw;
        var bang = name.IndexOf('!');
        if (bang >= 0) name = name[..bang];
        var us = name.IndexOf('_');
        if (us >= 0) name = name[..us];
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        name = name.Trim();

        if (name.Length == 0) return raw;
        return char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }
}

/// <summary>"▶ Title · Artist" with a play-state glyph (the Mac's <c>NowPlayingText</c>). Whole
/// everywhere: the widget and the Desk line truncate it to their width.</summary>
public static class NowPlayingText
{
    public const string PlayingGlyph = "▶︎";
    public const string PausedGlyph = "⏸︎";

    /// <summary>The line for a track, null when there is no track or no title.</summary>
    public static string? Line(NowPlayingInfo? info)
    {
        if (info is null || info.State == NowPlayingState.None || Clean(info.Title) is not { } title) return null;
        var glyph = info.State == NowPlayingState.Playing ? PlayingGlyph : PausedGlyph;
        return Clean(info.Artist) is { } artist ? $"{glyph} {title} · {artist}" : $"{glyph} {title}";
    }

    /// <summary>A line split into its play-state glyph and the rest, so a line can keep the glyph
    /// still while the title scrolls. A line without a leading glyph comes back whole.</summary>
    public static (string? Glyph, string Text) SplitGlyph(string line)
    {
        foreach (var g in new[] { PlayingGlyph, PausedGlyph })
        {
            if (line.StartsWith(g + " ", StringComparison.Ordinal)) return (g, line[(g.Length + 1)..]);
        }
        return (null, line);
    }

    /// <summary>The Desk line: the same text (the view truncates to its width).</summary>
    public static string? Desk(NowPlayingInfo? info) => Line(info);

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}

/// <summary>The Settings status line. Never the title: only whether something plays and where.</summary>
public static class NowPlayingStatus
{
    /// <summary>"Off", "Nothing playing", "Playing in Spotify", "Paused in Spotify". Without an app
    /// id the "in ..." part is left off.</summary>
    public static string Text(bool on, NowPlayingInfo? shown)
    {
        if (!on) return "Off";
        if (shown is null || shown.State == NowPlayingState.None) return "Nothing playing";
        var verb = shown.State == NowPlayingState.Playing ? "Playing" : "Paused";
        return string.IsNullOrWhiteSpace(shown.AppId) ? verb : $"{verb} in {NowPlayingApps.DisplayName(shown.AppId)}";
    }
}
