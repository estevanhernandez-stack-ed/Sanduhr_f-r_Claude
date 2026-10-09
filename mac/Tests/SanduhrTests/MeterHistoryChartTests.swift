import Foundation
import Testing
@testable import Sanduhr

/// Item 37: Claude Usage, Meters. The charts' rows from the history files, the overlay's colors,
/// and the CSV export, byte for byte as Windows' `CsvExport` writes it.
@Suite("Meter history chart and CSV")
struct MeterHistoryChartTests {
    let now = HistoryStore.date("2026-10-10T12:00:00Z")!

    func p(_ t: String, _ v: Double) -> HistoryStore.Point { .init(t: t, v: v) }

    var personal: HistoryStore.History {
        ["five_hour": [p("2026-09-01T00:00:00Z", 90),            // older than a month
                       p("2026-10-05T10:00:00Z", 20), p("2026-10-10T11:00:00Z", 42)],
         "seven_day": [p("2026-10-09T08:00:00Z", 61.5)],
         "iguana_necktie": [p("2026-10-01T00:00:00Z", 100)]]
    }

    var work: HistoryStore.History {
        ["five_hour": [p("2026-10-10T11:00:00.123456+00:00", 7)],    // Windows' timestamp form
         "future_limit": [p("2026-10-10T09:00:00Z", 12)]]
    }

    @Test func oneAccountWeekAndMonth() {
        let week = MeterHistoryChart.rows(histories: ["Personal": personal], accounts: ["Personal"], window: .week, now: now)
        #expect(week.map(\.tier) == ["five_hour", "seven_day"])          // the special one is older than a week
        #expect(week[0].title == "Session (5hr)")
        #expect(week[0].series.map(\.account) == ["Personal"])
        #expect(week[0].series[0].points.map(\.value) == [20, 42])
        let month = MeterHistoryChart.rows(histories: ["Personal": personal], accounts: ["Personal"], window: .month, now: now)
        #expect(month.map(\.tier) == ["five_hour", "seven_day", "iguana_necktie"])
        #expect(month[0].series[0].points.count == 2)                    // September's is out
    }

    @Test func allAccountsOverlayInRegistryOrder() {
        let h = ["Personal": personal, "Work": work, "Empty": [:]]
        let rows = MeterHistoryChart.rows(histories: h, accounts: ["Work", "Personal", "Empty"], window: .week, now: now)
        #expect(rows.map(\.tier) == ["five_hour", "seven_day", "future_limit"])  // unknown keys after Tier's
        #expect(rows[0].series.map(\.account) == ["Work", "Personal"])        // no line for an empty account
        #expect(rows[0].series[0].points.map(\.value) == [7])
        #expect(rows[2].title == "future_limit")
        #expect(rows[1].series.map(\.account) == ["Personal"])
    }

    @Test func hiddenLimitsAndClampedValues() {
        let h: [String: HistoryStore.History] = ["A": ["five_hour": [p("2026-10-10T10:00:00Z", 140)], "seven_day": [p("2026-10-10T10:00:00Z", -3)]]]
        let rows = MeterHistoryChart.rows(histories: h, accounts: ["A"], window: .week, now: now, hidden: ["seven_day"])
        #expect(rows.map(\.tier) == ["five_hour"])
        #expect(rows[0].series[0].points[0].value == 100)
        let shown = MeterHistoryChart.rows(histories: h, accounts: ["A"], window: .week, now: now)
        #expect(shown[1].series[0].points[0].value == 0)
        #expect(MeterHistoryChart.rows(histories: [:], accounts: ["A"], window: .month, now: now).isEmpty)
    }

