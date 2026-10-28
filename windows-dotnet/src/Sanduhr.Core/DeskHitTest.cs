namespace Sanduhr.Core;

/// <summary>The Desk pieces that take clicks.</summary>
public enum DeskElementKind { NowPlaying, WeatherCredit, Clock, Message, ClaudeLine, LauncherButton }

/// <summary>
/// One interactive piece of the Desk as the clicks see it: its kind, a key (never a title or a
/// label) and its frame in the island's coordinates, top-left origin.
/// </summary>
/// <param name="Kind">What the piece is.</param>
/// <param name="Key">A key for the kinds that need one, else null.</param>
/// <param name="Frame">Where it draws; empty when it is not drawn or its frame never arrived.</param>
/// <param name="Clickable">Takes a click.</param>
public sealed record DeskElement(DeskElementKind Kind, string? Key, DeskRect Frame, bool Clickable = true);

/// <summary>
/// Which Desk element is under a point. One pure answer for the click-through switch, left clicks
/// and right clicks, so they can never disagree about what the pointer is over. Ported from the Mac
/// DeskHitTest, cut to what Windows draws.
/// </summary>
public static class DeskHitTest
{
    /// <summary>First match wins, in this order. The Claude line comes last: the account name sits inside it.</summary>
    public static readonly IReadOnlyList<DeskElementKind> Priority =
        [DeskElementKind.LauncherButton, DeskElementKind.NowPlaying, DeskElementKind.WeatherCredit, DeskElementKind.Clock, DeskElementKind.Message, DeskElementKind.ClaudeLine];

    /// <summary>How far past its frame each kind still takes the click (horizontal, vertical): a little slack, so the gaps between letters and the line above or below still count.</summary>
    public static (double W, double H) Slack(DeskElementKind kind) => kind switch
    {
        DeskElementKind.NowPlaying or DeskElementKind.Clock or DeskElementKind.Message => (8, 4),
        DeskElementKind.ClaudeLine => (8, 2),
        // A Launcher button takes exactly its frame: the gaps between buttons pass clicks to the desktop.
        DeskElementKind.LauncherButton => (0, 0),
        _ => (4, 2),
    };

    /// <summary>The element's click area: its frame and the slack. Empty for an empty frame, so it never takes a click.</summary>
    public static DeskRect Area(DeskElement element)
    {
        var f = element.Frame;
        if (!(f.Width > 0) || !(f.Height > 0)) return new DeskRect(0, 0, 0, 0);
        var (w, h) = Slack(element.Kind);
        return new DeskRect(f.X - w, f.Y - h, f.Width + w * 2, f.Height + h * 2);
    }

    /// <summary>The clickable element under the point, by <see cref="Priority"/>; null where the Desk lets the mouse through.</summary>
    public static DeskElement? Element(double x, double y, IReadOnlyList<DeskElement> elements)
    {
        foreach (var kind in Priority)
            foreach (var e in elements)
            {
                if (e.Kind != kind || !e.Clickable) continue;
                var a = Area(e);
                if (a.Width > 0 && x >= a.Left && x < a.Right && y >= a.Top && y < a.Bottom) return e;
            }
        return null;
    }

    /// <summary>True when a right click on the element opens a Desk menu: the clock, the message, the Claude line and a Launcher button.</summary>
    public static bool HasMenu(DeskElement? element) =>
        element?.Kind is DeskElementKind.Clock or DeskElementKind.Message or DeskElementKind.ClaudeLine or DeskElementKind.LauncherButton;
}

