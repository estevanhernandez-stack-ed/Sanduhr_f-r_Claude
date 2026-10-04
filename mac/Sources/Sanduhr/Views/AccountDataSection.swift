import AppKit
import SwiftUI

/// Settings, Accounts, Data (item 44): what Sanduhr keeps for the selected account and what
/// Claude can see of it. Meter history (item 43) works now; the Claude Code folder link is kept
/// now and read from item 45 on; Claude Code activity, project names and sharing are stored for
/// items 45 to 47, and the page says so.
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
            DataChoiceRows(vm: vm, label: label)
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
                vm.linkFolder(move.path, to: label, move: true)
                pendingMove = nil
            }
            Button("Cancel", role: .cancel) { pendingMove = nil }
        } message: { move in
            Text("A Claude Code folder belongs to one account. Moving it unlinks it from \(move.owner).")
        }
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
            vm.unlinkFolder(label)
        } else if tag != choices.folder {
            link(tag)
        }
    }

    private func link(_ path: String) {
        if case .linkedElsewhere(let owner) = vm.linkFolder(path, to: label) {
            pendingMove = PendingMove(path: path, owner: owner)
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

/// Claude Code activity, project names and Share with Claude: stored now for items 45 to 47.
private struct DataChoiceRows: View {
    var vm: UsageViewModel
    let label: String

    private var choices: AccountDataChoices { vm.dataChoices(for: label) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            activityRow
            namesRow
            shareRow
            Caption("These three are saved now and take effect as they arrive: live activity in the next update, then the record and sharing with Claude.")
                .italic()
        }
    }

    private var activityRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Claude Code activity", selection: Binding(
                get: { choices.activity },
                set: { vm.setActivity($0, for: label) })) {
                ForEach(ActivityChoice.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.menu)
            .fixedSize()
            Caption("Live only reads the linked folder's logs for the cards and stores nothing. Keep a record also keeps this account's sessions on this Mac until you erase them.")
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
            .disabled(choices.activity != .record)
            Caption("Hidden keeps a short code per project instead of its name, so the record still groups by project. Used only with Keep a record.")
        }
    }

    private var shareRow: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("Share with Claude", selection: Binding(
                get: { choices.share },
                set: { vm.setShare($0, for: label) })) {
                ForEach(ShareChoice.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.menu)
            .fixedSize()
            Caption("What Claude can read about this account through the Sanduhr MCP server. Off answers as if the account weren't here.")
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
