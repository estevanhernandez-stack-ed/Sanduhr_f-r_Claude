import SwiftUI

// The seat sections for history, housekeeping, repo, sync, drift and routes, split out of
// ModsSeatSections.swift; the views are unchanged.

// MARK: History

struct SeatHistorySection: View {
    let part: SeatReport.HistoryPart
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if part.warns {
                SeatCaption("Transcripts older than 30 days are deleted at the next start: cleanupPeriodDays isn't set, so Claude Code's default of 30 applies, as a silent sweep after a session starts.", color: .orange)
            } else {
                SeatCaption("cleanupPeriodDays: \(part.cleanupDays ?? 30) days.")
            }
            SeatCaption("desktopSessionCleanupPeriodDays (Claude Desktop and Cowork): " + (part.desktopDays.map { "\($0) days." } ?? "not set."))
            SeatCaption("\(bytes(part.transcriptBytes)) of transcripts in \(part.sessions) sessions across \(part.projects) projects; \(bytes(part.recentBytes)) written in the last 30 days.")
            if let oldest = part.oldest { SeatCaption("Oldest: \(oldest.formatted(date: .abbreviated, time: .omitted)).") }
            SeatCaption("history.jsonl: " + (part.historyBytes.map { bytes($0) + ", kept until you delete it." } ?? "none."))
            if part.orphanSidecars > 0 {
                SeatCaption("\(part.orphanSidecars) subagent folder\(part.orphanSidecars == 1 ? "" : "s") outlived their session.", color: .orange)
            }
            if part.partial {
                SeatCaption("The walk stopped at \(SeatReports.transcriptLimit) entries: these sizes are partial.", color: .orange)
            }
            if !hideNames, !part.largest.isEmpty {
                SeatSubHeading("Largest projects")
                ForEach(part.largest, id: \.name) { SeatCaption("\($0.name): \(bytes($0.bytes))") }
            }
        }
    }
}

// MARK: Housekeeping

struct SeatHousekeepingSection: View {
    let items: [SeatReport.Leftover]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if items.isEmpty { SeatCaption("Nothing left over: no Sanduhr backups, temp files, stray backups or copies.") }
            ForEach(items) { item in
                VStack(alignment: .leading, spacing: 1) {
                    Text("\(item.kind): \(item.name)").font(.caption.weight(.medium))
                    SeatCaption(detail(item))
                }
            }
            SeatCaption("Read-only for now: nothing here is deleted or moved.")
        }
    }

    private func detail(_ item: SeatReport.Leftover) -> String {
        var parts = ["Left by \(item.origin)"]
        if item.bytes > 0 { parts.append(bytes(item.bytes)) }
        if let m = item.modified { parts.append(m.formatted(date: .abbreviated, time: .omitted)) }
        parts.append(item.ifLeft)
        return parts.joined(separator: " · ")
    }
}

// MARK: Seat

struct SeatRepoSection: View {
    let part: SeatReport.RepoPart
    let hideNames: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            SeatCaption(part.model.title + (part.branch.map { ", branch \($0)" } ?? "") + ".")
            if part.model == .plain {
                SeatCaption("Unsynced folder: Sanduhr offers no conversion. The folder's own onboarding (kill switch first, fetch, review drift, dry run, enable) is the route.")
            }
            switch part.remoteMatches {
            case .some(true): SeatCaption("Its origin matches the folder's identity file.")
            case .some(false): SeatCaption("Its origin doesn't match the folder's identity file (.seat/seat.json).", color: Color.hex("f87171"))
            case .none: EmptyView()
            }
            if part.killSwitch { SeatCaption("Sync is switched off (.seat/disabled).", color: .orange) }
            SeatSyncView(sync: part.sync, hideNames: hideNames)
            if !part.buckets.isEmpty {
                SeatSubHeading("The never-list, from the folder's .gitignore")
                ForEach(SeatReport.Bucket.allCases, id: \.self) { b in
                    if let list = part.buckets[b], !list.isEmpty {
                        // A work seat's patterns can name its projects: counted only.
                        SeatCaption(hideNames ? "\(b.title): \(list.count) pattern\(list.count == 1 ? "" : "s")."
                                          : "\(b.title): " + list.map(ConfigMask.maskString).joined(separator: ", "))
                    }
                }
            }
        }
    }
}

