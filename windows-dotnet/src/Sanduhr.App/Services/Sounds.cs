using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Runtime.Versioning;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Short audio cues for save / error / info / toggle interactions — the Windows
/// playback half of <c>sounds.py</c>. The WAV bytes come from Core's
/// <see cref="ChimeSynth"/>; this writes them to <c>%APPDATA%\Sanduhr\sounds</c>
/// on first use and plays them async via <see cref="SoundPlayer"/>.
///
/// Parity with <c>sounds.py</c>: file-based async playback (not in-memory — the
/// Python build found <c>SND_MEMORY</c> silently dropped on some configs); the
/// per-process file cache pays the build+write cost once; and
/// <c>SANDUHR_SILENT_SOUNDS</c> disables playback entirely (set in tests so temp
/// dirs aren't held open by async WAV handles). Every path soft-fails — a bad
/// sound device must never crash the widget over a save dialog.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Sounds
{
    private static readonly ConcurrentDictionary<string, string> FileCache = new();

    private static bool Silent =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANDUHR_SILENT_SOUNDS"));

    /// <summary>Ascending C-E-G arpeggio for save / action success.</summary>
    public static void PlaySaveConfirmation() => Play(ChimeSynth.Success, "success");

    /// <summary>Descending D-B-G arpeggio for failures / invalid input.</summary>
    public static void PlayError() => Play(ChimeSynth.Error, "error");

    /// <summary>Two-beat E tap for neutral / informational dialogs.</summary>
    public static void PlayInfo() => Play(ChimeSynth.Info, "info");

    /// <summary>Brief A tap for toggle / theme-switch interactions.</summary>
    public static void PlayToggle() => Play(ChimeSynth.Toggle, "toggle");

    /// <summary>Warn-threshold alert chime (WS-B).</summary>
    public static void PlayAlertWarn() => Play(ChimeSynth.AlertWarn, "alert-warn");

    /// <summary>Urgent-threshold alert chime (WS-B).</summary>
    public static void PlayAlertUrgent() => Play(ChimeSynth.AlertUrgent, "alert-urgent");

    /// <summary>The opt-in 100% sting (synthesized homage — see ChimeSynth.AlertSnake).</summary>
    public static void PlayAlertSnake() => Play(ChimeSynth.AlertSnake, "alert-snake");

    private static void Play(IReadOnlyList<ChimeSynth.Note> notes, string cacheKey)
    {
        if (Silent || !OperatingSystem.IsWindows())
            return;
        try
        {
            var path = WavPathFor(notes, cacheKey);
            // SoundPlayer.Play() loads + plays on a background thread — non-blocking,
            // the equivalent of winsound SND_ASYNC.
            new SoundPlayer(path).Play();
        }
        catch
        {
            // Soft-fail: sound failures never propagate.
        }
    }

    private static string WavPathFor(IReadOnlyList<ChimeSynth.Note> notes, string cacheKey)
    {
        if (FileCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
            return cached;

        var dir = new Paths().SoundsDir;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, cacheKey + ".wav");
        try
        {
            File.WriteAllBytes(path, ChimeSynth.BuildWav(notes));
        }
        catch (IOException)
        {
            // File may be locked from a previous async play — reuse the existing one.
            if (!File.Exists(path))
                throw;
        }
        FileCache[cacheKey] = path;
        return path;
    }
}
