namespace Sanduhr.Core;

/// <summary>
/// A rasterized pixel sheet: straight (not premultiplied) RGBA, 8 bits a channel, rows top to bottom. Frames sit in
/// a grid of <see cref="Columns"/> by <see cref="Rows"/> cells of <see cref="FrameWidth"/> by
/// <see cref="FrameHeight"/>, row-major, exactly as FORMAT 5.5 lays out a PNG sheet: frame <c>i</c> is cell
/// <c>(i % Columns, i / Columns)</c>. Cells past the last frame are transparent.
/// </summary>
public sealed record PixelSheetImage(int FrameWidth, int FrameHeight, int FrameCount, int Columns, int Rows, byte[] Rgba)
{
    public int Width => Columns * FrameWidth;
    public int Height => Rows * FrameHeight;

    /// <summary>One frame's RGBA (<see cref="FrameWidth"/> x <see cref="FrameHeight"/> x 4 bytes), copied out of the sheet.</summary>
    public byte[] Frame(int index)
    {
        if (index < 0 || index >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var frame = new byte[FrameWidth * FrameHeight * 4];
        int ox = index % Columns * FrameWidth, oy = index / Columns * FrameHeight;
        for (int y = 0; y < FrameHeight; y++)
            Array.Copy(Rgba, ((oy + y) * Width + ox) * 4, frame, y * FrameWidth * 4, FrameWidth * 4);
        return frame;
    }
}

/// <summary>
/// Sprite v2 pixel sheets (FORMAT 5.5): frames written as characters, <c>.</c> transparent and <c>0</c>-<c>9</c>,
/// <c>a</c>-<c>f</c> indexing the palette. <see cref="Rasterize"/> turns one into the RGBA grid a PNG sheet would
/// hold, which is what Sanduhr draws (encoding it to PNG for the install folder is the app's job). Pure: bytes in,
/// pixels out.
/// </summary>
public static class PixelSheet
{
    /// <summary>Frames per row of the rasterized grid: 256 frames of 32 px make a 512 x 512 sheet, inside the
    /// 4096-pixel image limit.</summary>
    public const int SheetColumns = 16;

    /// <summary>Rasterize a pixel sheet's JSON. The sheet is checked first with the validator's own rules (against
    /// its own frame size); a sheet that fails returns null and the first reason. Never throws on bad input.</summary>
    public static PixelSheetImage? Rasterize(ReadOnlySpan<byte> json, out string? error)
    {
        error = null;
        if (json.Length > PackValidator.PixelMaxBytes)
        {
            error = "a pixel sheet is at most 2 MB";
            return null;
        }
        if (json.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            error = "a pixel sheet must be UTF-8 without a byte-order mark";
            return null;
        }
        if (!PackJson.TryParse(json, out var data, out var parseError))
        {
            error = "invalid JSON (" + parseError + ")";
            return null;
        }
        if (data is not PackObject o || o["frame_w"] is not PackNumber fwn || o["frame_h"] is not PackNumber fhn)
        {
            error = "a pixel sheet must be a JSON object with " + string.Join(", ", PackValidator.PixelKeys);
            return null;
        }
        var problems = PackValidator.CheckPixelSheet("sheet", data, (long)fwn.Value, (long)fhn.Value, -1, "sheet");
        if (problems.Count > 0)
        {
            error = problems[0];
            return null;
        }

        int fw = (int)fwn.Value, fh = (int)fhn.Value;
        var palette = ((List<object?>)o["palette"]!).Select(c => ParseHex((string)c!)).ToArray();
        var frames = (List<object?>)o["frames"]!;
        int count = frames.Count;
        int columns = Math.Min(count, SheetColumns);
        int rows = (count + columns - 1) / columns;
        int width = columns * fw;
        var rgba = new byte[width * rows * fh * 4];
        for (int i = 0; i < count; i++)
        {
            var frame = (List<object?>)frames[i]!;
            int ox = i % columns * fw, oy = i / columns * fh;
            for (int y = 0; y < fh; y++)
            {
                string row = (string)frame[y]!;
                for (int x = 0; x < fw; x++)
                {
                    char c = row[x];
                    if (c == '.')
                        continue;   // transparent: already zero
                    var (r, g, b) = palette[PackValidator.PixelIndex(c)];
                    int at = ((oy + y) * width + ox + x) * 4;
                    rgba[at] = r;
                    rgba[at + 1] = g;
                    rgba[at + 2] = b;
                    rgba[at + 3] = 255;
                }
            }
        }
        return new PixelSheetImage(fw, fh, count, columns, rows, rgba);
    }

    private static (byte R, byte G, byte B) ParseHex(string hex)
        => (Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
}
