import SwiftUI

/// By Seat's eight sections (item 71, slice 1b), each with a one-line summary that shows while
/// it is collapsed, so the page reads as a health report before anything opens.
enum SeatSectionID: String, CaseIterable, Sendable {
    case settings, instructions, extensions, mcp, history, housekeeping, seat, drift

    var title: String {
        switch self {
        case .settings: "Settings"
        case .instructions: "Instructions"
        case .extensions: "Extensions"
        case .mcp: "MCP servers"
        case .history: "History"
        case .housekeeping: "Housekeeping"
        case .seat: "Seat"
        case .drift: "Drift between seats"
        }
    }

    /// The collapsed line, and whether it is a warning. Counts and words only, never a value.
    func summary(_ r: SeatReport, others: Int) -> (text: String, warns: Bool) {
        switch self {
        case .settings: SeatSummary.settings(r.settings)
        case .instructions: SeatSummary.instructions(r.instructions)
        case .extensions: SeatSummary.extensions(r.extensions)
        case .mcp: SeatSummary.mcp(r.mcp)
        case .history: SeatSummary.history(r.history)
        case .housekeeping: SeatSummary.housekeeping(r.housekeeping)
        case .seat: SeatSummary.repo(r.repo)
        case .drift: SeatSummary.drift(r.drift, others: others)
        }
    }
}

/// The sections' one-line summaries.
enum SeatSummary {
    static func plural(_ n: Int, _ one: String, _ many: String? = nil) -> String {
        "\(n) " + (n == 1 ? one : (many ?? one + "s"))
    }

    static func settings(_ p: SeatReport.SettingsPart) -> (text: String, warns: Bool) {
        if let why = p.unreadable { return ("settings.json is unreadable: it \(why).", true) }
        let w: Int = p.warningCount
        let head: String = plural(p.rows.count, "key") + " in effect"
        return (w > 0 ? head + ", \(w) to look at." : head + ".", w > 0)
    }

    static func instructions(_ p: SeatReport.InstructionsPart) -> (text: String, warns: Bool) {
        let bad: Int = p.unresolved
        let over: Int = p.memory.filter(\.overLimit).count
        var parts: [String] = [p.nodes.isEmpty ? "No CLAUDE.md or rules" : plural(p.nodes.count, "file") + " load"]
        if bad > 0 { parts.append(plural(bad, "import") + " don't resolve") }
        if over > 0 { parts.append(plural(over, "memory index", "memory indexes") + " over the limit") }
        if p.leak != nil { parts.append("~/.claude/CLAUDE.md leaks in") }
        return (parts.joined(separator: ", ") + ".", bad > 0 || over > 0 || p.leak != nil)
    }

    static func extensions(_ e: SeatReport.ExtensionsPart) -> (text: String, warns: Bool) {
        func n(_ k: CatalogKind) -> Int { e.rows.filter { $0.kind == k }.count }
        let parts: [String] = [plural(n(.agent), "agent"), plural(n(.skill), "skill"), plural(n(.command), "command"),
                               plural(n(.outputStyle), "output style"), plural(e.plugins.count, "plugin"), plural(e.hooks.count, "hook")]
        let head: String = parts.joined(separator: ", ")
        return (e.parity.isEmpty ? head + "." : head + ", \(e.parity.count) to look at.", !e.parity.isEmpty)
    }

    static func mcp(_ rows: [SeatMCPRow]) -> (text: String, warns: Bool) {
        let shadowed: Int = rows.filter { $0.shadowedBy != nil }.count
        let head: String = plural(rows.count, "server")
        return (shadowed > 0 ? head + ", \(shadowed) shadowed." : head + ".", shadowed > 0)
    }

    static func history(_ h: SeatReport.HistoryPart) -> (text: String, warns: Bool) {
        let size: String = ByteCountFormatter.string(fromByteCount: h.transcriptBytes, countStyle: .file)
        if h.warns { return ("Transcripts older than 30 days are deleted at the next start. \(size) kept.", true) }
        return ("Kept \(h.cleanupDays ?? 30) days. \(size) of transcripts.", false)
    }

    static func housekeeping(_ items: [SeatReport.Leftover]) -> (text: String, warns: Bool) {
        (items.isEmpty ? "Nothing left over." : plural(items.count, "leftover") + ".", !items.isEmpty)
    }

    static func repo(_ r: SeatReport.RepoPart) -> (text: String, warns: Bool) {
        var parts: [String] = [r.model.title]
        if r.killSwitch { parts.append("sync switched off") }
        if case .unknownSchema = r.sync { parts.append("state.json in an unknown schema") }
        if r.remoteMatches == false { parts.append("its origin doesn't match the seat file") }
        return (parts.joined(separator: ", ") + ".", r.remoteMatches == false)
    }

    static func drift(_ d: [SeatReport.Drift], others: Int) -> (text: String, warns: Bool) {
        if others == 0 { return ("No other seat to compare.", false) }
        let n: Int = d.reduce(0) { $0 + $1.count }
        return (n == 0 ? "Same as the other seats." : plural(n, "difference") + " with " + plural(others, "other seat") + ".", false)
    }
}

