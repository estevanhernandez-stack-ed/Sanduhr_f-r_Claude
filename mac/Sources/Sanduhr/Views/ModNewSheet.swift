import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// New Mod's two ways in: the prompt, or what Claude made (in a session, or proposed).
enum NewModWay: String, CaseIterable, Identifiable, Sendable {
    case build, fromClaude

    var id: String { rawValue }
    var title: String { self == .build ? "Build with Claude" : "From Claude" }
}

/// A mod made in a session, as New Mod lists it (slice F): where it is, and its kept copy when
/// the files are the same as one Sanduhr kept.
struct SessionModRow: Identifiable, Equatable, Sendable {
    let item: ModItem
    let session: String
    var kept: KeptMod?
    /// Why it can't be kept, nil when it can.
    var refusal: String?

    var id: String { item.id }
}

/// Slice F: finding mods made in sessions, keeping them, checking and trying the kept copies.
extension ModsPageModel {
    /// Every folder's session mods, read again with what is kept. In demo mode no work folder's.
    func refreshKept() async {
        let store = keptStore
        let items = inventory.flatMap(\.items).compactMap { item -> (ModItem, String)? in
            guard case .devMods(let session) = item.origin, item.path != nil, item.state != .missing else { return nil }
            return (item, session)
        }
        let installer = installer
        let folders = inventory.map(\.folder)
        let (rows, kept, states) = await Task.detached(priority: .userInitiated) {
            let kept = store.list()
            var states: [String: [String: KeptModState]] = [:]
            for mod in kept {
                for folder in folders { states[mod.id, default: [:]][folder] = store.state(mod, folder: folder, installer: installer) }
            }
            let rows = items.map { item, session in
                var row = SessionModRow(item: item, session: session)
                let url = URL(fileURLWithPath: item.path ?? "")
                do {
                    _ = try KeptMods.files(in: url)
                    let name = try KeptMods.manifest(url).name
                    let stamp = KeptMods.stamp(of: url)
                    row.kept = kept.first { $0.name == name && $0.stamp == stamp }
                } catch {
                    row.refusal = (error as? KeptMods.Failure)?.message ?? "It can't be read."
                }
                return row
            }
            return (rows, kept, states)
        }.value
        sessionMods = rows
        self.kept = kept
        keptStates = states
        // list_catalog's file follows what is kept and where it is on (slice G2).
        ModProposalStore.shared.publishCatalog()
    }

    /// The folders a kept mod can be turned on in: every Claude Code folder, no work folder in demo mode.
    func keptFolders(demo: Bool) -> [String] {
        inventory.map(\.folder).filter { !(demo && isWork($0)) }
    }

    func keptState(_ mod: KeptMod, folder: String) -> KeptModState {
        keptStates[mod.id]?[folder] ?? .off
    }

    /// Another plugin of the same name already loads in the folder (not a kept copy, not a
    /// session's): turning this one on there would shadow one of the two.
    func keptClash(_ mod: KeptMod, folder: String) -> Bool {
        inventory.first { $0.folder == folder }?.items.contains { item in
            guard item.name == mod.name else { return false }
            if case .devMods = item.origin { return false }
            return item.path.flatMap(keptStore.keptEntry) == nil
        } ?? false
    }

    /// On (after the consent) or Off for one folder, by receipt.
    func setKept(_ mod: KeptMod, on: Bool, folder: String) async {
        let key = mod.id + "\n" + folder
        guard !keptBusy.contains(key) else { return }
        keptBusy.insert(key)
        defer { keptBusy.remove(key) }
        let store = keptStore, installer = installer
        let failed = await Task.detached(priority: .userInitiated) { () -> String? in
            do {
                if on { try store.turnOn(mod, folder: folder, installer: installer) } else {
                    try store.turnOff(mod.name, folder: folder, installer: installer)
                }
                return nil
            } catch let f as KeptMods.Failure {
                return f.message
            } catch {
                return "Sanduhr couldn't change \(installer.configFile(.meters, folder: folder)): it was changed while writing, or isn't JSON Sanduhr can edit."
            }
        }.value
        keptNote = failed
        await refreshKept()
        // The Catalog shows the mod on or off there without Read Again.
        await load(linked: lastLinked)
    }

