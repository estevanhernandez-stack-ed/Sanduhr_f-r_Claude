using System.IO;
using System.Text;
using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class ZstdRolloutsTests
{
    [Fact]
    public void Round_trips_a_zstd_frame()
    {
        var text = "{\"a\":1}\n{\"b\":2}\n";
        using var compressor = new ZstdSharp.Compressor();
        var frame = compressor.Wrap(Encoding.UTF8.GetBytes(text)).ToArray();
        using var s = ZstdRollouts.Decompress(new MemoryStream(frame));
        using var r = new StreamReader(s);
        Assert.Equal(text, r.ReadToEnd());
    }

    [Fact]
    public void Garbage_reads_as_invalid_data()
    {
        using var s = ZstdRollouts.Decompress(new MemoryStream(Encoding.UTF8.GetBytes("not zstd")));
        Assert.Throws<InvalidDataException>(() => s.ReadByte());
    }

    private static byte[] TruncatedFrame()
    {
        var sb = new StringBuilder();
        var rng = new Random(7);
        for (int i = 0; i < 4000; i++) sb.Append("{\"n\":").Append(rng.Next()).Append("}\n");
        using var compressor = new ZstdSharp.Compressor();
        var frame = compressor.Wrap(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return frame.AsSpan(0, frame.Length / 2).ToArray();
    }

    [Fact]
    public void A_truncated_frame_reads_as_invalid_data()
    {
        // ZstdSharp raises EndOfStreamException on a cut frame; the adapter translates it.
        using var s = ZstdRollouts.Decompress(new MemoryStream(TruncatedFrame()));
        var buf = new byte[4096];
        Assert.Throws<InvalidDataException>(() => { while (s.Read(buf, 0, buf.Length) > 0) { } });
    }

    [Fact]
    public void The_reader_over_a_truncated_zst_never_throws()
    {
        var home = Path.Combine(Path.GetTempPath(), "sanduhr-zst-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(home, "sessions", "2026", "10", "22");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "rollout-2026-10-22T10-00-00-t.jsonl.zst"), TruncatedFrame());
            var r = new Sanduhr.Core.CodexLimitsReader(() => [home], ZstdRollouts.Decompress).Read();
            Assert.True(r.Status is Sanduhr.Core.CodexReadStatus.NoLimits or Sanduhr.Core.CodexReadStatus.Unreadable, r.Status.ToString());
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }
}
