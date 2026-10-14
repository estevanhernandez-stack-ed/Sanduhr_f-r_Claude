import AppKit
import SwiftUI

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

    /// Asks for the folder Try starts in, then tries there.
    func chooseTryFolder(_ mod: KeptMod) {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = true
        panel.prompt = "Try Here"
        panel.message = "Claude Code starts in this folder with \(mod.name) loaded for that session only."
        panel.directoryURL = lastTryFolder ?? FileManager.default.homeDirectoryForCurrentUser
        guard panel.runModal() == .OK, let url = panel.url else { return }
        trySession(mod, in: url)
    }
}

/// New Mod, from the Catalog. Build with Claude (slice E): the idea in a sentence, then Copy
/// Prompt puts the prompt on the clipboard. Made in a Session (slice F): keep a mod Claude made in
/// a session, check it, and try it in a new one.
struct NewModSheet: View {
    enum Way: String, CaseIterable, Identifiable {
        case build, session

        var id: String { rawValue }
        var title: String { self == .build ? "Build with Claude" : "Made in a Session" }
    }

    var model: ModsPageModel
    let demo: Bool
    let close: () -> Void
    @State private var way: Way = .build
    @State private var idea = ""
    @State private var showPrompt = false
    @State private var copied = false
    @State private var trying: KeptMod?
    @State private var forgetting: KeptMod?
    @State private var consent: KeptConsent?

    struct KeptConsent: Identifiable {
        let mod: KeptMod
        let folder: String
        var id: String { mod.id + "\n" + folder }
    }

    private var prompt: String { ModPrompt.text(idea: idea) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New Mod").font(.title3.weight(.semibold))
            Picker("Way", selection: $way) {
                ForEach(Way.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented).labelsHidden().fixedSize()
            switch way {
            case .build: build
            case .session: session
            }
        }
        .padding(20)
        .frame(width: 580)
        .task { await model.refreshKept() }
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
        .alert(consentTitle, isPresented: Binding(get: { consent != nil }, set: { if !$0 { consent = nil } })) {
            Button("Turn On") { if let c = consent { Task { await model.setKept(c.mod, on: true, folder: c.folder) } } }
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
        Text("The prompt names no folder on this Mac and carries none of your settings. Nothing is installed from here: the mod runs only in the session that builds it. Keep it afterwards from Made in a Session.")
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

    // MARK: Made in a Session

    @ViewBuilder
    private var session: some View {
        Text("A mod Claude makes in a session lives in that session's folder, and Claude Code deletes it after a while. Keep copies it, unchanged, into Sanduhr's own folder and checks what it can do. None of its code runs until you try it or turn it on in a folder, and each asks first.")
            .font(.caption).foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        if let note = model.keptNote {
            Text(note).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
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
        .frame(height: 340)
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
                    Text("Kept \(mod.keptAt.formatted(date: .abbreviated, time: .shortened)) from session \(mod.session), \(mod.files) \(mod.files == 1 ? "file" : "files")")
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
