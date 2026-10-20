import SwiftUI

// By Seat's section bodies (item 71, slice 1b). Every value they draw came through ConfigMask
// in the background read; a work seat's project names are never drawn (`hideNames`).
// The history, housekeeping, repo, sync, drift and routes sections live in ModsSeatRepoSections.swift.

struct SeatCaption: View {
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

struct SeatSubHeading: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text).font(.subheadline.weight(.semibold)).padding(.top, 4)
    }
}

func seatBytes(_ n: Int64) -> String { ByteCountFormatter.string(fromByteCount: n, countStyle: .file) }

// MARK: Settings

struct SeatSettingsSection: View {
    let part: SeatReport.SettingsPart
    let isWork: Bool
    let display: (String) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SeatCaption("Computed by Sanduhr for the folder as a whole: managed settings, then this folder's settings.json. Each project's .claude/settings.local.json and .claude/settings.json can override a key inside that project; they're counted here, never named. Objects merge key by key and lists combine, the strict keys take the strictest value, and values where they're ignored are left out. A --settings flag or shell variables of a running session can change this; Sanduhr can't see them.")
            if let why = part.unreadable {
                SeatCaption(ModsPageModel.unreadableText(why), color: Color.hex("f87171"))
            }
            if part.managedFiles > 0 { SeatCaption("\(part.managedFiles) managed settings file\(part.managedFiles == 1 ? "" : "s") apply, merged in name order.") }
            ForEach(part.lints, id: \.self) { SeatCaption($0, color: .orange) }
            let families = Array(Set(part.rows.map(\.family))).sorted()
            ForEach(families, id: \.self) { family in
                SeatSubHeading(family)
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
            if part.nodes.isEmpty { SeatCaption("No managed CLAUDE.md, no CLAUDE.md in the folder and no rules.") }
            ForEach(part.nodes) { InstructionNodeRow(node: $0, title: SeatInstructionTitles.title($0, in: part.nodes, hideNames: hideNames)) }
            if let leak = part.leak { SeatCaption(leak, color: .orange) }
            if !part.excludes.isEmpty {
                SeatSubHeading("claudeMdExcludes")
                if hideNames {
                    // A work seat's patterns can name its projects: counted only.
                    let matched = part.excludes.reduce(0) { $0 + $1.matches }
                    SeatCaption("\(part.excludes.count) pattern\(part.excludes.count == 1 ? "" : "s"), matching \(matched) CLAUDE.md file\(matched == 1 ? "" : "s") on this Mac.")
                } else {
                    ForEach(part.excludes, id: \.pattern) { e in
                        SeatCaption("\(ConfigMask.maskString(e.pattern)): matches \(e.matches) CLAUDE.md file\(e.matches == 1 ? "" : "s") on this Mac.")
                    }
                }
            }
            if !part.memory.isEmpty {
                SeatSubHeading("Auto memory")
                ForEach(Array(part.memory.enumerated()), id: \.offset) { i, m in
                    SeatCaption(memoryLine(m, index: i), color: m.overLimit || m.missingLinks > 0 ? .orange : .secondary)
                }
            }
        }
    }

    private func memoryLine(_ m: SeatReport.MemoryIndex, index: Int) -> String {
        let name = hideNames ? "Project \(index + 1)" : m.project
        var t = "\(name): MEMORY.md \(m.lines) lines, \(seatBytes(Int64(m.bytes)))"
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
        var parts = ["\(node.lines) lines", seatBytes(Int64(node.bytes))]
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
                    SeatSubHeading(kind.title)
                    ForEach(rows) { ExtensionRowView(row: $0, hideNames: seat.isWork) }
                }
            }
            if !part.skillOverrides.isEmpty {
                SeatSubHeading("skillOverrides")
                ForEach(part.skillOverrides, id: \.self) { SeatCaption($0) }
            }
            SeatSubHeading("Plugins: enabled, installed, declared")
            if part.plugins.isEmpty { SeatCaption("None.") }
            ForEach(part.plugins) { PluginParityRow(row: $0) }
            ForEach(part.parity, id: \.self) { SeatCaption($0, color: .orange) }
            SeatSubHeading("Hooks (read-only)")
            if part.hooks.isEmpty { SeatCaption("None.") }
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
            if !row.description.isEmpty { SeatCaption(row.description) }
            if let s = row.shadow { SeatCaption(s, color: .orange) }
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
            SeatCaption("In precedence order: managed, then local, project and user (the same name: the higher one wins whole, no field merge), then plugins. Connectors from claude.ai aren't listed here. Env and header values are never shown.")
            if rows.isEmpty { SeatCaption("No MCP servers.") }
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
                SeatCaption(names)
            }
            if let a = row.approval { SeatCaption("Approval: \(a)") }
            if let s = row.shadowedBy { SeatCaption("Shadowed where the \(s.title.lowercased()) entry of the same name applies.", color: .orange) }
            if !row.sameEndpoint.isEmpty {
                SeatCaption("Same endpoint as \(row.sameEndpoint.joined(separator: ", ")): allow rules keyed to one name miss the other.", color: .orange)
            }
            if let r = row.route { SeatCaption(r) }
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
