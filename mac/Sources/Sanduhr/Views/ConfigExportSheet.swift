import AppKit
import SwiftUI

/// What the export sheet works on: one seat's rows and which are chosen (item 76, slice C).
struct ConfigExportState: Identifiable {
    let seat: String
    let label: String
    /// A folder marked work: the pack is stamped so, and the sheet says who can read it.
    var isWork = false
    /// A work seat's watch words, on this Mac only (`WatchWords`).
    var words: [String] = []
    /// Adds `any-agent/`, the same rows for other coding agents (slice H). On by default.
    var forOtherAgents = true
    var items: [ConfigPack.Item]
    var chosen: Set<String>
    var id: String { seat }
}

extension ModsPageModel {
    /// Plans the export of `seat`. A seat marked work exports too (the owner, 2026-10-15), stamped.
    func startConfigExport(_ seat: Seat) async {
        guard let scan = scans.first(where: { $0.seat == AccountData.normalized(seat.path) }) else { return }
        await refreshKept()
        let on = kept.filter { keptState($0, folder: seat.path) == .on }
        let store = keptStore
        let words = seat.isWork ? WatchWords.standard.words(for: seat.path) : []
        let items = await Task.detached(priority: .userInitiated) {
            WatchWords.flag(ConfigPack.plan(seat: seat.path, scan: scan, keptOn: on, kept: store), words: words)
        }.value
        configExport = ConfigExportState(seat: seat.path, label: seat.label, isWork: seat.isWork, words: words, items: items,
                                         chosen: Set(items.filter { $0.canTravel && $0.onByDefault }.map(\.id)))
    }

    /// Keeps `words` for the seat and flags its rows again: a row newly holding one is unchecked.
    func saveWatchWords(_ words: [String], state: inout ConfigExportState) {
        WatchWords.standard.set(words, for: state.seat)
        state.words = WatchWords.parse(words.joined(separator: "\n"))
        let before = Set(state.items.filter { !$0.watched.isEmpty }.map(\.id))
        state.items = WatchWords.flag(state.items, words: state.words)
        for item in state.items where !item.watched.isEmpty && !before.contains(item.id) { state.chosen.remove(item.id) }
    }

    /// Asks where, then writes the chosen rows.
    func finishConfigExport(_ state: ConfigExportState) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = ConfigPack.fileName(label: state.label)
        panel.canCreateDirectories = true
        panel.message = state.isWork ? "A pack from a folder marked work. Anyone you give it to can read it." : "A pack anyone you give it to can read."
        panel.prompt = "Export"
        guard panel.runModal() == .OK, let picked = panel.url else { return }
        let url = ModPack.destination(picked)
        do {
            try ConfigPack.write(state.items.filter { state.chosen.contains($0.id) }, label: state.label, to: url, fromWork: state.isWork,
                                 anyAgent: state.forOtherAgents)
            configExportNote = "Exported \(state.chosen.count) \(state.chosen.count == 1 ? "item" : "items") from \(state.label) to \(url.lastPathComponent)."
            configExport = nil
        } catch {
            configExportNote = (error as? ModPack.Failure)?.message ?? ModPack.Failure.writeFailed.message
        }
    }
}

