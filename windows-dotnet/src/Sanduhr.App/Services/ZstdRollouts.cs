using System.IO;

namespace Sanduhr.App.Services;

/// <summary>The zstd decompressor <see cref="Sanduhr.Core.CodexLimitsReader"/> takes; Core has no packages.</summary>
public static class ZstdRollouts
{
    public static Stream Decompress(Stream compressed) => new Translating(new ZstdSharp.DecompressionStream(compressed));

    private sealed class Translating(Stream inner) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return inner.Read(buffer, offset, count); }
            catch (ZstdSharp.ZstdException e) { throw new InvalidDataException("zstd: " + e.Message, e); }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
