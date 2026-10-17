import SwiftUI

/// The version of Sanduhr's mod this copy of the app carries.
struct AppModVersion: Equatable, Sendable {
    let stamp: String
    /// The manifest's version, empty without one.
    let version: String

    static func read(_ scripts: IntegrationScripts) -> AppModVersion? {
        guard scripts.hasMod, let source = scripts.source, let stamp = scripts.bundledStamp else { return nil }
        let manifest = ModStatusEntries.manifest(source.appendingPathComponent(IntegrationScripts.modPath))
        return AppModVersion(stamp: stamp, version: manifest["version"] as? String ?? "")
    }
}

/// Sanduhr's own mod in one Claude Code folder (item 64, slice 2), as Mods & Config's Meters above
/// the prompt row shows it (Settings v2, 2026-10-08; slice 2 had it on Claude Code).
struct OwnModRow: Identifiable, Equatable, Sendable {
    let state: OwnModState
    /// The plugin folders entry as Claude Code's integrations read it: outdated names older scripts.
    var status: IntegrationStatus = .notInstalled
    /// The manifest version of the folder the entry names, when it is there.
    var version: String?
    /// The entry pins a version other than the app's.
    var updateAvailable = false
    /// The pinned folder is gone.
    var missing = false
    /// The mod's options in this folder's settings.json (item 75, slice D).
    var options = MetersOptionsState()

    var folder: String { state.folder }
    var id: String { folder }

    static func read(_ installer: IntegrationInstaller, folder: String, app: AppModVersion?) -> OwnModRow {
        let state = installer.ownModState(folder: folder)
        var row = OwnModRow(state: state, status: installer.status(.meters, folder: folder))
        if let entry = state.entry {
            let dir = URL(fileURLWithPath: entry)
            row.missing = !FileManager.default.fileExists(atPath: dir.appendingPathComponent(".claude-plugin/plugin.json").path)
            row.version = ModStatusEntries.manifest(dir)["version"] as? String
        }
        if let pinned = state.pinned, let app { row.updateAvailable = pinned != app.stamp }
        row.options = installer.ownModOptions(folder: folder)
        return row
    }

    /// Update has something to do here: an entry naming older scripts, or an older pinned version.
    var updateWaiting: Bool { state.listed && (status == .outdated || (updateAvailable && !missing)) }

    /// "On in 2 of 3 folders, an update waiting in 1", nil without a folder (item 75, slice C).
    static func onLine(_ rows: [OwnModRow]) -> String? {
        guard !rows.isEmpty else { return nil }
        var text = "On in \(rows.filter(\.state.isOn).count) of \(rows.count) folder\(rows.count == 1 ? "" : "s")"
        let waiting = rows.filter(\.updateWaiting).count
        if waiting > 0 { text += ", an update waiting in \(waiting)" }
        return text
    }

    /// Where it stands, in words.
    var summary: String {
        guard state.readable else { return "settings.json isn't JSON Sanduhr can edit, so it is left alone." }
        guard state.listed else { return "Not among the plugin folders this folder's settings list. On adds it." }
        let v = version.map { "Version \($0)" } ?? "Its version"
        var text = state.pinned != nil
            ? "\(v), pinned when it was switched on: Update moves it to a new version."
            : "\(v), through Sanduhr's current folder: it follows Sanduhr's updates."
        if missing { text = "Its folder isn't there any more. Remove takes the entry out." }
        if !state.isOn { text += " Off: enabledPlugins sets sanduhr-meters@inline to false here." }
        return text
    }
}

/// What the last switch, Update or Remove said for a folder.
struct OwnModNote: Equatable, Sendable {
    enum Kind: Equatable, Sendable { case info, warning, error }

    let text: String
    let kind: Kind
    /// Update's question: what the new version can also do.
    var added: [String]?

    init(text: String, kind: Kind, added: [String]? = nil) {
        self.text = text
        self.kind = kind
        self.added = added
    }

    init(failure: Error) {
        kind = .error
        added = nil
        switch failure as? IntegrationInstaller.Failure {
        case .malformed: text = "settings.json isn't JSON Sanduhr can edit (or a key in it isn't the kind it should be), so nothing was changed."
        case .scriptsMissing: text = "This copy of Sanduhr doesn't carry the mod (a development build), so it can't add it."
        case .writeFailed: text = "Couldn't write settings.json: it kept changing, or the folder isn't writable. Nothing was changed."
        case .receiptSetAside: text = IntegrationsModel.setAsideText("settings.json")
        case nil: text = "Something went wrong, so nothing was changed."
        }
    }
}

