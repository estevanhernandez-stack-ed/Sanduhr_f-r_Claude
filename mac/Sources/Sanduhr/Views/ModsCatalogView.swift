import SwiftUI

/// Mods & Config's tabs (item 71, slice 1): Catalog | By Seat, remembered while the window lives.
struct ModsTabBar: View {
    @Bindable var model: ModsPageModel

    var body: some View {
        Picker("Show", selection: $model.tab) {
            ForEach(ModsTab.allCases, id: \.self) { Text($0.title).tag($0) }
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
        case .unmarked: "Unmarked"
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
            Text("Everything your Claude Code folders hold or a marketplace they added lists, one card each, with a chip for every folder (seat) that has it. Read from files only: nothing is fetched, run or changed. Click a chip to open that seat.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(model.unreadableNotes(demo: demo), id: \.self) { note in
                Text(note).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
            if model.loaded { SessionLoadPanel(loads: model.shownLoads(demo: demo)) }
            CatalogFilterBar(model: model, demo: demo)
            HStack {
                Text(countLine(shown: cards.count))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Picker("Sort plugins", selection: $model.sortByLoad) {
                    Text("Plugins by name").tag(false)
                    Text("Plugins by session load").tag(true)
                }
                .labelsHidden()
                .controlSize(.small)
                .frame(maxWidth: 200)
            }
            if model.loaded && cards.isEmpty {
                Text("Nothing matches these filters.").font(.caption).foregroundStyle(.secondary)
            }
            ForEach(model.sections(demo: demo)) { section in
                CatalogGroupView(section: section, model: model, searching: searching)
            }
        }
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

/// One group: a header that opens and closes it, then its runs of cards.
private struct CatalogGroupView: View {
    let section: CatalogSection
    var model: ModsPageModel
    /// A search opens every group and holds them open, so the header doesn't toggle meanwhile.
    let searching: Bool

    var body: some View {
        let open = searching || model.openGroups.contains(section.group)
        VStack(alignment: .leading, spacing: 8) {
            Button {
                if model.openGroups.contains(section.group) { model.openGroups.remove(section.group) } else { model.openGroups.insert(section.group) }
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: open ? "chevron.down" : "chevron.right")
                        .font(.caption.weight(.semibold))
                        .frame(width: 12)
                    Text(section.group.title).font(.callout.weight(.semibold))
                        .foregroundStyle(section.group == .attention ? Color.orange : Color.primary)
                    Text("\(section.count)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    Spacer()
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(searching)
            .help(searching ? "Every group is open while you search." : "")
            .accessibilityLabel("\(section.group.title), \(section.count)")
            .accessibilityValue(open ? "Open" : "Closed")
            if open {
                ForEach(section.runs) { run in
                    if !run.title.isEmpty {
                        Text(run.title + " · \(run.cards.count)")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.secondary)
                            .padding(.top, 2)
                    }
                    LazyVStack(alignment: .leading, spacing: section.group == .available ? 4 : 10) {
                        ForEach(run.cards) { card in
                            if section.group == .available {
                                AvailableRow(card: card)
                            } else {
                                CatalogCardView(card: card, model: model)
                            }
                        }
                    }
                }
            }
        }
        .padding(.top, 4)
    }
}

/// A marketplace's plugin installed nowhere: one line, so a list of hundreds stays short.
private struct AvailableRow: View {
    let card: CatalogCard

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(card.name).font(.caption.weight(.semibold))
            if let v = card.version {
                Text(v).font(.caption2.monospaced()).foregroundStyle(.secondary)
            }
            Text(card.description).font(.caption).foregroundStyle(.secondary).lineLimit(1)
        }
        .help(card.description)
    }
}

private struct CatalogFilterBar: View {
    @Bindable var model: ModsPageModel
    let demo: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Picker("Kind", selection: $model.filter.kind) {
                    Text("All kinds").tag(CatalogKind?.none)
                    ForEach(CatalogKind.allCases, id: \.self) { Text($0.title).tag(CatalogKind?.some($0)) }
                }
                .frame(maxWidth: 200)
                Picker("Seat", selection: $model.filter.seat) {
                    Text("All seats").tag(String?.none)
                    ForEach(model.filterSeats(demo: demo)) { Text($0.label).tag(String?.some($0.path)) }
                }
                .frame(maxWidth: 200)
            }
            HStack(spacing: 8) {
                Picker("Show", selection: $model.filter.show) {
                    ForEach(CatalogFilter.Show.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .frame(maxWidth: 240)
                TextField("Search name, description or marketplace", text: $model.filter.search)
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 280)
            }
        }
        .controlSize(.small)
    }
}

