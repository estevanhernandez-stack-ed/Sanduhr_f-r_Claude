import AppKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Trends

/// Trends (item 48): weekly totals and top projects over 4, 12 or 26 weeks, from the record.
struct UsageTrendsTab: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @Bindable var navigation: SettingsNavigation
    let label: String
    let accent: Color

    var body: some View {
        if UsageSource.of(vm.dataChoices(for: label)).recordID == nil {
            UsageNeedsRecord(what: "Trends", label: label, navigation: navigation)
        } else {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Picker("Weeks", selection: $page.trendsWeeks) {
                        ForEach(UsageTrends.ranges, id: \.self) { Text("\($0) weeks").tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .fixedSize()
                    if let t = page.trends {
                        UsageTrendsBody(trends: t, accent: accent,
                                        hidden: vm.dataChoices(for: label).names == .hidden)
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                .padding(.trailing, 12)
                .frame(maxWidth: .infinity, alignment: .topLeading)
            }
        }
    }
}

private struct UsageTrendsBody: View {
    let trends: UsageTrends
    let accent: Color
    let hidden: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let note = trends.freshNote { UsageCaption(note) }
            if trends.bars.isEmpty {
                Text("No record yet.").font(.caption).foregroundStyle(.secondary)
            } else {
                UsageWeeksChart(bars: trends.bars, accent: accent)
                    .frame(height: 150)
                HStack {
                    Text(trends.bars.first.map { UsageDates.short($0.weekStart) } ?? "")
                    Spacer()
                    Text(trends.bars.last.map { "Week of \(UsageDates.short($0.weekStart))" } ?? "")
                }
                .font(.caption2)
                .foregroundStyle(.secondary)
                UsageLegend(noRecord: trends.bars.contains { $0.kind == .noRecord || $0.partialGap },
                            current: trends.bars.contains(where: \.isCurrent))
            }
            UsageRankedList(title: "Top projects", items: trends.top)
            if hidden || UsageProjectNames.anyHidden(trends.top.map(\.name)) {
                UsageCaption(UsageProjectNames.hiddenCaption)
            }
            if let footer = trends.footer {
                Text(footer).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

/// Weekly bars. A week without a record is the dot texture at a fixed short height, never a
/// zero bar; a covered empty week is a hairline; the current week is hatched; a week with tokens
/// and some day uncovered gets a dotted edge under its bar.
struct UsageWeeksChart: View {
    let bars: [UsageTrends.Bar]
    let accent: Color

    static let gap: CGFloat = 4
    static let edge: CGFloat = 8

    var body: some View {
        Canvas { ctx, size in draw(ctx, size) }
            .overlay { tips }
            .accessibilityElement()
            .accessibilityLabel("Claude Code tokens by week")
            .accessibilityValue(bars.map(spoken).joined(separator: ", "))
    }

    private func spoken(_ b: UsageTrends.Bar) -> String {
        let when = "week of \(UsageDates.short(b.weekStart))"
        switch b.kind {
        case .noRecord: return "\(when): no record"
        case .zero: return "\(when): none"
        case .bar: return "\(when): \(TokenFormat.compact(b.total))\(b.isCurrent ? ", in progress" : "")"
        }
    }

    private func draw(_ ctx: GraphicsContext, _ size: CGSize) {
        let n = CGFloat(bars.count)
        guard n > 0 else { return }
        let width = max(4, (size.width - Self.gap * (n - 1)) / n)
        let base = size.height - Self.edge
        let peak = CGFloat(max(1, bars.map(\.total).max() ?? 1))
        for (i, b) in bars.enumerated() {
            let x = CGFloat(i) * (width + Self.gap)
            switch b.kind {
            case .noRecord:
                UsageTexture.dots(ctx, CGRect(x: x, y: base - 16, width: width, height: 16), color: .secondary)
            case .zero:
                ctx.fill(Path(CGRect(x: x, y: base - 1, width: width, height: 1)), with: .color(.secondary.opacity(0.4)))
            case .bar:
                let h = max(3, CGFloat(b.total) / peak * (base - 4))
                let rect = CGRect(x: x, y: base - h, width: width, height: h)
                if b.isCurrent {
                    UsageTexture.hatch(ctx, rect, color: accent)
                } else {
                    ctx.fill(Path(roundedRect: rect, cornerRadius: 2), with: .color(accent))
                }
                if b.partialGap {
                    UsageTexture.dots(ctx, CGRect(x: x, y: base + 2, width: width, height: Self.edge - 2), color: .secondary)
                }
            }
        }
    }

    private var tips: some View {
        HStack(spacing: Self.gap) {
            ForEach(bars) { b in
                Color.clear.contentShape(Rectangle()).help(spoken(b))
            }
        }
    }
}

// MARK: - Sessions

/// Sessions (item 48): the ledger, one row per logical session, scoped tokens, sort, row
/// expansion and Export CSV. The list owns its scrolling (no enclosing scroll view) and rows are
/// keyed by `root|uuid`, so a reload keeps the scroll position and the expanded rows.
struct UsageSessionsTab: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @Bindable var navigation: SettingsNavigation
    let label: String
    let accent: Color

    @State private var exportNote: (text: String, isError: Bool)?

    private var choices: AccountDataChoices { vm.dataChoices(for: label) }

    var body: some View {
        if UsageSource.of(choices).recordID == nil {
            UsageNeedsRecord(what: "Sessions", label: label, navigation: navigation)
        } else {
            VStack(alignment: .leading, spacing: 8) {
                toolbar
                LedgerHeader(page: page)
                LedgerList(page: page, recordTitle: recordTitle)
                footer
            }
        }
    }

    private var recordTitle: String {
        choices.folder.map(UsagePageLoader.recordTitle) ?? ""
    }

    private var toolbar: some View {
        HStack(spacing: 10) {
            Picker("Scope", selection: Binding(get: { page.scope }, set: { page.setScope($0) })) {
                ForEach(LedgerScope.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            Spacer(minLength: 0)
            Button("Export CSV…") { export() }
                .disabled(page.rows.isEmpty)
        }
    }

    @ViewBuilder
    private var footer: some View {
        if page.sessionsLoaded && page.rows.isEmpty {
            UsageCaption("No sessions recorded yet. The first pass lands within a minute of choosing Keep a record.")
        } else {
            let inScope = page.rows.reduce(0) { $0 + ($1.scoped > 0 ? 1 : 0) }
            UsageCaption("\(page.rows.count) sessions, \(inScope) with tokens in \(page.scope == .all ? "the record" : page.scope.title). Times are wall clock; tokens are input plus output.")
        }
        if choices.names == .hidden || UsageProjectNames.anyHidden(page.rows.prefix(200).map(\.session.projectName)) {
            UsageCaption(UsageProjectNames.hiddenCaption)
        }
        if let exportNote {
            Text(exportNote.text)
                .font(.caption)
                .foregroundStyle(Color.hex(exportNote.isError ? "f87171" : "4ade80"))
        }
    }

    /// Export CSV: the rows in the order shown, through the ledger port, to a file the user
    /// picks. Written only there; nothing is exported on its own.
    private func export() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = UsageLedger.exportName(CCLocalDay(Date(), calendar: .current))
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let built = UsageLedger.csv(page.rows, recordTitle: recordTitle)
        do {
            try built.text.write(to: url, atomically: true, encoding: .utf8)
            exportNote = ("Wrote \(built.rowCount) session rows.", false)
        } catch {
            exportNote = ("Could not write the file. Is it open elsewhere?", true)
        }
    }
}

/// The sort header: Last active, Project and Tokens sort (the direction by glyph); Models
/// doesn't.
private struct LedgerHeader: View {
    @Bindable var page: UsagePageModel

    var body: some View {
        HStack(spacing: 8) {
            sortButton(.lastActive).frame(width: LedgerLayout.lastActive, alignment: .leading)
            sortButton(.project).frame(maxWidth: .infinity, alignment: .leading)
            Text("Models").frame(width: LedgerLayout.models, alignment: .leading)
            sortButton(.tokens).frame(width: LedgerLayout.tokens, alignment: .trailing)
            Spacer().frame(width: LedgerLayout.chevron)
        }
        .font(.caption.weight(.semibold))
        .foregroundStyle(.secondary)
        .padding(.horizontal, LedgerLayout.inset)
    }

    private func sortButton(_ column: LedgerColumn) -> some View {
        Button(UsageLedger.header(column, sortedBy: page.sortColumn, descending: page.descending, scope: page.scope)) {
            page.sort(by: column)
        }
        .buttonStyle(.plain)
        .lineLimit(1)
    }
}

enum LedgerLayout {
    static let lastActive: CGFloat = 76
    static let models: CGFloat = 128
    static let tokens: CGFloat = 86
    static let chevron: CGFloat = 12
    static let inset: CGFloat = 8
}

private struct LedgerList: View {
    @Bindable var page: UsagePageModel
    let recordTitle: String

    var body: some View {
        let now = page.loadedAt
        List(page.rows) { row in
            LedgerRowView(row: row, expanded: page.expanded.contains(row.id), now: now, recordTitle: recordTitle) {
                page.toggle(row.id)
            }
            .equatable()
        }
        .listStyle(.plain)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// One session. Equatable, so a reload redraws only rows whose numbers changed; the expansion's
/// lines are built only while it is open.
private struct LedgerRowView: View, Equatable {
    let row: LedgerRow
    let expanded: Bool
    let now: Date
    let recordTitle: String
    let toggle: () -> Void

    static func == (a: LedgerRowView, b: LedgerRowView) -> Bool {
        a.row == b.row && a.expanded == b.expanded && a.now == b.now && a.recordTitle == b.recordTitle
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Button(action: toggle) { summary }
                .buttonStyle(.plain)
                .accessibilityLabel("\(row.projectText), \(UsageLedger.tokensText(row.scoped)) tokens, \(UsageLedger.relative(row.session.lastTs, now: now))")
                .accessibilityHint(expanded ? "Hides the session's details" : "Shows the session's details")
            if expanded {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(Array(UsageLedger.detail(row.session, recordTitle: recordTitle).enumerated()), id: \.offset) { _, line in
                        Text(line)
                    }
                }
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
                .padding(.leading, LedgerLayout.lastActive + 8)
                .padding(.bottom, 4)
            }
        }
    }

    private var summary: some View {
        HStack(spacing: 8) {
            Text(UsageLedger.relative(row.session.lastTs, now: now))
                .foregroundStyle(.secondary)
                .frame(width: LedgerLayout.lastActive, alignment: .leading)
            Text(row.projectText)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: .infinity, alignment: .leading)
            Text(row.badge)
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .frame(width: LedgerLayout.models, alignment: .leading)
            Text(UsageLedger.tokensText(row.scoped))
                .monospacedDigit()
                .foregroundStyle(row.scoped > 0 ? Color.primary : Color.secondary)
                .frame(width: LedgerLayout.tokens, alignment: .trailing)
            Image(systemName: expanded ? "chevron.down" : "chevron.right")
                .font(.caption2)
                .foregroundStyle(.secondary)
                .frame(width: LedgerLayout.chevron)
        }
        .font(.callout)
        .contentShape(Rectangle())
    }
}

/// Trends and Sessions need a record: what to choose, and the way there.
private struct UsageNeedsRecord: View {
    let what: String
    let label: String
    @Bindable var navigation: SettingsNavigation

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            UsageNeedsSetup(text: "\(what) come from the record Sanduhr keeps on this Mac. Choose Keep a record in \(label)'s Data settings (with a Claude Code folder linked) to start one; the first pass keeps about four weeks from Claude Code's logs.",
                            label: label, navigation: navigation)
            Spacer(minLength: 0)
        }
    }
}
