using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One app seen playing this session, with its switch (on = it shows).</summary>
public sealed partial class NowPlayingAppRow : ObservableObject
{
    private readonly NowPlayingPageViewModel _owner;
    private bool _loading;

    internal NowPlayingAppRow(NowPlayingPageViewModel owner, string appId, bool shown)
    {
        _owner = owner;
        AppId = appId;
        // The Start menu's name ("YouTube Music"), not the raw id of a web app ("Chrome._crx_…"): #317.
        Name = NowPlayingApps.DisplayName(appId, LauncherAppCache.Current is { } apps ? apps.StartMenuName : null);
        _loading = true;
        Shown = shown;
        _loading = false;
    }

    public string AppId { get; }
    public string Name { get; }

    public override string ToString() => Name;

    [ObservableProperty] private bool _shown;

    partial void OnShownChanged(bool value) { if (!_loading) _owner.SetExcluded(AppId, !value); }
}

/// <summary>
/// One song of Claude's suggestion, drawn in its look on black (Settings, Now playing, Looks): the title and artist in the look's gradient and
/// letter style, and its mood. A screen reader reads "Title by Artist, small caps, mood neon", with the plain words.
/// </summary>
public sealed class SongLookRow
{
    internal SongLookRow(SongLook song)
    {
        Title = song.Title;
        Artist = song.Artist;
        Style = song.Look.Style;
        Mood = song.Look.Mood ?? "";
        string line = song.Artist.Length == 0 ? song.Title : $"{song.Title} · {song.Artist}";
        Display = LookStyles.Text(line, song.Look.Style);
        Foreground = DeskView.InkBrush([.. song.Look.Ink.Select(h => InkHex.Normalize(h) ?? InkHex.Fallback)]);
        FontWeight = LookStyles.IsBold(song.Look.Style) ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = LookStyles.IsItalic(song.Look.Style) ? FontStyles.Italic : FontStyles.Normal;
        string by = song.Artist.Length == 0 ? song.Title : $"{song.Title} by {song.Artist}";
        string style = song.Look.Style == LookStyle.Plain ? "" : ", " + LookStyles.Title(song.Look.Style).ToLowerInvariant();
        AccessibleName = by + style + (Mood.Length > 0 ? ", mood " + Mood : "");
    }

    public string Title { get; }
    public string Artist { get; }
    public LookStyle Style { get; }
    public string Mood { get; }
    /// <summary>The words as drawn (small caps as the phonetic letters).</summary>
    public string Display { get; }
    public Brush Foreground { get; }
    public FontWeight FontWeight { get; }
    public FontStyle FontStyle { get; }
    public string AccessibleName { get; }
    public bool HasMood => Mood.Length > 0;

    public override string ToString() => AccessibleName;
}