/// One card: what it is, where it comes from, and where it is installed.
private struct CatalogCardView: View {
    let card: CatalogCard
    var model: ModsPageModel

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            CatalogCardHeader(card: card)
            if !card.description.isEmpty {
                Text(card.description)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if !card.sources.isEmpty {
                Text("From " + card.sources.joined(separator: ", "))
                    .font(.caption)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let c = card.contents, !c.summary.isEmpty {
                PluginContentsLine(card: card, contents: c, model: model)
            }
            if let line = loadLine {
                Text(line).font(.caption).foregroundStyle(.secondary)
            }
            if card.isSanduhrs {
                Text("Switches per folder above, \(SettingsNames.metersAbovePrompt).")
                    .font(.caption)
                    .foregroundStyle(Color.hex("a78bfa"))
            }
            if let risk = riskLine {
                Text(risk).font(.caption).foregroundStyle(.orange)
            }
            ForEach(card.problems, id: \.self) { Text($0).font(.caption).foregroundStyle(.orange) }
            CatalogChips(card: card, model: model)
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.secondary.opacity(0.08)))
    }

    /// "About 1.2K tokens a session in Work, 300 in Personal": the seats where it is on.
    private var loadLine: String? {
        let parts = card.chips.compactMap { chip -> String? in
            guard let n = card.load[chip.seat], n > 0 else { return nil }
            return "\(SessionLoad.short(n)) in \(chip.label)"
        }
        return parts.isEmpty ? nil : "About " + parts.joined(separator: ", ") + " tokens a session."
    }

    /// The validator's answer for one of its items, when Check ran.
    private var riskLine: String? {
        for id in card.itemIDs {
            guard case .report(let r)? = model.checks[id] else { continue }
            return "Checked: " + r.risk.title + (r.passed ? "" : ", Claude Code would refuse it")
        }
        return nil
    }
}

/// What a plugin brings, and its names behind Show.
private struct PluginContentsLine: View {
    let card: CatalogCard
    let contents: CatalogContents
    var model: ModsPageModel

    var body: some View {
        let open = model.openContents.contains(card.id)
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Text(contents.summary).font(.caption)
                Button(open ? "Hide" : "Show") {
                    if open { model.openContents.remove(card.id) } else { model.openContents.insert(card.id) }
                }
                .buttonStyle(.link)
                .font(.caption)
                .accessibilityLabel(open ? "Hide what \(card.name) brings" : "Show what \(card.name) brings")
            }
            if open {
                ForEach(rows, id: \.0) { title, names in
                    Text(title + ": " + names.joined(separator: ", "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private var rows: [(String, [String])] {
        [("Skills", contents.skills), ("Agents", contents.agents), ("Commands", contents.commands),
         ("Output styles", contents.outputStyles)].filter { !$0.1.isEmpty }
    }
}

private struct CatalogCardHeader: View {
    let card: CatalogCard

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(card.name).font(.body.weight(.semibold))
            if let v = card.version {
                Text(v).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            CatalogBadge(text: card.kind == .plugin && card.isMod ? "Mod" : card.kind.badge, color: .secondary)
            if card.isDuplicate { CatalogBadge(text: "Duplicate", color: .orange) }
            if card.isAvailable { CatalogBadge(text: "Available", color: Color.hex("4ade80")) }
        }
    }
}

private struct CatalogBadge: View {
    let text: String
    let color: Color

    var body: some View {
        Text(text)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 6)
            .padding(.vertical, 1)
            .foregroundStyle(color == .secondary ? Color.primary : color)
            .background(Capsule().fill(color.opacity(0.18)))
    }
}

/// The seat chips: label, side of the wall and the state there. A click opens By Seat.
private struct CatalogChips: View {
    let card: CatalogCard
    var model: ModsPageModel

    var body: some View {
        if card.isAvailable {
            Text(card.isSanduhrs ? "Installed in no folder yet." : "Listed by a marketplace, installed in no folder.")
                .font(.caption)
                .foregroundStyle(.secondary)
        } else {
            ChipFlow(spacing: 6) {
                ForEach(card.chips) { chip in
                    Button { model.open(chip, card: card) } label: { CatalogChipLabel(chip: chip) }
                        .buttonStyle(.plain)
                        .help("Open \(chip.label) on By Seat")
                        .accessibilityLabel("\(chip.label), \(SeatMarkBadge(mark: chip.mark).title), \(chip.state.title)")
                }
            }
        }
    }
}

private struct CatalogChipLabel: View {
    let chip: CatalogChip

    var body: some View {
        HStack(spacing: 4) {
            Text(chip.label).font(.caption.weight(.medium))
            SeatMarkBadge(mark: chip.mark)
            Text(chip.state.title).font(.caption).foregroundStyle(stateColor)
        }
        .padding(.horizontal, 7)
        .padding(.vertical, 3)
        .background(Capsule().strokeBorder(Color.secondary.opacity(0.35)))
        .contentShape(Capsule())
    }

    private var stateColor: Color {
        switch chip.state {
        case .on: Color.hex("4ade80")
        case .declared: Color.hex("f87171")
        case .session: .orange
        case .off, .projects: .secondary
        }
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
                Text("side not set").font(.caption).foregroundStyle(.secondary)
            }
            Button("Edit on Accounts…") { SettingsWindowController.shared.showAccount(account) }
                .help("This folder is linked to \(account). Its name and Work or Personal side are set on Accounts.")
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
            Picker("Side", selection: Binding(get: { seat.mark }, set: { model.setMark($0, for: seat.path) })) {
                Text("Unmarked").tag(SeatMark.unmarked)
                Text("Work").tag(SeatMark.work)
                Text("Personal").tag(SeatMark.personal)
            }
            .labelsHidden()
            .frame(maxWidth: 120)
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
