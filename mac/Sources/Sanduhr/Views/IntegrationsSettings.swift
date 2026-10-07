import AppKit
import SwiftUI

/// One Claude Code folder's row on the Integrations page.
struct IntegrationFolderState: Identifiable, Equatable {
    let path: String
    var mcp: IntegrationStatus
    var statusline: IntegrationStatus
    var meters: IntegrationStatus
    var hooks: IntegrationStatus = .notInstalled
    /// The statusline is Sanduhr's combined with the user's own (item 63).
    var statuslineCombined = false
    var id: String { path }

    func status(_ kind: IntegrationKind) -> IntegrationStatus {
        switch kind {
        case .mcp: mcp
        case .statusline: statusline
        case .meters: meters
        case .hooks: hooks
        }
    }
}

/// The Integrations page's state (item 49): the folders, what each has, the python3 found.
/// Reads and writes run off the main thread; the folders' paths stay on this page.
@MainActor
@Observable
final class IntegrationsModel {
    private(set) var folders: [IntegrationFolderState] = []
    private(set) var python: PythonFinder.Result?
    private(set) var loaded = false
    /// A folder or action in progress.
    private(set) var busy: String?
    var note: (text: String, isError: Bool)?
    /// Folders picked with Choose… this time Settings is open.
    private var chosen: [String] = []

    let home = NSHomeDirectory()

    /// The folders to list: found ones, the accounts' linked ones, ones installed into, chosen.
    func load(linked: [String]) async {
        let home = self.home
        let extra = chosen
        let result = await Task.detached(priority: .userInitiated) { () -> ([IntegrationFolderState], PythonFinder.Result) in
            let installer = IntegrationInstaller.standard
            var paths = ClaudeCodeFolders.discover(home: home, environment: ProcessInfo.processInfo.environment).map(\.path)
            for p in linked + installer.installedFolders() + extra {
                let n = AccountData.normalized(p)
                if !paths.contains(n) { paths.append(n) }
            }
            let states = paths.map {
                IntegrationFolderState(path: $0, mcp: installer.status(.mcp, folder: $0),
                                       statusline: installer.status(.statusline, folder: $0),
                                       meters: installer.status(.meters, folder: $0),
                                       hooks: installer.status(.hooks, folder: $0),
                                       statuslineCombined: installer.isCombined(folder: $0))
            }
            return (states, PythonFinder.find())
        }.value
        folders = result.0
        python = result.1
        loaded = true
    }

    var pythonPath: String? {
        if case .found(let path, _) = python { return path }
        return nil
    }

    func display(_ path: String) -> String { ClaudeCodeFolders.Folder(path: path).display(home: home) }

    /// A row's buttons work: nothing in progress, and python3 found where the kind runs on it.
    func canRun(_ kind: IntegrationKind) -> Bool { busy == nil && (pythonPath != nil || !kind.needsPython) }

    func configDisplay(_ kind: IntegrationKind, folder: String) -> String {
        display(IntegrationInstaller.standard.configFile(kind, folder: folder))
    }

    /// The user's statusline run once against sample statusline JSON and split into segments,
    /// with Sanduhr's, for the sheet's chips (item 63b). Nil when it couldn't run (no python3,
    /// a build without the scripts).
    func inspectStatusline(chain: String, input: Data? = nil) async -> StatuslineInspection? {
        guard let python = pythonPath else { return nil }
        return await Task.detached(priority: .userInitiated) { () -> StatuslineInspection? in
            guard let args = IntegrationInstaller.standard.inspectArguments(chain: chain),
                  let text = StatuslinePreview.run(python: python, arguments: args, input: input) else { return nil }
            return StatuslineInspection.decode(text)
        }.value
    }

