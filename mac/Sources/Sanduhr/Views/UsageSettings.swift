import AppKit
import SwiftUI

/// The Claude Usage page's state (item 48), one per Settings window so a reopened page keeps its
/// tab, range, scope, sort and expanded rows. Reads run off the main thread through
/// `UsagePageLoader`; a newer read for the same tab drops an older one's answer.
@MainActor
@Observable
final class UsagePageModel {
    /// The account picked in the page's picker; nil follows the active account.
    var picked: String?
    private(set) var overview: UsageOverview?
    private(set) var trends: UsageTrends?
    var trendsWeeks = UsageTrends.defaultWeeks
    private(set) var rows: [LedgerRow] = []
    private(set) var sessionsLoaded = false
    private(set) var loadedAt = Date()
    var scope: LedgerScope = .default
    private(set) var sortColumn: LedgerColumn = .tokens
    private(set) var descending = true
    var expanded: Set<String> = []
    private(set) var records: [VaultRecordInfo] = []
    private(set) var recordsLoaded = false
    var note: (text: String, isError: Bool)?

    @ObservationIgnored private var reader: CCLogReader?
    @ObservationIgnored private var generations: [String: Int] = [:]
    /// The day a hot-day pass was last asked for, so it is asked once.
    @ObservationIgnored private var rolloverAsked: CCLocalDay?

    /// The account the page shows: the pick while it exists, else the active account.
    func label(_ vm: UsageViewModel) -> String? {
        if let picked, vm.accountLabels.contains(picked) { return picked }
        return vm.activeAccount ?? vm.accountLabels.first
    }

    /// One live reader, kept while the folder stays the same (its per-file cache makes the next
    /// read cheap); dropped when the page goes away.
    private func liveReader(_ folder: String?) -> CCLogReader? {
        guard let folder else {
            reader = nil
            return nil
        }
        if let r = reader, r.root == folder { return r }
        let r = CCLogReader(root: folder)
        reader = r
        return r
    }

    func dropReader() { reader = nil }

    private func begin(_ key: String) -> Int {
        let g = (generations[key] ?? 0) + 1
        generations[key] = g
        return g
    }

    private func current(_ key: String, _ g: Int) -> Bool { generations[key] == g && !Task.isCancelled }

    // MARK: Loads

    func loadOverview(_ vm: UsageViewModel, label: String) async {
        let choices = vm.dataChoices(for: label)
        let source = UsageSource.of(choices)
        let live = liveReader(source.folder)
        let vault = vm.vault.reader
        let names = choices.names
        let g = begin("overview")
        let result = await Task.detached(priority: .userInitiated) {
            UsagePageLoader.overview(source, names: names, live: live, vault: vault)
        }.value
        guard current("overview", g) else { return }
        overview = result
        // Just after midnight yesterday is still hot: ask for the pass that closes it, once.
        let today = CCLocalDay(Date(), calendar: .current)
        if let result, result.mode == .record, result.strip.filter(\.live).count > 1, rolloverAsked != today {
            rolloverAsked = today
            vm.vault.trigger()
        }
    }

    func loadTrends(_ vm: UsageViewModel, label: String) async {
        guard let id = UsageSource.of(vm.dataChoices(for: label)).recordID else {
            trends = nil
            return
        }
        let vault = vm.vault.reader
        let weeks = trendsWeeks
        let g = begin("trends")
        let result = await Task.detached(priority: .userInitiated) {
            UsagePageLoader.trends(recordID: id, weeks: weeks, vault: vault, today: CCLocalDay(Date(), calendar: .current))
        }.value
        guard current("trends", g) else { return }
        trends = result
    }

    func loadSessions(_ vm: UsageViewModel, label: String) async {
        guard let id = UsageSource.of(vm.dataChoices(for: label)).recordID else {
            rows = []
            sessionsLoaded = false
            return
        }
        let vault = vm.vault.reader
        let scope = self.scope
        let column = sortColumn
        let descending = self.descending
        let g = begin("sessions")
        let result = await Task.detached(priority: .userInitiated) { () -> [LedgerRow] in
            let today = CCLocalDay(Date(), calendar: .current)
            let rows = UsageLedger.rows(UsagePageLoader.sessions(recordID: id, vault: vault), scope: scope, today: today)
            return UsageLedger.sorted(rows, by: column, descending: descending)
        }.value
        guard current("sessions", g) else { return }
        // A chip or a sort during the read: apply it to the fresh rows.
        let fresh = scope == self.scope ? result : UsageLedger.rescoped(result, scope: self.scope, today: today())
        rows = column == sortColumn && descending == self.descending && scope == self.scope
            ? fresh : UsageLedger.sorted(fresh, by: sortColumn, descending: self.descending)
        let ids = Set(rows.map(\.id))
        expanded.formIntersection(ids)
        loadedAt = Date()
        sessionsLoaded = true
    }

