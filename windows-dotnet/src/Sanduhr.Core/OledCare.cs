using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// "OLED care" (Settings, Desk; spec 3.7.0). Windows cannot reliably tell an OLED panel from any other, so it is
/// a switch: off by default, and when on the Desk drifts a few pixels every few minutes (<see cref="OledDrift"/>),
/// the Desk and the widget dim while you are away (<see cref="OledIdle"/>), and the Desk's ink never holds pure
/// white (<see cref="DeskInk.CapWhite"/>).
/// </summary>
public static class OledCare
{
    /// <summary>The settings key (settings.json <c>desk_oled_care</c>).</summary>
    public const string Key = "desk_oled_care";

    /// <summary>The switch is off when the key is absent.</summary>
    public const bool DefaultOn = false;

    public const string Title = "OLED care";
    public const string Caption = "For OLED screens: the Desk drifts a few pixels every few minutes, dims when you're away, and never holds pure white.";

    /// <summary>Whether the switch is on in <paramref name="root"/> (the settings.json object); a missing or odd value is off.</summary>
    public static bool Read(JsonObject? root) =>
        root?[Key] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : DefaultOn;
}

/// <summary>
/// The slow orbit a Desk island's window follows with OLED care on. Pure geometry: the host moves the window,
/// never its content, so the plate's padding stays where it is.
/// </summary>
public static class OledDrift
{
    /// <summary>The farthest a window strays from its anchor, per axis, in physical pixels.</summary>
    public const int Radius = 6;

    /// <summary>Points in one loop of the orbit.</summary>
    public const int Steps = 16;

    /// <summary>How often the orbit moves on to its next point.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A figure-eight (a 1:2 Lissajous curve) sampled at 16 points: x = 6 sin(k pi / 8), y = 6 sin(k pi / 4), rounded.
    /// Each side of the eight sums to nothing, so the loop's average is the anchor; successive points always differ;
    /// it takes 48 minutes to come round, and no pixel row or column is held for long.
    /// </summary>
    private static readonly (int X, int Y)[] Orbit =
    [
        (0, 0), (2, 4), (4, 6), (6, 4), (6, 0), (6, -4), (4, -6), (2, -4),
        (0, 0), (-2, 4), (-4, 6), (-6, 4), (-6, 0), (-6, -4), (-4, -6), (-2, -4),
    ];

    /// <summary>The offset from the anchor at <paramref name="step"/> (any integer; the loop repeats every <see cref="Steps"/>).</summary>
    public static (int X, int Y) Offset(int step) => Orbit[((step % Steps) + Steps) % Steps];

    /// <summary>
    /// <paramref name="rect"/> moved the least that puts it inside <paramref name="area"/> less <paramref name="clearance"/>
    /// (the taskbar's reach, in pixels). A rect larger than that region sits at its top left.
    /// </summary>
    public static PxRect Clamp(PxRect rect, PxRect area, DeskInsets clearance)
    {
        var (loX, hiX, loY, hiY) = Region(area, clearance);
        return rect with
        {
            X = Axis(rect.X, rect.Width, loX, hiX, relaxTo: null),
            Y = Axis(rect.Y, rect.Height, loY, hiY, relaxTo: null),
        };
    }

    /// <summary>
    /// <paramref name="home"/> (the piece's true place) moved to the orbit's point at <paramref name="step"/>, then clamped
    /// as <see cref="Clamp"/>. A piece the layout already put outside the region is never pulled in by drift: it may move
    /// along the orbit, but never farther out than it started.
    /// </summary>
    public static PxRect Drifted(PxRect home, int step, PxRect area, DeskInsets clearance)
    {
        var (dx, dy) = Offset(step);
        var (loX, hiX, loY, hiY) = Region(area, clearance);
        return home with
        {
            X = Axis(home.X + dx, home.Width, loX, hiX, relaxTo: home.X),
            Y = Axis(home.Y + dy, home.Height, loY, hiY, relaxTo: home.Y),
        };
    }

    private static (int LoX, int HiX, int LoY, int HiY) Region(PxRect area, DeskInsets c) =>
        (area.X + Ceil(c.Left), area.Right - Ceil(c.Right), area.Y + Ceil(c.Top), area.Bottom - Ceil(c.Bottom));

    private static int Ceil(double v) => v > 0 ? (int)Math.Ceiling(v) : 0;

    /// <summary>One axis: where a span of <paramref name="size"/> starting at <paramref name="pos"/> lands between <paramref name="lo"/> and
    /// <paramref name="hi"/> (the region's edges). <paramref name="relaxTo"/> widens the allowed range to include a start the layout chose.</summary>
    private static int Axis(int pos, int size, int lo, int hi, int? relaxTo)
    {
        int max = Math.Max(lo, hi - size);
        if (relaxTo is { } home)
        {
            lo = Math.Min(lo, home);
            max = Math.Max(max, home);
        }
        return Math.Clamp(pos, lo, max);
    }
}

/// <summary>What <see cref="OledIdle"/> asks for after a look at the last input.</summary>
public enum OledIdleEdge { None, Dim, Wake }

/// <summary>
/// The away-from-keyboard rule for OLED care: after <see cref="Threshold"/> without input the Desk and the widget dim,
/// and any input wakes them. The clock is injected; the caller reads when the last input happened (Windows'
/// GetLastInputInfo, polled every <see cref="PollEvery"/>) and this keeps the state. It touches no window.
/// </summary>
public sealed class OledIdle
{
    /// <summary>No input for this long dims the Desk.</summary>
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMinutes(5);

    /// <summary>How often the last input is looked at: cheap, and the dim lands within this of the threshold.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(15);

    /// <summary>The Desk's opacity while dimmed.</summary>
    public const double DeskDimmedOpacity = 0.40;

    /// <summary>The widget window's opacity while dimmed.</summary>
    public const double WidgetDimmedOpacity = 0.70;

    /// <summary>How long the dim eases in.</summary>
    public static readonly TimeSpan DimEase = TimeSpan.FromSeconds(2);

    /// <summary>How long the wake eases back.</summary>
    public static readonly TimeSpan WakeEase = TimeSpan.FromMilliseconds(300);

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _threshold;

    public OledIdle(Func<DateTimeOffset> clock, TimeSpan? threshold = null)
    {
        _clock = clock;
        _threshold = threshold ?? DefaultThreshold;
    }

    /// <summary>True while dimmed.</summary>
    public bool Dimmed { get; private set; }

    /// <summary>
    /// Looks at when the last input happened (null: Windows would not say, which changes nothing). Dim once, when the time
    /// since then reaches the threshold; Wake once, when it falls back under it while dimmed; otherwise None.
    /// </summary>
    public OledIdleEdge Observe(DateTimeOffset? lastInputAt)
    {
        if (lastInputAt is not { } at) return OledIdleEdge.None;
        bool away = _clock() - at >= _threshold;
        if (away == Dimmed) return OledIdleEdge.None;
        Dimmed = away;
        return away ? OledIdleEdge.Dim : OledIdleEdge.Wake;
    }

    /// <summary>OLED care was switched off: Wake when it had dimmed, else None. Dimmed is clear afterwards.</summary>
    public OledIdleEdge Reset()
    {
        if (!Dimmed) return OledIdleEdge.None;
        Dimmed = false;
        return OledIdleEdge.Wake;
    }

    /// <summary>The ease for an edge, snapped to nothing when Windows' Show animations is off.</summary>
    public static TimeSpan EaseFor(OledIdleEdge edge, bool animate) => !animate ? TimeSpan.Zero : edge == OledIdleEdge.Dim ? DimEase : WakeEase;
}