    /// What the combined line prints with these picks, from the user's output (their command
    /// doesn't run again), for the sheet's live preview. Nil when it couldn't run.
    func composeStatusline(theirs: String, join: StatuslineJoin, selection: StatuslineSelection,
                           input: Data? = nil) async -> String? {
        guard let python = pythonPath else { return nil }
        return await Task.detached(priority: .userInitiated) { () -> String? in
            guard let args = IntegrationInstaller.standard.composeArguments(theirs: theirs, join: join, selection: selection) else { return nil }
            return StatuslinePreview.run(python: python, arguments: args, input: input)
        }.value
    }

    /// The input for Test with live data: Sanduhr's current numbers and the folder's latest
    /// session (item 63b).
    func liveStatuslineInput(folder: String) async -> StatuslineLiveInput.Result {
        await Task.detached(priority: .userInitiated) { StatuslineLiveInput.build(folder: folder) }.value
    }

    /// The mods `folder` loads that draw status entries in Claude Code's status area (item 63b).
    func modStatusEntries(folder: String) async -> [ModStatusEntry] {
        let home = self.home
        return await Task.detached(priority: .userInitiated) { ModStatusEntries.scan(folder: folder, home: home) }.value
    }

    /// Someone else's entry under Sanduhr's key, for the consent sheet.
    func otherEntry(_ kind: IntegrationKind, folder: String) -> String? {
        IntegrationInstaller.standard.otherEntry(kind, folder: folder)
    }

    func choose() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.directoryURL = URL(fileURLWithPath: home)
        panel.prompt = "Add"
        panel.message = "Choose a Claude Code folder (one with a projects folder or a .claude.json)."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard ClaudeCodeFolders.looksLikeHome(url.path, home: home) else {
            note = ("That folder doesn't look like a Claude Code folder: it has no projects folder and no .claude.json.", true)
            return
        }
        let p = AccountData.normalized(url.path)
        if !chosen.contains(p) { chosen.append(p) }
    }

    /// Installs (or updates) `kind` in `folder`. With `replaceOther`, someone else's entry is
    /// replaced (the consent sheet asked); with `combine`, someone else's statusline is kept and
    /// shown with Sanduhr's (item 63). Returns the other entry when one turned up unasked.
    func install(_ kind: IntegrationKind, folder: String, replaceOther: Bool, combine: StatuslineJoin? = nil,
                 selection: StatuslineSelection = StatuslineSelection(), linked: [String]) async -> String? {
        // The mod runs inside Claude Code: no python3 needed.
        // The mod and the hooks run inside Claude Code: no python3 needed.
        guard let python = kind.needsPython ? pythonPath : (pythonPath ?? "") else { return nil }
        busy = folder
        defer { busy = nil }
        let outcome = await Task.detached(priority: .userInitiated) { () -> Result<IntegrationInstaller.Outcome, IntegrationInstaller.Failure> in
            do {
                return .success(try IntegrationInstaller.standard.install(kind, folder: folder, python: python,
                                                                         replaceOther: replaceOther, combine: combine,
                                                                         selection: selection))
            } catch let f as IntegrationInstaller.Failure {
                return .failure(f)
            } catch {
                return .failure(.writeFailed(file: folder))
            }
        }.value
        await load(linked: linked)
        switch outcome {
        case .success(.installed):
            let what = combine != nil && kind == .statusline
                ? "Statusline combined with yours" : "\(kind.title) installed"
            note = ("\(what) for \(display(folder)). Claude Code sessions started from now on use it.", false)
            return nil
        case .success(.needsReplaceConsent(let other)):
            return other
        case .failure(let f):
            note = (message(f), true)
            return nil
        }
    }

    func remove(_ kind: IntegrationKind, folder: String, linked: [String]) async {
        busy = folder
        defer { busy = nil }
        let failure = await Task.detached(priority: .userInitiated) { () -> IntegrationInstaller.Failure? in
            do {
                try IntegrationInstaller.standard.remove(kind, folder: folder)
                return nil
            } catch let f as IntegrationInstaller.Failure {
                return f
            } catch {
                return .writeFailed(file: folder)
            }
        }.value
        await load(linked: linked)
        note = failure.map { (message($0), true) }
            ?? ("\(kind.title) removed from \(display(folder)).", false)
    }

    private func message(_ f: IntegrationInstaller.Failure) -> String {
        switch f {
        case .malformed(let file):
            return "Couldn't safely edit \(display(file)): it isn't valid JSON, so Sanduhr left it as it is. Fix or remove the file in Claude Code, then try again."
        case .scriptsMissing:
            return "This copy of Sanduhr doesn't contain the integration scripts. Nothing was changed."
        case .writeFailed(let file):
            return "Couldn't write \(display(file)). Nothing was changed."
        }
    }
}

