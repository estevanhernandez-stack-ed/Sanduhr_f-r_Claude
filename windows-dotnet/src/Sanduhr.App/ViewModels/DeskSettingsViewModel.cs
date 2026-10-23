using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One ink choice in Settings, Desk, Look.</summary>
public sealed record DeskInkChoice(DeskInkMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A place a piece can sit: one of the eight anchors, or Hidden (null).</summary>
public sealed record DeskPlaceOption(DeskAnchor? Anchor, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One row of "Where each piece sits": a piece, its place and its size (60% to 160%).</summary>
public sealed partial class DeskPieceRow : ObservableObject
{
    private readonly DeskSettingsViewModel _owner;
    private bool _loading;

    public DeskPieceRow(DeskSettingsViewModel owner, string widget, string name)
    {
        _owner = owner;
        Widget = widget;
        Name = name;
    }

    public string Widget { get; }
    public string Name { get; }

    public override string ToString() => Name;

    [ObservableProperty] private DeskPlaceOption _place = DeskSettingsViewModel.Hidden;
    [ObservableProperty] private double _size = 1;

    /// <summary>What the Monitor dropdown offers for this piece: All monitors, Main monitor, each connected monitor, and (when the piece is
    /// on one that is gone) that monitor as "not connected".</summary>
    [ObservableProperty] private IReadOnlyList<DeskMonitorChoice> _monitorChoices = [];

    /// <summary>The monitor the piece is drawn on.</summary>
    [ObservableProperty] private DeskMonitorChoice _monitor = new(null, DeskMonitorNames.AllMonitors);

    /// <summary>The meter style (#336), for the pieces that have one (<see cref="HasStyle"/>).</summary>
    [ObservableProperty] private DeskStyleOption _style = DeskSettingsViewModel.StyleOptions[0];

    /// <summary>"120%", for the size picker.</summary>
    public string SizeText => $"{Math.Round(Size * 100)}%";

    public bool IsPlaced => Place.Anchor is not null;

    /// <summary>True for the Claude line, the Claude limits and telemetry: their row shows a Style picker.</summary>
    public bool HasStyle => DeskMeterStyles.Supports(Widget);

    internal void Load(DeskPlacement? p)
    {
        _loading = true;
        Place = p is null ? DeskSettingsViewModel.Hidden : DeskSettingsViewModel.PlaceFor(p.Anchor);
        Size = p?.Scale ?? 1;
        Style = DeskSettingsViewModel.StyleFor(p?.Style ?? DeskMeterStyle.Bar);
        MonitorChoices = DeskMonitorNames.Choices(_owner.Monitors, p?.Monitor);
        Monitor = DeskMonitorNames.Select(MonitorChoices, p?.Monitor);
        _loading = false;
    }

    partial void OnPlaceChanged(DeskPlaceOption value)
    {
        OnPropertyChanged(nameof(IsPlaced));
        if (!_loading) _owner.Edit(a => a.Place(Widget, value.Anchor));
    }

    partial void OnMonitorChanged(DeskMonitorChoice value)
    {
        if (!_loading && value is not null) _owner.Edit(a => a.SetMonitor(Widget, value.Value));
    }

    partial void OnSizeChanged(double value)
    {
        OnPropertyChanged(nameof(SizeText));
        if (!_loading) _owner.Edit(a => a.SetScale(Widget, value));
    }

    partial void OnStyleChanged(DeskStyleOption value)
    {
        if (!_loading && value is not null && HasStyle) _owner.Edit(a => a.SetStyle(Widget, value.Style));
    }
}

/// <summary>A meter style as the Style picker lists it (#336).</summary>
public sealed record DeskStyleOption(DeskMeterStyle Style, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Settings, Desk (spec 5.1): the switch (off by default), where each piece sits and its size, the
/// font, the clock size and the margins. Every change saves at once and tells the running Desk
/// through <see cref="Changed"/>, so the desktop follows as you choose.
/// </summary>
public sealed partial class DeskSettingsViewModel : ObservableObject
{
    public static readonly DeskPlaceOption Hidden = new(null, "Hidden");

    /// <summary>The eight places in the Mac's order, then Hidden.</summary>
    public static IReadOnlyList<DeskPlaceOption> PlaceOptions { get; } =
        [.. DeskAnchors.All.Select(a => new DeskPlaceOption(a, DeskAnchors.Name(a))), Hidden];

    public static DeskPlaceOption PlaceFor(DeskAnchor a) => PlaceOptions.First(o => o.Anchor == a);

    /// <summary>The size steps, 60% to 160%.</summary>
    public static IReadOnlyList<double> SizeSteps => DeskArrangement.ScaleSteps;

    /// <summary>The meter styles (#336): Bars, Slim, Rings, Ticks.</summary>
    public static IReadOnlyList<DeskStyleOption> StyleOptions { get; } =
        [.. DeskMeterStyles.All.Select(s => new DeskStyleOption(s, DeskMeterStyles.Name(s)))];

    public static DeskStyleOption StyleFor(DeskMeterStyle s) => StyleOptions.First(o => o.Style == s);

    /// <summary>The font picker's first entry: the system font (saved as "").</summary>
    public const string SystemFont = "System font (Segoe UI)";

    private readonly SettingsStore _store;
    private readonly Func<IReadOnlyList<DeskMonitorInfo>> _monitors;
    private DeskSettings _saved;
    private bool _loading;

    /// <param name="monitors">The connected monitors, named (the Monitor dropdown). Default: what Windows reports now.</param>
    public DeskSettingsViewModel(SettingsStore store, IReadOnlyList<string> fontFamilies, Func<IReadOnlyList<DeskMonitorInfo>>? monitors = null)
    {
        _store = store;
        _monitors = monitors ?? Sanduhr.App.Desk.DeskMonitorSource.Read;
        Monitors = _monitors();
        _saved = store.LoadDesk();
        FontChoices = [.. fontFamilies, SystemFont];
        Pieces =
        [
            new DeskPieceRow(this, "message", SettingsNames.DeskMessage),
            new DeskPieceRow(this, "clock", SettingsNames.DeskClock),
            new DeskPieceRow(this, "claude", SettingsNames.DeskClaudeLine),
            new DeskPieceRow(this, "meters", SettingsNames.DeskClaudeBars),
            new DeskPieceRow(this, "nowPlaying", SettingsNames.DeskNowPlaying),
            new DeskPieceRow(this, "telemetry", SettingsNames.DeskTelemetry),
            new DeskPieceRow(this, "weather", SettingsNames.DeskWeather),
            new DeskPieceRow(this, "watchers", SettingsNames.DeskWatchers),
            new DeskPieceRow(this, "launcher", SettingsNames.DeskLauncher),
            new DeskPieceRow(this, "glow", SettingsNames.DeskGlow),
            new DeskPieceRow(this, "companion", SettingsNames.DeskCompanion),
        ];
        Launcher = new LauncherSettingsViewModel(store, new Paths(), () => Pieces.FirstOrDefault(p => p.Widget == "launcher")?.IsPlaced == true, PlaceLauncherIfHidden)
        {
            Changed = () => Changed?.Invoke(),
        };
        Designs = new DesignsSettingsViewModel(() => Sanduhr.App.Desk.DeskDesignService.Current);
        Load();
        LoadInk();
    }

    /// <summary>Settings, Desk, Designs: the saved designs and Put my Desk back (spec, the agent-designed Desk).</summary>
    public DesignsSettingsViewModel Designs { get; }

    /// <summary>The monitors connected when the page was loaded (read again by <see cref="Reload"/>).</summary>
    public IReadOnlyList<DeskMonitorInfo> Monitors { get; private set; }

    /// <summary>Settings, Desk, Launcher: the buttons, their pictures and the pickers (spec, Desk Launcher).</summary>
    public LauncherSettingsViewModel Launcher { get; }

    /// <summary>Called after every saved change (App re-applies the Desk).</summary>
    public Action? Changed { get; set; }

    /// <summary>The Arrange Desk… button was pressed (App begins Arrange mode on the desktop).</summary>
    public Action? ArrangeRequested { get; set; }

    /// <summary>True while Arrange mode is up on the desktop (App sets it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanArrange), nameof(ArrangeNote), nameof(NotArranging))]
    private bool _arranging;

    /// <summary>The piece rows are editable only while no Arrange session runs (Done would overwrite them).</summary>
    public bool NotArranging => !Arranging;

    /// <summary>The working layout of the running Arrange session, null when none runs: the map draws it live.</summary>
    [ObservableProperty] private string? _arrangingLayout;

    /// <summary>The button is live while the Desk is on and no Arrange session runs.</summary>
    public bool CanArrange => Enabled && !Arranging;

    /// <summary>The line under the button: why it is off, what Arrange mode is doing, or what it does.</summary>
    public string ArrangeNote => Arranging ? DeskArrangeCopy.SettingsArranging
        : !Enabled ? DeskArrangeCopy.SettingsDeskOff : DeskArrangeCopy.SettingsNote;

    /// <summary>Starts Arrange mode on the desktop.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Arrange() => ArrangeRequested?.Invoke();

    /// <summary>Reads the saved settings again without saving (Arrange mode wrote the layout, or the
    /// glow's place was added): the rows and the map follow.</summary>
    public void Reload()
    {
        Monitors = _monitors();
        _saved = _store.LoadDesk();
        Load();
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Everything on the page is read again from the settings (a design was applied or switched to): the pieces, the font, the
    /// ink, the plate, the Launcher and the designs.</summary>
    public void RefreshFromStore()
    {
        Reload();
        LoadInk();
        Launcher.Reload();
        OnPropertyChanged(nameof(Plate));
        OnPropertyChanged(nameof(DirectDesign));
        Designs.Reload();
    }

    /// <summary>Slider drags (clock size, margins) settle for this long before they save, so a drag
    /// writes settings.json and rebuilds the Desk once, not on every tick (PR #209 review, 6).</summary>
    public static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(200);
    private DispatcherTimer? _settle;
    private DeskSettings? _pending;

    public ObservableCollection<DeskPieceRow> Pieces { get; }
    public IReadOnlyList<string> FontChoices { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _font = BundledFonts.Family;
    [ObservableProperty] private double _timeSize = DeskSettings.DefaultTimeSize;
    [ObservableProperty] private double _marginLeft;
    [ObservableProperty] private double _marginRight;
    [ObservableProperty] private double _marginTop;
    [ObservableProperty] private double _marginBottom;

    // ---- Ink (spec 6.7) ----

    /// <summary>The ink choices, in the order Settings lists them.</summary>
    public static IReadOnlyList<DeskInkChoice> InkChoices { get; } =
    [
        new(DeskInkMode.Auto, "Automatic (from the wallpaper)"),
        new(DeskInkMode.Light, "Light"),
        new(DeskInkMode.Dark, "Dark"),
        new(DeskInkMode.Theme, "The widget theme's color"),
        new(DeskInkMode.Custom, "Custom"),
    ];

    /// <summary>Custom's presets (the Mac's), then Custom hex.</summary>
    public static IReadOnlyList<string> InkPresetNames { get; } = [.. DeskInkPresets.All.Select(p => p.Name)];

    private DeskInkSettings _ink = DeskInkSettings.Default;
    private bool _loadingInk;

    [ObservableProperty] private DeskInkChoice _inkChoice = InkChoices[0];
    [ObservableProperty] private string _inkColors = "ffffff";
    [ObservableProperty] private string? _inkPreset;
    [ObservableProperty] private bool _inkShadow = true;

    public bool IsCustomInk => InkChoice.Mode == DeskInkMode.Custom;

    /// <summary>Raised after "Let agents show watchers" is saved (App writes watchers.json and, off, clears the cards).</summary>
    public Action? WatchersChanged { get; set; }

    /// <summary>"Let agents show watchers" (spec 6.9): off by default; saved at once.</summary>
    public bool WatchersAgents
    {
        get => _store.LoadWatchersAgents();
        set
        {
            if (value == WatchersAgents) return;
            _store.SaveWatchersAgents(value);
            OnPropertyChanged();
            WatchersChanged?.Invoke();
        }
    }

    /// <summary>"Let Claude change the Desk directly" (spec, the agent-designed Desk): off by default; saved at once. On, a design Claude
    /// proposes is applied without the review bar; the Desk it replaces is still saved first.</summary>
    public bool DirectDesign
    {
        get => _store.LoadDeskDirect();
        set
        {
            if (value == DirectDesign) return;
            _store.SaveDeskDirect(value);
            OnPropertyChanged();
        }
    }

    /// <summary>"OLED care" (spec 3.7.0): off by default; saved at once, and the Desk and the widget follow.</summary>
    public bool OledCare
    {
        get => _store.LoadOledCare();
        set
        {
            if (value == OledCare) return;
            _store.SaveOledCare(value);
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    /// <summary>"Clock and message take clicks" (spec 7.4): saved at once, and the Desk follows.</summary>
    public bool PiecesTakeClicks
    {
        get => _store.LoadDeskPiecesTakeClicks();
        set
        {
            if (value == PiecesTakeClicks) return;
            _store.SaveDeskPiecesTakeClicks(value);
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    /// <summary>"Plate behind the text" (Desk Look): saved at once, and the Desk follows.</summary>
    public bool Plate
    {
        get => _store.LoadDeskPlate();
        set
        {
            if (value == Plate) return;
            _store.SaveDeskPlate(value);
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    private void LoadInk()
    {
        _loadingInk = true;
        _ink = _store.LoadDeskInk();
        InkChoice = InkChoices.First(c => c.Mode == _ink.Mode);
        InkColors = _ink.Color;
        InkPreset = DeskInkPresets.All.FirstOrDefault(p => p.Value == _ink.Color).Name;
        InkShadow = _ink.Shadow;
        _loadingInk = false;
    }

    partial void OnInkChoiceChanged(DeskInkChoice value)
    {
        OnPropertyChanged(nameof(IsCustomInk));
        SaveInk(_ink with { Mode = value.Mode });
    }

    partial void OnInkPresetChanged(string? value)
    {
        if (_loadingInk || value is null) return;
        var preset = DeskInkPresets.All.FirstOrDefault(p => p.Name == value);
        if (preset.Value is null) return;
        _loadingInk = true;
        InkColors = preset.Value;
        _loadingInk = false;
        SaveInk(_ink with { Color = preset.Value });
    }

    partial void OnInkColorsChanged(string value)
    {
        if (_loadingInk) return;
        // One to four hex colors with commas; anything unreadable is kept as typed and drawn white.
        var spec = string.Join(",", (value ?? "").Split(',').Select(s => Sanduhr.Core.InkHex.Normalize(s)).Where(s => s is not null));
        if (spec.Length == 0) return;
        _loadingInk = true;
        InkPreset = DeskInkPresets.All.FirstOrDefault(p => p.Value == spec).Name;
        _loadingInk = false;
        SaveInk(_ink with { Color = spec });
    }

    partial void OnInkShadowChanged(bool value) => SaveInk(_ink with { Shadow = value });

    private void SaveInk(DeskInkSettings next)
    {
        if (_loadingInk || next == _ink) return;
        _ink = next;
        _store.SaveDeskInk(next);
        Changed?.Invoke();
    }

    /// <summary>The saved layout string (the map reads it).</summary>
    public string Layout => _saved.Layout;

    /// <summary>The saved settings (the map and previews read them).</summary>
    public DeskSettings Settings => _saved;

    private void Load()
    {
        _loading = true;
        Enabled = _saved.Enabled;
        Font = _saved.Font switch
        {
            null => BundledFonts.Family,
            "" => SystemFont,
            var f => f,
        };
        TimeSize = _saved.TimeSize;
        MarginLeft = _saved.Margins.Left;
        MarginRight = _saved.Margins.Right;
        MarginTop = _saved.Margins.Top;
        MarginBottom = _saved.Margins.Bottom;
        var a = new DeskArrangement(_saved.Layout);
        foreach (var row in Pieces) row.Load(a.Placement(row.Widget));
        _loading = false;
        Launcher?.PlacementChanged();
    }

    /// <summary>
    /// The Launcher's first button was added while the piece has no place: puts it at the first free corner
    /// (<see cref="DeskArrangement.FirstFreeAnchor"/>) and returns that corner's name in lower case ("bottom left"),
    /// or null when the piece already has a place.
    /// </summary>
    internal string? PlaceLauncherIfHidden()
    {
        if (Pieces.FirstOrDefault(p => p.Widget == "launcher")?.IsPlaced == true) return null;
        var anchor = new DeskArrangement(_saved.Layout).FirstFreeAnchor();
        Edit(a => a.Place("launcher", anchor));
        return DeskAnchors.Name(anchor).ToLowerInvariant();
    }

    /// <summary>Change the layout through <see cref="DeskArrangement"/> and save.</summary>
    internal void Edit(Action<DeskArrangement> change)
    {
        var a = new DeskArrangement(_saved.Layout);
        change(a);
        Save(_saved with { Layout = a.Layout });
        _loading = true;
        foreach (var row in Pieces) row.Load(a.Placement(row.Widget));
        _loading = false;
        Launcher.PlacementChanged();
    }

    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanArrange));
        OnPropertyChanged(nameof(ArrangeNote));
        if (!_loading) Save(_saved with { Enabled = value });
    }

    partial void OnFontChanged(string value)
    {
        if (_loading || value is null) return;
        // EsteFont Pro is the default: saved as absent, so a later default change follows it.
        string? saved = value == SystemFont ? "" : value == BundledFonts.Family ? null : value;
        Save(_saved with { Font = saved });
    }

    partial void OnTimeSizeChanged(double value) { if (!_loading) SaveSoon(Current with { TimeSize = value }); }
    partial void OnMarginLeftChanged(double value) => SaveMargins();
    partial void OnMarginRightChanged(double value) => SaveMargins();
    partial void OnMarginTopChanged(double value) => SaveMargins();
    partial void OnMarginBottomChanged(double value) => SaveMargins();

    private void SaveMargins()
    {
        if (_loading) return;
        SaveSoon(Current with { Margins = new DeskMarginSettings(MarginLeft, MarginRight, MarginTop, MarginBottom) });
    }

    /// <summary>The settings as they stand, a pending slider change included.</summary>
    private DeskSettings Current => _pending ?? _saved;

    private void SaveSoon(DeskSettings next)
    {
        _pending = next;
        if (_settle is null)
        {
            _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = SettleFor };
            _settle.Tick += (_, _) => Flush();
        }
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Saves a pending slider change now (the settle timer, or any other change first).</summary>
    public void Flush()
    {
        _settle?.Stop();
        if (_pending is { } p)
        {
            _pending = null;
            Save(p);
        }
    }

    private void Save(DeskSettings next)
    {
        if (_pending is { } pending && !ReferenceEquals(next, pending))
        {
            // Another change arrives while a slider settles: keep the slider's value too.
            next = next with { TimeSize = pending.TimeSize, Margins = pending.Margins };
            _pending = null;
            _settle?.Stop();
        }
        _saved = next.Normalized();
        _store.SaveDesk(_saved);
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(Settings));
        Changed?.Invoke();
    }
}
