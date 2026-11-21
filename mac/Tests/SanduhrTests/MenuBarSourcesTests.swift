import Foundation
import Testing
@testable import Sanduhr

/// The menu bar with more than one source (agent sources, item 7): the reading follows Primary under
/// the same MenuBarMode, a stale Codex primary says "as of earlier" in the tooltip, and the tooltip
/// names the other source when it is further out (Windows' TrayReadout clause, "Claude week 40%.
/// Codex week 70%."). With only Claude on, the text is today's and there is no tooltip.
@Suite("Menu bar sources")
struct MenuBarSourcesTests {
    private func usage(_ tiers: [Tier: Double]) -> UsageResponse {
        var u = UsageResponse()
        for (tier, util) in tiers { u.tiers[tier] = TierUsage(utilization: util, resetsAt: nil) }
        return u
    }

    private func display(
        primary: AgentSource = .claude,
        claude: UsageResponse? = nil, claudeOn: Bool = true,
        codex: UsageResponse? = nil, codexOn: Bool = false, codexFresh: Set<Tier> = [.fiveHour, .sevenDay],
        mode: MenuBarMode = .higher, step: Int = 0
    ) -> MenuBarDisplay {
        MenuBarText.display(primary: primary, claude: claude, claudeOn: claudeOn,
                            codex: codex, codexOn: codexOn, codexFresh: codexFresh, mode: mode, step: step)
    }

    // MARK: Claude only is today's menu bar

    @Test func claudeOnlyTextIsUnchangedInEveryMode() {
        let u = usage([.fiveHour: 12, .sevenDay: 96.7])
        for mode in MenuBarMode.allCases {
            for step in 0..<3 {
                let shown = display(claude: u, mode: mode, step: step)
                #expect(shown.reading == MenuBarText.reading(u, mode: mode, step: step))
                #expect(shown.tooltip == nil)
            }
        }
    }

    @Test func codexNumbersHeldWhileCodexIsOffChangeNothing() {
        let u = usage([.fiveHour: 12, .sevenDay: 30])
        let held = usage([.fiveHour: 90, .sevenDay: 95])
        for mode in MenuBarMode.allCases {
            let shown = display(claude: u, codex: held, codexOn: false, mode: mode)
            #expect(shown.reading == MenuBarText.reading(u, mode: mode, step: 0))
            #expect(shown.tooltip == nil)
        }
    }

    @Test func claudeUsageOffIsTheHourglassAlone() {
        let u = usage([.fiveHour: 12])
        #expect(display(claude: u, claudeOn: false).reading == nil)
        #expect(display(claude: u, claudeOn: false).tooltip == nil)
    }

    // MARK: the reading follows Primary

    @Test func codexPrimaryReadsCodexsNumbersInEveryMode() {
        let c = usage([.fiveHour: 20, .sevenDay: 61.9])
        let claude = usage([.fiveHour: 5, .sevenDay: 5])
        let text: (MenuBarMode, Int) -> String? = { mode, step in
            display(primary: .codex, claude: claude, codex: c, codexOn: true, mode: mode, step: step).reading?.text
        }
        #expect(text(.session, 0) == "20%")
        #expect(text(.weekly, 0) == "61%")
        #expect(text(.higher, 0) == "61%")
        #expect(text(.rotate, 0) == "S 20%")
        #expect(text(.rotate, 1) == "W 61%")
    }

    @Test func claudePrimaryReadsClaudeEvenWithCodexOn() {
        let claude = usage([.fiveHour: 10, .sevenDay: 30])
        let c = usage([.fiveHour: 80, .sevenDay: 90])
        let shown = display(primary: .claude, claude: claude, codex: c, codexOn: true, mode: .weekly)
        #expect(shown.reading?.text == "30%")
        #expect(shown.reading?.tier == .sevenDay)
    }

    @Test func codexPrimaryWithNoReadingIsTheHourglassAlone() {
        let claude = usage([.fiveHour: 10])
        let shown = display(primary: .codex, claude: claude, codex: nil, codexOn: true)
        #expect(shown.reading == nil)
        #expect(shown.tooltip == nil)
    }

    // MARK: the tooltip