    func shownSessionMods(demo: Bool) -> [SessionModRow] {
        sessionMods.filter { !(demo && isWork($0.item.folder)) }
    }

    /// Keep: copies the mod, then checks the copy.
    func keep(_ row: SessionModRow) async {
        guard let path = row.item.path, !keptBusy.contains(row.id) else { return }
        keptBusy.insert(row.id)
        defer { keptBusy.remove(row.id) }
        let store = keptStore
        let result = await Task.detached(priority: .userInitiated) { () -> Result<KeptMod, KeptMods.Failure> in
            do { return .success(try store.keep(path, session: row.session, folder: row.item.folder)) } catch {
                return .failure((error as? KeptMods.Failure) ?? .writeFailed)
            }
        }.value
        switch result {
        case .success(let mod):
            keptNote = nil
            await refreshKept()
            await checkKept(mod)
        case .failure(let f):
            keptNote = f.message
        }
    }

    /// `claude plugin validate --json` on the kept copy: reads its files, runs none of its code.
    func checkKept(_ mod: KeptMod) async {
        guard !keptBusy.contains(mod.id) else { return }
        guard let claude else {
            keptChecks[mod.id] = .unavailable
            return
        }
        keptBusy.insert(mod.id)
        defer { keptBusy.remove(mod.id) }
        let dir = keptStore.path(mod)
        keptChecks[mod.id] = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: dir) }.value
    }

    func forget(_ mod: KeptMod) async {
        let using = keptStates[mod.id]?.filter { $0.value == .on }.count ?? 0
        guard using == 0 else {
            keptNote = "\(mod.name) is on in \(using) \(using == 1 ? "folder" : "folders"). Turn it off there first."
            return
        }
        let store = keptStore
        let failed = await Task.detached(priority: .userInitiated) { () -> KeptMods.Failure? in
            do { try store.forget(mod); return nil } catch { return (error as? KeptMods.Failure) ?? .writeFailed }
        }.value
        keptNote = failed?.message
        keptChecks[mod.id] = nil
        await refreshKept()
    }

    /// Check for a proposed mod's staged files (slice G): reads them, runs none of the code.
    func checkProposal(_ r: ModProposalRecord, store: ModProposalStore) async {
        guard !keptBusy.contains(r.id) else { return }
        guard let claude else {
            proposalChecks[r.id] = .unavailable
            return
        }
        keptBusy.insert(r.id)
        defer { keptBusy.remove(r.id) }
        let dir = store.stagedPath(r)
        proposalChecks[r.id] = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: dir) }.value
    }

    /// Approve: the proposal becomes a kept mod, checked as one.
    func approve(_ r: ModProposalRecord, store: ModProposalStore) async {
        do {
            let kept = try store.approve(r.id)
            keptNote = nil
            if let outcome = proposalChecks[r.id] { keptChecks[kept.id] = outcome }
            proposalChecks[r.id] = nil
            await refreshKept()
            if keptChecks[kept.id] == nil { await checkKept(kept) }
        } catch {
            keptNote = (error as? KeptMods.Failure)?.message ?? KeptMods.Failure.writeFailed.message
        }
    }

    /// Try in a Session is offered once Check read a report Claude Code would load.
    func canTry(_ mod: KeptMod) -> Bool {
        guard claude != nil, case .report(let r)? = keptChecks[mod.id] else { return false }
        return r.passed
    }

    /// The folder Try last started in, while it is still there.
    static let tryFolderKey = "keptModTryFolder"

    var lastTryFolder: URL? {
        guard let path = UserDefaults.standard.string(forKey: Self.tryFolderKey) else { return nil }
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue else { return nil }
        return URL(fileURLWithPath: path)
    }

    /// Try in a Session, in `folder` (remembered for next time) or a new empty one.
    func trySession(_ mod: KeptMod, in folder: URL? = nil) {
        guard let claude, canTry(mod) else { return }
        do {
            try keptStore.trySession(mod, claude: claude, in: folder)
            if let folder { UserDefaults.standard.set(folder.path, forKey: Self.tryFolderKey) }
        } catch {
            keptNote = (error as? KeptMods.Failure)?.message ?? KeptMods.Failure.writeFailed.message
        }
    }

    /// Export (item 76, slice A): asks where, then writes the kept copy as a `.sanduhrpack`.
    func exportKept(_ mod: KeptMod) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = ModPack.fileName(mod)
        if let type = UTType(filenameExtension: ModPack.fileExtension) { panel.allowedContentTypes = [type] }
        panel.canCreateDirectories = true
        panel.message = "A pack anyone can read and add, with Sanduhr or with Claude Code."
        panel.prompt = "Export"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        var report: ModCheckReport?
        if case .report(let r)? = keptChecks[mod.id] { report = r }
        do {
            try ModPack.write(mod, from: URL(fileURLWithPath: keptStore.path(mod)), to: url, check: report)
            keptNote = "Exported \(mod.name) to \(url.lastPathComponent)."
        } catch {
            keptNote = (error as? ModPack.Failure)?.message ?? ModPack.Failure.writeFailed.message
        }
    }

    /// Asks for the folder Try starts in, then tries there.
    func chooseTryFolder(_ mod: KeptMod) {
        guard let url = askTryFolder(mod.name) else { return }
        trySession(mod, in: url)
    }

    private func askTryFolder(_ name: String) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = true
        panel.prompt = "Try Here"
        panel.message = "Claude Code starts in this folder with \(name) loaded for that session only."
        panel.directoryURL = lastTryFolder ?? FileManager.default.homeDirectoryForCurrentUser
        guard panel.runModal() == .OK else { return nil }
        return panel.url
    }

    /// Try in a Session on a waiting proposal (slice G3): offered once Check passed on its staged
    /// copy and while that copy is there. Trying keeps nothing and leaves the proposal waiting.
    func canTryProposal(_ r: ModProposalRecord, store: ModProposalStore) -> Bool {
        Self.canTryProposal(claude: claude, staged: store.isStaged(r), check: proposalChecks[r.id])
    }

    nonisolated static func canTryProposal(claude: String?, staged: Bool, check: ModCheckOutcome?) -> Bool {
        guard claude != nil, staged, case .report(let report)? = check else { return false }
        return report.passed
    }

    func tryProposal(_ r: ModProposalRecord, store: ModProposalStore, in folder: URL? = nil) {
        guard let claude, canTryProposal(r, store: store) else {
            if !store.isStaged(r) { keptNote = KeptMods.Failure.gone.message }
            return
        }
        do {
            try store.trySession(r.id, claude: claude, in: folder)
            keptNote = nil
            if let folder { UserDefaults.standard.set(folder.path, forKey: Self.tryFolderKey) }
        } catch {
            keptNote = (error as? KeptMods.Failure)?.message ?? KeptMods.Failure.writeFailed.message
        }
    }

    func chooseTryFolder(_ r: ModProposalRecord, store: ModProposalStore) {
        guard let url = askTryFolder(r.name) else { return }
        tryProposal(r, store: store, in: url)
    }
}

