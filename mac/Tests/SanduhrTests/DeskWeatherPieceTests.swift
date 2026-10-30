import Foundation
import Testing
@testable import Sanduhr

/// The weather piece on the Desk and in designs (spec 13, spec 8): the layout word and its default anchor, the
/// nickname through a design's review, Apply and Put my Desk back, desk-state.json, and the model's rule for the
/// forecast service. No network: the service is built over a session nothing is sent on, and no place is set up.
@MainActor
@Suite("Desk weather piece")
struct DeskWeatherPieceTests {
    private func suite() -> UserDefaults {
        let name = "sanduhr.tests.weatherpiece.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: name)!
        d.removePersistentDomain(forName: name)
        return d
    }

    private let place = WeatherPlace.create("Mapleton", admin: nil, country: nil, lat: 10, lon: 10)

    // MARK: The layout word

    @Test func weatherIsALayoutWordThatDefaultsToTheMiddleRight() {
        #expect(DeskArrangement.known.contains("weather"))
        #expect(DeskArrangement.defaultAnchor("weather") == .mr)
        var a = DeskArrangement(DeskLayout.standard)
        a.show("weather")
        #expect(a.placement("weather")?.anchor == .mr)
        #expect(DeskArrangement("weather:tr:1.2").placement("weather")?.scale == 1.2)
        // Settings' order: after the system piece, before the watchers.
        let known = DeskArrangement.known
        #expect(known.firstIndex(of: "telemetry")! < known.firstIndex(of: "weather")!)
        #expect(known.firstIndex(of: "weather")! < known.firstIndex(of: "watchers")!)
    }

    @Test func theServiceIsPlacedOnlyForALayoutThatPlacesTheWeather() {
        #expect(WeatherService.isPlaced(layout: "message:tl weather:mr"))
        #expect(!WeatherService.isPlaced(layout: DeskLayout.standard))
        #expect(!WeatherService.isPlaced(layout: "telemetry:tr"))
    }

    // MARK: The model's rule

    @Test func theModelReadsThePlaceAndNicknameAndStaysQuietWithoutAPlace() {
        let m = DeskModel()
        var saved = DeskWeatherSettings()
        saved.nickname = "Funky Town"
        m.applyWeather(running: true, layout: "weather:mr", settings: saved)
        #expect(m.weather.place == nil && m.weather.nickname == "Funky Town")
        #expect(!m.weatherService.isRunning, "with no place chosen nothing is asked for")

        m.applyWeather(running: false, layout: "weather:mr", settings: saved)
        #expect(!m.weatherService.isRunning)
    }

    @Test func demoModeShowsAMadeUpPlaceAndNeverStartsTheService() {
        let m = DeskModel()
        m.setDemo(true, now: Date(timeIntervalSince1970: 1_700_000_000))
        #expect(m.weather.place?.name == "Mapleton")
        #expect(m.weather.reading != nil && !m.weather.stale)
        #expect(!m.weatherService.isRunning)
        m.setDemo(false)
        #expect(m.weather.reading == nil)
    }

    // MARK: The nickname through a design

    @Test func aNicknameIsMergedWrittenAndKeepsThePlace() {
        let d = suite()
        var weather = DeskWeatherSettings()
        weather.place = place
        weather.unit = .celsius
        weather.save(d)

        let current = DeskDesignLook.read(d)
        var design = DeskDesign()
        design.weatherNickname = "Funky Town"
        let next = DeskDesignApply.merged(current, design)
        #expect(next.weatherNickname == "Funky Town")
        #expect(DeskDesignApply.changes(from: current, to: next) == ["Weather: shown as \u{201C}Funky Town\u{201D}"])

        DeskDesignApply.write(next, over: current, to: d)
        let saved = DeskWeatherSettings.load(d)
        #expect(saved.nickname == "Funky Town")
        #expect(saved.place == place && saved.unit == .celsius)

        // "" goes back to the place's own name and removes the member.
        var clear = DeskDesign()
        clear.weatherNickname = ""
        let cleared = DeskDesignApply.merged(DeskDesignLook.read(d), clear)
        #expect(DeskDesignApply.changes(from: DeskDesignLook.read(d), to: cleared) == ["Weather: shows the place's own name"])
        DeskDesignApply.write(cleared, over: DeskDesignLook.read(d), to: d)
        let after = DeskWeatherSettings.load(d)
        #expect(after.nickname == "" && after.place == place)
        #expect((d.dictionary(forKey: DeskWeatherSettings.key)?["nickname"]) == nil)
    }

