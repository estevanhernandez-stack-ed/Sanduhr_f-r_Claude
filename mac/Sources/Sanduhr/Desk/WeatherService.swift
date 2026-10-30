import Foundation

/// The weather piece's data (spec section 13), from Open-Meteo: no key, no account, no Location
/// Services. Only the place's coordinates (rounded to about 1 km) go out for a forecast, and only the
/// typed city name for a look-up; no cookie, id, machine name or version (the user agent is just
/// "Sanduhr"), and never the nickname. It fetches only while `sync(running:placed:)` says the Desk
/// shows and the weather piece is placed: once, then at most every 30 minutes, a failure waiting as
/// long as a success. No timer exists otherwise. The reading stays in memory; nothing about the
/// weather is written anywhere.
@MainActor
final class WeatherService {
    /// Starts a repeating call and returns what cancels it (the same shape as `TelemetrySampler`).
    typealias Schedule = (_ interval: TimeInterval, _ fire: @escaping @MainActor () -> Void) -> () -> Void

    /// A repeating main-run-loop timer.
    nonisolated static func liveSchedule(_ interval: TimeInterval, _ fire: @escaping @MainActor () -> Void) -> () -> Void {
        let timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { _ in
            MainActor.assumeIsolated { fire() }
        }
        return { timer.invalidate() }
    }

    private let session: URLSession
    private let settings: () -> DeskWeatherSettings
    private let isMetric: () -> Bool
    private let schedule: Schedule
    private let now: () -> Date
    private var cancel: (() -> Void)?
    private var lastAttempt: Date?
    private var fetched: (place: WeatherPlace, unit: WeatherUnit)?
    private var busy = false
    /// The fetch in flight, for tests to await.
    private(set) var pending: Task<Void, Never>?

    /// The last good reading for the current place, nil before the first and after `stop`.
    private(set) var latest: WeatherReading?
    /// The place the reading is for.
    private(set) var place: WeatherPlace?
    /// Called on the main thread when `latest` changes.
    var onUpdate: (() -> Void)?

    var isRunning: Bool { cancel != nil }

    /// The layout places the weather piece somewhere (the layout word is `weather`, as on Windows).
    static func isPlaced(layout: String) -> Bool { DeskArrangement(layout).placement("weather") != nil }

    init(session: URLSession = WeatherService.makeSession(),
         settings: @escaping () -> DeskWeatherSettings = { DeskWeatherSettings.load() },
         isMetric: @escaping () -> Bool = { Weather.regionIsMetric },
         schedule: @escaping Schedule = WeatherService.liveSchedule,
         now: @escaping () -> Date = Date.init) {
        self.session = session
        self.settings = settings
        self.isMetric = isMetric
        self.schedule = schedule
        self.now = now
    }

    /// An ephemeral session: no cookies, no cache, no credentials stored, no redirects followed, the
    /// user agent "Sanduhr". `protocolClasses` lets tests stub the network.
    nonisolated static func makeSession(protocolClasses: [AnyClass]? = nil) -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil
        config.httpShouldSetCookies = false
        config.httpCookieAcceptPolicy = .never
        config.urlCache = nil
        config.urlCredentialStorage = nil
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.timeoutIntervalForRequest = 15
        config.waitsForConnectivity = false
        config.httpAdditionalHeaders = ["User-Agent": Weather.userAgent]
        if let protocolClasses { config.protocolClasses = protocolClasses }
        return URLSession(configuration: config, delegate: NoRedirects(), delegateQueue: nil)
    }

    /// The Desk's rule in one call: the service runs while the Desk shows and the weather piece is
    /// placed, and not at all otherwise. Call it again when either changes, and when the place or the
    /// unit changes in Settings (a new place or unit drops the old reading and asks at once).
    func sync(running: Bool, placed: Bool) {
        let wants = running && placed && settings().place != nil
        guard wants else { stop(); return }
        let current = settings()
        let unit = current.unit(isMetric: isMetric())
        if let place = current.place, fetched?.place != place || fetched?.unit != unit {
            fetched = (place, unit)
            self.place = place
            if latest != nil { latest = nil; onUpdate?() }
            lastAttempt = nil
        }
        if cancel == nil {
            cancel = schedule(WeatherSchedule.refresh) { [weak self] in self?.fetchSoon() }
        }
        fetchSoon()
    }

    /// Stops the timer and forgets the reading; a Desk shown again asks at once.
    func stop() {
        cancel?()
        cancel = nil
        pending?.cancel()
        pending = nil
        busy = false
        lastAttempt = nil
        fetched = nil
        place = nil
        if latest != nil { latest = nil; onUpdate?() }
    }

    private func fetchSoon() {
        guard !busy, fetched != nil, WeatherSchedule.due(lastAttempt: lastAttempt, now: now()) else { return }
        pending = Task { [weak self] in await self?.fetch() }
    }

    private func fetch() async {
        guard let target = fetched else { return }
        busy = true
        lastAttempt = now()
        defer { busy = false }
        do {
            let data = try await Self.get(Weather.forecastURL(target.place, unit: target.unit), session: session)
            // A stop, or a new place or unit, while the answer was out: drop it.
            guard !Task.isCancelled, let current = fetched, current.place == target.place, current.unit == target.unit,
                  let reading = Weather.parseForecast(data, fetchedAt: now()) else { return }
            latest = reading
            onUpdate?()
        } catch {
            // Keep the last reading; the next attempt waits the full 30 minutes. Never log the place.
        }
    }

    /// Settings, Weather's Look up: the places Open-Meteo's geocoding finds for a city name (one
    /// request, the name only). Empty for an empty name or no match; throws on a network failure so
    /// the page can say so.
    nonisolated static func lookUp(_ city: String, session: URLSession = makeSession()) async throws -> [WeatherPlace] {
        guard let url = Weather.geocodeURL(city) else { return [] }
        return Weather.parsePlaces(try await get(url, session: session))
    }

    /// One GET with the user agent set on the request itself, no cookies, a 2xx answer or a throw.
    nonisolated private static func get(_ url: URL, session: URLSession) async throws -> Data {
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        request.httpShouldHandleCookies = false
        request.setValue(Weather.userAgent, forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw URLError(.badServerResponse)
        }
        return data
    }
}

/// Refuses every redirect, as Windows' handler does: an answer from elsewhere is not an answer.
private final class NoRedirects: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}
