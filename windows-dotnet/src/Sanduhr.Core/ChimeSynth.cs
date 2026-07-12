namespace Sanduhr.Core;

/// <summary>
/// PCM-WAV chime synthesis — the pure, platform-free half of <c>sounds.py</c>.
/// Builds 16-bit mono WAV blobs for the four UI cues (success / error / info /
/// toggle) so the byte-shape is unit-testable in Core; the App's <c>Sounds</c>
/// writes these to <c>%APPDATA%\Sanduhr\sounds</c> and plays them async.
///
/// Parity note: the note sequences, sample rate, attack/release envelope, and
/// the conservative ~25%-of-full-scale amplitude all match <c>sounds.py</c>
/// verbatim, so the generated WAVs are byte-identical to the Python build's.
/// </summary>
public static class ChimeSynth
{
    /// <summary>A single tone: a frequency in Hz held for a duration in seconds,
    /// at an optional level (0..1 of the sequence amplitude — lets a ring-out
    /// decay across sequential notes; 1.0 for every pre-WS-B cue).</summary>
    public readonly record struct Note(double Frequency, double DurationSeconds, double Level = 1.0);

    /// <summary>Oscillator shape. Sine is the house voice (soft UI cues); Square
    /// is the PSG-era console bite, used only by the opt-in snake sting — a pure
    /// sine can never read as a codec-era alert.</summary>
    public enum Waveform { Sine, Square }

    // ~25% of full-scale 16-bit (32767). Background-of-attention, not announcements.
    private const double Amplitude = 8000.0;
    private const double AttackSeconds = 0.005;
    private const double ReleaseSeconds = 0.030;

    /// <summary>Ascending C-E-G arpeggio — save / action success.</summary>
    public static readonly IReadOnlyList<Note> Success = new[]
    {
        new Note(261.63, 0.10), // C4
        new Note(329.63, 0.10), // E4
        new Note(392.00, 0.16), // G4 (slightly longer landing)
    };

    /// <summary>Descending D-B-G arpeggio — failures / invalid input.</summary>
    public static readonly IReadOnlyList<Note> Error = new[]
    {
        new Note(293.66, 0.10), // D4
        new Note(246.94, 0.10), // B3
        new Note(196.00, 0.18), // G3 (low landing)
    };

    /// <summary>Two-beat E tap — neutral / informational.</summary>
    public static readonly IReadOnlyList<Note> Info = new[]
    {
        new Note(329.63, 0.08), // E4
        new Note(329.63, 0.12), // E4 (same pitch, two-beat tap)
    };

    /// <summary>Single brief A tap — checkbox / toggle interactions.</summary>
    public static readonly IReadOnlyList<Note> Toggle = new[]
    {
        new Note(440.00, 0.04), // A4
    };

    /// <summary>Soft ascending two-note — a tier crossed the warn threshold.
    /// Background-of-attention, same amplitude discipline as the UI cues.</summary>
    public static readonly IReadOnlyList<Note> AlertWarn = new[]
    {
        new Note(329.63, 0.09), // E4
        new Note(392.00, 0.16), // G4
    };

    /// <summary>Firmer three-note landing-and-holding on C5 — urgent threshold.</summary>
    public static readonly IReadOnlyList<Note> AlertUrgent = new[]
    {
        new Note(392.00, 0.09), // G4
        new Note(523.25, 0.09), // C5
        new Note(523.25, 0.18), // C5 held
    };

