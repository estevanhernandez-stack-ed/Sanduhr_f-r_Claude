using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What the message piece draws on one redraw: today's lines, the message settings, and the
/// lines whose once-only motion ({write}, a {sweep} without a period) has already run, kept by the
/// controller across redraws so a line writes itself in once when it appears, as on the Mac.</summary>
internal sealed record DeskMessageFrame(MessageToday Today, DeskMessageSettings Settings)
{
    public ISet<string> Played { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>The glow's color (theme match: the followed theme's desk.glow, else its accent); null glows in the line's middle stop.</summary>
    public string? GlowHex { get; init; }
}

/// <summary>
/// The Desk message (spec 6.2, W11): <c>messages.txt</c> read with the Mac's grammar and drawn with its
/// effects. Owns the file's place (<c>%APPDATA%\Sanduhr\messages.txt</c>), creates the Mac's starter the
/// first time the piece or the editor needs it (never after Reset), keeps the text by write time and
/// watches the file so an edit shows at once. Message text is never logged.
/// </summary>
internal sealed class DeskMessageSource : IDisposable
{
    private readonly SettingsStore _store;
    private readonly string _path;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private DeskMessageSettings _settings = DeskMessageSettings.Default;
    private (DateTime Written, string? Text)? _cache;

    public DeskMessageSource(SettingsStore store, Paths paths)
    {
        _store = store;
        _path = Path.Combine(paths.AppDataDir, "messages.txt");
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _cache = null; Changed?.Invoke(); };
    }

    public string FilePath => _path;

    /// <summary>The file or the message settings changed: today's lines may differ.</summary>
    public event Action? Changed;

    public void Reload() => _settings = _store.LoadDeskMessage();

    /// <summary>Creates the starter file when there is none (the piece or the editor needs it).</summary>
    public void EnsureFile() => MessageEditorState.EnsureFile(_path, () => !ResetGuard.Done);

    /// <summary>Today's message for the piece, or null when the piece is not placed.</summary>
    public DeskMessageFrame? Frame(DateTimeOffset now, DeskSettings desk)
    {
        if (new DeskArrangement(desk.Layout).Placement("message") is null)
        {
            StopWatching();
            return null;
        }
        EnsureFile();
        Watch();
        var today = DeskMessages.Today(Read(), now, _settings.Hourly, _settings.Mix, _settings.PinnedLine);
        return new DeskMessageFrame(today, _settings);
    }

    private string? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var written = File.GetLastWriteTimeUtc(_path);
            if (_cache is { } c && c.Written == written) return c.Text;
            var text = File.ReadAllText(_path, System.Text.Encoding.UTF8);
            _cache = (written, text);
            return text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Watch()
    {
        if (_watcher is not null) return;
        try
        {
            var dir = Path.GetDirectoryName(_path)!;   // streamer: notname - folder of the message file being watched, never drawn
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))   // streamer: notname - watcher pattern, never drawn
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,   // streamer: notname - NotifyFilters.FileName is a watcher flag, not a name
            };
            FileSystemEventHandler on = (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            _watcher.Changed += on;
            _watcher.Created += on;
            _watcher.Deleted += on;
            _watcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        StopWatching();
        _debounce.Stop();
    }
}

/// <summary>
/// Draws the message piece (spec 6.2): each line's ink (its <c>{ink:}</c> gradient, else the message
/// color, which follows the Desk ink when unset), its glow, size and letter style, and its motion:
/// <c>{write}</c> once, <c>{sweep}</c> once or every n seconds, <c>{shimmer}</c> every 8 s. Special days
/// stack, take turns or scroll by the clock. Motion runs at 30 frames a second on this island only and
/// stops between runs; with Windows' animations off nothing moves. The piece keeps one size whichever
/// line shows, so turning lines never lays the screen out again.
/// </summary>
internal static class DeskMessagePiece
{
    private const int FrameRate = 30;