    @Test func aDesignThatLeavesTheNicknameAloneWritesNothingForIt() {
        let d = suite()
        var design = DeskDesign()
        design.plate = true
        let current = DeskDesignLook.read(d)
        let next = DeskDesignApply.merged(current, design)
        DeskDesignApply.write(next, over: current, to: d)
        #expect(d.object(forKey: DeskWeatherSettings.key) == nil)
        #expect(!DeskDesignApply.changedKeys(next, over: current).contains(DeskWeatherSettings.key))
    }

    @Test func putMyDeskBackRestoresTheNickname() {
        let d = suite()
        var weather = DeskWeatherSettings()
        weather.place = place
        weather.nickname = "Old Name"
        weather.save(d)
        let current = DeskDesignLook.read(d)
        var design = DeskDesign()
        design.weatherNickname = "New Name"
        let next = DeskDesignApply.merged(current, design)
        DeskDesignApply.recordBefore(next, over: current, name: "Test", now: Date(), in: d)
        DeskDesignApply.write(next, over: current, to: d)
        #expect(DeskWeatherSettings.load(d).nickname == "New Name")
        DeskDesignApply.Undo.read(d)?.putBack(in: d)
        let back = DeskWeatherSettings.load(d)
        #expect(back.nickname == "Old Name" && back.place == place)
    }

    @Test func theReviewPreviewsTheNicknameFromMemoryAndApplyWritesIt() throws {
        let w = DeskDesignReviewTests.World()
        var weather = DeskWeatherSettings()
        weather.place = place
        weather.save(w.defaults)
        w.send("w1", #"{"name":"Fold","layout":{"weather":{"anchor":"tr"}},"weather":{"nickname":"Funky Town"}}"#)
        #expect(try w.status() == "previewing")
        let m = try #require(w.handoff.model)
        #expect(m.changes.contains("Weather: shown as \u{201C}Funky Town\u{201D}"))
        #expect(m.changes.contains("Weather: shown at top right"))
        w.handoff.previewOnDesk()
        #expect(w.host.shown?.weatherNickname == "Funky Town")
        #expect(DeskWeatherSettings.load(w.defaults).nickname == "", "the review wrote nothing")
        w.handoff.apply()
        #expect(DeskWeatherSettings.load(w.defaults).nickname == "Funky Town")
        #expect(DeskWeatherSettings.load(w.defaults).place == place)
        #expect(DeskArrangement(w.layout).placement("weather")?.anchor == .tr)
    }

    @Test func aTooLongNicknameIsRefusedByTheReview() throws {
        let w = DeskDesignReviewTests.World()
        w.send("w2", #"{"weather":{"nickname":"abcdefghijklmnopqrstuvwxy"}}"#)
        #expect(try w.status() == "rejected")
        #expect(w.handoff.pending == nil)
    }

    // MARK: desk-state.json

    @Test func theStateReportsTheNicknameAndThePieces() throws {
        let d = suite()
        d.set("weather:mr telemetry:tr:1:0:ring", forKey: "layout")
        var weather = DeskWeatherSettings()
        weather.place = place
        weather.nickname = "Funky Town"
        weather.save(d)
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.38.0", fontAvailable: { _ in true })
        let s = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let design = try #require(s["design"] as? [String: Any])
        #expect((design["weather"] as? [String: Any])?["nickname"] as? String == "Funky Town")
        let layout = try #require(design["layout"] as? [String: [String: Any]])
        #expect(layout["weather"]?["anchor"] as? String == "mr")
        #expect(layout["weather"]?["style"] == nil)
        #expect(layout["telemetry"]?["style"] as? String == "ring")
        #expect((s["shown"] as? [String])?.contains("weather") == true)
        // The place and its coordinates never leave the app through this file.
        #expect(!String(decoding: data, as: UTF8.self).contains("Mapleton"))
    }
}
