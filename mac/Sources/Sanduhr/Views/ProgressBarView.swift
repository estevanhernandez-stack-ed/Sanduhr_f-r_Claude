import Foundation
import SwiftUI

/// Progress bar: track, usage-colored fill with top sheen, a breathing-glass
/// accent-tinted shimmer over the fill, and an always-on 2px pace ghost tick
/// marking where pace says usage should be right now. A warning tier (MeterWarning) fills red
/// with a steady glow in the theme's accent (Match Desk: the Desk ink) around the bar.
///
/// Parity with windows/src/sanduhr/tiers.py (TierCard paint loop).
struct ProgressBarView: View {
    let utilization: Double            // 0–100
    let paceFraction: Double?          // 0–1, position of the ghost tick
    let palette: Theme.Palette
    /// Nearly full with the reset still far off: red fill, glow behind the bar.
    var warning = false
    /// The two-tone layout of a combined bar (agent sources). Nil, or a layout that is only one
    /// fill (one source), draws the bar as it always has.
    var sources: SourceBarLayout?

    private let breathAmp: Double = 0.08  // subliminal — any higher reads as flicker

    /// Whether this bar draws source segments and ticks instead of the usage-colored fill.
    var drawsTwoTone: Bool { sources.map { !$0.isSingleFill } ?? false }

    var body: some View {
        if let layout = sources, !layout.isSingleFill {
            twoToneBar(layout)
        } else {
            classicBar
        }
    }

    /// The bar as it was before sources: one usage-colored fill and the pace tick.
    private var classicBar: some View {
        GeometryReader { g in
            let fillWidth = max(utilization / 100, 0.008) * g.size.width
            ZStack(alignment: .leading) {
                if let ink = palette.ink {
                    // Match Desk: Desk's meter, the ink faint for the track and full for the
                    // fill, until the warning line (or a meter warning) hands the fill back to
                    // the usage colors.
                    inkRun(ink).opacity(0.22)
                    if !warning && DeskThemeMapping.inkCarries(utilization) {
                        inkRun(ink).frame(width: fillWidth)
                    } else {
                        usageFill(width: fillWidth)
                    }
                } else {
                    // Track — subtle linear gradient instead of flat, for depth.
                    LinearGradient(
                        colors: [palette.barBg.opacity(0.9), palette.barBg.opacity(0.6)],
                        startPoint: .top, endPoint: .bottom)

                    usageFill(width: fillWidth)
                }

                // Breathing glass — subliminal accent-tinted pulse over the
                // fill only, scoped inside the clipShape so empty tracks
                // (utilization=0) stay pure. Period driven by the theme so
                // Matrix pulses faster than the other themes. A Core Animation
                // layer breathes on the render server (BreathLayerView); a
                // timeline view here cost most of the app's idle CPU.
                if utilization > 0 {
                    BreathLayerView(color: NSColor(palette.accent),
                                    pulse: BreathPulse(periodMs: palette.breathPeriodMs, amplitude: breathAmp))
                        .frame(width: fillWidth)
                        .allowsHitTesting(false)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
            .overlay(alignment: .leading) {
                // Always-on pace ghost — 2px tick in the theme's pace_marker
                // color, positioned at paceFraction * bar_width. Rendered in
                // the .overlay (outside clipShape) so it survives the rounded
                // corners on bars where the ghost is near the edge. Parity
                // with windows/src/sanduhr/tiers.py::_update_pace_tick.
                paceTick(in: g.size)
            }
        }
        .frame(height: 10)
        .background {
            // A warning bar: a steady glow wrapping it, a few points larger, as Desk's meters.
            if warning {
                Capsule()
                    .fill(glowStyle)
                    .padding(-4)
                    .blur(radius: 5)
                    .opacity(0.6)
                    .allowsHitTesting(false)
            }
        }
    }

    /// The pace ghost: 2px, at paceFraction of the bar's width.
    @ViewBuilder
    private func paceTick(in size: CGSize) -> some View {
        if let f = paceFraction {
            let tickX = max(0, min(size.width - 2, f * size.width))
            // Under Match Desk the tick is ink on ink, so it stands taller than the
            // bar, as on Desk's meters, to read where it crosses the fill.
            palette.paceMarker
                .opacity(palette.ghostAlpha)
                .frame(width: 2, height: palette.ink == nil ? nil : size.height * 1.6)
                .offset(x: tickX)
        }
    }

    /// The two-tone bar: the primary's fill and the continuation in their source colors, a tick per
    /// other source, the pace tick on top. The container reads as the whole sentence, each mark as its own.
    private func twoToneBar(_ layout: SourceBarLayout) -> some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                trackFill
                SourceBarSegmentsView(layout: layout, size: g.size)
            }
            .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
            .overlay(alignment: .leading) {
                ZStack(alignment: .leading) {
                    SourceBarTicksView(layout: layout, size: g.size)
                    paceTick(in: g.size)
                }
            }
        }
        .frame(height: 10)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(layout.accessibleName)
    }

    /// The empty track under the source segments.
    @ViewBuilder
    private var trackFill: some View {
        if let ink = palette.ink {
            inkRun(ink).opacity(0.22)
        } else {
            LinearGradient(
                colors: [palette.barBg.opacity(0.9), palette.barBg.opacity(0.6)],
                startPoint: .top, endPoint: .bottom)
        }
    }

    /// The glow's color: the theme's accent, or under Match Desk the ink run.
    private var glowStyle: AnyShapeStyle {
        if let ink = palette.ink { return AnyShapeStyle(inkRun(ink)) }
        return AnyShapeStyle(palette.accent)
    }

    /// The fill with a top-to-bottom sheen in the tier color (the warning red for a warning bar).
    private func usageFill(width fillWidth: CGFloat) -> some View {
        let base = warning ? meterWarningRed : usageColor(utilization)
        return LinearGradient(
            colors: [base.opacity(0.95), base.opacity(0.72)],
            startPoint: .top, endPoint: .bottom)
            .frame(width: fillWidth)
            .overlay(
                Rectangle()
                    .fill(Color.white.opacity(0.18))
                    .frame(width: fillWidth, height: 1)
                    .frame(maxHeight: .infinity, alignment: .top)
            )
    }

    /// The Desk ink run left to right, as Desk's meters draw it.
    private func inkRun(_ ink: Theme.Palette.Ink) -> LinearGradient {
        LinearGradient(colors: ink.stops, startPoint: .leading, endPoint: .trailing)
    }
}

