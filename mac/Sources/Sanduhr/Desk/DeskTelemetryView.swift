import SwiftUI

/// The system piece as the Desk draws it (layout word `telemetry`): CPU, RAM and Disk, in the meter
/// styles. Bars are the Claude limits' own rows, the other styles their gauges, so it reads as one
/// family. It is not usage: no pace mark, no warning, no reset line, and it takes no clicks. A reading
/// changes in place; nothing animates.
struct DeskTelemetryPiece: View {
    /// The newest reading; nil (not sampled yet, or a preview) draws zeros.
    let sample: TelemetrySample?
    let style: DeskMeterStyle
    let ink: String
    let font: String
    /// The Desk's clock size times the piece's size: the sizes below are shares of it, as the meters' are.
    let timeSize: Double
    let alignment: HorizontalAlignment

    var body: some View {
        let size = timeSize * 0.17
        let reading = sample ?? TelemetrySample(cpu: 0, ram: 0, disk: 0, at: .distantPast)
        VStack(alignment: alignment, spacing: size * 0.6) {
            if style == .bar {
                ForEach(Telemetry.rows(reading)) { row in
                    MeterRow(row: DeskMeterRow(tier: .fiveHour, label: row.label, percent: row.percent,
                                               fill: row.fill, pace: nil, reset: ""),
                             ink: ink, font: font, size: size, width: timeSize * 3.2, alignment: alignment)
                }
            } else {
                DeskStyledGauges(gauges: DeskMeterStyleModel.telemetryGauges(reading), style: style, ink: ink,
                                 font: font, size: size, width: timeSize * 2.6, alignment: alignment)
            }
        }
        .font(.custom(font, size: size))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(SettingsNames.system)
    }
}
