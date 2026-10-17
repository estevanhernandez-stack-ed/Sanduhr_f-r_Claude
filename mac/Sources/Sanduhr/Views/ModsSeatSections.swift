import SwiftUI

// By Seat's section bodies (item 71, slice 1b). Every value they draw came through ConfigMask
// in the background read; a work seat's project names are never drawn (`hideNames`).

private struct Caption: View {
    let text: String
    var color: Color = .secondary

    init(_ text: String, color: Color = .secondary) {
        self.text = text
        self.color = color
    }

    var body: some View {
        Text(text).font(.caption).foregroundStyle(color).fixedSize(horizontal: false, vertical: true)
    }
}

private struct SubHeading: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text).font(.subheadline.weight(.semibold)).padding(.top, 4)
    }
}

private func bytes(_ n: Int64) -> String { ByteCountFormatter.string(fromByteCount: n, countStyle: .file) }

// MARK: Settings

struct SeatSettingsSection: View {
    let part: SeatReport.SettingsPart
    let isWork: Bool
    let display: (String) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Caption("Computed by Sanduhr for the folder as a whole: managed settings, then this folder's settings.json. Each project's .claude/settings.local.json and .claude/settings.json can override a key inside that project; they're counted here, never named. Objects merge key by key and lists combine, the strict keys take the strictest value, and values where they're ignored are left out. A --settings flag or shell variables of a running session can change this; Sanduhr can't see them.")
            if let why = part.unreadable {
                Caption(ModsPageModel.unreadableText(why), color: Color.hex("f87171"))
            }
            if part.managedFiles > 0 { Caption("\(part.managedFiles) managed settings file\(part.managedFiles == 1 ? "" : "s") apply, merged in name order.") }
            ForEach(part.lints, id: \.self) { Caption($0, color: .orange) }
            let families = Array(Set(part.rows.map(\.family))).sorted()
            ForEach(families, id: \.self) { family in
                SubHeading(family)
                ForEach(part.rows.filter { $0.family == family }) { SettingRowView(row: $0, isWork: isWork, display: display) }
            }
        }
    }
}

private struct SettingRowView: View {
    let row: SettingRow
    let isWork: Bool
    let display: (String) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(row.key).font(.caption.monospaced().weight(.medium))
                switch row.shown(work: isWork) {
                case .toggle(let on):
                    Toggle("", isOn: .constant(on)).toggleStyle(.switch).controlSize(.mini).labelsHidden().disabled(true)
                case .text(let t):
                    Text(t).font(.caption.monospaced()).lineLimit(3).truncationMode(.tail).textSelection(.enabled)
                }
            }
            Text(origin).font(.caption2).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ForEach(row.warnings, id: \.self) { Text($0).font(.caption2).foregroundStyle(.orange) }
        }
        .padding(.vertical, 1)
    }

    private var origin: String {
        var parts = ["From \(row.source.title) (\(display(row.file)))"]
        if row.contributors.count > 1 { parts.append("merged from " + row.contributors.map(\.title).joined(separator: ", ")) }
        if row.strictWon { parts.append("the strictest value wins") }
        parts.append("honored: \(row.honored)")
        if row.machineOnly { parts.append("this machine only") }
        if row.setInProjects > 0 { parts.append("also set in \(row.setInProjects) project\(row.setInProjects == 1 ? "" : "s")") }
        return parts.joined(separator: " · ")
    }
}

// MARK: Instructions

