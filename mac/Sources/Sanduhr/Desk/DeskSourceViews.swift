import SwiftUI

/// The Desk's usage pieces with two sources (agent sources, item 8): the combined fill, ring arcs and
/// cells, and the whole-block views for a piece that draws Codex or the joined rows. A piece that draws
/// Claude alone never comes here: `DeskPiece` keeps its own drawing for it, so a Mac with only Claude on
/// is drawn as it was. Static shapes and text, nothing animates, so idle CPU stays where it was.

extension View {
    /// A Codex window that is not fresh draws dimmed (`SourceBar.staleOpacity`); a fresh one is left alone.
    @ViewBuilder
    func sourceStale(_ stale: Bool) -> some View {
        if stale { opacity(SourceBar.staleOpacity) } else { self }
    }
}

/// The two-tone fill of a bar (bar and slim styles): the furthest other source's stretch under the
/// primary's fill, each in its own color, and a tick for every other source. The pills meet without a
/// notch (the continuation starts a radius early, under the primary), ticks are taller than the bar.
struct DeskSourceBarFill: View {
    let layout: SourceBarLayout
    let width: CGFloat
    let height: CGFloat
    let tickHeight: CGFloat

    var body: some View {
        ZStack(alignment: .leading) {
            if let beyond = layout.beyond {
                let span = SourceBar.span(beyond, in: width)
                let start = max(0, span.x - height / 2)
                Capsule().fill(Color.hex(beyond.colorHex))
                    .frame(width: max(0, span.x + span.width - start), height: height)
                    .opacity(beyond.opacity)
                    .offset(x: start)
            }
            if let primary = layout.primary {
                Capsule().fill(Color.hex(primary.colorHex))
                    .frame(width: SourceBar.span(primary, in: width).width, height: height)
                    .opacity(primary.opacity)
            }
            ForEach(layout.ticks, id: \.source) { tick in
                Rectangle().fill(Color.hex(tick.colorHex))
                    .frame(width: SourceBar.tickWidth, height: tickHeight)
                    .opacity(tick.opacity)
                    .offset(x: SourceBar.tickX(at: tick.at, in: width))
            }
        }
        .frame(width: width, height: max(height, tickHeight), alignment: .leading)
    }
}

/// A ring's combined arcs: the primary's from the start, the continuation after it, a short tick across
/// the arc for every other source, in the source colors. The track and the pace mark are the ring's own.
struct DeskSourceRingArcs: View {
    let layout: SourceBarLayout
    let diameter: CGFloat
    let thickness: CGFloat

    var body: some View {
        ZStack {
            ForEach(layout.segments, id: \.source) { segment in
                arc(segment)
            }
            ForEach(layout.ticks, id: \.source) { tick in
                tickMark(tick)
            }
        }
        .frame(width: diameter, height: diameter)
    }

    @ViewBuilder
    private func arc(_ segment: SourceBarSegment) -> some View {
        if let part = DeskMeterStyleModel.ringSegment(from: segment.from, to: segment.to) {
            Circle().inset(by: thickness).trim(from: part.from, to: part.to)
                .stroke(Color.hex(segment.colorHex), style: StrokeStyle(lineWidth: thickness, lineCap: .round))
                .rotationEffect(.degrees(DeskMeterStyleModel.ringStart))
                .opacity(segment.opacity)
        }
    }

    private func tickMark(_ tick: SourceBarTick) -> some View {
        let radius = diameter / 2 - thickness
        let a = DeskMeterStyleModel.ringTickAngle(at: tick.at) * .pi / 180
        let inner = radius - thickness * 0.95, outer = radius + thickness * 0.95
        let c = diameter / 2
        return Path { p in
            p.move(to: CGPoint(x: c + inner * cos(a), y: c + inner * sin(a)))
            p.addLine(to: CGPoint(x: c + outer * cos(a), y: c + outer * sin(a)))
        }
        .stroke(Color.hex(tick.colorHex), lineWidth: 3)
        .opacity(tick.opacity)
        .frame(width: diameter, height: diameter)
    }
}

/// The ticks style's cells for a combined layout: lit in the source colors (the primary's, then the
/// cells the furthest other source reaches), the others a faint outline in the ink, and a short mark across
/// the row for every other source. The cell rules are `DeskMeterStyleModel.sourceCells`.
struct DeskSourceCells: View {
    let layout: SourceBarLayout
    let ink: String
    let width: CGFloat
    let cellWidth: CGFloat
    let cellHeight: CGFloat
    let gap: CGFloat
    let over: CGFloat
    let radius: CGFloat

    var body: some View {
        let cells = DeskMeterStyleModel.sourceCells(layout)
        ZStack(alignment: .topLeading) {
            HStack(spacing: gap) {
                ForEach(Array(cells.enumerated()), id: \.offset) { _, cell in
                    cellView(cell)
                }
            }
            .offset(y: over)
            ForEach(layout.ticks, id: \.source) { tick in
                Rectangle().fill(Color.hex(tick.colorHex))
                    .frame(width: SourceBar.tickWidth, height: cellHeight + 2 * over)
                    .opacity(tick.opacity)
                    .offset(x: SourceBar.tickX(at: tick.at, in: width))
            }
        }
        .frame(width: width, height: cellHeight + 2 * over, alignment: .topLeading)
    }