    @Test func colorsFollowTheRegistry() {
        let accounts = ["A", "B", "C", "D", "E", "F", "G"]
        #expect(MeterHistoryChart.color(for: "A", in: accounts) == MeterHistoryChart.palette[0])
        #expect(MeterHistoryChart.color(for: "B", in: accounts) == MeterHistoryChart.palette[1])
        #expect(MeterHistoryChart.color(for: "G", in: accounts) == MeterHistoryChart.palette[0])   // wraps
        #expect(Set(MeterHistoryChart.palette).count == MeterHistoryChart.palette.count)
    }

    @Test func csvForOneAccount() {
        let built = MeterHistoryChart.csv(histories: ["Personal": personal], accounts: ["Personal"], account: "Personal")
        #expect(built.rowCount == 5)
        #expect(built.text == """
        timestamp,tier,util_pct\r
        2026-09-01T00:00:00Z,five_hour,90\r
        2026-10-01T00:00:00Z,iguana_necktie,100\r
        2026-10-05T10:00:00Z,five_hour,20\r
        2026-10-09T08:00:00Z,seven_day,61.5\r
        2026-10-10T11:00:00Z,five_hour,42\r

        """)
    }

    @Test func csvForAllAccountsAddsTheAccountColumn() {
        let built = MeterHistoryChart.csv(histories: ["Personal": personal, "Work": work], accounts: ["Work", "Personal"], account: nil)
        #expect(built.rowCount == 7)
        let lines = built.text.components(separatedBy: "\r\n")
        #expect(lines[0] == "timestamp,account,tier,util_pct")
        #expect(lines.contains("2026-10-10T11:00:00.123456+00:00,Work,five_hour,7"))
        #expect(lines.contains("2026-10-09T08:00:00Z,Personal,seven_day,61.5"))
        #expect(lines.last == "")
        // Sorted by the timestamp text.
        let stamps = lines.dropFirst().dropLast().map { $0.components(separatedBy: ",")[0] }
        #expect(stamps == stamps.sorted())
    }

    @Test func csvHeaderOnlyAndQuoting() {
        #expect(MeterHistoryChart.csv(histories: [:], accounts: ["A"], account: "A") == ("timestamp,tier,util_pct\r\n", 0))
        #expect(MeterHistoryChart.csv(histories: [:], accounts: [], account: nil).text == "timestamp,account,tier,util_pct\r\n")
        let odd = MeterHistoryChart.csv(histories: ["Team, \"A\"": ["five_hour": [p("2026-10-10T10:00:00Z", 1)]]],
                                        accounts: ["Team, \"A\""], account: nil)
        #expect(odd.text.contains("2026-10-10T10:00:00Z,\"Team, \"\"A\"\"\",five_hour,1\r\n"))
        #expect(MeterHistoryChart.escape("plain") == "plain")
        #expect(MeterHistoryChart.escape("a\nb") == "\"a\nb\"")
    }

    @Test func numbersAsWindowsWritesThem() {
        #expect(MeterHistoryChart.number(42) == "42")
        #expect(MeterHistoryChart.number(0) == "0")
        #expect(MeterHistoryChart.number(61.5) == "61.5")
        #expect(MeterHistoryChart.number(33.25) == "33.25")
    }

    @Test func exportNamesAsWindows() {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        #expect(MeterHistoryChart.exportName(account: nil, day: now, calendar: cal) == "Sanduhr-usage-all-accounts-2026-10-10.csv")
        #expect(MeterHistoryChart.exportName(account: "Work Team", day: now, calendar: cal) == "Sanduhr-usage-work-team-2026-10-10.csv")
    }

    @Test func windowsAndStateYAML() {
        #expect(MeterHistoryChart.Window.allCases.map(\.days) == [7, 30])
        #expect(MeterHistoryChart.Window.allCases.map(\.title) == ["Week", "Month"])
        let yaml = YAMLEmitter.emit(.object([("meter_history", DebugState.meterHistoryYAML(
            MeterHistoryDebug(all: true, window: "month", rows: 3, series: 5)))]))
        #expect(yaml == "meter_history:\n  all: true\n  window: month\n  rows: 3\n  series: 5\n")
    }
}
