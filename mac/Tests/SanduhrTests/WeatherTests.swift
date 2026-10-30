import Foundation
import AppKit
import Testing
@testable import Sanduhr

/// The weather piece's core (2.38.0 item 2), ported from Windows' WeatherTests: URLs, readers, words,
/// schedule, lines, the nickname rules and the Desk defaults record.
@Suite("Weather: core")
struct WeatherCoreTests {
    static let now = Date(timeIntervalSince1970: 1_791_720_000)

    static let geocodeJSON = """
    {"results": [
      {"id": 4691930, "name": "Fort Worth", "latitude": 32.72541, "longitude": -97.32085, "country": "United States", "admin1": "Texas"},
      {"id": 5000000, "name": "Fort Worth", "latitude": 40.005, "longitude": -75.005, "country": "United States"},
      {"name": "No coordinates"}], "generationtime_ms": 0.7}
    """

    static let forecastJSON = """
    {"latitude": 32.72, "longitude": -97.32, "timezone": "America/Chicago",
     "current": {"time": "2026-10-11T12:00", "interval": 900, "temperature_2m": 72.4, "weather_code": 2},
     "daily": {"time": ["2026-10-11", "2026-10-12"], "weather_code": [2, 80],
               "temperature_2m_max": [80.3, 75.5], "temperature_2m_min": [60.6, 60.0]}}
    """

    private func data(_ s: String?) -> Data? { s.map { Data($0.utf8) } }

    @Test func parsePlacesReadsARealisticAnswerAndSkipsIncompleteEntries() {
        let places = Weather.parsePlaces(data(Self.geocodeJSON))
        #expect(places.count == 2)
        #expect(places[0].label == "Fort Worth, Texas, United States")
        #expect(places[0].lat == 32.73)
        #expect(places[0].lon == -97.32)
        #expect(places[1].label == "Fort Worth, United States")
        #expect(places[1].admin == nil)
    }

    @Test(arguments: ["", "   ", "not json", "{\"results\": 5}", "{}", "[1,2]", "{\"results\": [1, null, \"x\"]}"])
    func parsePlacesIsTolerant(json: String) {
        #expect(Weather.parsePlaces(data(json)).isEmpty)
    }

    @Test func parsePlacesNilIsEmpty() { #expect(Weather.parsePlaces(nil).isEmpty) }

    @Test func parseForecastReadsCurrentAndBothDays() throws {
        let r = try #require(Weather.parseForecast(data(Self.forecastJSON), fetchedAt: Self.now))
        #expect(r.tempNow == 72.4)
        #expect(r.codeNow == 2)
        #expect(r.todayHigh == 80.3)
        #expect(r.todayLow == 60.6)
        #expect(r.tomorrowCode == 80)
        #expect(r.tomorrowHigh == 75.5)
        #expect(r.tomorrowLow == 60.0)
        #expect(r.fetchedAt == Self.now)
    }

    @Test func parseForecastMissingTomorrowKeepsToday() throws {
        let json = """
        {"current": {"temperature_2m": 50, "weather_code": 0},
         "daily": {"weather_code": [0], "temperature_2m_max": [55], "temperature_2m_min": [40]}}
        """
        let r = try #require(Weather.parseForecast(data(json), fetchedAt: Self.now))
        #expect(r.tomorrowCode == nil && r.tomorrowHigh == nil && r.tomorrowLow == nil)
        #expect(WeatherLines.make(.create("X", admin: nil, country: nil, lat: 0, lon: 0), r).tomorrow == nil)
    }

    @Test(arguments: [
        "", "{", "null", "{}", "{\"current\": {}, \"daily\": {}}",
        "{\"current\": {\"temperature_2m\": \"warm\", \"weather_code\": 1}, \"daily\": {\"temperature_2m_max\": [1], \"temperature_2m_min\": [0]}}",
        "{\"current\": {\"temperature_2m\": 1, \"weather_code\": 1}, \"daily\": {\"temperature_2m_max\": [], \"temperature_2m_min\": [0]}}",
        "{\"current\": {\"temperature_2m\": true, \"weather_code\": 1}, \"daily\": {\"temperature_2m_max\": [1], \"temperature_2m_min\": [0]}}",
    ])
    func parseForecastIsTolerant(json: String) {
        #expect(Weather.parseForecast(data(json), fetchedAt: Self.now) == nil)
    }

    @Test func parseForecastNilIsNil() { #expect(Weather.parseForecast(nil, fetchedAt: Self.now) == nil) }

    @Test func geocodeURLEscapesSpacesAndNonAscii() {
        #expect(Weather.geocodeURL("  Fort Worth ")?.absoluteString
                == "https://geocoding-api.open-meteo.com/v1/search?name=Fort%20Worth&count=5&language=en&format=json")
        #expect(Weather.geocodeURL("São Paulo")?.absoluteString
                == "https://geocoding-api.open-meteo.com/v1/search?name=S%C3%A3o%20Paulo&count=5&language=en&format=json")
    }

