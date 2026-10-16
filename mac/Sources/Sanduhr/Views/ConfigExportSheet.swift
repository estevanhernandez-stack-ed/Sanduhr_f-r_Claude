import AppKit
import SwiftUI

/// What the export sheet works on: one seat's rows and which are chosen (item 76, slice C).
struct ConfigExportState: Identifiable {
    let seat: String
    let label: String
    /// A folder marked work: the pack is stamped so, and the sheet says who can read it.
    var isWork = false
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
        let items = await Task.detached(priority: .userInitiated) {
            ConfigPack.plan(seat: seat.path, scan: scan, keptOn: on, kept: store)
        }.value
        configExport = ConfigExportState(seat: seat.path, label: seat.label, isWork: seat.isWork, items: items,
                                         chosen: Set(items.filter { $0.canTravel && $0.onByDefault }.map(\.id)))
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
            try ConfigPack.write(state.items.filter { state.chosen.contains($0.id) }, label: state.label, to: url, fromWork: state.isWork)
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

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Export \(state.label)").font(.title3.weight(.semibold))
            Text("A pack describes this folder rather than copying it: plain files, plugins by where they come from, MCP servers with a ${NAME} in place of each secret, chosen settings, and environment variables by name. Your home folder is written as ~. Anyone you give the pack to can read every file in it.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if state.isWork {
                Text("This folder is marked work. The pack is stamped as from work, and opening it into a folder not marked work warns first. Leave out anything that names work: people, products, endpoints, internal repos.")
                    .font(.caption.weight(.semibold)).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
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
            if let why = item.blocked {
                Text(why).font(.caption).foregroundStyle(.orange).padding(.leading, 20)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
