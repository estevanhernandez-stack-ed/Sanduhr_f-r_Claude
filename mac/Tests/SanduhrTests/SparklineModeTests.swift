import Testing
@testable import Sanduhr

@Suite("Sparkline mode")
struct SparklineModeTests {
    @Test func matchDeskDrawsTheLine() {
        #expect(SparklineView.mode(themeID: DeskThemeMapping.id) == .line)
    }

    @Test func otherThemesKeepTheHorizonChart() {
        for id in ["obsidian", "aurora", "matrix", "my-custom"] {
            #expect(SparklineView.mode(themeID: id) == .horizon)
        }
    }
}

@Suite("Sparkline steady readings")
struct SparklineSteadyTests {
    @Test func steadyHighReadingsDrawALevelNotASlab() {
        #expect(SparklineView.drawn(.horizon, values: [96, 96, 96.4, 96, 97.2]) == .level)
    }

    @Test func movementKeepsTheHorizonChart() {
        #expect(SparklineView.drawn(.horizon, values: [40, 52, 61, 70]) == .horizon)
        #expect(SparklineView.drawn(.horizon, values: [95, 97.5]) == .horizon)
    }

    @Test func theLineModeIsLeftAlone() {
        #expect(SparklineView.drawn(.line, values: [96, 96, 96]) == .line)
        #expect(SparklineView.drawn(.horizon, values: []) == .horizon)
    }
}
