import SwiftUI

/// The widget with Claude and Codex on (agent sources, item 6): the combined bars on top, then a
/// section per source. Draws `WidgetSourceLayout` and nothing else; a Mac with only Claude on never
/// reaches this view (RootView keeps today's card stack). Every body is small for the CI type-checker.
struct WidgetSourcesView: View {
    let layout: WidgetSourceLayout
    let palette: Theme.Palette
    var vm: UsageViewModel
    var onToggle: (AgentSource) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(layout.combined, id: \.window.label) { bar in
                CombinedBarRow(bar: bar, palette: palette)
            }
            ForEach(layout.sections, id: \.source) { section in
                SourceSectionView(section: section, layout: layout, palette: palette, vm: vm, onToggle: onToggle)
            }
        }
    }
}

/// One combined bar: the window's label over the two-tone bar. No pace tick, glow or breath: each
/// source keeps those on its own card (Windows' CombinedBar draws the same bare bar).
struct CombinedBarRow: View {
    let bar: WidgetCombinedBar
    let palette: Theme.Palette

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(bar.window.label)
                .font(.app(size: 10, weight: .semibold, design: .rounded))
                .foregroundStyle(palette.textSecondary)
                .accessibilityHidden(true)
            ProgressBarView(utilization: bar.layout.primary?.percent ?? 0, paceFraction: nil,
                            palette: palette, sources: bar.layout)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 2)
    }
}

/// A source's header, then its cards, its empty line, or nothing while collapsed.
struct SourceSectionView: View {
    let section: WidgetSourceSection
    let layout: WidgetSourceLayout
    let palette: Theme.Palette
    var vm: UsageViewModel
    var onToggle: (AgentSource) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let header = section.header {
                SourceHeaderView(header: header, palette: palette) { onToggle(section.source) }
            }
            SourceSectionContent(section: section, layout: layout, palette: palette, vm: vm)
        }
    }
}

struct SourceSectionContent: View {
    let section: WidgetSourceSection
    let layout: WidgetSourceLayout
    let palette: Theme.Palette
    var vm: UsageViewModel

    var body: some View {
        switch section.content {
        case .collapsed:
            EmptyView()
        case .empty(let line):
            SourceEmptyLine(text: line, palette: palette)
        case .cards(let cards):
            if section.source == .claude {
                if !cards.isEmpty {
                    // Claude's cards exactly as the widget draws them alone, limit menu and local burn included.
                    WidgetCardStack(cards: WidgetCards.live(vm, rows: layout.claudeRows), palette: palette, vm: vm)
                }
            } else {
                CodexCardStack(cards: cards, palette: palette, tick: vm.countdownTick, pinDeepMath: vm.pacingPinned,
                               sparklineMode: SparklineView.mode(themeID: vm.theme.id))
            }
        }
    }
}

/// The header: the source's color dot and name, Codex's "as of", the summary while collapsed, and
/// the chevron. The whole row toggles.
struct SourceHeaderView: View {
    let header: WidgetSourceHeader
    let palette: Theme.Palette
    var onToggle: () -> Void

    var body: some View {
        Button(action: onToggle) {
            HStack(spacing: 6) {
                Circle().fill(Color.hex(header.colorHex)).frame(width: 7, height: 7)
                Text(header.name)
                    .font(.app(size: 11, weight: .semibold, design: .rounded))
                    .foregroundStyle(palette.text)
                if let asOf = header.asOf {
                    Text(asOf)
                        .font(.app(size: 9, design: palette.numericFontDesign))
                        .foregroundStyle(header.stale ? palette.textMuted : palette.textDim)
                        .lineLimit(1)
                }
                Spacer(minLength: 4)
                SourceHeaderTrailing(header: header, palette: palette)
            }
            .padding(.horizontal, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(header.asOf.map { "\(header.name), \($0)" } ?? header.name)
        .accessibilityHint((header.collapsed ? "Expand " : "Collapse ") + header.name)
        .accessibilityAddTraits(.isButton)
        .help((header.collapsed ? "Expand " : "Collapse ") + header.name)
    }
}

struct SourceHeaderTrailing: View {
    let header: WidgetSourceHeader
    let palette: Theme.Palette

    var body: some View {
        HStack(spacing: 6) {
            if header.collapsed && !header.summary.isEmpty {
                Text(header.summary)
                    .font(.app(size: 9, design: palette.numericFontDesign))
                    .foregroundStyle(palette.textDim)
                    .monospacedDigit()
                    .lineLimit(1)
            }
            Image(systemName: header.collapsed ? "chevron.right" : "chevron.down")
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(palette.textDim)
        }
    }
}

/// Codex's cards: the same meters, pace and burn as Claude's, dimmed when stale. No limit menu (its
/// items are Claude's), no local burn (that is Claude Code's), no sparkline yet.
struct CodexCardStack: View {
    let cards: [WidgetSourceCard]
    let palette: Theme.Palette
    let tick: Int
    let pinDeepMath: Bool
    let sparklineMode: SparklineView.Mode

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(cards, id: \.tier) { card in
                TierCardView(tier: card.tier, usage: card.usage, history: [], palette: palette, tick: tick,
                             pinDeepMath: pinDeepMath, sparklineMode: sparklineMode,
                             label: card.label, spokenLabel: card.spokenLabel, stale: card.stale)
                    .opacity(card.stale ? SourceBar.staleOpacity : 1)
            }
        }
    }
}

struct SourceEmptyLine: View {
    let text: String
    let palette: Theme.Palette

    var body: some View {
        Text(text)
            .font(.app(size: 11))
            .foregroundStyle(palette.textDim)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 4)
    }
}
