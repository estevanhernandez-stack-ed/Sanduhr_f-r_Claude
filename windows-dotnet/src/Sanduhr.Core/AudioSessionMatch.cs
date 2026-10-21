namespace Sanduhr.Core;

/// <summary>
/// One render audio session as the bars see it: who owns it and how loud it is right now. The App
/// reads these from the default output device's session list (one peak level each, nothing captured)
/// and hands them to <see cref="AudioSessionMatch.Pick"/>.
/// </summary>
/// <param name="ProcessName">The owning process's image name without ".exe" (null when unknown).</param>
/// <param name="IsSystemSounds">The Windows "System Sounds" session: chimes, dings and alerts.</param>
/// <param name="IsOwn">A session of Sanduhr itself (its own alert chimes).</param>
/// <param name="Peak">The session's peak level, 0 to 1.</param>
public readonly record struct AudioSessionReading(string? ProcessName, bool IsSystemSounds, bool IsOwn, float Peak);

/// <summary>What the bars should draw: the level, and whether the playing app's own session was found.</summary>
/// <param name="Peak">0 to 1.</param>
/// <param name="Matched">True when the level is the playing app's sessions; false when it is the
/// fallback (the loudest of the other sessions).</param>
public readonly record struct AudioSessionPick(float Peak, bool Matched);

/// <summary>
/// Which audio sessions the bars follow (spec 6.6). The bars used to read the whole output's meter, so
/// a Windows chime moved them. Now they read the sessions of the app that owns the playing media
/// session. A media session names its app by AppUserModelId ("Spotify.exe", "chrome", "MSEdge",
/// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"); an audio session names a process ("Spotify",
/// "chrome", "msedge"). <see cref="Candidates"/> turns the first into the process names it could mean,
/// <see cref="Matches"/> compares, and <see cref="Pick"/> takes the loudest matching session, or when
/// none matches the loudest of every session that is not System Sounds and not Sanduhr's own. The
/// endpoint's whole-output meter is never a fallback.
/// </summary>
public static class AudioSessionMatch
{
    // AppUserModelIds that do not spell their process: a hashed id (Firefox), a package whose
    // processes carry another name. Keys and values are lower case, without ".exe".
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["308046B0AF4A39CB"] = ["firefox"],
        ["Mozilla.Firefox"] = ["firefox"],
        ["Microsoft.ZuneMusic"] = ["microsoft.media.player", "mediaplayer", "music.ui"],
        ["Microsoft.ZuneVideo"] = ["microsoft.media.player", "mediaplayer", "video.ui"],
        ["Microsoft.MediaPlayer"] = ["microsoft.media.player", "mediaplayer"],
        ["AppleInc.AppleMusicWin"] = ["applemusic", "applemusicwin"],
        ["AppleInc.iTunes"] = ["itunes"],
        ["MSEdge"] = ["msedge"],
        ["BraveSoftware.Brave-Browser"] = ["brave"],
        ["com.squirrel.Discord.Discord"] = ["discord"],
        ["com.squirrel.TIDAL.TIDAL"] = ["tidal"],
    };

    /// <summary>
    /// The process names (lower case, no ".exe", no path) an app id could mean, best first. Takes the
    /// package family part and the app part around '!', each without a "_publisherid" suffix, a path or
    /// ".exe"; the whole text and its last dotted segment; and any alias. Never empty strings.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? appId)
    {
        var raw = appId?.Trim();
        if (string.IsNullOrEmpty(raw)) return [];
        var found = new List<string>();
        void Add(string? s)
        {
            var t = s?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(t) && !found.Contains(t)) found.Add(t);
        }

        foreach (var part in raw.Split('!'))
        {
            var name = part;
            var us = name.IndexOf('_');
            if (us > 0 && !name.Contains('\\') && !name.Contains('/')) name = name[..us];
            var slash = name.LastIndexOfAny(['\\', '/']);
            if (slash >= 0) name = name[(slash + 1)..];
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            Add(name);
            var dot = name.LastIndexOf('.');
            if (dot > 0 && dot < name.Length - 1) Add(name[(dot + 1)..]);
            if (Aliases.TryGetValue(name, out var more)) foreach (var m in more) Add(m);
        }
        if (Aliases.TryGetValue(raw, out var direct)) foreach (var m in direct) Add(m);
        return found;
    }

    /// <summary>True when <paramref name="processName"/> (any case, with or without ".exe") is one of the
    /// processes <paramref name="appId"/> could mean. A package id that runs under a shorter name
    /// ("spotifymusic" for the process "spotify") counts when the process name is at least five letters.</summary>
    public static bool Matches(string? appId, string? processName)
    {
        var proc = processName?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(proc)) return false;
        if (proc.EndsWith(".exe", StringComparison.Ordinal)) proc = proc[..^4];
        foreach (var c in Candidates(appId))
        {
            if (c == proc) return true;
            if (proc.Length >= 5 && c.StartsWith(proc, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The level to draw. System Sounds and Sanduhr's own sessions are never read. With an app id, the
    /// loudest of the sessions that match it (a browser has one per tab process); if none matches (or
    /// there is no app id), the loudest of the rest. No session at all is a level of 0 (not matched).
    /// </summary>
    public static AudioSessionPick Pick(string? appId, IReadOnlyList<AudioSessionReading> sessions)
    {
        float best = 0, matched = 0;
        bool anyMatch = false;
        foreach (var s in sessions)
        {
            if (s.IsSystemSounds || s.IsOwn) continue;
            float peak = float.IsNaN(s.Peak) ? 0f : Math.Clamp(s.Peak, 0f, 1f);
            if (peak > best) best = peak;
            if (!string.IsNullOrEmpty(appId) && Matches(appId, s.ProcessName))
            {
                anyMatch = true;
                if (peak > matched) matched = peak;
            }
        }
        return anyMatch ? new AudioSessionPick(matched, true) : new AudioSessionPick(best, false);
    }
}
