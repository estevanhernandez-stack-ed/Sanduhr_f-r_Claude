import SwiftUI

/// Settings, Mods & Config (item 64; raw value `mods`): Sanduhr's own mod first, Meters above the
/// prompt, with its switch, Update and Remove per folder (its one home since 2026-10-08; slice 2
/// had it on Claude Code; a header above More since 2026-10-17), then, under More: mods and
/// config, every mod and plugin each Claude Code folder loads, what each draws and can reach,
/// and Check (Claude Code's own validator) for a risk card. Other mods are read-only.
struct ModsSettings: View {
    var vm: UsageViewModel
    var model: ModsPageModel
    @Environment(SettingsNavigation.self) private var navigation: SettingsNavigation?

    /// Packs opened from Finder: New Mod's From Claude opens and reads each (item 76, slice B).
    private func openPacksIfAny() {
        let urls = PackInbox.shared.take()
        guard !urls.isEmpty else { return }
        // A mod pack goes to New Mod, a config pack to the Open sheet (item 76, slices B and D).
        Task { for url in urls { await model.openAnyPack(url, store: .shared) } }
    }

    /// A link or search into More (the Mods and plugins anchor) opens it, so the window's scroll
    /// finds the tab bar. The meters anchor is the header at the top, which needs nothing opened.
    private func openMoreIfAsked() {
        guard navigation?.anchor == SettingsAnchor.inventory else { return }
        model.moreOpenedByLink = true
    }

    private var linked: [String] { vm.realAccountLabels.compactMap { vm.dataChoices(for: $0).folder } }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    // Sanduhr's own mod first (2026-10-17): the switch per folder, under the title.
                    MetersHeader(model: model, linked: linked, demo: vm.demo)
                        .settingsAnchor(SettingsAnchor.metersMod)
                    ModsMore(model: model, demo: vm.demo) {
                        // The agents switch on the page (polish 2026-10-17, #20); New Mod mirrors it.
                        ModsFromClaudeRow()
                        ModsSummaryCard(model: model)
                        // Catalog | By Seat (item 71, slice 1), right above what each shows.
                        ModsTabBar(model: model, demo: vm.demo)
                            .settingsAnchor(SettingsAnchor.inventory)
                        // Check needs `claude`: the note sits with the cards that offer it, not at
                        // the page top (polish 2026-10-17, #25).
                        if model.loaded && model.claude == nil {
                            Text(ModsPageModel.cliMissing)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        tabContent
                    }
                    ModsFolderActions(model: model, linked: linked)
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .padding(20)
            }
            .onChange(of: model.focus) {
                guard let f = model.focus else { return }
                DispatchQueue.main.async { withAnimation { proxy.scrollTo(ModsPageModel.seatAnchor(f.seat), anchor: .top) } }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task { await model.load(linked: linked) }
        // Links and search that name the meters mod land on the header (2026-10-17; from
        // 2026-10-14 they opened the Studio's Folders tab).
        .onAppear { openMoreIfAsked(); openPacksIfAny() }
        .onChange(of: PackInbox.shared.urls) { openPacksIfAny() }
        .sheet(item: Binding(get: { model.configImport }, set: { model.configImport = $0 })) { state in
            ConfigImportSheet(model: model, demo: vm.demo, state: state, close: { model.configImport = nil })
        }
        .onChange(of: navigation?.scrollRequest) { openMoreIfAsked() }
        .sheet(isPresented: Binding(get: { model.studioOpen }, set: { model.studioOpen = $0 })) {
            ModStudioSheet(model: .live(), mods: model, linked: linked, demo: vm.demo, tab: model.studioTab,
                           close: { model.studioOpen = false })
        }
    }

    @ViewBuilder
    private var tabContent: some View {
        switch model.tab {
        case .catalog:
            ModsCatalogView(model: model, demo: vm.demo)
        case .bySeat:
            ModsBySeatView(model: model, demo: vm.demo, linked: linked)
        case .compare:
            ModsCompareView(model: model, demo: vm.demo)
        }
    }
}

/// More: mods and config (2026-10-17): the summary card, the tabs and what they show, under one
/// disclosure so a first-timer sees the header alone. It starts open when the user has a kept
/// mod, a waiting proposal or install request, or more than one folder; opened or closed by
/// hand, it stays so (ModsPageModel.moreExpandedKey).
private struct ModsMore<Content: View>: View {
    var model: ModsPageModel
    let demo: Bool
    @ViewBuilder let content: () -> Content

    /// Computed: a generic type can't hold a static stored property.
    static var title: String { "More: mods and config" }

