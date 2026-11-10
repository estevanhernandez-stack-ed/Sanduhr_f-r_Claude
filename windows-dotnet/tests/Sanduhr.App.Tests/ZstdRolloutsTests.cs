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
}
