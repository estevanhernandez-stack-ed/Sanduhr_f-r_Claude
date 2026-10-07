import SwiftUI

/// The Mods page's state (item 64, slice 1): every Claude Code folder's mods and plugins, the
/// `claude` found for Check, and each Check's answer. Reads run off the main thread; nothing on
/// this page writes anywhere. Kept by SettingsNavigation, so the summary card and state.yaml read
/// the same model while the window lives.
@MainActor
@Observable
final class ModsPageModel {
    private(set) var inventory: [ModFolderInventory] = []
    private(set) var loaded = false
    private(set) var loading = false
    /// The `claude` Check runs, nil when none was found (known once `loaded`).
    private(set) var claude: String?
    /// Each Check's answer by item id, and the ones running.
    private(set) var checks: [String: ModCheckOutcome] = [:]
    private(set) var checking: Set<String> = []

    let home = NSHomeDirectory()

    var counts: ModCounts { ModCounts(inventory) }

    /// The folders to list: found ones (never a `*.config-backup-*` copy), the accounts' linked
    /// ones, and ones Sanduhr installed into.
    nonisolated static func folders(home: String, environment: [String: String], linked: [String],
                                    installed: [String]) -> [String] {
        var paths = ClaudeCodeFolders.discover(home: home, environment: environment).map(\.path)
        for p in linked + installed {
            let n = AccountData.normalized(p)
            if !paths.contains(n) { paths.append(n) }
        }
        return paths
    }

    func load(linked: [String]) async {
        guard !loading else { return }
        loading = true
        defer { loading = false }
        let home = self.home
        let result = await Task.detached(priority: .userInitiated) { () -> ([ModFolderInventory], String?) in
            let env = ProcessInfo.processInfo.environment
            let paths = ModsPageModel.folders(home: home, environment: env, linked: linked,
                                              installed: IntegrationInstaller.standard.installedFolders())
            return (ModInventory.scan(folders: paths, home: home), ModCheck.findClaude(environment: env, home: home))
        }.value
        inventory = result.0
        claude = result.1
        let ids = Set(inventory.flatMap(\.items).map(\.id))
        checks = checks.filter { ids.contains($0.key) }
        loaded = true
    }

    /// Runs `claude plugin validate --json` on the item's folder (never `plugin test`).
    func check(_ item: ModItem) async {
        guard let path = item.path, !checking.contains(item.id) else { return }
        checking.insert(item.id)
        defer { checking.remove(item.id) }
        let claude = self.claude
        let outcome = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: path) }.value
        checks[item.id] = outcome
    }

    func display(_ path: String) -> String { ClaudeCodeFolders.Folder(path: path).display(home: home) }

    static let cliMissing = "Check needs Claude Code's command line (claude), and it wasn't found on your PATH or in the usual install folders (~/.local/bin, /opt/homebrew/bin, /usr/local/bin)."
}

/// A terminal-styled sketch of where a mod draws in Claude Code (item 64): blocks stand in for
/// its content, so nothing here is live text, and nothing of the mod runs.
enum ModSketch {
    static let dim = "\u{1B}[2m", reset = "\u{1B}[0m"
    static let cyan = "\u{1B}[36m", yellow = "\u{1B}[33m", magenta = "\u{1B}[35m", green = "\u{1B}[32m"

    /// The sketch for `touches`, top to bottom as Claude Code lays it out; nil when the mod
    /// draws nothing.
    static func text(name: String, touches: ModTouches) -> String? {
        let s = Set(touches.surfaces)
        guard !s.isEmpty else { return nil }
        var lines = [dim + "⏺ ░░░░░░░░░░░░░░░░░░░░ ░░░░░░░░░" + reset]
        if s.contains(.pane) {
            lines.append(magenta + "┌ \(name) ─────────────────────" + reset)
            lines.append(magenta + "│ " + reset + dim + "░░░░░░░░░░ ░░░░░░░░" + reset)
            lines.append(magenta + "└──────────────────────────────" + reset)
        }
        if s.contains(.band) { lines.append(cyan + "▌ \(name) " + reset + cyan + "██████████░░░░░░  ░░░░" + reset) }
        lines.append("> " + dim + "░░░░░░░" + reset)
        if s.contains(.status) { lines.append(yellow + "⚠ \(name): " + reset + dim + "░░░░░░░░" + reset) }
        if s.contains(.toast) { lines.append(green + "  ◆ \(name) " + reset + dim + "░░░░░░░░░░░░" + reset) }
        if s.contains(.commands) {
            let names = touches.commands.isEmpty ? ["░░░░"] : touches.commands.prefix(4).map { $0 }
            lines.append(dim + names.map { "/" + $0 }.joined(separator: "  ") + reset)
        }
        return lines.joined(separator: "\n")
    }

