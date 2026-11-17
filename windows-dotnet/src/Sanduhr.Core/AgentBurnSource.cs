namespace Sanduhr.Core;

/// <summary>One log file an agent source hands the Vault. <paramref name="SessionId"/> keys the Vault row (two files with
/// the same id write the same row, replacing it). <paramref name="Cwd"/> is the project when the source knows it up
/// front (Antigravity); null lets the parser's events name it (Codex). A compressed file is never resumed mid-file.</summary>
public sealed record AgentBurnFile(FileInfo File, string SessionId, string? Cwd, bool Compressed);

/// <summary>An agent whose local burn the Vault holds (spec 3). Its root folder in the Vault is <see cref="Agent"/>.</summary>
public interface IAgentBurnSource
{
    string Agent { get; }

    /// <summary>The files to read now. May throw; the ingester then skips this agent's root for the cycle.</summary>
    IReadOnlyList<AgentBurnFile> Discover();

    /// <summary>A readable stream of the file's lines (decompressed for a compressed file). May throw
    /// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> or <see cref="InvalidDataException"/>.</summary>
    Stream Open(AgentBurnFile file);

    /// <summary>A parser for one file, resumed from a checkpoint's state (null for a fresh read).</summary>
    IBurnLineParser NewParser(string? state);
}

/// <summary>Reads at most <c>maxBytes</c> from an inner stream, then throws <see cref="InvalidDataException"/>: a
/// decompression bomb or an oversized rollout fails that one file instead of the cycle.</summary>
public sealed class CappedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = inner.Read(buffer, offset, count);
        _read += n;
        if (_read > maxBytes)
            throw new InvalidDataException("decompressed size over the cap");
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
