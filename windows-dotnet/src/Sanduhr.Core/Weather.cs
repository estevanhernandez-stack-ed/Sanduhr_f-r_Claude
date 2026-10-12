using System.Globalization;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>Temperature unit for the forecast request and the lines.</summary>
public enum WeatherUnit { Fahrenheit, Celsius }

/// <summary>
/// A place the user picked: name, admin area, country and coordinates. Build it with
/// <see cref="Create"/> so the coordinates are rounded to two decimals (about 1 km) before they are
/// saved or sent.
/// </summary>
public sealed record WeatherPlace(string Name, string? Admin, string? Country, double Lat, double Lon)
{
    /// <summary>Builds a place with coordinates rounded to 2 decimals, half away from zero.</summary>
    public static WeatherPlace Create(string name, string? admin, string? country, double lat, double lon) =>
        new(name, admin, country, Round2(lat), Round2(lon));

    /// <summary>"Fort Worth, Texas, United States", skipping empty parts.</summary>
    public string Label => string.Join(", ", new[] { Name, Admin, Country }.Where(p => !string.IsNullOrWhiteSpace(p)));

    internal static double Round2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero) + 0.0;
}

/// <summary>One forecast reading: now, today's range and, when present, tomorrow's.</summary>
public sealed record WeatherReading(
    double TempNow, int CodeNow, double TodayHigh, double TodayLow,
    int? TomorrowCode, double? TomorrowHigh, double? TomorrowLow, DateTimeOffset FetchedAt);

/// <summary>The text lines of the Desk weather piece.</summary>
public sealed record WeatherLineSet(string Heading, string Now, string Range, string? Tomorrow);

/// <summary>
/// Open-Meteo URLs and JSON readers for the Desk weather piece (spec 6.5), from Este's Rainmeter
/// weather skin. No key, no account; only the city name and rounded coordinates leave the machine.
/// The readers are tolerant: anything missing or malformed gives null or an empty list.
/// </summary>
public static class Weather
{
    public const string Credit = "Weather data by Open-Meteo.com";
    public const string CreditUrl = "https://open-meteo.com/";
    public const string LicenceUrl = "https://creativecommons.org/licenses/by/4.0/";
    public const string UserAgent = "Sanduhr";
    public const string NoPlaceLine = "Set a city in Settings, Weather";

    private const int MaxCityLength = 100;

    /// <summary>Celsius for metric regions, Fahrenheit otherwise.</summary>
    public static WeatherUnit DefaultUnit(bool isMetric) => isMetric ? WeatherUnit.Celsius : WeatherUnit.Fahrenheit;

    /// <summary>The geocoding search URL for a city, or null for an empty one. The city is trimmed and capped at 100 characters.</summary>
    public static string? GeocodeUrl(string? city)
    {
        string trimmed = (city ?? "").Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxCityLength) trimmed = trimmed[..MaxCityLength].TrimEnd();
        return "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(trimmed)
            + "&count=5&language=en&format=json";
    }

    /// <summary>The forecast URL for a place, coordinates in invariant culture at 2 decimals.</summary>
    public static string ForecastUrl(WeatherPlace place, WeatherUnit unit)
    {
        string lat = WeatherPlace.Round2(place.Lat).ToString("0.00", CultureInfo.InvariantCulture);
        string lon = WeatherPlace.Round2(place.Lon).ToString("0.00", CultureInfo.InvariantCulture);
        return "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon
            + "&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&temperature_unit=" + (unit == WeatherUnit.Celsius ? "celsius" : "fahrenheit")
            + "&timezone=auto&forecast_days=2";
    }

    /// <summary>Reads <c>results[]</c> of a geocoding answer. Entries without a name or coordinates are skipped.</summary>
    public static IReadOnlyList<WeatherPlace> ParsePlaces(string? json)
    {
        var places = new List<WeatherPlace>();
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return places;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array) return places;
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string? name = Str(item, "name");
                double? lat = Num(item, "latitude");
                double? lon = Num(item, "longitude");
                if (string.IsNullOrWhiteSpace(name) || lat is null || lon is null) continue;
                places.Add(WeatherPlace.Create(name, Str(item, "admin1"), Str(item, "country"), lat.Value, lon.Value));
            }
        }
        catch (JsonException) { places.Clear(); }
        return places;
    }

    /// <summary>
    /// Reads a forecast answer. Null when the current temperature or code, or today's high or low,
    /// is missing; tomorrow's values are optional.
    /// </summary>
    public static WeatherReading? ParseForecast(string? json, DateTimeOffset fetchedAt)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("current", out var cur) || cur.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("daily", out var daily) || daily.ValueKind != JsonValueKind.Object) return null;

            double? tempNow = Num(cur, "temperature_2m");
            double? codeNow = Num(cur, "weather_code");
            double? high0 = At(daily, "temperature_2m_max", 0);
            double? low0 = At(daily, "temperature_2m_min", 0);
            if (tempNow is null || codeNow is null || high0 is null || low0 is null) return null;

            double? code1 = At(daily, "weather_code", 1);
            return new WeatherReading(
                tempNow.Value, (int)codeNow.Value, high0.Value, low0.Value,
                code1 is null ? null : (int)code1.Value,
                At(daily, "temperature_2m_max", 1), At(daily, "temperature_2m_min", 1), fetchedAt);
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
        && double.IsFinite(d) ? d : null;

    private static double? At(JsonElement o, string name, int index)
    {
        if (!o.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        if (arr.GetArrayLength() <= index) return null;
        var v = arr[index];
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    }
}

/// <summary>WMO weather-code words in sentence case, from Este's weather skin.</summary>
public static class WeatherCodes
{
    /// <summary>The words for a code; "Unknown" for anything not in the table.</summary>
    public static string Words(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 77 => "Snow",
        75 => "Heavy snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Unknown",
    };
}

/// <summary>When to fetch and when a reading is stale.</summary>
public static class WeatherSchedule
{
    /// <summary>Minimum gap between attempts, success or failure.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(30);

    /// <summary>A reading older than this is drawn dim.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    /// <summary>True when nothing was tried yet or 30 minutes have passed since the last attempt. A failure counts as an attempt.</summary>
    public static bool Due(DateTimeOffset? lastAttempt, DateTimeOffset now) =>
        lastAttempt is not { } at || now - at >= Refresh;

    /// <summary>True when the reading is more than 3 hours old.</summary>
    public static bool IsStale(WeatherReading reading, DateTimeOffset now) => now - reading.FetchedAt > StaleAfter;
}

/// <summary>Builds the Desk weather piece's text lines.</summary>
public static class WeatherLines
{
    /// <summary>Heading, now, range and tomorrow lines. Tomorrow is null unless its code, high and low are all present.</summary>
    public static WeatherLineSet For(WeatherPlace place, WeatherReading reading)
    {
        string? tomorrow = reading is { TomorrowCode: { } code, TomorrowHigh: { } hi, TomorrowLow: { } lo }
            ? $"Tomorrow: {WeatherCodes.Words(code)} {Deg(hi)} / {Deg(lo)}"
            : null;
        return new WeatherLineSet(
            place.Name,
            $"{WeatherCodes.Words(reading.CodeNow)} · {Deg(reading.TempNow)}",
            $"High {Deg(reading.TodayHigh)} · Low {Deg(reading.TodayLow)}",
            tomorrow);
    }

    private static string Deg(double t)
    {
        int n = (int)Math.Round(t, MidpointRounding.AwayFromZero);
        return n.ToString(CultureInfo.InvariantCulture) + "°";
    }
}
