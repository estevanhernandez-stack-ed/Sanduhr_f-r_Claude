import Foundation
import Testing
@testable import Sanduhr

/// The plan label, mirroring Windows' PlanLabelTests case for case, plus the Organization decode
/// that feeds it. Made-up org data only.
@Suite("Plan label")
struct PlanLabelTests {
    static let max20x = "Max \u{00D7}20"

    func decode(_ json: String) throws -> [Organization] {
        try JSONDecoder().decode([Organization].self, from: Data(json.utf8))
    }

    @Test func max20xSubscription() throws {
        let b = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_max_20x",
                                               billingType: "stripe_subscription",
                                               capabilities: ["claude_max", "chat"]))
        #expect(b.name == Self.max20x)
        #expect(b.riffs.contains("Galaxy Brain Max"))
        #expect(b.riffs.count >= 2)
    }

    @Test func max5xSubscription() throws {
        let b = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_max_5x",
                                               billingType: "stripe_subscription",
                                               capabilities: ["claude_max"]))
        #expect(b.name == "Max")
        #expect(b.riffs.contains("Maximum Effort"))
    }

    @Test func proSubscription() throws {
        let b = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_pro",
                                               billingType: "stripe_subscription",
                                               capabilities: ["claude_pro", "chat"]))
        #expect(b.name == "Pro")
        #expect(b.riffs.isEmpty)
    }

    @Test func teamSubscription() throws {
        let b = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_team",
                                               billingType: "stripe_subscription",
                                               capabilities: ["claude_team"]))
        #expect(b.name == "Team")
        #expect(b.riffs.isEmpty)
    }

    @Test func prepaidAPIOrgIsNil() {
        #expect(PlanLabel.resolve(rateLimitTier: "auto_prepaid_tier_0", billingType: "prepaid",
                                  capabilities: ["api", "api_individual"]) == nil)
    }

    @Test func unknownSubscriptionTierIsNil() {
        #expect(PlanLabel.resolve(rateLimitTier: "some_future_tier",
                                  billingType: "stripe_subscription", capabilities: ["chat"]) == nil)
    }

    @Test func missingTierIsNil() {
        #expect(PlanLabel.resolve(rateLimitTier: nil) == nil)
    }

    @Test func emptyTierIsNil() {
        #expect(PlanLabel.resolve(rateLimitTier: "") == nil)
    }

    @Test func nonSubscriptionBillingBlocksTheBadge() {
        #expect(PlanLabel.resolve(rateLimitTier: "weird_max_thing", billingType: "prepaid",
                                  capabilities: ["api"]) == nil)
    }

    @Test func lenientWhenBillingAndCapabilitiesAreAbsent() throws {
        let b = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_max_20x"))
        #expect(b.name == Self.max20x)
    }

    @Test func riffValuesAreTheSpecsInOrder() throws {
        let m20 = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_max_20x"))
        #expect(m20.riffs == ["Plaid Max", "Maximum Overdrive", "Ridiculous Speed", "Galaxy Brain Max"])
        let m5 = try #require(PlanLabel.resolve(rateLimitTier: "default_claude_max_5x"))
        #expect(m5.riffs == ["Maximum Effort", "Max Headroom", "Big Max"])
    }

    @Test func twoResolvesAreEqualValues() {
        let a = PlanLabel.resolve(rateLimitTier: "default_claude_max_20x",
                                  billingType: "stripe_subscription", capabilities: ["claude_max"])
        let b = PlanLabel.resolve(rateLimitTier: "default_claude_max_20x",
                                  billingType: "stripe_subscription", capabilities: ["claude_max"])
        #expect(a != nil)
        #expect(a == b)
    }

    @Test func twoOrgLoginGivesTheBadgeToTheSubscriptionOrgOnly() throws {
        let orgs = try decode("""
        [{"uuid": "api", "rate_limit_tier": "auto_prepaid_tier_0", "billing_type": "prepaid",
          "capabilities": ["api", "api_individual"]},
         {"uuid": "sub", "rate_limit_tier": "default_claude_max_20x",
          "billing_type": "stripe_subscription", "capabilities": ["chat", "claude_max"]}]
        """)
        #expect(PlanLabel.resolve(orgs[0]) == nil)
        #expect(PlanLabel.resolve(orgs[1])?.name == Self.max20x)
        #expect(Organization.tracked(in: orgs)?.uuid == "sub")
    }

    @Test func decodesBothTierFields() throws {
        let orgs = try decode("""
        [{"uuid": "a", "rate_limit_tier": "default_claude_pro", "billing_type": "stripe_subscription",
          "capabilities": ["chat"]}]
        """)
        #expect(orgs[0].rateLimitTier == "default_claude_pro")
        #expect(orgs[0].billingType == "stripe_subscription")
        #expect(orgs[0].capabilities == ["chat"])
    }

    @Test func tierFieldsAbsentDecodeAsNil() throws {
        let orgs = try decode(#"[{"uuid": "a", "capabilities": ["chat"]}]"#)
        #expect(orgs[0].rateLimitTier == nil)
        #expect(orgs[0].billingType == nil)
        #expect(PlanLabel.resolve(orgs[0]) == nil)
    }

    @Test func wrongTypedTierFieldsStillDecodeTheOrg() throws {
        let orgs = try decode("""
        [{"uuid": "a", "rate_limit_tier": 5, "billing_type": {"x": 1}, "capabilities": ["chat"]}]
        """)
        #expect(orgs[0].uuid == "a")
        #expect(orgs[0].capabilities == ["chat"])
        #expect(orgs[0].rateLimitTier == nil)
        #expect(orgs[0].billingType == nil)
    }

    @Test func trackedPicksTheSameOrgAsBefore() throws {
        let orgs = try decode("""
        [{"uuid": "api", "rate_limit_tier": 1, "capabilities": ["api"]},
         {"uuid": "chat", "rate_limit_tier": "default_claude_pro", "capabilities": ["chat"]},
         {"uuid": "max", "billing_type": "stripe_subscription", "capabilities": ["chat", "claude_max"]}]
        """)
        #expect(Organization.tracked(in: orgs)?.uuid == "max")
    }
}
