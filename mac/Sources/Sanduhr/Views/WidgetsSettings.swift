import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// Settings, Widgets (spec 3): the widget catalog with Install, Update or Installed, what is
/// installed with Uninstall, and the Desk companion's two choices. The catalog is fetched when the
/// page appears and on Check the catalog again; nothing polls.
struct WidgetsSettings: View {
    let vm: UsageViewModel
    let model: WidgetsModel
    /// The proposal waiting for review (Claude's `propose_widget`); observed, so the banner comes and goes with it.
    var handoff = WidgetProposalHandoff.shared

    var body: some View {
        Form {
            WidgetsSuggestionSection(handoff: handoff)
            WidgetsCatalogSection(model: model)
            WidgetsInstalledSection(model: model)
            WidgetsCompanionSection(model: model)
        }
        .formStyle(.grouped)
        .task { await model.refreshCatalog() }
        // Installed theme packs join the Theme menu and the gallery: they re-read the registry.
        .onChange(of: model.installed) { _, _ in vm.userThemesTick &+= 1 }
        // A proposal arrived or was decided: the installed list and the companion pick are read again (Apply installs).
        .onChange(of: handoff.pending?.id) { _, _ in model.reloadInstalled() }
    }
}

/// The review banner, while a proposal waits.
private struct WidgetsSuggestionSection: View {
    let handoff: WidgetProposalHandoff

    var body: some View {
        if let review = handoff.model {
            Section {
                WidgetSuggestionBanner(
                    model: review, previewNote: handoff.previewNote,
                    preview: { handoff.previewOnDesk() },
                    endPreview: { handoff.endPreview() },
                    makeCompanion: { handoff.apply(makeCompanion: true) },
                    apply: { handoff.apply() },
                    dismiss: { handoff.dismiss() })
            }
            .settingsAnchor(SettingsAnchor.widgetSuggestion)
        }
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
            if let status = model.ownArtStatus {
                Text(status).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
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
            if model.canUseOwnArt(row) {
                Button("Use my own art\u{2026}") { OwnArtPicker.run(model: model, id: row.id) }
                    .accessibilityLabel("Use my own art for \(row.name)")
                    .settingsAnchor(SettingsAnchor.ownArt)
            }
            Button("Uninstall") { model.uninstall(row.id) }
        }
    }
}

/// "Use my own art..." (spec 3.8 section 6): one PNG per stage from an open panel, each checked before the next
/// panel; a stage whose panel is cancelled keeps its sheet. The panel is only the source of bytes: the checks and
/// the reinstall are `WidgetsModel`'s. The files are read here and copied into the pack folder; nothing is sent.
@MainActor
private enum OwnArtPicker {
    static func run(model: WidgetsModel, id: String) {
        guard let prompts = model.ownArtPrompts(for: id) else { return }
        var art: [String: Data] = [:]
        for prompt in prompts {
            guard let url = pick(prompt.title) else { continue }
            let data = read(url)
            if model.checkOwnArt(id: id, stageId: prompt.stageId, data: data) != nil { return }
            art[prompt.stageId] = data
        }
        model.useOwnArt(id: id, art: art)
    }

    private static func pick(_ title: String) -> URL? {
        let panel = NSOpenPanel()
        panel.title = title
        panel.message = title
        panel.allowedContentTypes = [.png]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        return panel.runModal() == .OK ? panel.url : nil
    }

    /// At most one byte past the 2 MB limit, so an oversized file is refused by the rule without being read whole.
    private static func read(_ url: URL) -> Data? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        return try? handle.read(upToCount: PackValidator.maxImageBytes + 1)
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
            }
            // Where the companion is placed and moved: its row under Where each piece sits.
            SettingsLinkButton(.deskLayout, anchor: SettingsAnchor.companionPlace)
        }
        .settingsAnchor(SettingsAnchor.companion)
    }
}
