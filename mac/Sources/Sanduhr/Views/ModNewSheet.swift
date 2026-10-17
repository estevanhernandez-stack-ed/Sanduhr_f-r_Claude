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

/// A publish waiting on its confirm (item 76, slice F).
struct MarketplacePublish: Identifiable, Equatable {
    let mod: KeptMod
    let folder: URL
    let gitPath: String
    let plan: ModMarketplace.Plan

    var id: String { mod.id }
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
            let kept = try store.approve(r.id, fallback: packFolder(r))
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
    func exportKept(_ mod: KeptMod) async {
        let panel = NSSavePanel()
        // No allowedContentTypes: until Sanduhr declares the type, the panel adds the extension
        // a second time. ModPack.destination puts it on when the name lacks it.
        panel.nameFieldStringValue = ModPack.fileName(mod)
        panel.canCreateDirectories = true
        panel.message = "A pack anyone can read and add, with Sanduhr or with Claude Code."
        panel.prompt = "Export"
        guard panel.runModal() == .OK, let picked = panel.url else { return }
        let url = ModPack.destination(picked)
        // The pack says what Check found: check first when it hasn't run this session.
        if keptChecks[mod.id] == nil { await checkKept(mod) }
        var report: ModCheckReport?
        if case .report(let r)? = keptChecks[mod.id] { report = r }
        do {
            try ModPack.write(mod, from: URL(fileURLWithPath: keptStore.path(mod)), to: url, check: report)
            keptNote = "Exported \(mod.name) to \(url.lastPathComponent)."
        } catch {
            keptNote = (error as? ModPack.Failure)?.message ?? ModPack.Failure.writeFailed.message
        }
    }

    /// Publish (item 76, slice F): reads the remembered clone (asking for one the first time, or
    /// when `choose`), then plans publishing `mod` into it. The confirm shows the plan.
    func preparePublish(_ mod: KeptMod, choose: Bool = false) async {
        publishedCommands = nil
        guard let gitPath = ModMarketplace.findGit() else { keptNote = ModMarketplace.Failure.noGit.message; return }
        var folder = UserDefaults.standard.string(forKey: ModMarketplace.folderKey).map { URL(fileURLWithPath: $0) }
        var isDir: ObjCBool = false
        if choose || folder.map({ !FileManager.default.fileExists(atPath: $0.path, isDirectory: &isDir) || !isDir.boolValue }) ?? true {
            guard let picked = askMarketplaceFolder(folder) else { return }
            folder = picked
        }
        guard let folder else { return }
        let market = ModMarketplace(folder: folder, gitPath: gitPath)
        let kept = URL(fileURLWithPath: keptStore.path(mod))
        let words = WatchWords.standard.all()
        keptBusy.insert(mod.id)
        let result = await Task.detached(priority: .userInitiated) {
            Result { try market.plan(mod, kept: kept, repo: try market.inspect(), watchWords: words) }
        }.value
        keptBusy.remove(mod.id)
        switch result {
        case .success(let plan):
            UserDefaults.standard.set(folder.path, forKey: ModMarketplace.folderKey)
            publishing = MarketplacePublish(mod: mod, folder: folder, gitPath: gitPath, plan: plan)
        case .failure(let error):
            keptNote = (error as? ModMarketplace.Failure)?.message ?? ModMarketplace.Failure.notAClone.message
        }
    }

    /// Writes, commits and pushes what the confirm showed.
    func publish(_ request: MarketplacePublish) async {
        let market = ModMarketplace(folder: request.folder, gitPath: request.gitPath)
        let kept = URL(fileURLWithPath: keptStore.path(request.mod))
        let (mod, plan) = (request.mod, request.plan)
        keptBusy.insert(mod.id)
        let result = await Task.detached(priority: .userInitiated) {
            Result { try market.publish(mod, kept: kept, plan: plan) }
        }.value
        keptBusy.remove(mod.id)
        switch result {
        case .success(let outcome) where outcome.pushFailure == nil:
            keptNote = "Published \(mod.name) to \(plan.repo.source) (\(plan.repo.branch), \(outcome.commit)). On another machine, add it with the commands below."
            publishedCommands = plan.commands
        case .success(let outcome):
            keptNote = "Committed \(mod.name) as \(outcome.commit) in \(request.folder.lastPathComponent), but the push didn't go through: \(outcome.pushFailure ?? ""). Push from Terminal with git push."
        case .failure(let error):
            keptNote = (error as? ModMarketplace.Failure)?.message ?? ModMarketplace.Failure.writeFailed.message
        }
    }