    func loadRecords(_ vm: UsageViewModel) async {
        let vault = vm.vault
        let choices = vm.accountDataChoices
        let g = begin("records")
        let result = await Task.detached(priority: .utility) { vault.records(choices) }.value
        guard current("records", g) else { return }
        records = result
        recordsLoaded = true
    }

    // MARK: Ledger

    private func today() -> CCLocalDay { CCLocalDay(Date(), calendar: .current) }

    func setScope(_ s: LedgerScope) {
        guard s != scope else { return }
        scope = s
        rows = UsageLedger.sorted(UsageLedger.rescoped(rows, scope: s, today: today()),
                                  by: sortColumn, descending: descending)
    }

    func sort(by column: LedgerColumn) {
        if column == sortColumn {
            descending.toggle()
        } else {
            sortColumn = column
            descending = column.startsDescending
        }
        rows = UsageLedger.sorted(rows, by: sortColumn, descending: descending)
    }

    func toggle(_ id: String) {
        if expanded.contains(id) { expanded.remove(id) } else { expanded.insert(id) }
    }
}

/// Settings, Claude Usage (item 48): an account's Claude Code activity as Sanduhr reads it live
/// and keeps it in the record, with Overview, Trends and Sessions, and every record on this Mac
/// with its erase button.
///
/// **Why a Settings section.** Everything about an account's data already lives in Settings:
/// the choices that decide what this page can show are in Accounts, Data, one row up, and the
/// page sends people there when a choice is missing. Settings is the one window every menu,
/// Option+S and the notch already open, it resizes, and it keeps its state while open. A
/// separate window would be a second place to look for the same account's data. The widget's
/// Tools menu, Desk's menus and the menu bar menu reach it with Claude Usage….
struct UsageSettings: View {
    @Bindable var vm: UsageViewModel
    @Bindable var navigation: SettingsNavigation
    let theme: Theme.Palette

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let label = navigation.usagePage.label(vm) {
                UsageHeader(vm: vm, page: navigation.usagePage, navigation: navigation, label: label)
                UsageTabContent(vm: vm, page: navigation.usagePage, navigation: navigation,
                                label: label, accent: theme.accent)
            } else {
                Text("Add an account in Accounts first.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 0)
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .onDisappear { navigation.usagePage.dropReader() }
    }
}

/// The account picker (two or more accounts), the tabs and what the page reads for the account.
private struct UsageHeader: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @Bindable var navigation: SettingsNavigation
    let label: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 12) {
                if vm.accountLabels.count > 1 {
                    Picker("Account", selection: Binding(
                        get: { label },
                        set: { page.picked = $0 })) {
                        ForEach(vm.accountLabels, id: \.self) { Text($0).tag($0) }
                    }
                    .pickerStyle(.menu)
                    .fixedSize()
                }
                Spacer(minLength: 0)
                Picker("Tab", selection: $navigation.usageTab) {
                    ForEach(UsageTab.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
            }
            UsageCaption(sourceLine)
        }
    }

    private var sourceLine: String {
        let c = vm.dataChoices(for: label)
        switch UsageSource.of(c) {
        case .notTracked: return "Claude Code activity isn't tracked for \(label)."
        case .noFolder: return "No Claude Code folder is linked to \(label)."
        case .live(let folder):
            return "Live only, from \(ClaudeCodeFolders.Folder(path: folder).display(home: NSHomeDirectory())). Nothing is kept on this Mac."
        case .record(let folder, _):
            return "Kept as a record of \(ClaudeCodeFolders.Folder(path: folder).display(home: NSHomeDirectory())), on this Mac only."
        }
    }
}

