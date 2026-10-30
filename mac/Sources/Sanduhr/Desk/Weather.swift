import Foundation

/// Temperature unit for the forecast request and the lines.
enum WeatherUnit: String, Equatable {
    case fahrenheit, celsius
}

/// A place the user picked: name, admin area, country and coordinates. Build it with `create` so the
/// coordinates are rounded to two decimals (about 1 km) before they are saved or sent.
struct WeatherPlace: Equatable {
    var name: String
    var admin: String?
    var country: String?
    var lat: Double
    var lon: Double

    static func create(_ name: String, admin: String?, country: String?, lat: Double, lon: Double) -> WeatherPlace {
        WeatherPlace(name: name, admin: admin, country: country, lat: round2(lat), lon: round2(lon))
    }

    /// "Fort Worth, Texas, United States", skipping empty parts.
    var label: String {
        [name, admin, country].compactMap { $0 }
            .filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
            .joined(separator: ", ")
    }

    /// Two decimals, half away from zero; never negative zero.
    static func round2(_ v: Double) -> Double {
        (v * 100).rounded(.toNearestOrAwayFromZero) / 100 + 0.0
    }
}

/// One forecast reading: now, today's range and, when present, tomorrow's.
struct WeatherReading: Equatable {
    var tempNow: Double
    var codeNow: Int
    var todayHigh: Double
    var todayLow: Double
    var tomorrowCode: Int?
    var tomorrowHigh: Double?
    var tomorrowLow: Double?
    var fetchedAt: Date
}

/// The text lines of the Desk weather piece.
struct WeatherLineSet: Equatable {
    var heading: String
    var now: String
    var range: String
    var tomorrow: String?
}

/// Open-Meteo URLs and JSON readers for the Desk weather piece (spec section 13), ported from
/// Windows' `Weather`. No key, no account; only the city name and rounded coordinates leave the
/// machine. The readers are tolerant: anything missing or malformed gives nil or an empty list.
enum Weather {
    static let credit = "Weather data by Open-Meteo.com"
    static let creditURL = URL(string: "https://open-meteo.com/")!
    static let licenceURL = URL(string: "https://creativecommons.org/licenses/by/4.0/")!
    static let userAgent = "Sanduhr"
    static let noPlaceLine = "Set a city in Settings, Weather"

    static let maxCityLength = 100
    /// The longest nickname, in characters (the same cap as Windows' `DeskDesign.WeatherNicknameMax`).
    static let nicknameMax = 24

    /// Celsius for metric regions, Fahrenheit otherwise.
    static func defaultUnit(isMetric: Bool) -> WeatherUnit { isMetric ? .celsius : .fahrenheit }

    static var regionIsMetric: Bool { Locale.current.measurementSystem == .metric }

