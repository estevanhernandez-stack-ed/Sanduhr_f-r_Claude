using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>A rectangle of a pixel buffer that one Desk anchor covers (pixels of that buffer).</summary>
public sealed record AnchorBox(DeskAnchor Anchor, int X, int Y, int Width, int Height);

/// <summary>One dominant color of a wallpaper: <c>#rrggbb</c> and the share of the pixels it stands for (0 to 1).</summary>
public sealed record DominantColor(string Hex, double Share);

/// <summary>How bright (mean relative luminance, 0 to 1) and how busy (edge density, 0 to 1) the wallpaper is behind one anchor.</summary>
public sealed record AnchorReading(DeskAnchor Anchor, double Brightness, double Busyness);

/// <summary>
/// What a wallpaper looks like to a person arranging text on it, for one screen (spec, the agent-designed Desk, B): up to
/// five dominant colors, the overall brightness, and a brightness and busyness for each Desk anchor. Pure math over a
/// pixel buffer; the App decodes the image and draws it as the monitor shows it (fit mode and all). Deterministic: the
/// same buffer always gives the same numbers. Never sent anywhere by Sanduhr.
/// </summary>
public sealed record WallpaperSummary(IReadOnlyList<DominantColor> Dominant, double Brightness, IReadOnlyList<AnchorReading> Anchors)
{
    /// <summary>How many dominant colors are kept at most.</summary>
    public const int MaxDominant = 5;

    /// <summary>The dominant colors come from at most about this many pixels (an even grid over the buffer).</summary>
    public const int MaxColorSamples = 20000;

    /// <summary>A pixel is an edge when its luma changes by this much (0 to 1, over two pixels, both directions summed).</summary>
    public const double EdgeThreshold = 0.10;

    private const int MaxRegionSamples = 65536;

    /// <summary>The brightness and busyness behind <paramref name="anchor"/>, or null when the summary has none for it.</summary>
    public AnchorReading? At(DeskAnchor anchor) => Anchors.FirstOrDefault(a => a.Anchor == anchor);

    /// <summary>
    /// The anchors' regions in a buffer of <paramref name="width"/> by <paramref name="height"/>: each about 30% of the width
    /// and 24% of the height, set in from the edges (4% and 6%), centered along the middle lines. Roughly where the Desk's
    /// stacks sit at their standard size and margins.
    /// </summary>
    public static IReadOnlyList<AnchorBox> DefaultAnchors(int width, int height)
    {
        int bw = Math.Max(1, (int)Math.Round(width * 0.30)), bh = Math.Max(1, (int)Math.Round(height * 0.24));
        int mx = (int)Math.Round(width * 0.04), my = (int)Math.Round(height * 0.06);
        var boxes = new List<AnchorBox>();
        foreach (var a in DeskAnchors.All)
        {
            int x = DeskAnchors.Column(a) switch
            {
                DeskColumn.Left => mx,
                DeskColumn.Center => (width - bw) / 2,
                _ => width - bw - mx,
            };
            int y = DeskAnchors.Row(a) switch
            {
                DeskRow.Top => my,
                DeskRow.Middle => (height - bh) / 2,
                _ => height - bh - my,
            };
            boxes.Add(new AnchorBox(a, Math.Max(0, x), Math.Max(0, y), bw, bh));
        }
        return boxes;
    }