    static func lineCount(_ touches: ModTouches) -> Int {
        text(name: "", touches: touches)?.split(separator: "\n", omittingEmptySubsequences: false).count ?? 0
    }
}

/// Settings, Mods (item 64, slice 1): every mod and plugin each Claude Code folder loads, what
/// each draws and can reach, and Check (Claude Code's own validator) for a risk card. Read-only.
struct ModsSettings: View {
    var vm: UsageViewModel
    var model: ModsPageModel

    private var linked: [String] { vm.accountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                ModsIntro()
                if model.loaded && model.claude == nil {
                    Text(ModsPageModel.cliMissing)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                folders
                Button(model.loading ? "Reading…" : "Read Again") { Task { await model.load(linked: linked) } }
                    .disabled(model.loading)
            }
            .frame(maxWidth: .infinity, alignment: .topLeading)
            .padding(20)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task { await model.load(linked: linked) }
    }

    @ViewBuilder
    private var folders: some View {
        if model.loaded && model.inventory.isEmpty {
            Text("No Claude Code folder found (~/.claude or a ~/.claude-name folder).")
                .font(.caption)
                .foregroundStyle(.secondary)
        } else if !model.loaded {
            Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
        }
        ForEach(model.inventory) { f in
            ModsFolderBox(folder: f, model: model)
        }
    }
}

private struct ModsIntro: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Mods and plugins").font(.headline)
            Text("Everything each Claude Code folder loads: mods (plugins with a hooks module that draw in Claude Code) and plain plugins, from its plugin folder list, its installed plugins, its skills folder and the mods a session made. Sanduhr finds them by reading files: no mod runs, and nothing here changes a setting. Check asks Claude Code's own validator, which reads a mod without running it, what the mod hooks and calls.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// One Claude Code folder and its items.
private struct ModsFolderBox: View {
    let folder: ModFolderInventory
    var model: ModsPageModel

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 12) {
                if folder.items.isEmpty {
                    Text("Loads no mods or plugins.").font(.caption).foregroundStyle(.secondary)
                }
                ForEach(folder.items) { item in
                    ModRow(item: item, model: model)
                    if item.id != folder.items.last?.id { Divider() }
                }
            }
            .padding(4)
            .frame(maxWidth: .infinity, alignment: .leading)
        } label: {
            Text(model.display(folder.folder)).font(.body.monospaced())
        }
    }
}

/// One mod or plugin: what it is, where it loads from, what it touches, its sketch and Check.
private struct ModRow: View {
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

/// The validator's answer as a risk card.
private struct ModRiskCard: View {
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

/// The page's summary card (item 68's pattern): the counts across folders, on the preview
/// wallpaper.
struct ModsSummaryCard: View {
    var model: ModsPageModel

    var body: some View {
        SettingsPreviewCard(kind: .mods, label: label) {
            VStack(spacing: 10) {
                if model.loaded {
                    HStack(spacing: 14) {
                        let tiles = self.tiles
                        ForEach(tiles.indices, id: \.self) { i in ModsTile(number: tiles[i].0, title: tiles[i].1) }
                    }
                } else {
                    Text("Reading your Claude Code folders…").foregroundStyle(.white.opacity(0.8))
                }
                Text("Read-only: nothing here turns a mod on or off.")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(12)
        }
    }

    private var tiles: [(Int, String)] {
        let c = model.counts
        var out = [(c.mods, c.mods == 1 ? "mod" : "mods"), (c.plugins, c.plugins == 1 ? "plugin" : "plugins"),
                   (c.on, "on"), (c.folders, c.folders == 1 ? "folder" : "folders")]
        if c.missing > 0 { out.append((c.missing, "missing")) }
        return out
    }

    private var label: String {
        guard model.loaded else { return "Reading your Claude Code folders." }
        let c = model.counts
        return "\(c.mods) mods and \(c.plugins) plugins across \(c.folders) Claude Code folders, \(c.on) of them on."
    }
}

private struct ModsTile: View {
    let number: Int
    let title: String

    var body: some View {
        VStack(spacing: 2) {
            Text("\(number)").font(.system(size: 30, weight: .semibold, design: .rounded)).foregroundStyle(.white)
            Text(title).font(.caption).foregroundStyle(.white.opacity(0.75))
        }
        .frame(minWidth: 70)
        .padding(.vertical, 8)
        .padding(.horizontal, 10)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.white.opacity(0.08)))
    }
}