    @Test(arguments: [nil, "", "   "] as [String?])
    func geocodeURLEmptyCityIsNil(city: String?) { #expect(Weather.geocodeURL(city) == nil) }

    @Test func geocodeURLCapsTheCityAt100Characters() throws {
        let url = try #require(Weather.geocodeURL(String(repeating: "a", count: 300))?.absoluteString)
        #expect(url.contains("name=" + String(repeating: "a", count: 100) + "&"))
    }

    @Test func forecastURLUsesTwoDecimalsAndTheUnit() {
        let place = WeatherPlace(name: "Fort Worth", admin: "Texas", country: "United States", lat: 32.72541, lon: -97.32085)
        #expect(Weather.forecastURL(place, unit: .fahrenheit).absoluteString
                == "https://api.open-meteo.com/v1/forecast?latitude=32.73&longitude=-97.32&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit=fahrenheit&timezone=auto&forecast_days=2")
        #expect(Weather.forecastURL(place, unit: .celsius).absoluteString.contains("temperature_unit=celsius"))
    }

    @Test func forecastURLNeverCarriesAName() {
        let place = WeatherPlace(name: "Fort Worth", admin: "Texas", country: "United States", lat: 32.7, lon: -97.3)
        let url = Weather.forecastURL(place, unit: .celsius).absoluteString
        #expect(!url.contains("Fort") && !url.contains("Texas"))
    }

    @Test func createRoundsCoordinatesHalfAwayFromZero() {
        let p = WeatherPlace.create("X", admin: nil, country: nil, lat: 0.125, lon: -0.125)
        #expect(p.lat == 0.13 && p.lon == -0.13)
    }

    @Test func labelSkipsEmptyParts() {
        #expect(WeatherPlace(name: "Paris", admin: "", country: "France", lat: 0, lon: 0).label == "Paris, France")
        #expect(WeatherPlace(name: "Paris", admin: nil, country: " ", lat: 0, lon: 0).label == "Paris")
    }

    @Test func defaultUnitFollowsTheRegion() {
        #expect(Weather.defaultUnit(isMetric: true) == .celsius)
        #expect(Weather.defaultUnit(isMetric: false) == .fahrenheit)
    }