/// Update found the new version can do more: the question before switching to it.
struct OwnModUpdateQuestion: Identifiable, Equatable {
    let folder: String
    let added: [String]
    var id: String { folder }
}

extension View {
    /// The meters mod's update question (an update that can do more), on the page's header and
    /// in the Studio's Folders tab. On in a folder without the mod is the consent sheet's, not a
    /// question here. `active` is false on the header beneath an open Studio, so it shows once.
    func ownModQuestions(_ model: ModsPageModel, linked: [String], active: Bool = true) -> some View {
        modifier(OwnModQuestions(model: model, linked: linked, active: active))
    }
}

private struct OwnModQuestions: ViewModifier {
    @Bindable var model: ModsPageModel
    let linked: [String]
    let active: Bool

    func body(content: Content) -> some View {
        content
            .alert(updateTitle, isPresented: updateShown, presenting: model.updateQuestion) { q in
                Button("Update") { Task { await model.update(folder: q.folder, confirmed: true, linked: linked) } }
                Button("Keep the Version in Use", role: .cancel) {}
            } message: { q in
                Text("The new version can also: " + q.added.joined(separator: ", ") + ". The version in use stays on until you agree.")
            }
    }

    private var updateTitle: String { "The new version of \(IntegrationScripts.modName) can do more" }

    private var updateShown: Binding<Bool> {
        Binding(get: { active && model.updateQuestion != nil }, set: { if !$0 { model.updateQuestion = nil } })
    }
}

/// The meters mod in one line, for the places that point at Mods & Config instead of switching it
/// (Claude Code's folder boxes): "Meters above the prompt: on".
enum MetersModStatus {
    /// `row` is the folder's own-mod state, nil until Mods & Config's model has read the folders;
    /// `fallback` is what Claude Code's integrations read for the entry meanwhile.
    static func text(_ row: OwnModRow?, fallback: IntegrationStatus) -> String {
        "\(SettingsNames.metersAbovePrompt): " + word(row, fallback: fallback)
    }

    static func word(_ row: OwnModRow?, fallback: IntegrationStatus) -> String {
        let status = row?.status ?? fallback
        if status == .unreadable || row?.state.readable == false { return "settings.json isn't valid JSON" }
        guard let row else { return status.isOurs ? "installed" : "not installed" }
        let base = row.state.isOn ? "on" : (row.state.listed ? "off" : "not installed")
        return row.updateWaiting ? base + ", update waiting" : base
    }
}

/// Sanduhr's own mod, sanduhr-meters, at the top of Mods & Config (2026-10-08: the meters above
/// the prompt are a mod, so their controls live with the mods; slice 2 had them on Claude Code).
/// Item 75 slice C moved its per-folder rows into the Mod Studio's Folders tab; since 2026-10-17
/// the page's header (MetersHeader) shows the same rows again, above More.
/// One folder's meters mod: its state, Update, Remove and the switch, then where it stands and
/// what the last action said (a project's override included). The header and the Studio's
/// Folders tab.
struct MetersModRow: View {
    let row: OwnModRow
    var model: ModsPageModel
    let linked: [String]
    let install: () -> Void

    private var busy: Bool { model.ownBusy.contains(row.folder) }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(model.display(row.folder)).font(.callout.monospaced())
                    .lineLimit(1).truncationMode(.middle)
                Text(row.state.isOn ? "On" : (row.state.listed ? "Off" : "Not installed"))
                    .font(.caption)
                    .foregroundStyle(row.state.isOn ? Color.hex("4ade80") : .secondary)
                Spacer(minLength: 8)
                if busy { ProgressView().controlSize(.small) }
                MetersModButtons(row: row, model: model, linked: linked, busy: busy)
                Toggle("On", isOn: Binding(get: { row.state.isOn }, set: { switchTo($0) }))
                    .toggleStyle(.switch)
                    .labelsHidden()
                    .disabled(busy || !row.state.readable)
                    .accessibilityLabel("\(SettingsNames.metersAbovePrompt) in \(model.display(row.folder))")
            }
            Text(row.summary)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if let note = model.ownNotes[row.folder], !note.text.isEmpty {
                Text(note.text)
                    .font(.caption)
                    .foregroundStyle(note.kind == .info ? Color.secondary : (note.kind == .warning ? Color.orange : Color.hex("f87171")))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func switchTo(_ on: Bool) {
        // A folder without the entry installs through the consent sheet.
        if on && !row.state.listed { return install() }
        Task { await model.setOwn(on: on, folder: row.folder, linked: linked) }
    }
}

