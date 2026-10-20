import SwiftUI

/// Which of Mods & Config's controls apply (polish 2026-10-17, #14 to #16), from what the page
/// read: a fresh profile with one folder and the stock mod sees Search, the count and the cards
/// alone. Nothing is taken away; a control appears when the catalog grows past
/// `toolbarCardThreshold` cards or `pickerFolderThreshold` folders.
struct ModsControls: Equatable, Sendable {
    static let toolbarCardThreshold = 10
    static let pickerFolderThreshold = 2

    /// The filter row (Kind, Show), the sort and Expand All / Collapse All.
    let toolbar: Bool
    /// The Folder filter and By Folder's chip picker.
    let picker: Bool
    /// The Compare segment of the tab bar.
    let compare: Bool

    /// `cards` is the whole catalog, before any filter; `narrowed` keeps the toolbar while a
    /// filter is set, so a filter chosen before the folders changed can always be cleared.
    init(cards: Int, folders: Int, narrowed: Bool = false) {
        picker = folders >= Self.pickerFolderThreshold
        compare = picker
        toolbar = cards > Self.toolbarCardThreshold || picker || narrowed
    }
}

/// Mods & Config's tabs (item 71, slice 1): Catalog | By Seat, remembered while the window lives.
/// Compare joins with two folders; the smoke hook can still show it before then.
struct ModsTabBar: View {
    @Bindable var model: ModsPageModel
    let demo: Bool

    var body: some View {
        Picker("Show", selection: $model.tab) {
            ForEach(model.tabs(demo: demo), id: \.self) { Text($0.title).tag($0) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(maxWidth: 340)
        .accessibilityLabel("Mods & Config view")
    }
}

/// A seat's side of the wall as a badge, in fixed colors: the same everywhere a seat is named.
struct SeatMarkBadge: View {
    let mark: SeatMark

    var body: some View {
        Text(title)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .foregroundStyle(color)
            .background(Capsule().fill(color.opacity(0.16)))
    }

    var title: String {
        switch mark {
        case .work: "Work"
        case .personal: "Personal"
        case .unmarked: "Not marked"
        }
    }

    var color: Color {
        switch mark {
        case .work: Color.hex("f59e0b")
        case .personal: Color.hex("38bdf8")
        case .unmarked: .secondary
        }
    }
}

/// The Catalog (item 71, slice 1): every extension any seat holds or any marketplace a seat
/// added lists, one card each, with a chip per seat. Slice 1c groups the cards (what needs a
/// look, yours, plugins by source, MCP servers, then the marketplaces' long list, closed) and
/// shows what each seat starts a session with. Read-only: Sanduhr's own mod switches above,
/// nothing here writes.
struct ModsCatalogView: View {
    @Bindable var model: ModsPageModel
    let demo: Bool

    var body: some View {
        let cards = model.shownCards(demo: demo)
        let searching = !model.filter.search.trimmingCharacters(in: .whitespaces).isEmpty
        VStack(alignment: .leading, spacing: 10) {
            Text("Everything your Claude Code folders hold or a marketplace they added lists, one line each, with a dot for every folder that has it. Click a line for its card, and a chip on the card to open that folder on By Folder. The cards are read from files: nothing is fetched, run or changed until you use a button.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(model.unreadableNotes(demo: demo), id: \.self) { note in
                Text(note).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
            if model.loaded { SessionLoadPanel(loads: model.shownLoads(demo: demo)) }
            let proposed = ModProposalStore.shared.pendingFromClaude.count + ModProposalStore.shared.pendingInstalls.count
            if proposed > 0 {
                HStack(spacing: 8) {
                    Image(systemName: "shippingbox").foregroundStyle(Color.accentColor)
                    Text(proposed == 1 ? "Claude has a mod request for you to review." : "Claude has \(proposed) mod requests for you to review.")
                        .font(.callout)
                    Spacer()
                    Button("Review…") {
                        model.newModWay = .fromClaude
                        model.newModOpen = true
                    }
                    .controlSize(.small)
                }
                .padding(8)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(0.1)))
            }
            let controls = model.controls(demo: demo)
            CatalogFilterBar(model: model, demo: demo, controls: controls)
            HStack {
                Text(countLine(shown: cards.count))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("New Mod…") {
                    model.newModWay = .build
                    model.newModOpen = true
                }
                    .controlSize(.small)
                    .help("A prompt for Claude to build a mod in a session. Copies it to the clipboard; installs nothing.")
                if controls.toolbar { toolbar(cards: cards) }
            }
            if model.loaded && cards.isEmpty {
                Text("Nothing matches these filters.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(model.sections(demo: demo)) { section in
                CatalogGroupView(section: section, model: model, searching: searching, demo: demo)
            }
        }
        .sheet(isPresented: $model.newModOpen) { NewModSheet(model: model, demo: demo, close: { model.newModOpen = false }) }
    }

    /// Expand All, Collapse All and the sort, in New Mod…'s style; shown when they apply.
    @ViewBuilder
    private func toolbar(cards: [CatalogCard]) -> some View {
        Button("Expand All") { model.expandAll(demo: demo) }
            .controlSize(.small)
            .disabled(cards.isEmpty)
        Button("Collapse All") { model.collapseAll() }
            .controlSize(.small)
            .disabled(model.openCards.isEmpty)
        Picker("Sort plugins", selection: $model.sortByLoad) {
            Text("Plugins by name").tag(false)
            Text("Plugins by session load").tag(true)
        }
        .labelsHidden()
        .controlSize(.small)
        .frame(maxWidth: 200)
    }

    private func countLine(shown: Int) -> String {
        guard model.loaded else { return "Reading your Claude Code folders…" }
        let total = ConfigCatalog.filter(model.catalog, CatalogFilter(), demo: demo).count
        return shown == total ? "\(total) in the catalog." : "\(shown) of \(total) shown."
    }
}

/// What a new session in each seat starts with before anything is typed (slice 1c), estimated
/// from the files. Claude Code shows no such figure across seats; `/context` and `/doctor` give
/// it for the session they run in.
private struct SessionLoadPanel: View {
    let loads: [(seat: Seat, load: SessionLoad)]

    var body: some View {
        if !loads.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text("What a new session starts with").font(.callout.weight(.semibold))
                ForEach(loads, id: \.seat.path) { pair in
                    SessionLoadRow(seat: pair.seat, load: pair.load)
                }
                Text("Estimated from the files at about four characters a token. Projects add their own. Inside a session, /context and /doctor give the exact figure.")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.secondary.opacity(0.08)))
        }
    }
}

