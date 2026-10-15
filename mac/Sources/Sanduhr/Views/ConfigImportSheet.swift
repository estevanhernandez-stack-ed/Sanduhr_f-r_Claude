import AppKit
import CryptoKit
import SwiftUI

/// What the Open sheet works on: one config pack against the seat chosen (item 76, slice D).
struct ConfigImportState: Identifiable {
    let id = UUID()
    let pack: ModPackReader.Unpacked
    let packSHA256: String
    let fileName: String
    var seat: String
    var rows: [ConfigImport.Row] = []
    var chosen: Set<String> = []
    var env: [String: String] = [:]
    /// After Import: what was written, and what Undo would leave.
    var done: ConfigImport.Record?
    var failure: String?
    var staged = 0
    /// Whether plugin and MCP rows run through `claude` here (slice E).
    var cli: ConfigImport.CLIState = .missing
    /// Import pressed: the CLI is running.
    var working = false
    /// A line per plugin or server `claude` didn't put in.
    var cliFailures: [String] = []

    var label: String { pack.manifest["label"] as? String ?? "Config" }

    /// No folder chosen yet: nothing is planned, and Import waits.
    var hasSeat: Bool { !seat.isEmpty }

    mutating func plan(home: String = NSHomeDirectory()) {
        guard hasSeat else { rows = []; chosen = []; return }
        rows = ConfigImport.plan(pack, seat: seat, home: home, cli: cli)
        chosen = Set(rows.filter { $0.choosable && $0.kind != .instructions }.map(\.id))
    }
}

extension ModsPageModel {
    /// Whether `claude` can put plugins and MCP servers into `seat` now.
    func cliState(for seat: String) -> ConfigImport.CLIState {
        guard claude != nil else { return .missing }
        return running.contains(AccountData.normalized(seat)) ? .running : .ready
    }

    /// Seats Claude Code runs in right now, read fresh (the page's list can be minutes old).
    nonisolated static func runningSeats(home: String) -> Set<String> {
        Set(ClaudeProcesses.running(home: home).sessions.map { AccountData.normalized($0.seat) })
    }

    /// Opens a pack from Finder or a panel: a config pack opens the Open sheet on `seat`, or with
    /// no folder chosen when none is given, so a pack never lands in a real folder by default
    /// (the 2026-10-15 check: a double-clicked pack went into the first personal folder); a mod
    /// pack goes to New Mod as before.
    func openAnyPack(_ url: URL, store: ModProposalStore, seat: String? = nil) async {
        let read = await Task.detached(priority: .userInitiated) { () -> Result<(ModPackReader.Unpacked, String), ModPackReader.Refusal> in
            do {
                let pack = try ModPackReader.read(url)
                let sha = FileManager.default.contents(atPath: url.path).map { ConfigImport.sha($0) } ?? ""
                return .success((pack, sha))
            } catch let r as ModPackReader.Refusal { return .failure(r) } catch {
                return .failure(ModPackReader.Refusal("Sanduhr can't read that file."))
            }
        }.value
        switch read {
        case .success((let pack, let sha)) where pack.kind == "config":
            // No wait for the folders: none is chosen for you, and the picker fills once they're read.
            var state = ConfigImportState(pack: pack, packSHA256: sha, fileName: url.lastPathComponent, seat: seat ?? "")
            state.cli = cliState(for: seat ?? "")
            state.plan()
            tab = .bySeat
            if let seat { selectedSeat = seat }
            configImport = state
        case .success((let pack, _)) where pack.kind == "mod":
            newModWay = .fromClaude
            newModOpen = true
            await openPack(url, store: store)
        case .success:
            configImportNote = "Sanduhr can't open \(url.lastPathComponent): it isn't a kind of pack this Sanduhr opens."
        case .failure(let r):
            newModWay = .fromClaude
            newModOpen = true
            keptNote = "Sanduhr can't open \(url.lastPathComponent): " + r.reasons.joined(separator: " ")
        }
    }

    /// Asks for a pack to open into `seat`.
    func chooseConfigPack(seat: String) async {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.message = "Open a config .sanduhrpack. Nothing is written until you choose what to bring over."
        panel.prompt = "Open"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        await openAnyPack(url, store: .shared, seat: seat)
    }