/// Settings, Integrations (item 49): install or remove Sanduhr's MCP server and statusline for
/// each Claude Code folder. Sits under Claude Usage in the first group: like the Data section,
/// it is about what Claude Code and Claude get from Sanduhr, and its consent points at the
/// accounts' Share with your agents choices.
struct IntegrationsSettings: View {
    var vm: UsageViewModel
    @Bindable var navigation: SettingsNavigation
    @State private var model = IntegrationsModel()
    @State private var consent: IntegrationConsent?

    /// The accounts' linked folders, listed even where discovery wouldn't find them.
    private var linked: [String] { vm.accountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                IntegrationsIntro()
                PythonRow(model: model, reload: reload)
                folderList
                GlowHint(openNotch: { navigation.selection = .notch })
                Divider()
                WatcherSettings(openNotch: { navigation.selection = .notch },
                                openLayout: { navigation.selection = .deskLayout })
                Divider()
                Button("Add Folder…") {
                    model.choose()
                    reload()
                }
                if let note = model.note {
                    Text(note.text)
                        .font(.caption)
                        .foregroundStyle(Color.hex(note.isError ? "f87171" : "4ade80"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .frame(maxWidth: .infinity, alignment: .topLeading)
            .padding(20)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task { await model.load(linked: linked) }
        .sheet(item: $consent) { c in
            IntegrationConsentSheet(consent: c, vm: vm, model: model,
                                    install: { join, selection in confirm(c, combine: join, selection: selection) },
                                    openAccounts: {
                                        consent = nil
                                        navigation.selection = .credentials
                                    },
                                    openNotch: {
                                        consent = nil
                                        navigation.selection = .notch
                                    },
                                    cancel: { consent = nil })
        }
    }

    @ViewBuilder
    private var folderList: some View {
        if model.loaded && model.folders.isEmpty {
            Text("No Claude Code folder found (~/.claude or a ~/.claude-name folder). Add one to install into it.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        ForEach(model.folders) { f in
            IntegrationFolderBox(folder: f, model: model, owner: vm.account(linkedTo: f.path),
                                 install: { kind in ask(kind, folder: f.path) },
                                 update: { kind in Task { await run(kind, folder: f.path, replace: false, combine: nil) } },
                                 remove: { kind in Task { await model.remove(kind, folder: f.path, linked: linked) } })
        }
    }

    private func reload() { Task { await model.load(linked: linked) } }

    private func ask(_ kind: IntegrationKind, folder: String) {
        model.note = nil
        consent = IntegrationConsent(kind: kind, folder: folder, other: model.otherEntry(kind, folder: folder))
    }

    private func confirm(_ c: IntegrationConsent, combine: StatuslineJoin?, selection: StatuslineSelection) {
        consent = nil
        Task {
            await run(c.kind, folder: c.folder, replace: c.other != nil && combine == nil, combine: combine,
                      selection: selection)
        }
    }

    private func run(_ kind: IntegrationKind, folder: String, replace: Bool, combine: StatuslineJoin?,
                     selection: StatuslineSelection = StatuslineSelection()) async {
        if let other = await model.install(kind, folder: folder, replaceOther: replace, combine: combine,
                                           selection: selection, linked: linked) {
            consent = IntegrationConsent(kind: kind, folder: folder, other: other)
        }
    }
}

private struct IntegrationsIntro: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Claude Code").font(.headline)
            Text("The MCP server lets Claude Code ask Sanduhr about your usage, as each account's Share with your agents choice allows. The statusline shows the active account's meters under Claude Code's prompt; the meters mod draws them as bars above it. The notch glow hooks let Claude Code tell Sanduhr when a session waits on you or finishes, so the notch can glow. Each is installed per Claude Code folder: Sanduhr adds one entry to that folder's settings, keeps a backup of the file beside it, and Remove takes the entry out again. Nothing leaves this Mac.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The python3 the scripts run with, or what is needed when there is none.
private struct PythonRow: View {
    var model: IntegrationsModel
    let reload: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            switch model.python {
            case .found(let path, let version):
                Text("Runs with Python \(version) at \(path).")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            case .tooOld(let path, let version):
                missing("The Python at \(path) is \(version); the integrations need 3.9 or later.")
            case .missing:
                missing("The integrations run on Python 3.9 or later, and none was found.")
            case nil:
                Text("Looking for Python…").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func missing(_ first: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(first + " macOS includes one with Apple's Command Line Tools; Python from python.org or Homebrew works too. Sanduhr doesn't start an install on its own.")
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button("Install Command Line Tools…") { PythonFinder.installCommandLineTools() }
                Button("Check Again", action: reload)
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.orange.opacity(0.12)))
    }
}

/// Where the glow the hooks feed is switched on (item 51).
private struct GlowHint: View {
    let openNotch: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text("The notch glow hooks only tell Sanduhr; the glow itself is off until you turn it on in Notch, Glow.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            Button("Glow Settings…", action: openNotch)
        }
    }
}

/// One folder: its MCP server, statusline, meters mod and notch glow hooks rows.
private struct IntegrationFolderBox: View {
    let folder: IntegrationFolderState
    var model: IntegrationsModel
    let owner: String?
    let install: (IntegrationKind) -> Void
    let update: (IntegrationKind) -> Void
    let remove: (IntegrationKind) -> Void

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 8) {
                ForEach(IntegrationKind.allCases, id: \.self) { kind in
                    IntegrationRow(kind: kind, status: folder.status(kind),
                                   combined: kind == .statusline && folder.statuslineCombined,
                                   file: model.configDisplay(kind, folder: folder.path),
                                   enabled: model.canRun(kind),
                                   install: { install(kind) }, update: { update(kind) },
                                   remove: { remove(kind) })
                }
            }
            .padding(4)
        } label: {
            HStack(spacing: 6) {
                Text(model.display(folder.path)).font(.body.monospaced())
                if let owner {
                    Text(owner).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

private struct IntegrationRow: View {
    let kind: IntegrationKind
    let status: IntegrationStatus
    /// The statusline is combined with the user's own (item 63).
    var combined = false
    let file: String
    let enabled: Bool
    let install: () -> Void
    let update: () -> Void
    let remove: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(kind.title).frame(width: 150, alignment: .leading)
            statusText
            Spacer(minLength: 8)
            buttons
        }
    }

    private var statusText: some View {
        let (text, color): (String, Color) = switch status {
        case .installed: (combined ? "Combined with your statusline" : "Installed", Color.hex("4ade80"))
        case .outdated: ("Outdated", .orange)
        case .notInstalled: ("Not installed", .secondary)
        case .other: (kind == .statusline ? "Another statusline is set" : "Another sanduhr entry is set", .secondary)
        case .unreadable: ("\(file) isn't valid JSON", Color.hex("f87171"))
        }
        return Text(text).font(.caption).foregroundStyle(color)
    }

    @ViewBuilder
    private var buttons: some View {
        switch status {
        case .notInstalled, .other:
            Button("Install…", action: install).disabled(!enabled)
        case .outdated:
            HStack {
                Button("Update", action: update).disabled(!enabled)
                Button("Remove", action: remove).disabled(!enabled)
            }
        case .installed:
            Button("Remove", action: remove).disabled(!enabled)
        case .unreadable:
            EmptyView()
        }
    }
}

/// What the consent sheet is about.
struct IntegrationConsent: Identifiable, Equatable {
    let kind: IntegrationKind
    let folder: String
    /// Someone else's entry the install would replace.
    let other: String?
    var id: String { "\(kind.rawValue)|\(folder)" }
}

/// Before an install: what Claude Code (and Claude) get, what is written where.
private struct IntegrationConsentSheet: View {
    let consent: IntegrationConsent
    var vm: UsageViewModel
    var model: IntegrationsModel
    /// Install; a join combines someone else's statusline with Sanduhr's (item 63), keeping the
    /// segments picked (item 63b).
    let install: (StatuslineJoin?, StatuslineSelection) -> Void
    let openAccounts: () -> Void
    let openNotch: () -> Void
    let cancel: () -> Void
    @State private var join: StatuslineJoin = .line
    @State private var selection = StatuslineSelection()

    /// The statusline sheet when one is already set: Combine, Replace or Cancel.
    private var offersCombine: Bool { consent.kind == .statusline && consent.other != nil }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            if offersCombine, let other = consent.other {
                CombineChoice(other: other, folder: model.display(consent.folder), folderPath: consent.folder,
                              model: model, join: $join,
                              selection: $selection)
            } else if let other = consent.other {
                ReplaceNotice(other: other, folder: model.display(consent.folder), kind: consent.kind)
            }
            Text(writes)
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            buttons
        }
        .padding(20)
        .frame(width: offersCombine ? 600 : 460)
    }

    @ViewBuilder
    private var header: some View {
        Text(Self.headline(consent.kind))
            .font(.headline)
        if consent.kind == .mcp {
            MCPConsentBody(vm: vm, folder: model.display(consent.folder), openAccounts: openAccounts)
        } else if consent.kind == .meters {
            MetersConsentBody(folder: model.display(consent.folder))
        } else if consent.kind == .hooks {
            HooksConsentBody(folder: model.display(consent.folder), openNotch: openNotch)
        } else {
            Text("Claude Code sessions using \(model.display(consent.folder)) show the active account's session and weekly meters under the prompt, read from the numbers Sanduhr saves on this Mac. Claude Code shows the line to you; it isn't added to the conversation.")
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    @ViewBuilder
    private var buttons: some View {
        HStack {
            Spacer()
            if offersCombine {
                Button("Cancel", role: .cancel, action: cancel).keyboardShortcut(.cancelAction)
                Button("Replace") { install(nil, StatuslineSelection()) }
                Button("Combine") { install(join, selection) }
                    .keyboardShortcut(.defaultAction)
            } else {
                Button("Not Now", role: .cancel, action: cancel).keyboardShortcut(.cancelAction)
                Button(consent.other == nil ? "Install" : "Replace and Install") { install(nil, StatuslineSelection()) }
                    .keyboardShortcut(.defaultAction)
            }
        }
    }

    static func headline(_ kind: IntegrationKind) -> String {
        switch kind {
        case .mcp: "Let Claude Code ask Sanduhr about your usage?"
        case .statusline: "Show the meters in Claude Code?"
        case .meters: "Show the meters above Claude Code's prompt?"
        case .hooks: "Glow the notch when Claude Code needs you?"
        }
    }

    /// What Install writes, where.
    private var writes: String {
        let file = model.configDisplay(consent.kind, folder: consent.folder)
        if consent.kind == .hooks {
            return "Sanduhr adds one entry to each of \(consent.kind.keyPath) in \(file), keeping every hook already there, and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove takes out only those two entries."
        }
        if consent.kind == .meters {
            return "Sanduhr adds its mod's folder to \(consent.kind.keyPath) in \(file), keeping any folders already listed, and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove takes out only that folder."
        }
        if offersCombine {
            return "Either way Sanduhr sets \(consent.kind.keyPath) in \(file), keeping your line's padding, refresh interval and vim setting, and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove puts your statusline back exactly as it is now."
        }
        return "Sanduhr sets \(consent.kind.keyPath) in \(file) and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove takes the entry out again."
    }
}

/// Someone else's entry the install replaces (the MCP server named sanduhr).
private struct ReplaceNotice: View {
    let other: String
    let folder: String
    let kind: IntegrationKind

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("\(folder) already has \(kind == .statusline ? "a statusline" : "an MCP server named sanduhr"):")
                .fixedSize(horizontal: false, vertical: true)
            OtherCommandBox(text: other)
            Text("Installing replaces it. Sanduhr keeps it and puts it back when you remove Sanduhr's.")
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The other entry's text, set off in a box.
struct OtherCommandBox: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.caption.monospaced())
            .lineLimit(3)
            .padding(6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 4).fill(Color.secondary.opacity(0.12)))
    }
}

