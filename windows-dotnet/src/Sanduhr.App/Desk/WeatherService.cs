using System.Net.Http;
using System.Windows.Threading;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The weather piece's data (spec 6.5), from Open-Meteo: no key, no account. Only the place's
/// coordinates (rounded to about 1 km) go out for a forecast, and only the typed city name for a
/// look-up; no cookie, id, machine name or version (the user agent is just "Sanduhr"). It fetches only
/// while <see cref="Start"/>ed (the Desk is on and the piece is placed): once, then at most every 30
/// minutes, a failure waiting as long as a success. The reading stays in memory; nothing about the
/// weather is written anywhere.
/// </summary>
internal sealed class WeatherService : IDisposable
{
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        // network: open-meteo
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Weather.UserAgent);
        return c;
    });

    private readonly DispatcherTimer _timer;
    private WeatherPlace? _place;
    private WeatherUnit _unit;
    private DateTimeOffset? _lastAttempt;
    private bool _busy;

    public WeatherService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) => await FetchIfDue();
    }

    /// <summary>The last good reading for the current place, or null.</summary>
    public WeatherReading? Latest { get; private set; }

    public WeatherPlace? Place => _place;

    /// <summary>A new reading arrived (the controller redraws).</summary>
    public event Action? Updated;

    /// <summary>Fetch for <paramref name="place"/> in <paramref name="unit"/>; a new place or unit drops
    /// the old reading and asks at once.</summary>
    public void Start(WeatherPlace place, WeatherUnit unit)
    {
        if (_place != place || _unit != unit)
        {
            _place = place;
            _unit = unit;
            Latest = null;
            _lastAttempt = null;
        }
        if (!_timer.IsEnabled) _timer.Start();
        _ = FetchIfDue();
    }

    public void Stop() => _timer.Stop();

    private async Task FetchIfDue()
    {
        if (_busy || _place is null || !WeatherSchedule.Due(_lastAttempt, DateTimeOffset.Now)) return;
        var place = _place;
        var unit = _unit;
        _busy = true;
        _lastAttempt = DateTimeOffset.Now;
        try
        {
            var url = Weather.ForecastUrl(place, unit);
            // network: open-meteo (the forecast)
            var json = await Http.Value.GetStringAsync(url);
            var reading = Weather.ParseForecast(json, DateTimeOffset.Now);
            if (reading is not null && place == _place && unit == _unit)
            {
                Latest = reading;
                Updated?.Invoke();
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Keep the last reading; the next attempt waits the full 30 minutes. Never log the place.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] weather fetch failed: {e.GetType().Name}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Settings, Weather's Look up: the places Open-Meteo's geocoding finds for a city name
    /// (one request, the name only). Empty on no match; throws on a network failure so the page can
    /// say so.</summary>
    public static async Task<IReadOnlyList<WeatherPlace>> LookUp(string city, CancellationToken ct = default)
    {
        var url = Weather.GeocodeUrl(city);
        if (url is null) return [];
        // network: open-meteo (Look up)
        var json = await Http.Value.GetStringAsync(url, ct);
        return Weather.ParsePlaces(json);
    }

    public void Dispose() => _timer.Stop();
}