    /// <summary>The 100% sting — a synthesized homage to a certain codec-era
    /// alert ("!"), NOT a sample. Contour measured from a reference recording
    /// (2026-07-12): a ~90 ms rising sweep from ~660 Hz through ~1.6 kHz into a
    /// bright body near C7, then a metallic ring-out decaying over ~800 ms.
    /// Rendered square (<see cref="Waveform.Square"/>) for the console-era bite;
    /// the stepped Levels are the decay. Opt-in via the Alerts tab; when off,
    /// Full uses <see cref="AlertUrgent"/>.</summary>
    public static readonly IReadOnlyList<Note> AlertSnake = new[]
    {
        new Note(659.26, 0.030, 0.80),  // E5  — the grab
        new Note(987.77, 0.030, 0.90),  // B5  — sweep
        new Note(1480.00, 0.035, 1.00), // F#6 — sweep
        new Note(2093.00, 0.10, 1.00),  // C7  — the "!" lands
        new Note(2093.00, 0.12, 0.70),  // ring-out…
        new Note(2093.00, 0.14, 0.50),
        new Note(2093.00, 0.16, 0.34),
        new Note(2093.00, 0.20, 0.22),
        new Note(2093.00, 0.24, 0.13),  // …to silence
    };

    /// <summary>
    /// Build a 16-bit mono PCM-WAV blob from <paramref name="notes"/> played
    /// sequentially. Each note carries a 5 ms linear attack + 30 ms release so
    /// the boundaries don't click. Header is the canonical 44-byte RIFF/WAVE.
    /// 1:1 with <c>sounds._build_wav</c>.
    /// </summary>
    public static byte[] BuildWav(IReadOnlyList<Note> notes, int sampleRate = 44100)
        => BuildWav(notes, sampleRate, Waveform.Sine);

    /// <summary>As above, with an oscillator choice. Square runs at a reduced
    /// amplitude (its harmonics carry far more perceived energy than a sine at
    /// the same peak) so the sting bites without breaking the house discipline.</summary>
    public static byte[] BuildWav(IReadOnlyList<Note> notes, int sampleRate, Waveform waveform)
    {
        var samples = new List<byte>();
        double attack = AttackSeconds * sampleRate;
        double release = ReleaseSeconds * sampleRate;
        double amplitude = waveform == Waveform.Square ? Amplitude * 0.75 : Amplitude;

        foreach (var note in notes)
        {
            int n = (int)(sampleRate * note.DurationSeconds);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sampleRate;
                double attackEnv = attack > 0 ? Math.Min(1.0, i / attack) : 1.0;
                double releaseEnv = release > 0 ? Math.Min(1.0, (n - i) / release) : 1.0;
                double env = attackEnv * releaseEnv;
                double osc = Math.Sin(2 * Math.PI * note.Frequency * t);
                if (waveform == Waveform.Square)
                    osc = Math.Sign(osc);
                int sample = (int)(amplitude * note.Level * env * osc);
                short s16 = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
                samples.Add((byte)(s16 & 0xff));
                samples.Add((byte)((s16 >> 8) & 0xff));
            }
        }

        int dataSize = samples.Count;
        var wav = new byte[44 + dataSize];
        WriteAscii(wav, 0, "RIFF");
        WriteInt32(wav, 4, 36 + dataSize);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt32(wav, 16, 16);               // PCM fmt chunk size
        WriteInt16(wav, 20, 1);                // PCM format
        WriteInt16(wav, 22, 1);                // mono
        WriteInt32(wav, 24, sampleRate);
        WriteInt32(wav, 28, sampleRate * 2);   // byte rate (mono * 16-bit)
        WriteInt16(wav, 32, 2);                // block align
        WriteInt16(wav, 34, 16);               // bits per sample
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, dataSize);
        samples.CopyTo(wav, 44);
        return wav;
    }

    private static void WriteAscii(byte[] buf, int offset, string s)
    {
        for (int i = 0; i < s.Length; i++)
            buf[offset + i] = (byte)s[i];
    }

    private static void WriteInt32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xff);
        buf[offset + 1] = (byte)((value >> 8) & 0xff);
        buf[offset + 2] = (byte)((value >> 16) & 0xff);
        buf[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static void WriteInt16(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xff);
        buf[offset + 1] = (byte)((value >> 8) & 0xff);
    }
}