    /// <summary>
    /// The summary of a BGRA buffer (<paramref name="width"/> by <paramref name="height"/>, <paramref name="stride"/> bytes a
    /// row). Null when the buffer is empty or smaller than it says. An anchor box is clipped to the buffer; one with nothing
    /// inside it reads as the whole picture's brightness and no busyness.
    /// </summary>
    public static WallpaperSummary? From(ReadOnlySpan<byte> bgra, int width, int height, int stride, IReadOnlyList<AnchorBox> anchors)
    {
        if (width <= 0 || height <= 0 || stride < width * 4) return null;
        if (bgra.Length < (long)(height - 1) * stride + (long)width * 4) return null;

        // Colors and overall brightness from an even grid over the whole buffer.
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / MaxColorSamples)));
        var samples = new List<int>();
        double sum = 0;
        for (int y = step / 2; y < height; y += step)
        {
            for (int x = step / 2; x < width; x += step)
            {
                int o = y * stride + x * 4;
                int r = bgra[o + 2], g = bgra[o + 1], b = bgra[o];
                samples.Add((r << 16) | (g << 8) | b);
                sum += InkHex.Luminance(r, g, b);
            }
        }
        if (samples.Count == 0) return null;
        double brightness = Round(sum / samples.Count);

        var readings = new List<AnchorReading>();
        foreach (var box in anchors)
            readings.Add(ReadAnchor(bgra, width, height, stride, box, brightness));
        return new WallpaperSummary(MedianCut(samples), brightness, readings);
    }

    private static AnchorReading ReadAnchor(ReadOnlySpan<byte> bgra, int width, int height, int stride, AnchorBox box, double fallback)
    {
        int x0 = Math.Clamp(box.X, 0, width), y0 = Math.Clamp(box.Y, 0, height);
        int x1 = Math.Clamp(box.X + box.Width, 0, width), y1 = Math.Clamp(box.Y + box.Height, 0, height);
        if (x1 <= x0 || y1 <= y0) return new AnchorReading(box.Anchor, fallback, 0);
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)(x1 - x0) * (y1 - y0) / MaxRegionSamples)));
        double lum = 0, edges = 0;
        int n = 0;
        for (int y = y0; y < y1; y += step)
        {
            for (int x = x0; x < x1; x += step)
            {
                int o = y * stride + x * 4;
                lum += InkHex.Luminance(bgra[o + 2], bgra[o + 1], bgra[o]);
                double dx = Math.Abs(Luma(bgra, stride, width, height, x + step, y) - Luma(bgra, stride, width, height, x - step, y));
                double dy = Math.Abs(Luma(bgra, stride, width, height, x, y + step) - Luma(bgra, stride, width, height, x, y - step));
                if (dx + dy >= EdgeThreshold) edges++;
                n++;
            }
        }
        return new AnchorReading(box.Anchor, Round(lum / n), Round(edges / n));
    }

    /// <summary>The pixel's luma in the colors as stored (0 to 1), the edge check's measure; off the buffer reads the nearest pixel.</summary>
    private static double Luma(ReadOnlySpan<byte> bgra, int stride, int width, int height, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        int o = y * stride + x * 4;
        return (0.299 * bgra[o + 2] + 0.587 * bgra[o + 1] + 0.114 * bgra[o]) / 255.0;
    }

    private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    /// <summary>Up to <see cref="MaxDominant"/> colors by median cut: split the box with the most to gain along its widest
    /// channel at the median until there are five or nothing varies. Each color is its box's mean; each share its box's pixels.
    /// Sorted by share (largest first), then by hex, so the order is fixed.</summary>
    private static IReadOnlyList<DominantColor> MedianCut(List<int> pixels)
    {
        var all = pixels.ToArray();
        var boxes = new List<(int Start, int End)> { (0, all.Length) };
        while (boxes.Count < MaxDominant)
        {
            int pick = -1, channel = 0;
            double best = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var (range, ch) = WidestRange(all, boxes[i].Start, boxes[i].End);
                double score = (double)(boxes[i].End - boxes[i].Start) * range;
                if (range > 0 && score > best) { best = score; pick = i; channel = ch; }
            }
            if (pick < 0) break;
            var (s, e) = boxes[pick];
            Array.Sort(all, s, e - s, new ChannelOrder(channel));
            int mid = s + (e - s) / 2;
            boxes[pick] = (s, mid);
            boxes.Add((mid, e));
        }
        var result = new List<DominantColor>();
        foreach (var (s, e) in boxes)
        {
            long r = 0, g = 0, b = 0;
            for (int i = s; i < e; i++)
            {
                r += (all[i] >> 16) & 0xFF;
                g += (all[i] >> 8) & 0xFF;
                b += all[i] & 0xFF;
            }
            int n = e - s;
            string hex = string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Round((double)r / n):x2}{(int)Math.Round((double)g / n):x2}{(int)Math.Round((double)b / n):x2}");
            result.Add(new DominantColor(hex, Round((double)n / all.Length)));
        }
        return result.OrderByDescending(c => c.Share).ThenBy(c => c.Hex, StringComparer.Ordinal).ToList();
    }

    private static (int Range, int Channel) WidestRange(int[] all, int start, int end)
    {
        int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
        for (int i = start; i < end; i++)
        {
            int r = (all[i] >> 16) & 0xFF, g = (all[i] >> 8) & 0xFF, b = all[i] & 0xFF;
            rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            gMin = Math.Min(gMin, g); gMax = Math.Max(gMax, g);
            bMin = Math.Min(bMin, b); bMax = Math.Max(bMax, b);
        }
        int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
        if (rr >= gr && rr >= br) return (rr, 16);
        return gr >= br ? (gr, 8) : (br, 0);
    }

    /// <summary>Orders packed colors by one channel, then by the whole color, so no two differ in order by chance.</summary>
    private sealed class ChannelOrder(int shift) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            int c = ((x >> shift) & 0xFF).CompareTo((y >> shift) & 0xFF);
            return c != 0 ? c : x.CompareTo(y);
        }
    }
}