/// What the meters mod shows and reads (item 50).
private struct MetersConsentBody: View {
    let folder: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Claude Code sessions using \(folder) draw the active account's session and weekly bars above the prompt, with the pace mark and reset countdowns, and show a short notice when a limit nearly fills or the session resets. It is a Claude Code mod, so it needs a Claude Code version that loads mods.")
                .fixedSize(horizontal: false, vertical: true)
            Text("The mod reads only the numbers Sanduhr saves on this Mac (snapshot.json). It never uses the network, never calls a model and adds nothing to the conversation.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// What the notch glow hooks tell Sanduhr (item 51), and where the glow is switched on.
private struct HooksConsentBody: View {
    let folder: String
    let openNotch: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Claude Code sessions using \(folder) tell Sanduhr when they wait on you (a permission prompt or a question) and when a turn finishes, so the notch can glow.")
                .fixedSize(horizontal: false, vertical: true)
            Text("Claude Code tells Sanduhr only that it is waiting or finished, by opening a sanduhr:// link that carries that one word. Nothing about the conversation or the project is sent, and nothing leaves this Mac. While Sanduhr isn't running, the hooks do nothing.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text("Only with Watchers, Show Claude Code's background work on: when a turn finishes, the hook also hands Sanduhr the session's background tasks (each one's kind, status and short description, and the Claude Code folder), never a command. Sanduhr reads that and deletes it at once.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(alignment: .firstTextBaseline) {
                Text("The glow stays off until you turn it on in Notch, Glow.")
                    .font(.caption)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 8)
                Button("Glow Settings…", action: openNotch)
            }
        }
    }
}

