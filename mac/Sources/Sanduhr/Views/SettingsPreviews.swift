import SwiftUI
import AppKit

/// The widget, General and Integrations previews (item 68): the widget's cards (WidgetCardStack
/// in WidgetGlass), the menu bar item (MenuBarText and MenuBarItemLook), and Sanduhr's
/// statusline in a terminal (TerminalPreviewFrame) with a watcher as the Desk draws it.

// MARK: - Widget

/// The widget's cards in the chosen theme, font and subtle mode, with the pacing calculators
/// when pinned.
struct WidgetPreview: View {
    var vm: UsageViewModel
    var display = DisplaySettings.shared

    var body: some View {
        let t = vm.theme.palette
        let sample = vm.shownUsage == nil
        let cards = sample ? WidgetCards.sample(vm, usage: SurfacePreviewData.sampleUsage())
                           : WidgetCards.live(vm, rows: vm.visibleTiers())
        SettingsPreviewCard(kind: .widget, label: Self.spoken(cards, theme: vm.theme.displayName),
                            samples: sample ? .meters : [], maximum: 1.2) {
            WidgetCardStack(cards: cards, palette: t)
                .padding(10)
                .frame(width: 340)
                .modifier(WidgetGlass(palette: t, shadow: WidgetShadow.resolve(subtle: display.drawsSubtle, ink: t.ink)))
                .padding(16)
        }
    }

    static func spoken(_ cards: WidgetCards, theme: String) -> String {
        let rows = cards.rows.map { "\($0.tier.label) \(Int($0.usage.utilization ?? 0))%" }.joined(separator: ", ")
        return "The widget's cards in the \(theme) theme: \(rows.isEmpty ? "no limits yet" : rows)."
    }
}

// MARK: - Menu bar

/// The menu bar item as it will read: the hourglass and the percent, both readings in turn for
/// Rotate.
struct MenuBarPreview: View {
    var vm: UsageViewModel
    @AppStorage(MenuBarMode.key) private var mode = MenuBarMode.higher

    var body: some View {
        let sample = vm.usage == nil
        let readings = Self.readings(vm.usage ?? SurfacePreviewData.sampleUsage(), mode: mode)
        SettingsPreviewCard(kind: .menuBar, label: Self.spoken(readings, mode: mode),
                            samples: sample ? .meters : [], maximum: 2) {
            HStack(spacing: 12) {
                ForEach(Array(readings.enumerated()), id: \.offset) { i, reading in
                    if i > 0 {
                        Text("then").font(.system(size: 11)).foregroundStyle(.white.opacity(0.6))
                    }
                    MenuBarItemPreview(reading: reading)
                }
            }
            .padding(.horizontal, 14)
            .frame(height: 26)
            .background(Rectangle().fill(Color.black.opacity(0.3)))
        }
    }

    /// One reading, or Rotate's two (the session's, then the weekly limit's).
    static func readings(_ usage: UsageResponse?, mode: MenuBarMode) -> [MenuBarReading?] {
        let steps = mode == .rotate ? [0, 1] : [0]
        return steps.map { MenuBarText.reading(usage, mode: mode, step: $0) }
    }

    static func spoken(_ readings: [MenuBarReading?], mode: MenuBarMode) -> String {
        let texts = readings.compactMap { $0?.text }
        guard !texts.isEmpty else { return "The menu bar item: the hourglass alone, until Sanduhr has numbers." }
        return "The menu bar item: the hourglass with \(texts.joined(separator: ", then "))."
    }
}

/// The status item's button, drawn: the hourglass template and the styled text beside it.
private struct MenuBarItemPreview: View {
    let reading: MenuBarReading?

    var body: some View {
        HStack(spacing: 0) {
            Image(systemName: "hourglass")
                .font(.system(size: 13, weight: .regular))
                .foregroundStyle(.white)
            if let reading {
                let look = MenuBarItemLook(reading)
                Text(look.title)
                    .font(Font(MenuBarItemLook.font))
                    .foregroundStyle(look.urgency == .normal ? Color.white : Color(nsColor: look.color))
            }
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
    }
}

// MARK: - Integrations

/// Sanduhr's statusline, run once on the sample statusline input as Claude Code would run it,
/// in a terminal frame; and a watcher as the Desk draws it.
struct IntegrationsPreview: View {
    var model: IntegrationsModel
    @State private var statusline: String?
    @State private var ran = false

    var body: some View {
        SettingsPreviewCard(kind: .integrations,
                            label: "Sanduhr's statusline in a terminal, run on sample input.",
                            samples: .statusline) {
            TerminalPreviewFrame(statusline, title: "Claude Code",
                                 placeholder: Self.placeholder(loaded: model.loaded, ran: ran, python: model.pythonPath),
                                 maxLines: 3)
                .frame(width: 560)
                .padding(8)
        }
        .task(id: model.pythonPath) {
            guard model.pythonPath != nil else { return }
            statusline = await model.sampleStatusline()
            ran = true
        }
    }

    static func placeholder(loaded: Bool, ran: Bool, python: String?) -> String {
        if !loaded || (python != nil && !ran) { return "Running…" }
        return "No preview: Python or the scripts weren't found."
    }
}

/// Settings, Watchers (slice 2): a watcher as the Desk draws it, the user's own while one shows,
/// else a labeled sample (moved here from Claude Code's preview).
struct WatchersPreview: View {
    var live: DeskModel
    @State private var preview = DeskModel()
    @State private var samples: PreviewSamples = []

    var body: some View {
        SettingsPreviewCard(kind: .watchers, label: "A watcher as the Desk shows it.", samples: samples) {
            DeskPiece(widget: .watchers, model: preview, alignment: .leading)
                .padding(8)
        }
        .modifier(PreviewModelSync(preview: preview, live: live, samples: $samples))
    }
}