    @Test func tooltipNamesTheOtherSourceWhenItIsFurtherOut() {
        let claude = usage([.fiveHour: 10, .sevenDay: 40])
        let c = usage([.fiveHour: 15, .sevenDay: 70])
        #expect(display(claude: claude, codex: c, codexOn: true, mode: .weekly).tooltip == "Claude week 40%. Codex week 70%.")
        #expect(display(claude: claude, codex: c, codexOn: true, mode: .session).tooltip == "Claude 5h 10%. Codex 5h 15%.")
        #expect(display(claude: claude, codex: c, codexOn: true, mode: .rotate, step: 1).tooltip == "Claude week 40%. Codex week 70%.")
    }

    @Test func tooltipFollowsTheReadingsWindowUnderHigher() {
        // Higher picks the weekly (40 over 10), so the clause compares the weekly windows.
        let claude = usage([.fiveHour: 10, .sevenDay: 40])
        let c = usage([.fiveHour: 99, .sevenDay: 41])
        #expect(display(claude: claude, codex: c, codexOn: true).tooltip == "Claude week 40%. Codex week 41%.")
    }

    @Test func noTooltipWhenTheOtherSourceIsNotFurtherOut() {
        let claude = usage([.fiveHour: 10, .sevenDay: 70])
        let c = usage([.fiveHour: 15, .sevenDay: 40])
        #expect(display(claude: claude, codex: c, codexOn: true, mode: .weekly).tooltip == nil)
        // Equal does not count as further out.
        #expect(display(claude: claude, codex: usage([.sevenDay: 70]), codexOn: true, mode: .weekly).tooltip == nil)
    }

    @Test func aStaleOtherSourceIsNeverNamed() {
        let claude = usage([.sevenDay: 40])
        let c = usage([.sevenDay: 90])
        #expect(display(claude: claude, codex: c, codexOn: true, codexFresh: [], mode: .weekly).tooltip == nil)
        #expect(display(claude: claude, codex: c, codexOn: true, codexFresh: [.fiveHour], mode: .weekly).tooltip == nil)
    }

    @Test func codexPrimaryNamesClaudeWhenClaudeIsFurtherOut() {
        let claude = usage([.sevenDay: 80])
        let c = usage([.sevenDay: 30])
        let shown = display(primary: .codex, claude: claude, codex: c, codexOn: true, mode: .weekly)
        #expect(shown.reading?.text == "30%")
        #expect(shown.tooltip == "Codex week 30%. Claude week 80%.")
    }

    @Test func aStaleCodexPrimarySaysAsOfEarlier() {
        let claude = usage([.sevenDay: 10])
        let c = usage([.fiveHour: 20, .sevenDay: 70])
        let stale = display(primary: .codex, claude: claude, codex: c, codexOn: true, codexFresh: [], mode: .weekly)
        #expect(stale.reading?.text == "70%")   // the text stays a number
        #expect(stale.tooltip == "Codex week 70%, as of earlier.")
        // Fresh in the other window only: the weekly reading is still old.
        let oneWindow = display(primary: .codex, claude: claude, codex: c, codexOn: true, codexFresh: [.fiveHour], mode: .weekly)
        #expect(oneWindow.tooltip == "Codex week 70%, as of earlier.")
        // Fresh in its own window: no tooltip.
        let fresh = display(primary: .codex, claude: claude, codex: c, codexOn: true, codexFresh: [.sevenDay], mode: .weekly)
        #expect(fresh.tooltip == nil)
    }

    @Test func aStaleCodexPrimaryThatClaudeOutrunsSaysBoth() {
        let claude = usage([.sevenDay: 90])
        let c = usage([.sevenDay: 70])
        let shown = display(primary: .codex, claude: claude, codex: c, codexOn: true, codexFresh: [], mode: .weekly)
        #expect(shown.tooltip == "Codex week 70%, as of earlier. Claude week 90%.")
    }

    @Test func claudeWithUsageOffIsNeverNamed() {
        let claude = usage([.sevenDay: 95])
        let c = usage([.sevenDay: 40])
        let shown = display(primary: .codex, claude: claude, claudeOn: false, codex: c, codexOn: true, mode: .weekly)
        #expect(shown.reading?.text == "40%")
        #expect(shown.tooltip == nil)
    }
}
