import AppKit
import SwiftUI

/// Settings, Accounts, Data (item 44): what Sanduhr keeps for the selected account and what
/// Claude can see of it. Meter history (item 43), live activity (item 45) and the record with its
/// project names (item 46) and sharing (item 47) work.
///
/// Leaving Keep a record, or unlinking the folder while recording, asks whether to erase what
/// was kept (the choice changes first: it is the tombstone). "Erase this account's data" deletes
/// the meter history and the record after a confirmation naming both.
///
/// Folder paths show on this page only (as `~/…`); they never reach a log or state.yaml. The
/// organization match runs in the view model, which returns only the folder to suggest.
struct AccountDataSection: View {
    var vm: UsageViewModel
    let label: String

    @State private var folders: [ClaudeCodeFolders.Folder] = []
    @State private var suggestion: ClaudeCodeFolders.Folder?
    @State private var confirmingErase = false
    @State private var pendingMove: PendingMove?
    @State private var note: Note?
    /// A folder whose record may be erased, waiting for "Keep the record or erase it?".
    @State private var pendingRecordErase: String?
    @State private var confirmingEraseAll = false

    /// A one-line result under the section: green for done, red for a refusal.
    struct Note: Equatable {
        let text: String
        let isError: Bool
    }

    /// A folder already linked to another account, waiting for the move confirmation.
    struct PendingMove: Equatable {
        let path: String
        let owner: String
    }

