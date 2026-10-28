import SwiftUI

/// The meter styles as the Desk draws them (2.37.0 item 3): slim, ring and ticks for the Claude
/// limits and the `claude` piece, after Windows' `DeskView.Styles.cs`. Everything here is static
/// shapes and text: a reading changes in place and nothing animates, so idle CPU stays where the
/// bars have it. `ink` is the ink as the piece draws it (OLED care's white cap already applied).
/// The rules (cells lit, short names, what each style leaves out) are `DeskMeterStyleModel`'s.

/// The readings of one styled block, in a style: rings side by side in a row, slim and ticks
/// stacked. `report` gets each reading's frame, for the limit menu's hit areas.
struct DeskStyledGauges: View {
    let gauges: [DeskMeterStyleModel.Gauge]
    let style: DeskMeterStyle
    let ink: String
    let font: String
    let size: CGFloat
    let width: CGFloat
    let alignment: HorizontalAlignment
    var report: ((Tier, CGRect) -> Void)?

    var body: some View {
        if style == .ring {
            HStack(alignment: .top, spacing: size * 0.6) {
                ForEach(gauges) { g in
                    DeskRingGauge(gauge: g, ink: ink, font: font, size: size)
                        .reporting(g.tier, report)
                }
            }
        } else {
            VStack(alignment: alignment, spacing: size * 0.4) {
                ForEach(gauges) { g in
                    row(g)
                        .reporting(g.tier, report)
                }
            }
        }
    }

    @ViewBuilder
    private func row(_ g: DeskMeterStyleModel.Gauge) -> some View {
        if style == .ticks {
            DeskTicksGauge(gauge: g, ink: ink, font: font, size: size, width: width)
        } else {
            DeskSlimGauge(gauge: g, ink: ink, font: font, size: size, width: width)
        }
    }
}

private extension View {
    @ViewBuilder
    func reporting(_ tier: Tier, _ report: ((Tier, CGRect) -> Void)?) -> some View {
        if let report {
            onGlobalFrame { report(tier, $0) }
        } else {
            self
        }
    }
}

/// The label and the value on one line, the value at the end: red with the glyph beside it over
/// the warning line, as the bar's row says it.
private struct DeskGaugeHead: View {
    let gauge: DeskMeterStyleModel.Gauge
    let ink: String
    let size: CGFloat

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(gauge.label).lineLimit(1).opacity(0.85)
            Spacer(minLength: size * 0.5)
            HStack(alignment: .firstTextBaseline, spacing: size * 0.25) {
                if gauge.warning {
                    Image(systemName: MeterWarning.glyph)
                        .font(.system(size: size * 0.75, weight: .semibold))
                        .foregroundStyle(LinearGradient.ink(ink))
                    Text("\(gauge.percent)%").foregroundStyle(meterWarningRed)
                } else {
                    Text("\(gauge.percent)%")
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(MeterWarning.spokenValue(percent: gauge.percent, warning: gauge.warning))
        }
    }
}

/// Slim: the label and the value on one line over a thin bar, about half a bar row's height. The
/// pace mark crosses the bar; over the warning line the fill is red with a steady glow behind it.
struct DeskSlimGauge: View {
    let gauge: DeskMeterStyleModel.Gauge
    let ink: String
    let font: String
    let size: CGFloat
    let width: CGFloat

    var body: some View {
        let text = size * 0.9
        let barH = max(2, (size * 0.16).rounded())
        let markH = gauge.pace == nil ? barH : max(6, barH * 3)
        VStack(alignment: .leading, spacing: size * 0.1) {
            DeskGaugeHead(gauge: gauge, ink: ink, size: text)
                .font(.custom(font, size: text))
            bar(barH: barH, markH: markH)
        }
        .frame(width: width)
    }