/// <summary>One monitor's summary, with where it sits among the monitors.</summary>
public sealed record WallpaperMonitorSummary(int Index, bool Primary, int Width, int Height, WallpaperSummary Summary);

/// <summary>
/// The wallpaper across every monitor, as <c>get_desk</c> reports it: an overall brightness and dominant colors, and each
/// monitor's own summary. The JSON is <c>{brightness, dominant: [{hex, share}], monitors: [{index, primary, width, height,
/// brightness, dominant, anchors: {tl: {brightness, busyness}, ...}}]}</c>.
/// </summary>
public sealed record WallpaperOverview(IReadOnlyList<DominantColor> Dominant, double Brightness, IReadOnlyList<WallpaperMonitorSummary> Monitors)
{
    /// <summary>Merges the monitors: brightness and colors weighted by each monitor's pixel count; colors within a short
    /// distance of each other count as one.</summary>
    public static WallpaperOverview Combine(IReadOnlyList<WallpaperMonitorSummary> monitors)
    {
        if (monitors.Count == 0) return new WallpaperOverview([], 0, []);
        double total = monitors.Sum(m => (double)Math.Max(1, m.Width) * Math.Max(1, m.Height));
        double brightness = monitors.Sum(m => m.Summary.Brightness * Math.Max(1, m.Width) * Math.Max(1, m.Height)) / total;

        var weighted = monitors
            .SelectMany(m => m.Summary.Dominant.Select(c => (c.Hex, Weight: c.Share * Math.Max(1, m.Width) * Math.Max(1, m.Height) / total)))
            .OrderByDescending(c => c.Weight).ThenBy(c => c.Hex, StringComparer.Ordinal)
            .ToList();
        var clusters = new List<(string Hex, double Weight)>();
        foreach (var c in weighted)
        {
            int at = clusters.FindIndex(k => Distance(k.Hex, c.Hex) <= 48);
            if (at >= 0) clusters[at] = (clusters[at].Hex, clusters[at].Weight + c.Weight);
            else clusters.Add(c);
        }
        var dominant = clusters.OrderByDescending(c => c.Weight).ThenBy(c => c.Hex, StringComparer.Ordinal)
            .Take(WallpaperSummary.MaxDominant)
            .Select(c => new DominantColor(c.Hex, Math.Round(c.Weight, 3, MidpointRounding.AwayFromZero)))
            .ToList();
        return new WallpaperOverview(dominant, Math.Round(brightness, 3, MidpointRounding.AwayFromZero), monitors);
    }