struct SeatInstructionsSection: View {
    let part: SeatReport.InstructionsPart
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if part.nodes.isEmpty { Caption("No managed CLAUDE.md, no CLAUDE.md in the folder and no rules.") }
            ForEach(part.nodes) { InstructionNodeRow(node: $0, title: SeatInstructionTitles.title($0, in: part.nodes, hideNames: hideNames)) }
            if let leak = part.leak { Caption(leak, color: .orange) }
            if !part.excludes.isEmpty {
                SubHeading("claudeMdExcludes")
                if hideNames {
                    // A work seat's patterns can name its projects: counted only.
                    let matched = part.excludes.reduce(0) { $0 + $1.matches }
                    Caption("\(part.excludes.count) pattern\(part.excludes.count == 1 ? "" : "s"), matching \(matched) CLAUDE.md file\(matched == 1 ? "" : "s") on this Mac.")
                } else {
                    ForEach(part.excludes, id: \.pattern) { e in
                        Caption("\(ConfigMask.maskString(e.pattern)): matches \(e.matches) CLAUDE.md file\(e.matches == 1 ? "" : "s") on this Mac.")
                    }
                }
            }
            if !part.memory.isEmpty {
                SubHeading("Auto memory")
                ForEach(Array(part.memory.enumerated()), id: \.offset) { i, m in
                    Caption(memoryLine(m, index: i), color: m.overLimit || m.missingLinks > 0 ? .orange : .secondary)
                }
            }
        }
    }

    private func memoryLine(_ m: SeatReport.MemoryIndex, index: Int) -> String {
        let name = hideNames ? "Project \(index + 1)" : m.project
        var t = "\(name): MEMORY.md \(m.lines) lines, \(bytes(Int64(m.bytes)))"
        if m.overLimit { t += ", over the \(SeatReports.memoryLineLimit)-line or 25 KB load limit" }
        if m.missingLinks > 0 { t += ", \(m.missingLinks) link\(m.missingLinks == 1 ? "" : "s") to missing files" }
        return t + "."
    }
}

/// What an instruction file is called on screen. A work seat's imports are numbered, never
/// named: a path can name a project.
enum SeatInstructionTitles {
    static func title(_ node: SeatReport.InstructionNode, in nodes: [SeatReport.InstructionNode], hideNames: Bool) -> String {
        guard hideNames, node.kind == .imported else { return node.title }
        if node.outsideSeat { return "An import outside the folder" }
        let n = nodes.filter { $0.kind == .imported && !$0.outsideSeat && $0.id <= node.id }.count
        return "Import \(n)"
    }
}

private struct InstructionNodeRow: View {
    let node: SeatReport.InstructionNode
    let title: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(String(repeating: "  ", count: node.depth) + (node.depth > 0 ? "↳ " : "") + title)
                .font(.caption.monospaced())
                .lineLimit(1)
                .truncationMode(.middle)
            Text(detail).font(.caption2).foregroundStyle(node.missing ? Color.hex("f87171") : .secondary)
        }
    }

    private var detail: String {
        if node.missing { return "doesn't resolve" }
        var parts = ["\(node.lines) lines", bytes(Int64(node.bytes))]
        switch node.kind {
        case .managed: parts.insert("managed", at: 0)
        case .rule: parts.insert(node.gate.map { "rule, paths \(ConfigMask.maskString($0))" } ?? "rule", at: 0)
        case .seat, .imported: break
        }
        if node.truncated { parts.append("over 1 MB, counted from its first megabyte") }
        if node.outsideSeat { parts.append("outside the folder") }
        return parts.joined(separator: " · ")
    }
}

// MARK: Extensions

struct SeatExtensionsSection: View {
    let seat: Seat
    let part: SeatReport.ExtensionsPart
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach([CatalogKind.agent, .skill, .command, .outputStyle], id: \.self) { kind in
                let rows = part.rows.filter { $0.kind == kind }
                if !rows.isEmpty {
                    SubHeading(kind.title)
                    ForEach(rows) { ExtensionRowView(row: $0, hideNames: seat.isWork) }
                }
            }
            if !part.skillOverrides.isEmpty {
                SubHeading("skillOverrides")
                ForEach(part.skillOverrides, id: \.self) { Caption($0) }
            }
            SubHeading("Plugins: enabled, installed, declared")
            if part.plugins.isEmpty { Caption("None.") }
            ForEach(part.plugins) { PluginParityRow(row: $0) }
            ForEach(part.parity, id: \.self) { Caption($0, color: .orange) }
            SubHeading("Hooks (read-only)")
            if part.hooks.isEmpty { Caption("None.") }
            ForEach(part.hooks) { HookRowView(row: $0, isWork: seat.isWork) }
            ModsSeatMods(seat: seat.path, model: model).padding(.top, 6)
        }
    }
}

