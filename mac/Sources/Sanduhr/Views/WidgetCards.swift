import SwiftUI
import AppKit

/// What the widget's card stack draws: the rows, their sparklines, which draw red, the local
/// burn and the switch's veil. The widget builds it from the view model (`live`); Settings'
/// widget preview (item 68) builds it the same way, or from sample numbers through the same
/// visibility and warning rules before the first fetch (`sample`).
struct WidgetCards {
    var rows: [(tier: Tier, usage: TierUsage)]
    var history: [Tier: [Double]] = [:]
    var warning: Set<Tier> = []
    var tick = 0
    var pinDeepMath = false
    var sparklineMode: SparklineView.Mode = .horizon
    var localTokens: [Tier: Int64] = [:]
    var extra: ExtraUsage?
    var veil = false
    var note = false

    /// The widget's own cards, for `rows` (`vm.visibleTiers()`).
    @MainActor
    static func live(_ vm: UsageViewModel, rows: [(tier: Tier, usage: TierUsage)]) -> WidgetCards {
        var history: [Tier: [Double]] = [:]
        var tokens: [Tier: Int64] = [:]
        for row in rows {
            history[row.tier] = vm.shownHistory[row.tier.rawValue]?.map(\.v) ?? []
            tokens[row.tier] = vm.localBurn.tokens(for: row.tier)
        }
        let extra = vm.shownUsage?.extraUsage
        return WidgetCards(rows: rows, history: history, warning: vm.warningTiers, tick: vm.countdownTick,
                           pinDeepMath: vm.pacingPinned, sparklineMode: SparklineView.mode(themeID: vm.theme.id),
                           localTokens: tokens, extra: (extra?.isEnabled ?? false) && !vm.compact ? extra : nil,
                           veil: vm.switchVeil, note: vm.switchNote)
    }

    /// Sample cards for `usage`, with the widget's switches (compact, pinned calculators, theme)
    /// and the saved Meters settings: the same visibility and warning functions the widget uses.
    @MainActor
    static func sample(_ vm: UsageViewModel, usage: UsageResponse, now: Date = Date(),
                       desk: UserDefaults = .desk) -> WidgetCards {
        let rows = UsageViewModel.visibleTiers(usage, hidden: MeterVisibility.hidden(in: desk), compact: vm.compact)
        return WidgetCards(rows: rows, warning: UsageViewModel.warningTiers(usage, now: now, desk: desk),
                           tick: vm.countdownTick, pinDeepMath: vm.pacingPinned,
                           sparklineMode: SparklineView.mode(themeID: vm.theme.id))
    }
}

/// The tier cards and the extra-usage card. During an account switch these are the old
/// account's, faded out and kept in the layout unseen so the widget keeps its height, with
/// the faint "Switching account…" over them once the fetch outlasts the fade; the new
/// account's fade in when they arrive (AccountSwitchFade). `vm` gives each card its limit menu;
/// a preview passes nil.
struct WidgetCardStack: View {
    let cards: WidgetCards
    let palette: Theme.Palette
    var vm: UsageViewModel?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(cards.rows, id: \.tier) { row in
                card(row)
            }
            if let extra = cards.extra {
                ExtraUsageCard(extra: extra, palette: palette)
            }
        }
        .opacity(cards.veil ? 0 : 1)
        .accessibilityHidden(cards.veil)
        .allowsHitTesting(!cards.veil)
        .overlay(alignment: .topLeading) {
            if cards.note {
                Text(AccountSwitchFade.note)
                    .font(.app(size: 11))
                    .foregroundStyle(palette.textDim)
                    .opacity(AccountSwitchFade.noteOpacity)
                    .transition(.opacity)
            }
        }
    }

    @ViewBuilder
    private func card(_ row: (tier: Tier, usage: TierUsage)) -> some View {
        let view = TierCardView(
            tier: row.tier,
            usage: row.usage,
            history: cards.history[row.tier] ?? [],
            palette: palette,
            tick: cards.tick,
            pinDeepMath: cards.pinDeepMath,
            warning: cards.warning.contains(row.tier),
            sparklineMode: cards.sparklineMode,
            localTokens: cards.localTokens[row.tier] ?? 0)
        if let vm {
            view.modifier(LimitContextMenu(tier: row.tier, vm: vm))
        } else {
            view
        }
    }
}

/// The widget's glass: NSVisualEffectView behind the window blurs whatever's underneath; a very
/// faint theme tint adds mood without killing the transparency; a hairline white inner stroke is
/// the "lit glass edge" convention Apple uses in Control Center, the HUDs, etc. Subtle mode and
/// Match Desk draw none of it (Chrome.opacity). The widget and Settings' widget preview share it.
struct WidgetGlass: ViewModifier {
    let palette: Theme.Palette
    let shadow: WidgetShadow

    func body(content: Content) -> some View {
        let t = palette
        content
            .background(
                ZStack {
                    VisualEffectView(material: .hudWindow,
                                     blendingMode: .behindWindow,
                                     state: .active)
                    // Theme tint — translucent for most themes (so vibrancy
                    // dominates), opaque for Matrix (so the green reads pure
                    // and the desktop wallpaper can't bleed through).
                    t.bg.opacity(t.overlayOpacity)
                    // Top-edge sheen: lighter at the top, nothing at the bottom.
                    LinearGradient(
                        colors: [Color.white.opacity(0.06), Color.clear],
                        startPoint: .top, endPoint: .center)
                }
                .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                .opacity(Chrome.opacity)
            )
            .overlay(
                // Outer hairline — slightly darker than the inner highlight so
                // the window reads "raised" against whatever's behind it.
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.10), lineWidth: 0.5)
                    .opacity(Chrome.opacity)
            )
            .overlay(
                // Inset inner highlight — the "lit from above" glass rim.
                RoundedRectangle(cornerRadius: 13.5, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [Color.white.opacity(0.18),
                                     Color.white.opacity(0.04)],
                            startPoint: .top, endPoint: .bottom),
                        lineWidth: 0.5)
                    .padding(0.5)
                    .allowsHitTesting(false)
                    .opacity(Chrome.opacity)
            )
            // Soft drop shadow for the floating-panel feel.
            // In subtle mode the same shadow, small and tight, keeps the text readable on any wallpaper;
            // Match Desk uses Desk's drop shadow, or none when Desk's shadow switch is off.
            .shadow(color: .black.opacity(shadow.opacity), radius: shadow.radius, x: 0, y: shadow.y)
    }
}
