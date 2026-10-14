import AppKit
import SwiftUI

/// Item 75, slices C and D: the Mod Studio's tabs. Look (slice B) styles the band; Layout sets the
/// mod's options per folder; Folders is where the mod is switched per Claude Code folder, the rows
/// Mods & Config carried until now, unchanged in behavior; About is what the mod can do, read by
/// `claude plugin validate`, before turning it on.
enum StudioTab: String, CaseIterable, Identifiable, Sendable {
    case look, layout, folders, about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .look: "Look"
        case .layout: "Layout"
        case .folders: "Folders"
        case .about: "About"
        }
    }
}

/// Folders: the mod per Claude Code folder, with its switch, Update and Remove through the
/// receipts. In demo mode no work folder is listed.
struct StudioFolders: View {
    var model: ModsPageModel
    let linked: [String]
    let demo: Bool
    /// On in a folder whose list has no entry: the consent sheet, then the install.
    let install: (String) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Switch the mod per Claude Code folder. Off switches it off for that folder (enabledPlugins false) and keeps its entry and files; Remove takes the entry out, deletes Sanduhr's record and puts settings.json back byte for byte.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            let rows = model.studioRows(demo: demo)
            if !model.ownLoaded {
                Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
            } else if rows.isEmpty {
                Text("No Claude Code folder found (~/.claude or a ~/.claude-name folder).")
                    .font(.caption).foregroundStyle(.secondary)
            }
            ForEach(rows) { row in
                MetersModRow(row: row, model: model, linked: linked, install: { install(row.folder) })
                if row.id != rows.last?.id { Divider() }
            }
        }
    }
}