private struct ExtensionRowView: View {
    let row: SeatReport.ExtensionRow
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 6) {
                Text(row.name).font(.caption.weight(.medium))
                Text(scope).font(.caption2).foregroundStyle(.secondary)
            }
            if !row.description.isEmpty { Caption(row.description) }
            if let s = row.shadow { Caption(s, color: .orange) }
        }
    }

    private var scope: String {
        switch row.scope {
        case .seat: return "folder"
        case .synced: return "from claude.ai, overwritten on the next sync"
        case .project:
            guard !hideNames, let p = row.project else { return "a project" }
            return "project " + (p as NSString).lastPathComponent
        }
    }
}

private struct PluginParityRow: View {
    let row: SeatReport.PluginRow

    var body: some View {
        HStack(spacing: 8) {
            Text(row.key).font(.caption.monospaced())
            mark("enabled", row.enabled)
            mark("installed", row.installed)
            mark("declared", row.declared)
        }
    }

    private func mark(_ title: String, _ on: Bool) -> some View {
        Text((on ? "✓ " : "– ") + title).font(.caption2).foregroundStyle(on ? Color.primary : Color.secondary)
    }
}

private struct HookRowView: View {
    let row: SeatReport.HookRow
    let isWork: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text("\(row.event)\(row.matcher.isEmpty ? "" : " (\(row.matcher))") · \(row.source)\(row.timeout.map { " · \($0) s" } ?? "")")
                .font(.caption.weight(.medium))
            Text(isWork ? row.workCommand : row.command).font(.caption2.monospaced()).foregroundStyle(.secondary).lineLimit(2).truncationMode(.middle)
        }
    }
}

// MARK: MCP servers

struct SeatMCPSection: View {
    let rows: [SeatMCPRow]
    let hideNames: Bool
    let display: (String) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Caption("In precedence order: managed, then local, project and user (the same name: the higher one wins whole, no field merge), then plugins. Connectors from claude.ai aren't listed here. Env and header values are never shown.")
            if rows.isEmpty { Caption("No MCP servers.") }
            ForEach(rows) { MCPRowView(row: $0, hideNames: hideNames) }
        }
    }
}

private struct MCPRowView: View {
    let row: SeatMCPRow
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 6) {
                Text(row.server.name).font(.caption.weight(.medium))
                Text(row.server.scope.title + owner).font(.caption2).foregroundStyle(.secondary)
            }
            Text("\(row.server.transport) · \(row.server.target)").font(.caption2.monospaced()).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            if !row.server.envNames.isEmpty || !row.server.headerNames.isEmpty {
                Caption(names)
            }
            if let a = row.approval { Caption("Approval: \(a)") }
            if let s = row.shadowedBy { Caption("Shadowed where the \(s.title.lowercased()) entry of the same name applies.", color: .orange) }
            if !row.sameEndpoint.isEmpty {
                Caption("Same endpoint as \(row.sameEndpoint.joined(separator: ", ")): allow rules keyed to one name miss the other.", color: .orange)
            }
            if let r = row.route { Caption(r) }
        }
    }

    private var owner: String {
        guard let o = row.server.owner else { return "" }
        if row.server.scope == .plugin { return ", " + o }
        return hideNames ? ", a project" : ", " + (o as NSString).lastPathComponent
    }

    private var names: String {
        var parts: [String] = []
        if !row.server.envNames.isEmpty { parts.append("env " + row.server.envNames.joined(separator: ", ")) }
        if !row.server.headerNames.isEmpty { parts.append("headers " + row.server.headerNames.joined(separator: ", ")) }
        return parts.joined(separator: " · ")
    }
}

// MARK: History