    @ViewBuilder
    private func cellView(_ cell: DeskMeterStyleModel.SourceCell) -> some View {
        if let hex = cell.colorHex {
            RoundedRectangle(cornerRadius: radius).fill(Color.hex(hex))
                .frame(width: cellWidth, height: cellHeight)
                .opacity(cell.stale ? SourceBar.staleOpacity : 1)
        } else {
            RoundedRectangle(cornerRadius: radius)
                .strokeBorder(LinearGradient.ink(ink), lineWidth: 1)
                .frame(width: cellWidth, height: cellHeight)
                .opacity(0.3)
        }
    }
}

/// The meters piece when it draws the joined rows or Codex alone: a bar (or slim, ring, ticks) per
/// row, in the piece's own frame and hit plate. Codex rows dim one by one and report no limit-menu
/// frames (the limit menu is Claude's); a Codex piece with no Codex numbers draws nothing.
struct DeskSourceMeters: View {
    var model: DeskModel
    let drawn: DeskMeterSource
    let style: DeskMeterStyle
    let ink: String
    let font: String
    let timeSize: Double
    let alignment: HorizontalAlignment

    var body: some View {
        let view = model.sourceView(drawn)
        if !view.rows.isEmpty {
            block(view.rows)
        }
    }

    private var isCodex: Bool { drawn == .codex }

    private func block(_ rows: [DeskMeterRow]) -> some View {
        let size = timeSize * 0.17
        return VStack(alignment: alignment, spacing: size * 0.6) { content(rows, size: size) }
            .font(.custom(font, size: size))
            .opacity(isCodex ? 1 : (model.veiled ? 0 : (model.claudeLineIsStale ? 0.5 : 1)))
            .accessibilityHidden(!isCodex && model.veiled)
            .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity)
                .padding(EdgeInsets(top: -6, leading: -8, bottom: -6, trailing: -8)))
            .contentShape(Rectangle())
            .onDeskFrame(model) { $0.metersFrame = $1 }
    }

    @ViewBuilder
    private func content(_ rows: [DeskMeterRow], size: CGFloat) -> some View {
        if style == .bar {
            ForEach(rows) { row in
                MeterRow(row: row, ink: ink, font: font, size: size, width: timeSize * 3.2, alignment: alignment)
                    .sourceStale(row.stale)
                    .modifier(DeskSourceRowFrame(model: model, tier: isCodex ? nil : row.tier))
                    .deskPulse(DeskPulses.trigger(model.pulses, drawn: drawn, row: row), ink: ink, size: size)
            }
        } else {
            DeskStyledGauges(gauges: DeskMeterStyleModel.gauges(rows), style: style, ink: ink, font: font, size: size,
                             width: timeSize * 2.6, alignment: alignment,
                             report: isCodex ? nil : { tier, frame, window in
                                 model.setFrames(in: window) { $0.meterRowFrames[tier] = frame }
                             })
        }
    }
}

/// A bar row's limit-menu frame, reported for Claude's limits (a joined row keeps its Claude tier) and
/// not at all for a Codex row.
private struct DeskSourceRowFrame: ViewModifier {
    var model: DeskModel
    let tier: Tier?

    func body(content: Content) -> some View {
        if let tier {
            content.onDeskFrame(model) { $0.meterRowFrames[tier] = $1 }
        } else {
            content
        }
    }
}

/// The claude piece when it draws Codex: "Codex 5h 30% · wk 70%" in the bar style, the two compact rows
/// in slim, the name over the session and week gauges in ring and ticks. Without Codex numbers: nothing.
struct DeskSourceClaude: View {
    let view: DeskSourceView
    let style: DeskMeterStyle
    let ink: String
    let font: String
    let timeSize: Double
    let alignment: HorizontalAlignment

    var body: some View {
        let size = timeSize * 0.19
        switch DeskMeterStyleModel.claudeForm(style) {
        case .line:
            if let line = view.line {
                Text(line).font(.custom(font, size: size)).opacity(view.stale ? 0.45 : 0.75)
            }
        case .compactRows:
            if let two = view.compact { rows(two, size: size) }
        case .gauges:
            gauges
        }
    }

    private func rows(_ two: DeskClaudeCompact, size: CGFloat) -> some View {
        VStack(alignment: alignment, spacing: 0) {
            Text(two.lines[0]).font(.custom(font, size: size)).opacity(view.stale ? 0.45 : 0.75)
            if two.lines.count > 1 {
                Text(two.lines[1]).font(.custom(font, size: size)).opacity(view.stale ? 0.45 * 0.85 : 0.75 * 0.85)
            }
        }
    }

    @ViewBuilder
    private var gauges: some View {
        if !view.gauges.isEmpty {
            VStack(alignment: alignment, spacing: timeSize * 0.17 * 0.3) {
                Text(view.name ?? "Codex").font(.custom(font, size: timeSize * 0.17)).opacity(0.75)
                DeskStyledGauges(gauges: view.gauges, style: style, ink: ink, font: font, size: timeSize * 0.17,
                                 width: timeSize * 2.6, alignment: alignment)
            }
        }
    }
}
