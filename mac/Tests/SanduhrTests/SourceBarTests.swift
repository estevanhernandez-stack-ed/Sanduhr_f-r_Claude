import Foundation
import Testing
@testable import Sanduhr

/// The two-tone bar (agent sources, item 5): the pure layout, ported from Windows' `SourceBar`
/// (SourceBarTests.cs), plus the marks' names and the geometry `ProgressBarView` draws from. No
/// view, no defaults, no files.

private func P(_ s: AgentSource, _ p: Double, stale: Bool = false, asOf: String? = nil) -> SourceBarPoint {
    SourceBarPoint(source: s, percent: p, stale: stale, asOf: asOf)
}

private func layout(_ primary: AgentSource, _ points: [SourceBarPoint],
                    _ window: SourceBarWindow = .weekly) -> SourceBarLayout {
    SourceBar.layout(primary: primary, window: window, points: points)
}

@Suite("Source bar: segments and ticks")
struct SourceBarLayoutTests {
    @Test func primaryFillsAndASourceFurtherOutContinuesTheBar() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 70)])
        #expect(l.primary?.source == .claude)
        #expect(l.primary?.from == 0)
        #expect(l.primary?.to == 0.40)
        #expect(l.beyond?.source == .codex)
        #expect(l.beyond?.from == 0.40)
        #expect(l.beyond?.to == 0.70)
        #expect(l.beyond?.colorHex == AgentSource.codex.defaultColor)
        #expect(l.ticks.map(\.source) == [.codex])
        #expect(l.ticks.first?.at == 0.70)
    }

    @Test func aSourceBehindThePrimaryIsOnlyATick() {
        let l = layout(.claude, [P(.claude, 80), P(.codex, 30)])
        #expect(l.beyond == nil)
        #expect(l.ticks.map(\.source) == [.codex])
        #expect(l.ticks.first?.at == 0.30)
    }

    @Test func equalPercentsDrawNoBeyondSegment() {
        let l = layout(.claude, [P(.claude, 50), P(.codex, 50)])
        #expect(l.beyond == nil)
        #expect(l.ticks.map(\.source) == [.codex])
    }

    @Test func aMissingPrimaryDrawsTheHighestAsTheFill() {
        let l = layout(.claude, [P(.codex, 25)])
        #expect(l.primary?.source == .codex)
        #expect(l.ticks.isEmpty)
        #expect(l.isSingleFill)
    }

    @Test func aMissingPrimaryAmongTwoPicksTheHigherAndTheEarlierOnATie() {
        let l = layout(.claude, [P(.antigravity, 60), P(.codex, 30)])
        #expect(l.primary?.source == .antigravity)
        #expect(l.beyond == nil)
        #expect(l.ticks.map(\.source) == [.codex])
        let tie = layout(.claude, [P(.antigravity, 60), P(.codex, 60)])
        #expect(tie.primary?.source == .codex)
    }

    @Test func percentsClampAndStalenessCarries() {
        let l = layout(.codex, [P(.codex, 120, stale: true), P(.claude, -5)])
        #expect(l.primary?.to == 1)
        #expect(l.primary?.percent == 100)
        #expect(l.primary?.stale == true)
        #expect(l.ticks.map(\.source) == [.claude])
        #expect(l.ticks.first?.at == 0)
        #expect(l.ticks.first?.stale == false)
    }

    @Test func notANumberReadsAsZero() {
        let l = layout(.claude, [P(.claude, .nan), P(.codex, 10)])
        #expect(l.primary?.to == 0)
        #expect(l.beyond?.to == 0.10)
    }

    @Test func noSourcesIsAnEmptyBar() {
        let l = layout(.claude, [])
        #expect(l.primary == nil)
        #expect(l.beyond == nil)
        #expect(l.ticks.isEmpty)
        #expect(l.isSingleFill)
    }

    @Test func aTickNeverDrawnForThePrimaryItself() {
        let l = layout(.codex, [P(.claude, 10), P(.codex, 90)])
        #expect(l.ticks.map(\.source) == [.claude])
    }

    @Test func colorsComeFromThePointsOrTheDefaults() {
        let custom = SourceBarPoint(source: .codex, percent: 70, colorHex: "#112233")
        let l = layout(.claude, [P(.claude, 40), custom])
        #expect(l.beyond?.colorHex == "#112233")
        #expect(l.ticks.first?.colorHex == "#112233")
        #expect(l.primary?.colorHex == AgentSource.claude.defaultColor)
    }
}