    private static double Distance(string a, string b)
    {
        int dr = Channel(a, 1) - Channel(b, 1), dg = Channel(a, 3) - Channel(b, 3), db = Channel(a, 5) - Channel(b, 5);
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static int Channel(string hex, int at) => int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // -- JSON ---------------------------------------------------------------------------------------------

    public JsonObject ToJson()
    {
        var monitors = new JsonArray();
        foreach (var m in Monitors)
        {
            var anchors = new JsonObject();
            foreach (var a in m.Summary.Anchors)
                anchors[DeskAnchors.Word(a.Anchor)] = new JsonObject { ["brightness"] = a.Brightness, ["busyness"] = a.Busyness };
            monitors.Add((JsonNode?)new JsonObject
            {
                ["index"] = m.Index,
                ["primary"] = m.Primary,
                ["width"] = m.Width,
                ["height"] = m.Height,
                ["brightness"] = m.Summary.Brightness,
                ["dominant"] = DominantJson(m.Summary.Dominant),
                ["anchors"] = anchors,
            });
        }
        return new JsonObject
        {
            ["brightness"] = Brightness,
            ["dominant"] = DominantJson(Dominant),
            ["monitors"] = monitors,
        };
    }

    private static JsonArray DominantJson(IReadOnlyList<DominantColor> colors)
    {
        var arr = new JsonArray();
        foreach (var c in colors) arr.Add((JsonNode?)new JsonObject { ["hex"] = c.Hex, ["share"] = c.Share });
        return arr;
    }

    /// <summary>Reads an overview back from JSON; null when it is not one (the rules that need it are then skipped).</summary>
    public static WallpaperOverview? FromJson(JsonNode? node)
    {
        try
        {
            if (node is not JsonObject o || !Num(o["brightness"], out double brightness)) return null;
            var monitors = new List<WallpaperMonitorSummary>();
            if (o["monitors"] is JsonArray arr)
            {
                foreach (var item in arr.Take(16))
                {
                    if (item is not JsonObject mo || !Num(mo["brightness"], out double mb)) return null;
                    var readings = new List<AnchorReading>();
                    if (mo["anchors"] is JsonObject ao)
                    {
                        foreach (var anchor in DeskAnchors.All)
                        {
                            if (ao[DeskAnchors.Word(anchor)] is not JsonObject r) continue;
                            if (!Num(r["brightness"], out double ab)) return null;
                            Num(r["busyness"], out double busy);
                            readings.Add(new AnchorReading(anchor, Math.Clamp(ab, 0, 1), Math.Clamp(busy, 0, 1)));
                        }
                    }
                    int index = (int)(Num(mo["index"], out double ix) ? ix : monitors.Count);
                    bool primary = mo["primary"] is JsonValue pv && pv.TryGetValue<bool>(out var p) && p;
                    int w = (int)(Num(mo["width"], out double wv) ? wv : 0), h = (int)(Num(mo["height"], out double hv) ? hv : 0);
                    monitors.Add(new WallpaperMonitorSummary(index, primary, w, h,
                        new WallpaperSummary(ReadDominant(mo["dominant"]), Math.Clamp(mb, 0, 1), readings)));
                }
            }
            return new WallpaperOverview(ReadDominant(o["dominant"]), Math.Clamp(brightness, 0, 1), monitors);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<DominantColor> ReadDominant(JsonNode? node)
    {
        var list = new List<DominantColor>();
        if (node is not JsonArray arr) return list;
        foreach (var item in arr.Take(WallpaperSummary.MaxDominant))
        {
            if (item is JsonObject c && c["hex"] is JsonValue hv && hv.TryGetValue<string>(out var hex) && InkHex.Normalize(hex) is { } h
                && Num(c["share"], out double share))
                list.Add(new DominantColor("#" + h, Math.Clamp(share, 0, 1)));
        }
        return list;
    }

    private static bool Num(JsonNode? n, out double value)
    {
        value = 0;
        if (n is not JsonValue v) return false;
        if (v.TryGetValue<double>(out value)) return double.IsFinite(value);
        if (v.TryGetValue<int>(out var i)) { value = i; return true; }
        if (v.TryGetValue<long>(out var l)) { value = l; return true; }
        return false;
    }
}
