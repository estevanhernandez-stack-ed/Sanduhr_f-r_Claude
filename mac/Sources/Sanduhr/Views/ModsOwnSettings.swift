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

/// Sanduhr's own mod in one Claude Code folder (item 64, slice 2), as Claude Code's Meters above
/// the prompt row shows it (Settings v2, slice 2).
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
    /// The meters mod's two questions, on Claude Code: On in a folder without the mod, and an
    /// update that can do more.
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
