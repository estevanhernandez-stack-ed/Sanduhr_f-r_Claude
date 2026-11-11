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
    /// A sprite motion waiting for review (Claude's `propose_sprite_motion`).
    var motion = SpriteMotionHandoff.shared

    var body: some View {
        Form {
            WidgetsSuggestionSection(handoff: handoff)
            WidgetsMotionSection(handoff: motion)
            WidgetsCatalogSection(model: model)
            WidgetsInstalledSection(model: model)
            WidgetsNewSpriteSection(model: model)
            WidgetsCompanionSection(model: model)
        }
        .formStyle(.grouped)
        .task { await model.refreshCatalog() }
        // Installed theme packs join the Theme menu and the gallery: they re-read the registry.
        .onChange(of: model.installed) { _, _ in vm.userThemesTick &+= 1 }
        // A proposal arrived or was decided: the installed list and the companion pick are read again (Apply installs).
        .onChange(of: handoff.pending?.id) { _, _ in model.reloadInstalled() }
        .onChange(of: motion.pending?.id) { _, _ in model.reloadInstalled() }
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
        }
    }
}

/// The motion review banner, while a motion waits.
private struct WidgetsMotionSection: View {
    let handoff: SpriteMotionHandoff

    var body: some View {
        if let review = handoff.model {
            Section {
                SpriteMotionBanner(
                    model: review, previewNote: handoff.previewNote,
                    preview: { handoff.previewOnDesk() },
                    endPreview: { handoff.endPreview() },
                    apply: { handoff.apply() },
                    decline: { handoff.decline() })
            }
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

/// "New sprite from an image...": pick a PNG or an animated GIF, then the sheet tags its cells to actions and makes a sprite of it.
private struct WidgetsNewSpriteSection: View {
    let model: WidgetsModel
    @State private var sprite: NewSpriteModel?

    var body: some View {
        Section("New sprite") {
            HStack {
                Text("Make a companion from your own sprite sheet.")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("New sprite from an image\u{2026}") { open() }
                    .settingsAnchor(SettingsAnchor.newSprite)
            }
            WidgetsFromFileRow(model: model)
        }
        .sheet(isPresented: Binding(get: { sprite != nil }, set: { if !$0 { sprite = nil } })) {
            if let sprite { NewSpriteSheet(model: sprite, close: { self.sprite = nil }) }
        }
    }

    private func open() {
        guard let data = NewSpritePicker.pick() else { return }
        let install: (WidgetNewSprite.Input) -> [String] = { model.installNewSprite($0) }
        guard NewSpritePicker.looksLikeGif(data) else {
            sprite = NewSpriteModel(png: data, install: install)
            return
        }
        switch SpriteFrames.read(data) {
        case .success(let gif): sprite = NewSpriteModel(gif: gif, install: install)
        case .failure(let why): sprite = NewSpriteModel(refusal: why.message, install: install)
        }
    }
}

/// "Install widget from file...": a pack zip made outside Sanduhr, through the same checks as the catalog's.
private struct WidgetsFromFileRow: View {
    let model: WidgetsModel

    var body: some View {
        HStack {
            Text("Install a widget pack from a .zip, from a pack maker or compressed in Finder. It is checked like a catalog download and stays on this Mac.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer()
            Button("Install widget from file\u{2026}") { WidgetFilePicker.run(model: model) }
                .accessibilityLabel("Install widget from file")
        }
        if let status = model.fileInstallStatus {
            Text(status).font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        if let status = model.exportStatus {
            Text(status).font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The panel for "Install widget from file...": one `.zip`, read once, at most one byte past the catalog's cap so a
/// file over it is refused by `WidgetFileInstall` without being read whole. The panel is only the source of bytes;
/// the checks, the install and the companion choice are `WidgetsModel`'s. Nothing else on disk is touched.
@MainActor
private enum WidgetFilePicker {
    static func run(model: WidgetsModel) {
        model.clearFileInstallStatus()
        guard let url = pick() else { return }
        model.installFromFile(read(url))
    }

    private static func pick() -> URL? {
        let panel = NSOpenPanel()
        panel.title = "Install widget from file"
        panel.message = "Choose a widget pack (.zip)."
        panel.allowedContentTypes = [UTType.zip]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.canCreateDirectories = false
        return panel.runModal() == .OK ? panel.url : nil
    }

    private static func read(_ url: URL) -> Data? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        return try? handle.read(upToCount: WidgetZip.maxZipBytes + 1)
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
                if let note = model.fontNote(row) {
                    Text(note)
                        .font(.caption).foregroundStyle(.orange)
                }
                if model.drawsNothing(row) {
                    Text(WidgetsModel.effectsNote)
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            if model.canUseOwnArt(row) {
                if model.firstOwnArtRow?.id == row.id {
                    ownArtButton.settingsAnchor(SettingsAnchor.ownArt)
                } else {
                    ownArtButton
                }
            }
            if model.canExport(row) { exportButton }
            Button("Uninstall") { model.uninstall(row.id) }
        }
    }
}

private extension WidgetsInstalledRow {
    var ownArtButton: some View {
        Button("Use my own art\u{2026}") { OwnArtPicker.run(model: model, id: row.id) }
            .accessibilityLabel("Use my own art for \(row.name)")
    }
}

private extension WidgetsInstalledRow {
    var exportButton: some View {
        Button("Export\u{2026}") { ExportPicker.run(model: model, id: row.id) }
            .accessibilityLabel("Export \(row.name)")
    }
}

/// "Export..." (spec 21): a save panel named `<id>-<version>.zip`. The checks and the zip are `WidgetExport`'s and the
/// file is written only where the panel says, atomically. Nothing is uploaded or shown to Claude.
@MainActor
private enum ExportPicker {
    static func run(model: WidgetsModel, id: String) {
        model.clearExportStatus()
        guard case .zip(let data, let fileName) = model.exportOutcome(for: id) else { return }
        let panel = NSSavePanel()
        panel.title = "Export widget"
        panel.nameFieldStringValue = fileName
        panel.allowedContentTypes = [UTType.zip]
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        model.writeExport(data, to: url, id: id, fileName: fileName)
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
