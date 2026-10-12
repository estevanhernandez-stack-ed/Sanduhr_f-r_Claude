using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class WeatherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private const string GeocodeJson = """
        {
          "results": [
            {"id": 4691930, "name": "Fort Worth", "latitude": 32.72541, "longitude": -97.32085,
             "elevation": 198.0, "feature_code": "PPLA2", "country_code": "US", "admin1_id": 4736286,
             "timezone": "America/Chicago", "population": 935508, "country_id": 6252001,
             "country": "United States", "admin1": "Texas"},
            {"id": 5000000, "name": "Fort Worth", "latitude": 40.005, "longitude": -75.005,
             "country": "United States"},
            {"name": "No coordinates"}
          ],
          "generationtime_ms": 0.7
        }
        """;

    private const string ForecastJson = """
        {
          "latitude": 32.72, "longitude": -97.32, "generationtime_ms": 0.05,
          "utc_offset_seconds": -18000, "timezone": "America/Chicago", "timezone_abbreviation": "CDT",
          "elevation": 198.0,
          "current_units": {"time": "iso8601", "interval": "seconds", "temperature_2m": "°F", "weather_code": "wmo code"},
          "current": {"time": "2026-10-11T12:00", "interval": 900, "temperature_2m": 72.4, "weather_code": 2},
          "daily_units": {"time": "iso8601", "weather_code": "wmo code", "temperature_2m_max": "°F", "temperature_2m_min": "°F"},
          "daily": {
            "time": ["2026-10-11", "2026-10-12"],
            "weather_code": [2, 80],
            "temperature_2m_max": [80.3, 75.5],
            "temperature_2m_min": [60.6, 60.0]
          }
        }
        """;

    [Fact]
    public void ParsePlaces_ReadsRealisticAnswer_AndSkipsIncompleteEntries()
    {
        var places = Weather.ParsePlaces(GeocodeJson);
        Assert.Equal(2, places.Count);
        Assert.Equal("Fort Worth, Texas, United States", places[0].Label);
        Assert.Equal(32.73, places[0].Lat);
        Assert.Equal(-97.32, places[0].Lon);
        Assert.Equal("Fort Worth, United States", places[1].Label);
        Assert.Null(places[1].Admin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"results\": 5}")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    [InlineData("{\"results\": [1, null, \"x\"]}")]
    public void ParsePlaces_IsTolerant(string json) => Assert.Empty(Weather.ParsePlaces(json));

    [Fact]
    public void ParsePlaces_NullInput_IsEmpty() => Assert.Empty(Weather.ParsePlaces(null));

    [Fact]
    public void ParseForecast_ReadsCurrentAndBothDays()
    {
        var r = Weather.ParseForecast(ForecastJson, Now);
        Assert.NotNull(r);
        Assert.Equal(72.4, r!.TempNow);
        Assert.Equal(2, r.CodeNow);
        Assert.Equal(80.3, r.TodayHigh);
        Assert.Equal(60.6, r.TodayLow);
        Assert.Equal(80, r.TomorrowCode);
        Assert.Equal(75.5, r.TomorrowHigh);
        Assert.Equal(60.0, r.TomorrowLow);
        Assert.Equal(Now, r.FetchedAt);
    }

    [Fact]
    public void ParseForecast_MissingTomorrow_KeepsToday()
    {
        const string json = """
            {"current": {"temperature_2m": 50, "weather_code": 0},
             "daily": {"weather_code": [0], "temperature_2m_max": [55], "temperature_2m_min": [40]}}
            """;
        var r = Weather.ParseForecast(json, Now);
        Assert.NotNull(r);
        Assert.Null(r!.TomorrowCode);
        Assert.Null(r.TomorrowHigh);
        Assert.Null(r.TomorrowLow);
        Assert.Null(WeatherLines.For(WeatherPlace.Create("X", null, null, 0, 0), r).Tomorrow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"current\": {}, \"daily\": {}}")]
    [InlineData("{\"current\": {\"temperature_2m\": \"warm\", \"weather_code\": 1}, \"daily\": {\"temperature_2m_max\": [1], \"temperature_2m_min\": [0]}}")]
    [InlineData("{\"current\": {\"temperature_2m\": 1, \"weather_code\": 1}, \"daily\": {\"temperature_2m_max\": [], \"temperature_2m_min\": [0]}}")]
    public void ParseForecast_IsTolerant(string json) => Assert.Null(Weather.ParseForecast(json, Now));

    [Fact]
    public void ParseForecast_NullInput_IsNull() => Assert.Null(Weather.ParseForecast(null, Now));

    [Fact]
    public void GeocodeUrl_EscapesSpacesAndNonAscii()
    {
        Assert.Equal(
            "https://geocoding-api.open-meteo.com/v1/search?name=Fort%20Worth&count=5&language=en&format=json",
            Weather.GeocodeUrl("  Fort Worth "));
        Assert.Equal(
            "https://geocoding-api.open-meteo.com/v1/search?name=S%C3%A3o%20Paulo&count=5&language=en&format=json",
            Weather.GeocodeUrl("São Paulo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GeocodeUrl_EmptyCity_IsNull(string? city) => Assert.Null(Weather.GeocodeUrl(city));

    [Fact]
    public void GeocodeUrl_CapsTheCityAt100Characters()
    {
        string url = Weather.GeocodeUrl(new string('a', 300))!;
        Assert.Contains("name=" + new string('a', 100) + "&", url);
    }

    [Fact]
    public void ForecastUrl_UsesTwoDecimalsInvariantAndTheUnit()
    {
        var place = new WeatherPlace("Fort Worth", "Texas", "United States", 32.72541, -97.32085);
        Assert.Equal(
            "https://api.open-meteo.com/v1/forecast?latitude=32.73&longitude=-97.32&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit=fahrenheit&timezone=auto&forecast_days=2",
            Weather.ForecastUrl(place, WeatherUnit.Fahrenheit));
        Assert.Contains("temperature_unit=celsius", Weather.ForecastUrl(place, WeatherUnit.Celsius));
    }

    [Fact]
    public void ForecastUrl_UnderACommaCulture_StillUsesDots()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var url = Weather.ForecastUrl(WeatherPlace.Create("Berlin", null, "Germany", 52.52, 13.405), WeatherUnit.Celsius);
            Assert.Contains("latitude=52.52&longitude=13.41&", url);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Create_RoundsCoordinatesHalfAwayFromZero()
    {
        var p = WeatherPlace.Create("X", null, null, 0.125, -0.125);
        Assert.Equal(0.13, p.Lat);
        Assert.Equal(-0.13, p.Lon);
    }

    [Fact]
    public void Label_SkipsEmptyParts()
    {
        Assert.Equal("Paris, France", new WeatherPlace("Paris", "", "France", 0, 0).Label);
        Assert.Equal("Paris", new WeatherPlace("Paris", null, " ", 0, 0).Label);
    }

    [Fact]
    public void DefaultUnit_FollowsTheRegion()
    {
        Assert.Equal(WeatherUnit.Celsius, Weather.DefaultUnit(true));
        Assert.Equal(WeatherUnit.Fahrenheit, Weather.DefaultUnit(false));
    }

    [Theory]
    [InlineData(0, "Clear")] [InlineData(1, "Mostly clear")] [InlineData(2, "Partly cloudy")] [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")] [InlineData(48, "Fog")]
    [InlineData(51, "Drizzle")] [InlineData(53, "Drizzle")] [InlineData(55, "Drizzle")]
    [InlineData(56, "Freezing drizzle")] [InlineData(57, "Freezing drizzle")]
    [InlineData(61, "Rain")] [InlineData(63, "Rain")] [InlineData(65, "Heavy rain")]
    [InlineData(66, "Freezing rain")] [InlineData(67, "Freezing rain")]
    [InlineData(71, "Snow")] [InlineData(73, "Snow")] [InlineData(77, "Snow")] [InlineData(75, "Heavy snow")]
    [InlineData(80, "Showers")] [InlineData(81, "Showers")] [InlineData(82, "Showers")]
    [InlineData(85, "Snow showers")] [InlineData(86, "Snow showers")]
    [InlineData(95, "Thunderstorm")] [InlineData(96, "Thunderstorm")] [InlineData(99, "Thunderstorm")]
    [InlineData(4, "Unknown")] [InlineData(-1, "Unknown")] [InlineData(100, "Unknown")]
    public void Words_MatchTheSkinTable(int code, string words) => Assert.Equal(words, WeatherCodes.Words(code));

    [Fact]
    public void Schedule_DueWhenNeverTriedOrThirtyMinutesPassed()
    {
        Assert.True(WeatherSchedule.Due(null, Now));
        Assert.False(WeatherSchedule.Due(Now.AddMinutes(-29), Now));
        Assert.True(WeatherSchedule.Due(Now.AddMinutes(-30), Now));
    }

    [Fact]
    public void Schedule_StaleAfterThreeHours()
    {
        var r = Weather.ParseForecast(ForecastJson, Now)!;
        Assert.False(WeatherSchedule.IsStale(r, Now.AddHours(3)));
        Assert.True(WeatherSchedule.IsStale(r, Now.AddHours(3).AddSeconds(1)));
    }

    [Fact]
    public void Lines_MatchTheSkin()
    {
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.72, -97.32);
        var lines = WeatherLines.For(place, Weather.ParseForecast(ForecastJson, Now)!);
        Assert.Equal("Fort Worth", lines.Heading);
        Assert.Equal("Partly cloudy · 72°", lines.Now);
        Assert.Equal("High 80° · Low 61°", lines.Range);
        Assert.Equal("Tomorrow: Showers 76° / 60°", lines.Tomorrow);
    }

    [Fact]
    public void Lines_NegativeTemperatures_UseAsciiMinusAndRoundAwayFromZero()
    {
        var reading = new WeatherReading(-2.5, 71, -0.4, -10.5, 75, -3.2, -7.5, Now);
        var lines = WeatherLines.For(new WeatherPlace("Oslo", null, "Norway", 59.91, 10.75), reading);
        Assert.Equal("Snow · -3°", lines.Now);
        Assert.Equal("High 0° · Low -11°", lines.Range);
        Assert.Equal("Tomorrow: Heavy snow -3° / -8°", lines.Tomorrow);
    }

    [Fact]
    public void Lines_DigitsAreInvariantUnderAnotherCulture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var reading = new WeatherReading(21, 0, 25, 15, null, null, null, Now);
            Assert.Equal("Clear · 21°", WeatherLines.For(new WeatherPlace("X", null, null, 0, 0), reading).Now);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Constants_AreFixed()
    {
        Assert.Equal("Weather data by Open-Meteo.com", Weather.Credit);
        Assert.Equal("https://open-meteo.com/", Weather.CreditUrl);
        Assert.Equal("https://creativecommons.org/licenses/by/4.0/", Weather.LicenceUrl);
        Assert.Equal("Sanduhr", Weather.UserAgent);
        Assert.Equal("Set a city in Settings, Weather", Weather.NoPlaceLine);
    }
}