/// <summary>
/// Settings, Now playing (spec 6.3, 6.6): the widget line, keep showing while paused, the apps seen this
/// session with a switch each (an excluded app stays listed so it can come back), the bars beside the
/// Desk piece, and a status line that never shows the title. Looks (spec, a look per song): "Style what's playing",
/// "Let Claude style songs directly", how many looks are saved, Clear Looks… (after a question), and the card for Claude's
/// suggestion with Save and Dismiss, each song drawn in its look.
/// </summary>
public sealed partial class NowPlayingPageViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly NowPlayingHub? _hub;
    private readonly NowPlayingLookService? _looks;
    private NowPlayingSettings _saved;
    private bool _loading;
    private Window? _owner;

    internal NowPlayingPageViewModel(SettingsStore store, NowPlayingHub? hub, NowPlayingLookService? looks = null)
    {
        _store = store;
        _hub = hub;
        _looks = looks;
        _saved = store.LoadNowPlaying();
        Load();
        if (_hub is not null) _hub.Changed += Refresh;
        if (_looks is not null)
        {
            _looks.BookChanged += RefreshLooks;
            _looks.PendingChanged += RefreshLooks;
        }
    }

    /// <summary>The Settings window, for the question Clear Looks… asks.</summary>
    public void AttachOwner(Window owner) => _owner = owner;

    /// <summary>Answers "Clear every saved look?" in place of the dialog (tests set it).</summary>
    internal Func<bool>? ConfirmClear { get; set; }

    /// <summary>Called after every saved change (the widget line and the Desk follow).</summary>
    public Action? Changed { get; set; }

    /// <summary>The Now playing piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }

    [ObservableProperty] private bool _widget;
    [ObservableProperty] private bool _keepPaused;
    [ObservableProperty] private bool _bars;
    [ObservableProperty] private bool _barsGradient;
    [ObservableProperty] private bool _style;
    [ObservableProperty] private bool _looksDirect;
    [ObservableProperty] private string _status = "";

    public ObservableCollection<NowPlayingAppRow> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    private void Load()
    {
        _loading = true;
        Widget = _saved.Widget;
        KeepPaused = _saved.KeepPaused;
        Bars = _saved.Bars;
        BarsGradient = _saved.BarsGradient;
        Style = _saved.Style;
        LooksDirect = _saved.LooksDirect;
        _loading = false;
        Refresh();
        RefreshLooks();
    }

    /// <summary>The page is shown: re-read the apps seen so far.</summary>
    public void Refresh()
    {
        var seen = _hub?.Service.SeenApps ?? [];
        var rows = NowPlayingApps.Rows(seen, _saved.Excluded);
        if (!rows.SequenceEqual(Apps.Select(a => a.AppId), StringComparer.OrdinalIgnoreCase))
        {
            Apps.Clear();
            foreach (var id in rows) Apps.Add(new NowPlayingAppRow(this, id, !_saved.Prefs.IsExcluded(id)));
            OnPropertyChanged(nameof(HasApps));
        }
        bool on = _saved.Widget || (_hub?.Service.Running ?? false);
        Status = NowPlayingStatus.Text(on, _hub?.Shown, LauncherAppCache.Current is { } apps ? apps.StartMenuName : null);
    }

    /// <summary>The settings changed behind the page (theme match's Follow off or Match now turned Gradient bars on): read them again.</summary>
    public void ReloadSettings()
    {
        _saved = _store.LoadNowPlaying();
        Load();
    }

    internal void SetExcluded(string appId, bool excluded)
    {
        var list = _saved.Excluded.Where(e => !string.Equals(e, appId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (excluded) list.Add(appId);
        Save(_saved with { Excluded = list });
    }

    partial void OnWidgetChanged(bool value) { if (!_loading) Save(_saved with { Widget = value }); }
    partial void OnKeepPausedChanged(bool value) { if (!_loading) Save(_saved with { KeepPaused = value }); }
    // The bars and the song's look draw on the Desk: turning either on shows a hidden piece (Principle 4).
    partial void OnBarsChanged(bool value)
    {
        if (_loading) return;
        Save(_saved with { Bars = value });
        if (value) Piece?.PlaceIfHidden();
    }
    partial void OnBarsGradientChanged(bool value) { if (!_loading) Save(_saved with { BarsGradient = value }); }
    partial void OnStyleChanged(bool value)
    {
        if (_loading) return;
        Save(_saved with { Style = value });
        if (value) Piece?.PlaceIfHidden();
    }
    partial void OnLooksDirectChanged(bool value) { if (!_loading) Save(_saved with { LooksDirect = value }); }

    // ---- Looks (spec, a look per song) ----

    /// <summary>"3 saved looks", "1 saved look" or "No saved looks".</summary>
    [ObservableProperty] private string _savedLooksText = "No saved looks";

    [ObservableProperty] private bool _canClearLooks;
    [ObservableProperty] private bool _hasSuggestion;
    [ObservableProperty] private string _suggestionTitle = "";
    [ObservableProperty] private string _suggestionNote = "";

    /// <summary>The songs of Claude's waiting suggestion, each drawn in its look.</summary>
    public ObservableCollection<SongLookRow> SuggestionSongs { get; } = [];

    /// <summary>Reads the saved looks and the waiting suggestion again (they change from the files, not from this page).</summary>
    public void RefreshLooks()
    {
        int n = _looks?.Handoff.Book.Count ?? 0;
        SavedLooksText = n == 0 ? "No saved looks" : n == 1 ? "1 saved look" : $"{n} saved looks";
        CanClearLooks = n > 0;
        var pending = _looks?.Handoff.Pending;
        HasSuggestion = pending is not null;
        SuggestionTitle = pending?.Title ?? "";
        SuggestionNote = pending?.Note ?? "";
        SuggestionSongs.Clear();
        if (pending is not null)
            foreach (var song in pending.Looks) SuggestionSongs.Add(new SongLookRow(song));
    }

    /// <summary>Save: Claude's suggested looks go into the saved looks, and the Desk changes at once.</summary>
    [RelayCommand]
    private void SaveSuggestion() => _looks?.Handoff.Approve();

    /// <summary>Dismiss: the suggestion is dropped and Claude hears so.</summary>
    [RelayCommand]
    private void DismissSuggestion() => _looks?.Handoff.Dismiss();

    /// <summary>Clear Looks…: after "Clear every saved look?", the saved looks go and every song wears its seeded look again.</summary>
    [RelayCommand]
    private void ClearLooks()
    {
        if (_looks is null || !CanClearLooks) return;
        bool yes = ConfirmClear?.Invoke() ?? ThemedDialog.Show(_owner, "Clear every saved look?",
            "Every song goes back to the look it is given by its name. Looks Claude suggests again can be saved again.",
            MessageBoxButton.YesNo, ThemedDialogKind.Warning, primaryLabel: "Clear Looks", secondaryLabel: "Cancel") == MessageBoxResult.Yes;
        if (yes) _looks.Handoff.Clear();
    }

    private void Save(NowPlayingSettings next)
    {
        _saved = next;
        _store.SaveNowPlaying(next);
        Changed?.Invoke();
        Refresh();
    }
}

/// <summary>
/// Settings, Weather (spec 6.5): type a city, Look up sends that name (and only that) to Open-Meteo's
/// place search, pick one of the places found, choose the unit. The place's coordinates are saved
/// rounded to about 1 km. Clear forgets the place. "Show it as" (#315) is a nickname the Desk shows instead of the place's
/// name: kept on this PC only, never sent, and the forecast still uses the picked place. Picking a place shows a hidden Weather piece (Principle 4); where it sits is Settings, Desk, Pieces.
/// </summary>
public sealed partial class WeatherPageViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private DeskWeatherSettings _saved;
    private bool _loading;
    private CancellationTokenSource? _lookUp;

    public WeatherPageViewModel(SettingsStore store)
    {
        _store = store;
        _saved = store.LoadDeskWeather();
        _loading = true;
        Unit = Units.First(u => u.Unit == _saved.UnitOr(RegionInfo.CurrentRegion.IsMetric));
        Nickname = _saved.Nickname;
        _loading = false;
    }

    /// <summary>The nickname's longest length, for the text box.</summary>
    public static int NicknameMax => DeskDesign.WeatherNicknameMax;

    /// <summary>The hint under "Show it as": what it does, and that it is never sent.</summary>
    public const string NicknameHint =
        "Shown on the Desk instead of the city's name, up to 24 characters. It stays on this PC and is never sent anywhere; "
        + "the forecast still uses the city you picked.";

    public sealed record UnitChoice(WeatherUnit Unit, string Name)
    {
        public override string ToString() => Name;
    }

    public static IReadOnlyList<UnitChoice> Units { get; } =
        [new(WeatherUnit.Fahrenheit, "Fahrenheit (°F)"), new(WeatherUnit.Celsius, "Celsius (°C)")];

    public string Credit => Weather.Credit;
    public string CreditUrl => Weather.CreditUrl;
    public string LicenceUrl => Weather.LicenceUrl;

    /// <summary>Called after every saved change (the Desk follows).</summary>
    public Action? Changed { get; set; }

    /// <summary>The Weather piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }

    [ObservableProperty] private string _city = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private UnitChoice _unit = Units[0];
    [ObservableProperty] private WeatherPlace? _picked;
    [ObservableProperty] private string _nickname = "";

    public ObservableCollection<WeatherPlace> Results { get; } = [];

    /// <summary>"Fort Worth, Texas, United States", or "No city set".</summary>
    public string PlaceText => _saved.Place?.Label ?? "No city set";

    public bool HasPlace => _saved.Place is not null;

    [RelayCommand]
    private async Task LookUp()
    {
        _lookUp?.Cancel();
        var cts = _lookUp = new CancellationTokenSource();
        Results.Clear();
        if (string.IsNullOrWhiteSpace(City)) { Note = "Type a city first."; return; }
        Note = "Looking up…";
        try
        {
            var places = await WeatherService.LookUp(City, cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var p in places) Results.Add(p);
            Note = places.Count == 0 ? "No place by that name. Try the city with its country." : "Pick your place:";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (!cts.IsCancellationRequested) Note = "Open-Meteo didn't answer. Check the connection and try again.";
        }
    }

    partial void OnPickedChanged(WeatherPlace? value)
    {
        if (value is null) return;
        Save(_saved with { Place = WeatherPlace.Create(value.Name, value.Admin, value.Country, value.Lat, value.Lon) });
        Results.Clear();
        Note = "";
        City = "";
        Piece?.PlaceIfHidden();
    }

    [RelayCommand]
    private void Clear() => Save(_saved with { Place = null });

    partial void OnUnitChanged(UnitChoice value)
    {
        if (!_loading && value is not null) Save(_saved with { Unit = value.Unit });
    }

    partial void OnNicknameChanged(string value)
    {
        if (_loading) return;
        var nick = DeskWeatherSettings.NormalizeNickname(value);
        // A design may have set the nickname since the page opened: compare with what is saved now.
        _saved = _store.LoadDeskWeather();
        if (nick != _saved.Nickname) Save(_saved with { Nickname = nick });
    }

    private void Save(DeskWeatherSettings next)
    {
        // Keep a nickname a design applied while the page was open, unless this change is to the nickname itself.
        var stored = _store.LoadDeskWeather();
        if (next.Nickname == _saved.Nickname && stored.Nickname != _saved.Nickname) next = next with { Nickname = stored.Nickname };
        _saved = next;
        _store.SaveDeskWeather(next);
        OnPropertyChanged(nameof(PlaceText));
        OnPropertyChanged(nameof(HasPlace));
        Changed?.Invoke();
    }
}
