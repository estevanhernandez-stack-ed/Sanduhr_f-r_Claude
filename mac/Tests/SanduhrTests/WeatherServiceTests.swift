import Foundation
import Testing
@testable import Sanduhr

/// Answers every request from a closure and records it, so no test touches the network.
final class WeatherStub: URLProtocol, @unchecked Sendable {
    nonisolated(unsafe) static var handler: ((URLRequest) -> (Int, Data))?
    nonisolated(unsafe) static var seen: [URLRequest] = []
    private static let lock = NSLock()

    static func reset(_ h: ((URLRequest) -> (Int, Data))?) {
        lock.lock(); defer { lock.unlock() }
        handler = h
        seen = []
    }
    static var requests: [URLRequest] { lock.lock(); defer { lock.unlock() }; return seen }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.lock.lock()
        Self.seen.append(request)
        let h = Self.handler
        Self.lock.unlock()
        let (status, body) = h?(request) ?? (500, Data())
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: nil)!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

/// The weather service (2.38.0 item 2): when it fetches, what it sends, and that the nickname never leaves.
@Suite("Weather: service", .serialized)
@MainActor
struct WeatherServiceTests {
    nonisolated private static let place = WeatherPlace.create("Fort Worth", admin: "Texas", country: "United States", lat: 32.7254, lon: -97.3208)

    /// A hand-turned clock and timer.
    final class Rig {
        var now = WeatherCoreTests.now
        var fire: (@MainActor () -> Void)?
        var interval: TimeInterval?
        var started = 0
        var cancelled = 0
        var settings: DeskWeatherSettings
        init(settings: DeskWeatherSettings) { self.settings = settings }
    }

    private func make(_ settings: DeskWeatherSettings = DeskWeatherSettings(place: place, unit: .fahrenheit),
                      answer: @escaping (URLRequest) -> (Int, Data) = { _ in (200, Data(WeatherCoreTests.forecastJSON.utf8)) })
        -> (WeatherService, Rig) {
        WeatherStub.reset(answer)
        let rig = Rig(settings: settings)
        let service = WeatherService(
            session: WeatherService.makeSession(protocolClasses: [WeatherStub.self]),
            settings: { rig.settings }, isMetric: { false },
            schedule: { interval, fire in
                rig.interval = interval; rig.fire = fire; rig.started += 1
                return { rig.cancelled += 1; rig.fire = nil }
            },
            now: { rig.now })
        return (service, rig)
    }

