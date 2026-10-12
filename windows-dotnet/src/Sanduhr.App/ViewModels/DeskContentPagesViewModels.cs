using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
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
        Name = NowPlayingApps.DisplayName(appId);
        _loading = true;
        Shown = shown;
        _loading = false;
    }

    public string AppId { get; }
    public string Name { get; }

    [ObservableProperty] private bool _shown;

    partial void OnShownChanged(bool value) { if (!_loading) _owner.SetExcluded(AppId, !value); }
}

/// <summary>
/// Settings, Now playing (spec 6.3, 6.6): the widget line, hide while paused, the apps seen this
/// session with a switch each (an excluded app stays listed so it can come back), the bars beside the
/// Desk piece, and a status line that never shows the title.
/// </summary>
public sealed partial class NowPlayingPageViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly NowPlayingHub? _hub;
    private NowPlayingSettings _saved;
    private bool _loading;

    internal NowPlayingPageViewModel(SettingsStore store, NowPlayingHub? hub)
    {
        _store = store;
        _hub = hub;
        _saved = store.LoadNowPlaying();
        Load();
        if (_hub is not null) _hub.Changed += Refresh;
    }

    /// <summary>Called after every saved change (the widget line and the Desk follow).</summary>
    public Action? Changed { get; set; }

    [ObservableProperty] private bool _widget;
    [ObservableProperty] private bool _hidePaused;
    [ObservableProperty] private bool _bars;
    [ObservableProperty] private string _status = "";

    public ObservableCollection<NowPlayingAppRow> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    private void Load()
    {
        _loading = true;
        Widget = _saved.Widget;
        HidePaused = _saved.HidePaused;
        Bars = _saved.Bars;
        _loading = false;
        Refresh();
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
        Status = NowPlayingStatus.Text(on, _hub?.Shown);
    }

    internal void SetExcluded(string appId, bool excluded)
    {
        var list = _saved.Excluded.Where(e => !string.Equals(e, appId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (excluded) list.Add(appId);
        Save(_saved with { Excluded = list });
    }

    partial void OnWidgetChanged(bool value) { if (!_loading) Save(_saved with { Widget = value }); }
    partial void OnHidePausedChanged(bool value) { if (!_loading) Save(_saved with { HidePaused = value }); }
    partial void OnBarsChanged(bool value) { if (!_loading) Save(_saved with { Bars = value }); }

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
/// rounded to about 1 km. Clear forgets the place. The piece itself is placed on the Desk page.
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
        _loading = false;
    }

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

    [ObservableProperty] private string _city = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private UnitChoice _unit = Units[0];
    [ObservableProperty] private WeatherPlace? _picked;

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
    }

    [RelayCommand]
    private void Clear() => Save(_saved with { Place = null });

    partial void OnUnitChanged(UnitChoice value)
    {
        if (!_loading && value is not null) Save(_saved with { Unit = value.Unit });
    }

    private void Save(DeskWeatherSettings next)
    {
        _saved = next;
        _store.SaveDeskWeather(next);
        OnPropertyChanged(nameof(PlaceText));
        OnPropertyChanged(nameof(HasPlace));
        Changed?.Invoke();
    }
}
