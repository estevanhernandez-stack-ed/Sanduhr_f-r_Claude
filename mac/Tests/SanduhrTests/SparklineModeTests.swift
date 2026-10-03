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
