import AppKit
import SwiftUI

/// About's Reset Sanduhr… (Settings v2, slice 5): what will be removed, each part a checkbox, all
/// checked; the Claude Code folders listed with their entries, each folder its own checkbox.
/// Reset and Quit runs the checked steps in order (Reset.swift) and lists how each went; Quit
/// then ends Sanduhr, and the next launch starts fresh with the welcome sheet.
///
/// This button is the only place a real reset starts. The smoke hook has only `reset --dry-run`,
/// which reads the plan and touches nothing.
@MainActor
final class ResetSheetModel: ObservableObject {
    @Published var parts: Set<ResetPart> = Set(ResetPart.allCases)
    @Published var folders: [(folder: String, kinds: [IntegrationKind])] = []
    @Published var checkedFolders: Set<String> = []
    @Published var results: [ResetResult]?

    let home = NSHomeDirectory()

    func load() {
        folders = Reset.claudeCodeFolders(.standard)
        checkedFolders = Set(folders.map(\.folder))
        results = nil
    }

    /// Some folder's entries will stay: the part is off, or a folder is unchecked.
    var leavesClaudeCodeEntries: Bool {
        guard !folders.isEmpty else { return false }
        return !parts.contains(.claudeCode) || checkedFolders.count < folders.count
    }

    func display(_ folder: String) -> String {
        ClaudeCodeFolders.Folder(path: folder).display(home: home)
    }

    func binding(_ part: ResetPart) -> Binding<Bool> {
        Binding(get: { self.parts.contains(part) },
                set: { on in if on { self.parts.insert(part) } else { self.parts.remove(part) } })
    }

    func binding(folder: String) -> Binding<Bool> {
        Binding(get: { self.checkedFolders.contains(folder) },
                set: { on in if on { self.checkedFolders.insert(folder) } else { self.checkedFolders.remove(folder) } })
    }

    /// The confirmed button: the real reset, in order, every step tried.
    func resetNow() {
        let env = ResetEnvironment.standard
        let steps = Reset.plan(parts, folders: checkedFolders, in: env)
        AppDelegate.resetting = true
        results = Reset.perform(steps, in: env)
    }

    var failures: Int { results?.filter { !$0.ok }.count ?? 0 }
}

struct ResetSheet: View {
    @ObservedObject var model: ResetSheetModel
    var dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(model.results == nil ? SettingsNames.resetSanduhr : "Sanduhr was reset")
                .font(.title2.bold())
            if let results = model.results {
                ResetResultsList(results: results, failures: model.failures)
            } else {
                ResetChoices(model: model)
            }
            ResetSheetButtons(model: model, dismiss: dismiss)
        }
        .padding(20)
        .frame(width: 520)
        .frame(minHeight: 420)
        // Once the run started, only Quit leaves the sheet.
        .interactiveDismissDisabled(model.results != nil)
    }
}

/// The parts, each a checkbox, with the Claude Code folders under their own.
private struct ResetChoices: View {
    @ObservedObject var model: ResetSheetModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                Text("Checked items are removed, in this order. Sanduhr then quits, and the next launch starts fresh.")
                    .foregroundStyle(.secondary)
                ForEach(ResetPart.allCases) { part in
                    VStack(alignment: .leading, spacing: 4) {
                        Toggle(part.title, isOn: model.binding(part))
                            .toggleStyle(.checkbox)
                            .font(.body.bold())
                        Text(part.detail).font(.caption).foregroundStyle(.secondary)
                            .padding(.leading, 20)
                        if part == .claudeCode {
                            ResetFolderList(model: model)
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// Every folder holding a Sanduhr entry, with its entries; a warning when some will stay.
private struct ResetFolderList: View {
    @ObservedObject var model: ResetSheetModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if model.folders.isEmpty {
                Text("No Claude Code folder holds anything of Sanduhr's.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            ForEach(model.folders, id: \.folder) { row in
                Toggle(isOn: model.binding(folder: row.folder)) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(model.display(row.folder)).font(.callout.monospaced())
                        Text(row.kinds.map(\.title).joined(separator: ", "))
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                .toggleStyle(.checkbox)
                .disabled(!model.parts.contains(.claudeCode))
            }
            if model.leavesClaudeCodeEntries {
                Label("Sanduhr's entries stay in the unchecked folders, and its scripts stay with them so those folders keep working. Remove them later in Settings, Claude Code.",
                      systemImage: "exclamationmark.triangle.fill")
                    .font(.caption)
                    .foregroundStyle(.orange)
            }
        }
        .padding(.leading, 20)
    }
}

/// After the run: one row per step, failures first said in the header.
private struct ResetResultsList: View {
    let results: [ResetResult]
    let failures: Int

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(failures == 0
                 ? "Every step finished. Quit Sanduhr, and the next launch starts fresh."
                 : "\(failures) of \(results.count) steps didn't finish; the rest did. Quit Sanduhr, and the next launch starts fresh.")
                .foregroundStyle(.secondary)
            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    if results.isEmpty { Text("Nothing was checked.").foregroundStyle(.secondary) }
                    ForEach(results) { r in
                        ResetResultRow(result: r)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }
}

private struct ResetResultRow: View {
    let result: ResetResult

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Image(systemName: result.ok ? "checkmark.circle.fill" : "xmark.octagon.fill")
                .foregroundStyle(result.ok ? Color.green : Color.red)
                .accessibilityLabel(result.ok ? "Done" : "Failed")
            VStack(alignment: .leading, spacing: 1) {
                Text("\(result.step.part.title): \(result.step.target)")
                if let error = result.error {
                    Text(error).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

private struct ResetSheetButtons: View {
    @ObservedObject var model: ResetSheetModel
    var dismiss: () -> Void

    var body: some View {
        HStack {
            Spacer()
            if model.results == nil {
                Button("Cancel", action: dismiss).keyboardShortcut(.cancelAction)
                Button("Reset and Quit", role: .destructive) { model.resetNow() }
                    .disabled(model.parts.isEmpty)
            } else {
                Button("Quit Sanduhr") { NSApp.terminate(nil) }
                    .keyboardShortcut(.defaultAction)
            }
        }
    }
}