/// The selected tab, with the reloads each one needs.
private struct UsageTabContent: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @Bindable var navigation: SettingsNavigation
    let label: String
    let accent: Color

    private var choices: AccountDataChoices { vm.dataChoices(for: label) }

    /// What a reload depends on: the account, its choices, a finished ingest or erase, a refresh.
    private var key: String {
        let c = choices
        return [label, c.activity.rawValue, c.names.rawValue, c.folder ?? "", String(vm.vaultCycles),
                String(vm.lastUpdated?.timeIntervalSince1970 ?? 0)].joined(separator: "|")
    }

    var body: some View {
        switch navigation.usageTab {
        case .overview:
            UsageOverviewTab(vm: vm, page: page, navigation: navigation, label: label, accent: accent)
                .task(id: key) {
                    // Today moves while the page is open: read again every minute.
                    while !Task.isCancelled {
                        await page.loadOverview(vm, label: label)
                        try? await Task.sleep(for: .seconds(60))
                    }
                }
        case .trends:
            UsageTrendsTab(vm: vm, page: page, navigation: navigation, label: label, accent: accent)
                .task(id: key + "|\(page.trendsWeeks)") { await page.loadTrends(vm, label: label) }
        case .sessions:
            UsageSessionsTab(vm: vm, page: page, navigation: navigation, label: label, accent: accent)
                .task(id: key) { await page.loadSessions(vm, label: label) }
        }
    }
}

// MARK: - Overview

private struct UsageOverviewTab: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @Bindable var navigation: SettingsNavigation
    let label: String
    let accent: Color

    private var source: UsageSource { UsageSource.of(vm.dataChoices(for: label)) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                switch source {
                case .notTracked, .noFolder:
                    UsageNeedsSetup(text: "Choose a Claude Code folder and Live only or Keep a record in \(label)'s Data settings to see its activity here.",
                                    label: label, navigation: navigation)
                case .live, .record:
                    if let o = page.overview {
                        UsageOverviewBody(overview: o, accent: accent, hidden: vm.dataChoices(for: label).names == .hidden)
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                Divider()
                UsageRecordsSection(vm: vm, page: page)
            }
            .padding(.trailing, 12)
            .frame(maxWidth: .infinity, alignment: .topLeading)
        }
    }
}

private struct UsageOverviewBody: View {
    let overview: UsageOverview
    let accent: Color
    let hidden: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let status = overview.status {
                Label(status, systemImage: overview.mode == .live ? "dot.radiowaves.left.and.right" : "pause.circle")
                    .font(.caption)
                    .foregroundStyle(overview.mode == .live ? Color.secondary : Color.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(alignment: .top, spacing: 24) {
                UsageStat(title: "Today", value: overview.today > 0 ? "\(TokenFormat.compact(overview.today)) tokens" : "No activity yet",
                          detail: overview.today > 0
                              ? "↑ \(TokenFormat.compact(overview.todayInput)) sent · ↓ \(TokenFormat.compact(overview.todayOutput)) received" : nil)
                UsageStat(title: "Last 30 days", value: overview.windowTotal > 0 ? "\(TokenFormat.compact(overview.windowTotal)) tokens" : "No activity",
                          detail: nil)
            }
            VStack(alignment: .leading, spacing: 4) {
                UsageStripChart(days: overview.strip, accent: accent)
                    .frame(height: 64)
                HStack {
                    Text(overview.strip.first.map { UsageDates.short($0.day) } ?? "")
                    Spacer()
                    Text("Today")
                }
                .font(.caption2)
                .foregroundStyle(.secondary)
                UsageLegend(noRecord: overview.strip.contains(where: \.noRecord), current: false)
            }
            HStack(alignment: .top, spacing: 24) {
                UsageRankedList(title: "Projects", items: overview.projects)
                UsageRankedList(title: "Skills", items: overview.skills)
            }
            if hidden || UsageProjectNames.anyHidden(overview.projects.map(\.name)) {
                UsageCaption(UsageProjectNames.hiddenCaption)
            }
        }
    }
}

private struct UsageStat: View {
    let title: String
    let value: String
    let detail: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.title3.monospacedDigit()).fontWeight(.semibold)
            if let detail { Text(detail).font(.caption2.monospacedDigit()).foregroundStyle(.secondary) }
        }
    }
}

/// A titled list of names and tokens.
struct UsageRankedList: View {
    let title: String
    let items: [UsageRanked]

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.headline)
            if items.isEmpty {
                Text("Nothing yet").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(items) { item in
                HStack {
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                    Spacer(minLength: 8)
                    Text(TokenFormat.compact(item.total)).monospacedDigit().foregroundStyle(.secondary)
                }
                .font(.callout)
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }
}

