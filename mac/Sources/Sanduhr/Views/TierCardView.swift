import SwiftUI

/// One "glass card" showing a single tier — header (label, sparkline, %),
/// progress bar, reset countdown + pace, and reset date + burn warning.
/// Port of sanduhr.py:495-566, tightened for a 340-wide layout.
struct TierCardView: View {
    let tier: Tier
    let usage: TierUsage
    let history: [Double]
    let palette: Theme.Palette
    let tick: Int

    private var util: Double { usage.utilization ?? 0 }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            headerRow
            ProgressBarView(utilization: util,
                            paceFraction: paceFrac(usage.resetsAt, tier: tier),
                            palette: palette)
            infoRow
            reminderRow
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        // Layered glass: .thinMaterial is the vibrancy layer underneath,
        // then a theme-tinted pane uses `glassOnMica` (tuned for contrast
        // over vibrancy) at the theme's preferred density. An optional
        // `innerHighlight` adds an accent-colored top-edge light catch.
        .background(
            ZStack {
                RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                    .fill(.thinMaterial)
                RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                    .fill(palette.glassOnMica.opacity(palette.glassAlpha))
                if let hl = palette.innerHighlight {
                    RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                        .fill(LinearGradient(
                            colors: [hl.color.opacity(hl.alpha), Color.clear],
                            startPoint: .top, endPoint: .center))
                        .allowsHitTesting(false)
                }
            }
        )
        .overlay(
            RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                .strokeBorder(
                    (palette.borderTint ?? palette.border).opacity(palette.borderAlpha),
                    lineWidth: 0.5)
        )
        .clipShape(RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous))
        .shadow(color: .black.opacity(0.18), radius: 4, x: 0, y: 2)
    }

    // MARK: Rows

    private var headerRow: some View {
        HStack(spacing: 6) {
            Text(tier.label)
                .font(.system(size: 10, weight: .semibold, design: .rounded))
                .foregroundStyle(palette.textSecondary)
                .lineLimit(1)
                .minimumScaleFactor(0.85)

            Spacer(minLength: 4)

            if history.count >= 2 {
                SparklineView(values: history, color: palette.sparkline)
                    .frame(width: 44, height: 14)
            }

            Text("\(Int(util))%")
                .font(.system(size: 13, weight: .bold,
                              design: palette.numericFontDesign))
                .foregroundStyle(usageColor(util))
                .monospacedDigit()
                .shadow(color: usageColor(util).opacity(palette.accentBloom.alpha),
                        radius: palette.accentBloom.blur)
        }
    }

    private var infoRow: some View {
        HStack {
            Text("Resets in \(timeUntil(usage.resetsAt))")
                .font(.system(size: 9, design: palette.numericFontDesign))
                .foregroundStyle(palette.textDim)
                .id(tick)

            Spacer()

            if let pace = paceInfo(util: util, iso: usage.resetsAt, tier: tier) {
                Text(pace.text)
                    .font(.system(size: 9, weight: .semibold,
                                  design: palette.numericFontDesign))
                    .foregroundStyle(Color.hex(pace.colorHex))
            }
        }
    }

    private var reminderRow: some View {
        HStack {
            Text(resetDateTimeStr(usage.resetsAt))
                .font(.system(size: 8))
                .foregroundStyle(palette.textMuted)

            Spacer()

            if let burn = burnProjection(util: util, iso: usage.resetsAt, tier: tier) {
                Text(burn.text)
                    .font(.system(size: 8))
                    .foregroundStyle(Color.hex(burn.colorHex))
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
        }
    }
}

/// Bonus card shown when `extra_usage.is_enabled`. sanduhr.py:591-608.
struct ExtraUsageCard: View {
    let extra: ExtraUsage
    let palette: Theme.Palette

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text("Extra Usage")
                .font(.system(size: 10, weight: .semibold, design: .rounded))
                .foregroundStyle(palette.textSecondary)
            Text(text)
                .font(.system(size: 9))
                .foregroundStyle(palette.textDim)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .background(
            ZStack {
                RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                    .fill(.thinMaterial)
                // ExtraUsage is secondary info, so sit lighter than tier cards.
                RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                    .fill(palette.glassOnMica.opacity(palette.glassAlpha * 0.5))
            }
        )
        .overlay(
            RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous)
                .strokeBorder(
                    (palette.borderTint ?? palette.border).opacity(palette.borderAlpha * 0.6),
                    lineWidth: 0.5))
        .clipShape(RoundedRectangle(cornerRadius: palette.cardCornerRadius, style: .continuous))
    }

    private var text: String {
        let used = extra.usedCredits ?? 0
        var s = String(format: "$%.2f spent", used)
        if let lim = extra.monthlyLimit {
            s += String(format: " / $%.2f limit", lim)
        }
        return s
    }
}
