import AppKit
import SwiftUI

/// The session and weekly numbers a tour preview draws, from the view model's last fetch. No
/// network: nil until Sanduhr has fetched.
struct TourNumbers: Equatable {
    var session: Double?
    var weekly: Double?
    var sessionPace: Double?
    var weeklyPace: Double?

    @MainActor
    static func current() -> TourNumbers {
        let usage = (NSApp.delegate as? AppDelegate)?.viewModel.shownUsage
        let s = usage?.tiers[.fiveHour], w = usage?.tiers[.sevenDay]
        return TourNumbers(session: s?.utilization, weekly: w?.utilization,
                           sessionPace: paceFrac(s?.resetsAt, tier: .fiveHour),
                           weeklyPace: paceFrac(w?.resetsAt, tier: .sevenDay))
    }

    var hasAny: Bool { session != nil || weekly != nil }

    static func percent(_ v: Double?) -> String { v.map { "\(Int($0))%" } ?? "–" }

    /// What VoiceOver reads for a preview of these numbers.
    var spoken: String {
        guard hasAny else { return "Your meters show here after Sanduhr's first fetch" }
        return "Session \(Self.percent(session)), weekly \(Self.percent(weekly))"
    }
}

/// The tour's previews. Still with Reduce Motion: the meters drop the bar's breathing shimmer,
/// and nothing else here moves.
struct TourPreviewView: View {
    let preview: TourPreview
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        // Read here so SwiftUI redraws when the numbers change.
        let numbers = TourNumbers.current()
        switch preview {
        case .meters:
            meters(numbers)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Your limits: \(numbers.spoken)")
        case .deskCorner:
            deskCorner(numbers)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("A Desk corner with the date and your meters: \(numbers.spoken)")
        case .notch:
            notch(numbers)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("The notch with a wing on each side: \(numbers.spoken)")
        case .menuBar:
            WhatsNewPreviewView(preview: .menuBar)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("The menu bar hourglass with a percent beside it")
        case .accountChip:
            accountChip
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("The widget's account chip, Personal and Work")
        }
    }

    // MARK: Meters

    /// The palette the meters draw in: the widget's theme, or the default one under Match Desk
    /// (its clear background and ink would vanish in this window).
    private var palette: Theme.Palette {
        let theme = (NSApp.delegate as? AppDelegate)?.viewModel.theme
        guard let theme, theme.palette.ink == nil else { return ThemeRegistry.default.palette }
        return theme.palette
    }

    @ViewBuilder
    private func meters(_ n: TourNumbers) -> some View {
        let p = palette
        if n.hasAny {
            VStack(alignment: .leading, spacing: 10) {
                meterRow("Session", n.session, pace: n.sessionPace, palette: p)
                meterRow("Weekly", n.weekly, pace: n.weeklyPace, palette: p)
            }
            .padding(.horizontal, 16)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(ZStack { Color.black.opacity(0.8); p.bg })
        } else {
            Text("Your meters show here after Sanduhr's first fetch.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 16)
        }
    }

    private func meterRow(_ label: String, _ value: Double?, pace: Double?, palette p: Theme.Palette) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(label).font(.system(size: 11, weight: .medium)).foregroundStyle(p.textSecondary)
                Spacer()
                Text(TourNumbers.percent(value))
                    .font(.system(size: 12, weight: .semibold).monospacedDigit())
                    .foregroundStyle(value.map(usageColor) ?? p.textDim)
            }
            if reduceMotion {
                StillMeterBar(value: value ?? 0, pace: pace, track: p.barBg, marker: p.paceMarker,
                              fill: usageColor(value ?? 0), height: 10)
            } else {
                ProgressBarView(utilization: value ?? 0, paceFraction: pace, palette: p)
            }
        }
    }

    // MARK: Desk corner

    private func deskCorner(_ n: TourNumbers) -> some View {
        ZStack(alignment: .bottomLeading) {
            LinearGradient(colors: [Color.hex("1b2330"), Color.hex("0d1118")],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            VStack(alignment: .leading, spacing: 6) {
                Text(Date.now.formatted(.dateTime.weekday(.wide).month(.wide).day()))
                    .font(.custom(BundledFonts.regularFace, size: 22))
                deskMeter("S", n.session, pace: n.sessionPace)
                deskMeter("W", n.weekly, pace: n.weeklyPace)
            }
            .foregroundStyle(.white)
            .padding(14)
        }
    }

    private func deskMeter(_ letter: String, _ value: Double?, pace: Double?) -> some View {
        HStack(spacing: 6) {
            Text(letter).font(.system(size: 10, weight: .semibold))
            StillMeterBar(value: value ?? 0, pace: pace, track: .white.opacity(0.22), marker: .white,
                          fill: .white, height: 4)
                .frame(width: 120)
            Text(TourNumbers.percent(value)).font(.system(size: 10).monospacedDigit())
        }
    }

    // MARK: Notch

    private func notch(_ n: TourNumbers) -> some View {
        HStack(spacing: 0) {
            wing("S \(TourNumbers.percent(n.session))", alignment: .leading)
            Color.black.frame(width: 64, height: 32)
            wing("W \(TourNumbers.percent(n.weekly))", alignment: .trailing)
        }
        .background(UnevenRoundedRectangle(bottomLeadingRadius: 12, bottomTrailingRadius: 12).fill(.black))
    }

    private func wing(_ text: String, alignment: Alignment) -> some View {
        Text(text)
            .font(.system(size: 12, weight: .medium).monospacedDigit())
            .foregroundStyle(.white)
            .padding(.horizontal, 14)
            .frame(width: 90, height: 32, alignment: alignment)
    }

    // MARK: Account chip

    private var accountChip: some View {
        HStack(spacing: 8) {
            chip("Personal", active: true)
            Image(systemName: "arrow.right").font(.caption).foregroundStyle(.secondary)
            chip("Work", active: false)
        }
    }

    private func chip(_ name: String, active: Bool) -> some View {
        Text(name)
            .font(.system(size: 12, weight: .medium, design: .rounded))
            .padding(.horizontal, 10)
            .padding(.vertical, 3)
            .background(Capsule().fill(Color.primary.opacity(active ? 0.14 : 0.06)))
    }
}

/// A meter bar that never moves: the track, the fill and the pace tick.
struct StillMeterBar: View {
    let value: Double
    let pace: Double?
    let track: Color
    let marker: Color
    let fill: Color
    let height: CGFloat

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(track)
                Capsule().fill(fill).frame(width: max(0, min(1, value / 100)) * g.size.width)
                if let pace {
                    marker.frame(width: 2, height: height * 1.6)
                        .offset(x: max(0, min(g.size.width - 2, pace * g.size.width)))
                }
            }
        }
        .frame(height: height)
    }
}