    static let chooseTag = "\u{0}choose"
    private let home = NSHomeDirectory()
    private var choices: AccountDataChoices { vm.dataChoices(for: label) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Data").font(.headline)
                Caption("What Sanduhr keeps for this account and what Claude can see of it. The meters themselves never change with these choices, and nothing leaves this Mac.")
            }
            historyRow
            folderRow
            DataChoiceRows(vm: vm, label: label, stoppedRecording: offerRecordErase)
            eraseAllRow
            if let note {
                Text(note.text)
                    .font(.caption)
                    .foregroundStyle(Color.hex(note.isError ? "f87171" : "4ade80"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        // Rediscover when the account or its link changes: a folder freed elsewhere or unlinked
        // here can be suggested again.
        .task(id: "\(label)|\(choices.folder ?? "")") { await discover() }
        .confirmationDialog("Erase this account's meter history?", isPresented: $confirmingErase,
                            titleVisibility: .visible) {
            Button("Erase History", role: .destructive) {
                vm.eraseHistory(label)
                note = Note(text: "Meter history erased.", isError: false)
            }
            Button("Keep It", role: .cancel) {}
        } message: {
            Text("Sanduhr no longer records this account's meters. The history it kept so far can be deleted from this Mac now, or kept until you erase it or remove the account.")
        }
        .confirmationDialog("This folder is linked to another account.",
                            isPresented: Binding(get: { pendingMove != nil },
                                                 set: { if !$0 { pendingMove = nil } }),
                            titleVisibility: .visible, presenting: pendingMove) { move in
            Button("Move It to \(label)") {
                let before = choices
                vm.linkFolder(move.path, to: label, move: true)
                pendingMove = nil
                afterFolderChange(from: before)
            }
            Button("Cancel", role: .cancel) { pendingMove = nil }
        } message: { move in
            Text("A Claude Code folder belongs to one account. Moving it unlinks it from \(move.owner).")
        }
        .confirmationDialog("Keep the record or erase it?",
                            isPresented: Binding(get: { pendingRecordErase != nil },
                                                 set: { if !$0 { pendingRecordErase = nil } }),
                            titleVisibility: .visible, presenting: pendingRecordErase) { folder in
            Button("Erase Record", role: .destructive) {
                vm.eraseRecord(folder: folder)
                pendingRecordErase = nil
                note = Note(text: "Claude Code record erased.", isError: false)
            }
            Button("Keep It", role: .cancel) { pendingRecordErase = nil }
        } message: { folder in
            Text("Sanduhr no longer records the Claude Code sessions in \(ClaudeCodeFolders.Folder(path: folder).display(home: home)) for this account. The sessions it kept so far can be deleted from this Mac now, or kept until you erase them or remove the account.")
        }
        .confirmationDialog("Erase this account's data?", isPresented: $confirmingEraseAll,
                            titleVisibility: .visible) {
            Button("Erase Data", role: .destructive) {
                vm.eraseAccountData(label)
                note = Note(text: "This account's data was erased.", isError: false)
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(eraseAllMessage)
        }
    }

    // MARK: Erase

    /// After Keep a record is left: ask about the folder's record, when there is one.
    private func offerRecordErase() {
        guard let folder = choices.folder, vm.hasRecord(folder: folder) else { return }
        pendingRecordErase = folder
    }

    /// After the folder changed while recording: ask about the old folder's record, unless the
    /// folder is still recorded (moved to an account that keeps one).
    private func afterFolderChange(from before: AccountDataChoices) {
        guard before.activity == .record, let old = before.folder, old != choices.folder,
              vm.account(linkedTo: old).map({ vm.dataChoices(for: $0).activity != .record }) ?? true,
              vm.hasRecord(folder: old) else { return }
        pendingRecordErase = old
    }

    private var eraseAllRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Button("Erase this account's data…") { confirmingEraseAll = true }
            Caption("Deletes the meter history and the Claude Code record Sanduhr keeps for this account. Nothing else is touched: not Claude Code's own logs, not the account.")
        }
    }

    private var eraseAllMessage: String {
        let record: String
        if let folder = choices.folder, vm.hasRecord(folder: folder) {
            let shown = ClaudeCodeFolders.Folder(path: folder).display(home: home)
            record = " and its Claude Code record (the sessions kept from \(shown))"
            + (choices.activity == .record ? ". Keep a record switches to Live only first, so nothing is recorded again until you choose it" : "")
        } else {
            record = " (there is no Claude Code record)"
        }
        return "Deletes this account's meter history\(record). Claude Code's own logs stay as they are. Meter history goes on recording new readings while it is on."
    }

    // MARK: Meter history

    /// Meter history (item 43): Off · 30 days. Choosing Off offers to erase what was kept.
    private var historyRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Meter history", selection: Binding(
                get: { !vm.historyOffAccounts.contains(label) },
                set: { on in
                    guard on == vm.historyOffAccounts.contains(label) else { return }
                    vm.setMeterHistory(label, on: on)
                    if !on { confirmingErase = true }
                })) {
                Text("Off").tag(false)
                Text("\(MeterHistory.days) days").tag(true)
            }
            .pickerStyle(.segmented)
            .fixedSize()
            Caption("The readings behind the sparklines, kept on this Mac for 30 days. Off stops recording this account's meters.")
        }
    }

    // MARK: Claude Code folder

    /// The found folders, plus the linked one when it was chosen by hand somewhere else.
    private var options: [ClaudeCodeFolders.Folder] {
        guard let linked = choices.folder, !folders.contains(where: { $0.path == linked }) else { return folders }
        return folders + [ClaudeCodeFolders.Folder(path: linked)]
    }

    private func optionTitle(_ f: ClaudeCodeFolders.Folder) -> String {
        let shown = f.display(home: home)
        if let owner = vm.account(linkedTo: f.path), owner != label { return "\(shown) (\(owner))" }
        return shown
    }

    private var folderRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Claude Code folder", selection: Binding(
                get: { choices.folder ?? "" },
                set: { pick($0) })) {
                Text("None").tag("")
                ForEach(options) { f in
                    Text(optionTitle(f)).tag(f.path)
                }
                Divider()
                Text("Choose…").tag(Self.chooseTag)
            }
            .pickerStyle(.menu)
            .fixedSize()
            if choices.folder == nil, let suggestion {
                SuggestionBox(folder: suggestion.display(home: home)) { pick(suggestion.path) }
            }
            Caption("The Claude Code folder whose logs belong to this account, such as ~/.claude or a ~/.claude-name folder used with CLAUDE_CONFIG_DIR. A folder links to one account. Sanduhr suggests the folder signed in to this account's organization and never links one by itself.")
        }
    }

    private func pick(_ tag: String) {
        note = nil
        if tag == Self.chooseTag {
            choose()
        } else if tag.isEmpty {
            let before = choices
            vm.unlinkFolder(label)
            afterFolderChange(from: before)
        } else if tag != choices.folder {
            link(tag)
        }
    }

    private func link(_ path: String) {
        let before = choices
        if case .linkedElsewhere(let owner) = vm.linkFolder(path, to: label) {
            pendingMove = PendingMove(path: path, owner: owner)
        } else {
            afterFolderChange(from: before)
        }
    }

    /// Choose…: any folder that looks like a Claude Code home.
    private func choose() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.directoryURL = URL(fileURLWithPath: home)
        panel.prompt = "Link"
        panel.message = "Choose a Claude Code folder (one with a projects folder or a .claude.json)."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard ClaudeCodeFolders.looksLikeHome(url.path, home: home) else {
            note = Note(text: "That folder doesn't look like a Claude Code folder: it has no projects folder and no .claude.json.", isError: true)
            return
        }
        link(url.path)
    }

    private func discover() async {
        let home = self.home
        let found = await Task.detached(priority: .userInitiated) {
            ClaudeCodeFolders.discover(home: home, environment: ProcessInfo.processInfo.environment)
        }.value
        folders = found
        suggestion = nil
        guard choices.folder == nil else { return }
        let picked = await vm.suggestedFolder(for: label, among: found, home: home)
        guard !Task.isCancelled, choices.folder == nil else { return }
        suggestion = picked
    }
}