    /// Writes the chosen rows, records the import, and stages chosen kept mods for review. Files
    /// and settings first; then plugins and MCP servers through `claude`, off the main thread,
    /// refused when a session runs in the seat by now.
    func finishConfigImport(_ given: ConfigImportState) async -> ConfigImportState {
        var state = given
        let chosen = state.rows.filter { state.chosen.contains($0.id) }
        var (record, failure) = ConfigImport.apply(chosen.filter { $0.kind != .keptMod }, env: state.env, seat: state.seat,
                                                   label: state.label, packSHA256: state.packSHA256)
        let jobs = chosen.compactMap { row -> ConfigImport.Job? in
            if case .install = row.status { return row.job }
            return nil
        }
        if !jobs.isEmpty, let claude {
            let seat = record.seat, home = self.home
            let ran = await Task.detached(priority: .userInitiated) { () -> (steps: [ConfigImport.Step], failures: [String]) in
                guard !Self.runningSeats(home: home).contains(seat) else {
                    return ([], ["Claude Code started in this folder, so no plugin or MCP server was put in. Quit it, then open the pack again."])
                }
                return ConfigImport.run(jobs, seat: seat, cli: ClaudeCLI(claude: claude, home: home))
            }.value
            record.steps += ran.steps
            state.cliFailures = ran.failures
        }
        if !record.steps.isEmpty { ConfigImport.Log.standard.add(record) }
        state.done = record
        state.failure = failure?.message
        var staged = 0
        for row in chosen where row.kind == .keptMod {
            var doc: [String: Any] = ["name": row.name]
            if let m = (state.pack.manifest["kept_mods"] as? [[String: Any]])?.first(where: { $0["name"] as? String == row.name }) {
                doc["version"] = m["version"]
            }
            if let opened = try? ModPackReader.mod(in: state.pack.files, prefix: "mods/\(row.name)/", doc: doc, now: Date()),
               case .staged? = try? ModProposalStore.shared.open(opened) {
                staged += 1
            }
        }
        state.staged = staged
        state.working = false
        imports = ConfigImport.Log.standard.list()
        if state.done?.steps.contains(where: \.throughCLI) == true { Task { await load(linked: lastLinked) } }
        return state
    }

    /// Undo Import: last write first, plugins and servers through `claude`; anything changed since
    /// is left and named. Refused, with nothing undone, while a session runs in the seat.
    func undoImport(_ record: ConfigImport.Record) async -> UndoOutcome {
        // A second press while the first runs: the first one answers.
        guard !undoingImports.contains(record.id) else { return .done([]) }
        undoingImports.insert(record.id)
        defer { undoingImports.remove(record.id) }
        if record.steps.contains(where: \.throughCLI) {
            let claude = self.claude, home = self.home
            let cliLeft = await Task.detached(priority: .userInitiated) { () -> [String]? in
                guard !Self.runningSeats(home: home).contains(record.seat) else { return nil }
                return ConfigImport.undoCLI(record, cli: claude.map { ClaudeCLI(claude: $0, home: home) }, home: home)
            }.value
            guard let cliLeft else {
                return .refused("Nothing was undone: Claude Code is running in \(display(record.seat)). Quit it, then Undo Import again.")
            }
            let left = cliLeft + ConfigImport.undo(record)
            ConfigImport.Log.standard.remove(record.id)
            imports = ConfigImport.Log.standard.list()
            Task { await load(linked: lastLinked) }
            return .done(left)
        }
        let left = ConfigImport.undo(record)
        ConfigImport.Log.standard.remove(record.id)
        imports = ConfigImport.Log.standard.list()
        return .done(left)
    }

    enum UndoOutcome {
        /// Undone, except what's named.
        case done([String])
        /// Nothing undone, and why.
        case refused(String)

        var isClean: Bool { if case .done(let l) = self { l.isEmpty } else { false } }
        var isRefused: Bool { if case .refused = self { true } else { false } }

        var note: String {
            switch self {
            case .done(let left): left.isEmpty ? "Undone: the folder is as it was." : "Undone, except: " + left.joined(separator: "; ") + "."
            case .refused(let why): why
            }
        }
    }
}

/// Open Config Pack (item 76, slice D): the pack against a folder, row by row. Only what the
/// folder lacks can be brought over; what it has stays.
struct ConfigImportSheet: View {
    var model: ModsPageModel
    let demo: Bool
    @State var state: ConfigImportState
    let close: () -> Void
    @State private var confirming = false
    @State private var undone: ModsPageModel.UndoOutcome?
    @State private var undoing = false

