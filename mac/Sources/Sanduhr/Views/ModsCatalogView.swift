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
        .frame(maxWidth: 260)
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
/// added lists, one card each, with a chip per seat. Read-only: Sanduhr's own mod switches
/// above, nothing here writes.
struct ModsCatalogView: View {
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        let cards = model.shownCards(demo: demo)
        VStack(alignment: .leading, spacing: 10) {
            Text("Everything your Claude Code folders hold or a marketplace they added lists, one card each, with a chip for every folder (seat) that has it. Read from files only: nothing is fetched, run or changed. Click a chip to open that seat.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            CatalogFilterBar(model: model)
            Text(countLine(shown: cards.count))
                .font(.caption)
                .foregroundStyle(.secondary)
            if model.loaded && cards.isEmpty {
                Text("Nothing matches these filters.").font(.caption).foregroundStyle(.secondary)
            }
            LazyVStack(alignment: .leading, spacing: 10) {
                ForEach(cards) { CatalogCardView(card: $0, model: model) }
            }
        }
    }

    private func countLine(shown: Int) -> String {
        guard model.loaded else { return "Reading your Claude Code folders…" }
        let total = ConfigCatalog.filter(model.catalog, CatalogFilter(), demo: demo).count
        return shown == total ? "\(total) in the catalog." : "\(shown) of \(total) shown."
    }
}

private struct CatalogFilterBar: View {
    @Bindable var model: ModsPageModel

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
                    ForEach(model.seats) { Text($0.label).tag(String?.some($0.path)) }
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

    /// The validator's answer for one of its items, when Check ran.
    private var riskLine: String? {
        for id in card.itemIDs {
            guard case .report(let r)? = model.checks[id] else { continue }
            return "Checked: " + r.risk.title + (r.passed ? "" : ", Claude Code would refuse it")
        }
        return nil
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
/// folder. A seat whose linked account is marked work stays work (the link only ever marks work).
struct SeatLabelControls: View {
    let seat: Seat
    var model: ModsPageModel
    @State private var draft = ""

    var body: some View {
        HStack(spacing: 8) {
            TextField("Label", text: $draft, prompt: Text(seat.name))
                .textFieldStyle(.roundedBorder)
                .frame(maxWidth: 160)
                .onSubmit { model.setLabel(draft, for: seat.path) }
                .help("Your name for this folder. Sanduhr never takes one from the folder's files.")
            if seat.markSource == .account {
                SeatMarkBadge(mark: .work)
                Text("from its account").font(.caption).foregroundStyle(.secondary)
            } else {
                Picker("Side", selection: Binding(get: { seat.mark }, set: { model.setMark($0, for: seat.path) })) {
                    Text("Unmarked").tag(SeatMark.unmarked)
                    Text("Work").tag(SeatMark.work)
                    Text("Personal").tag(SeatMark.personal)
                }
                .labelsHidden()
                .frame(maxWidth: 120)
            }
        }
        .controlSize(.small)
        .onAppear { draft = seat.labelChosen ? seat.label : "" }
        .onChange(of: seat.label) { draft = seat.labelChosen ? seat.label : "" }
    }
}
