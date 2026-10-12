using System.Globalization;

namespace Sanduhr.Core;

/// <summary>
/// Where a Desk piece sits: the four corners, the top and bottom centers, and the middles of the
/// left and right edges. The lowercase names are the layout string's words.
/// </summary>
public enum DeskAnchor { TL, TC, TR, ML, MR, BL, BC, BR }

/// <summary>An anchor's column on the screen.</summary>
public enum DeskColumn { Left, Center, Right }

/// <summary>An anchor's row on the screen.</summary>
public enum DeskRow { Top, Middle, Bottom }

/// <summary>Words, columns, rows and names for <see cref="DeskAnchor"/>.</summary>
public static class DeskAnchors
{
    /// <summary>Every anchor, in enum order.</summary>
    public static readonly IReadOnlyList<DeskAnchor> All =
        [DeskAnchor.TL, DeskAnchor.TC, DeskAnchor.TR, DeskAnchor.ML, DeskAnchor.MR, DeskAnchor.BL, DeskAnchor.BC, DeskAnchor.BR];

    /// <summary>The layout word: "tl" to "br".</summary>
    public static string Word(DeskAnchor a) => a.ToString().ToLowerInvariant();

    /// <summary>The anchor for an exact lowercase layout word, null for anything else.</summary>
    public static DeskAnchor? Parse(string? word)
    {
        foreach (var a in All)
            if (string.Equals(Word(a), word, StringComparison.Ordinal)) return a;
        return null;
    }

    public static DeskColumn Column(DeskAnchor a) => a switch
    {
        DeskAnchor.TL or DeskAnchor.ML or DeskAnchor.BL => DeskColumn.Left,
        DeskAnchor.TC or DeskAnchor.BC => DeskColumn.Center,
        _ => DeskColumn.Right,
    };

    public static DeskRow Row(DeskAnchor a) => a switch
    {
        DeskAnchor.TL or DeskAnchor.TC or DeskAnchor.TR => DeskRow.Top,
        DeskAnchor.ML or DeskAnchor.MR => DeskRow.Middle,
        _ => DeskRow.Bottom,
    };

    /// <summary>As Settings, Desk, Layout names it.</summary>
    public static string Name(DeskAnchor a) => a switch
    {
        DeskAnchor.TL => "Top left",
        DeskAnchor.TC => "Top center",
        DeskAnchor.TR => "Top right",
        DeskAnchor.ML => "Middle left",
        DeskAnchor.MR => "Middle right",
        DeskAnchor.BL => "Bottom left",
        DeskAnchor.BC => "Bottom center",
        _ => "Bottom right",
    };

    /// <summary>The anchor in <paramref name="column"/> at <paramref name="row"/>; null for the center's middle (the open middle of the screen).</summary>
    public static DeskAnchor? At(DeskColumn column, DeskRow row)
    {
        foreach (var a in All)
            if (Column(a) == column && Row(a) == row) return a;
        return null;
    }
}

/// <summary>One placed piece: its widget word, its anchor and its size (1 is the piece's own size).</summary>
public sealed record DeskPlacement(string Widget, DeskAnchor Anchor, double Scale = 1);

/// <summary>
/// The Desk layout string read and written. Each word is <c>widget:anchor</c> or
/// <c>widget:anchor:scale</c>; within an anchor, pieces stack top to bottom in the string order.
/// A word for a widget this build does not know is dropped; an anchor it does not know puts the
/// piece at its default anchor instead of hiding it; a size it cannot read is 1. A widget named
/// twice keeps its last word. A size of 1 is not written. Ported from the Mac DeskArrangement.
/// </summary>
public sealed class DeskArrangement
{
    public const double MinScale = 0.6;
    public const double MaxScale = 1.6;
    public const double ScaleStep = 0.1;

    /// <summary>The layout a fresh install draws.</summary>
    public const string Standard = "clock:bl claude:bl";