    /// A folder chosen by Other Folder…: a new seat Claude Code hasn't used yet isn't found, and
    /// a new machine's seat is what a pack is most often opened into.
    @State private var other: String?
    private static let otherTag = "\u{0}other"

    private var seat: Seat? { model.seats.first { $0.path == state.seat } }

    private func pick(_ tag: String) {
        guard tag == Self.otherTag else { state.seat = tag; state.cli = model.cliState(for: tag); state.plan(); return }
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.showsHiddenFiles = true
        panel.allowsMultipleSelection = false
        panel.directoryURL = URL(fileURLWithPath: NSHomeDirectory())
        panel.message = "Choose the Claude Code folder to open the pack into, such as ~/.claude or a CLAUDE_CONFIG_DIR folder."
        panel.prompt = "Choose"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let path = AccountData.normalized(url.path)
        if !model.seats.contains(where: { $0.path == path }) { other = path }
        state.seat = path
        state.cli = model.cliState(for: path)
        state.plan()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Open \(state.label)").font(.title3.weight(.semibold))
            Text("\(state.fileName), made by \(state.pack.manifest["created_by"] as? String ?? "Sanduhr").")
                .font(.caption).foregroundStyle(.secondary)
            if state.done == nil {
                Picker("Into", selection: Binding(get: { state.seat }, set: { pick($0) })) {
                    if !state.hasSeat { Text("Choose a folder…").tag("") }
                    ForEach(model.pickerSeats(demo: demo)) { s in
                        Text(s.label + (s.isWork ? " (work)" : "") + "  " + model.display(s.path)).tag(s.path)
                    }
                    if let other { Text(model.display(other)).tag(other) }
                    Divider()
                    Text("Other Folder…").tag(Self.otherTag)
                }
                .frame(maxWidth: 420)
                Text("Only what this folder doesn't have yet can be brought over. What it has, the same or its own, stays as it is. Plugins and MCP servers are shown as the commands to run.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if seat?.isWork == true {
                    Text("This is a folder marked work.").font(.caption.weight(.semibold)).foregroundStyle(.orange)
                }
                if FileManager.default.fileExists(atPath: (state.seat as NSString).appendingPathComponent(".git")) {
                    Text("This folder is a git clone: its next sync commits what the import writes.")
                        .font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                }
                if state.hasSeat {
                    rowList
                } else {
                    Text("Choose the folder to open it into. Nothing is written until you press Import and confirm.")
                        .font(.callout).frame(maxWidth: .infinity, minHeight: 120, alignment: .center)
                }
                footer
            } else {
                result
            }
        }
        .padding(20)
        .frame(width: 640)
        .alert("Bring \(chosenCount) \(chosenCount == 1 ? "item" : "items") into \(model.display(state.seat))?", isPresented: $confirming) {
            Button("Import") {
                state.working = true
                let given = state
                Task { state = await model.finishConfigImport(given) }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Into \(seat?.label ?? "this folder"), \(model.display(state.seat))" + (seat?.isWork == true ? ", a folder marked work" : "") + ". "
                 + "Sanduhr writes the files and settings this folder lacks"
                 + (cliCount > 0 ? ", and runs claude to put in \(cliCount == 1 ? "a plugin or MCP server" : "\(cliCount) plugins and MCP servers"), which can fetch from the network" : "")
                 + ". It records them so Undo Import can take them back.")
        }
    }

    private var cliCount: Int {
        state.rows.filter { row in
            guard state.chosen.contains(row.id), case .install = row.status else { return false }
            return true
        }.count
    }

    private var chosenCount: Int {
        state.rows.filter { state.chosen.contains($0.id) && ($0.status != .needsValue || !(state.env[$0.name] ?? "").isEmpty) }.count
    }

    private var rowList: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                ForEach(ConfigPack.Item.Kind.allCases, id: \.self) { kind in
                    let rows = state.rows.filter { $0.kind == kind }
                    if !rows.isEmpty {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(kind.title).font(.callout.weight(.semibold))
                            ForEach(rows) { row($0) }
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.trailing, 8)
        }
        .frame(height: 340)
    }

    @ViewBuilder
    private func row(_ r: ConfigImport.Row) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                if r.choosable {
                    Toggle(isOn: Binding(get: { state.chosen.contains(r.id) },
                                         set: { on in if on { state.chosen.insert(r.id) } else { state.chosen.remove(r.id) } })) {
                        Text(r.name).font(.callout)
                    }
                } else {
                    Text(r.name).font(.callout).padding(.leading, 20)
                }
                if !r.detail.isEmpty {
                    Text(r.detail).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.tail)
                }
            }
            switch r.status {
            case .same:
                Text("The same here already.").font(.caption).foregroundStyle(.secondary).padding(.leading, 20)
            case .differs:
                Text("This folder has its own; it stays.").font(.caption).foregroundStyle(.orange).padding(.leading, 20)
            case .needsValue:
                SecureField("Value for \(r.name)", text: Binding(get: { state.env[r.name] ?? "" }, set: { state.env[r.name] = $0 }))
                    .textFieldStyle(.roundedBorder).frame(maxWidth: 320).padding(.leading, 20)
            case .review:
                Text("Waits in New Mod for Check, Try and Keep; nothing runs before.").font(.caption).foregroundStyle(.secondary).padding(.leading, 20)
            case .install(let cmd):
                Text("Sanduhr runs: " + cmd).font(.caption.monospaced()).foregroundStyle(.secondary)
                    .textSelection(.enabled).lineLimit(3).padding(.leading, 20)
            case .command(let cmd):
                if state.hasSeat && state.cli == .missing {
                    Text("Claude Code's claude wasn't found, so run this yourself:").font(.caption).foregroundStyle(.secondary).padding(.leading, 20)
                }
                HStack(alignment: .top, spacing: 6) {
                    Text(cmd).font(.caption.monospaced()).textSelection(.enabled).lineLimit(3)
                    Button("Copy") {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(cmd, forType: .string)
                    }
                    .controlSize(.small)
                }
                .padding(.leading, 20)
            case .refused(let why):
                Text(why).font(.caption).foregroundStyle(.orange).padding(.leading, 20)
            case .new:
                EmptyView()
            }
        }
    }

    private var footer: some View {
        HStack {
            Text("\(chosenCount) chosen").font(.caption).foregroundStyle(.secondary)
            Spacer()
            Button("Cancel", role: .cancel) { close() }
            if state.working {
                ProgressView().controlSize(.small)
                Text(cliCount > 0 ? "Installing with claude…" : "Importing…").font(.caption).foregroundStyle(.secondary)
            }
            Button("Import…") { confirming = true }
                .keyboardShortcut(.defaultAction)
                .disabled(!state.hasSeat || chosenCount == 0 || state.working)
        }
    }

    @ViewBuilder
    private var result: some View {
        let files = state.done?.steps.filter { $0.kind == .file }.count ?? 0
        let settings = state.done?.steps.filter { $0.kind == .setting || $0.kind == .env }.count ?? 0
        let plugins = state.done?.steps.filter { $0.kind == .plugin }.count ?? 0
        let servers = state.done?.steps.filter { $0.kind == .mcpServer }.count ?? 0
        Text("Brought \(files) \(files == 1 ? "file" : "files") and \(settings) \(settings == 1 ? "setting" : "settings") into \(seat?.label ?? "the folder")."
             + (plugins + servers > 0 ? " Installed \(plugins) \(plugins == 1 ? "plugin" : "plugins") and added \(servers) MCP \(servers == 1 ? "server" : "servers") with claude." : "")
             + (state.staged > 0 ? " \(state.staged) kept \(state.staged == 1 ? "mod waits" : "mods wait") in New Mod for review." : ""))
            .font(.callout).fixedSize(horizontal: false, vertical: true)
        if let f = state.failure {
            Text(f).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        ForEach(state.cliFailures, id: \.self) { line in
            Text("Not put in: " + line).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        if let undone {
            Text(undone.note).font(.caption).foregroundStyle(undone.isClean ? Color.secondary : Color.orange).fixedSize(horizontal: false, vertical: true)
        }
        HStack {
            Spacer()
            if let record = state.done, !record.steps.isEmpty, undone == nil || undone?.isRefused == true {
                Button("Undo Import") {
                    undoing = true
                    Task {
                        let outcome = await model.undoImport(record)
                        undone = outcome
                        // Kept on By Seat after the sheet closes.
                        model.configImportNote = outcome.note
                        undoing = false
                    }
                }
                .disabled(undoing)
            }
            Button("Done") { close() }.keyboardShortcut(.defaultAction)
        }
    }
}