/// <summary>What the Desk draws that can take clicks, with the frames the view reported.</summary>
/// <param name="Placed">The widget words the layout places.</param>
/// <param name="NowPlayingDrawn">Now playing is drawn (placed and a track shows).</param>
/// <param name="NowPlayingFrame">Its frame.</param>
/// <param name="WeatherCreditDrawn">The weather credit is drawn.</param>
/// <param name="WeatherCreditFrame">Its frame.</param>
/// <param name="PiecesTakeClicks">"Clock and message take clicks" is on.</param>
/// <param name="ClockFrame">The clock's frame.</param>
/// <param name="MessageDrawn">The message draws something.</param>
/// <param name="MessageFrame">Its frame.</param>
/// <param name="ClaudeLineDrawn">The Claude line draws something.</param>
/// <param name="ClaudeLineFrame">Its frame.</param>
/// <param name="LauncherFrames">The frames of the Launcher's drawn buttons, in button order (null or empty: none drawn).</param>
public sealed record DeskElementsInput(
    IReadOnlySet<string> Placed,
    bool NowPlayingDrawn = false,
    DeskRect NowPlayingFrame = default,
    bool WeatherCreditDrawn = false,
    DeskRect WeatherCreditFrame = default,
    bool PiecesTakeClicks = false,
    DeskRect ClockFrame = default,
    bool MessageDrawn = false,
    DeskRect MessageFrame = default,
    bool ClaudeLineDrawn = false,
    DeskRect ClaudeLineFrame = default,
    IReadOnlyList<DeskRect>? LauncherFrames = null);

/// <summary>
/// What the Desk draws, worked out from the same settings it reads, so a piece that is drawn but
/// never reported its frame shows up as an empty frame instead of not at all.
/// </summary>
public static class DeskElements
{
    /// <summary>
    /// Every element the Desk draws. The Launcher's buttons, now playing and the weather credit always, when drawn
    /// (a button is keyed by its index in the Launcher, as text). The clock,
    /// the message and the Claude line only while <see cref="DeskElementsInput.PiecesTakeClicks"/> and
    /// placed (the message and the line also drawn).
    /// </summary>
    public static IReadOnlyList<DeskElement> Build(DeskElementsInput input)
    {
        var list = new List<DeskElement>();
        if (input.LauncherFrames is { } buttons)
            for (int i = 0; i < buttons.Count; i++)
                list.Add(new DeskElement(DeskElementKind.LauncherButton, i.ToString(System.Globalization.CultureInfo.InvariantCulture), buttons[i]));
        if (input.NowPlayingDrawn)
            list.Add(new DeskElement(DeskElementKind.NowPlaying, null, input.NowPlayingFrame));
        if (input.WeatherCreditDrawn)
            list.Add(new DeskElement(DeskElementKind.WeatherCredit, null, input.WeatherCreditFrame));
        if (input.PiecesTakeClicks)
        {
            if (input.Placed.Contains("clock"))
                list.Add(new DeskElement(DeskElementKind.Clock, null, input.ClockFrame));
            if (input.Placed.Contains("message") && input.MessageDrawn)
                list.Add(new DeskElement(DeskElementKind.Message, null, input.MessageFrame));
            if (input.Placed.Contains("claude") && input.ClaudeLineDrawn)
                list.Add(new DeskElement(DeskElementKind.ClaudeLine, null, input.ClaudeLineFrame));
        }
        return list;
    }
}

/// <summary>
/// "Clock and message take clicks" (Settings, Desk). On, the clock, the message and the Claude line
/// become click areas, so a right click there opens Sanduhr's menu. The price is the desktop icons
/// right beneath them. Off, all three let every click through.
/// </summary>
public static class DeskPieceClicks
{
    /// <summary>The settings key.</summary>
    public const string Key = "desk_pieces_take_clicks";

    /// <summary>The switch is on when the key is absent.</summary>
    public const bool DefaultOn = true;

    public const string Title = "Clock and message take clicks";
    public const string Caption = "On: right-click them for Sanduhr's menu. Desktop icons right beneath them can't be clicked there while it's on.";

    /// <summary>The item a piece's menu has above the shared items, and the Settings page it opens ("message" or "clock"); null for the rest.</summary>
    public static (string Title, DeskElementKind Page)? MenuItem(DeskElementKind kind) => kind switch
    {
        DeskElementKind.Message => (SettingsNames.LinkTitle(SettingsPage.DeskMessage), DeskElementKind.Message),
        DeskElementKind.Clock => (SettingsNames.LinkTitle(SettingsPage.Desk), DeskElementKind.Clock),
        _ => null,
    };
}
