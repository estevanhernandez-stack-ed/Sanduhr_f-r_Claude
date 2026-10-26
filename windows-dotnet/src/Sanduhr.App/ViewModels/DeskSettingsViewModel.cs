using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>One row of Desk, Pieces: the piece's Show box, its place, size, monitor and style, its order among the pieces that
/// share its place, and the link to its own page.</summary>
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

    /// <summary>Order (WF5): live when another piece shares the place and this one isn't at that end of its stack.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveUpCommand))] private bool _canMoveUp;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveDownCommand))] private bool _canMoveDown;

    /// <summary>"120%", for the size picker.</summary>
    public string SizeText => $"{Math.Round(Size * 100)}%";

    public bool IsPlaced => Place.Anchor is not null;

    /// <summary>True for the Claude line, the Claude limits and telemetry: their row shows a Style picker.</summary>
    public bool HasStyle => DeskMeterStyles.Supports(Widget);

    /// <summary>The companion's row carries its sprite and roaming inline (spec, Pieces).</summary>
    public bool IsCompanion => Widget == CompanionSettings.Widget;
    public DeskCompanionViewModel? Companion => IsCompanion ? _owner.Companion : null;

    /// <summary>Show (spec, Pieces): on means Place is not Hidden. On places the piece where it last was in this window, else at its
    /// default place (<see cref="DeskArrangement.DefaultPlaceFor"/>); off hides it and remembers where it was.</summary>
    public bool Shown
    {
        get => IsPlaced;
        set
        {
            if (_loading || value == IsPlaced) return;
            if (value) _owner.PlaceIfHidden(Widget);
            else Place = DeskSettingsViewModel.Hidden;
        }
    }

    /// <summary>The Show box spoken; its visible label is <see cref="Name"/>.</summary>
    public string ShownName => SettingsNames.PieceShownSpoken(Name);
    public string MoveUpName => SettingsNames.MoveUpIn(Name, Place.Name);
    public string MoveDownName => SettingsNames.MoveDownIn(Name, Place.Name);

    /// <summary>The page with this piece's own settings; null for a piece that has none.</summary>
    public SettingsPage? PiecePage => SettingsNames.DeskPiecePage(Widget);

    /// <summary>True when that page is in this build's sidebar: the row then shows "&lt;Page&gt; settings…".</summary>
    public bool HasSettingsPage => PiecePage is { } page && SettingsNames.Pages.Contains(page);

    public string SettingsTitle => PiecePage is { } page ? SettingsNames.LinkTitle(page) : "";

    /// <summary>The link the button opens: the page, and for the glow the section where its place is chosen.</summary>
    public string SettingsLink => PiecePage switch
    {
        null => "",
        SettingsPage.ClaudeCode => SettingsIndex.PageId(SettingsPage.ClaudeCode) + "#" + SettingsIndex.GlowWhere,
        { } page => SettingsIndex.PageId(page),
    };

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.Edit(a => a.Move(Widget, -1));

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _owner.Edit(a => a.Move(Widget, 1));

    internal void Load(DeskPlacement? p, int stackIndex, int stackCount)
    {
        _loading = true;
        Place = p is null ? DeskSettingsViewModel.Hidden : DeskSettingsViewModel.PlaceFor(p.Anchor);
        Size = p?.Scale ?? 1;
        Style = DeskSettingsViewModel.StyleFor(p?.Style ?? DeskMeterStyle.Bar);
        MonitorChoices = DeskMonitorNames.Choices(_owner.Monitors, p?.Monitor);
        Monitor = DeskMonitorNames.Select(MonitorChoices, p?.Monitor);
        if (p is not null) _owner.Remember(Widget, p.Anchor);   // whatever hides it later, Show returns to the last visible place
        CanMoveUp = stackIndex > 0;
        CanMoveDown = stackIndex >= 0 && stackIndex < stackCount - 1;
        _loading = false;
    }

    partial void OnPlaceChanged(DeskPlaceOption value)
    {
        OnPropertyChanged(nameof(IsPlaced));
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(MoveUpName));
        OnPropertyChanged(nameof(MoveDownName));
        if (!_loading) _owner.Edit(a => a.Place(Widget, value.Anchor));
    }

    /// <summary>Hidden by hand (Show or Place): the place is remembered for this window's life.</summary>
    partial void OnPlaceChanged(DeskPlaceOption? oldValue, DeskPlaceOption newValue)
    {
        if (!_loading && newValue.Anchor is null && oldValue?.Anchor is { } was) _owner.Remember(Widget, was);
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
    /// <param name="widgets">The installed packs (the companion's sprites). Default: this store's own widgets folder.</param>
    /// <param name="inkPresets">The Ink presets (name and hex spec): the built-ins and the installed palette packs'. Default: the built-ins.</param>
    public DeskSettingsViewModel(SettingsStore store, IReadOnlyList<string> fontFamilies, Func<IReadOnlyList<DeskMonitorInfo>>? monitors = null,
        Func<IReadOnlyList<(string Name, string Value)>>? inkPresets = null, Func<WidgetStore>? widgets = null)
    {
        _store = store;
        _inkPresets = inkPresets ?? (() => DeskInkPresets.All);
        _monitors = monitors ?? Sanduhr.App.Desk.DeskMonitorSource.Read;
        Monitors = _monitors();
        _saved = store.LoadDesk();
        FontChoices = new([.. fontFamilies, SystemFont]);
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
        Companion = new DeskCompanionViewModel(store,
            widgets ?? (() => new WidgetStore(store.Paths.WidgetsDir, Sanduhr.App.Services.WidgetCatalogService.EncodePng)))
        {
            Changed = () => Changed?.Invoke(),
        };
        Launcher = new LauncherSettingsViewModel(store, new Paths(), () => Pieces.FirstOrDefault(p => p.Widget == "launcher")?.IsPlaced == true, PlaceLauncherIfHidden)
        {
            Changed = () => Changed?.Invoke(),
        };
        Designs = new DesignsSettingsViewModel(() => Sanduhr.App.Desk.DeskDesignService.Current);
        Load();
        LoadInk();
        RefreshInkPresets();
    }

    /// <summary>Settings, Desk, Designs: the saved designs and Put my Desk back (spec, the agent-designed Desk).</summary>
    public DesignsSettingsViewModel Designs { get; }

    /// <summary>Desk, Pieces, the Companion row's sprite and roaming (moved from Settings, Widgets).</summary>
    public DeskCompanionViewModel Companion { get; }

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
        RefreshInkPresets();
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>The font packs changed (one switched, installed or removed): the Desk font list follows. The saved family stays listed, so the
    /// box never goes blank and nothing rewrites the setting, while the Desk draws its fallback for a family this PC no longer has.</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        var wanted = new List<string>(families);
        if (Font != SystemFont && !wanted.Contains(Font)) wanted.Add(Font);
        wanted.Add(SystemFont);
        FontListSync.Sync(FontChoices, wanted);
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
    public ObservableCollection<string> FontChoices { get; }

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

    /// <summary>Custom's presets: the Mac's, then the palettes of installed palette packs (3.9.1). Rebuilt by <see cref="Reload"/>, so an
    /// install or an uninstall shows without reopening Settings.</summary>
    [ObservableProperty] private IReadOnlyList<string> _inkPresetNames = [.. DeskInkPresets.All.Select(p => p.Name)];

    private readonly Func<IReadOnlyList<(string Name, string Value)>> _inkPresets;
    private IReadOnlyList<(string Name, string Value)> _inkList = DeskInkPresets.All;

    private DeskInkSettings _ink = DeskInkSettings.Default;
    private bool _loadingInk;

    [ObservableProperty] private DeskInkChoice _inkChoice = InkChoices[0];
    [ObservableProperty] private string _inkColors = "ffffff";
    [ObservableProperty] private string? _inkPreset;
    [ObservableProperty] private bool _inkShadow = true;

    public bool IsCustomInk => InkChoice.Mode == DeskInkMode.Custom;

    /// <summary>The Colors presets show whenever the ink is not Automatic (WF3: a palette pack is no longer hidden behind Custom).</summary>
    public bool ShowsInkColors => InkChoice.Mode != DeskInkMode.Auto;

    /// <summary>Raised after "Let Claude show watchers" is saved (App writes watchers.json and, off, clears the cards).</summary>
    public Action? WatchersChanged { get; set; }

    /// <summary>Settings, Watchers: "Let Claude show watchers" (spec 6.9). Off by default; saved at once. Turning it on shows a hidden Watchers piece.</summary>
    public bool WatchersAgents
    {
        get => _store.LoadWatchersAgents();
        set
        {
            if (value == WatchersAgents) return;
            _store.SaveWatchersAgents(value);
            OnPropertyChanged();
            WatchersChanged?.Invoke();
            if (value) PlaceIfHidden("watchers");   // Principle 4: turning watchers on never leaves them invisible
        }
    }

    /// <summary>Raised after "Show my CI runners' jobs" is saved (App starts or stops the runner watch).</summary>
    public Action? CiRunnersChanged { get; set; }

    /// <summary>Settings, Watchers: "Show my CI runners' jobs" (spec ci-watchers-3.9). Off by default; saved at once. Turning it on shows a hidden Watchers piece.</summary>
    public bool WatchersCiRunners
    {
        get => _store.LoadWatchersCiRunners();
        set
        {
            if (value == WatchersCiRunners) return;
            _store.SaveWatchersCiRunners(value);
            OnPropertyChanged();
            CiRunnersChanged?.Invoke();
            if (value) PlaceIfHidden("watchers");
        }
    }

    /// <summary>The line under the switch: which runners, or why none (a live region).</summary>
    [ObservableProperty] private string _ciRunnersStatus = "";

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
        InkPreset = _inkList.FirstOrDefault(p => p.Value == _ink.Color).Name;
        InkShadow = _ink.Shadow;
        _loadingInk = false;
    }

    /// <summary>Reads the preset list again (a palette pack was installed or removed) and keeps the ink's own preset selected: the
    /// ink stores colors, not a reference to a pack, so a removed pack's entry just stops being selected.</summary>
    private void RefreshInkPresets()
    {
        _inkList = _inkPresets();
        bool was = _loadingInk;
        _loadingInk = true;
        InkPresetNames = [.. _inkList.Select(p => p.Name)];
        InkPreset = _inkList.FirstOrDefault(p => p.Value == _ink.Color).Name;
        _loadingInk = was;
    }

    partial void OnInkChoiceChanged(DeskInkChoice value)
    {
        OnPropertyChanged(nameof(IsCustomInk));
        OnPropertyChanged(nameof(ShowsInkColors));
        SaveInk(_ink with { Mode = value.Mode });
    }

    partial void OnInkPresetChanged(string? value)
    {
        if (_loadingInk || value is null) return;
        var preset = _inkList.FirstOrDefault(p => p.Name == value);
        if (preset.Value is null) return;
        // Choosing a preset sets Custom (spec, Desk Look): the colors are the ink's own from now on.
        _loadingInk = true;
        InkColors = preset.Value;
        if (InkChoice.Mode != DeskInkMode.Custom) InkChoice = InkChoices.First(c => c.Mode == DeskInkMode.Custom);
        _loadingInk = false;
        OnPropertyChanged(nameof(IsCustomInk));
        OnPropertyChanged(nameof(ShowsInkColors));
        SaveInk(_ink with { Color = preset.Value, Mode = DeskInkMode.Custom });
    }

    partial void OnInkColorsChanged(string value)
    {
        if (_loadingInk) return;
        // One to four hex colors with commas; anything unreadable is kept as typed and drawn white.
        var spec = string.Join(",", (value ?? "").Split(',').Select(s => Sanduhr.Core.InkHex.Normalize(s)).Where(s => s is not null));
        if (spec.Length == 0) return;
        // A typed color is the ink's own, so it switches the ink to Custom, as choosing a preset does.
        _loadingInk = true;
        InkPreset = _inkList.FirstOrDefault(p => p.Value == spec).Name;
        if (InkChoice.Mode != DeskInkMode.Custom) InkChoice = InkChoices.First(c => c.Mode == DeskInkMode.Custom);
        _loadingInk = false;
        OnPropertyChanged(nameof(IsCustomInk));
        OnPropertyChanged(nameof(ShowsInkColors));
        SaveInk(_ink with { Color = spec, Mode = DeskInkMode.Custom });
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
        LoadRows(new DeskArrangement(_saved.Layout));
        Companion?.Reload();
        _loading = false;
        Launcher?.PlacementChanged();
    }

    /// <summary>The Launcher's first button was added while the piece has no place: puts it at its default place (or where it last
    /// was in this window) and returns that place's name in lower case ("top left"), or null when it already has a place.</summary>
    internal string? PlaceLauncherIfHidden() =>
        PlaceIfHidden(LauncherModel.Widget) is { } at ? DeskAnchors.Name(at).ToLowerInvariant() : null;

    private readonly Dictionary<string, DeskAnchor> _lastPlace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeskPieceStatusViewModel> _statuses = new(StringComparer.Ordinal);

    /// <summary>A piece hidden by hand: where it was, for the life of this Settings window (spec, Pieces: Show).</summary>
    internal void Remember(string widget, DeskAnchor at) => _lastPlace[widget] = at;

    /// <summary>
    /// Shows a hidden piece (Principle 4): where it last was in this window, else at <see cref="DeskArrangement.DefaultPlaceFor"/>;
    /// saves, and the running Desk follows. Returns where, or null when nothing changed: the piece is already placed, the word is
    /// unknown, or an Arrange session runs (its Done would overwrite the layout). Nothing is written then.
    /// </summary>
    public DeskAnchor? PlaceIfHidden(string widget)
    {
        if (Arranging || new DeskArrangement(_saved.Layout).Placement(widget) is not null
            || !DeskArrangement.Known.Contains(widget, StringComparer.Ordinal))
            return null;
        DeskAnchor? remembered = _lastPlace.TryGetValue(widget, out var at) ? at : null;
        DeskAnchor? placed = null;
        Edit(a => placed = a.PlaceAtDefault(widget, remembered));
        return placed;
    }

    /// <summary>The line a feature page shows about <paramref name="widget"/>'s piece; one per piece for this window.</summary>
    public DeskPieceStatusViewModel PieceStatus(string widget) =>
        _statuses.TryGetValue(widget, out var status) ? status : _statuses[widget] = new DeskPieceStatusViewModel(this, widget);

    /// <summary>Watchers, Launcher and Claude Code bind to these (a binding can't call <see cref="PieceStatus"/>).</summary>
    public DeskPieceStatusViewModel WatchersPiece => PieceStatus("watchers");
    public DeskPieceStatusViewModel LauncherPiece => PieceStatus(LauncherModel.Widget);
    public DeskPieceStatusViewModel GlowPiece => PieceStatus("glow");

    private void LoadRows(DeskArrangement a)
    {
        foreach (var row in Pieces)
        {
            var (index, count) = a.StackPosition(row.Widget);
            row.Load(a.Placement(row.Widget), index, count);
        }
    }

    /// <summary>Change the layout through <see cref="DeskArrangement"/> and save.</summary>
    internal void Edit(Action<DeskArrangement> change)
    {
        var a = new DeskArrangement(_saved.Layout);
        change(a);
        Save(_saved with { Layout = a.Layout });
        _loading = true;
        LoadRows(a);
        Companion.RefreshStatus();
        _loading = false;
        Launcher.PlacementChanged();
    }

    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanArrange));
        OnPropertyChanged(nameof(ArrangeNote));
        if (!_loading) Save(_saved with { Enabled = value });
        Companion?.RefreshStatus();
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
