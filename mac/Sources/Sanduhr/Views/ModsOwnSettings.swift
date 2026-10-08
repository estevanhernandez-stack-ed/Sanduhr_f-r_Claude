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

/// Sanduhr's own mod in one Claude Code folder, as the Mods page shows it (item 64, slice 2).
struct OwnModRow: Identifiable, Equatable, Sendable {
    let state: OwnModState
    /// The manifest version of the folder the entry names, when it is there.
    var version: String?
    /// The entry pins a version other than the app's.
    var updateAvailable = false
    /// The pinned folder is gone.
    var missing = false

    var folder: String { state.folder }
    var id: String { folder }

    static func read(_ installer: IntegrationInstaller, folder: String, app: AppModVersion?) -> OwnModRow {
        let state = installer.ownModState(folder: folder)
        var row = OwnModRow(state: state)
        if let entry = state.entry {
            let dir = URL(fileURLWithPath: entry)
            row.missing = !FileManager.default.fileExists(atPath: dir.appendingPathComponent(".claude-plugin/plugin.json").path)
            row.version = ModStatusEntries.manifest(dir)["version"] as? String
        }
        if let pinned = state.pinned, let app { row.updateAvailable = pinned != app.stamp }
        return row
    }

    /// Where it stands, in words.
    var summary: String {
        guard state.readable else { return "settings.json isn't JSON Sanduhr can edit, so it is left alone." }
        guard state.listed else { return "Not among the plugin folders this folder's settings list. On adds it." }
        let v = version.map { "Version \($0)" } ?? "Its version"
        var text = state.pinned != nil
            ? "\(v), kept by the Mods page: Update moves it to a new version."
            : "\(v), through Sanduhr's current folder: it follows Sanduhr's updates (installed from Integrations)."
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

/// Settings, Mods: Sanduhr's own mod with a switch per Claude Code folder, Update and Remove.
struct OwnModBox: View {
    var model: ModsPageModel
    let linked: [String]

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                Text("Sanduhr's meters above Claude Code's prompt. The switch turns it on or off for each Claude Code folder by editing that folder's settings.json. Sanduhr keeps a record in its own folder, so switching back or Remove can put the file back exactly as it was.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                if model.loaded && model.own.isEmpty {
                    Text("No Claude Code folder to switch it in.").font(.caption).foregroundStyle(.secondary)
                }
                ForEach(model.own) { row in
                    OwnModFolderRow(row: row, model: model, linked: linked)
                    if row.id != model.own.last?.id { Divider() }
                }
            }
            .padding(4)
            .frame(maxWidth: .infinity, alignment: .leading)
        } label: {
            HStack(spacing: 6) {
                Text("Sanduhr's mod: \(IntegrationScripts.modName)").font(.headline)
                if let v = model.appMod?.version, !v.isEmpty {
                    Text(v).font(.caption.monospaced()).foregroundStyle(.secondary)
                }
            }
        }
    }
}

private struct OwnModFolderRow: View {
    let row: OwnModRow
    var model: ModsPageModel
    let linked: [String]

    private var busy: Bool { model.ownBusy.contains(row.folder) }
    private var canAdd: Bool { row.state.listed || model.appMod != nil }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(model.display(row.folder)).font(.body.monospaced())
                Spacer(minLength: 8)
                if busy { ProgressView().controlSize(.small) }
                Toggle("On", isOn: Binding(get: { row.state.isOn },
                                           set: { model.requestSwitch(row, on: $0, linked: linked) }))
                    .toggleStyle(.switch)
                    .labelsHidden()
                    .disabled(busy || !row.state.readable || !canAdd)
                    .accessibilityLabel("\(IntegrationScripts.modName) in \(model.display(row.folder))")
                    .help(row.state.isOn ? "Turn it off for this folder" : "Turn it on for this folder")
            }
            Text(row.summary)
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            OwnModActions(row: row, model: model, linked: linked, busy: busy)
            if let note = model.ownNotes[row.folder], !note.text.isEmpty {
                Text(note.text)
                    .font(.caption)
                    .foregroundStyle(note.kind == .info ? Color.secondary : (note.kind == .warning ? Color.orange : Color.hex("f87171")))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct OwnModActions: View {
    let row: OwnModRow
    var model: ModsPageModel
    let linked: [String]
    let busy: Bool

    var body: some View {
        if row.updateAvailable || row.state.listed || row.state.hasReceipt {
            HStack(spacing: 8) {
                if row.updateAvailable, !row.missing {
                    Button("Update to \(model.appMod?.version.isEmpty == false ? model.appMod!.version : "this Sanduhr's version")") {
                        Task { await model.update(folder: row.folder, confirmed: false, linked: linked) }
                    }
                    .help("Moves this folder's entry to the version this copy of Sanduhr carries. If it can do something the version in use can't, Sanduhr asks first.")
                }
                Button("Remove") { Task { await model.remove(folder: row.folder, linked: linked) } }
                    .help("Takes Sanduhr's mod out of this folder: the switch, then its entry among the plugin folders, back to the bytes from before.")
            }
            .disabled(busy || !row.state.readable)
        }
    }
}

extension View {
    /// The Mods page's two questions: On in a folder without the mod, and an update that can
    /// do more.
    func ownModQuestions(_ model: ModsPageModel, linked: [String]) -> some View {
        modifier(OwnModQuestions(model: model, linked: linked))
    }
}

private struct OwnModQuestions: ViewModifier {
    @Bindable var model: ModsPageModel
    let linked: [String]

    func body(content: Content) -> some View {
        content
            .confirmationDialog(onTitle, isPresented: onShown, titleVisibility: .visible) {
                Button("Turn On") {
                    if let folder = model.confirmOn { Task { await model.setOwn(on: true, folder: folder, linked: linked) } }
                    model.confirmOn = nil
                }
                Button("Cancel", role: .cancel) { model.confirmOn = nil }
            } message: {
                Text("Sanduhr adds its mod's folder to env.CLAUDE_CODE_PLUGIN_DIRS in this folder's settings.json, pinned to the version this copy of Sanduhr carries. Off or Remove puts the file back exactly.")
            }
            .alert(updateTitle, isPresented: updateShown, presenting: model.updateQuestion) { q in
                Button("Update") { Task { await model.update(folder: q.folder, confirmed: true, linked: linked) } }
                Button("Keep the Version in Use", role: .cancel) {}
            } message: { q in
                Text("The new version can also: " + q.added.joined(separator: ", ") + ". The version in use stays on until you agree.")
            }
    }

    private var onTitle: String {
        "Turn on \(IntegrationScripts.modName) in \(model.confirmOn.map(model.display) ?? "this folder")?"
    }

    private var updateTitle: String { "The new version of \(IntegrationScripts.modName) can do more" }

    private var onShown: Binding<Bool> {
        Binding(get: { model.confirmOn != nil }, set: { if !$0 { model.confirmOn = nil } })
    }

    private var updateShown: Binding<Bool> {
        Binding(get: { model.updateQuestion != nil }, set: { if !$0 { model.updateQuestion = nil } })
    }
}