struct SeatHistorySection: View {
    let part: SeatReport.HistoryPart
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if part.warns {
                Caption("Transcripts older than 30 days are deleted at the next start: cleanupPeriodDays isn't set, so Claude Code's default of 30 applies, as a silent sweep after a session starts.", color: .orange)
            } else {
                Caption("cleanupPeriodDays: \(part.cleanupDays ?? 30) days.")
            }
            Caption("desktopSessionCleanupPeriodDays (Claude Desktop and Cowork): " + (part.desktopDays.map { "\($0) days." } ?? "not set."))
            Caption("\(bytes(part.transcriptBytes)) of transcripts in \(part.sessions) sessions across \(part.projects) projects; \(bytes(part.recentBytes)) written in the last 30 days.")
            if let oldest = part.oldest { Caption("Oldest: \(oldest.formatted(date: .abbreviated, time: .omitted)).") }
            Caption("history.jsonl: " + (part.historyBytes.map { bytes($0) + ", kept until you delete it." } ?? "none."))
            if part.orphanSidecars > 0 {
                Caption("\(part.orphanSidecars) subagent folder\(part.orphanSidecars == 1 ? "" : "s") outlived their session.", color: .orange)
            }
            if part.partial {
                Caption("The walk stopped at \(SeatReports.transcriptLimit) entries: these sizes are partial.", color: .orange)
            }
            if !hideNames, !part.largest.isEmpty {
                SubHeading("Largest projects")
                ForEach(part.largest, id: \.name) { Caption("\($0.name): \(bytes($0.bytes))") }
            }
        }
    }
}

// MARK: Housekeeping

struct SeatHousekeepingSection: View {
    let items: [SeatReport.Leftover]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if items.isEmpty { Caption("Nothing left over: no Sanduhr backups, temp files, stray backups or copies.") }
            ForEach(items) { item in
                VStack(alignment: .leading, spacing: 1) {
                    Text("\(item.kind): \(item.name)").font(.caption.weight(.medium))
                    Caption(detail(item))
                }
            }
            Caption("Read-only for now: nothing here is deleted or moved.")
        }
    }

    private func detail(_ item: SeatReport.Leftover) -> String {
        var parts = ["Left by \(item.origin)"]
        if item.bytes > 0 { parts.append(bytes(item.bytes)) }
        if let m = item.modified { parts.append(m.formatted(date: .abbreviated, time: .omitted)) }
        parts.append(item.ifLeft)
        return parts.joined(separator: " · ")
    }
}

// MARK: Seat

struct SeatRepoSection: View {
    let part: SeatReport.RepoPart
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Caption(part.model.title + (part.branch.map { ", branch \($0)" } ?? "") + ".")
            if part.model == .plain {
                Caption("Unsynced folder: Sanduhr offers no conversion. The folder's own onboarding (kill switch first, fetch, review drift, dry run, enable) is the route.")
            }
            switch part.remoteMatches {
            case .some(true): Caption("Its origin matches the folder's identity file.")
            case .some(false): Caption("Its origin doesn't match the folder's identity file (.seat/seat.json).", color: Color.hex("f87171"))
            case .none: EmptyView()
            }
            if part.killSwitch { Caption("Sync is switched off (.seat/disabled).", color: .orange) }
            SeatSyncView(sync: part.sync, hideNames: hideNames)
            if !part.buckets.isEmpty {
                SubHeading("The never-list, from the folder's .gitignore")
                ForEach(SeatReport.Bucket.allCases, id: \.self) { b in
                    if let list = part.buckets[b], !list.isEmpty {
                        // A work seat's patterns can name its projects: counted only.
                        Caption(hideNames ? "\(b.title): \(list.count) pattern\(list.count == 1 ? "" : "s")."
                                          : "\(b.title): " + list.map(ConfigMask.maskString).joined(separator: ", "))
                    }
                }
            }
        }
    }
}

private struct SeatSyncView: View {
    let sync: SeatReport.SyncState
    let hideNames: Bool