@Suite("Source bar: every ordering")
struct SourceBarOrderingTests {
    private func permutations<T>(_ items: [T]) -> [[T]] {
        guard items.count > 1 else { return [items] }
        return items.indices.flatMap { i -> [[T]] in
            var rest = items
            let head = rest.remove(at: i)
            return permutations(rest).map { [head] + $0 }
        }
    }

    @Test func twoSourcesInEveryOrderAndInEveryPrimary() {
        // Claude 30, Codex 60: whichever way the input is ordered, the answer is the same.
        for input in permutations([P(.claude, 30), P(.codex, 60)]) {
            let c = layout(.claude, input)
            #expect(c.primary?.source == .claude)
            #expect(c.beyond?.source == .codex)
            #expect(c.beyond?.from == 0.30)
            #expect(c.beyond?.to == 0.60)
            #expect(c.ticks.map(\.source) == [.codex])

            let x = layout(.codex, input)
            #expect(x.primary?.source == .codex)
            #expect(x.primary?.to == 0.60)
            #expect(x.beyond == nil)
            #expect(x.ticks.map(\.source) == [.claude])
        }
    }

    @Test func threeSourcesBeyondIsTheFurthestAndEveryOtherIsATick() {
        let points = [P(.claude, 20), P(.codex, 50), P(.antigravity, 80)]
        for input in permutations(points) {
            let l = layout(.claude, input)
            #expect(l.primary?.to == 0.20)
            #expect(l.beyond?.source == .antigravity)
            #expect(l.beyond?.from == 0.20)
            #expect(l.beyond?.to == 0.80)
            // Ticks come in the fixed source order, whatever the input order.
            #expect(l.ticks.map(\.source) == [.codex, .antigravity])
        }
    }

    @Test func threeSourcesEveryPrimary() {
        let points = [P(.claude, 20), P(.codex, 50), P(.antigravity, 80)]
        for input in permutations(points) {
            let codex = layout(.codex, input)
            #expect(codex.primary?.to == 0.50)
            #expect(codex.beyond?.source == .antigravity)
            #expect(codex.ticks.map(\.source) == [.claude, .antigravity])

            let agy = layout(.antigravity, input)
            #expect(agy.primary?.to == 0.80)
            #expect(agy.beyond == nil)
            #expect(agy.ticks.map(\.source) == [.claude, .codex])
        }
    }

    @Test func threeSourcesTiedForFurthestTheEarlierSourceContinuesTheBar() {
        let points = [P(.claude, 10), P(.codex, 60), P(.antigravity, 60)]
        for input in permutations(points) {
            let l = layout(.claude, input)
            #expect(l.beyond?.source == .codex)
            #expect(l.ticks.map(\.source) == [.codex, .antigravity])
        }
    }

    @Test func threeSourcesAllEqualDrawOnlyTheFillAndTwoTicks() {
        let l = layout(.codex, [P(.claude, 40), P(.codex, 40), P(.antigravity, 40)])
        #expect(l.beyond == nil)
        #expect(l.ticks.map(\.source) == [.claude, .antigravity])
    }

    @Test func aSourceBehindThePrimaryNeverContinuesTheBarEvenWithAFurtherOne() {
        // Claude 50 primary, Codex 30 behind, Antigravity 70 beyond: Codex is a tick only.
        let l = layout(.claude, [P(.claude, 50), P(.codex, 30), P(.antigravity, 70)])
        #expect(l.beyond?.source == .antigravity)
        #expect(l.ticks.map(\.source) == [.codex, .antigravity])
    }
}

