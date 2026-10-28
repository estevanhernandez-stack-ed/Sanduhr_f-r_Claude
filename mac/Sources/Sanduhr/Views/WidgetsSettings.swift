import SwiftUI

/// Settings, Widgets (spec 3): the widget catalog with Install, Update or Installed, what is
/// installed with Uninstall, and the Desk companion's two choices. The catalog is fetched when the
/// page appears and on Check the catalog again; nothing polls.
struct WidgetsSettings: View {
    let vm: UsageViewModel
    let model: WidgetsModel

    var body: some View {
        Form {
            if model.justInstalledSprite != nil {
                Section { CompanionTurnOnRow(name: model.justInstalledSprite) }
            }
            WidgetsCatalogSection(model: model)
            WidgetsInstalledSection(model: model)
            WidgetsCompanionSection(model: model)
        }
        .formStyle(.grouped)
        .task { await model.refreshCatalog() }
        // Installed theme packs join the Theme menu and the gallery: they re-read the registry.
        .onChange(of: model.installed) { _, _ in vm.userThemesTick &+= 1 }
    }
}

/// The catalog's packs, each with what installing it would do.
private struct WidgetsCatalogSection: View {
    let model: WidgetsModel

    var body: some View {
        Section("Catalog") {
            Text("Sprites, themes, palettes and fonts made for Sanduhr. Each download is checked against a signed catalog before it is installed, and nothing is fetched until you open this page or press a button.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(model.catalog, id: \.id) { entry in
                WidgetsCatalogRow(model: model, entry: entry)
            }
            if model.catalog.isEmpty, model.catalogNote == nil, !model.checking {
                Text("The catalog has no widgets yet.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            WidgetsCatalogFooter(model: model)
        }
        .settingsAnchor(SettingsAnchor.catalog)
    }
}

private struct WidgetsCatalogFooter: View {
    let model: WidgetsModel

    var body: some View {
        HStack {
            Button("Check the catalog again") { Task { await model.refreshCatalog() } }
                .disabled(model.checking)
            if model.checking { ProgressView().controlSize(.small) }
            Spacer()
        }
        if let note = model.catalogNote {
            Text(note).font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        if let notice = model.notice {
            Text(notice).font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

private struct WidgetsCatalogRow: View {
    let model: WidgetsModel
    let entry: WidgetCatalogEntry

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(entry.name)
                Text("\(entry.kind) \u{00B7} \(entry.version)")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            WidgetsOfferControl(model: model, entry: entry)
        }
    }
}

/// Install, Update, or the word Installed, in place of a button that would do nothing.
private struct WidgetsOfferControl: View {
    let model: WidgetsModel
    let entry: WidgetCatalogEntry

    var body: some View {
        if model.busy.contains(entry.id) {
            ProgressView().controlSize(.small)
        } else {
            switch model.offer(for: entry) {
            case .install: Button("Install") { Task { await model.install(entry) } }
            case .update: Button("Update") { Task { await model.install(entry) } }
            case .installed: Text("Installed").foregroundStyle(.secondary)
            case .madeHere: Text("Made here").foregroundStyle(.secondary)
            }
        }
    }
}

/// What is installed: kind, version and where it came from, with Uninstall.
private struct WidgetsInstalledSection: View {
    let model: WidgetsModel

    var body: some View {
        Section("Installed") {
            if model.installed.isEmpty {
                Text("No widgets installed.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            ForEach(model.installed) { row in
                WidgetsInstalledRow(model: model, row: row)
            }
        }
        .settingsAnchor(SettingsAnchor.installedWidgets)
    }
}

private struct WidgetsInstalledRow: View {
    let model: WidgetsModel
    let row: WidgetsModel.InstalledRow

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(row.name)
                Text("\(row.kind) \u{00B7} \(row.version) \u{00B7} \(row.sourceText)")
                    .font(.caption).foregroundStyle(.secondary)
                if model.drawsNothing(row) {
                    Text(WidgetsModel.effectsNote)
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button("Uninstall") { model.uninstall(row.id) }
        }
    }
}

/// The companion: one installed sprite at a time.
private struct WidgetsCompanionSection: View {
    let model: WidgetsModel

    var body: some View {
        Section("Companion") {
            if model.sprites.isEmpty {
                Text("Install a sprite widget to pick a companion.")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                Picker("Companion", selection: Binding(
                    get: { model.activeCompanion ?? "" },
                    set: { model.pickCompanion($0.isEmpty ? nil : $0) })) {
                    ForEach(model.sprites) { Text($0.name).tag($0.id) }
                }
                Text("One companion at a time. With no pick, the first installed sprite is used.")
                    .font(.caption).foregroundStyle(.secondary)
                if model.justInstalledSprite == nil { CompanionTurnOnRow(name: nil) }
            }
        }
        .settingsAnchor(SettingsAnchor.companion)
    }
}

/// A link to where the companion is set, shown while a sprite is installed but the Desk is off or
/// the piece isn't placed: Desk Settings…, landing on the Companion's place or the Desk's switch.
private struct CompanionTurnOnRow: View {
    let name: String?
    @AppStorage(DeskArrangeMode.layoutKey, store: .desk) private var layout = DeskLayout.standard
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskOn = false

    var body: some View {
        let state = CompanionOnDesk.state(deskOn: deskOn, layout: layout)
        if let text = CompanionOnDesk.prompt(state, name: name) {
            HStack {
                Text(text)
                Spacer()
                SettingsLinkButton(.deskLayout, anchor: CompanionOnDesk.anchor(state))
                    .buttonStyle(.borderedProminent)
            }
        }
    }
}
