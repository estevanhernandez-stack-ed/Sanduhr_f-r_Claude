import SwiftUI

/// Compare (item 71, slice 1d): two seats side by side. What one has and the other doesn't, what
/// both have in a different state, and what is the same, with each seat's session load on top.
/// Names and states only, read from the Catalog: nothing is written, and settings values stay on
/// By Seat's Drift, where the wall decides whether they show.
struct ModsCompareView: View {
    @Bindable var model: ModsPageModel
    let demo: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Two seats side by side: what each has that the other doesn't, what both have in a different state, and what is the same. Names and states only. Settings values are on By Seat, under Drift.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if !model.loaded {
                Text("Reading your Claude Code folders…").font(.caption).foregroundStyle(.secondary)
            } else if let pair = model.comparePair(demo: demo), let cmp = model.comparison(demo: demo) {
                CompareSeatPickers(model: model, demo: demo, a: pair.a, b: pair.b)
                HStack(alignment: .top, spacing: 12) {
                    CompareLoad(seat: pair.a, load: model.load(pair.a.path))
                    CompareLoad(seat: pair.b, load: model.load(pair.b.path))
                }
                ForEach(SeatComparison.Bucket.allCases, id: \.self) { bucket in
                    CompareBucketView(model: model, bucket: bucket, rows: cmp.rows(bucket), a: pair.a, b: pair.b)
                }
                Text(footnote(pair.a, pair.b))
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text(demo ? "Compare needs two seats that aren't work seats while demo mode is on." : "Compare needs two seats. Add Folder… lists another one.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    /// What a later slice will offer, and the wall's direction rule, in words.
    private func footnote(_ a: Seat, _ b: Seat) -> String {
        let rule = "Installing or turning things on in another seat comes in a later slice, through Claude Code's own CLI. A plugin from a marketplace can go into any seat by name; your own skills, agents and commands are copied only out of a personal seat, never out of a work one."
        return SeatWall.allowsCopy(from: a, to: b) || SeatWall.allowsCopy(from: b, to: a) ? rule : rule + " Neither of these two is marked personal."
    }
}

private struct CompareSeatPickers: View {
    @Bindable var model: ModsPageModel
    let demo: Bool
    let a: Seat
    let b: Seat

    var body: some View {
        HStack(spacing: 8) {
            picker("First seat", selection: Binding(get: { a.path }, set: { model.compareA = $0 }))
            Button {
                model.compareA = b.path
                model.compareB = a.path
            } label: {
                Image(systemName: "arrow.left.arrow.right")
            }
            .buttonStyle(.borderless)
            .help("Swap the two seats")
            .accessibilityLabel("Swap the two seats")
            picker("Second seat", selection: Binding(get: { b.path }, set: { model.compareB = $0 }))
        }
        .controlSize(.small)
    }

    private func picker(_ title: String, selection: Binding<String>) -> some View {
        Picker(title, selection: selection) {
            ForEach(model.filterSeats(demo: demo)) { seat in
                Text(seat.label + " · " + SeatMarkBadge(mark: seat.mark).title).tag(seat.path)
            }
        }
        .labelsHidden()
        .frame(maxWidth: 220)
        .accessibilityLabel(title)
    }
}

/// One seat's session load, in a line or two.
private struct CompareLoad: View {
    let seat: Seat
    let load: SessionLoad?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                Text(seat.label).font(.caption.weight(.semibold))
                SeatMarkBadge(mark: seat.mark)
            }
            if let load {
                Text("About \(SessionLoad.short(load.listingTokens + load.agentTokens)) tokens before you type: \(load.listed + load.nameOnly) skill list entries, \(load.agents) agents, \(load.mcpServers) MCP servers.")
                    .font(.caption)
                    .foregroundStyle(load.overBudget ? Color.orange : Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct CompareBucketView: View {
    var model: ModsPageModel
    let bucket: SeatComparison.Bucket
    let rows: [SeatComparison.Row]
    let a: Seat
    let b: Seat

    var body: some View {
        let open = model.openBuckets.contains(bucket)
        VStack(alignment: .leading, spacing: 6) {
            Button {
                if open { model.openBuckets.remove(bucket) } else { model.openBuckets.insert(bucket) }
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: open ? "chevron.down" : "chevron.right")
                        .font(.caption.weight(.semibold))
                        .frame(width: 12)
                    Text(title).font(.callout.weight(.semibold))
                    Text("\(rows.count)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    Spacer()
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("\(title), \(rows.count)")
            .accessibilityValue(open ? "Open" : "Closed")
            if open {
                if rows.isEmpty {
                    Text("Nothing.").font(.caption).foregroundStyle(.secondary)
                }
                ForEach(runs, id: \.0) { kind, list in
                    Text(kind.title + " · \(list.count)")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                    LazyVStack(alignment: .leading, spacing: 3) {
                        ForEach(list) { CompareRow(row: $0, bucket: bucket, a: a, b: b) }
                    }
                }
            }
        }
        .padding(.top, 4)
    }

    private var title: String {
        switch bucket {
        case .onlyA: "Only in \(a.label)"
        case .onlyB: "Only in \(b.label)"
        case .differ: "In both, in a different state"
        case .same: "The same in both"
        }
    }

    /// The rows by kind, in the Catalog's kind order.
    private var runs: [(CatalogKind, [SeatComparison.Row])] {
        CatalogKind.allCases.compactMap { k in
            let list = rows.filter { $0.card.kind == k }
            return list.isEmpty ? nil : (k, list)
        }
    }
}

private struct CompareRow: View {
    let row: SeatComparison.Row
    let bucket: SeatComparison.Bucket
    let a: Seat
    let b: Seat

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(row.card.name).font(.caption.weight(.semibold))
            Text(state).font(.caption).foregroundStyle(.secondary)
            if let c = row.card.contents, !c.summary.isEmpty {
                Text(c.summary).font(.caption2).foregroundStyle(.secondary)
            }
        }
        .help(row.card.description)
    }

    private var state: String {
        switch bucket {
        case .onlyA: row.a?.title ?? ""
        case .onlyB: row.b?.title ?? ""
        case .differ: "\(a.label): \(row.a?.title ?? "") · \(b.label): \(row.b?.title ?? "")"
        case .same: row.a?.title ?? ""
        }
    }
}