private struct SessionLoadRow: View {
    let seat: Seat
    let load: SessionLoad

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                Text(seat.label).font(.caption.weight(.semibold))
                SeatMarkBadge(mark: seat.mark)
                Text("\(load.partial ? "at least" : "about") \(SessionLoad.short(load.listingTokens + load.agentTokens)) tokens before you type")
                    .font(.caption)
            }
            Text(detail).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if load.overBudget {
                Text("Over Claude Code's skill list budget (about \(SessionLoad.short(load.budgetTokens(context: 200_000))) tokens with a 200K context, \(SessionLoad.short(load.budgetTokens(context: 1_000_000))) with 1M), so it drops the descriptions of the skills you use least and Claude may miss them. Turning off plugins you don't use frees room.")
                    .font(.caption)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if !biggest.isEmpty {
                Text("Biggest: " + biggest).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var detail: String {
        var parts = ["Skill list: \(load.listed + load.nameOnly) entries, about \(SessionLoad.short(load.listingTokens)) tokens"]
        if load.hidden > 0 { parts[0] += " (\(load.hidden) kept out of it)" }
        parts.append("agents: \(load.agents), about \(SessionLoad.short(load.agentTokens))")
        if load.mcpServers > 0 {
            parts.append("\(load.mcpServers) MCP server\(load.mcpServers == 1 ? "" : "s"), their tools not counted")
        }
        return parts.joined(separator: "; ") + "."
    }

    private var biggest: String {
        load.sources.prefix(4).map { "\($0.name) \(SessionLoad.short($0.tokens))" }.joined(separator: ", ")
    }
}

/// A seat's label and side of the wall, as Sanduhr preferences: nothing is written into the
/// folder. A folder linked to an account on Accounts takes that account's name and side, set
/// there; the controls here are for folders no account links.
struct SeatLabelControls: View {
    let seat: Seat
    var model: ModsPageModel
    @State private var draft = ""
    @FocusState private var editing: Bool

    var body: some View {
        if let account = seat.account {
            linked(account)
        } else {
            own
        }
    }

    private func linked(_ account: String) -> some View {
        HStack(spacing: 8) {
            Image(systemName: "link").foregroundStyle(.secondary).accessibilityHidden(true)
            Text(account).font(.callout.weight(.medium))
            if seat.markSource == .account {
                SeatMarkBadge(mark: seat.mark)
            } else {
                Text("not marked").font(.caption).foregroundStyle(.secondary)
            }
            Button("Edit on Accounts…") { SettingsWindowController.shared.showAccount(account) }
                .help("This folder is linked to \(account). Its name and whether it is marked work or personal are set on Accounts.")
        }
        .controlSize(.small)
        .accessibilityElement(children: .combine)
    }

    private var own: some View {
        HStack(spacing: 8) {
            TextField("Label", text: $draft, prompt: Text(seat.name))
                .textFieldStyle(.roundedBorder)
                .frame(maxWidth: 160)
                .focused($editing)
                .onSubmit { save() }
                .onChange(of: editing) { if !editing { save() } }
                .help("Your name for this folder. Sanduhr never takes one from the folder's files.")
            Picker("Work or personal", selection: Binding(get: { seat.mark }, set: { model.setMark($0, for: seat.path) })) {
                Text("Not marked").tag(SeatMark.unmarked)
                Text("Work").tag(SeatMark.work)
                Text("Personal").tag(SeatMark.personal)
            }
            .fixedSize()
        }
        .controlSize(.small)
        .onAppear { draft = seat.labelChosen ? seat.label : "" }
        .onChange(of: seat.label) { draft = seat.labelChosen ? seat.label : "" }
    }

    /// Saves on Return and when the field loses focus; an unchanged label writes nothing.
    private func save() {
        let current = seat.labelChosen ? seat.label : ""
        guard draft.trimmingCharacters(in: .whitespacesAndNewlines) != current else { return }
        model.setLabel(draft, for: seat.path)
    }
}
