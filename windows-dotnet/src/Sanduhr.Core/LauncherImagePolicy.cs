namespace Sanduhr.Core;

/// <summary>
/// The rules for a PNG a person picks for a Launcher button (spec, Desk Launcher): at most 1 MB on disk, a real
/// PNG, decoded and re-encoded to a 256 px square before it is stored under a hash of the result. Pure checks
/// and arithmetic here; the decoding is the App's.
/// </summary>
public static class LauncherImagePolicy
{
    /// <summary>The biggest file accepted, in bytes (1 MB).</summary>
    public const long MaxBytes = 1_048_576;

    /// <summary>The stored image's side in pixels.</summary>
    public const int Side = 256;

    /// <summary>The biggest source side accepted, so a small file that decodes to a huge bitmap is refused.</summary>
    public const int MaxSourceSide = 4096;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>True when the bytes begin with the PNG signature.</summary>
    public static bool LooksLikePng(ReadOnlySpan<byte> head) => head.Length >= PngSignature.Length && head[..PngSignature.Length].SequenceEqual(PngSignature);

    /// <summary>Why a picked file is refused before decoding, or null when it may be tried.</summary>
    public static string? RefuseFile(long length, ReadOnlySpan<byte> head)
    {
        if (length <= 0) return "That file is empty.";
        if (length > MaxBytes) return "That image is over 1 MB.";
        if (!LooksLikePng(head)) return "Choose a PNG image.";
        return null;
    }

    /// <summary>Why a decoded size is refused, or null when it is fine.</summary>
    public static string? RefuseSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return "That image has no size.";
        if (width > MaxSourceSide || height > MaxSourceSide) return "That image is too large.";
        return null;
    }

    /// <summary>
    /// Where the source lands inside the <see cref="Side"/> square: scaled to fit (longest side to the square, never
    /// enlarged past it), centered, the rest transparent. Returns x, y, width and height in pixels.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Fit(int width, int height)
    {
        if (width <= 0 || height <= 0) return (0, 0, 0, 0);
        double scale = Math.Min((double)Side / width, (double)Side / height);
        int w = Math.Clamp((int)Math.Round(width * scale), 1, Side);
        int h = Math.Clamp((int)Math.Round(height * scale), 1, Side);
        return ((Side - w) / 2, (Side - h) / 2, w, h);
    }

    /// <summary>The stored file name for a re-encoded image: lowercase hex of its SHA-256 and <c>.png</c>.</summary>
    public static string FileName(ReadOnlySpan<byte> encoded) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded)).ToLowerInvariant() + ".png";
}
