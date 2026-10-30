import SwiftUI
import Observation

/// Settings, Weather (spec section 13): type a city, Look up sends that name (and only that) to
/// Open-Meteo's place search, pick one of the places found, choose the unit. The place's coordinates
/// are saved rounded to about 1 km. Clear forgets the place. "Show it as" is a nickname the Desk shows
/// instead of the place's name: kept on this Mac only, never sent, and the forecast still uses the
/// picked place.
@MainActor
@Observable
final class WeatherPageModel {
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let lookUpPlaces: (String) async throws -> [WeatherPlace]
    @ObservationIgnored private let isMetric: () -> Bool
    @ObservationIgnored private var lookUpTask: Task<Void, Never>?
    /// Called after every saved change, so the Desk follows.
    @ObservationIgnored var changed: (() -> Void)?

    private(set) var saved: DeskWeatherSettings
    var city = ""
    private(set) var note = ""
    private(set) var results: [WeatherPlace] = []

    init(defaults: UserDefaults = .desk,
         isMetric: @escaping () -> Bool = { Weather.regionIsMetric },
         lookUp: @escaping (String) async throws -> [WeatherPlace] = { try await WeatherService.lookUp($0) }) {
        self.defaults = defaults
        self.isMetric = isMetric
        self.lookUpPlaces = lookUp
        saved = DeskWeatherSettings.load(defaults)
    }

    static let nicknameHint = "Shown on the Desk instead of the city's name, up to \(Weather.nicknameMax) characters. It stays on this Mac and is never sent anywhere; the forecast still uses the city you picked."

    /// "Fort Worth, Texas, United States", or "No city set".
    var placeText: String { saved.place?.label ?? "No city set" }
    var hasPlace: Bool { saved.place != nil }
    var unit: WeatherUnit { saved.unit(isMetric: isMetric()) }
    var nickname: String { saved.nickname }

    /// Look up: one request carrying the typed name. A newer look-up replaces an older one.
    func lookUp() {
        lookUpTask?.cancel()
        results = []
        let typed = city
        guard !typed.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { note = "Type a city first."; return }
        note = "Looking up…"
        lookUpTask = Task { [weak self, lookUpPlaces] in
            do {
                let places = try await lookUpPlaces(typed)
                guard !Task.isCancelled, let self else { return }
                results = places
                note = places.isEmpty ? "No place by that name. Try the city with its country." : "Pick your place:"
            } catch {
                guard !Task.isCancelled, let self else { return }
                note = "Open-Meteo didn't answer. Check the connection and try again."
            }
        }
    }

    /// Awaits the look-up in flight (tests).
    func lookUpFinished() async { await lookUpTask?.value }

    func pick(_ place: WeatherPlace) {
        var next = saved
        next.place = .create(place.name, admin: place.admin, country: place.country, lat: place.lat, lon: place.lon)
        commit(next)
        results = []
        note = ""
        city = ""
    }

    func clear() {
        var next = saved
        next.place = nil
        commit(next)
    }

    func setUnit(_ unit: WeatherUnit) {
        var next = saved
        next.unit = unit
        commit(next)
    }

    /// Up to 24 characters, trimmed; empty goes back to the place's name and removes the key's member.
    func setNickname(_ raw: String) {
        let nick = Weather.normalizeNickname(raw)
        // A design may have set the nickname since the page opened: compare with what is saved now.
        var next = DeskWeatherSettings.load(defaults)
        guard nick != next.nickname else { saved = next; return }
        next.nickname = nick
        commit(next)
    }

    private func commit(_ next: DeskWeatherSettings) {
        // Keep a nickname set elsewhere since the page opened, unless this change is the nickname.
        var next = next
        let stored = DeskWeatherSettings.load(defaults)
        if next.nickname == saved.nickname, stored.nickname != saved.nickname { next.nickname = stored.nickname }
        saved = next
        next.save(defaults)
        changed?()
    }
}

struct WeatherSection: View {
    @State private var model = WeatherPageModel()
    @State private var nickname = ""

    var body: some View {
        Form {
            NeedsDeskRow()
            WeatherPlaceSection(model: model)
            WeatherUnitSection(model: model)
            WeatherNicknameSection(model: model, nickname: $nickname)
            Section {
                Text("Open-Meteo's forecast shows on the Desk while Weather is placed there and the Desk is on. Where it sits is Settings, Desk, Pieces.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack {
                    SettingsLinkButton(.deskLayout, anchor: SettingsAnchor.pieces)
                    Spacer()
                }
                Link(Weather.credit, destination: Weather.creditURL).font(.caption)
                    .settingsAnchor(SettingsAnchor.weatherCredit)
                Link("Licence: CC BY 4.0", destination: Weather.licenceURL).font(.caption)
            }
        }
        .formStyle(.grouped)
        .onAppear {
            nickname = model.nickname
            // A new place, unit or nickname reaches the Desk at once: the service re-asks for a new place or unit.
            model.changed = { DeskController.shared.model.syncWeather() }
        }
    }
}

private struct WeatherPlaceSection: View {
    @Bindable var model: WeatherPageModel

    var body: some View {
        Section {
            LabeledContent("Place", value: model.placeText)
            HStack {
                TextField("City", text: $model.city)
                    .onSubmit { model.lookUp() }
                Button(SettingsNames.weatherLookUp) { model.lookUp() }
                if model.hasPlace { Button("Clear") { model.clear() } }
            }
            .settingsAnchor(SettingsAnchor.weatherPlace)
            if !model.note.isEmpty { Text(model.note).font(.caption).foregroundStyle(.secondary) }
            ForEach(Array(model.results.enumerated()), id: \.offset) { _, place in
                Button(place.label) { model.pick(place) }
                    .buttonStyle(.link)
            }
            Text("Look up sends the name you typed, and only that, to Open-Meteo's place search. The forecast then asks for the place's coordinates, rounded to about 1 km, every 30 minutes while it shows. No Location Services, no account.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

private struct WeatherUnitSection: View {
    let model: WeatherPageModel

    var body: some View {
        Section {
            Picker("Temperature", selection: Binding(get: { model.unit }, set: { model.setUnit($0) })) {
                Text("Fahrenheit (°F)").tag(WeatherUnit.fahrenheit)
                Text("Celsius (°C)").tag(WeatherUnit.celsius)
            }
            .pickerStyle(.segmented)
        }
    }
}

private struct WeatherNicknameSection: View {
    let model: WeatherPageModel
    @Binding var nickname: String

    var body: some View {
        Section {
            TextField(SettingsNames.weatherShowAs, text: $nickname)
                .onChange(of: nickname) { _, value in
                    let kept = Weather.normalizeNickname(value)
                    // Typing past 24 characters stops at 24; spaces stay while typing.
                    if value.unicodeScalars.count > Weather.nicknameMax { nickname = kept }
                    model.setNickname(value)
                }
            Text(WeatherPageModel.nicknameHint)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