// MARK: - Records on this Mac

/// Every record on this Mac (all `vault/<id>` folders), linked or not, with size, oldest day,
/// the account it belongs to, Erase (a confirmation naming it) and Open in Finder.
private struct UsageRecordsSection: View {
    var vm: UsageViewModel
    @Bindable var page: UsagePageModel
    @State private var pendingErase: VaultRecordInfo?

    private var key: String {
        let links = vm.accountDataChoices.map { "\($0.key)=\($0.value.activity.rawValue):\($0.value.folder ?? "")" }.sorted()
        return "\(vm.vaultCycles)|" + links.joined(separator: ",")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Records on this Mac").font(.headline)
                Spacer()
                Button("Open Vault Folder") { openVault() }
                    .disabled(!FileManager.default.fileExists(atPath: vm.vault.store.vaultDir))
            }
            UsageCaption("Each record is a summary of one Claude Code folder's sessions (tokens by day, model and project, never what was written), kept until you erase it. A record whose folder is no longer linked stays until erased here.")
            if page.recordsLoaded && page.records.isEmpty {
                Text("No records on this Mac.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(page.records) { r in
                UsageRecordRow(record: r, home: NSHomeDirectory(),
                               erase: { pendingErase = r },
                               reveal: { reveal(r) })
            }
            if let note = page.note {
                Text(note.text)
                    .font(.caption)
                    .foregroundStyle(Color.hex(note.isError ? "f87171" : "4ade80"))
            }
        }
        .task(id: key) { await page.loadRecords(vm) }
        .confirmationDialog(pendingErase.map(eraseTitle) ?? "Erase this record?",
                            isPresented: Binding(get: { pendingErase != nil }, set: { if !$0 { pendingErase = nil } }),
                            titleVisibility: .visible, presenting: pendingErase) { r in
            Button("Erase Record", role: .destructive) {
                pendingErase = nil
                vm.eraseRecord(id: r.id) { ok in
                    page.note = ok ? ("Record erased.", false) : ("The record is being kept again, so it wasn't erased.", true)
                }
            }
            Button("Cancel", role: .cancel) { pendingErase = nil }
        } message: { r in
            Text(eraseMessage(r))
        }
    }

    private func eraseTitle(_ r: VaultRecordInfo) -> String {
        if let account = r.account { return "Erase \(account)'s Claude Code record?" }
        return "Erase the record \(r.shortID), not linked to an account?"
    }

    private func eraseMessage(_ r: VaultRecordInfo) -> String {
        var text = "Deletes \(VaultStewardship.sizeText(r.bytes)) of session summaries"
        if let day = r.oldestDay { text += " going back to \(UsageDates.long(day))" }
        if let folder = r.folder {
            text += ", the record of \(ClaudeCodeFolders.Folder(path: folder).display(home: NSHomeDirectory()))"
        }
        text += ". Claude Code's own logs stay as they are."
        if r.recording, let account = r.account {
            text += " \(account) switches from Keep a record to Live only first, so nothing is recorded again until you choose it."
        }
        return text
    }

    private func reveal(_ r: VaultRecordInfo) {
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: vm.vault.store.rootDir(r.id))])
    }

    private func openVault() {
        NSWorkspace.shared.open(URL(fileURLWithPath: vm.vault.store.vaultDir, isDirectory: true))
    }
}

private struct UsageRecordRow: View {
    let record: VaultRecordInfo
    let home: String
    let erase: () -> Void
    let reveal: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title).lineLimit(1)
                Text(details).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            Button("Show in Finder", action: reveal)
            Button("Erase…", role: .destructive, action: erase)
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.secondary.opacity(0.08)))
    }

    private var title: String {
        guard let account = record.account else { return "Not linked to an account (\(record.shortID))" }
        let folder = record.folder.map { ClaudeCodeFolders.Folder(path: $0).display(home: home) } ?? ""
        return "\(account) · \(folder)"
    }

    private var details: String {
        var parts = [VaultStewardship.sizeText(record.bytes)]
        if let day = record.oldestDay { parts.append("since \(UsageDates.long(day))") }
        if record.isLinked { parts.append(record.recording ? "recording" : "not recording") }
        return parts.joined(separator: " · ")
    }
}

// MARK: - Shared pieces