    private func bar(barH: CGFloat, markH: CGFloat) -> some View {
        let fill = DeskMeterStyleModel.slimFillWidth(gauge.fill, width: Double(width))
        return ZStack(alignment: .leading) {
            Capsule().fill(LinearGradient.ink(ink)).opacity(0.22).frame(height: barH)
            Capsule().fill(gauge.warning ? AnyShapeStyle(meterWarningRed) : AnyShapeStyle(LinearGradient.ink(ink)))
                .frame(width: fill, height: barH)
                .background { glow(barH) }
            if let pace = gauge.pace {
                Rectangle().fill(LinearGradient.ink(ink))
                    .frame(width: 2, height: markH)
                    .offset(x: DeskMeterStyleModel.markOffset(pace, width: Double(width)))
            }
        }
        .frame(width: width, height: markH)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(gauge.label)
        .accessibilityValue(MeterWarning.spokenValue(percent: gauge.percent, warning: gauge.warning))
    }

    @ViewBuilder
    private func glow(_ barH: CGFloat) -> some View {
        if gauge.warning {
            Capsule().fill(LinearGradient.ink(ink))
                .padding(-barH * 0.7)
                .blur(radius: barH * 0.9)
                .opacity(0.6)
        }
    }
}

/// Ticks: the label and the value on one line over ten cells. A lit cell is filled; an unlit one a
/// faint outline, so the meter lights far fewer pixels than a bar (the OLED-friendly style). The
/// pace mark stands across the cells; over the warning line the lit cells are red.
struct DeskTicksGauge: View {
    let gauge: DeskMeterStyleModel.Gauge
    let ink: String
    let font: String
    let size: CGFloat
    let width: CGFloat

    var body: some View {
        let text = size * 0.9
        VStack(alignment: .leading, spacing: size * 0.14) {
            DeskGaugeHead(gauge: gauge, ink: ink, size: text)
                .font(.custom(font, size: text))
            cells
        }
        .frame(width: width)
    }

    private var cells: some View {
        let n = DeskMeterStyleModel.ticks
        let gap = max(2, (size * 0.16).rounded())
        let cw = (width - CGFloat(n - 1) * gap) / CGFloat(n)
        let ch = max(4, (size * 0.42).rounded())
        let over: CGFloat = gauge.pace == nil ? 0 : max(2, (size * 0.18).rounded())
        let lit = DeskMeterStyleModel.litTicks(gauge.fill, cells: n)
        let radius = min(cw, ch) * 0.2
        return ZStack(alignment: .topLeading) {
            // Lit cells: the ink runs along the row (or red), cut to the lit cells.
            litShape
                .frame(width: width, height: ch)
                .mask(alignment: .leading) {
                    HStack(spacing: gap) {
                        ForEach(0..<n, id: \.self) { i in
                            RoundedRectangle(cornerRadius: radius).frame(width: cw, height: ch).opacity(i < lit ? 1 : 0)
                        }
                    }
                }
                .offset(y: over)
            // Unlit cells: a faint outline in the ink.
            HStack(spacing: gap) {
                ForEach(0..<n, id: \.self) { i in
                    RoundedRectangle(cornerRadius: radius)
                        .strokeBorder(LinearGradient.ink(ink), lineWidth: 1)
                        .frame(width: cw, height: ch)
                        .opacity(i < lit ? 0 : 0.3)
                }
            }
            .offset(y: over)
            if let pace = gauge.pace {
                Rectangle().fill(LinearGradient.ink(ink))
                    .frame(width: 2, height: ch + 2 * over)
                    .offset(x: DeskMeterStyleModel.markOffset(pace, width: Double(width)))
            }
        }
        .frame(width: width, height: ch + 2 * over, alignment: .topLeading)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(gauge.label)
        .accessibilityValue(MeterWarning.spokenValue(percent: gauge.percent, warning: gauge.warning))
    }

    private var litShape: some View {
        Rectangle().fill(gauge.warning ? AnyShapeStyle(meterWarningRed) : AnyShapeStyle(LinearGradient.ink(ink)))
    }
}