/// The source segments of a two-tone bar: each a source-colored fill with the same top sheen as
/// the usage fill, dimmed when stale, named for a screen reader and a tooltip.
struct SourceBarSegmentsView: View {
    let layout: SourceBarLayout
    let size: CGSize

    var body: some View {
        ZStack(alignment: .leading) {
            ForEach(layout.segments, id: \.source) { segment in
                SourceBarSegmentView(segment: segment, span: SourceBar.span(segment, in: size.width))
            }
        }
        .frame(width: size.width, height: size.height, alignment: .leading)
    }
}

struct SourceBarSegmentView: View {
    let segment: SourceBarSegment
    let span: (x: CGFloat, width: CGFloat)

    var body: some View {
        let base = Color.hex(segment.colorHex)
        LinearGradient(colors: [base.opacity(0.95), base.opacity(0.72)], startPoint: .top, endPoint: .bottom)
            .overlay(alignment: .top) {
                Rectangle().fill(Color.white.opacity(0.18)).frame(height: 1)
            }
            .frame(width: span.width)
            .opacity(segment.opacity)
            .offset(x: span.x)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(segment.name)
            .help(segment.name)
    }
}

/// The ticks of a two-tone bar: 2 points wide, in the source's color, dimmed when stale.
struct SourceBarTicksView: View {
    let layout: SourceBarLayout
    let size: CGSize

    var body: some View {
        ZStack(alignment: .leading) {
            ForEach(layout.ticks, id: \.source) { tick in
                Rectangle()
                    .fill(Color.hex(tick.colorHex))
                    .frame(width: SourceBar.tickWidth)
                    .opacity(tick.opacity)
                    .offset(x: SourceBar.tickX(at: tick.at, in: size.width))
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(tick.name)
                    .help(tick.name)
            }
        }
        .frame(width: size.width, height: size.height, alignment: .leading)
    }
}
