import SwiftUI

/// What the weather piece draws from: the chosen place (nil: none set up), the name it shows instead of
/// the place's own, the newest reading and whether that reading is stale (older than 3 hours). In memory
/// only; the place and nickname are read from the Desk suite, the reading is never stored.
struct DeskWeatherShown: Equatable {
    var place: WeatherPlace?
    var nickname = ""
    var reading: WeatherReading?
    var stale = false
}

/// Made-up weather for demo mode: a made-up place and a plain reading, so screenshots show no real city.
enum WeatherDemo {
    static func shown(now: Date) -> DeskWeatherShown {
        let place = WeatherPlace.create("Mapleton", admin: nil, country: nil, lat: 10, lon: 10)
        let reading = WeatherReading(tempNow: 68, codeNow: 2, todayHigh: 74, todayLow: 55,
                                     tomorrowCode: 61, tomorrowHigh: 66, tomorrowLow: 52, fetchedAt: now)
        return DeskWeatherShown(place: place, nickname: "", reading: reading, stale: false)
    }
}

/// The weather piece as the Desk draws it (layout word `weather`): the place (or the nickname), now with
/// a symbol, today's range, tomorrow, and the Open-Meteo credit its licence asks for. A reading older
/// than 3 hours is drawn dim. With no place chosen it says where to set one. It takes no clicks and
/// nothing animates.
struct DeskWeatherPiece: View {
    let shown: DeskWeatherShown
    let font: String
    /// The Desk's clock size times the piece's size; the sizes below are shares of it.
    let timeSize: Double
    let alignment: HorizontalAlignment

    var body: some View {
        let size = timeSize * 0.17
        VStack(alignment: alignment, spacing: size * 0.2) {
            if let place = shown.place {
                if let reading = shown.reading {
                    lines(place, reading, size: size)
                } else {
                    Text(heading(place)).font(.custom(BundledFonts.face(font, bold: true), size: size * 1.6))
                    Text("…").font(.custom(font, size: size)).opacity(0.6)
                }
            } else {
                Text(Weather.noPlaceLine).font(.custom(font, size: size)).opacity(0.75)
            }
            Text(Weather.credit)
                .font(.custom(font, size: max(9, size * 0.55)))
                .opacity(0.55)
                .padding(.top, size * 0.3)
        }
        .multilineTextAlignment(textAlignment)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Weather")
    }

    private var textAlignment: TextAlignment {
        alignment == .trailing ? .trailing : alignment == .center ? .center : .leading
    }

    private func heading(_ place: WeatherPlace) -> String {
        shown.nickname.isEmpty ? place.name : shown.nickname
    }

    @ViewBuilder
    private func lines(_ place: WeatherPlace, _ reading: WeatherReading, size: Double) -> some View {
        let set = WeatherLines.make(place, reading, nickname: shown.nickname)
        let dim = shown.stale ? 0.5 : 1.0
        Group {
            Text(set.heading).font(.custom(BundledFonts.face(font, bold: true), size: size * 1.6))
            HStack(spacing: size * 0.3) {
                Image(systemName: WeatherCodes.symbol(reading.codeNow)).imageScale(.small)
                Text(set.now)
            }
            .font(.custom(font, size: size * 1.15)).opacity(0.92)
            Text(set.range).font(.custom(font, size: size * 0.9)).opacity(0.8)
            if let tomorrow = set.tomorrow {
                Text(tomorrow).font(.custom(font, size: size * 0.9)).opacity(0.7)
            }
        }
        .opacity(dim)
    }
}