/// Ring: a small 270-degree arc gauge with the value inside and the short name tucked into its
/// open bottom. The track is the ink at 22%, the arc the ink (red over the warning line, with a
/// steady glow and the warning glyph above the value), and a Claude limit's pace mark crosses the arc.
struct DeskRingGauge: View {
    let gauge: DeskMeterStyleModel.Gauge
    let ink: String
    let font: String
    let size: CGFloat

    var body: some View {
        let d = (size * 3.4).rounded()
        VStack(spacing: 0) {
            face(d)
            Text(gauge.short)
                .font(.custom(font, size: size * 0.68))
                .lineLimit(1)
                .opacity(0.8)
                .frame(maxWidth: d + size * 0.5)
                .padding(.top, -size * 0.3)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(gauge.label)
        .accessibilityValue(MeterWarning.spokenValue(percent: gauge.percent, warning: gauge.warning))
    }

    private func face(_ d: CGFloat) -> some View {
        let t = max(3, (size * 0.26).rounded())
        let trim = DeskMeterStyleModel.ringTrim(gauge.fill)
        let arcStyle: AnyShapeStyle = gauge.warning ? AnyShapeStyle(meterWarningRed) : AnyShapeStyle(LinearGradient.ink(ink))
        return ZStack {
            Circle().inset(by: t).trim(from: 0, to: DeskMeterStyleModel.ringSweep / 360)
                .stroke(LinearGradient.ink(ink), style: StrokeStyle(lineWidth: t, lineCap: .round))
                .opacity(0.22)
                .rotationEffect(.degrees(DeskMeterStyleModel.ringStart))
            if trim > 0 {
                Circle().inset(by: t).trim(from: 0, to: trim)
                    .stroke(arcStyle, style: StrokeStyle(lineWidth: t, lineCap: .round))
                    .rotationEffect(.degrees(DeskMeterStyleModel.ringStart))
                    .background { glow(d, t) }
            }
            if let pace = gauge.pace {
                paceMark(d: d, t: t, pace: pace)
            }
            value
        }
        .frame(width: d, height: d)
    }

    @ViewBuilder
    private func glow(_ d: CGFloat, _ t: CGFloat) -> some View {
        if gauge.warning {
            Circle().inset(by: t).trim(from: 0, to: DeskMeterStyleModel.ringTrim(gauge.fill))
                .stroke(LinearGradient.ink(ink), style: StrokeStyle(lineWidth: t * 1.6, lineCap: .round))
                .rotationEffect(.degrees(DeskMeterStyleModel.ringStart))
                .blur(radius: t * 0.9)
                .opacity(0.6)
        }
    }

    private func paceMark(d: CGFloat, t: CGFloat, pace: Double) -> some View {
        let radius = d / 2 - t
        let a = DeskMeterStyleModel.ringPaceAngle(pace) * .pi / 180
        let inner = radius - t * 0.95, outer = radius + t * 0.95
        let c = d / 2
        return Path { p in
            p.move(to: CGPoint(x: c + inner * cos(a), y: c + inner * sin(a)))
            p.addLine(to: CGPoint(x: c + outer * cos(a), y: c + outer * sin(a)))
        }
        .stroke(LinearGradient.ink(ink), lineWidth: 2)
        .frame(width: d, height: d)
    }

    private var value: some View {
        VStack(spacing: 0) {
            if gauge.warning {
                Image(systemName: MeterWarning.glyph)
                    .font(.system(size: size * 0.6, weight: .semibold))
                    .foregroundStyle(LinearGradient.ink(ink))
            }
            Text(DeskMeterStyleModel.percentText(gauge.percent, style: .ring))
                .font(.custom(BundledFonts.face(font, bold: true), size: size))
                .foregroundStyle(gauge.warning ? AnyShapeStyle(meterWarningRed) : AnyShapeStyle(.foreground))
        }
    }
}
