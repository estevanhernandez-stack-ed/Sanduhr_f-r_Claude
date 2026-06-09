using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Shape tests for Core/ChimeSynth.cs — the pure WAV-synthesis ported from
/// <c>sounds.py</c>. Verifies the note tables and that BuildWav emits a valid
/// 16-bit mono PCM-WAV with the canonical 44-byte RIFF header and the expected
/// data size for the given note sequence.
/// </summary>
public class ChimeSynthTests
{
    [Fact]
    public void Note_tables_match_python_sequences()
    {
        Assert.Equal(3, ChimeSynth.Success.Count);
        Assert.Equal(3, ChimeSynth.Error.Count);
        Assert.Equal(2, ChimeSynth.Info.Count);
        Assert.Single(ChimeSynth.Toggle);

        Assert.Equal(261.63, ChimeSynth.Success[0].Frequency);   // C4
        Assert.Equal(392.00, ChimeSynth.Success[2].Frequency);   // G4 landing
        Assert.Equal(196.00, ChimeSynth.Error[2].Frequency);     // G3 low landing
        Assert.Equal(329.63, ChimeSynth.Info[0].Frequency);      // E4
        Assert.Equal(440.00, ChimeSynth.Toggle[0].Frequency);    // A4
        Assert.Equal(0.04, ChimeSynth.Toggle[0].DurationSeconds);
    }

    [Fact]
    public void Build_wav_emits_canonical_16bit_mono_header()
    {
        const int sampleRate = 44100;
        var wav = ChimeSynth.BuildWav(ChimeSynth.Toggle, sampleRate);

        Assert.Equal("RIFF", Ascii(wav, 0, 4));
        Assert.Equal("WAVE", Ascii(wav, 8, 4));
        Assert.Equal("fmt ", Ascii(wav, 12, 4));
        Assert.Equal(16, ReadInt32(wav, 16));                 // PCM fmt chunk size
        Assert.Equal(1, ReadInt16(wav, 20));                  // PCM format
        Assert.Equal(1, ReadInt16(wav, 22));                  // mono
        Assert.Equal(sampleRate, ReadInt32(wav, 24));         // sample rate
        Assert.Equal(sampleRate * 2, ReadInt32(wav, 28));     // byte rate (mono * 16-bit)
        Assert.Equal(2, ReadInt16(wav, 32));                  // block align
        Assert.Equal(16, ReadInt16(wav, 34));                 // bits per sample
        Assert.Equal("data", Ascii(wav, 36, 4));
    }

    [Fact]
    public void Build_wav_data_size_matches_sample_count()
    {
        const int sampleRate = 44100;
        var notes = ChimeSynth.Success;
        var wav = ChimeSynth.BuildWav(notes, sampleRate);

        int expectedSamples = 0;
        foreach (var n in notes)
            expectedSamples += (int)(sampleRate * n.DurationSeconds);
        int expectedData = expectedSamples * 2; // 16-bit mono

        Assert.Equal(expectedData, ReadInt32(wav, 40)); // data chunk size
        Assert.Equal(36 + expectedData, ReadInt32(wav, 4)); // RIFF chunk size
        Assert.Equal(44 + expectedData, wav.Length); // header + samples
    }

    [Fact]
    public void Build_wav_toggle_has_expected_length()
    {
        const int sampleRate = 44100;
        var wav = ChimeSynth.BuildWav(ChimeSynth.Toggle, sampleRate);
        int samples = (int)(sampleRate * 0.04); // 1764
        Assert.Equal(44 + samples * 2, wav.Length);
    }

    private static string Ascii(byte[] buf, int offset, int len)
        => Encoding.ASCII.GetString(buf, offset, len);

    private static int ReadInt32(byte[] b, int o)
        => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

    private static int ReadInt16(byte[] b, int o)
        => b[o] | (b[o + 1] << 8);
}
