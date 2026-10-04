import Foundation
import Testing
@testable import Sanduhr

@Suite("Organization choice")
struct OrganizationTests {
    func decode(_ json: String) throws -> [Organization] {
        try JSONDecoder().decode([Organization].self, from: Data(json.utf8))
    }

    @Test func prefersTheMaxOrgOverAnAPIOrgListedFirst() throws {
        let orgs = try decode("""
        [{"uuid": "api", "capabilities": ["api", "api_individual"]},
         {"uuid": "chat", "capabilities": ["chat"]},
         {"uuid": "max", "capabilities": ["chat", "claude_max"]}]
        """)
        #expect(Organization.tracked(in: orgs)?.uuid == "max")
    }

    @Test func fallsBackToTheFirstChatOrg() throws {
        let orgs = try decode("""
        [{"uuid": "api", "capabilities": ["api"]},
         {"uuid": "chat1", "capabilities": ["chat", "claude_pro"]},
         {"uuid": "chat2", "capabilities": ["chat"]}]
        """)
        #expect(Organization.tracked(in: orgs)?.uuid == "chat1")
    }

    @Test func fallsBackToTheFirstWhenNoCapabilitiesHelp() throws {
        let orgs = try decode("""
        [{"uuid": "a"}, {"uuid": "b", "capabilities": []}, {"uuid": "c", "capabilities": ["api"]}]
        """)
        #expect(Organization.tracked(in: orgs)?.uuid == "a")
    }

    @Test func noOrgsIsNil() throws {
        #expect(Organization.tracked(in: try decode("[]")) == nil)
    }

    @Test func extraFieldsAreIgnored() throws {
        let orgs = try decode("""
        [{"uuid": "x", "name": "n", "rate_limit_tier": "default_claude_max_5x",
          "billing_type": "stripe_subscription", "capabilities": ["chat", "claude_max"]}]
        """)
        #expect(Organization.tracked(in: orgs)?.uuid == "x")
    }
}