@Suite("Source bar: edges and one source")
struct SourceBarEdgeTests {
    @Test func zeroAndHundred() {
        let zero = layout(.claude, [P(.claude, 0), P(.codex, 0)])
        #expect(zero.primary?.to == 0)
        #expect(zero.beyond == nil)
        #expect(zero.ticks.first?.at == 0)

        let full = layout(.claude, [P(.claude, 100), P(.codex, 100)])
        #expect(full.primary?.to == 1)
        #expect(full.beyond == nil)
        #expect(full.ticks.first?.at == 1)

        let up = layout(.claude, [P(.claude, 0), P(.codex, 100)])
        #expect(up.beyond?.from == 0)
        #expect(up.beyond?.to == 1)
    }

    @Test func oneSourceIsTodaysSingleFillWithNoTicks() {
        let l = layout(.claude, [P(.claude, 42)])
        #expect(l.isSingleFill)
        #expect(l.primary?.source == .claude)
        #expect(l.primary?.from == 0)
        #expect(l.primary?.to == 0.42)
        #expect(l.beyond == nil)
        #expect(l.ticks.isEmpty)
        #expect(l.segments.count == 1)
    }

    @Test func twoSourcesAreNotASingleFill() {
        #expect(!layout(.claude, [P(.claude, 42), P(.codex, 10)]).isSingleFill)
    }

    @Test func aStaleSourceDimsAndAFreshOneDoesNot() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 70, stale: true)])
        #expect(l.beyond?.stale == true)
        #expect(l.ticks.first?.stale == true)
        #expect(l.primary?.stale == false)
        #expect(l.beyond?.opacity == SourceBar.staleOpacity)
        #expect(l.ticks.first?.opacity == SourceBar.staleOpacity)
        #expect(l.primary?.opacity == 1)
        #expect(SourceBar.staleOpacity == 0.55)
    }

    @Test func aStalePrimaryDimsTheFill() {
        let l = layout(.codex, [P(.claude, 10), P(.codex, 30, stale: true)])
        #expect(l.primary?.opacity == SourceBar.staleOpacity)
        #expect(l.ticks.first?.opacity == 1)
    }
}

@Suite("Source bar: names")
struct SourceBarNameTests {
    @Test func marksReadSourceWindowAndPercent() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 62)], .weekly)
        #expect(l.primary?.name == "Claude weekly, 40%.")
        #expect(l.beyond?.name == "Codex weekly, 62%.")
        #expect(l.ticks.first?.name == "Codex weekly, 62%.")
    }

    @Test func theFiveHourWindowSaysSessionAsWindowsDoes() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 62)], .fiveHour)
        #expect(l.primary?.name == "Claude session, 40%.")
        #expect(l.ticks.first?.name == "Codex session, 62%.")
    }

    @Test func aStaleMarkSaysWhenItWasReadOrEarlier() {
        let withTime = layout(.claude, [P(.claude, 40), P(.codex, 62, stale: true, asOf: "3:42 PM")])
        #expect(withTime.ticks.first?.name == "Codex weekly, 62%. As of 3:42 PM.")
        #expect(withTime.beyond?.name == "Codex weekly, 62%. As of 3:42 PM.")
        let without = layout(.claude, [P(.claude, 40), P(.codex, 62, stale: true)])
        #expect(without.ticks.first?.name == "Codex weekly, 62%. As of earlier.")
        // A fresh source never says it, even with a time on hand.
        let fresh = layout(.claude, [P(.claude, 40), P(.codex, 62, asOf: "3:42 PM")])
        #expect(fresh.ticks.first?.name == "Codex weekly, 62%.")
    }

    @Test func percentsTruncateLikeTheVisibleNumbersAndClamp() {
        let l = layout(.claude, [P(.claude, 40.9), P(.codex, 70.6)])
        #expect(l.primary?.name == "Claude weekly, 40%.")
        #expect(l.ticks.first?.name == "Codex weekly, 70%.")
        // 0.29 * 100 is 28.999999999999996: float noise must not cost a point.
        let noise = layout(.claude, [P(.claude, 0.29 * 100), P(.codex, 140)])
        #expect(noise.primary?.name == "Claude weekly, 29%.")
        #expect(noise.ticks.first?.name == "Codex weekly, 100%.")
    }

    @Test func theWholeBarListsEverySourceInAFixedOrder() {
        let l = layout(.claude, [P(.codex, 70, stale: true), P(.claude, 40)], .weekly)
        #expect(l.accessibleName == "Weekly. Claude 40%. Codex 70%, as of earlier.")
        let five = layout(.claude, [P(.claude, 40), P(.codex, 70)], .fiveHour)
        #expect(five.accessibleName == "Session (5hr). Claude 40%. Codex 70%.")
    }

    @Test func theWindowLabelsMatchTheCards() {
        #expect(SourceBarWindow.fiveHour.label == Tier.fiveHour.label)
        #expect(SourceBarWindow.weekly.label == "Weekly")
    }

    @Test func nothingInANameCarriesAnAccountOrAProject() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 62, stale: true, asOf: "3:42 PM")])
        for name in l.markNames + [l.accessibleName] {
            #expect(!name.contains("@"))
            #expect(!name.contains("/"))
        }
    }
}

