using System.Buffers.Binary;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Docker's multiplexed log stream (no TTY) into whole lines (spec ci-watchers-3.9 section 4).</summary>
public class DockerLogFramesTests
{
    private static byte[] Frame(string text, byte stream = 1)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = stream;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    private static readonly string[] Lines =
    [
        "2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: compile",
        "2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job compile completed with result: Succeeded",
    ];

    private static byte[] Stream() => [.. Frame(Lines[0] + "\n"), .. Frame(Lines[1] + "\n", stream: 2)];

    [Fact]
    public void WholeFramesInOneRead() => Assert.Equal(Lines, new DockerLogFrames().Push(Stream()));

    [Fact]
    public void EveryByteSplitGivesTheSameLines()
    {
        var bytes = Stream();
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            var d = new DockerLogFrames();
            var got = d.Push(bytes.AsSpan(0, cut)).Concat(d.Push(bytes.AsSpan(cut))).ToList();
            Assert.Equal(Lines, got);
        }
    }

    [Fact]
    public void ByteAtATime()
    {
        var d = new DockerLogFrames();
        var got = Stream().SelectMany(b => d.Push([b])).ToList();
        Assert.Equal(Lines, got);
    }

    [Fact]
    public void LineAcrossTwoFrames()
    {
        var d = new DockerLogFrames();
        Assert.Empty(d.Push(Frame("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running ")));
        Assert.Equal(["2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: compile"], d.Push(Frame("job: compile\n")));
    }

    [Fact]
    public void CarriageReturnsAndEmptyFrames()
    {
        var d = new DockerLogFrames();
        Assert.Equal(["a", "", "b"], d.Push([.. Frame(""), .. Frame("a\r\n\nb\n")]));
    }

    [Fact]
    public void OversizedFrameIsSkippedAndTheNextOneReads()
    {
        var big = new byte[8 + DockerLogFrames.MaxFrame + 1];
        big[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(big.AsSpan(4), (uint)(DockerLogFrames.MaxFrame + 1));
        big.AsSpan(8).Fill((byte)'x');
        Assert.Equal(["ok"], new DockerLogFrames().Push([.. big, .. Frame("ok\n")]));
    }

    [Fact]
    public void StdinOrUnknownStreamIsSkipped() =>
        Assert.Equal(["ok"], new DockerLogFrames().Push([.. Frame("secret\n", stream: 0), .. Frame("ok\n", stream: 1)]));

    [Fact]
    public void OverlongLineIsDropped()
    {
        var d = new DockerLogFrames();
        Assert.Equal(["after"], d.Push([.. Frame(new string('x', DockerLogFrames.MaxLine + 10) + "\n"), .. Frame("after\n")]));
    }

    [Fact]
    public void AnUnframedTtyStreamReadsAsPlainLines()
    {
        var text = Encoding.UTF8.GetBytes(Lines[0] + "\r\n" + Lines[1] + "\r\n");
        Assert.Equal(Lines, new DockerLogFrames().Push(text));
        var d = new DockerLogFrames();
        Assert.Equal(Lines, text.SelectMany(b => d.Push([b])).ToList());
    }

    [Fact]
    public void AHeaderWithAReservedByteSetIsNotAFrame()
    {
        byte[] header = [1, 0, 0, 1, 0, 0, 0, 3];
        var lines = new DockerLogFrames().Push([.. header, .. "ok\n"u8.ToArray()]);
        Assert.EndsWith("ok", Assert.Single(lines));
    }

    [Fact]
    public void APartialLineBeforeASkippedFrameIsNotSplicedOntoTheNext()
    {
        var d = new DockerLogFrames();
        var got = d.Push([.. Frame("partial "), .. Frame("secret\n", stream: 0), .. Frame("rest\n"), .. Frame("ok\n")]);
        Assert.Equal(["ok"], got);
    }

    [Fact]
    public void ASkippedFrameBetweenWholeLinesLosesNothing() =>
        Assert.Equal(["a", "b"], new DockerLogFrames().Push([.. Frame("a\n"), .. Frame("secret\n", stream: 0), .. Frame("b\n")]));
}
