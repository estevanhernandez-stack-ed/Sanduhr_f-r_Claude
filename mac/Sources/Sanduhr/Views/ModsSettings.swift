import SwiftUI

/// The Mods page's state (item 64): every Claude Code folder's mods and plugins, the `claude`
/// found for Check, each Check's answer, and (slice 2) Sanduhr's own mod per folder with its
/// switch, Update and Remove. Reads and writes run off the main thread; the only writes are the
/// own mod's, by receipt (`ModSwitch`). Kept by SettingsNavigation, so the summary card and
/// state.yaml read the same model while the window lives.
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

    /// Sanduhr's own mod in each folder, the app's version of it, and what the last action said.
    private(set) var own: [OwnModRow] = []
    private(set) var appMod: AppModVersion?
    private(set) var ownNotes: [String: OwnModNote] = [:]
    private(set) var ownBusy: Set<String> = []
    /// A folder whose list has no entry, waiting for On's confirmation.
    var confirmOn: String?
    /// An update that can do more than the version in use, waiting for an answer.
    var updateQuestion: OwnModUpdateQuestion?

    let home = NSHomeDirectory()
    var installer = IntegrationInstaller.standard

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
        let installer = self.installer
        let result = await Task.detached(priority: .userInitiated) { () -> ([ModFolderInventory], String?, [OwnModRow], AppModVersion?) in
            let env = ProcessInfo.processInfo.environment
            let paths = ModsPageModel.folders(home: home, environment: env, linked: linked,
                                              installed: installer.installedFolders())
            let inventory = ModInventory.scan(folders: paths, home: home)
            let app = AppModVersion.read(installer.scripts)
            let own = inventory.map { OwnModRow.read(installer, folder: $0.folder, app: app) }
            return (inventory, ModCheck.findClaude(environment: env, home: home), own, app)
        }.value
        inventory = result.0
        claude = result.1
        own = result.2
        appMod = result.3
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

    // MARK: Sanduhr's own mod (slice 2)

    /// The switch: On in a folder whose list has no entry asks first (it adds one).
    func requestSwitch(_ row: OwnModRow, on: Bool, linked: [String]) {
        if on && !row.state.listed {
            confirmOn = row.folder
            return
        }
        Task { await setOwn(on: on, folder: row.folder, linked: linked) }
    }

    func setOwn(on: Bool, folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, home in
            try installer.switchOwnMod(on: on, folder: folder)
            let now = installer.ownModState(folder: folder)
            let found = ModOverrides.find(key: IntegrationInstaller.ownModKey, folder: folder, home: home)
            if let text = ModOverrides.message(found, on: now.isOn, home: home) { return OwnModNote(text: text, kind: .warning) }
            return OwnModNote(text: ModsPageModel.takesEffect, kind: .info)
        }
    }

    func update(folder: String, confirmed: Bool, linked: [String]) async {
        let claude = self.claude
        let note = await act(folder, linked: linked) { installer, _ in
            let outcome = try installer.updateOwnMod(folder: folder, confirmed: confirmed) { old, new in
                ModCapabilities.added(old: old, new: new) { dir in
                    if case .report(let r) = ModCheck.run(claude: claude, dir: dir), r.passed { return r }
                    return nil
                }
            }
            switch outcome {
            case .upToDate: return OwnModNote(text: "Already the version this copy of Sanduhr carries.", kind: .info)
            case .updated: return OwnModNote(text: "Updated. " + ModsPageModel.takesEffect, kind: .info)
            case .needsConsent(let added): return OwnModNote(text: "", kind: .info, added: added)
            }
        }
        if let added = note?.added { updateQuestion = OwnModUpdateQuestion(folder: folder, added: added) }
    }

    func remove(folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, _ in
            try installer.remove(.meters, folder: folder)
            return OwnModNote(text: "Removed: this folder's settings.json is back to what it was before Sanduhr's mod. " + ModsPageModel.takesEffect, kind: .info)
        }
    }

    /// Runs one write off the main thread, then reads the page again. A note carrying `added`
    /// is a question, not a note.
    @discardableResult
    private func act(_ folder: String, linked: [String],
                     _ body: @escaping @Sendable (IntegrationInstaller, String) throws -> OwnModNote?) async -> OwnModNote? {
        guard !ownBusy.contains(folder) else { return nil }
        ownBusy.insert(folder)
        let installer = self.installer, home = self.home
        let note = await Task.detached(priority: .userInitiated) { () -> OwnModNote? in
            do { return try body(installer, home) } catch { return OwnModNote(failure: error) }
        }.value
        ownNotes[folder] = note?.added == nil ? note : nil
        ownBusy.remove(folder)
        await load(linked: linked)
        return note
    }

    nonisolated static let takesEffect = "Takes effect in new Claude Code sessions (or after /reload-plugins)."

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

/// Settings, Mods & Config (item 64; raw value `mods`): every mod and plugin each Claude Code
/// folder loads, what each draws and can reach, and Check (Claude Code's own validator) for a risk
/// card. Every mod is read-only here, Sanduhr's own included: its switch, Update and Remove live
/// in Claude Code, Meters above the prompt (Settings v2, slice 2).
struct ModsSettings: View {
    var vm: UsageViewModel
    var model: ModsPageModel

    private var linked: [String] { vm.accountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if model.loaded && model.claude == nil {
                    Text(ModsPageModel.cliMissing)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                // The heading sits right above the list it describes (Settings v2, slice 1).
                ModsIntro()
                    .settingsAnchor(SettingsAnchor.inventory)
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
            Text("Everything each Claude Code folder loads: mods (plugins with a hooks module that draw in Claude Code) and plain plugins, from the plugin folders its settings list, its installed plugins, its skills folder and the mods a session made. Sanduhr finds them by reading files: no mod runs. Every mod is read-only here: Sanduhr's own switches per folder in Claude Code, Meters above the prompt, and switching other mods comes later. Check asks Claude Code's own validator, which reads a mod without running it, what the mod hooks and calls.")
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

/// Whose mod it is. Sanduhr's own switches in Claude Code; the rest are read-only for now.
private struct ModRowOwnership: View {
    let item: ModItem

    var body: some View {
        switch item.ownership {
        case .sanduhrs:
            HStack(alignment: .firstTextBaseline) {
                Text("Sanduhr's own mod. It switches per folder in Claude Code, \(SettingsNames.metersAbovePrompt).")
                    .font(.caption)
                    .foregroundStyle(Color.hex("a78bfa"))
                Spacer(minLength: 8)
                SettingsLinkButton(.integrations, anchor: SettingsAnchor.folders)
            }
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
                Text("Every mod is read-only here; Sanduhr's own switches in Claude Code.")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(12)
        }
    }

    private var tiles: [(number: Int, title: String)] { model.counts.tiles }

    private var label: String {
        guard model.loaded else { return "Reading your Claude Code folders." }
        return model.counts.summary
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
