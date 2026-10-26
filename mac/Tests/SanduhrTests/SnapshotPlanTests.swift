import Foundation
import Testing
@testable import Sanduhr

/// Item 2: the active plan reaches snapshot.json's `plan` key. Temp files and made-up data only.
private func tempFile() -> URL {
    FileManager.default.temporaryDirectory
        .appendingPathComponent("sanduhr-plan-\(UUID().uuidString)", isDirectory: true)
        .appendingPathComponent("snapshot.json")
}

private func usage() -> UsageResponse {
    UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 10, resetsAt: "2026-10-11T18:00:00Z")], extraUsage: nil)
}

private func root(_ url: URL) throws -> [String: Any] {
    try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
}

@Suite("Snapshot plan key")
struct SnapshotPlanTests {
    @Test("writeOk writes the plan name")
    func okWritesName() throws {
        let url = tempFile()
        SnapshotWriter.writeOk(usage(), accountRef: "abcd1234", plan: "Max \u{00D7}20", to: url)
        #expect(try root(url)["plan"] as? String == "Max \u{00D7}20")
    }

    @Test("a nil plan writes null")
    func nilWritesNull() throws {
        let url = tempFile()
        SnapshotWriter.writeOk(usage(), accountRef: "abcd1234", to: url)
        #expect(try root(url)["plan"] is NSNull)
    }

    @Test("writeError carries the plan, signed out writes null")
    func errorAndSignedOut() throws {
        let url = tempFile()
        SnapshotWriter.writeError("network", accountRef: "abcd1234", plan: "Pro", to: url)
        #expect(try root(url)["plan"] as? String == "Pro")
        SnapshotWriter.writeSignedOut(accountRef: "abcd1234", to: url)
        #expect(try root(url)["plan"] is NSNull)
    }

    @Test("the plan comes from the one tracked org the uuid comes from")
    func planFromTrackedOrg() {
        let api = Organization(uuid: "o-api", capabilities: ["api"], rateLimitTier: nil, billingType: "prepaid")
        let max = Organization(uuid: "o-max", capabilities: ["chat", "claude_max"],
                               rateLimitTier: "default_claude_max_20x", billingType: "stripe_subscription")
        let tracked = Organization.tracked(in: [api, max])
        #expect(tracked?.uuid == "o-max")
        #expect(tracked.flatMap(PlanLabel.resolve)?.name == "Max \u{00D7}20")
    }

    @Test("a seat file takes the plan passed for it")
    func seatFile() throws {
        let url = tempFile()
        SnapshotWriter.writeOk(usage(), accountRef: "abcd1234", plan: "Team", now: Date(timeIntervalSince1970: 0), to: url)
        #expect(try root(url)["plan"] as? String == "Team")
    }
}
