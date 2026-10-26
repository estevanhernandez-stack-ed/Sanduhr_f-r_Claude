using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Desk companion's live half (spec 3.8 section 4, FORMAT 6): the installed sprite pack the user chose, its current
/// stage's sheet, and the clocks that animate it. It outlives the Desk's redraws (a redraw builds new pieces; the pose,
/// the roaming distance and the companion's memory carry on) and runs its frame timer only while a piece that shows it
/// is in sight. Growth is read from Sanduhr Time's day files, or the days with Claude Code activity, on a background
/// thread once an hour; nothing here reads tokens for growth, and nothing leaves the PC.
/// </summary>
internal sealed class CompanionService : IDisposable
{
    public static readonly TimeSpan GrowthEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(5);
    /// <summary>While walking the position moves at least this often, whatever the pack's frame rate.</summary>
    private static readonly TimeSpan WalkStep = TimeSpan.FromMilliseconds(80);

    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly Func<IReadOnlyList<CodingDay>> _claudeCodeDays;
    private readonly Func<DateTimeOffset?> _lastInput;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<bool> _reduceMotion;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _growthTimer;
    private readonly HashSet<object> _wanted = [];
    private readonly Dictionary<int, ImageSource?> _frames = [];
    private readonly CompanionBrain _brain = new();
    private readonly CompanionRoamer _roamer = new();
    private CompanionAnimator? _animator;
    private CompanionGrowthState _growth = CompanionGrowthState.Empty;
    private BitmapSource? _sheet;
    /// <summary>A pack under review (spec 3.8 section 5): drawn from its staged copy in place of the chosen one, until <see cref="EndPreviewPack"/>.</summary>
    private (WidgetStore Store, string Id)? _previewPack;
    /// <summary>The widgets folder the pack drawn now comes from (the installed one, or a staged preview).</summary>
    private string? _packRoot;
    private DateTimeOffset _lastStep;
    private DateTimeOffset _lastIdlePoll = DateTimeOffset.MinValue;
    private bool _growthRunning;
    private bool _disposed;