/// About: the mod this copy of Sanduhr carries, where its files are, what it reads and never
/// does, and Check's report, run when the tab opens. Nothing of the mod's is run.
struct StudioAbout: View {
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            facts
            VStack(alignment: .leading, spacing: 4) {
                Text("Reads").font(.callout.weight(.semibold))
                Text("The numbers Sanduhr saves on this Mac for the folder's account (snapshot.json), and band.json, where Sanduhr keeps the band's looks and the watchers you show above the prompt.")
                    .font(.caption).fixedSize(horizontal: false, vertical: true)
                Text("Never").font(.callout.weight(.semibold)).padding(.top, 4)
                Text("Uses the network, calls a model, writes a file or adds anything to the conversation.")
                    .font(.caption).fixedSize(horizontal: false, vertical: true)
            }
            check
        }
        // Once the folders are read, `claude` is known.
        .task(id: model.ownLoaded) { if model.ownCheck == nil { await model.checkOwnMod() } }
    }

    private var facts: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let version = model.appMod?.version, !version.isEmpty {
                Text("\(IntegrationScripts.modName) \(version), the version this copy of Sanduhr carries. Each folder runs the copy pinned when it was switched on; Folders shows which.")
                    .font(.caption).fixedSize(horizontal: false, vertical: true)
            }
            if let dir = model.ownModDir {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(model.display(dir)).font(.caption.monospaced()).foregroundStyle(.secondary)
                        .lineLimit(1).truncationMode(.middle).textSelection(.enabled)
                    Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: dir)]) }
                        .buttonStyle(.link).font(.caption)
                }
            } else {
                Text("This copy of Sanduhr doesn't carry the mod (a development build).")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var check: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Text("What it can do").font(.callout.weight(.semibold))
                Spacer()
                if model.checkingOwn { ProgressView().controlSize(.small) }
                Button("Check Again") { Task { await model.checkOwnMod() } }
                    .controlSize(.small)
                    .disabled(model.checkingOwn || model.ownModDir == nil)
                    .help("Runs claude plugin validate --json on the mod's folder. It reads the files and runs none of the mod's code.")
            }
            if let outcome = model.ownCheck {
                ModRiskCard(outcome: outcome)
            } else if model.checkingOwn {
                Text("Asking claude plugin validate…").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

/// Layout (slice D): the mod's options, Meters, Style, Label and Motion, for one folder or every
/// folder where it's on. The preview draws the draft; the sheet's Save writes it by receipt.
struct StudioLayout: View {
    var studio: ModStudioModel
    var model: ModsPageModel
    let demo: Bool

    /// The folders whose settings list the mod: the only ones its options mean anything in.
    static func rows(_ model: ModsPageModel, demo: Bool) -> [OwnModRow] {
        model.studioRows(demo: demo).filter(\.state.listed)
    }

    var body: some View {
        let rows = Self.rows(model, demo: demo)
        VStack(alignment: .leading, spacing: 10) {
            Text("The mod's own options, kept per Claude Code folder in its settings.json. The preview shows them as you change them. Sessions started after Save use them; a session already running keeps the layout it started with.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if !model.ownLoaded {
                Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
            } else if rows.isEmpty {
                Text("The mod isn't in any folder yet. Turn it on in Folders, then set its layout here.")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                form(rows)
            }
        }
        .onAppear { follow(rows) }
        .onChange(of: rows.map(\.folder)) { follow(rows) }
    }

    /// Shows the first folder until one is picked, and again when the picked one goes.
    private func follow(_ rows: [OwnModRow]) {
        guard !rows.contains(where: { $0.folder == studio.layoutFolder }) else { return }
        studio.showLayout(of: rows.first)
    }

    @ViewBuilder
    private func form(_ rows: [OwnModRow]) -> some View {
        @Bindable var studio = studio
        Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
            GridRow {
                Text("Folder").gridColumnAlignment(.trailing)
                Picker("Folder", selection: Binding(get: { studio.layoutFolder ?? "" },
                                                    set: { f in studio.showLayout(of: rows.first { $0.folder == f }) })) {
                    ForEach(rows) { Text(model.display($0.folder)).tag($0.folder) }
                }
                .labelsHidden().fixedSize()
            }
            GridRow {
                Text("Meters")
                Picker("Meters", selection: $studio.layout.bars) {
                    Text("Both").tag(MetersOptions.Bars.both)
                    Text("Session").tag(MetersOptions.Bars.session)
                    Text("Weekly").tag(MetersOptions.Bars.weekly)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
            }
            GridRow {
                Text("Style")
                Picker("Style", selection: $studio.layout.style) {
                    Text("Compact").tag(MetersOptions.Style.compact)
                    Text("Full").tag(MetersOptions.Style.full)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
                .help("Compact: one line. Full: a line per meter with pace words; it folds to one line in a terminal narrower than 64 columns.")
            }
            GridRow {
                Text("Label")
                TextField("None", text: $studio.layout.label)
                    .textFieldStyle(.roundedBorder).frame(width: 220)
                    .help("A few words before the meters, for your eyes only. Empty shows none.")
            }
            GridRow {
                Text("Motion")
                Picker("Motion", selection: $studio.layout.motion) {
                    Text("On").tag(MetersOptions.Motion.on)
                    Text("Off").tag(MetersOptions.Motion.off)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
                .help("On: the sweep, shimmer, glow and the waiting pulse, still while macOS Reduce Motion is on. Off: the band stays still.")
            }
            GridRow {
                Text("Save to")
                Picker("Save to", selection: $studio.layoutEverywhere) {
                    Text("This folder").tag(false)
                    Text("Every folder where it's on (\(rows.filter(\.state.isOn).count))").tag(true)
                }
                .pickerStyle(.radioGroup).labelsHidden()
            }
        }
        .controlSize(.small)
        HStack {
            Button("Reset to Defaults") { studio.layout = .defaults }
                .controlSize(.small)
                .disabled(studio.layout.normalized == .defaults)
                .help("The mod's own defaults. Saved, it takes Sanduhr's options out of settings.json.")
            Spacer()
        }
        notes(rows)
    }

    @ViewBuilder
    private func notes(_ rows: [OwnModRow]) -> some View {
        let targets = studio.layoutTargets(rows)
        let theirs = targets.filter(\.options.isTheirs)
        if !theirs.isEmpty {
            Text(theirs.count == 1 && !studio.layoutEverywhere
                 ? ModsPageModel.optionsKept
                 : "\(theirs.count) of these folders already have options for the mod that Sanduhr didn't write (Claude Code's /config writes them there). Save leaves those as they are.")
                .font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
        ForEach(targets) { row in
            if let note = model.ownNotes[row.folder], !note.text.isEmpty {
                Text((targets.count > 1 ? model.display(row.folder) + ": " : "") + note.text)
                    .font(.caption)
                    .foregroundStyle(note.kind == .info ? Color.secondary : (note.kind == .warning ? Color.orange : Color.hex("f87171")))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
