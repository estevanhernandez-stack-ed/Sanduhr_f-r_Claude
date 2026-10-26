import Foundation
import Testing
@testable import Sanduhr

/// Item 3: each account's plan name, kept per account, following renames and going with Sign Out
/// and Remove Account. In-memory defaults and made-up labels only.
@Suite("Account plans")
struct AccountPlansTests {
    @Test func aFetchedPlanIsKeptByNameForItsAccount() {
        let d = MemoryDefaults()
        AccountPlans.set("Max \u{00D7}20", for: "Work", in: d)
        AccountPlans.set("Pro", for: "Home", in: d)
        #expect(AccountPlans.plan(for: "Work", in: d) == "Max \u{00D7}20")
        #expect(AccountPlans.plan(for: "Home", in: d) == "Pro")
        #expect(AccountPlans.plan(for: "Lab", in: d) == nil)
        #expect(d.object(forKey: AccountPlans.key) as? [String: String]
                == ["Work": "Max \u{00D7}20", "Home": "Pro"])
    }

    /// The follower's `onPlan` hands the same (label, name) pair to the same store call.
    @MainActor @Test func theFollowersAnswerIsKeptTheSameWay() {
        let d = MemoryDefaults()
        let follower = AccountFollower()
        follower.onPlan = { label, name in AccountPlans.set(name, for: label, in: d) }
        follower.onPlan("Home", "Team")
        #expect(AccountPlans.plan(for: "Home", in: d) == "Team")
        follower.onPlan("Home", nil)
        #expect(AccountPlans.plan(for: "Home", in: d) == nil)
    }

    @Test func aNilPlanClearsTheStoredOne() {
        let d = MemoryDefaults()
        AccountPlans.set("Pro", for: "Work", in: d)
        AccountPlans.set(nil, for: "Work", in: d)
        #expect(AccountPlans.plan(for: "Work", in: d) == nil)
        #expect(d.object(forKey: AccountPlans.key) == nil)
        AccountPlans.set("Pro", for: "Work", in: d)
        AccountPlans.set("", for: "Work", in: d)
        #expect(d.object(forKey: AccountPlans.key) == nil)
    }

    @Test func nothingIsWrittenForANilOrUnnamedAccount() {
        let d = MemoryDefaults()
        AccountPlans.set("Pro", for: nil, in: d)
        AccountPlans.set("Pro", for: "", in: d)
        #expect(d.object(forKey: AccountPlans.key) == nil)
        #expect(AccountPlans.plan(for: nil, in: d) == nil)
    }

    @Test func renameMovesIt() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "k1"], labels: ["Work"], active: "Work")
        AccountPlans.set("Max", for: "Work", in: f.defaults)
        try? f.registry.rename("Work", to: "Client")
        #expect(AccountPlans.plan(for: "Client", in: f.defaults) == "Max")
        #expect(AccountPlans.plan(for: "Work", in: f.defaults) == nil)
    }

    @Test func removeAndSignOutClearIt() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "k1", "sessionKey:Home": "k2"],
                                labels: ["Work", "Home"], active: "Home")
        AccountPlans.set("Max", for: "Work", in: f.defaults)
        AccountPlans.set("Pro", for: "Home", in: f.defaults)
        f.registry.signOut("Home")
        #expect(AccountPlans.plan(for: "Home", in: f.defaults) == nil)
        #expect(AccountPlans.plan(for: "Work", in: f.defaults) == "Max")
        f.registry.remove("Work")
        #expect(AccountPlans.plan(for: "Work", in: f.defaults) == nil)
        #expect(f.defaults.object(forKey: AccountPlans.key) == nil)
    }

    @Test func signingOutTheLegacySlotsTouchesNoAccountsPlan() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "k1"], labels: ["Work"], active: "Work")
        AccountPlans.set("Max", for: "Work", in: f.defaults)
        f.registry.signOut(nil)
        #expect(AccountPlans.plan(for: "Work", in: f.defaults) == "Max")
    }

    @Test func rowTextShowsNothingWhenUnknownAndSpeaksThePlanWhenKnown() {
        #expect(AccountPlans.caption(nil) == nil)
        #expect(AccountPlans.caption("") == nil)
        #expect(AccountPlans.caption("Pro") == "Pro")
        #expect(AccountPlans.spokenLabel("Work", plan: nil) == "Work")
        #expect(AccountPlans.spokenLabel("Work", plan: "Max \u{00D7}20") == "Work, Max \u{00D7}20")
    }

    @Test func demoRowsCarryTheSamplePlansAndNeverAStoredOne() {
        #expect(DemoMode.plan(for: "Work") == "Max \u{00D7}20")
        #expect(DemoMode.plan(for: "Personal") == "Pro")
        #expect(DemoMode.plan(for: "Anything else") == nil)
    }
}