    var body: some View {
        let proposals = ModProposalStore.shared
        let waiting = proposals.pendingFromClaude.count + proposals.pendingInstalls.count
        DisclosureGroup(isExpanded: expanded(waiting: waiting)) {
            VStack(alignment: .leading, spacing: 14) { content() }
                .padding(.top, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
        } label: {
            Text(Self.title).font(.headline)
        }
    }

    private func expanded(waiting: Int) -> Binding<Bool> {
        Binding(get: { model.moreExpanded(demo: demo, waiting: waiting) }, set: { model.setMoreExpanded($0) })
    }
}

/// Read Again, Add Folder… and what the last read or add said, under either tab.
private struct ModsFolderActions: View {
    var model: ModsPageModel
    let linked: [String]

    var body: some View {
        if model.receiptsSkipped > 0 {
            Text(ModsPageModel.receiptsSkippedText(model.receiptsSkipped))
                .font(.caption)
                .foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
        HStack(spacing: 8) {
            Button(model.loading ? "Reading…" : "Read Again") { Task { await model.load(linked: linked) } }
                .disabled(model.loading)
            Button("Add Folder…") { Task { await model.addFolder(linked: linked) } }
                .disabled(model.loading)
                .help("Add a Claude Code folder Sanduhr didn't find. Sanduhr remembers it; nothing is written into the folder.")
        }
        if let note = model.addNote {
            Text(note).font(.caption).foregroundStyle(Color.hex("f87171"))
        }
    }
}

struct ModsIntro: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Mods and plugins").font(.subheadline.weight(.semibold))
            Text("Everything each Claude Code folder loads: mods (plugins with a hooks module that draw in Claude Code) and plain plugins, from the plugin folders its settings list, its installed plugins, its skills folder and the mods a session made. Sanduhr finds them by reading files: no mod runs. Other mods are read-only here: Sanduhr's own switches per folder in Meters above the prompt, at the top of the page, and switching other mods comes later. Check asks Claude Code's own validator, which reads a mod without running it, what the mod hooks and calls.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// A seat's mods and plugins (item 64's list), now Extensions' Mods subsection on By Seat
/// (item 71, slice 1b): each with where it loads from, what it touches, its sketch and Check.
struct ModsSeatMods: View {
    let seat: String
    var model: ModsPageModel

    var body: some View {
        let items = model.inventory.first { $0.folder == seat }?.items ?? []
        VStack(alignment: .leading, spacing: 10) {
            ModsIntro()
            if items.isEmpty {
                Text("Loads no mods or plugins.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(items) { item in
                ModRow(item: item, model: model)
                    .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(lit(item) ? 0.18 : 0)).padding(-4))
                if item.id != items.last?.id { Divider() }
            }
        }
    }

    /// The item a Catalog chip opened this seat for.
    private func lit(_ item: ModItem) -> Bool {
        guard let f = model.focus, f.seat == seat else { return false }
        return item.name == f.name
    }
}

/// From Claude, on the page (polish 2026-10-17, #20): the agents switch, in plain words, at the
/// top of More. New Mod's From Claude shows the same switch and caption; both bind to
/// ModProposalStore.shared, so they never disagree.
struct ModsFromClaudeRow: View {
    static let title = "Let agents propose mods"
    static let caption = "Claude can hand you mods and config to review here (New Mod, From Claude, and the folder's row on By Folder). Off: Claude's requests are refused, and the Build prompt leaves that step out."

    var body: some View {
        @Bindable var proposals = ModProposalStore.shared
        VStack(alignment: .leading, spacing: 4) {
            Text("From Claude").font(.subheadline.weight(.semibold))
            Toggle(Self.title, isOn: $proposals.allowed)
            Text(Self.caption)
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The page's summary card (item 68's pattern): the counts across folders, on the preview
/// wallpaper. Since 2026-10-17 it sits under More, on the page, after the From Claude row.
struct ModsSummaryCard: View {
    var model: ModsPageModel

    var body: some View {
        SettingsPreviewCard(kind: .mods, label: label) {
            VStack(spacing: 10) {
                if model.loaded {
                    HStack(spacing: 14) {
                        let tiles = self.tiles
                        ForEach(tiles.indices, id: \.self) { i in ModsTile(number: tiles[i].0, title: tiles[i].1) }
                    }
                } else {
                    Text("Reading your Claude Code folders…").foregroundStyle(.white.opacity(0.8))
                }
                Text("Sanduhr's own mod switches per folder in Meters above the prompt, at the top of the page; other mods are read-only.")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(12)
        }
    }

    private var tiles: [(number: Int, title: String)] { model.counts.tiles }

    private var label: String {
        guard model.loaded else { return "Reading your Claude Code folders." }
        return model.counts.summary
    }
}

private struct ModsTile: View {
    let number: Int
    let title: String

    var body: some View {
        VStack(spacing: 2) {
            Text("\(number)").font(.system(size: 30, weight: .semibold, design: .rounded)).foregroundStyle(.white)
            Text(title).font(.caption).foregroundStyle(.white.opacity(0.75))
        }
        .frame(minWidth: 70)
        .padding(.vertical, 8)
        .padding(.horizontal, 10)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.white.opacity(0.08)))
    }
}