/// Update (an entry naming older scripts, or a pinned version older than the app's) and Remove,
/// which asks first: it rewrites that folder's settings.json.
private struct MetersModButtons: View {
    let row: OwnModRow
    var model: ModsPageModel
    let linked: [String]
    let busy: Bool
    @State private var removing = false

    var body: some View {
        if row.status == .outdated || row.updateWaiting {
            Button("Update") {
                Task {
                    if row.status == .outdated {
                        await model.install(folder: row.folder, updating: true, linked: linked)
                    } else {
                        await model.update(folder: row.folder, confirmed: false, linked: linked)
                    }
                }
            }
            .disabled(busy)
        }
        if row.state.listed || row.state.hasReceipt {
            Button("Remove…") { removing = true }
                .disabled(busy || !row.state.readable)
                .confirmationDialog("Remove the meters mod from \(model.display(row.folder))?", isPresented: $removing) {
                    Button("Remove", role: .destructive) { Task { await model.remove(folder: row.folder, linked: linked) } }
                    Button("Cancel", role: .cancel) {}
                } message: {
                    Text("Its entries come out of that folder's settings.json. The receipt keeps the undo.")
                }
        }
    }
}

/// Meters above the prompt at the top of Mods & Config (2026-10-17): one row per Claude Code
/// folder, the same folders the Studio's Folders tab lists (no work folder in demo mode), each
/// with its state in words, Update and Remove where they apply, and the switch; the watchers'
/// line; and the way into the Studio. On in a folder without the mod goes through the consent
/// sheet and the install, as in the Studio.
struct MetersHeader: View {
    var model: ModsPageModel
    let linked: [String]
    let demo: Bool
    @State private var consent: MetersConsent?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text(SettingsNames.metersAbovePrompt).font(.headline)
                Spacer()
                Button("Studio…") { model.openStudio() }
                    .controlSize(.small)
                    .help("Preview the band with demo numbers, style it and set its layout.")
            }
            rows
            WatchersAbovePromptLine()
        }
        .ownModQuestions(model, linked: linked, active: !model.studioOpen)
        .sheet(item: $consent) { c in
            MetersConsentSheet(folder: model.display(c.folder),
                               file: model.display(model.installer.configFile(.meters, folder: c.folder)),
                               install: {
                                   consent = nil
                                   Task { await model.install(folder: c.folder, updating: false, linked: linked) }
                               },
                               cancel: { consent = nil })
        }
    }

    @ViewBuilder
    private var rows: some View {
        let rows = model.studioRows(demo: demo)
        if !model.ownLoaded {
            Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
        } else if rows.isEmpty {
            Text("No Claude Code folder found (~/.claude or a ~/.claude-name folder).")
                .font(.caption).foregroundStyle(.secondary)
        }
        ForEach(rows) { row in
            MetersModRow(row: row, model: model, linked: linked, install: { consent = MetersConsent(folder: row.folder) })
            if row.id != rows.last?.id { Divider() }
        }
    }
}

/// The watchers' switch for the band lives on Watchers (it decides what band.json carries): its
/// state here, with the way there.
struct WatchersAbovePromptLine: View {
    @AppStorage(BandFile.watchersKey) private var inBand = false

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text("Watcher rows above the prompt: \(inBand ? "on" : "off"). They draw through this mod; switch them on Watchers.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            SettingsLinkButton(.watchers, anchor: SettingsAnchor.abovePrompt)
        }
    }
}

/// The folder the meters mod's consent sheet is about.
struct MetersConsent: Identifiable, Equatable {
    let folder: String
    var id: String { folder }
}

/// Before the first install in a folder: what the mod shows and reads, and what is written where.
struct MetersConsentSheet: View {
    let folder: String
    /// The folder's settings.json, as the page shows paths.
    let file: String
    let install: () -> Void
    let cancel: () -> Void

    static let headline = "Show the meters above Claude Code's prompt?"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(Self.headline).font(.headline)
            MetersConsentBody(folder: folder)
            Text("Sanduhr adds its mod's folder to \(IntegrationKind.meters.keyPath) in \(file), keeping any folders already listed, and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove takes out only that folder.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Not Now", role: .cancel, action: cancel).keyboardShortcut(.cancelAction)
                Button("Install", action: install).keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 460)
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