    private static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    public static FrameworkElement? Create(DeskMessageFrame frame, double scale, ResolvedInk ink, DeskFonts fonts,
        string? deskFont, HorizontalAlignment align, double width, List<IDisposable> live, DeskPause gate)
    {
        var lines = frame.Today.Lines;
        if (lines.Count == 0) return null;
        var s = frame.Settings;
        var (regular, bold) = fonts.Families(s.Font.Length > 0 ? s.Font : deskFont);
        double baseSize = s.Size * scale;
        var ta = align switch
        {
            HorizontalAlignment.Right => TextAlignment.Right,
            HorizontalAlignment.Center => TextAlignment.Center,
            _ => TextAlignment.Left,
        };
        var built = lines.Select(l => Line(l, s, ink, regular, bold, baseSize, ta, align, width, live, frame.Played, gate, frame.GlowHex)).ToList();

        if (!MessageSpecialModes.Cycles(s.Special, frame.Today))
        {
            var stack = new StackPanel { HorizontalAlignment = align };
            foreach (var b in built)
            {
                b.Margin = new Thickness(0, stack.Children.Count > 0 ? baseSize * 0.1 : 0, 0, 0);
                stack.Children.Add(b);
            }
            return stack;
        }

        // Take turns or Scroll: every line in one cell (the cell is the largest line's size), one showing.
        var cell = new Grid { HorizontalAlignment = align, ClipToBounds = true };
        foreach (var b in built)
        {
            b.HorizontalAlignment = align;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Opacity = 0;
            b.RenderTransform = new TranslateTransform();
            cell.Children.Add(b);
        }
        var change = MessageSpecialModes.Change(s.Special, ReduceMotion);
        int shown = MessageSpecialModes.Index(MessageSpecialModes.ToUnixSeconds(DateTimeOffset.Now), built.Count, s.Seconds);
        built[shown].Opacity = 1;
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        void Schedule()
        {
            double unix = MessageSpecialModes.ToUnixSeconds(DateTimeOffset.Now);
            timer.Interval = TimeSpan.FromSeconds(Math.Max(0.2, MessageSpecialModes.NextChange(unix, s.Seconds) - unix));
        }
        void Next()
        {
            double unix = MessageSpecialModes.ToUnixSeconds(DateTimeOffset.Now);
            int i = MessageSpecialModes.Index(unix + 0.05, built.Count, s.Seconds);
            if (i != shown) Swap(built[shown], built[i], change, cell);
            shown = i;
            timer.Interval = TimeSpan.FromSeconds(Math.Max(0.2, MessageSpecialModes.NextChange(unix, s.Seconds) - unix));
        }
        // While the island is covered the turns stop. On resume the line the clock says shows at once,
        // without motion, and the timer is scheduled for the next change (S7, 7.1).
        void StopMotion()
        {
            foreach (var b in built)
            {
                b.BeginAnimation(UIElement.OpacityProperty, null);
                if (b.RenderTransform is TranslateTransform t) t.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }
        void OnGate()
        {
            timer.Stop();
            StopMotion();
            if (gate.Paused) return;
            shown = MessageSpecialModes.Index(MessageSpecialModes.ToUnixSeconds(DateTimeOffset.Now), built.Count, s.Seconds);
            for (int i = 0; i < built.Count; i++)
            {
                built[i].Opacity = i == shown ? 1 : 0;
                if (built[i].RenderTransform is TranslateTransform t) t.Y = 0;
            }
            Schedule();
            timer.Start();
        }
        timer.Tick += (_, _) => Next();
        Schedule();
        if (!gate.Paused) timer.Start();
        gate.Changed += OnGate;
        live.Add(new Stopper(() => { timer.Stop(); gate.Changed -= OnGate; }));
        return cell;
    }

    private static void Swap(FrameworkElement from, FrameworkElement to, SpecialChange change, FrameworkElement cell)
    {
        switch (change)
        {
            case SpecialChange.Crossfade:
                Animate(from, UIElement.OpacityProperty, 1, 0, MessageSpecialModes.Fade);
                Animate(to, UIElement.OpacityProperty, 0, 1, MessageSpecialModes.Fade);
                break;
            case SpecialChange.Scroll:
                double h = Math.Max(1, cell.ActualHeight);
                from.Opacity = 1;
                to.Opacity = 1;
                Animate((TranslateTransform)from.RenderTransform, TranslateTransform.YProperty, 0, -h, MessageSpecialModes.Glide, () => from.Opacity = 0);
                Animate((TranslateTransform)to.RenderTransform, TranslateTransform.YProperty, h, 0, MessageSpecialModes.Glide);
                break;
            default:
                from.Opacity = 0;
                to.Opacity = 1;
                break;
        }
    }

    private static void Animate(IAnimatable target, DependencyProperty dp, double from, double to, double seconds, Action? done = null)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(a, FrameRate);
        a.Completed += (_, _) =>
        {
            if (target is DependencyObject d) d.SetValue(dp, to);
            done?.Invoke();
        };
        target.BeginAnimation(dp, a);
    }