    @Test func placedAndShowingFetchesOnceAtOnceAndKeepsTheReading() async throws {
        let (service, rig) = make()
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.count == 1)
        #expect(WeatherStub.requests[0].url == Weather.forecastURL(Self.place, unit: .fahrenheit))
        #expect(service.latest?.tempNow == 72.4)
        #expect(service.place == Self.place)
        #expect(rig.interval == WeatherSchedule.refresh)
        // A second sync inside the 30 minutes does not ask again.
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.count == 1)
        #expect(rig.started == 1)
    }

    @Test func nothingIsFetchedWhileThePieceIsNotPlaced() async {
        let (service, rig) = make()
        service.sync(running: true, placed: false)
        await service.pending?.value
        #expect(WeatherStub.requests.isEmpty)
        #expect(!service.isRunning && rig.started == 0)
    }

    @Test func nothingIsFetchedWhileTheDeskIsHidden() async {
        let (service, rig) = make()
        service.sync(running: false, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.isEmpty)
        #expect(!service.isRunning && rig.started == 0)
    }

    @Test func nothingIsFetchedWithoutAPlace() async {
        let (service, rig) = make(DeskWeatherSettings())
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.isEmpty && rig.started == 0)
    }

    @Test func hidingTheDeskOrUnplacingThePieceStopsTheTimerAndDropsTheReading() async {
        let (service, rig) = make()
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(service.latest != nil)
        service.sync(running: false, placed: true)
        #expect(rig.cancelled == 1 && !service.isRunning && service.latest == nil)
        // Back again: asks at once, from a fresh start.
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.count == 2 && rig.started == 2)
        service.sync(running: true, placed: false)
        #expect(rig.cancelled == 2 && service.latest == nil)
        service.sync(running: true, placed: false)
        #expect(rig.cancelled == 2)
    }

    @Test func theTimerAsksAgainOnlyWhenThirtyMinutesPassed() async {
        let (service, rig) = make()
        service.sync(running: true, placed: true)
        await service.pending?.value
        rig.now = rig.now.addingTimeInterval(29 * 60)
        rig.fire?()
        await service.pending?.value
        #expect(WeatherStub.requests.count == 1)
        rig.now = rig.now.addingTimeInterval(60)
        rig.fire?()
        await service.pending?.value
        #expect(WeatherStub.requests.count == 2)
    }

    @Test func aFailureKeepsTheReadingAndWaitsTheFullThirtyMinutes() async {
        var fail = false
        let (service, rig) = make(answer: { _ in fail ? (503, Data()) : (200, Data(WeatherCoreTests.forecastJSON.utf8)) })
        service.sync(running: true, placed: true)
        await service.pending?.value
        fail = true
        rig.now = rig.now.addingTimeInterval(30 * 60)
        rig.fire?()
        await service.pending?.value
        #expect(WeatherStub.requests.count == 2)
        #expect(service.latest?.tempNow == 72.4)
        rig.now = rig.now.addingTimeInterval(10 * 60)
        rig.fire?()
        await service.pending?.value
        #expect(WeatherStub.requests.count == 2)
    }

    @Test func aNewPlaceOrUnitDropsTheOldReadingAndAsksAtOnce() async {
        let (service, rig) = make()
        service.sync(running: true, placed: true)
        await service.pending?.value
        rig.settings.unit = .celsius
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.count == 2)
        #expect(WeatherStub.requests[1].url?.absoluteString.contains("temperature_unit=celsius") == true)
        rig.settings.place = .create("Oslo", admin: nil, country: "Norway", lat: 59.91, lon: 10.75)
        service.sync(running: true, placed: true)
        await service.pending?.value
        #expect(WeatherStub.requests.count == 3)
        #expect(WeatherStub.requests[2].url?.absoluteString.contains("latitude=59.91&longitude=10.75") == true)
        #expect(rig.started == 1)
    }

    /// The `weather` word joins `DeskArrangement.pieces` with the piece itself (item 3), and the
    /// placed case is pinned there; until then a layout never places it.
    @Test func aLayoutWithoutWeatherDoesNotPlaceIt() {
        #expect(!WeatherService.isPlaced(layout: ""))
        #expect(!WeatherService.isPlaced(layout: "message:tl telemetry:tr"))
    }

    // MARK: What leaves

    /// Whether `needle` appears anywhere in the request: URL (as sent and decoded), headers, body.
    private static func carries(_ r: URLRequest, _ needle: String) -> Bool {
        var haystack = [r.url?.absoluteString ?? "", r.url?.absoluteString.removingPercentEncoding ?? ""]
        for (k, v) in r.allHTTPHeaderFields ?? [:] { haystack += [k, v] }
        if let body = r.httpBody { haystack.append(String(decoding: body, as: UTF8.self)) }
        let fold = needle.lowercased()
        return haystack.contains { $0.lowercased().contains(fold) }
    }

    @Test func theNicknameNeverReachesEitherRequest() async throws {
        let secret = "Zebra Cabin 77"
        let (service, _) = make(DeskWeatherSettings(place: Self.place, unit: .fahrenheit, nickname: secret))
        service.sync(running: true, placed: true)
        await service.pending?.value
        let forecast = try #require(WeatherStub.requests.first)
        #expect(WeatherStub.requests.count == 1)
        #expect(!Self.carries(forecast, secret) && !Self.carries(forecast, "Zebra"))

        // Look up with the nickname saved: the typed name is the only thing sent.
        WeatherStub.reset { _ in (200, Data(WeatherCoreTests.geocodeJSON.utf8)) }
        let found = try await WeatherService.lookUp("Fort Worth", session: WeatherService.makeSession(protocolClasses: [WeatherStub.self]))
        #expect(found.count == 2)
        let lookup = try #require(WeatherStub.requests.first)
        #expect(WeatherStub.requests.count == 1)
        #expect(!Self.carries(lookup, secret) && !Self.carries(lookup, "Zebra"))
    }

    @Test func eachRequestCarriesOnlyItsOwnInputsAndTheUserAgent() async throws {
        let (service, _) = make(DeskWeatherSettings(place: Self.place, unit: .fahrenheit, nickname: "Home"))
        service.sync(running: true, placed: true)
        await service.pending?.value
        let forecast = try #require(WeatherStub.requests.first)
        #expect(forecast.url?.host == "api.open-meteo.com")
        #expect(!Self.carries(forecast, "Fort") && !Self.carries(forecast, "Texas"))

        WeatherStub.reset { _ in (200, Data(WeatherCoreTests.geocodeJSON.utf8)) }
        _ = try await WeatherService.lookUp("Fort Worth", session: WeatherService.makeSession(protocolClasses: [WeatherStub.self]))
        let lookup = try #require(WeatherStub.requests.first)
        #expect(lookup.url?.host == "geocoding-api.open-meteo.com")
        #expect(lookup.url?.absoluteString == "https://geocoding-api.open-meteo.com/v1/search?name=Fort%20Worth&count=5&language=en&format=json")
        // The look-up never carries the place's coordinates either.
        #expect(!Self.carries(lookup, "32.73"))

        for request in [forecast, lookup] {
            #expect(request.value(forHTTPHeaderField: "User-Agent") == "Sanduhr")
            #expect(request.value(forHTTPHeaderField: "Cookie") == nil)
            #expect(request.value(forHTTPHeaderField: "Authorization") == nil)
            #expect(request.httpMethod == "GET" && request.httpBody == nil)
            #expect(request.httpShouldHandleCookies == false)
            #expect(request.url?.scheme == "https")
        }
    }

    @Test func aLookUpFailureThrowsAndAnEmptyNameSendsNothing() async throws {
        WeatherStub.reset { _ in (503, Data()) }
        let session = WeatherService.makeSession(protocolClasses: [WeatherStub.self])
        await #expect(throws: (any Error).self) { _ = try await WeatherService.lookUp("Oslo", session: session) }
        WeatherStub.reset { _ in (200, Data()) }
        let none = try await WeatherService.lookUp("   ", session: session)
        #expect(none.isEmpty && WeatherStub.requests.isEmpty)
    }

    // MARK: The Settings page's model

    private func page(_ nickname: String = "") -> (WeatherPageModel, UserDefaults) {
        let d = UserDefaults(suiteName: "sanduhr.tests.weatherpage." + UUID().uuidString.prefix(8))!
        let model = WeatherPageModel(defaults: d, isMetric: { false }, lookUp: { city in
            try await WeatherService.lookUp(city, session: WeatherService.makeSession(protocolClasses: [WeatherStub.self]))
        })
        return (model, d)
    }

    @Test func lookUpListsThePlacesAndPickingOneSavesItRounded() async {
        WeatherStub.reset { _ in (200, Data(WeatherCoreTests.geocodeJSON.utf8)) }
        let (model, d) = page()
        var changes = 0
        model.changed = { changes += 1 }
        model.city = "Fort Worth"
        model.lookUp()
        await model.lookUpFinished()
        #expect(model.results.count == 2 && model.note == "Pick your place:")
        model.pick(model.results[0])
        #expect(model.placeText == "Fort Worth, Texas, United States" && model.hasPlace)
        #expect(model.results.isEmpty && model.city == "" && model.note == "")
        #expect(changes == 1)
        let saved = d.dictionary(forKey: DeskWeatherSettings.key)
        #expect(saved?["lat"] as? Double == 32.73 && saved?["lon"] as? Double == -97.32)
        #expect(saved?["nickname"] == nil)
        model.clear()
        #expect(model.placeText == "No city set" && !model.hasPlace)
    }

    @Test func lookUpSaysSoOnNoMatchAnEmptyFieldAndAFailure() async {
        let (model, _) = page()
        model.lookUp()
        #expect(model.note == "Type a city first.")
        WeatherStub.reset { _ in (200, Data("{}".utf8)) }
        model.city = "Nowhereville"
        model.lookUp()
        await model.lookUpFinished()
        #expect(model.note == "No place by that name. Try the city with its country.")
        WeatherStub.reset { _ in (500, Data()) }
        model.lookUp()
        await model.lookUpFinished()
        #expect(model.note == "Open-Meteo didn't answer. Check the connection and try again.")
        #expect(model.results.isEmpty)
    }

    @Test func theNicknameIsKeptTrimmedCappedAndWrittenOnlyWhenSet() {
        let (model, d) = page()
        model.setNickname("  Home  ")
        #expect(model.nickname == "Home")
        #expect(d.dictionary(forKey: DeskWeatherSettings.key)?["nickname"] as? String == "Home")
        model.setNickname(String(repeating: "x", count: 60))
        #expect(model.nickname.count == 24)
        model.setNickname("   ")
        #expect(model.nickname == "")
        // Nothing but the nickname was set, so the record is gone entirely.
        #expect(d.object(forKey: DeskWeatherSettings.key) == nil)
    }

    @Test func aNicknameSetElsewhereSurvivesAPlaceChange() {
        let (model, d) = page()
        DeskWeatherSettings(nickname: "Set by a design").save(d)
        model.setUnit(.celsius)
        #expect(DeskWeatherSettings.load(d).nickname == "Set by a design")
        #expect(DeskWeatherSettings.load(d).unit == .celsius)
    }
}
