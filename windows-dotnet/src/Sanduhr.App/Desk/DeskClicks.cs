using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What a Desk menu item asks the app to do (spec 7.4).</summary>
internal enum DeskMenuAction { EditMessages, DeskSettings, LauncherSettings, Arrange, TurnOffDesk }

/// <summary>What a click on the Desk can do; the controller answers.</summary>
internal interface IDeskClickActions
{
    /// <summary>Now playing was clicked: play or pause the playing app.</summary>
    void PlayPause();

    /// <summary>The weather credit was clicked: open Open-Meteo's site (its licence asks for the link).</summary>
    void OpenWeatherCredit();

    /// <summary>A Launcher button was clicked, tapped or invoked: open its app or link (the controller answers, and
    /// sends a click on a missing app to Settings).</summary>
    void LaunchButton(int index);

    /// <summary>A Desk menu item was chosen.</summary>
    void Menu(DeskMenuAction action);
}

/// <summary>
/// The Desk's clicks (spec 7.4, the Mac's <c>DeskHitTest</c>). After a redraw hands out its islands, this
/// finds the pieces in each island that take clicks (now playing and the weather credit always; the clock,
/// the message and the Claude line while "Clock and message take clicks" is on), gives each a faint hit
/// plate (alpha 1 of 255, so its frame takes the mouse while it looks the same), marks the island
/// <see cref="DeskIsland.TakesClicks"/> and answers its clicks through <see cref="DeskHitTest"/>. An island
/// with none stays click-through as before, and fully transparent pixels always pass clicks to the desktop.
/// The Launcher's buttons always take clicks: each is its own hit region (key = its index), the mouse glows them on
/// hover and launches on release, and touch glows on touch-down, launches on touch-up and cancels on a drag out
/// (<see cref="LauncherPointer"/> holds those rules). Nothing here activates a window.
/// </summary>
internal static class DeskClicks
{
    /// <summary>The weather credit line's tag (DeskView sets it).</summary>
    public const string WeatherCreditTag = "weatherCredit";

    /// <summary>Open-Meteo's site, the link its CC BY 4.0 licence asks for beside its data.</summary>
    public const string WeatherCreditUrl = "https://open-meteo.com/";

    /// <summary>The message's menu item: "Message settings…", a page name like every Settings entry point.</summary>
    public static readonly string MessageSettings = SettingsNames.LinkTitle(SettingsPage.DeskMessage);
    public static readonly string DeskSettings = SettingsNames.LinkTitle(SettingsPage.Desk);
    /// <summary>A Launcher button's menu item: "Launcher settings…" (WF17, WF19), the page's own name like every entry point.</summary>
    public static readonly string LauncherSettings = SettingsNames.LinkTitle(SettingsPage.Launcher);
    public const string TurnOffDesk = "Turn off the Desk";

    private static readonly Brush Plate = Frozen(Color.FromArgb(1, 0, 0, 0));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>The islands with their clicks wired: an island holding a click element comes back with
    /// <see cref="DeskIsland.TakesClicks"/> set, the rest unchanged.</summary>
    public static IReadOnlyList<DeskIsland> Prepare(IReadOnlyList<DeskIsland> islands, bool piecesTakeClicks, IDeskClickActions actions)
    {
        var result = new List<DeskIsland>(islands.Count);
        foreach (var island in islands)
        {
            bool drawer = HasDrawer(island.Content);
            var found = Find(island.Content);
            var kinds = Kinds(found, island.Widgets, piecesTakeClicks);
            if (kinds.Count == 0)
            {
                result.Add(drawer ? island with { TakesClicks = true, TakesFocus = true } : island);
                continue;
            }
            var targets = found.Where(f => kinds.Contains(f.Kind)).ToList();
            foreach (var (_, element) in targets) SetPlate(element);
            Wire(island.Content, targets, actions);
            result.Add(island with { TakesClicks = true, TakesFocus = drawer });
        }
        return result;
    }