    private func askMarketplaceFolder(_ current: URL?) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Use This Clone"
        panel.message = "Choose the folder of a git clone of your marketplace repo. Sanduhr writes plugins/ and .claude-plugin/marketplace.json in it, commits, and pushes when you confirm."
        panel.directoryURL = current ?? FileManager.default.homeDirectoryForCurrentUser
        guard panel.runModal() == .OK else { return nil }
        return panel.url
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
            try store.trySession(r.id, claude: claude, in: folder, fallback: packFolder(r))
            keptNote = nil
            if let folder { UserDefaults.standard.set(folder.path, forKey: Self.tryFolderKey) }
        } catch {
            keptNote = (error as? KeptMods.Failure)?.message ?? KeptMods.Failure.writeFailed.message
        }
    }

    /// A mod opened from a pack names no folder: Try and Keep use the first Claude Code folder
    /// not marked work, so a pack from someone else never starts in a work seat unasked.
    func packFolder(_ r: ModProposalRecord) -> String? {
        r.fromFile ? seats.first { !$0.isWork }?.path : nil
    }

    /// Open a pack (item 76, slice B): checked, unpacked and staged for review, or why not.
    func openPack(_ url: URL, store: ModProposalStore) async {
        let read = await Task.detached(priority: .userInitiated) { () -> Result<ModPackReader.Opened, ModPackReader.Refusal> in
            do { return .success(try ModPackReader.open(url)) } catch let r as ModPackReader.Refusal { return .failure(r) } catch {
                return .failure(ModPackReader.Refusal("Sanduhr can't read that file."))
            }
        }.value
        do {
            switch try store.open(read.get()) {
            case .staged(let r):
                keptNote = nil
                await checkProposal(r, store: store)
            case .alreadyKept(let mod):
                keptNote = "\(mod.name)\(mod.version.map { " " + $0 } ?? "") is kept already, with the same files."
            }
        } catch {
            let reasons = (error as? ModPackReader.Refusal)?.reasons ?? ["Sanduhr can't read that file."]
            keptNote = "Sanduhr can't open \(url.lastPathComponent): " + reasons.joined(separator: " ")
        }
    }

    /// Asks for a pack, then opens it.
    func choosePack(store: ModProposalStore) async {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        if let type = UTType(filenameExtension: ModPack.fileExtension) { panel.allowedContentTypes = [type] }
        panel.message = "Open a .sanduhrpack. Its mod waits here for you to check, try and keep; nothing runs until you do."
        panel.prompt = "Open"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        // A config pack picked here closes New Mod and opens the Open sheet instead.
        await openAnyPack(url, store: store)
        if configImport != nil { newModOpen = false }
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
    @State private var idea = ""
    @State private var showPrompt = false
    @State private var copied = false
    @State private var trying: KeptMod?
    @State private var tryingProposal: ModProposalRecord?
    @State private var forgetting: KeptMod?
    @State private var dismissing: ModProposalRecord?
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
        .alert(approveTitle, isPresented: Binding(get: { approving != nil && !needsTypedName }, set: { if !$0 { approving = nil } })) {
            Button("Keep It") { if let r = approving { Task { await model.approve(r, store: proposals) } } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(approveMessage)
        }
        // A high-risk mod asks for its name typed, in a sheet that can show the name in blue.
        .sheet(isPresented: Binding(get: { approving != nil && needsTypedName }, set: { if !$0 { approving = nil } })) {
            TypedNameConfirm(title: approveTitle, verb: "keep", name: approving?.name ?? "", message: approveMessage, actions: [
                .init(title: "Keep It") {
                    if let r = approving { Task { await model.approve(r, store: proposals) } }
                    approving = nil
                },
            ], cancel: { approving = nil })
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
        .alert(tryProposalTitle,
               isPresented: Binding(get: { tryingProposal != nil && !tryNeedsTypedName }, set: { if !$0 { tryingProposal = nil } })) {
            if let last = model.lastTryFolder {
                Button("Try in \(last.lastPathComponent)") { if let r = tryingProposal { model.tryProposal(r, store: proposals, in: last) } }
            }
            Button("Choose Folder…") {
                if let r = tryingProposal { DispatchQueue.main.async { model.chooseTryFolder(r, store: proposals) } }
            }
            Button("Try in an Empty Folder") { if let r = tryingProposal { model.tryProposal(r, store: proposals) } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(tryProposalMessage)
        }
        .sheet(isPresented: Binding(get: { tryingProposal != nil && tryNeedsTypedName }, set: { if !$0 { tryingProposal = nil } })) {
            TypedNameConfirm(title: tryProposalTitle, verb: "try", name: tryingProposal?.name ?? "", message: tryProposalMessage,
                             actions: tryProposalActions, cancel: { tryingProposal = nil })
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
        .confirmationDialog("Dismiss \(dismissing?.name ?? "the proposal")?", isPresented: Binding(get: { dismissing != nil }, set: { if !$0 { dismissing = nil } })) {
            Button("Dismiss", role: .destructive) { if let r = dismissing { proposals.dismiss(r.id) } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(dismissing?.fromFile == true
                 ? "Sanduhr's copy of the pack's files is deleted. The pack itself is left where it is."
                 : "Its staged files are deleted. Ask Claude again to get it back.")
        }
        .alert(publishTitle, isPresented: Binding(get: { model.publishing != nil }, set: { if !$0 { model.publishing = nil } })) {
            let same = model.publishing?.plan.change == .same
            if model.publishing?.plan.canPublish == true {
                Button("Publish") { if let r = model.publishing { Task { await model.publish(r) } } }
            }
            if same, let commands = model.publishing?.plan.commands {
                Button("Copy Commands") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(commands, forType: .string)
                    model.publishedCommands = commands
                }
            }
            Button("Choose Another Clone…") {
                if let m = model.publishing?.mod { DispatchQueue.main.async { Task { await model.preparePublish(m, choose: true) } } }
            }
            Button(same ? "Done" : "Cancel", role: .cancel) {}
        } message: {
            Text(publishMessage)
        }
        .alert("Export \(exporting?.name ?? "the mod")?", isPresented: Binding(get: { exporting != nil }, set: { if !$0 { exporting = nil } })) {
            Button("Export…") { if let m = exporting { DispatchQueue.main.async { Task { await model.exportKept(m) } } } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(exportMessage)
        }
    }

    private func publishedLine(_ commands: String) -> some View {
        HStack(alignment: .top) {
            Text(commands).font(.caption.monospaced()).textSelection(.enabled)
            Spacer()
            Button("Copy") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(commands, forType: .string)
            }
            .controlSize(.small)
        }
    }

    private var publishTitle: String {
        guard let r = model.publishing else { return "Publish the mod?" }
        let what = r.plan.mod + (r.plan.version.map { " " + $0 } ?? "")
        return r.plan.change == .same ? "\(what) is already published" : "Publish \(what) to \(r.plan.repo.source)?"
    }

    private var publishMessage: String {
        guard let p = model.publishing?.plan else { return "" }
        let size = ByteCountFormatter.string(fromByteCount: Int64(p.bytes), countStyle: .file)
        let what = "\(p.mod)\(p.version.map { " " + $0 } ?? "")"
        var lines: [String] = []
        switch p.change {
        case .same: lines.append("\(p.repo.source) has this copy of \(p.mod) on \(p.repo.branch) already. On another machine, add it with:\n\n\(p.commands)")
        case .add: lines.append("Adds \(what) as plugins/\(p.mod) (\(p.files) \(p.files == 1 ? "file" : "files"), \(size)) to the \(p.repo.name) marketplace, commits on \(p.repo.branch), and pushes to \(p.repo.source).")
        case .update(let old): lines.append("Replaces plugins/\(p.mod)\(old.map { " (" + $0 + ")" } ?? "") with \(what) (\(p.files) \(p.files == 1 ? "file" : "files"), \(size)), commits on \(p.repo.branch), and pushes to \(p.repo.source).")
        }
        if let s = p.secret { lines.append("Refused: \(s) looks like a secret. Edit the mod and keep it again.") }
        if !p.watched.isEmpty { lines.append("It holds a watch word: \(p.watched.joined(separator: ", ")). Anyone who can read the repo reads it.") }
        if p.canPublish { lines.append("The marketplace's list and the README's plugin list change with it. Everyone who can read the repo can read every file. Nothing from your Claude Code folders goes: no settings, no paths.") }
        return lines.joined(separator: "\n\n")
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
        HStack {
            Toggle("Let agents propose mods", isOn: $proposals.allowed)
            Spacer()
            Button("Open Pack…") { Task { await model.choosePack(store: proposals) } }
                .controlSize(.small)
                .help("Opens a .sanduhrpack someone exported. Its mod waits here to be checked, tried and kept.")
        }
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
        if let commands = model.publishedCommands { publishedLine(commands) }
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                if !proposals.pendingFromFiles.isEmpty {
                    Text("Opened from a pack").font(.callout.weight(.semibold))
                    ForEach(proposals.pendingFromFiles) { proposalRow($0) }
                    Divider()
                }
                if !proposals.pendingFromClaude.isEmpty {
                    Text("Proposed by Claude").font(.callout.weight(.semibold))
                    ForEach(proposals.pendingFromClaude) { proposalRow($0) }
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
                    Button("Publish to Marketplace…") { Task { await model.preparePublish(mod) } }
                        .help("Commits it to a git clone of your own marketplace repo and pushes, after you confirm. Any machine adds the repo once with claude plugin marketplace add.")
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
                    Text("\(r.fromFile ? "Opened" : "Proposed") \(r.requestedAt.formatted(date: .abbreviated, time: .shortened)), \(r.files) \(r.files == 1 ? "file" : "files"), \(ByteCountFormatter.string(fromByteCount: Int64(r.bytes), countStyle: .file))")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                if model.keptBusy.contains(r.id) { ProgressView().controlSize(.small) }
                Button("Dismiss…") { dismissing = r }
                    .controlSize(.small)
                    .help(r.fromFile ? "Deletes Sanduhr's copy of the pack's files. The pack itself is left where it is."
                          : "Deletes the proposed files. Claude hears that you dismissed it.")
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
            ?? model.packFolder(r).map { "with \(($0 as NSString).lastPathComponent), your first Claude Code folder not marked work" }
            ?? "with Claude Code's default folder (.claude)"
        var lines = ["Opens Terminal and starts Claude Code \(seat), with \(r.name) loaded for that session only, in a folder you choose or a new empty one. The mod's code runs there with your permissions. It stays a proposal: trying keeps nothing and turns it on nowhere."]
        if case .report(let report)? = model.proposalChecks[r.id] {
            lines.append("\(report.risk.title): \(report.reasons.joined(separator: " "))")
        }
        return lines.joined(separator: "\n\n")
    }

    private var tryProposalTitle: String { "Try \(tryingProposal?.name ?? "the mod") before keeping it?" }

    /// The typed-name sheet's Try buttons, as the alert's: each closes the sheet first.
    private var tryProposalActions: [TypedNameConfirm.Action] {
        guard let r = tryingProposal else { return [] }
        var out: [TypedNameConfirm.Action] = []
        if let last = model.lastTryFolder {
            out.append(.init(title: "Try in \(last.lastPathComponent)") { tryingProposal = nil; model.tryProposal(r, store: proposals, in: last) })
        }
        out.append(.init(title: "Choose Folder…") {
            tryingProposal = nil
            DispatchQueue.main.async { model.chooseTryFolder(r, store: proposals) }
        })
        out.append(.init(title: "Try in an Empty Folder") { tryingProposal = nil; model.tryProposal(r, store: proposals) })
        return out
    }

    private var approveTitle: String { "Keep \(approving?.name ?? "the mod")?" }

    private var approveMessage: String {
        guard let r = approving else { return "" }
        var lines = ["It joins your kept mods. No Claude Code folder loads it until you turn it on there, which asks again."]
        if case .report(let report)? = model.proposalChecks[r.id] {
            lines.append("\(report.risk.title): \(report.reasons.joined(separator: " "))")
        }
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