/// New Mod, from the Catalog. Build with Claude (slice E): the idea in a sentence, then Copy
/// Prompt puts the prompt on the clipboard. From Claude (slices F, G): keep a mod Claude made in
/// a session or one it proposed, check it, and try it in a new one.
struct NewModSheet: View {
    var model: ModsPageModel
    let demo: Bool
    let close: () -> Void
    var proposals = ModProposalStore.shared
    @State private var way: NewModWay = .build
    @State private var approving: ModProposalRecord?
    @State private var typedName = ""
    @State private var idea = ""
    @State private var showPrompt = false
    @State private var copied = false
    @State private var trying: KeptMod?
    @State private var tryingProposal: ModProposalRecord?
    @State private var tryTypedName = ""
    @State private var forgetting: KeptMod?
    @State private var exporting: KeptMod?
    @State private var consent: KeptConsent?

    struct KeptConsent: Identifiable {
        let mod: KeptMod
        let folder: String
        /// The propose_install request it answers, if any.
        var request: String?
        var id: String { mod.id + "\n" + folder }
    }

    private var prompt: String { ModPrompt.text(idea: idea, canPropose: proposals.allowed) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New Mod").font(.title3.weight(.semibold))
            Picker("Way", selection: $way) {
                ForEach(NewModWay.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented).labelsHidden().fixedSize()
            switch way {
            case .build: build
            case .fromClaude: session
            }
        }
        .padding(20)
        .frame(width: 600)
        .onAppear { way = model.newModWay }
        .task { await model.refreshKept() }
        .alert(approveTitle, isPresented: Binding(get: { approving != nil }, set: { if !$0 { approving = nil; typedName = "" } })) {
            if needsTypedName { TextField("The mod's name", text: $typedName) }
            Button("Keep It") {
                if let r = approving { Task { await model.approve(r, store: proposals) } }
                typedName = ""
            }
            .disabled(needsTypedName && typedName != approving?.name)
            Button("Cancel", role: .cancel) { typedName = "" }
        } message: {
            Text(approveMessage)
        }
        .alert("Try \(trying?.name ?? "the mod") in a session?", isPresented: Binding(get: { trying != nil }, set: { if !$0 { trying = nil } })) {
            if let last = model.lastTryFolder {
                Button("Try in \(last.lastPathComponent)") { if let m = trying { model.trySession(m, in: last) } }
            }
            Button("Choose Folder…") {
                if let m = trying { DispatchQueue.main.async { model.chooseTryFolder(m) } }
            }
            Button("Try in an Empty Folder") { if let m = trying { model.trySession(m) } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(tryMessage)
        }
        .alert("Try \(tryingProposal?.name ?? "the mod") before keeping it?",
               isPresented: Binding(get: { tryingProposal != nil }, set: { if !$0 { tryingProposal = nil; tryTypedName = "" } })) {
            let blocked = tryNeedsTypedName && tryTypedName != tryingProposal?.name
            if tryNeedsTypedName { TextField("The mod's name", text: $tryTypedName) }
            if let last = model.lastTryFolder {
                Button("Try in \(last.lastPathComponent)") { if let r = tryingProposal { model.tryProposal(r, store: proposals, in: last) } }
                    .disabled(blocked)
            }
            Button("Choose Folder…") {
                if let r = tryingProposal { DispatchQueue.main.async { model.chooseTryFolder(r, store: proposals) } }
            }
            .disabled(blocked)
            Button("Try in an Empty Folder") { if let r = tryingProposal { model.tryProposal(r, store: proposals) } }
                .disabled(blocked)
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(tryProposalMessage)
        }
        .alert(consentTitle, isPresented: Binding(get: { consent != nil }, set: { if !$0 { consent = nil } })) {
            Button("Turn On") {
                if let c = consent {
                    Task {
                        await model.setKept(c.mod, on: true, folder: c.folder)
                        if let id = c.request, model.keptState(c.mod, folder: c.folder) == .on {
                            proposals.decideInstall(id, approved: true)
                        }
                    }
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(consentMessage)
        }
        .confirmationDialog("Forget \(forgetting?.name ?? "the mod")?", isPresented: Binding(get: { forgetting != nil }, set: { if !$0 { forgetting = nil } })) {
            Button("Forget", role: .destructive) { if let m = forgetting { Task { await model.forget(m) } } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Deletes Sanduhr's copy. The session's own folder is left as it is, until Claude Code cleans it up.")
        }
        .alert("Export \(exporting?.name ?? "the mod")?", isPresented: Binding(get: { exporting != nil }, set: { if !$0 { exporting = nil } })) {
            Button("Export…") { if let m = exporting { DispatchQueue.main.async { model.exportKept(m) } } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(exportMessage)
        }
    }

    private var exportMessage: String {
        guard let m = exporting else { return "" }
        let size = ByteCountFormatter.string(fromByteCount: Int64(m.bytes), countStyle: .file)
        return "Writes one .sanduhrpack file with its \(m.files) \(m.files == 1 ? "file" : "files") (\(size)) and what Check found. Nothing from your Claude Code folders travels: no settings, no paths, no names of folders. Anyone you give it to can read every file, and can add it with Sanduhr or with claude plugin marketplace add on the unzipped folder."
    }

    // MARK: Build with Claude

    @ViewBuilder
    private var build: some View {
        Text("Say what the mod should do in a sentence. Copy Prompt puts a ready prompt on the clipboard: paste it into Claude Code or Claude Desktop, and Claude builds the mod in that session, where you can watch it as it's written.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        TextField("A band that shows my CI runs", text: $idea, axis: .vertical)
            .lineLimit(2...4)
            .textFieldStyle(.roundedBorder)
            .accessibilityLabel("The mod's idea")
            .onChange(of: idea) { copied = false }
        if ModPrompt.idea(idea).count >= ModPrompt.maxIdea {
            Text("The prompt keeps the first \(ModPrompt.maxIdea) characters.")
                .font(.caption).foregroundStyle(.orange)
        }
        DisclosureGroup("The prompt", isExpanded: $showPrompt) {
            ScrollView {
                Text(prompt)
                    .font(.caption.monospaced())
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
            }
            .frame(height: 220)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.secondary.opacity(0.08)))
        }
        .font(.callout)
        Text("The prompt names no folder on this Mac and carries none of your settings. Nothing is installed from here: the mod runs only in the session that builds it. Keep it afterwards from From Claude.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        HStack {
            if copied {
                Label("Copied. Paste it into a Claude Code session.", systemImage: "checkmark.circle.fill")
                    .font(.caption).foregroundStyle(.green)
            }
            Spacer()
            Button("Done") { close() }.keyboardShortcut(.cancelAction)
            Button("Copy Prompt") { copy() }.keyboardShortcut(.defaultAction)
        }
    }

    private func copy() {
        let board = NSPasteboard.general
        board.clearContents()
        board.setString(prompt, forType: .string)
        copied = true
    }

    // MARK: From Claude

    @ViewBuilder
    private var session: some View {
        @Bindable var proposals = proposals
        Toggle("Let agents propose mods", isOn: $proposals.allowed)
            .help("Lets Claude send a mod it built to Sanduhr with the propose_mod tool. You review its files and what it can do, then keep it or dismiss it; nothing is turned on anywhere by a proposal.")
        Text("Off, Sanduhr's MCP server refuses propose_mod, propose_install and list_catalog. On, Claude can see your kept mods and where they're on, send a mod for you to review, or ask to turn a kept one on in a folder; each waits here for you.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        Text("A mod Claude makes in a session lives in that session's folder, and Claude Code deletes it after a while. Keep copies it, unchanged, into Sanduhr's own folder and checks what it can do. None of its code runs until you try it or turn it on in a folder, and each asks first.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        if let note = model.keptNote {
            Text(note).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                if !proposals.pending.isEmpty {
                    Text("Proposed by Claude").font(.callout.weight(.semibold))
                    ForEach(proposals.pending) { proposalRow($0) }
                    Divider()
                }
                let asks = proposals.pendingInstalls.filter { !(demo && model.isWork($0.folder)) }
                if !asks.isEmpty {
                    Text("Claude asks to turn on").font(.callout.weight(.semibold))
                    ForEach(asks) { installRow($0) }
                    Divider()
                }
                Text("In your sessions").font(.callout.weight(.semibold))
                let rows = model.shownSessionMods(demo: demo)
                if !model.loaded {
                    Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
                } else if rows.isEmpty {
                    Text("No mod made in a session right now. Build one with Claude, then come back here.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                ForEach(rows) { sessionRow($0) }
                Divider()
                Text("Kept").font(.callout.weight(.semibold))
                if model.kept.isEmpty {
                    Text("Nothing kept yet.").font(.caption).foregroundStyle(.secondary)
                }
                ForEach(model.kept) { keptRow($0) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.trailing, 8)
        }
        .frame(height: 380)
        HStack {
            Spacer()
            Button("Done") { close() }.keyboardShortcut(.defaultAction)
        }
    }

    private func sessionRow(_ row: SessionModRow) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(row.item.name + (row.item.version.map { " " + $0 } ?? "")).font(.callout)
                Text("Made in session \(row.session.prefix(8)), \(model.display(row.item.folder))")
                    .font(.caption).foregroundStyle(.secondary)
                if let refusal = row.refusal {
                    Text(refusal).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer()
            if model.keptBusy.contains(row.id) {
                ProgressView().controlSize(.small)
            } else if row.kept != nil {
                Label("Kept", systemImage: "checkmark").font(.caption).foregroundStyle(.secondary)
            } else {
                Button("Keep") { Task { await model.keep(row) } }
                    .controlSize(.small)
                    .disabled(row.refusal != nil)
                    .help("Copies its files into Sanduhr's folder, read-only, and checks what it can do. Runs none of its code.")
            }
        }
    }

    private func keptRow(_ mod: KeptMod) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(mod.name + (mod.version.map { " " + $0 } ?? "")).font(.callout)
                    Text("Kept \(mod.keptAt.formatted(date: .abbreviated, time: .shortened)) \(mod.proposed == true ? "from Claude's proposal" : "from session \(mod.session)"), \(mod.files) \(mod.files == 1 ? "file" : "files")")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                if model.keptBusy.contains(mod.id) { ProgressView().controlSize(.small) }
                Button("Try in a Session…") { trying = mod }
                    .controlSize(.small)
                    .disabled(!model.canTry(mod))
                    .help(model.canTry(mod)
                          ? "Opens Terminal with Claude Code and this mod loaded, in a folder you choose or an empty one. Runs its code."
                          : "Check has to read a report Claude Code would load first.")
                Menu {
                    Button("Check Again") { Task { await model.checkKept(mod) } }
                    Button("Show in Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: model.keptStore.path(mod))])
                    }
                    Button("Export…") { exporting = mod }
                        .help("Writes a .sanduhrpack to share it with another Mac or person.")
                    Divider()
                    Button("Forget…", role: .destructive) { forgetting = mod }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
                .menuStyle(.borderlessButton).fixedSize()
                .accessibilityLabel("More for \(mod.name)")
            }
            if !mod.description.isEmpty {
                Text(mod.description).font(.caption).fixedSize(horizontal: false, vertical: true)
            }
            if let outcome = model.keptChecks[mod.id] {
                ModRiskCard(outcome: outcome)
                folderLines(mod)
            } else {
                Button("Check") { Task { await model.checkKept(mod) } }
                    .controlSize(.small)
                    .help("Runs claude plugin validate --json on the kept copy. It reads the files and runs none of the mod's code.")
            }
        }
        .padding(.vertical, 2)
    }

    private func proposalRow(_ r: ModProposalRecord) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(r.name + (r.version.map { " " + $0 } ?? "")).font(.callout)
                    Text("Proposed \(r.requestedAt.formatted(date: .abbreviated, time: .shortened)), \(r.files) \(r.files == 1 ? "file" : "files"), \(ByteCountFormatter.string(fromByteCount: Int64(r.bytes), countStyle: .file))")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                if model.keptBusy.contains(r.id) { ProgressView().controlSize(.small) }
                Button("Dismiss") { proposals.dismiss(r.id) }
                    .controlSize(.small)
                    .help("Deletes the proposed files. Claude hears that you dismissed it.")
                Button("Try in a Session…") { tryingProposal = r }
                    .controlSize(.small)
                    .disabled(!model.canTryProposal(r, store: proposals))
                    .help(!proposals.isStaged(r) ? KeptMods.Failure.gone.message
                          : model.canTryProposal(r, store: proposals)
                          ? "Opens Terminal with Claude Code and the proposed files loaded for that session only. Runs its code. It stays a proposal."
                          : "Check has to read a report Claude Code would load first.")
                Button("Keep…") { approving = r }
                    .controlSize(.small)
                    .disabled(!proposalPassed(r))
                    .help(proposalPassed(r) ? "Keeps it with your kept mods. Turning it on in a folder asks again." : "Check has to read a report Claude Code would load first.")
            }
            Text(r.summary).font(.caption).fixedSize(horizontal: false, vertical: true)
            if !proposals.isStaged(r) {
                Text(KeptMods.Failure.gone.message).font(.caption).foregroundStyle(.orange)
            }
            if let outcome = model.proposalChecks[r.id] {
                ModRiskCard(outcome: outcome)
            } else {
                Text("Checking what it can do…").font(.caption).foregroundStyle(.secondary)
                    .task { await model.checkProposal(r, store: proposals) }
            }
            DisclosureGroup("Its files") {
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(proposals.files(r), id: \.path) { f in
                        DisclosureGroup(f.path) {
                            ScrollView {
                                Text(f.text)
                                    .font(.caption.monospaced())
                                    .textSelection(.enabled)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(6)
                            }
                            .frame(maxHeight: 200)
                            .background(RoundedRectangle(cornerRadius: 4).fill(Color.secondary.opacity(0.08)))
                        }
                        .font(.caption.monospaced())
                    }
                }
            }
            .font(.caption)
        }
        .padding(.vertical, 2)
    }

    private func installRow(_ r: ModInstallRecord) -> some View {
        let mod = model.kept.first { $0.name == r.name }
        return VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(r.name) in \(model.display(r.folder))").font(.callout)
                    Text("Asked \(r.requestedAt.formatted(date: .abbreviated, time: .shortened))")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Button("Dismiss") { proposals.decideInstall(r.id, approved: false) }
                    .controlSize(.small)
                Button("Turn On…") {
                    if let mod { consent = KeptConsent(mod: mod, folder: r.folder, request: r.id) }
                }
                .controlSize(.small)
                .disabled(mod.map { !model.canTry($0) || model.keptClash($0, folder: r.folder) } ?? true)
                .help(mod == nil ? "Sanduhr no longer keeps this mod." : "Asks first, as Turn On does on the mod's own line.")
            }
            if let mod {
                if let outcome = model.keptChecks[mod.id] {
                    ModRiskCard(outcome: outcome)
                } else {
                    Text("Checking what it can do…").font(.caption).foregroundStyle(.secondary)
                        .task { await model.checkKept(mod) }
                }
            } else {
                Text("This mod isn't kept any more, so it can't be turned on.").font(.caption).foregroundStyle(.orange)
            }
        }
        .padding(.vertical, 2)
    }

    private func proposalPassed(_ r: ModProposalRecord) -> Bool {
        guard case .report(let report)? = model.proposalChecks[r.id] else { return false }
        return report.passed
    }

    /// A high risk asks for the mod's name typed out.
    private var needsTypedName: Bool {
        guard let r = approving, case .report(let report)? = model.proposalChecks[r.id] else { return false }
        return report.risk == .high
    }

    /// Trying a high-risk proposal asks for its name typed out, as keeping it does.
    private var tryNeedsTypedName: Bool {
        guard let r = tryingProposal, case .report(let report)? = model.proposalChecks[r.id] else { return false }
        return report.risk == .high
    }

    private var tryProposalMessage: String {
        guard let r = tryingProposal else { return "" }
        let seat = proposals.folderPath(r).map { "with \(($0 as NSString).lastPathComponent), the Claude Code folder whose session proposed it" }
            ?? "with Claude Code's default folder (.claude)"
        var lines = ["Opens Terminal and starts Claude Code \(seat), with \(r.name) loaded for that session only, in a folder you choose or a new empty one. The mod's code runs there with your permissions. It stays a proposal: trying keeps nothing and turns it on nowhere."]
        if case .report(let report)? = model.proposalChecks[r.id] {
            lines.append("\(report.risk.title): \(report.reasons.joined(separator: " "))")
        }
        if tryNeedsTypedName { lines.append("Type \(r.name) to try it.") }
        return lines.joined(separator: "\n\n")
    }

    private var approveTitle: String { "Keep \(approving?.name ?? "the mod")?" }

    private var approveMessage: String {
        guard let r = approving else { return "" }
        var lines = ["It joins your kept mods. No Claude Code folder loads it until you turn it on there, which asks again."]
        if case .report(let report)? = model.proposalChecks[r.id] {
            lines.append("\(report.risk.title): \(report.reasons.joined(separator: " "))")
        }
        if needsTypedName { lines.append("Type \(r.name) to keep it.") }
        return lines.joined(separator: "\n\n")
    }

    /// The mod per Claude Code folder: On asks first, Off takes it out by receipt.
    @ViewBuilder
    private func folderLines(_ mod: KeptMod) -> some View {
        let folders = model.keptFolders(demo: demo)
        if !folders.isEmpty {
            VStack(alignment: .leading, spacing: 4) {
                Text("In your Claude Code folders").font(.caption.weight(.semibold))
                ForEach(folders, id: \.self) { folder in
                    folderLine(mod, folder: folder)
                }
            }
            .padding(.leading, 8)
        }
    }

    private func folderLine(_ mod: KeptMod, folder: String) -> some View {
        let state = model.keptState(mod, folder: folder)
        let clash = model.keptClash(mod, folder: folder)
        return HStack(spacing: 8) {
            Text(model.display(folder)).font(.caption).frame(minWidth: 140, alignment: .leading)
            Text(stateText(state, clash: clash)).font(.caption).foregroundStyle(clash && state == .off ? .orange : .secondary)
            Spacer()
            if model.keptBusy.contains(mod.id + "\n" + folder) {
                ProgressView().controlSize(.small)
            } else {
                switch state {
                case .on:
                    Button("Turn Off") { Task { await model.setKept(mod, on: false, folder: folder) } }
                        .help("Takes the kept copy out of this folder's plugin folders list, leaving settings.json as it was before.")
                case .otherVersion:
                    Button("Use This Version…") { consent = KeptConsent(mod: mod, folder: folder) }
                        .disabled(!model.canTry(mod))
                case .off:
                    Button("Turn On…") { consent = KeptConsent(mod: mod, folder: folder) }
                        .disabled(!model.canTry(mod) || clash)
                        .help(clash ? "A plugin with this name already loads in this folder." : "Asks first.")
                }
            }
        }
        .controlSize(.small)
    }

    private func stateText(_ state: KeptModState, clash: Bool) -> String {
        switch state {
        case .on: "On, new sessions load it"
        case .otherVersion(let stamp): "Another version is on (\(stamp.prefix(7)))"
        case .off: clash ? "Another plugin has this name here" : "Off"
        }
    }

    private var consentTitle: String {
        guard let c = consent else { return "" }
        return "Turn on \(c.mod.name) in \(model.display(c.folder))?"
    }

    private var consentMessage: String {
        guard let c = consent else { return "" }
        var lines = ["Adds the kept copy to CLAUDE_CODE_PLUGIN_DIRS in \(model.display(model.installer.configFile(.meters, folder: c.folder))). New sessions in that folder load it and run its code with your permissions. Turn Off takes it out again and leaves the file as it was."]
        if case .report(let r)? = model.keptChecks[c.mod.id] {
            lines.append("\(r.risk.title): \(r.reasons.joined(separator: " "))")
        }
        return lines.joined(separator: "\n\n")
    }

    private var tryMessage: String {
        guard let mod = trying else { return "" }
        let seat = mod.folder.map { " in \(model.display($0))" } ?? ""
        var lines = ["Opens Terminal and starts Claude Code\(seat) with \(mod.name) loaded for that session only, in a folder you choose or a new empty one. The mod's code runs there with your permissions."]
        if case .report(let r)? = model.keptChecks[mod.id] {
            lines.append("\(r.risk.title): \(r.reasons.first ?? "")")
        }
        return lines.joined(separator: "\n\n")
    }
}
