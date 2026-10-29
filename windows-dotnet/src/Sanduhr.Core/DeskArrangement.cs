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

/// <summary>One placed piece: its widget word, its anchor, its size (1 is the piece's own size) and the monitor it is drawn on:
/// null for every monitor (what every layout written before monitors meant), <see cref="DeskMonitors.Main"/>, or a monitor's
/// device path (the id <c>IDesktopWallpaper</c> uses). <paramref name="Tilt"/> is the fold (#314): the slant, in whole degrees from
/// <see cref="DeskArrangement.MinTilt"/> to <see cref="DeskArrangement.MaxTilt"/>, of the stack the piece sits in. The pieces of one anchor
/// share one slant (<see cref="DeskArrangement.SetTilt"/>); a stack whose words disagree slants by its first piece's.
/// <paramref name="Style"/> is how a meter-like piece draws its numbers (#336): <see cref="DeskMeterStyle.Bar"/> for every other piece.</summary>
public sealed record DeskPlacement(string Widget, DeskAnchor Anchor, double Scale = 1, string? Monitor = null, double Tilt = 0,
    DeskMeterStyle Style = DeskMeterStyle.Bar);

/// <summary>
/// How a meter-like piece draws (#336), so a stack fits its column, upright or slanted. <see cref="Bar"/>: today's full-width bar with
/// its label above (the default, unchanged). <see cref="Slim"/>: the label and the value on one line over a thin bar, about half the
/// height. <see cref="Ring"/>: a small arc gauge with the value inside, the readings of one piece side by side in a row.
/// <see cref="Ticks"/>: ten segmented cells, fewer lit pixels (the OLED-friendly one). A Claude limit keeps its pace mark in every style.
/// </summary>
public enum DeskMeterStyle { Bar, Slim, Ring, Ticks }

/// <summary>Words, names and the pieces that take a <see cref="DeskMeterStyle"/>.</summary>
public static class DeskMeterStyles
{
    /// <summary>Every style, in Settings order.</summary>
    public static readonly IReadOnlyList<DeskMeterStyle> All = [DeskMeterStyle.Bar, DeskMeterStyle.Slim, DeskMeterStyle.Ring, DeskMeterStyle.Ticks];

    /// <summary>The pieces that have a style: the Claude line (which gains a compact two-row form), the Claude limits and CPU/RAM/Disk.</summary>
    public static readonly IReadOnlyList<string> Pieces = ["claude", "meters", "telemetry"];

    /// <summary>True when <paramref name="widget"/> takes a style.</summary>
    public static bool Supports(string widget) => Pieces.Contains(widget, StringComparer.Ordinal);

    /// <summary>The layout and design word: "bar", "slim", "ring", "ticks".</summary>
    public static string Word(DeskMeterStyle s) => s.ToString().ToLowerInvariant();

    /// <summary>The style for an exact lowercase word, null for anything else.</summary>
    public static DeskMeterStyle? Parse(string? word)
    {
        foreach (var s in All)
            if (string.Equals(Word(s), word, StringComparison.Ordinal)) return s;
        return null;
    }

    /// <summary>As Settings, Desk names it.</summary>
    public static string Name(DeskMeterStyle s) => s switch
    {
        DeskMeterStyle.Slim => "Slim",
        DeskMeterStyle.Ring => "Rings",
        DeskMeterStyle.Ticks => "Ticks",
        _ => "Bars",
    };

    /// <summary>The style a piece can carry: <paramref name="style"/> for a piece that has one, <see cref="DeskMeterStyle.Bar"/> for any other.</summary>
    public static DeskMeterStyle For(string widget, DeskMeterStyle style) => Supports(widget) ? style : DeskMeterStyle.Bar;
}

/// <summary>A monitor as the Desk and <c>get_desk</c> know it. <paramref name="Id"/> is its device path, stable across restarts and
/// resolution changes; <paramref name="Name"/> is how a person is told which one it is ("3440×1440, main"); the size is in physical
/// pixels and the position is where its top-left sits on the virtual screen.</summary>
public sealed record DeskMonitorInfo(string Id, string Name, int Width, int Height, bool Primary, int Left, int Top);