private struct SeatSyncView: View {
    let sync: SeatReport.SyncState
    let hideNames: Bool

    private func lastSync(_ s: SeatReport.SyncRead) -> String {
        let parts: [String?] = [s.lastRun, s.phase.map { "phase \($0)" }, s.paused == true ? "paused" : nil]
        let head: String = "Last sync: " + parts.compactMap { $0 }.joined(separator: ", ")
        return head + (s.applySteps > 0 ? ", \(s.applySteps) apply steps seen." : ".")
    }

    var body: some View {
        switch sync {
        case .none: SeatCaption("No .seat/state.json.")
        case .unknownSchema: SeatCaption(".seat/state.json is in an unknown schema: Sanduhr doesn't read it.", color: .orange)
        case .read(let s):
            VStack(alignment: .leading, spacing: 2) {
                SeatCaption(lastSync(s))
                if hideNames {
                    if !s.report.isEmpty { SeatCaption("Its last report has \(s.report.count) lines.") }
                } else {
                    ForEach(Array(s.report.prefix(8).enumerated()), id: \.offset) { Text($0.element).font(.caption2.monospaced()).foregroundStyle(.secondary) }
                }
            }
        }
    }
}

// MARK: Drift

struct SeatDriftSection: View {
    let seat: Seat
    let report: SeatReport
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SeatCaption("Read-only. Across the wall (a folder marked work and any other), names and \"differs\" only, never values.")
            ForEach(model.shownDrift(report, demo: demo), id: \.other) { d in
                DriftBlock(drift: d, otherLabel: model.label(of: d.other),
                           values: SeatWall.showsValues(between: seat, and: other(d.other)))
            }
            SeatRoutesView(seat: seat, routes: report.routes, model: model, demo: demo)
        }
    }

    private func other(_ path: String) -> Seat {
        model.seats.first { $0.path == path } ?? seat
    }
}

private struct DriftBlock: View {
    let drift: SeatReport.Drift
    let otherLabel: String
    let values: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            SeatSubHeading("Against \(otherLabel)")
            if drift.settingsUnreadable {
                SeatCaption("A settings.json on one side is unreadable, so keys aren't compared.", color: .orange)
            } else if drift.count == 0 {
                SeatCaption("No differences.")
            }
            ForEach(drift.keys, id: \.key) { k in
                SeatCaption(values ? "\(k.key): \(k.mine ?? "not set") here, \(k.theirs ?? "not set") there" : "\(k.key): differs")
            }
            if !drift.onlyHere.isEmpty { SeatCaption("Only here: " + drift.onlyHere.joined(separator: ", ")) }
            if !drift.onlyThere.isEmpty { SeatCaption("Only there: " + drift.onlyThere.joined(separator: ", ")) }
        }
    }
}

/// Routing: which of this seat's projects name a seat in `.vscode/settings.json`, and a red
/// flag when a work seat's project routes to a seat that isn't work.
private struct SeatRoutesView: View {
    let seat: Seat
    let routes: [SeatReport.Route]
    var model: ModsPageModel
    let demo: Bool

    var body: some View {
        if !routes.isEmpty {
            SeatSubHeading("Routing (.vscode/settings.json)")
            let crossing = routes.filter { seat.isWork && !model.isWork($0.target) }
            SeatCaption("\(routes.count) project\(routes.count == 1 ? "" : "s") name a folder: \(routes.filter { $0.target == seat.path }.count) this one.")
            if !crossing.isEmpty {
                SeatCaption("\(crossing.count) of this folder's projects route to a folder not marked work.", color: Color.hex("f87171"))
            }
            if !seat.isWork {
                // A project routed into a work seat is work's: counted, never named; in demo
                // mode not even the work seat's label shows.
                let toWork = routes.filter { model.isWork($0.target) }
                ForEach(routes.filter { !model.isWork($0.target) }, id: \.project) { r in
                    SeatCaption("\((r.project as NSString).lastPathComponent) → \(model.label(of: r.target))")
                }
                if !toWork.isEmpty, !demo {
                    SeatCaption("\(toWork.count) project\(toWork.count == 1 ? "" : "s") route\(toWork.count == 1 ? "s" : "") to a folder marked work.")
                }
            }
        }
    }
}