/// By Seat (item 71, slice 1b): the seat picker, the seat's header and its eight read-only
/// sections. Nothing here writes to a seat; the label and mark are Sanduhr preferences.
struct ModsBySeatView: View {
    var model: ModsPageModel
    let demo: Bool
    let linked: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if !model.loaded {
                Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
            } else if let seat = model.shownSeat(demo: demo) {
                SeatPicker(model: model, demo: demo)
                SeatHeader(seat: seat, model: model, linked: linked)
                    .id(ModsPageModel.seatAnchor(seat.path))
                if let report = model.report(for: seat.path) {
                    SeatSections(seat: seat, report: report, model: model, demo: demo)
                }
                SeatStatuslineBox(folder: seat.path, folderName: model.display(seat.path))
                    .id(seat.path)
            } else {
                Text("No Claude Code folder found (~/.claude or a ~/.claude-name folder).")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// One chip per seat: label, Work or Personal badge, and a dot while Claude Code runs in it.
private struct SeatPicker: View {
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        let shown = model.shownSeat(demo: demo)?.path
        ChipFlow(spacing: 6) {
            ForEach(model.pickerSeats(demo: demo)) { seat in
                Button { model.selectedSeat = seat.path } label: {
                    SeatPickerChip(seat: seat, selected: seat.path == shown, running: model.running.contains(seat.path))
                }
                .buttonStyle(.plain)
                .accessibilityLabel("\(seat.label), \(SeatMarkBadge(mark: seat.mark).title)\(model.running.contains(seat.path) ? ", Claude Code running" : "")")
            }
        }
    }
}

private struct SeatPickerChip: View {
    let seat: Seat
    let selected: Bool
    let running: Bool

    var body: some View {
        HStack(spacing: 5) {
            if running {
                Circle().fill(Color.hex("4ade80")).frame(width: 7, height: 7).help("Claude Code is running in this seat.")
            }
            Text(seat.label).font(.callout.weight(selected ? .semibold : .regular))
            SeatMarkBadge(mark: seat.mark)
        }
        .padding(.horizontal, 9)
        .padding(.vertical, 4)
        .background(Capsule().fill(Color.accentColor.opacity(selected ? 0.22 : 0.0)))
        .overlay(Capsule().strokeBorder(Color.secondary.opacity(selected ? 0 : 0.35)))
        .contentShape(Capsule())
    }
}

/// The seat's folder, account, install model, Claude Code version, whether Sanduhr's own
/// `CLAUDE_CONFIG_DIR` names it, its label and mark, and Forget Folder for one added by hand.
private struct SeatHeader: View {
    let seat: Seat
    var model: ModsPageModel
    let linked: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(model.display(seat.path)).font(.body.monospaced()).textSelection(.enabled)
                if model.addedFolders.contains(seat.path) {
                    Button("Forget Folder") { Task { await model.forgetFolder(seat.path, linked: linked) } }
                        .controlSize(.small)
                        .help("Stop listing this folder here. Nothing in the folder changes.")
                }
            }
            Text(facts).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if let note = model.configNotes[seat.path] {
                Text(note).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            SeatLabelControls(seat: seat, model: model)
        }
    }

    private var facts: String {
        var parts: [String] = []
        parts.append(model.linkedAccount(seat.path).map { "Account \($0)" } ?? "No account linked")
        parts.append(model.report(for: seat.path)?.repo.model.title ?? "")
        parts.append(model.claudeVersion.map { "Claude Code \($0) on PATH" } ?? "Claude Code version not known (Sanduhr doesn't run claude to ask)")
        if model.environmentSeat == seat.path { parts.append("CLAUDE_CONFIG_DIR names it for apps Sanduhr starts") }
        if seat.isDefault { parts.append("Claude Code's default folder") }
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }
}

/// The eight sections, each a disclosure with its summary line.
private struct SeatSections: View {
    let seat: Seat
    let report: SeatReport
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        let others = model.pickerSeats(demo: demo).filter { $0.path != seat.path }.count
        VStack(alignment: .leading, spacing: 8) {
            ForEach(SeatSectionID.allCases, id: \.self) { id in
                SeatSectionBox(id: id, summary: id.summary(model.shownReport(report, demo: demo), others: others), model: model) {
                    SeatSectionBody(id: id, seat: seat, report: report, model: model, demo: demo)
                }
            }
        }
    }
}

private struct SeatSectionBox<Content: View>: View {
    let id: SeatSectionID
    let summary: (text: String, warns: Bool)
    var model: ModsPageModel
    @ViewBuilder let content: () -> Content

    var body: some View {
        DisclosureGroup(isExpanded: expanded) {
            content()
                .padding(.top, 6)
                .frame(maxWidth: .infinity, alignment: .leading)
        } label: {
            VStack(alignment: .leading, spacing: 1) {
                Text(id.title).font(.body.weight(.semibold))
                Text(summary.text)
                    .font(.caption)
                    .foregroundStyle(summary.warns ? Color.orange : Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.secondary.opacity(0.07)))
    }

    private var expanded: Binding<Bool> {
        Binding(get: { model.openSections.contains(id.rawValue) },
                set: { open in
                    if open { model.openSections.insert(id.rawValue) } else { model.openSections.remove(id.rawValue) }
                })
    }
}

/// Which section's content to draw.
private struct SeatSectionBody: View {
    let id: SeatSectionID
    let seat: Seat
    let report: SeatReport
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        switch id {
        case .settings: SeatSettingsSection(part: report.settings, isWork: seat.isWork, display: model.display)
        case .instructions: SeatInstructionsSection(part: report.instructions, hideNames: seat.isWork)
        case .extensions: SeatExtensionsSection(seat: seat, part: report.extensions, model: model)
        case .mcp: SeatMCPSection(rows: report.mcp, hideNames: seat.isWork, display: model.display)
        case .history: SeatHistorySection(part: report.history, hideNames: seat.isWork)
        case .housekeeping: SeatHousekeepingSection(items: report.housekeeping)
        case .seat: SeatRepoSection(part: report.repo, hideNames: seat.isWork)
        case .drift: SeatDriftSection(seat: seat, report: report, model: model, demo: demo)
        }
    }
}