/// <summary>A screen the Desk draws on, as far as monitor placement cares: its id (null when Windows would not say) and whether it is the main one.</summary>
public sealed record DeskScreenRef(string? Id, bool Primary);

/// <summary>
/// Which monitor a piece is on (spec, Desk pieces per monitor). A placement's monitor is null (every monitor), <see cref="Main"/>
/// (whichever is primary now) or a device path. Pure: the screens the Desk has now are passed in.
/// </summary>
public static class DeskMonitors
{
    /// <summary>The word for the main monitor, in a layout string and in a design.</summary>
    public const string Main = "main";

    /// <summary>The word for every monitor, in a design (a layout string writes nothing).</summary>
    public const string All = "all";

    /// <summary>The longest monitor id a layout or a design may carry.</summary>
    public const int MaxIdLength = 300;

    /// <summary>A design's or a layout's word as a placement monitor: "all", blank or null is every monitor (null); "main" is
    /// <see cref="Main"/>; anything else is an id, kept as given.</summary>
    public static string? FromWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        var t = word.Trim();
        if (string.Equals(t, All, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(t, Main, StringComparison.OrdinalIgnoreCase)) return Main;
        return t;
    }

    /// <summary>A placement monitor as a design's word: "all", "main" or the id.</summary>
    public static string Word(string? monitor) => FromWord(monitor) ?? All;

    /// <summary>True for a device path (neither every monitor nor the main one).</summary>
    public static bool IsId(string? monitor) => FromWord(monitor) is { } m && m != Main;

