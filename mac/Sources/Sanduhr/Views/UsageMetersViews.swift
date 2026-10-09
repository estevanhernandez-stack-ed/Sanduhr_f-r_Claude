import AppKit
import SwiftUI

/// Settings, Claude Usage, Meters (item 37): the meter history behind the sparklines as a chart per
/// limit, over a week or a month, for the picked account or every account at once (one color
/// each, the legend above), and Export CSV of every reading kept. Windows 2.2's History tab.
struct UsageMetersTab: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    let label: String
    let accent: Color

    @State private var exportNote: (text: String, isError: Bool)?

    private var historyOff: Bool { !page.metersAll && vm.historyOffAccounts.contains(label) }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            toolbar
            if page.metersAll, vm.accountLabels.count > 1 { legend }
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    ForEach(page.meterRows) { row in
                        MeterHistoryRowView(row: row, window: page.metersWindow, overlay: page.metersAll,
                                            accounts: vm.accountLabels, accent: accent)
                    }
                    if page.metersLoaded && page.meterRows.isEmpty {
                        UsageCaption(emptyLine)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            footer
        }
    }

    private var toolbar: some View {
        HStack(spacing: 10) {
            Picker("Window", selection: $page.metersWindow) {
                ForEach(MeterHistoryChart.Window.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            if vm.accountLabels.count > 1 {
                Toggle("All accounts", isOn: $page.metersAll)
                    .toggleStyle(.checkbox)
                    .help("Draw every account's line on each chart, one color per account.")
            }
            Spacer(minLength: 0)
            Button("Export CSV…") { export() }
                .disabled(page.meterHistories.values.allSatisfy { $0.values.allSatisfy(\.isEmpty) })
                .help(page.metersAll ? "Every reading kept for every account, with an account column."
                                     : "Every reading kept for \(label).")
        }
    }

    private var legend: some View {
        HStack(spacing: 12) {
            ForEach(vm.accountLabels, id: \.self) { account in
                HStack(spacing: 5) {
                    Circle()
                        .fill(Color.hex(MeterHistoryChart.color(for: account, in: vm.accountLabels)))
                        .frame(width: 9, height: 9)
                    Text(account).font(.caption).foregroundStyle(.secondary)
                }
                .accessibilityElement(children: .combine)
            }
        }
    }

    private var emptyLine: String {
        if historyOff { return "Meter history is off for \(label). Turn it on in Accounts, Data." }
        return "No readings in the last \(page.metersWindow.days) days yet. Each refresh adds one while Meter history is on."
    }

    @ViewBuilder
    private var footer: some View {
        UsageCaption("The readings behind the sparklines, kept on this Mac for \(MeterHistory.days) days and never uploaded. Export CSV to look at them in a spreadsheet or with any agent.")
        if let exportNote {
            Text(exportNote.text)
                .font(.caption)
                .foregroundStyle(Color.hex(exportNote.isError ? "f87171" : "4ade80"))
        }
    }

    /// Export CSV: written only to the file the user picks.
    private func export() {
        let account = page.metersAll ? nil : label
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = MeterHistoryChart.exportName(account: account, day: Date())
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let built = MeterHistoryChart.csv(histories: page.meterHistories, accounts: page.meterAccounts, account: account)
        do {
            try built.text.write(to: url, atomically: true, encoding: .utf8)
            exportNote = ("Wrote \(built.rowCount) readings.", false)
        } catch {
            exportNote = ("Could not write the file. Is it open elsewhere?", true)
        }
    }
}

/// One limit: its name and latest reading, then the lines over the window, 0 to 100%.
private struct MeterHistoryRowView: View {
    let row: MeterHistoryChart.Row
    let window: MeterHistoryChart.Window
    let overlay: Bool
    let accounts: [String]
    let accent: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(row.title).font(.subheadline.weight(.semibold))
                Spacer(minLength: 8)
                if !overlay, let latest = row.series.first.flatMap(MeterHistoryChart.latest) {
                    Text("\(Int(latest.rounded()))% now").font(.caption).foregroundStyle(.secondary).monospacedDigit()
                }
            }
            MeterHistoryLines(series: row.series, window: window, colors: colors)
                .frame(height: 64)
                .accessibilityElement()
                .accessibilityLabel("\(row.title), last \(window.days) days")
                .accessibilityValue(spoken)
        }
    }

    private var colors: [String: Color] {
        Dictionary(uniqueKeysWithValues: row.series.map { s in
            (s.account, overlay ? Color.hex(MeterHistoryChart.color(for: s.account, in: accounts)) : accent)
        })
    }

    private var spoken: String {
        row.series.map { s in
            let values = s.points.map(\.value)
            let latest = Int((values.last ?? 0).rounded()), peak = Int((values.max() ?? 0).rounded())
            return (overlay ? "\(s.account): " : "") + "\(latest)% now, peak \(peak)%"
        }.joined(separator: "; ")
    }
}

/// The lines: time across the window ending now, utilization up; faint lines at 50% and 100%.
private struct MeterHistoryLines: View {
    let series: [MeterHistoryChart.Series]
    let window: MeterHistoryChart.Window
    let colors: [String: Color]

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            Canvas { ctx, size in draw(ctx, size, now: context.date) }
        }
    }

    private func draw(_ ctx: GraphicsContext, _ size: CGSize, now: Date) {
        let start = now.addingTimeInterval(-TimeInterval(window.days) * 86_400)
        let span = now.timeIntervalSince(start)
        let inset: CGFloat = 2
        let h = size.height - inset * 2
        func y(_ v: Double) -> CGFloat { inset + h - CGFloat(v / 100) * h }
        for level in [50.0, 100.0] {
            var grid = Path()
            grid.move(to: CGPoint(x: 0, y: y(level)))
            grid.addLine(to: CGPoint(x: size.width, y: y(level)))
            ctx.stroke(grid, with: .color(.secondary.opacity(level == 100 ? 0.25 : 0.15)),
                       style: StrokeStyle(lineWidth: 0.5, dash: level == 50 ? [3, 3] : []))
        }
        var base = Path()
        base.move(to: CGPoint(x: 0, y: y(0)))
        base.addLine(to: CGPoint(x: size.width, y: y(0)))
        ctx.stroke(base, with: .color(.secondary.opacity(0.3)), lineWidth: 0.5)
        for s in series {
            let color = colors[s.account] ?? .accentColor
            var line = Path()
            for (i, p) in s.points.enumerated() {
                let pt = CGPoint(x: CGFloat(p.date.timeIntervalSince(start) / span) * size.width, y: y(p.value))
                if i == 0 { line.move(to: pt) } else { line.addLine(to: pt) }
            }
            ctx.stroke(line, with: .color(color), style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
            if let last = s.points.last {
                let x = CGFloat(last.date.timeIntervalSince(start) / span) * size.width
                ctx.fill(Path(ellipseIn: CGRect(x: x - 2.5, y: y(last.value) - 2.5, width: 5, height: 5)), with: .color(color))
            }
        }
    }
}