    /// <summary>One line with its effects. A line that is nothing but tags draws as written.</summary>
    private static FrameworkElement Line(string raw, DeskMessageSettings s, ResolvedInk ink, FontFamily regular, FontFamily bold,
        double baseSize, TextAlignment ta, HorizontalAlignment align, double width, List<IDisposable> live, ISet<string> played, DeskPause gate,
        string? glowHex = null)
    {
        var parsed = MessageMarkup.Parse(raw);
        var e = parsed.Effects;
        // "Use the theme's colors" ignores a line's own ink: the resolver already put the theme's stops in s.Color.
        var stops = e.Ink is { Count: > 0 } own && !s.ThemeColors
            ? InkHex.Stops(string.Join(",", own))
            : InkHex.Stops(DeskInk.MessageColor(s.Color, ink.DarkInk));
        var text = parsed.Text;
        var weight = FontWeights.Normal;
        var style = FontStyles.Normal;
        switch (e.Font)
        {
            case LetterStyle.Bold: weight = FontWeights.Bold; break;
            case LetterStyle.Italic: style = FontStyles.Italic; break;
            case LetterStyle.BoldItalic: weight = FontWeights.Bold; style = FontStyles.Italic; break;
            case { } other: text = LetterMap.Map(text, other); break;
        }
        var block = new TextBlock
        {
            Text = text,
            FontFamily = weight == FontWeights.Bold ? bold : regular,
            FontWeight = weight,
            FontStyle = style,
            FontSize = Math.Max(1, MessageMotion.Size(e, baseSize)),
            Foreground = DeskView.InkBrush(stops),
            TextAlignment = ta,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(100, width),
            HorizontalAlignment = align,
        };
        if (MessageMotion.Glows(e, s.Glow))
            block.Effect = new DropShadowEffect
            {
                Color = DeskView.Rgb(glowHex ?? stops[stops.Count / 2]),
                ShadowDepth = 0,
                BlurRadius = DeskMessageLook.GlowBlur,
                Opacity = DeskMessageLook.GlowOpacity,
            };
        else if (ink.ShadowOpacity > 0)
            block.Effect = DeskView.Shadow(ink);

        var host = new Grid { HorizontalAlignment = align };
        host.Children.Add(block);
        bool reduce = ReduceMotion;
        if (MessageMotion.AnimatesWrite(e, reduce) && played.Add("write\n" + raw)) Write(block);
        // The Core rules decide whether and when each light runs, and are asked again on resume with the
        // replay delay. A once-only sweep counts as played only when it is actually scheduled, so one that
        // was waiting out a pause still runs when the island comes back into sight.
        if (e.Sweep)
        {
            (double First, double? Every)? SweepFor(bool replay)
            {
                bool sweptOnce = e.SweepPeriod is null && played.Contains("sweep\n" + raw);
                var plan = MessageMotion.SweepPlan(e, reduce, gate.Paused, sweptOnce, replay);
                if (plan is { Every: null }) played.Add("sweep\n" + raw);
                return plan;
            }
            Light(host, block, text, ink, live, gate, SweepFor, MessageMotion.SweepRun, band: null);
        }
        if (e.Shimmer)
        {
            (double First, double? Every)? ShimmerFor(bool replay) =>
                MessageMotion.Shimmers(e, reduce, gate.Paused)
                    ? (replay ? MessageMotion.SweepReplayDelay : MessageMotion.ShimmerPeriod - MessageMotion.ShimmerSweep,
                        MessageMotion.ShimmerPeriod)
                    : null;
            Light(host, block, text, ink, live, gate, ShimmerFor, MessageMotion.ShimmerSweep, band: MessageMotion.ShimmerBand);
        }
        return host;
    }

