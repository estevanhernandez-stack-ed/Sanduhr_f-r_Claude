using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Docker's log stream for a container without a TTY (spec ci-watchers-3.9 section 4): frames of an
/// 8-byte header (the stream, 1 stdout or 2 stderr; three zero bytes; the payload length, big-endian)
/// and the payload, decoded into whole lines. Frames and lines may split anywhere. A frame over
/// <see cref="MaxFrame"/> or from any other stream is skipped, and so is the line a skipped frame
/// interrupts; a line over <see cref="MaxLine"/> is dropped. A header whose three reserved bytes are not
/// zero means the stream is not framed (a container with a TTY sends raw text): from there on the bytes are
/// read as plain lines. Pure.
/// </summary>
public sealed class DockerLogFrames
{
    public const int MaxFrame = 64 * 1024;
    public const int MaxLine = 16 * 1024;

    private readonly byte[] _header = new byte[8];
    private int _headerFill;
    private long _payloadLeft;
    private bool _skipping;
    private bool _raw;
    private readonly List<byte> _line = [];
    private bool _lineTooLong;

    /// <summary>The lines <paramref name="data"/> completes, without their line ends.</summary>
    public IReadOnlyList<string> Push(ReadOnlySpan<byte> data)
    {
        var lines = new List<string>();
        if (_raw)
        {
            Append(data, lines);
            return lines;
        }
        while (data.Length > 0)
        {
            if (_payloadLeft == 0)
            {
                var take = Math.Min(_header.Length - _headerFill, data.Length);
                data[..take].CopyTo(_header.AsSpan(_headerFill));
                _headerFill += take;
                data = data[take..];
                if (_headerFill < _header.Length) break;
                _headerFill = 0;
                if (_header[1] != 0 || _header[2] != 0 || _header[3] != 0)
                {
                    // Not a frame header: the stream is raw text (TTY). Read the bytes consumed so far as text too.
                    _raw = true;
                    Append(_header, lines);
                    Append(data, lines);
                    return lines;
                }
                _payloadLeft = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(4));
                _skipping = _header[0] is not (1 or 2) || _payloadLeft > MaxFrame;
                // A line cut off by a skipped frame would be spliced onto the next frame's text: drop it.
                if (_skipping && _line.Count > 0) _lineTooLong = true;
                continue;
            }
            var n = (int)Math.Min(_payloadLeft, data.Length);
            if (!_skipping) Append(data[..n], lines);
            _payloadLeft -= n;
            data = data[n..];
        }
        return lines;
    }

    private void Append(ReadOnlySpan<byte> bytes, List<string> lines)
    {
        foreach (var b in bytes)
        {
            if (b == (byte)'\n')
            {
                if (!_lineTooLong) lines.Add(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_line)).TrimEnd('\r'));
                _line.Clear();
                _lineTooLong = false;
            }
            else if (_line.Count < MaxLine)
            {
                _line.Add(b);
            }
            else
            {
                _lineTooLong = true;
            }
        }
    }
}
