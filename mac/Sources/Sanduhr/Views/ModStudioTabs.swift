import AppKit
import SwiftUI

/// Item 75, slice C: the Mod Studio's tabs. Look (slice B) styles the band; Folders is where the
/// mod is switched per Claude Code folder, the rows Mods & Config carried until now, unchanged in
/// behavior; About is what the mod can do, read by `claude plugin validate`, before turning it on.
enum StudioTab: String, CaseIterable, Identifiable, Sendable {
    case look, folders, about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .look: "Look"
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