    /// <summary>{write}: the line reveals left to right over 1.5 s, once.</summary>
    private static void Write(TextBlock block)
    {
        var edge = new GradientStop(Colors.Black, 0);
        var after = new GradientStop(Colors.Transparent, 0);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        mask.GradientStops.Add(edge);
        mask.GradientStops.Add(after);
        block.OpacityMask = mask;
        var a = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(MessageMotion.WriteDuration));
        Timeline.SetDesiredFrameRate(a, FrameRate);
        var b = a.Clone();
        a.Completed += (_, _) => block.OpacityMask = null;
        edge.BeginAnimation(GradientStop.OffsetProperty, a);
        after.BeginAnimation(GradientStop.OffsetProperty, b);
    }

    /// <summary>
    /// {sweep} and {shimmer}: a band of light (white, toward <see cref="MessageMotion.SweepStrength"/>)
    /// crossing the line, drawn as a copy of the text in white masked to the band. {sweep}'s band is
    /// three characters wide; {shimmer}'s is 30% of the line. Runs once after <paramref name="first"/>
    /// seconds and then every period when the plan gives one; between runs nothing animates.
    /// <paramref name="plan"/> says when the light runs (null: not now), asked at the start and again with
    /// <c>replay</c> set each time the island comes back into sight. While <paramref name="gate"/> is
    /// paused the timer and any running band stop.
    /// </summary>
    private static void Light(Grid host, TextBlock block, string text, ResolvedInk ink, List<IDisposable> live,
        DeskPause gate, Func<bool, (double First, double? Every)?> plan, double run, double? band)
    {
        var glint = new TextBlock
        {
            Text = block.Text, FontFamily = block.FontFamily, FontWeight = block.FontWeight, FontStyle = block.FontStyle,
            FontSize = block.FontSize, TextAlignment = block.TextAlignment, TextWrapping = block.TextWrapping,
            MaxWidth = block.MaxWidth, HorizontalAlignment = block.HorizontalAlignment,
            Foreground = Brushes.White, Opacity = MessageMotion.SweepStrength, IsHitTestVisible = false,
        };
        int length = Math.Max(1, new System.Globalization.StringInfo(text).LengthInTextElements);
        double half = band is { } b ? b / 2 : MessageMotion.SweepWidth / length;
        var lead = new GradientStop(Colors.Transparent, -2);
        var mid = new GradientStop(Colors.Black, -2);
        var tail = new GradientStop(Colors.Transparent, -2);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        mask.GradientStops.Add(lead);
        mask.GradientStops.Add(mid);
        mask.GradientStops.Add(tail);
        glint.OpacityMask = mask;
        host.Children.Add(glint);

        var timer = new DispatcherTimer(DispatcherPriority.Background);
        double? every = null;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var d = TimeSpan.FromSeconds(run);
            void Go(GradientStop stop, double offset)
            {
                var a = new DoubleAnimation(-half + offset, 1 + half + offset, d) { FillBehavior = FillBehavior.Stop };
                Timeline.SetDesiredFrameRate(a, FrameRate);
                stop.BeginAnimation(GradientStop.OffsetProperty, a);
            }
            Go(lead, -half);
            Go(mid, 0);
            Go(tail, half);
            if (every is { } period)
            {
                timer.Interval = TimeSpan.FromSeconds(Math.Max(run, period));
                timer.Start();
            }
        };
        void Rest()
        {
            timer.Stop();
            lead.BeginAnimation(GradientStop.OffsetProperty, null);
            mid.BeginAnimation(GradientStop.OffsetProperty, null);
            tail.BeginAnimation(GradientStop.OffsetProperty, null);
        }
        void Begin(bool replay)
        {
            if (plan(replay) is not { } p) return;
            every = p.Every;
            timer.Interval = TimeSpan.FromSeconds(p.First);
            timer.Start();
        }
        void OnGate()
        {
            Rest();
            Begin(replay: true);
        }
        Begin(replay: false);
        gate.Changed += OnGate;
        live.Add(new Stopper(() =>
        {
            gate.Changed -= OnGate;
            Rest();
        }));
    }

    private sealed class Stopper(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