    /// The geocoding search URL for a city, or nil for an empty one. The city is trimmed and capped at 100 characters.
    static func geocodeURL(_ city: String?) -> URL? {
        var trimmed = (city ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return nil }
        if trimmed.count > maxCityLength {
            trimmed = String(trimmed.prefix(maxCityLength)).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return URL(string: "https://geocoding-api.open-meteo.com/v1/search?name=" + escape(trimmed)
                   + "&count=5&language=en&format=json")
    }

    /// The forecast URL for a place, coordinates at 2 decimals with a dot whatever the locale.
    static func forecastURL(_ place: WeatherPlace, unit: WeatherUnit) -> URL {
        let lat = twoDecimals(WeatherPlace.round2(place.lat))
        let lon = twoDecimals(WeatherPlace.round2(place.lon))
        return URL(string: "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon
                   + "&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min"
                   + "&temperature_unit=" + (unit == .celsius ? "celsius" : "fahrenheit")
                   + "&timezone=auto&forecast_days=2")!
    }

    /// `results[]` of a geocoding answer. Entries without a name or coordinates are skipped.
    static func parsePlaces(_ json: Data?) -> [WeatherPlace] {
        guard let json, let root = try? JSONSerialization.jsonObject(with: json) as? [String: Any],
              let results = root["results"] as? [Any] else { return [] }
        var places: [WeatherPlace] = []
        for case let item as [String: Any] in results {
            guard let name = item["name"] as? String, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  let lat = number(item["latitude"]), let lon = number(item["longitude"]) else { continue }
            places.append(.create(name, admin: item["admin1"] as? String, country: item["country"] as? String,
                                  lat: lat, lon: lon))
        }
        return places
    }

    /// A forecast answer. Nil when the current temperature or code, or today's high or low, is
    /// missing; tomorrow's values are optional.
    static func parseForecast(_ json: Data?, fetchedAt: Date) -> WeatherReading? {
        guard let json, let root = try? JSONSerialization.jsonObject(with: json) as? [String: Any],
              let cur = root["current"] as? [String: Any],
              let daily = root["daily"] as? [String: Any],
              let tempNow = number(cur["temperature_2m"]), let codeNow = number(cur["weather_code"]),
              let high0 = at(daily, "temperature_2m_max", 0), let low0 = at(daily, "temperature_2m_min", 0)
        else { return nil }
        return WeatherReading(
            tempNow: tempNow, codeNow: Int(codeNow), todayHigh: high0, todayLow: low0,
            tomorrowCode: at(daily, "weather_code", 1).map { Int($0) },
            tomorrowHigh: at(daily, "temperature_2m_max", 1), tomorrowLow: at(daily, "temperature_2m_min", 1),
            fetchedAt: fetchedAt)
    }

    /// A nickname as it is kept: control characters dropped, trimmed, and cut to 24 characters.
    static func normalizeNickname(_ raw: String?) -> String {
        guard let raw, !raw.isEmpty else { return "" }
        var kept = String.UnicodeScalarView()
        for s in raw.unicodeScalars where !isControl(s) { kept.append(s) }
        let trimmed = String(kept).trimmingCharacters(in: .whitespacesAndNewlines)
        return String(String.UnicodeScalarView(trimmed.unicodeScalars.prefix(nicknameMax)))
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func isControl(_ s: Unicode.Scalar) -> Bool {
        s.properties.generalCategory == .control || s.value == 0x2028 || s.value == 0x2029
    }

    private static func escape(_ s: String) -> String {
        // RFC 3986 unreserved characters only, like .NET's Uri.EscapeDataString.
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
    }

    private static func twoDecimals(_ v: Double) -> String {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.numberStyle = .decimal
        f.minimumFractionDigits = 2
        f.maximumFractionDigits = 2
        f.usesGroupingSeparator = false
        return f.string(from: NSNumber(value: v)) ?? "0.00"
    }

    /// A JSON number, never a bool or a string.
    private static func number(_ v: Any?) -> Double? {
        guard let n = v as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        let d = n.doubleValue
        return d.isFinite ? d : nil
    }

    private static func at(_ o: [String: Any], _ name: String, _ index: Int) -> Double? {
        guard let arr = o[name] as? [Any], arr.count > index else { return nil }
        return number(arr[index])
    }
}

/// WMO weather-code words in sentence case, and an SF Symbol for each.
enum WeatherCodes {
    /// The words for a code; "Unknown" for anything not in the table.
    static func words(_ code: Int) -> String {
        switch code {
        case 0: "Clear"
        case 1: "Mostly clear"
        case 2: "Partly cloudy"
        case 3: "Overcast"
        case 45, 48: "Fog"
        case 51, 53, 55: "Drizzle"
        case 56, 57: "Freezing drizzle"
        case 61, 63: "Rain"
        case 65: "Heavy rain"
        case 66, 67: "Freezing rain"
        case 71, 73, 77: "Snow"
        case 75: "Heavy snow"
        case 80, 81, 82: "Showers"
        case 85, 86: "Snow showers"
        case 95, 96, 99: "Thunderstorm"
        default: "Unknown"
        }
    }

    /// The SF Symbol for a code; a plain cloud for anything not in the table.
    static func symbol(_ code: Int) -> String {
        switch code {
        case 0: "sun.max"
        case 1, 2: "cloud.sun"
        case 3: "cloud"
        case 45, 48: "cloud.fog"
        case 51, 53, 55: "cloud.drizzle"
        case 56, 57, 66, 67: "cloud.sleet"
        case 61, 63: "cloud.rain"
        case 65: "cloud.heavyrain"
        case 71, 73, 77, 85, 86: "cloud.snow"
        case 75: "snowflake"
        case 80, 81, 82: "cloud.sun.rain"
        case 95, 96, 99: "cloud.bolt.rain"
        default: "cloud"
        }
    }
}

/// When to fetch and when a reading is stale.
enum WeatherSchedule {
    /// Minimum gap between attempts, success or failure.
    static let refresh: TimeInterval = 30 * 60
    /// A reading older than this is drawn dim.
    static let staleAfter: TimeInterval = 3 * 60 * 60

    /// True when nothing was tried yet or 30 minutes have passed since the last attempt. A failure counts as an attempt.
    static func due(lastAttempt: Date?, now: Date) -> Bool {
        guard let lastAttempt else { return true }
        return now.timeIntervalSince(lastAttempt) >= refresh
    }

    /// True when the reading is more than 3 hours old.
    static func isStale(_ reading: WeatherReading, now: Date) -> Bool {
        now.timeIntervalSince(reading.fetchedAt) > staleAfter
    }
}

/// Builds the Desk weather piece's text lines.
enum WeatherLines {
    /// Heading, now, range and tomorrow lines. Tomorrow is nil unless its code, high and low are all
    /// present. The heading is the nickname when one is set ("Show it as"), else the place's own name.
    static func make(_ place: WeatherPlace, _ reading: WeatherReading, nickname: String? = nil) -> WeatherLineSet {
        var tomorrow: String?
        if let code = reading.tomorrowCode, let hi = reading.tomorrowHigh, let lo = reading.tomorrowLow {
            tomorrow = "Tomorrow: \(WeatherCodes.words(code)) \(deg(hi)) / \(deg(lo))"
        }
        let nick = nickname?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return WeatherLineSet(
            heading: nick.isEmpty ? place.name : nick,
            now: "\(WeatherCodes.words(reading.codeNow)) · \(deg(reading.tempNow))",
            range: "High \(deg(reading.todayHigh)) · Low \(deg(reading.todayLow))",
            tomorrow: tomorrow)
    }

    /// Whole degrees, half away from zero, ASCII digits and minus whatever the locale.
    private static func deg(_ t: Double) -> String {
        "\(Int(t.rounded(.toNearestOrAwayFromZero)))°"
    }
}

/// The weather piece's saved choice, in the Desk defaults suite under `desk_weather` (Windows'
/// settings.json key, the same member names): the place, the unit, and the nickname. Absent: no place,
/// and the unit follows the region. The nickname is written only when set and stays on this Mac.
struct DeskWeatherSettings: Equatable {
    static let key = "desk_weather"

    var place: WeatherPlace?
    var unit: WeatherUnit?
    var nickname = ""

    /// The unit to ask for: the saved one, else the region's.
    func unit(isMetric: Bool) -> WeatherUnit { unit ?? Weather.defaultUnit(isMetric: isMetric) }

    /// What the weather piece shows as its heading: the nickname when there is one, else the place's own name.
    func shown(_ place: WeatherPlace) -> String { nickname.isEmpty ? place.name : nickname }

    static func load(_ defaults: UserDefaults = .desk) -> DeskWeatherSettings {
        decode(defaults.dictionary(forKey: key))
    }

    func save(_ defaults: UserDefaults = .desk) {
        let encoded = encode()
        if encoded.isEmpty { defaults.removeObject(forKey: Self.key) } else { defaults.set(encoded, forKey: Self.key) }
    }

    static func decode(_ o: [String: Any]?) -> DeskWeatherSettings {
        guard let o else { return DeskWeatherSettings() }
        let unit = WeatherUnit(rawValue: (o["unit"] as? String) ?? "")
        var place: WeatherPlace?
        let name = ((o["name"] as? String) ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !name.isEmpty, let lat = (o["lat"] as? NSNumber)?.doubleValue, let lon = (o["lon"] as? NSNumber)?.doubleValue,
           lat.isFinite, lon.isFinite, abs(lat) <= 90, abs(lon) <= 180 {
            let admin = (o["admin"] as? String) ?? ""
            let country = (o["country"] as? String) ?? ""
            place = .create(name, admin: admin.isEmpty ? nil : admin, country: country.isEmpty ? nil : country,
                            lat: lat, lon: lon)
        }
        return DeskWeatherSettings(place: place, unit: unit, nickname: Weather.normalizeNickname(o["nickname"] as? String))
    }

    func encode() -> [String: Any] {
        var o: [String: Any] = [:]
        if let p = place {
            o["name"] = p.name
            if let a = p.admin { o["admin"] = a }
            if let c = p.country { o["country"] = c }
            o["lat"] = WeatherPlace.round2(p.lat)
            o["lon"] = WeatherPlace.round2(p.lon)
        }
        if let u = unit { o["unit"] = u.rawValue }
        // Written only when set, so a suite from before nicknames stays as it was.
        let nick = Weather.normalizeNickname(nickname)
        if !nick.isEmpty { o["nickname"] = nick }
        return o
    }
}