    /// <summary>Whether two monitor words name the same monitor ("all" is null; ids compare without regard to case).</summary>
    public static bool Same(string? a, string? b) => string.Equals(FromWord(a), FromWord(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a piece placed on <paramref name="monitor"/> is drawn on <paramref name="screen"/>. Every monitor: always. Main: on the
    /// primary one. A device path: on the screen with that id; and while no connected screen has that id (the monitor is unplugged,
    /// or Windows did not say) on the primary screen, so nothing disappears. This is resolution only: the stored layout is never rewritten.
    /// </summary>
    public static bool ShowsOn(string? monitor, DeskScreenRef screen, IReadOnlyCollection<string> connectedIds)
    {
        var m = FromWord(monitor);
        if (m is null) return true;
        if (m == Main) return screen.Primary;
        if (IsConnected(m, connectedIds))
            return screen.Id is not null && string.Equals(screen.Id, m, StringComparison.OrdinalIgnoreCase);
        return screen.Primary;
    }

    /// <summary>The value that puts a piece on this one screen: the word for the main monitor on the primary screen (it then follows the
    /// primary), else the screen's own device path; the main monitor again when Windows gave it no id.</summary>
    public static string ThisScreen(DeskScreenRef screen) => screen.Primary || screen.Id is null ? Main : screen.Id;

    /// <summary>What "Show on" does to a piece's monitor in Arrange mode: a piece on every monitor goes to this screen only; a piece on one
    /// monitor goes back to every monitor.</summary>
    public static string? ToggleShowOn(string? current, DeskScreenRef screen) => FromWord(current) is null ? ThisScreen(screen) : null;

    /// <summary>
    /// Which screen a bar over the Desk opens on (the design review's Apply / Dismiss, Arrange mode's Done / Cancel): the one that
    /// draws the most of <paramref name="pieces"/>, so the bar sits where the change is (#319). A piece on every monitor counts on
    /// each. A tie that includes the primary screen, no pieces, or no screen with any goes to the primary; another tie to the first
    /// screen in the list. The index into <paramref name="screens"/>, or -1 when there are none.
    /// </summary>
    public static int BarScreen(IReadOnlyList<DeskScreenRef> screens, IEnumerable<DeskPlacement> pieces, IReadOnlyCollection<string> connectedIds)
    {
        if (screens.Count == 0) return -1;
        int primary = Math.Max(0, screens.ToList().FindIndex(s => s.Primary));
        var list = pieces.ToList();
        var counts = screens.Select(s => list.Count(p => ShowsOn(p.Monitor, s, connectedIds))).ToList();
        int most = counts.Max();
        if (most == 0 || counts[primary] == most) return primary;
        return counts.IndexOf(most);
    }

    /// <summary>
    /// The pieces a design review is about: those the proposed layout places differently from the current one (moved, resized, slanted, put on
    /// another monitor, or newly shown); when the layout does not change, every piece the proposal places.
    /// </summary>
    public static IReadOnlyList<DeskPlacement> ReviewPieces(DeskArrangement current, DeskArrangement proposed)
    {
        var changed = proposed.Pieces.Where(p => current.Placement(p.Widget) is not { } was
            || was.Anchor != p.Anchor || was.Scale != p.Scale || was.Tilt != p.Tilt || was.Style != p.Style || !Same(was.Monitor, p.Monitor)).ToList();
        return changed.Count > 0 ? changed : proposed.Pieces;
    }

    /// <summary>Whether a device path is one of the connected monitors.</summary>
    public static bool IsConnected(string? monitor, IReadOnlyCollection<string> connectedIds) =>
        FromWord(monitor) is { } m && m != Main && connectedIds.Contains(m, StringComparer.OrdinalIgnoreCase);

    // What a layout word carries as it is: printable ASCII but the characters that separate words, pieces and fields.
    private static bool Plain(int c) => c is > ' ' and < 0x7F && c is not ('%' or ',' or ':' or '@');

    /// <summary>A monitor id made safe for a layout word: a space, a comma, a colon, an at sign, a percent sign, a control character or a
    /// non-ASCII character becomes %XX of its UTF-8 bytes, so nothing in an id can break the list.</summary>
    public static string Escape(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length + 8);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in id.EnumerateRunes())
        {
            if (Plain(rune.Value)) { sb.Append((char)rune.Value); continue; }
            int n = rune.EncodeToUtf8(bytes);
            for (int i = 0; i < n; i++) sb.Append('%').Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The id back from <see cref="Escape"/>. A % that is not followed by two hex digits stays as it is.</summary>
    public static string Unescape(string word)
    {
        if (!word.Contains('%')) return word;
        var bytes = new List<byte>(word.Length);
        for (int i = 0; i < word.Length; i++)
        {
            if (word[i] == '%' && i + 2 < word.Length &&Uri.IsHexDigit(word[i + 1]) && Uri.IsHexDigit(word[i + 2]))
            {
                bytes.Add(byte.Parse(word.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
            }
            else
            {
                int len = char.IsHighSurrogate(word[i]) && i + 1 < word.Length ? 2 : 1;
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(word.Substring(i, len)));
                i += len - 1;
            }
        }
        return System.Text.Encoding.UTF8.GetString([.. bytes]);
    }
}

/// <summary>
/// The Desk layout string read and written. Each word is <c>widget:anchor</c>,
/// <c>widget:anchor:scale</c> or <c>widget:anchor:scale:tilt</c> (the fold, #314: whole degrees, written only when not 0, with the
/// scale written as 1 to hold its place; a build from before the fold reads the scale and ignores the tilt), or
/// <c>widget:anchor:scale:tilt:style</c> (the meter style, #336: <c>meters:tr:1:0:ring</c>, written only when not <c>bar</c>, with the
/// scale and the tilt holding their places; a build from before it reads the size and the tilt and ignores the style, so it draws bars),
/// with an optional <c>@monitor</c> at its end (<c>clock:tl@main</c>, or a monitor's device path with
/// <c>,</c> <c>:</c> <c>@</c> <c>%</c> and spaces escaped as %XX; no suffix is every monitor, so every older layout reads and writes
/// unchanged); within an anchor, pieces stack top to bottom in the string order, and a screen draws only the pieces
/// <see cref="ForScreen"/> gives it.
/// A word for a widget this build does not know is dropped; an anchor it does not know puts the
/// piece at its default anchor instead of hiding it; a size it cannot read is 1. A widget named
/// twice keeps its last word. A size of 1 is not written. Ported from the Mac DeskArrangement.
/// </summary>
public sealed class DeskArrangement
{
    public const double MinScale = 0.6;
    public const double MaxScale = 1.6;
    public const double ScaleStep = 0.1;

    /// <summary>The fold's range (#314): a stack slants at most this many degrees either way. Negative rises to the right, positive falls.</summary>
    public const double MinTilt = -20;
    public const double MaxTilt = 20;

    /// <summary>The layout a fresh install draws.</summary>
    public const string Standard = "clock:bl claude:bl";

    /// <summary>The widget words this build knows, in Settings order (also the order a newly placed piece takes among its neighbors):
    /// the Mac's list with Windows' telemetry and weather (S6, after Este's Knighthawk skins) before watchers.</summary>
    public static readonly IReadOnlyList<string> Known =
        ["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "meetings", "glow", "companion"];

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
            // An optional "@monitor" ends the word (an escaped id, or "main"); no suffix is every monitor, as every older layout meant.
            int at = word.IndexOf('@');
            string head = at < 0 ? word : word[..at];
            string? monitor = at < 0 ? null : DeskMonitors.FromWord(DeskMonitors.Unescape(word[(at + 1)..]));
            var bits = head.Split(':', StringSplitOptions.RemoveEmptyEntries);
            // A drawer piece (5.0) is drawer.<id>, any number of them; every other word must be one this build knows.
            if (bits.Length < 2 || !(Known.Contains(bits[0], StringComparer.Ordinal) || DrawerPieces.IsWord(bits[0]))) continue;
            var anchor = DeskAnchors.Parse(bits[1]) ?? DefaultAnchor(bits[0]);
            var scale = 1.0;
            if (bits.Length >= 3 &&
                double.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                scale = ClampScale(s);
            var tilt = 0.0;
            if (bits.Length >= 4 &&
                double.TryParse(bits[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                tilt = ClampTilt(t);
            // The meter style (#336) is the fifth field; a word this build does not know, or a style on a piece that has none, is a bar.
            var style = bits.Length >= 5 && DeskMeterStyles.Parse(bits[4]) is { } st ? DeskMeterStyles.For(bits[0], st) : DeskMeterStyle.Bar;
            _pieces.RemoveAll(p => p.Widget == bits[0]);
            _pieces.Add(new DeskPlacement(bits[0], anchor, scale, monitor, tilt, style));
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

    /// <summary>Whole degrees within <see cref="MinTilt"/> to <see cref="MaxTilt"/>; anything unreadable is 0 (no slant).</summary>
    public static double ClampTilt(double t)
    {
        if (!double.IsFinite(t)) return 0;
        var whole = Math.Round(Math.Clamp(t, MinTilt, MaxTilt), MidpointRounding.AwayFromZero);
        return whole == 0 ? 0 : whole;   // never -0
    }

    /// <summary>Where a piece goes when its anchor is not one this build knows: the message top left, telemetry top
    /// right, weather middle right, the glow top center, the companion bottom right, the rest bottom left.</summary>
    public static DeskAnchor DefaultAnchor(string widget) => widget switch
    {
        "message" => DeskAnchor.TL,
        "telemetry" => DeskAnchor.TR,
        "weather" => DeskAnchor.MR,
        "glow" => DeskAnchor.TC,
        "companion" => DeskAnchor.BR,
        _ when DrawerPieces.IsWord(widget) => DeskAnchor.TL,
        _ => DeskAnchor.BL,
    };

    /// <summary>
    /// Where Settings and Arrange put a piece they show (Settings v2, Principle 4): Desk, Pieces' Show box, "Place it on the
    /// Desk", a feature's own on-action, Arrange's Hidden strip, a companion install and the glow. One table, so a place is one
    /// line to change. The rows marked "proposal" are the spec's proposals for the owner to confirm (settings-v2 spec, Desk);
    /// the clock, the Claude meters and the glow keep the place they had before v2. <see cref="DefaultAnchor"/> stays the
    /// layout and design formats' fallback for an anchor this build can't read. A design that unhides a piece without an anchor
    /// uses this table too (the merge goes through <c>Show</c>).
    /// </summary>
    public static DeskAnchor DefaultPlaceFor(string widget) => widget switch
    {
        "message" => DeskAnchor.BL,      // proposal: bottom left
        "nowPlaying" => DeskAnchor.BR,   // proposal: bottom right
        "weather" => DeskAnchor.TR,      // proposal: top right
        "watchers" => DeskAnchor.MR,     // proposal: right (the middle of the right edge)
        "launcher" => DeskAnchor.TL,     // proposal: top left
        "companion" => DeskAnchor.BC,    // proposal: bottom center
        "telemetry" => DeskAnchor.TR,    // proposal: top right
        _ => DefaultAnchor(widget),      // clock, claude, meters, meetings: bottom left; glow: top center (as before v2)
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
        var tilt = ClampTilt(p.Tilt);
        var style = DeskMeterStyles.For(p.Widget, p.Style);
        var word = p.Scale == 1 && tilt == 0 && style == DeskMeterStyle.Bar ? head : head + ":" + p.Scale.ToString("G6", CultureInfo.InvariantCulture);
        // The fold rides after the size; no slant writes nothing, so every layout from before it reads and writes as it did.
        if (tilt != 0 || style != DeskMeterStyle.Bar) word += ":" + tilt.ToString("0", CultureInfo.InvariantCulture);
        // The meter style rides after the tilt (#336); a bar writes nothing.
        if (style != DeskMeterStyle.Bar) word += ":" + DeskMeterStyles.Word(style);
        // Every monitor writes nothing, so a layout that never named a monitor reads and writes exactly as before.
        return DeskMonitors.FromWord(p.Monitor) is { } monitor ? word + "@" + DeskMonitors.Escape(monitor) : word;
    }

    /// <summary>The pieces drawn on one screen: those on every monitor, those on the main monitor when this is it, and those on this
    /// screen's own monitor (or, while that monitor is not connected, on the main one). The order is the layout's. The stored layout is untouched.</summary>
    public DeskArrangement ForScreen(DeskScreenRef screen, IReadOnlyCollection<string> connectedIds) =>
        new(_pieces.Where(p => DeskMonitors.ShowsOn(p.Monitor, screen, connectedIds)));

    /// <summary>Puts a placed piece on <paramref name="monitor"/> (null: every monitor; <see cref="DeskMonitors.Main"/>; a device path).
    /// Its anchor, size and place in the stack stay. Nothing changes for a piece that is not placed.</summary>
    public void SetMonitor(string widget, string? monitor)
    {
        var i = _pieces.FindIndex(p => p.Widget == widget);
        if (i < 0) return;
        _pieces[i] = _pieces[i] with { Monitor = DeskMonitors.FromWord(monitor) };
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
        // Into a stack, a piece takes its slant (one per stack); into an empty anchor it keeps its own.
        var moved = new DeskPlacement(widget, a, old?.Scale ?? 1, old?.Monitor, StackTilt(a) ?? old?.Tilt ?? 0, old?.Style ?? DeskMeterStyle.Bar);
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
        var piece = _pieces[from];
        if (piece.Anchor != anchor && StackTilt(anchor) is { } joined) piece = piece with { Tilt = joined };
        piece = piece with { Anchor = anchor };
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

    /// <summary>The anchors a newly added piece (the Launcher's first button) is offered, in order: the four corners, bottom left first.</summary>
    public static readonly IReadOnlyList<DeskAnchor> AutoPlaceOrder = [DeskAnchor.BL, DeskAnchor.BR, DeskAnchor.TL, DeskAnchor.TR];

    /// <summary>
    /// Where to put a piece that has no place yet: the first of <see cref="AutoPlaceOrder"/> (bottom left, bottom right,
    /// top left, top right) with no piece in it, else bottom left, to stack with what is there.
    /// </summary>
    public DeskAnchor FirstFreeAnchor() =>
        AutoPlaceOrder.Cast<DeskAnchor?>().FirstOrDefault(a => !_pieces.Any(p => p.Anchor == a)) ?? DeskAnchor.BL;

    /// <summary>Takes <paramref name="widget"/> off the Desk, its size and all; the rest keep theirs.</summary>
    public void Hide(string widget) => Place(widget, null);

    /// <summary>
    /// Puts a hidden <paramref name="widget"/> back at its <see cref="DefaultPlaceFor"/> place at size 1, seated by Settings
    /// order. A placed piece stays where it is; a word this build does not know is ignored.
    /// </summary>
    public void Show(string widget) => PlaceAtDefault(widget);

    /// <summary>Places a hidden <paramref name="widget"/> at <paramref name="remembered"/> or else <see cref="DefaultPlaceFor"/>,
    /// seated by Settings order, and returns where. Null, with nothing changed, when it is already placed or this build doesn't
    /// know the word.</summary>
    public DeskAnchor? PlaceAtDefault(string widget, DeskAnchor? remembered = null)
    {
        if (Placement(widget) is not null || !Known.Contains(widget, StringComparer.Ordinal)) return null;
        var at = remembered ?? DefaultPlaceFor(widget);
        Place(widget, at);
        return at;
    }

    /// <summary>Where <paramref name="widget"/> sits in its place's stack, top first, and how many pieces share the place;
    /// (-1, 0) for a hidden piece. Desk, Pieces' Order buttons read it.</summary>
    public (int Index, int Count) StackPosition(string widget)
    {
        if (Placement(widget) is not { } p) return (-1, 0);
        var stack = Stack(p.Anchor);
        return (stack.ToList().FindIndex(x => x.Widget == widget), stack.Count);
    }

    /// <summary>
    /// The pieces the layout leaves out, in Settings order (Arrange mode's Hidden strip). Limited to
    /// <paramref name="among"/> when given (the pieces the Desk draws), else every known piece.
    /// </summary>
    public IReadOnlyList<string> Hidden(IEnumerable<string>? among = null)
    {
        var offered = among is null ? Known : Known.Where(new HashSet<string>(among, StringComparer.Ordinal).Contains).ToList();
        return offered.Where(w => Placement(w) is null).ToList();
    }

    /// <summary>True when none of the pieces in <paramref name="among"/> (default: every known one) is placed.</summary>
    public bool PlacesNothing(IEnumerable<string>? among = null) =>
        !(among ?? Known).Any(w => Placement(w) is not null);

    /// <summary>Sets a placed piece's size (snapped and kept within range). A hidden piece has none.</summary>
    public void SetScale(string widget, double scale)
    {
        var i = _pieces.FindIndex(p => p.Widget == widget);
        if (i < 0) return;
        _pieces[i] = _pieces[i] with { Scale = ClampScale(scale) };
    }

    /// <summary>Sets a placed piece's meter style (#336); a piece that has no style stays a bar, and a hidden piece has none.</summary>
    public void SetStyle(string widget, DeskMeterStyle style)
    {
        var i = _pieces.FindIndex(p => p.Widget == widget);
        if (i < 0) return;
        _pieces[i] = _pieces[i] with { Style = DeskMeterStyles.For(widget, style) };
    }

    /// <summary>The slant of the stack at <paramref name="anchor"/>: its first piece's; null when the anchor has none.</summary>
    public double? StackTilt(DeskAnchor anchor) => _pieces.FirstOrDefault(p => p.Anchor == anchor) is { } first ? first.Tilt : null;

    /// <summary>
    /// Slants the stack <paramref name="widget"/> sits in (#314): every piece at its anchor takes the tilt (whole degrees, kept
    /// within range), so a stack reads as one slanted face. Nothing changes for a piece that is not placed.
    /// </summary>
    public void SetTilt(string widget, double tilt)
    {
        if (Placement(widget) is not { } p) return;
        var t = ClampTilt(tilt);
        for (int i = 0; i < _pieces.Count; i++)
            if (_pieces[i].Anchor == p.Anchor) _pieces[i] = _pieces[i] with { Tilt = t };
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
        piece = piece with { Anchor = _pieces[to].Anchor, Tilt = same ? piece.Tilt : _pieces[to].Tilt };
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