    /// <summary>The widget words this build knows, in Settings order (also the order a newly placed piece takes among its neighbors):
    /// the Mac's list with Windows' telemetry and weather (S6, after Este's Knighthawk skins) before watchers.</summary>
    public static readonly IReadOnlyList<string> Known =
        ["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "meetings", "glow"];

    /// <summary>The widgets Stage 5 draws.</summary>
    public static readonly IReadOnlyList<string> DrawnInS5 = ["clock", "claude", "meters"];

    /// <summary>The size steps Settings offers: 0.6 to 1.6, eleven values.</summary>
    public static IReadOnlyList<double> ScaleSteps { get; } =
        Enumerable.Range(0, 11).Select(i => ClampScale(MinScale + i * ScaleStep)).ToArray();

    private readonly List<DeskPlacement> _pieces = [];

    public DeskArrangement() { }

    public DeskArrangement(IEnumerable<DeskPlacement> pieces) => _pieces.AddRange(pieces);

    public DeskArrangement(string? layout)
    {
        if (string.IsNullOrEmpty(layout)) return;
        foreach (var word in layout.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = word.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length < 2 || !Known.Contains(bits[0], StringComparer.Ordinal)) continue;
            var anchor = DeskAnchors.Parse(bits[1]) ?? DefaultAnchor(bits[0]);
            var scale = 1.0;
            if (bits.Length >= 3 &&
                double.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                scale = ClampScale(s);
            _pieces.RemoveAll(p => p.Widget == bits[0]);
            _pieces.Add(new DeskPlacement(bits[0], anchor, scale));
        }
    }

    /// <summary>Snaps to the step and keeps within the range; anything unreadable is 1.</summary>
    public static double ClampScale(double s)
    {
        if (!double.IsFinite(s)) return 1;
        var snapped = Math.Round(s / ScaleStep, MidpointRounding.AwayFromZero) * ScaleStep;
        var clamped = Math.Min(MaxScale, Math.Max(MinScale, snapped));
        return Math.Round(clamped * 100, MidpointRounding.AwayFromZero) / 100;
    }

    /// <summary>Where a piece goes when its anchor is not one this build knows: the message top left, telemetry top
    /// right, weather middle right, the rest bottom left.</summary>
    public static DeskAnchor DefaultAnchor(string widget) => widget switch
    {
        "message" => DeskAnchor.TL,
        "telemetry" => DeskAnchor.TR,
        "weather" => DeskAnchor.MR,
        "glow" => DeskAnchor.TC,
        _ => DeskAnchor.BL,
    };

    /// <summary>The widget's place in Settings order; an unknown word sorts last.</summary>
    public static int Rank(string widget)
    {
        for (var i = 0; i < Known.Count; i++)
            if (Known[i] == widget) return i;
        return Known.Count;
    }

    /// <summary>Known widgets, each once, in layout order.</summary>
    public IReadOnlyList<DeskPlacement> Pieces => _pieces;

    /// <summary>The layout string: a word per piece, in order.</summary>
    public string Layout => string.Join(' ', _pieces.Select(Word));

    private static string Word(DeskPlacement p)
    {
        var head = $"{p.Widget}:{DeskAnchors.Word(p.Anchor)}";
        // The Mac writes the size with printf "%g": six significant digits, no trailing zeros.
        return p.Scale == 1 ? head : head + ":" + p.Scale.ToString("G6", CultureInfo.InvariantCulture);
    }

    public DeskPlacement? Placement(string widget) => _pieces.FirstOrDefault(p => p.Widget == widget);

    /// <summary>One anchor's stack, every piece placed there, top to bottom.</summary>
    public IReadOnlyList<DeskPlacement> Stack(DeskAnchor anchor) => _pieces.Where(p => p.Anchor == anchor).ToList();

    /// <summary>
    /// Each anchor's stack of the pieces that are drawn (default: <see cref="DrawnInS5"/>).
    /// Anchors with none are absent.
    /// </summary>
    public IReadOnlyDictionary<DeskAnchor, IReadOnlyList<DeskPlacement>> Stacks(IEnumerable<string>? drawn = null)
    {
        var set = new HashSet<string>(drawn ?? DrawnInS5, StringComparer.Ordinal);
        var lists = new Dictionary<DeskAnchor, List<DeskPlacement>>();
        foreach (var p in _pieces.Where(p => set.Contains(p.Widget)))
        {
            if (!lists.TryGetValue(p.Anchor, out var l)) lists[p.Anchor] = l = [];
            l.Add(p);
        }
        return lists.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DeskPlacement>)kv.Value);
    }

    /// <summary>
    /// Puts <paramref name="widget"/> at <paramref name="anchor"/>, or hides it (null). Already there:
    /// nothing changes. Into an anchor that has pieces, it takes its place among them by Settings
    /// order. Into an empty anchor, a placed piece keeps its place in the string and a new one goes
    /// where Settings order puts it. A piece keeps its size when it moves.
    /// </summary>
    public void Place(string widget, DeskAnchor? anchor)
    {
        var old = Placement(widget);
        if (anchor is not { } a)
        {
            _pieces.RemoveAll(p => p.Widget == widget);
            return;
        }
        if (old?.Anchor == a) return;
        var moved = new DeskPlacement(widget, a, old?.Scale ?? 1);
        var rank = Rank(widget);
        if (!_pieces.Any(p => p.Anchor == a))
        {
            var i = _pieces.FindIndex(p => p.Widget == widget);
            if (i >= 0) _pieces[i] = moved;
            else
            {
                var at = _pieces.FindIndex(p => Rank(p.Widget) > rank);
                _pieces.Insert(at < 0 ? _pieces.Count : at, moved);
            }
            return;
        }
        _pieces.RemoveAll(p => p.Widget == widget);
        var there = Enumerable.Range(0, _pieces.Count).Where(i => _pieces[i].Anchor == a).ToList();
        var first = there.FindIndex(i => Rank(_pieces[i].Widget) > rank);
        var insertAt = first >= 0 ? there[first] : (there.Count > 0 ? there[^1] + 1 : _pieces.Count);
        _pieces.Insert(insertAt, moved);
    }

    /// <summary>
    /// Puts a placed piece at <paramref name="anchor"/>, in front of <paramref name="before"/> in that
    /// anchor's stack (null: at its bottom). Into an empty anchor it keeps its place in the string, as
    /// <see cref="Place"/> does. The piece keeps its size. Nothing changes for a piece that is not placed,
    /// or when <paramref name="before"/> is the piece itself.
    /// </summary>
    public void Put(string widget, DeskAnchor anchor, string? before)
    {
        if (widget == before) return;
        var from = _pieces.FindIndex(p => p.Widget == widget);
        if (from < 0) return;
        var piece = _pieces[from] with { Anchor = anchor };
        _pieces.RemoveAt(from);
        var i = before is null ? -1 : _pieces.FindIndex(p => p.Widget == before && p.Anchor == anchor);
        if (i >= 0)
        {
            _pieces.Insert(i, piece);
            return;
        }
        var last = _pieces.FindLastIndex(p => p.Anchor == anchor);
        if (last >= 0) _pieces.Insert(last + 1, piece);
        else _pieces.Insert(Math.Min(from, _pieces.Count), piece);
    }

    /// <summary>Sets a placed piece's size (snapped and kept within range). A hidden piece has none.</summary>
    public void SetScale(string widget, double scale)
    {
        var i = _pieces.FindIndex(p => p.Widget == widget);
        if (i < 0) return;
        _pieces[i] = _pieces[i] with { Scale = ClampScale(scale) };
    }

    /// <summary>
    /// Drops <paramref name="widget"/> onto <paramref name="onto"/>: in the same anchor it takes the
    /// target's place (coming from above it lands after, from below before); from another anchor it
    /// joins the target's anchor just before it. Nothing changes when either is not placed or they are the same.
    /// </summary>
    public void Move(string widget, string onto)
    {
        if (widget == onto) return;
        var from = _pieces.FindIndex(p => p.Widget == widget);
        var to = _pieces.FindIndex(p => p.Widget == onto);
        if (from < 0 || to < 0) return;
        var piece = _pieces[from];
        var same = piece.Anchor == _pieces[to].Anchor;
        piece = piece with { Anchor = _pieces[to].Anchor };
        _pieces.RemoveAt(from);
        var target = _pieces.FindIndex(p => p.Widget == onto);
        if (target < 0) target = _pieces.Count;
        _pieces.Insert(same && from <= to ? target + 1 : target, piece);
    }

    /// <summary>Moves <paramref name="widget"/> one place up (-1) or down (+1) in its stack; the stack ends stay put.</summary>
    public void Move(string widget, int step)
    {
        var p = Placement(widget);
        if (p is null) return;
        var stack = Stack(p.Anchor).Select(x => x.Widget).ToList();
        var i = stack.IndexOf(widget);
        if (i < 0 || i + step < 0 || i + step >= stack.Count) return;
        Move(widget, stack[i + step]);
    }
}
