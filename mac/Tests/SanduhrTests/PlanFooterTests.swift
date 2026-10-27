import Testing
import Foundation
@testable import Sanduhr

@Suite("Plan footer capsule")
struct PlanFooterTests {
    private let pro = PlanBadge(name: "Pro", riffs: [])

    @Test func capsuleVisibility() {
        #expect(FooterView.showsPlan(plan: pro, hasNumbers: true, needsSignIn: false))
        #expect(!FooterView.showsPlan(plan: nil, hasNumbers: true, needsSignIn: false))
        #expect(!FooterView.showsPlan(plan: pro, hasNumbers: true, needsSignIn: true))
        #expect(!FooterView.showsPlan(plan: pro, hasNumbers: false, needsSignIn: false))
    }

    @Test func tooltipIsTheNameWithNoRiffs() {
        #expect(PlanRiffs.tooltip(name: "Pro", riffs: [], index: 0) == "Pro")
        #expect(PlanRiffs.next(after: 0, count: 0) == 0)
        #expect(!PlanRiffs.rotates(riffCount: 0, reduceMotion: false))
    }

    @Test func oneRiffDoesNotRotate() {
        #expect(!PlanRiffs.rotates(riffCount: 1, reduceMotion: false))
        #expect(PlanRiffs.next(after: 0, count: 1) == 0)
        #expect(PlanRiffs.tooltip(name: "X", riffs: ["a"], index: 5) == "a")
    }

    @Test func threeRiffsAdvanceAndWrap() {
        let riffs = ["a", "b", "c"]
        #expect(PlanRiffs.rotates(riffCount: 3, reduceMotion: false))
        #expect(PlanRiffs.next(after: 0, count: 3) == 1)
        #expect(PlanRiffs.next(after: 2, count: 3) == 0)
        #expect(PlanRiffs.tooltip(name: "X", riffs: riffs, index: 4) == "b")
        #expect(PlanRiffs.index(atTick: 7, count: 3, reduceMotion: false) == 1)
        #expect(PlanRiffs.interval == 4)
    }

    @Test func reduceMotionPinsTheFirstRiff() {
        #expect(!PlanRiffs.rotates(riffCount: 3, reduceMotion: true))
        #expect(PlanRiffs.next(after: 1, count: 3, reduceMotion: true) == 0)
        #expect(PlanRiffs.index(atTick: 5, count: 3, reduceMotion: true) == 0)
    }

    @Test func namesResolveBackToBadges() {
        #expect(PlanLabel.badge(named: "Max \u{00D7}20")?.riffs.count == 4)
        #expect(PlanLabel.badge(named: "Max")?.riffs.count == 3)
        #expect(PlanLabel.badge(named: "Pro")?.name == "Pro")
        #expect(PlanLabel.badge(named: "Team")?.riffs.isEmpty == true)
        #expect(PlanLabel.badge(named: nil) == nil)
        #expect(PlanLabel.badge(named: "Enterprise") == nil)
    }

    @Test func demoPlansResolveToBadges() {
        for account in DemoMode.accounts {
            let badge = PlanLabel.badge(named: DemoMode.plan(for: account))
            #expect(badge != nil)
            #expect(badge?.name == DemoMode.plan(for: account))
        }
        #expect(PlanLabel.badge(named: DemoMode.plan(for: "Work"))?.riffs.isEmpty == false)
    }

    @Test func stateYamlCarriesPlanName() {
        var s = DebugStateInput()
        s.plan = "Max \u{00D7}20"
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("plan: \"Max \u{00D7}20\"\n") || yaml.contains("plan: Max \u{00D7}20\n"))
        #expect(!yaml.contains("Plaid"))
    }
}