@Suite("Source bar: geometry")
struct SourceBarGeometryTests {
    @Test func segmentsSpanFromToInTheBarsWidth() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 70)])
        let primary = SourceBar.span(l.primary!, in: 200)
        #expect(primary.x == 0)
        #expect(abs(primary.width - 80) < 1e-9)
        let beyond = SourceBar.span(l.beyond!, in: 200)
        #expect(abs(beyond.x - 80) < 1e-9)
        #expect(abs(beyond.width - 60) < 1e-9)
    }

    @Test func aZeroPrimaryKeepsTodaysSliver() {
        let l = layout(.claude, [P(.claude, 0), P(.codex, 0)])
        let primary = SourceBar.span(l.primary!, in: 200)
        #expect(abs(primary.width - 0.008 * 200) < 1e-9)
    }

    @Test func ticksAreTwoWideCenteredAndKeptInsideTheBar() {
        #expect(SourceBar.tickWidth == 2)
        #expect(SourceBar.tickX(at: 0.5, in: 200) == 99)
        #expect(SourceBar.tickX(at: 0, in: 200) == 0)
        #expect(SourceBar.tickX(at: 1, in: 200) == 198)
        #expect(SourceBar.tickX(at: 0.5, in: 1) == 0)
    }
}

@Suite("Source bar: the view")
struct SourceBarViewTests {
    private let palette = ThemeRegistry.builtIn[0].palette

    @Test func noLayoutDrawsTodaysBar() {
        let bar = ProgressBarView(utilization: 40, paceFraction: 0.3, palette: palette)
        #expect(bar.drawsTwoTone == false)
    }

    @Test func oneSourceDrawsTodaysBar() {
        let l = layout(.claude, [P(.claude, 40)])
        let bar = ProgressBarView(utilization: 40, paceFraction: 0.3, palette: palette, sources: l)
        #expect(bar.drawsTwoTone == false)
    }

    @Test func twoSourcesDrawTheTwoToneBar() {
        let l = layout(.claude, [P(.claude, 40), P(.codex, 70)])
        let bar = ProgressBarView(utilization: 40, paceFraction: 0.3, palette: palette, sources: l)
        #expect(bar.drawsTwoTone)
    }

    @Test func aBehindSourceIsAlreadyATickSoTheBarIsTwoTone() {
        let l = layout(.claude, [P(.claude, 80), P(.codex, 30)])
        let bar = ProgressBarView(utilization: 80, paceFraction: nil, palette: palette, sources: l)
        #expect(bar.drawsTwoTone)
    }
}