/// What each account lets the MCP server read, and the way to change it.
private struct MCPConsentBody: View {
    var vm: UsageViewModel
    let folder: String
    let openAccounts: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Claude Code sessions using \(folder) can call Sanduhr's tools. What they read follows each account's Share with your agents choice, and answers become part of those conversations:")
                .fixedSize(horizontal: false, vertical: true)
            VStack(alignment: .leading, spacing: 3) {
                ForEach(vm.accountLabels, id: \.self) { label in
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(label).font(.callout.weight(.medium))
                        Text(Self.shares(vm.dataChoices(for: label).share)).font(.callout).foregroundStyle(.secondary)
                    }
                }
            }
            .padding(.leading, 8)
            if !vm.accountLabels.contains(where: { vm.dataChoices(for: $0).share != .off }) {
                Text("No account shares anything now, so the tools answer with nothing until you choose otherwise.")
                    .font(.caption)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Button("Change in Accounts…", action: openAccounts)
            Text("The server reads only files on this Mac, never your session key, and never uses the network.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    static func shares(_ s: ShareChoice) -> String {
        switch s {
        case .off: "Off: nothing"
        case .meters: "Meters: the meters and their history"
        case .activity: "Meters and activity: also Claude Code tokens by day, model and project"
        }
    }
}