/// "Trends come from the record" and friends, with the way to the account's Data settings.
struct UsageNeedsSetup: View {
    let text: String
    let label: String
    @Bindable var navigation: SettingsNavigation

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(text)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            // Accounts, scrolled to this account's Data.
            SettingsLinkButton(.credentials) { navigation.accountToShow = label }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.08)))
    }
}

struct UsageCaption: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// The key to the textures: "no record" dots and the hatched week in progress.
struct UsageLegend: View {
    let noRecord: Bool
    let current: Bool

    var body: some View {
        HStack(spacing: 14) {
            if current {
                legendItem("Week in progress") { ctx, rect in UsageTexture.hatch(ctx, rect, color: .accentColor) }
            }
            if noRecord {
                legendItem("No record") { ctx, rect in UsageTexture.dots(ctx, rect, color: .secondary) }
            }
        }
        .font(.caption2)
        .foregroundStyle(.secondary)
    }

    private func legendItem(_ title: String, draw: @escaping (GraphicsContext, CGRect) -> Void) -> some View {
        HStack(spacing: 4) {
            Canvas { ctx, size in draw(ctx, CGRect(origin: .zero, size: size)) }
                .frame(width: 14, height: 10)
            Text(title)
        }
    }
}

/// The textures the charts share.
enum UsageTexture {
    /// "No record": a grid of small dots, never a bar.
    static func dots(_ ctx: GraphicsContext, _ rect: CGRect, color: Color) {
        let step: CGFloat = 4
        var y = rect.minY + 1
        while y < rect.maxY {
            var x = rect.minX + 1
            while x < rect.maxX {
                ctx.fill(Path(ellipseIn: CGRect(x: x, y: y, width: 1.6, height: 1.6)), with: .color(color.opacity(0.7)))
                x += step
            }
            y += step
        }
    }

    /// The week in progress: a pale fill with diagonal strokes.
    static func hatch(_ ctx: GraphicsContext, _ rect: CGRect, color: Color) {
        ctx.fill(Path(rect), with: .color(color.opacity(0.25)))
        var clipped = ctx
        clipped.clip(to: Path(rect))
        var lines = Path()
        var x = rect.minX - rect.height
        while x < rect.maxX {
            lines.move(to: CGPoint(x: x, y: rect.maxY))
            lines.addLine(to: CGPoint(x: x + rect.height, y: rect.minY))
            x += 5
        }
        clipped.stroke(lines, with: .color(color), lineWidth: 1.2)
    }
}

/// The Overview's 30 days: a bar per day, today brightest, "no record" days textured.
struct UsageStripChart: View {
    let days: [UsageOverview.Day]
    let accent: Color

    static let gap: CGFloat = 2

    var body: some View {
        Canvas { ctx, size in draw(ctx, size) }
            .overlay { tips }
            .accessibilityElement()
            .accessibilityLabel("Claude Code tokens, last 30 days")
            .accessibilityValue(days.last.map { "Today \(TokenFormat.compact($0.tokens))" } ?? "")
    }

    private func draw(_ ctx: GraphicsContext, _ size: CGSize) {
        let n = CGFloat(days.count)
        guard n > 0 else { return }
        let width = max(2, (size.width - Self.gap * (n - 1)) / n)
        let peak = CGFloat(max(1, days.map(\.tokens).max() ?? 1))
        for (i, d) in days.enumerated() {
            let x = CGFloat(i) * (width + Self.gap)
            if d.noRecord {
                UsageTexture.dots(ctx, CGRect(x: x, y: size.height - 14, width: width, height: 14), color: .secondary)
            } else if d.tokens == 0 {
                ctx.fill(Path(CGRect(x: x, y: size.height - 1, width: width, height: 1)), with: .color(.secondary.opacity(0.35)))
            } else {
                let h = max(3, CGFloat(d.tokens) / peak * (size.height - 2))
                let rect = CGRect(x: x, y: size.height - h, width: width, height: h)
                let opacity: Double = i == days.count - 1 ? 1 : 0.7
                ctx.fill(Path(roundedRect: rect, cornerRadius: 1.5), with: .color(accent.opacity(opacity)))
            }
        }
    }

    private var tips: some View {
        HStack(spacing: Self.gap) {
            ForEach(days) { d in
                Color.clear
                    .contentShape(Rectangle())
                    .help("\(UsageDates.short(d.day)): \(d.noRecord ? "no record" : TokenFormat.compact(d.tokens))")
            }
        }
    }
}