/// Export Config (item 76, slice C): every row the seat could carry, grouped, with a checkbox;
/// rows that can't travel say why and can't be checked.
struct ConfigExportSheet: View {
    var model: ModsPageModel
    @State var state: ConfigExportState
    let close: () -> Void
    @State private var editingWords = false
    @State private var draft = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Export \(state.label)").font(.title3.weight(.semibold))
            Text("A pack describes this folder rather than copying it: plain files, plugins by where they come from, MCP servers with a ${NAME} in place of each secret, chosen settings, and environment variables by name. Your home folder is written as ~. Anyone you give the pack to can read every file in it.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if state.isWork {
                Text("This folder is marked work. The pack is stamped as from work, and opening it into a folder not marked work warns first. Leave out anything that names work: people, products, endpoints, internal repos.")
                    .font(.caption.weight(.semibold)).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
            if state.isWork { watchWords }
            Text("Never in a pack: environment variable values, MCP header and env values, sign-in, .claude.json, transcripts, memory, plugin caches, or Sanduhr's own statusline, hooks, MCP server and meters mod.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(ConfigPack.Item.Kind.allCases, id: \.self) { kind in
                        let rows = state.items.filter { $0.kind == kind }
                        if !rows.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(kind.title).font(.callout.weight(.semibold))
                                ForEach(rows) { row($0) }
                            }
                        }
                    }
                    if state.items.isEmpty {
                        Text("Nothing in this folder can go in a pack.").font(.caption).foregroundStyle(.secondary)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.trailing, 8)
            }
            .frame(height: 380)
            otherAgents
            HStack {
                let blocked = state.items.filter { !$0.canTravel }.count
                Text("\(state.chosen.count) chosen" + (blocked > 0 ? ", \(blocked) can't travel" : ""))
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Cancel", role: .cancel) { close() }
                Button("Export…") { model.finishConfigExport(state) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(state.chosen.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 620)
    }

    /// A work seat's watch words: how many rows hold one, and the list to edit, with names
    /// suggested from the seat's own rows. All of it stays on this Mac.
    @ViewBuilder private var watchWords: some View {
        let flagged = state.items.filter { !$0.watched.isEmpty }.count
        HStack(spacing: 8) {
            Text(state.words.isEmpty ? "No watch words yet." : "Watch words: \(state.words.count), held by \(flagged) \(flagged == 1 ? "row" : "rows").")
                .font(.caption)
            Button(editingWords ? "Done" : "Watch Words…") {
                if editingWords { model.saveWatchWords(WatchWords.parse(draft), state: &state) } else { draft = state.words.joined(separator: ", ") }
                editingWords.toggle()
            }
            .controlSize(.small)
            .help("Names that mean work: the company, products, people. A row holding one is flagged and unchecked. Kept on this Mac only, never in a pack.")
        }
        if editingWords {
            VStack(alignment: .leading, spacing: 4) {
                TextField("Names, separated by commas", text: $draft, axis: .vertical)
                    .lineLimit(2...4)
                let had = WatchWords.parse(draft)
                let ideas = WatchWords.suggest(state.items, known: had)
                if !ideas.isEmpty {
                    Text("Names this folder uses that aren't ordinary words. Click to add.").font(.caption).foregroundStyle(.secondary)
                    ScrollView(.horizontal) {
                        HStack(spacing: 4) {
                            ForEach(ideas, id: \.self) { word in
                                Button(word) { draft = (had + [word]).joined(separator: ", ") }.controlSize(.small)
                            }
                        }
                    }
                }
            }
        }
    }

    /// The any-agent/ switch and what it adds, or why it can't this time.
    @ViewBuilder private var otherAgents: some View {
        let extra = ConfigPack.anyAgentFiles(state.items.filter { state.chosen.contains($0.id) })
        Toggle(isOn: Binding(get: { state.forOtherAgents && extra != nil }, set: { state.forOtherAgents = $0 })) {
            VStack(alignment: .leading, spacing: 1) {
                Text("Also for other agents (any-agent/)").font(.callout)
                Text(extra.map { "Adds \($0.count) files: skills, AGENTS.md and MCP server files for Codex, Gemini CLI, Cursor and VS Code." }
                     ?? "Too big with it: the pack would pass 256 files or 20 MB. Uncheck some rows to add it.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .disabled(extra == nil)
    }

    private func row(_ item: ConfigPack.Item) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Toggle(isOn: Binding(
                get: { state.chosen.contains(item.id) },
                set: { on in if on { state.chosen.insert(item.id) } else { state.chosen.remove(item.id) } })) {
                HStack(spacing: 6) {
                    Text(item.name).font(.callout)
                    if !item.detail.isEmpty {
                        Text(item.detail).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.tail)
                    }
                }
            }
            .disabled(!item.canTravel)
            if !item.watched.isEmpty {
                Text("Names work: " + item.watched.joined(separator: ", ")).font(.caption).foregroundStyle(.orange).padding(.leading, 20)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let why = item.blocked {
                Text(why).font(.caption).foregroundStyle(.orange).padding(.leading, 20)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