    @Test(arguments: [
        (0, "Clear"), (1, "Mostly clear"), (2, "Partly cloudy"), (3, "Overcast"), (45, "Fog"), (48, "Fog"),
        (51, "Drizzle"), (53, "Drizzle"), (55, "Drizzle"), (56, "Freezing drizzle"), (57, "Freezing drizzle"),
        (61, "Rain"), (63, "Rain"), (65, "Heavy rain"), (66, "Freezing rain"), (67, "Freezing rain"),
        (71, "Snow"), (73, "Snow"), (77, "Snow"), (75, "Heavy snow"), (80, "Showers"), (81, "Showers"), (82, "Showers"),
        (85, "Snow showers"), (86, "Snow showers"), (95, "Thunderstorm"), (96, "Thunderstorm"), (99, "Thunderstorm"),
        (4, "Unknown"), (-1, "Unknown"), (100, "Unknown"),
    ] as [(Int, String)])
    func wordsMatchTheTable(code: Int, words: String) { #expect(WeatherCodes.words(code) == words) }

    @Test func everyCodeHasASymbolThatExists() {
        for code in [-1, 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99, 100] {
            let name = WeatherCodes.symbol(code)
            #expect(NSImage(systemSymbolName: name, accessibilityDescription: nil) != nil, "\(code): \(name)")
        }
    }

    @Test func scheduleIsDueWhenNeverTriedOrThirtyMinutesPassed() {
        #expect(WeatherSchedule.due(lastAttempt: nil, now: Self.now))
        #expect(!WeatherSchedule.due(lastAttempt: Self.now.addingTimeInterval(-29 * 60), now: Self.now))
        #expect(WeatherSchedule.due(lastAttempt: Self.now.addingTimeInterval(-30 * 60), now: Self.now))
    }

    @Test func staleAfterThreeHours() throws {
        let r = try #require(Weather.parseForecast(data(Self.forecastJSON), fetchedAt: Self.now))
        #expect(!WeatherSchedule.isStale(r, now: Self.now.addingTimeInterval(3 * 3600)))
        #expect(WeatherSchedule.isStale(r, now: Self.now.addingTimeInterval(3 * 3600 + 1)))
    }

    @Test func linesMatchTheSkin() throws {
        let place = WeatherPlace.create("Fort Worth", admin: "Texas", country: "United States", lat: 32.72, lon: -97.32)
        let lines = WeatherLines.make(place, try #require(Weather.parseForecast(data(Self.forecastJSON), fetchedAt: Self.now)))
        #expect(lines.heading == "Fort Worth")
        #expect(lines.now == "Partly cloudy · 72°")
        #expect(lines.range == "High 80° · Low 61°")
        #expect(lines.tomorrow == "Tomorrow: Showers 76° / 60°")
    }

    @Test func negativeTemperaturesUseAsciiMinusAndRoundAwayFromZero() {
        let reading = WeatherReading(tempNow: -2.5, codeNow: 71, todayHigh: -0.4, todayLow: -10.5,
                                     tomorrowCode: 75, tomorrowHigh: -3.2, tomorrowLow: -7.5, fetchedAt: Self.now)
        let lines = WeatherLines.make(WeatherPlace(name: "Oslo", admin: nil, country: "Norway", lat: 59.91, lon: 10.75), reading)
        #expect(lines.now == "Snow · -3°")
        #expect(lines.range == "High 0° · Low -11°")
        #expect(lines.tomorrow == "Tomorrow: Heavy snow -3° / -8°")
    }

    @Test func theHeadingIsTheNicknameWhenSet() {
        let reading = WeatherReading(tempNow: 1, codeNow: 0, todayHigh: 2, todayLow: 0, tomorrowCode: nil, tomorrowHigh: nil, tomorrowLow: nil, fetchedAt: Self.now)
        let place = WeatherPlace(name: "Fort Worth", admin: nil, country: nil, lat: 0, lon: 0)
        #expect(WeatherLines.make(place, reading, nickname: "  Home ").heading == "Home")
        #expect(WeatherLines.make(place, reading, nickname: "   ").heading == "Fort Worth")
        #expect(WeatherLines.make(place, reading, nickname: nil).heading == "Fort Worth")
    }

    @Test func constantsAreFixed() {
        #expect(Weather.credit == "Weather data by Open-Meteo.com")
        #expect(Weather.creditURL.absoluteString == "https://open-meteo.com/")
        #expect(Weather.licenceURL.absoluteString == "https://creativecommons.org/licenses/by/4.0/")
        #expect(Weather.userAgent == "Sanduhr")
        #expect(Weather.noPlaceLine == "Set a city in Settings, Weather")
        #expect(WeatherSchedule.refresh == 1800)
        #expect(WeatherSchedule.staleAfter == 10_800)
    }

    // MARK: Nickname

    @Test func nicknameIsTrimmedAndCappedAt24Characters() {
        #expect(Weather.normalizeNickname("  Home  ") == "Home")
        #expect(Weather.normalizeNickname(nil) == "")
        #expect(Weather.normalizeNickname("   ") == "")
        #expect(Weather.normalizeNickname(String(repeating: "a", count: 40)) == String(repeating: "a", count: 24))
        #expect(Weather.normalizeNickname(String(repeating: "a", count: 23) + " bbb") == String(repeating: "a", count: 23))
    }

    @Test func nicknameDropsControlCharactersAndLineBreaks() {
        #expect(Weather.normalizeNickname("Ho\nme\t\u{7}") == "Home")
        #expect(Weather.normalizeNickname("A\u{2028}B\u{2029}C") == "ABC")
    }

    @Test func nicknameCapKeepsWholeScalarsNeverHalfAPair() {
        let nick = Weather.normalizeNickname(String(repeating: "\u{1F324}", count: 30))
        #expect(nick.unicodeScalars.count == 24)
        #expect(nick.unicodeScalars.allSatisfy { $0.value == 0x1F324 })
    }

    // MARK: Defaults record

    private func suite() -> UserDefaults {
        let d = UserDefaults(suiteName: "sanduhr.tests.weather." + UUID().uuidString.prefix(8))!
        return d
    }

    @Test func nothingIsWrittenUntilSomethingIsSet() {
        let d = suite()
        DeskWeatherSettings().save(d)
        #expect(d.object(forKey: DeskWeatherSettings.key) == nil)
        #expect(DeskWeatherSettings.load(d) == DeskWeatherSettings())
    }

    @Test func theNicknameIsWrittenOnlyWhenSet() {
        let d = suite()
        let place = WeatherPlace.create("Fort Worth", admin: "Texas", country: "United States", lat: 32.7254, lon: -97.3208)
        DeskWeatherSettings(place: place, unit: .celsius).save(d)
        let plain = d.dictionary(forKey: DeskWeatherSettings.key)
        #expect(plain?["nickname"] == nil)
        #expect(plain?["name"] as? String == "Fort Worth" && plain?["unit"] as? String == "celsius")
        DeskWeatherSettings(place: place, unit: .celsius, nickname: "  Home ").save(d)
        #expect(d.dictionary(forKey: DeskWeatherSettings.key)?["nickname"] as? String == "Home")
        let back = DeskWeatherSettings.load(d)
        #expect(back.place == place && back.unit == .celsius && back.nickname == "Home")
    }

    @Test func aHostileRecordDecodesToNoPlaceAndAShortNickname() {
        let s = DeskWeatherSettings.decode(["name": "X", "lat": 123.0, "lon": 5.0, "nickname": String(repeating: "z", count: 99), "unit": "kelvin"])
        #expect(s.place == nil && s.unit == nil)
        #expect(s.nickname.count == 24)
        #expect(DeskWeatherSettings.decode(["name": " ", "lat": 1.0, "lon": 1.0]).place == nil)
        #expect(DeskWeatherSettings.decode(nil) == DeskWeatherSettings())
    }

    @Test func unitFallsBackToTheRegionAndShownToThePlace() {
        let place = WeatherPlace(name: "Fort Worth", admin: nil, country: nil, lat: 0, lon: 0)
        #expect(DeskWeatherSettings().unit(isMetric: true) == .celsius)
        #expect(DeskWeatherSettings(unit: .fahrenheit).unit(isMetric: true) == .fahrenheit)
        #expect(DeskWeatherSettings().shown(place) == "Fort Worth")
        #expect(DeskWeatherSettings(nickname: "Home").shown(place) == "Home")
    }
}