/// "This folder is signed in to this account. Link it?" Never links on its own.
private struct SuggestionBox: View {
    let folder: String
    let link: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text("This folder is signed in to this account. Link it?")
                    .font(.caption)
                Text(folder).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
            Button("Link", action: link)
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(0.1)))
    }
}

/// Claude Code activity, project names and Share with your agents: activity reads the linked folder
/// (item 45) and keeps the record (item 46) with the project names chosen; sharing writes
/// `mcp-access.json` (item 47).
private struct DataChoiceRows: View {
    var vm: UsageViewModel
    let label: String
    /// Called after Keep a record was left, to offer erasing the record.
    let stoppedRecording: () -> Void

    private var choices: AccountDataChoices { vm.dataChoices(for: label) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            activityRow
            namesRow
            shareRow
            workRow
        }
    }

    private var activityRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Claude Code activity", selection: Binding(
                get: { choices.activity },
                set: { value in
                    let was = choices.activity
                    guard value != was else { return }
                    vm.setActivity(value, for: label)
                    if was == .record { stoppedRecording() }
                })) {
                ForEach(ActivityChoice.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.menu)
            .fixedSize()
            Caption(recordCaption)
        }
    }

    private var namesRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Project names in the record", selection: Binding(
                get: { choices.names },
                set: { vm.setProjectNames($0, for: label) })) {
                ForEach(ProjectNamesChoice.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.menu)
            .fixedSize()
            .disabled(choices.activity != .record && choices.share != .activity)
            Caption("Hidden keeps a short code per project instead of its name, so the record still groups by project; Full paths also keeps each project's folder path. Used with Keep a record, and for what Claude sees with Meters and activity. A change applies to sessions recorded or still running from now on; finished sessions already kept keep the names they were recorded with.")
        }
    }

    private var recordCaption: String {
        var text = "Live only reads the linked folder's logs for the cards and stores nothing. Keep a record also keeps a summary of each session (tokens by day, model and project, never what was written) on this Mac until you erase it, after Claude Code deletes its own logs."
        if choices.activity == .record, let folder = choices.folder {
            let months = vm.vault.months(folder: folder)
            if months > 0 { text += " Kept so far: \(months) \(months == 1 ? "month" : "months")." }
        }
        return text
    }

    private var shareRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Share with your agents", selection: Binding(
                get: { choices.share },
                set: { vm.setShare($0, for: label) })) {
                ForEach(ShareChoice.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.menu)
            .fixedSize()
            Caption("What your agents (Claude Code and Claude Desktop sessions on this Mac that use the Sanduhr MCP server) can read about this account. Sanduhr hands it to them on this Mac and sends nothing anywhere; an agent may use what it reads in its conversation. Meters: the meters and their history. Meters and activity: also Claude Code tokens by day, model and project, with project names as chosen above. Off answers as if the account weren't here.")
        }
    }

    /// Item 66: watchers from this account's Claude Code folder hide in demo mode.
    private var workRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle("Work account", isOn: Binding(
                get: { choices.work },
                set: { vm.setWork($0, for: label) }))
            Caption("Watchers from this account's linked Claude Code folder are work: they hide while demo mode is on, so screenshots never show them.")
        }
    }
}

/// A short secondary caption that wraps.
private struct Caption: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}
