import Foundation

/// The Claude Usage page's reads (item 48), synchronous and off the main thread: the live
/// reader for the account's linked folder and the vault reader for its record, turned into the
/// pure models in `UsagePage.swift`. Only the folder and record the account's choices name are
/// read; Not tracked or no folder reads nothing.
enum UsagePageLoader {
    /// The Overview: the live reader for the 30-day window, and with Keep a record the record's
    /// closed days per the hot-day rule (none at all when degraded).
    static func overview(_ source: UsageSource, names: ProjectNamesChoice, live: CCLogReader?,
                         vault: VaultReader, now: Date = Date(), calendar: Calendar = .current,
                         displayName: @escaping (String) -> String = CCProjectName.displayName) -> UsageOverview? {
        guard source.folder != nil, let live else { return nil }
        let today = CCLocalDay(now, calendar: calendar)
        let start = UsageOverview.windowStart(today)
        let liveDays = live.days(from: start, calendar: calendar)
        var facts: UsageRecordFacts?
        if let id = source.recordID {
            var f = UsageRecordFacts(lastIngest: vault.lastSuccessfulIngest([id]))
            if let r = UsageOverview.closedRange(lastIngest: f.lastIngest, today: today, now: now, calendar: calendar) {
                f.window = vault.readWindow([id], from: r.from, toExclusive: r.toExclusive)
                f.covered = vault.coveredSet([id], from: r.from, through: r.toExclusive.adding(days: -1))
            }
            facts = f
        }
        var named: [String: String] = [:]
        return UsageOverview.build(today: today, now: now, calendar: calendar, live: liveDays, record: facts) { cwd in
            if let hit = named[cwd] { return hit }
            let n = UsageProjectNames.name(cwd, choice: names, displayName: displayName)
            named[cwd] = n
            return n
        }
    }

    /// Trends: `weeks` weeks ending with this one, and the top projects since the first.
    static func trends(recordID id: String, weeks: Int, vault: VaultReader, today: CCLocalDay) -> UsageTrends {
        let list = vault.readWeeks([id], weeks: weeks, today: today)
        let from = list.first?.weekStart ?? today
        let top = vault.topProjects([id], from: from, toExclusive: today.adding(days: 1), top: UsageTrends.topCount)
        return UsageTrends.build(weeks: list, top: top, birth: vault.birthDate([id]), today: today)
    }

    /// Every logical session in the record.
    static func sessions(recordID id: String, vault: VaultReader) -> [VaultSessionInfo] {
        vault.readSessions([id])
    }

    /// What the CSV's `root` column and the expansion call the record: the linked folder's name
    /// (`.claude-work`), as Windows names a root by its folder.
    static func recordTitle(folder: String) -> String {
        (folder as NSString).lastPathComponent
    }
}