    private func lastSync(_ s: SeatReport.SyncRead) -> String {
        let parts: [String?] = [s.lastRun, s.phase.map { "phase \($0)" }, s.paused == true ? "paused" : nil]
        let head: String = "Last sync: " + parts.compactMap { $0 }.joined(separator: ", ")
        return head + (s.applySteps > 0 ? ", \(s.applySteps) apply steps seen." : ".")
    }

    var body: some View {
        switch sync {
        case .none: Caption("No .seat/state.json.")
        case .unknownSchema: Caption(".seat/state.json is in an unknown schema: Sanduhr doesn't read it.", color: .orange)
        case .read(let s):
            VStack(alignment: .leading, spacing: 2) {
                Caption(lastSync(s))
                if hideNames {
                    if !s.report.isEmpty { Caption("Its last report has \(s.report.count) lines.") }
                } else {
                    ForEach(Array(s.report.prefix(8).enumerated()), id: \.offset) { Text($0.element).font(.caption2.monospaced()).foregroundStyle(.secondary) }
                }
            }
        }
    }
}

// MARK: Drift

struct SeatDriftSection: View {
    let seat: Seat
    let report: SeatReport
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Caption("Read-only. Across the wall (a folder marked work and any other), names and \"differs\" only, never values.")
            ForEach(model.shownDrift(report, demo: demo), id: \.other) { d in
                DriftBlock(drift: d, otherLabel: model.label(of: d.other),
                           values: SeatWall.showsValues(between: seat, and: other(d.other)))
            }
            SeatRoutesView(seat: seat, routes: report.routes, model: model, demo: demo)
        }
    }

    private func other(_ path: String) -> Seat {
        model.seats.first { $0.path == path } ?? seat
    }
}

private struct DriftBlock: View {
    let drift: SeatReport.Drift
    let otherLabel: String
    let values: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            SubHeading("Against \(otherLabel)")
            if drift.settingsUnreadable {
                Caption("A settings.json on one side is unreadable, so keys aren't compared.", color: .orange)
            } else if drift.count == 0 {
                Caption("No differences.")
            }
            ForEach(drift.keys, id: \.key) { k in
                Caption(values ? "\(k.key): \(k.mine ?? "not set") here, \(k.theirs ?? "not set") there" : "\(k.key): differs")
            }
            if !drift.onlyHere.isEmpty { Caption("Only here: " + drift.onlyHere.joined(separator: ", ")) }
            if !drift.onlyThere.isEmpty { Caption("Only there: " + drift.onlyThere.joined(separator: ", ")) }
        }
    }
}

/// Routing: which of this seat's projects name a seat in `.vscode/settings.json`, and a red
/// flag when a work seat's project routes to a seat that isn't work.
private struct SeatRoutesView: View {
    let seat: Seat
    let routes: [SeatReport.Route]
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        if !routes.isEmpty {
            SubHeading("Routing (.vscode/settings.json)")
            let crossing = routes.filter { seat.isWork && !model.isWork($0.target) }
            Caption("\(routes.count) project\(routes.count == 1 ? "" : "s") name a folder: \(routes.filter { $0.target == seat.path }.count) this one.")
            if !crossing.isEmpty {
                Caption("\(crossing.count) of this folder's projects route to a folder not marked work.", color: Color.hex("f87171"))
            }
            if !seat.isWork {
                // A project routed into a work seat is work's: counted, never named; in demo
                // mode not even the work seat's label shows.
                let toWork = routes.filter { model.isWork($0.target) }
                ForEach(routes.filter { !model.isWork($0.target) }, id: \.project) { r in
                    Caption("\((r.project as NSString).lastPathComponent) → \(model.label(of: r.target))")
                }
                if !toWork.isEmpty, !demo {
                    Caption("\(toWork.count) project\(toWork.count == 1 ? "" : "s") route\(toWork.count == 1 ? "s" : "") to a folder marked work.")
                }
            }
        }
    }
}
