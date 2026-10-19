import SwiftUI

// One mod's row on Mods & Config: ModRow and its header, where, ownership, touches, sketch,
// Check and risk card. Split out of ModsSettings.swift on 2026-10-19.

/// One mod or plugin: what it is, where it loads from, what it touches, its sketch and Check.
struct ModRow: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ModRowHeader(item: item)
            if !item.description.isEmpty {
                Text(item.description)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ModRowWhere(item: item, model: model)
            ModRowOwnership(item: item)
            ModRowTouches(touches: item.touches)
            if item.isMod, let sketch = ModSketch.text(name: item.name, touches: item.touches) {
                ModSketchView(name: item.name, sketch: sketch, lines: ModSketch.lineCount(item.touches))
            }
            ModCheckRow(item: item, model: model)
        }
    }
}

private struct ModRowHeader: View {
    let item: ModItem

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(item.name).font(.body.weight(.semibold))
            if let v = item.version, !v.isEmpty {
                Text(v).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            Text(item.isMod ? "Mod" : "Plugin")
                .font(.caption2.weight(.semibold))
                .padding(.horizontal, 6)
                .padding(.vertical, 1)
                .background(Capsule().fill(Color.secondary.opacity(0.18)))
            Spacer(minLength: 8)
            Text(item.state.title)
                .font(.caption)
                .foregroundStyle(stateColor)
        }
    }

    private var stateColor: Color {
        switch item.state {
        case .on: Color.hex("4ade80")
        case .off: .secondary
        case .session: .orange
        case .missing: Color.hex("f87171")
        }
    }
}

private struct ModRowWhere: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(item.originTitle).font(.caption)
            if let path = item.path {
                Text(model.display(path) + (item.state == .missing ? " (not found)" : ""))
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .lineLimit(2)
                    .truncationMode(.middle)
            } else {
                Text("Turned \(item.state == .on ? "on" : "off") here, but its files aren't in this folder's installed plugins.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// Whose mod it is. Sanduhr's own switches at the top of this page; the rest are read-only for now.
private struct ModRowOwnership: View {
    let item: ModItem

    var body: some View {
        switch item.ownership {
        case .sanduhrs:
            Text("Sanduhr's own mod, \(SettingsNames.metersAbovePrompt). It switches per folder in the Mod Studio's Folders tab (its line in the Catalog).")
                .font(.caption)
                .foregroundStyle(Color.hex("a78bfa"))
                .fixedSize(horizontal: false, vertical: true)
        case .copyOfSanduhrs:
            Text("A copy of Sanduhr's mod outside Sanduhr's folder: read-only here.")
                .font(.caption)
                .foregroundStyle(.secondary)
        case .others:
            EmptyView()
        }
    }
}

private struct ModRowTouches: View {
    let touches: ModTouches

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            if !touches.surfaces.isEmpty {
                Text("Draws: " + touches.surfaces.map(title).joined(separator: ", ")).font(.caption)
            }
            if !touches.capabilities.isEmpty {
                Text("Its code " + touches.capabilities.map(\.title).joined(separator: ", ") + ".")
                    .font(.caption)
                    .foregroundStyle(.orange)
            }
        }
    }

    private func title(_ s: ModSurface) -> String {
        guard s == .commands, !touches.commands.isEmpty else { return s.title }
        return s.title + " (" + touches.commands.map { "/" + $0 }.joined(separator: ", ") + ")"
    }
}

/// The surfaces as a terminal sketch, labeled as one.
private struct ModSketchView: View {
    let name: String
    let sketch: String
    let lines: Int

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            TerminalPreviewFrame(sketch, title: "Claude Code", maxLines: max(lines, 1))
                .frame(maxWidth: 520, alignment: .leading)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Sketch of where \(name) draws in Claude Code")
            Text("Sketch: drawn by the mod in Claude Code")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
    }
}

/// Check and its answer.
private struct ModCheckRow: View {
    let item: ModItem
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Button(running ? "Checking…" : "Check") { Task { await model.check(item) } }
                    .disabled(!available || running)
                    .help(help)
                if !available {
                    Text(reason).font(.caption).foregroundStyle(.secondary)
                }
            }
            if let outcome = model.checks[item.id] { ModRiskCard(outcome: outcome) }
        }
    }

    private var running: Bool { model.checking.contains(item.id) }
    private var available: Bool { model.claude != nil && item.path != nil && item.state != .missing }

    private var reason: String {
        if item.path == nil || item.state == .missing { return "Nothing to check: its folder isn't here." }
        return model.loaded ? "Unavailable: Claude Code's command line wasn't found." : ""
    }

    private var help: String {
        if model.claude == nil { return ModsPageModel.cliMissing }
        return "Runs claude plugin validate --json on this folder. It reads the files and runs none of the mod's code."
    }
}

/// The validator's answer as a risk card (the Check button's, and the Studio's About tab).
struct ModRiskCard: View {
    let outcome: ModCheckOutcome

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            switch outcome {
            case .report(let r): ModReportView(report: r)
            case .unavailable: Text(ModsPageModel.cliMissing).font(.caption)
            case .timedOut:
                Text("claude plugin validate didn't answer within 10 seconds, so Sanduhr stopped it.").font(.caption)
            case .failed(let why): Text("Check couldn't read a report: \(why)").font(.caption)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 6).fill(tint.opacity(0.12)))
    }

    private var tint: Color {
        guard case .report(let r) = outcome else { return .secondary }
        if !r.passed { return Color.hex("f87171") }
        switch r.risk {
        case .low: return Color.hex("4ade80")
        case .medium: return .orange
        case .high: return Color.hex("f87171")
        }
    }
}

private struct ModReportView: View {
    let report: ModCheckReport

    var body: some View {
        Text(headline).font(.caption.weight(.semibold))
        ForEach(report.reasons, id: \.self) { Text("• " + $0).font(.caption) }
        ForEach(report.errors, id: \.self) { Text("Error: " + $0).font(.caption).foregroundStyle(Color.hex("f87171")) }
        ForEach(report.warnings, id: \.self) { Text("Warning: " + $0).font(.caption).foregroundStyle(.orange) }
        if !report.hooks.isEmpty { detail("Hooks", report.hooks) }
        if !report.calls.isEmpty { detail("Calls", report.calls) }
        ForEach(report.notes, id: \.self) { Text($0).font(.caption).foregroundStyle(.secondary) }
        Text("From claude plugin validate, which read the files without running them.")
            .font(.caption2)
            .foregroundStyle(.secondary)
    }

    private var headline: String {
        report.passed ? report.risk.title + " · Claude Code would load it"
            : report.risk.title + " · Claude Code would refuse it (\(report.errors.count) error\(report.errors.count == 1 ? "" : "s"))"
    }

    private func detail(_ title: String, _ values: [String]) -> some View {
        Text("\(title): " + values.joined(separator: ", "))
            .font(.caption.monospaced())
            .foregroundStyle(.secondary)
    }
}