    /// <summary>The island holds a drawer, which takes its own clicks (Task 8) and may take focus.</summary>
    private static bool HasDrawer(DependencyObject node)
    {
        if (node is DrawerPiece) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasDrawer(VisualTreeHelper.GetChild(node, i))) return true;
        return node is ContentControl { Content: DependencyObject c } && HasDrawer(c)
            || node is Decorator { Child: { } d } && HasDrawer(d)
            || node is Panel p && p.Children.OfType<DependencyObject>().Any(HasDrawer);
    }

    /// <summary>The click kinds this island has, by the Core rule (<see cref="DeskElements.Build"/>).</summary>
    private static HashSet<DeskElementKind> Kinds(List<(DeskElementKind Kind, FrameworkElement Element)> found,
        IReadOnlyList<string> widgets, bool piecesTakeClicks)
    {
        bool Has(DeskElementKind k) => found.Any(f => f.Kind == k);
        var launcherButtons = found.Where(f => f.Kind == DeskElementKind.LauncherButton).Select(_ => default(DeskRect)).ToList();
        var input = new DeskElementsInput(
            new HashSet<string>(widgets, StringComparer.Ordinal),
            LauncherFrames: launcherButtons.Count > 0 ? launcherButtons : null,
            NowPlayingDrawn: Has(DeskElementKind.NowPlaying),
            WeatherCreditDrawn: Has(DeskElementKind.WeatherCredit),
            PiecesTakeClicks: piecesTakeClicks,
            MessageDrawn: Has(DeskElementKind.Message),   // streamer: notname - DeskElementKind.Message is an enum member
            ClaudeLineDrawn: Has(DeskElementKind.ClaudeLine));
        var kinds = new HashSet<DeskElementKind>(DeskElements.Build(input).Select(e => e.Kind));
        // The clock counts only when it is drawn in this island.
        if (!Has(DeskElementKind.Clock)) kinds.Remove(DeskElementKind.Clock);
        return kinds;
    }

    /// <summary>The pieces in an island that could take clicks, by the tags DeskView gives them.</summary>
    private static List<(DeskElementKind Kind, FrameworkElement Element)> Find(FrameworkElement content)
    {
        var found = new List<(DeskElementKind, FrameworkElement)>();
        void Walk(object node)
        {
            if (node is LauncherButtonVisual button) found.Add((DeskElementKind.LauncherButton, button));
            if (node is FrameworkElement fe && fe.Tag is string tag)
            {
                DeskElementKind? kind = tag switch
                {
                    "clock" => DeskElementKind.Clock,
                    "message" => DeskElementKind.Message,   // streamer: notname - DeskElementKind.Message is an enum member
                    "claude" => DeskElementKind.ClaudeLine,
                    "nowPlaying" => DeskElementKind.NowPlaying,
                    WeatherCreditTag => DeskElementKind.WeatherCredit,
                    _ => null,
                };
                if (kind is { } k) found.Add((k, fe));
            }
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    Walk(child);
        }
        Walk(content);
        return found;
    }

    /// <summary>The faint plate behind a piece: its frame takes the mouse, not only its strokes.</summary>
    private static void SetPlate(FrameworkElement e)
    {
        switch (e)
        {
            // The now-playing piece's host holds the real piece: the plate goes on that.
            case DeskView.ReflowHost { Child: FrameworkElement inner }: SetPlate(inner); break;
            case Panel p when p.Background is null: p.Background = Plate; break;
            case TextBlock t when t.Background is null: t.Background = Plate; break;
            case Border b when b.Background is null: b.Background = Plate; break;
            case Control c when c.Background is null: c.Background = Plate; break;
        }
    }

    private static void Wire(FrameworkElement content, List<(DeskElementKind Kind, FrameworkElement Element)> targets, IDeskClickActions actions)
    {
        DeskElement? HitAt(Point p)
        {
            var elements = new List<DeskElement>(targets.Count);
            foreach (var (kind, fe) in targets)
            {
                var r = fe.IsVisible && fe.RenderSize.Width > 0
                    ? fe.TransformToAncestor(content).TransformBounds(new Rect(fe.RenderSize))
                    : Rect.Empty;
                string? key = fe is LauncherButtonVisual b ? b.Index.ToString(CultureInfo.InvariantCulture) : null;
                elements.Add(new DeskElement(kind, key, r.IsEmpty ? default : new DeskRect(r.X, r.Y, r.Width, r.Height)));
            }
            return DeskHitTest.Element(p.X, p.Y, elements);
        }
        DeskElement? Hit(MouseEventArgs e) => HitAt(e.GetPosition(content));

        var launcher = targets.Where(t => t.Kind == DeskElementKind.LauncherButton).Select(t => (LauncherButtonVisual)t.Element).ToList();
        var pointer = new LauncherPointer();
        static int? ButtonAt(DeskElement? hit) =>
            hit is { Kind: DeskElementKind.LauncherButton, Key: { } key }
            && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
        void Show()
        {
            foreach (var b in launcher)
            {
                b.SetGlow(pointer.Glow == b.Index);
                b.SetDip(pointer.Dip == b.Index);
            }
        }
        // A touch the system also promotes to mouse events must not act twice: the touch handlers take it.
        static bool FromTouch(MouseEventArgs e) => e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;

        foreach (var b in launcher)
        {
            int index = b.Index;
            b.Invoked += () => actions.LaunchButton(index);
        }

        if (launcher.Count > 0)
        {
            content.MouseMove += (_, e) =>
            {
                if (FromTouch(e)) return;
                pointer.MouseMove(ButtonAt(Hit(e)));
                Show();
            };
            content.MouseLeave += (_, _) =>
            {
                pointer.MouseLeave();
                Show();
            };
            content.MouseLeftButtonDown += (_, e) =>
            {
                if (FromTouch(e)) return;
                var index = ButtonAt(Hit(e));
                pointer.MouseDown(index);
                Show();
                if (index is not null) e.Handled = true;
            };
            content.TouchDown += (_, e) =>
            {
                pointer.TouchDown(ButtonAt(HitAt(e.GetTouchPoint(content).Position)));
                Show();
                if (!pointer.Touching) return;
                content.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            };
            content.TouchMove += (_, e) =>
            {
                if (!pointer.Touching) return;
                pointer.TouchMove(ButtonAt(HitAt(e.GetTouchPoint(content).Position)));
                Show();
                e.Handled = true;
            };
            content.TouchUp += (_, e) =>
            {
                if (!pointer.Touching) return;
                var launch = pointer.TouchUp(ButtonAt(HitAt(e.GetTouchPoint(content).Position)));
                content.ReleaseTouchCapture(e.TouchDevice);
                Show();
                e.Handled = true;
                if (launch is { } index) actions.LaunchButton(index);
            };
            content.LostTouchCapture += (_, _) =>
            {
                pointer.Cancel();
                Show();
            };
        }
        content.MouseLeftButtonUp += (_, e) =>
        {
            if (FromTouch(e)) return;
            var hit = Hit(e);
            int? launch = null;
            if (launcher.Count > 0)
            {
                launch = pointer.MouseUp(ButtonAt(hit));
                Show();
            }
            switch (hit?.Kind)
            {
                case DeskElementKind.LauncherButton:
                    if (launch is { } index) actions.LaunchButton(index);
                    e.Handled = true;
                    break;
                case DeskElementKind.NowPlaying: actions.PlayPause(); e.Handled = true; break;
                case DeskElementKind.WeatherCredit: actions.OpenWeatherCredit(); e.Handled = true; break;
                case null: break;
                default: e.Handled = true; break; // the clock, message and Claude line: a plain click does nothing
            }
        };
        content.MouseRightButtonUp += (_, e) =>
        {
            var hit = Hit(e);
            if (hit is null || hit.Kind == DeskElementKind.WeatherCredit) return;
            e.Handled = true;
            var target = targets.First(t => t.Kind == hit.Kind).Element;
            OpenMenu(target, hit.Kind, actions);
        };
    }

    /// <summary>The Desk menu: the piece's own item (Message settings… on the message), then the shared ones.</summary>
    private static void OpenMenu(FrameworkElement target, DeskElementKind kind, IDeskClickActions actions)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        AutomationProperties.SetName(menu, "Desk");
        void Add(string header, DeskMenuAction action)
        {
            var item = new MenuItem { Header = header };
            AutomationProperties.SetName(item, header.TrimEnd('…'));   // streamer: notname - context-menu item text, fixed copy
            item.Click += (_, _) => actions.Menu(action);
            menu.Items.Add(item);
        }
        if (kind == DeskElementKind.Message) Add(MessageSettings, DeskMenuAction.EditMessages);   // streamer: notname - DeskElementKind.Message is an enum member
        if (kind == DeskElementKind.LauncherButton) Add(LauncherSettings, DeskMenuAction.LauncherSettings);
        Add(DeskSettings, DeskMenuAction.DeskSettings);
        Add(DeskArrangeCopy.MenuItem, DeskMenuAction.Arrange);
        menu.Items.Add(new Separator());
        Add(TurnOffDesk, DeskMenuAction.TurnOffDesk);
        menu.IsOpen = true;
    }
}
