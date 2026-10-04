import Foundation
import Testing
@testable import Sanduhr

/// Temporary limits and their return (item 42). Settings come from an in-memory store, never the
/// real defaults.
@Suite("Limit lifetime")
struct LimitLifetimeTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    let day: TimeInterval = 86_400

    func iso(_ date: Date) -> String { ISO8601DateFormatter().string(from: date) }

    func tier(_ util: Double?, resetIn: TimeInterval?) -> TierUsage {
        TierUsage(utilization: util, resetsAt: resetIn.map { iso(now.addingTimeInterval($0)) })
    }

    func temporary(_ t: Tier, resetIn: TimeInterval? = 3 * 86_400, firstSeen: Date? = nil,
                   known: Set<Tier> = LimitLifetime.knownModelLimits) -> Bool {
        LimitLifetime.isTemporary(tier: t, usage: tier(40, resetIn: resetIn), now: now,
                                  firstSeen: firstSeen, known: known)
    }

    // MARK: isTemporary

    @Test func sessionAndWeeklyAreNeverTemporary() {
        for t in [Tier.fiveHour, .sevenDay] {
            #expect(!temporary(t))
            #expect(!temporary(t, resetIn: 30 * day))
            #expect(!temporary(t, firstSeen: now.addingTimeInterval(-day), known: []))
        }
    }

    @Test func thePromoSlotIsAlwaysTemporary() {
        #expect(temporary(.iguanaNecktie))
        #expect(temporary(.iguanaNecktie, resetIn: nil))
        #expect(temporary(.iguanaNecktie, firstSeen: now.addingTimeInterval(-100 * day)))
        #expect(LimitLifetime.isTemporary(tier: .iguanaNecktie, usage: nil, now: now, firstSeen: nil))
    }

    @Test func aKnownModelLimitIsTemporaryOnlyWithAFarReset() {
        for t in LimitLifetime.knownModelLimits {
            #expect(!temporary(t))
            #expect(!temporary(t, resetIn: 8 * day))          // exactly 8 days: still a weekly window
            #expect(temporary(t, resetIn: 8 * day + 60))
            #expect(!temporary(t, resetIn: nil))
            // New or not, a known model limit is not temporary on its first sighting alone.
            #expect(!temporary(t, firstSeen: now.addingTimeInterval(-day)))
        }
    }

    @Test func aNewUnknownLimitIsTemporaryForFourteenDays() {
        // Weekly — Design standing in for a limit the widget does not know yet.
        let unknown: Set<Tier> = LimitLifetime.knownModelLimits.subtracting([.sevenDayOmelette])
        #expect(temporary(.sevenDayOmelette, firstSeen: now, known: unknown))
        #expect(temporary(.sevenDayOmelette, firstSeen: now.addingTimeInterval(-13 * day), known: unknown))
        #expect(!temporary(.sevenDayOmelette, firstSeen: now.addingTimeInterval(-14 * day), known: unknown))
        #expect(!temporary(.sevenDayOmelette, firstSeen: nil, known: unknown))
        // Its reset still counts after the fourteen days.
        #expect(temporary(.sevenDayOmelette, resetIn: 9 * day, firstSeen: now.addingTimeInterval(-30 * day), known: unknown))
    }

    // MARK: shouldReturn

    @Test func aNewWindowBringsItBack() {
        let record = LimitLifetime.HideRecord(tier(80, resetIn: day))
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: day)) == nil)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: day + 1800)) == nil)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: day + 3600)) == nil)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: day + 3601)) == .newWindow)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: 8 * day)) == .newWindow)
        // An earlier reset is no new window.
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(80, resetIn: 0)) == nil)
    }

    @Test func aRefillBringsItBack() {
        let record = LimitLifetime.HideRecord(tier(80, resetIn: day))
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(70.5, resetIn: day)) == nil)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(70, resetIn: day)) == .refill)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(0, resetIn: day)) == .refill)
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(100, resetIn: day)) == nil)
    }

    @Test func missingNumbersDecideNothing() {
        let empty = LimitLifetime.HideRecord(resetsAt: nil, utilization: nil)
        #expect(LimitLifetime.shouldReturn(record: empty, usage: tier(0, resetIn: 30 * day)) == nil)
        let record = LimitLifetime.HideRecord(tier(80, resetIn: day))
        #expect(LimitLifetime.shouldReturn(record: record, usage: tier(nil, resetIn: nil)) == nil)
    }

    // MARK: Stored state (MeterVisibility)

    func usage(_ tiers: [Tier: TierUsage]) -> UsageResponse {
        var u = UsageResponse()
        u.tiers = tiers
        return u
    }

    @Test func firstSeenIsRecordedOnceAndNeverOverwritten() {
        let d = MemoryDefaults()
        MeterVisibility.recordFirstSeen(usage([.sevenDay: tier(1, resetIn: day)]), now: now, in: d)
        let later = now.addingTimeInterval(5 * day)
        MeterVisibility.recordFirstSeen(usage([.sevenDay: tier(1, resetIn: day), .iguanaNecktie: tier(1, resetIn: day)]),
                                        now: later, in: d)
        #expect(MeterVisibility.firstSeen(in: d) == [.sevenDay: now, .iguanaNecktie: later])
        #expect((d.values[MeterVisibility.firstSeenKey] as? [String: Date])?.keys.sorted() == ["iguana_necktie", "seven_day"])
    }

    @Test func hidingRecordsTheNumbersAndOnlyForTemporaryLimits() {
        let d = MemoryDefaults()
        let special = tier(80, resetIn: day)
        let u = usage([.iguanaNecktie: special, .sevenDaySonnet: tier(50, resetIn: 3 * day)])
        #expect(MeterVisibility.hide(.iguanaNecktie, usage: u, now: now, store: d))
        #expect(d.values["meterShow.iguana_necktie"] as? Bool == false)
        let saved = d.values["meterHidden.iguana_necktie"] as? [String: Any]
        #expect(saved?["resetsAt"] as? String == special.resetsAt)
        #expect(saved?["utilization"] as? Double == 80)
        #expect(MeterVisibility.record(.iguanaNecktie, in: d) == LimitLifetime.HideRecord(special))
        #expect(!MeterVisibility.hide(.sevenDaySonnet, usage: u, now: now, store: d))
        #expect(!MeterVisibility.hide(.fiveHour, usage: u, now: now, store: d))
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        MeterVisibility.show(.iguanaNecktie, store: d)
        #expect(d.values.isEmpty)
    }

    @Test func aHiddenLimitReturnsOnANewWindow() {
        let d = MemoryDefaults()
        MeterVisibility.hide(.iguanaNecktie, usage: usage([.iguanaNecktie: tier(95, resetIn: day)]), now: now, store: d)
        #expect(MeterVisibility.reconcile(usage([.iguanaNecktie: tier(96, resetIn: day)]), now: now, store: d).isEmpty)
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        let shown = MeterVisibility.reconcile(usage([.iguanaNecktie: tier(96, resetIn: 8 * day)]), now: now, store: d)
        #expect(shown == [.iguanaNecktie: .newWindow])
        #expect(MeterVisibility.hidden(in: d).isEmpty)
        #expect(MeterVisibility.record(.iguanaNecktie, in: d) == nil)
    }

    @Test func aHiddenLimitReturnsOnARefill() {
        let d = MemoryDefaults()
        MeterVisibility.hide(.iguanaNecktie, usage: usage([.iguanaNecktie: tier(95, resetIn: day)]), now: now, store: d)
        #expect(MeterVisibility.reconcile(usage([.iguanaNecktie: tier(86, resetIn: day)]), now: now, store: d).isEmpty)
        #expect(MeterVisibility.reconcile(usage([.iguanaNecktie: tier(85, resetIn: day)]), now: now, store: d)
            == [.iguanaNecktie: .refill])
        #expect(MeterVisibility.hidden(in: d).isEmpty)
    }

    @Test func aHiddenLimitThatIsNoLongerTemporaryShows() {
        let d = MemoryDefaults()
        // Weekly — Opus hidden while its reset was far off; now it is a normal weekly window.
        MeterVisibility.hide(.sevenDayOpus, usage: usage([.sevenDayOpus: tier(10, resetIn: 20 * day)]), now: now, store: d)
        #expect(MeterVisibility.hidden(in: d) == [.sevenDayOpus])
        let shown = MeterVisibility.reconcile(usage([.sevenDayOpus: tier(10, resetIn: 3 * day)]), now: now, store: d)
        #expect(shown == [.sevenDayOpus: .notTemporary])
        #expect(MeterVisibility.hidden(in: d).isEmpty)
        // A stored hide on the session or weekly limit is cleared too.
        d.set(false, forKey: MeterVisibility.showKey(.sevenDay))
        #expect(MeterVisibility.reconcile(usage([:]), now: now, store: d) == [.sevenDay: .notTemporary])
        #expect(d.values[MeterVisibility.showKey(.sevenDay)] == nil)
    }

    @Test func anOldHideWithoutARecordStaysAndTakesTheNextNumbers() {
        let d = MemoryDefaults()
        d.set(false, forKey: MeterVisibility.showKey(.iguanaNecktie))
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        #expect(MeterVisibility.reconcile(usage([.iguanaNecktie: tier(90, resetIn: day)]), now: now, store: d).isEmpty)
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
        #expect(MeterVisibility.record(.iguanaNecktie, in: d) == LimitLifetime.HideRecord(tier(90, resetIn: day)))
        #expect(MeterVisibility.reconcile(usage([.iguanaNecktie: tier(70, resetIn: day)]), now: now, store: d)
            == [.iguanaNecktie: .refill])
    }

    @Test func aLimitTheNumbersLeaveOutStaysHidden() {
        let d = MemoryDefaults()
        MeterVisibility.hide(.iguanaNecktie, usage: usage([.iguanaNecktie: tier(95, resetIn: day)]), now: now, store: d)
        #expect(MeterVisibility.reconcile(usage([.fiveHour: tier(5, resetIn: 3600)]), now: now, store: d).isEmpty)
        #expect(MeterVisibility.hidden(in: d) == [.iguanaNecktie])
    }

    @Test func temporaryListsOnlyTheReportedTemporaryLimits() {
        let d = MemoryDefaults()
        let u = usage([.fiveHour: tier(5, resetIn: 3600), .sevenDay: tier(5, resetIn: 3 * day),
                       .sevenDayOpus: tier(5, resetIn: 3 * day), .sevenDayCowork: tier(5, resetIn: 12 * day),
                       .iguanaNecktie: tier(5, resetIn: day)])
        #expect(MeterVisibility.temporary(u, now: now, store: d) == [.sevenDayCowork, .iguanaNecktie])
        #expect(MeterVisibility.temporary(nil, now: now, store: d).isEmpty)
    }
}