    /// <param name="claudeCodeDays">The fallback growth source: days with Claude Code activity. The app's reads the local
    /// session logs (only which days had activity); tests pass their own.</param>
    /// <param name="reduceMotion">Windows' "Show animations" off; the app reads <see cref="SystemParameters.ClientAreaAnimation"/>.</param>
    public CompanionService(Paths paths, SettingsStore store, Func<IReadOnlyList<CodingDay>>? claudeCodeDays = null,
        Func<DateTimeOffset?>? lastInput = null, Func<DateTimeOffset>? clock = null, Func<bool>? reduceMotion = null)
    {
        _paths = paths;
        _store = store;
        _claudeCodeDays = claudeCodeDays ?? ClaudeCodeDays;
        _lastInput = lastInput ?? DeskSensorsNative.LastInputAt;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _reduceMotion = reduceMotion ?? (() => !SystemParameters.ClientAreaAnimation);
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += (_, _) => Step(_clock());
        _growthTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = GrowthEvery };
        _growthTimer.Tick += (_, _) => RefreshGrowth();
        _growth = CompanionGrowthState.Load(paths.CompanionStateFile);
    }

    /// <summary>The installed pack drawn, null when there is none to draw.</summary>
    public string? PackId { get; private set; }

    public CompanionSprite? Sprite { get; private set; }

    public CompanionSettings Settings { get; private set; } = CompanionSettings.Default;

    /// <summary>True when a sprite and its current stage's sheet are loaded.</summary>
    public bool Ready => Sprite is not null && _sheet is not null;

    /// <summary>Growth 0 to 1, never backwards.</summary>
    public double Growth => _growth.Growth;

    /// <summary>What growth remembers: the coding days seen and the active time.</summary>
    public CompanionGrowthState GrowthState => _growth;

    /// <summary>The stage drawn now.</summary>
    public CompanionStage? Stage { get; private set; }

    /// <summary>The frame now.</summary>
    public CompanionPose Pose { get; private set; }

    /// <summary>The mood now (the brain's, at the last step).</summary>
    public CompanionMood Mood { get; private set; }

    /// <summary>True while it walks this step (roaming on, the walking part of its cycle, awake, no trigger playing).</summary>
    public bool Walking { get; private set; }

    /// <summary>Roaming is on and Windows' animations are on (reduce motion: no roaming).</summary>
    public bool Roaming => Settings.Roam && !_reduceMotion() && Sprite is not null;

    /// <summary>How far along a route it has walked, in DIPs at the pack's speed.</summary>
    public double Distance => Sprite is null ? 0 : _roamer.Distance(Sprite.SpeedPxPerSecond);

    /// <summary>The frame timer is running (the live check reads it).</summary>
    internal bool Running => _timer.IsEnabled;

    /// <summary>A new frame to draw (the pose or the position moved).</summary>
    public event Action? Frame;

    /// <summary>The stage or the pack changed: the pieces' size changes, so the Desk lays out again.</summary>
    public event Action? Changed;

    /// <summary>The frame's size in DIPs at piece size 1: the stage's cell times the pack's scale.</summary>
    public Size CellSize => Sprite is null || Stage is null
        ? Size.Empty
        : new Size(Stage.FrameWidth * Sprite.Scale, Stage.FrameHeight * Sprite.Scale);

    /// <summary>Reads the choice and the pack again (Settings, Mods & Widgets, an install or uninstall). Nothing animates until a piece wants it.</summary>
    public void Reload()
    {
        if (_disposed) return;
        Settings = _store.LoadCompanion();
        var store = StoreNow();
        string? pack = null;
        if (_previewPack is { } preview) pack = preview.Id;
        else
        {
            try { pack = Settings.ActivePack(store.Load(), id => store.ReadInfo(id)?.Kind); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        CompanionSprite? sprite = null;
        if (pack is not null)
        {
            try
            {
                var file = Path.Combine(store.PackDir(pack), "sprite.json");
                if (File.Exists(file) && new FileInfo(file).Length <= PackValidator.MaxImageBytes)
                    sprite = CompanionSprite.Parse(File.ReadAllBytes(file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        bool packChanged = pack != PackId || (sprite is null) != (Sprite is null) || store.Root != _packRoot;
        _packRoot = store.Root;
        PackId = sprite is null ? null : pack;
        Sprite = sprite;
        if (packChanged)
        {
            _animator = sprite is null ? null : new CompanionAnimator(sprite);
            Stage = null;
        }
        LoadStage(force: packChanged);
        if (Sprite is not null && !_growthTimer.IsEnabled)
        {
            _growthTimer.Start();
            RefreshGrowth();
        }
        else if (Sprite is null) _growthTimer.Stop();
    }

    /// <summary>The Desk is off or the piece hidden: the hourly growth read stops until <see cref="Reload"/> (growth is read
    /// again then, so no coding day is missed; the days are in the source files, not in memory).</summary>
    public void Rest() => _growthTimer.Stop();

    /// <summary>Loads the sheet of the stage growth has reached, when it differs from the one loaded. True when it changed.</summary>
    private bool LoadStage(bool force)
    {
        if (Sprite is null || PackId is null)
        {
            bool had = _sheet is not null || Stage is not null;
            _sheet = null;
            Stage = null;
            _frames.Clear();
            return had;
        }
        var stage = Sprite.StageAt(Growth);
        if (!force && Stage == stage && _sheet is not null) return false;
        Stage = stage;
        _frames.Clear();
        _sheet = LoadSheet(CompanionSheet.DrawnFile(StoreNow(), PackId, stage));
        return true;
    }

    /// <summary>The store the companion draws from: a pack under review's staged copy, else the installed widgets.</summary>
    private WidgetStore StoreNow() =>
        _previewPack?.Store ?? new WidgetStore(_paths.WidgetsDir, WidgetCatalogService.EncodePng);

    /// <summary>True while a pack under review is drawn in place of the chosen companion.</summary>
    public bool PreviewingPack => _previewPack is not null;

    /// <summary>
    /// Draws <paramref name="id"/> from <paramref name="store"/> (a Claude-made sprite staged for review, spec 3.8 section 5) as the
    /// companion until <see cref="EndPreviewPack"/>: the same loader, pointed at the staged pack. Nothing is chosen or saved; the
    /// Desk's next <see cref="Reload"/> picks it up.
    /// </summary>
    public void PreviewPack(WidgetStore store, string id) => _previewPack = (store, id);

    /// <summary>The review is over: the chosen companion is drawn again on the next <see cref="Reload"/>.</summary>
    public void EndPreviewPack() => _previewPack = null;

    /// <summary>A sheet decoded from memory (so Uninstall can delete the file), frozen; null when it is missing, too large or not an image.</summary>
    internal static BitmapSource? LoadSheet(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length > PackValidator.MaxImageBytes) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            if (image.PixelWidth > PackValidator.MaxImageSide || image.PixelHeight > PackValidator.MaxImageSide) return null;
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Frame <paramref name="index"/> of the current stage's sheet, cut at the stage's cell size; null when the sheet has no such cell.</summary>
    public ImageSource? FrameImage(int index)
    {
        if (_sheet is null || Stage is null) return null;
        if (_frames.TryGetValue(index, out var cached)) return cached;
        ImageSource? frame = null;
        if (CompanionSheet.Cell(index, _sheet.PixelWidth, _sheet.PixelHeight, Stage.FrameWidth, Stage.FrameHeight) is { } cell)
        {
            var cut = new CroppedBitmap(_sheet, new Int32Rect(cell.X, cell.Y, cell.Width, cell.Height));
            cut.Freeze();
            frame = cut;
        }
        _frames[index] = frame;
        return frame;
    }

    /// <summary>The image of the pose now.</summary>
    public ImageSource? CurrentImage => FrameImage(Pose.Frame);

    /// <summary>Where along a route of a given length a companion placed at an anchor starts, the first time a route is laid out.</summary>
    public void StartAt(double distance)
    {
        if (_roamer.WalkedSeconds == 0 && _roamer.Start == 0) _roamer.Start = Math.Max(0, distance);
    }

    /// <summary><paramref name="who"/> shows the companion in sight (or no longer does): the frame timer runs while anyone does.</summary>
    public void Want(object who, bool want)
    {
        if (_disposed) return;
        bool changed = want ? _wanted.Add(who) : _wanted.Remove(who);
        if (!changed) return;
        if (_wanted.Count > 0 && Ready)
        {
            if (!_timer.IsEnabled)
            {
                _lastStep = _clock();
                Step(_lastStep);
                _timer.Start();
            }
        }
        else _timer.Stop();
    }

    /// <summary>A new reading from the widget: the mood follows, and a crossed threshold plays <c>near_limit</c>.</summary>
    public void ObserveUsage(DeskUsage usage)
    {
        var alerts = _store.LoadAlertConfig();
        var now = _clock();
        foreach (var t in _brain.ObserveUsage(usage, alerts.WarnPct, alerts.UrgentPct, now)) _animator?.Play(t, now);
        Mood = _brain.Mood(now);
    }

    /// <summary>The watchers changed: one waiting on you plays <c>needs_you</c>, one that passed plays <c>job_passed</c>.</summary>
    public void ObserveWatchers(IReadOnlyList<Watcher> watchers)
    {
        var now = _clock();
        foreach (var t in _brain.ObserveWatchers(watchers)) _animator?.Play(t, now);
    }

    /// <summary>One step of the clocks: idle, mood, walking, the pose. The timer calls it; tests call it with their own time.</summary>
    internal void Step(DateTimeOffset now)
    {
        if (_animator is null || Sprite is null) return;
        bool reduce = _reduceMotion();
        if (now - _lastIdlePoll >= IdlePoll)
        {
            _lastIdlePoll = now;
            if (_lastInput() is { } input && _brain.ObserveActivity(input, now) is { } idle) _animator.Play(idle, now);
        }
        Mood = _brain.Mood(now);
        var step = now - _lastStep;
        _lastStep = now;
        bool roam = Roaming;
        bool allowed = roam && _animator.CanWalk(Mood, now, reduce);
        _roamer.Advance(step, allowed);
        Walking = allowed && _roamer.Walking;
        Pose = _animator.Pose(Mood, Walking, now, reduce);
        var frame = TimeSpan.FromSeconds(1.0 / CompanionAnimator.FpsFor(Sprite, reduce));
        _timer.Interval = Walking && frame > WalkStep ? WalkStep : frame;
        Frame?.Invoke();
    }

    /// <summary>Reads growth on a background thread and, when it moved, saves it and loads the stage it reached.</summary>
    public void RefreshGrowth()
    {
        if (_disposed || _growthRunning) return;
        _growthRunning = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var was = _growth;
        string daysDir = _paths.TimeDaysDir, file = _paths.CompanionStateFile;
        var fallback = _claudeCodeDays;
        Task.Run(() =>
        {
            IReadOnlyList<CodingDay> days = SanduhrTimeDays.Present(daysDir) ? SanduhrTimeDays.Read(daysDir) : fallback();
            var next = was.Observe(days);
            if (next.Growth != was.Growth || !next.Days.SetEquals(was.Days) || next.Active != was.Active) next.Save(file);
            return next;
        }).ContinueWith(t =>
        {
            dispatcher.BeginInvoke(() =>
            {
                _growthRunning = false;
                if (_disposed || !t.IsCompletedSuccessfully) return;
                ApplyGrowth(t.Result);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Takes a growth reading (never lower than the one held) and loads the stage it reached.</summary>
    internal void ApplyGrowth(CompanionGrowthState state)
    {
        if (state.Growth < _growth.Growth) state = state with { Growth = _growth.Growth };
        _growth = state;
        if (LoadStage(force: false)) Changed?.Invoke();
    }

    /// <summary>Days with Claude Code activity in the local session logs (which days only; the token counts are not used).</summary>
    private static IReadOnlyList<CodingDay> ClaudeCodeDays()
    {
        try
        {
            var reader = new CcLogReader(null, () => ClaudeCodeHomes.Discover(ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir));
            return [.. reader.TokensByDay(daysBack: 60).Keys.Select(d => new CodingDay(d, TimeSpan.Zero))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _growthTimer.Stop();
        _wanted.Clear();
    }
}

/// <summary>A roaming island's moving anchor: the host keeps the island's window on <see cref="Anchor"/> as it moves.</summary>
internal sealed class DeskIslandMotion
{
    public Point Anchor { get; private set; }

    public event Action<Point>? Moved;

    public DeskIslandMotion(Point start) => Anchor = start;

    public void MoveTo(Point p)
    {
        if (Math.Abs(p.X - Anchor.X) < 0.25 && Math.Abs(p.Y - Anchor.Y) < 0.25) return;
        Anchor = p;
        Moved?.Invoke(p);
    }
}

/// <summary>
/// The companion piece (spec 3.8 section 4): the current frame, scaled nearest-neighbour to the cell times the pack's
/// scale times the piece's size, anchored bottom-centre in a frame the stage's size. It takes no clicks. Roaming, it
/// is its own island and moves along <see cref="CompanionPath"/>, mirrored while it walks left.
/// </summary>
internal sealed class CompanionPieceVisual : Grid
{
    private readonly CompanionService _service;
    private readonly Image _image;
    private readonly ScaleTransform _flip = new(1, 1);
    private readonly CompanionPath? _path;
    private readonly DeskIslandMotion? _motion;

    private CompanionPieceVisual(CompanionService service, double pieceScale, CompanionPath? path, DeskIslandMotion? motion)
    {
        _service = service;
        _path = path;
        _motion = motion;
        var cell = service.CellSize;
        Width = Math.Max(1, cell.Width * pieceScale);
        Height = Math.Max(1, cell.Height * pieceScale);
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        _image = new Image
        {
            Width = Width,
            Height = Height,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _flip,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Children.Add(_image);
        System.Windows.Automation.AutomationProperties.SetName(this, SettingsNames.DeskCompanion);
        Draw();
    }

    /// <summary>The image drawn now (tests read it).</summary>
    internal ImageSource? Shown => _image.Source;

    /// <summary>True while drawn mirrored (walking left).</summary>
    internal bool Mirrored => _flip.ScaleX < 0;

    /// <summary>The companion at its anchor, in its stack. Its frames run while <paramref name="gate"/> is open.</summary>
    public static CompanionPieceVisual Create(CompanionService service, double pieceScale, DeskPause gate, List<IDisposable> live) =>
        Hook(new CompanionPieceVisual(service, pieceScale, null, null), gate, live);

    /// <summary>The roaming companion: its own island, moved along <paramref name="path"/> through <paramref name="motion"/>.</summary>
    public static CompanionPieceVisual Roaming(CompanionService service, double pieceScale, CompanionPath path, DeskIslandMotion motion,
        DeskPause gate, List<IDisposable> live) =>
        Hook(new CompanionPieceVisual(service, pieceScale, path, motion), gate, live);

    private static CompanionPieceVisual Hook(CompanionPieceVisual piece, DeskPause gate, List<IDisposable> live)
    {
        var service = piece._service;
        void OnFrame() => piece.Draw();
        void OnGate() => service.Want(piece, !gate.Paused);
        service.Frame += OnFrame;
        gate.Changed += OnGate;
        service.Want(piece, !gate.Paused);
        live.Add(new Unhook(() =>
        {
            service.Frame -= OnFrame;
            gate.Changed -= OnGate;
            service.Want(piece, false);
        }));
        return piece;
    }

    private void Draw()
    {
        var image = _service.CurrentImage;
        if (!ReferenceEquals(_image.Source, image)) _image.Source = image;
        bool left = false;
        if (_path is not null && _motion is not null)
        {
            var spot = _path.At(_service.Distance);
            _motion.MoveTo(new Point(spot.X, spot.Y));
            left = spot.FacingLeft;
        }
        double sx = left ? -1 : 1;
        if (_flip.ScaleX != sx) _flip.ScaleX = sx;
    }

    private sealed class Unhook(Action undo) : IDisposable
    {
        private Action? _undo = undo;
        public void Dispose() { _undo?.Invoke(); _undo = null; }
    }
}
