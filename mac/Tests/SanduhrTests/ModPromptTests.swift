import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice E: the Build with Claude prompt. It carries the idea and nothing of this Mac's.
@Suite("Mod Studio, the Build with Claude prompt")
struct ModPromptTests {
    @Test func theIdeaIsOneTrimmedCappedLine() {
        #expect(ModPrompt.idea("  a band that shows\nmy CI runs \n") == "a band that shows my CI runs")
        let long = String(repeating: "x", count: ModPrompt.maxIdea + 50)
        #expect(ModPrompt.idea(long).count == ModPrompt.maxIdea)
        let text = ModPrompt.text(idea: "a band that shows\nmy CI runs")
        #expect(text.hasPrefix("Build me a Claude Code mod: a band that shows my CI runs\n"))
    }

    @Test func withoutAnIdeaItAsksFirst() {
        #expect(ModPrompt.text(idea: " \n ").hasPrefix("Build me a Claude Code mod. Ask me what it should show or do"))
    }

    @Test func itCoversWhatAModIsTheLooksAndTheRules() {
        let text = ModPrompt.text(idea: "a clock")
        for needle in [".claude-plugin/plugin.json", "hooks/hooks.json", "register(on, options)", "userConfig",
                       "claude plugin validate --json", "claude plugin test", "No network", "No secrets",
                       "Read-only by default", "\"ink\"", "motion"] {
            #expect(text.contains(needle), "\(needle)")
        }
        // Every letter style the band and statusline accept, by the name they accept.
        for style in LetterStyle.allCases { #expect(text.contains(style.rawValue), "\(style)") }
    }

    @Test func itNamesNoFolderAndNoSetting() {
        let text = ModPrompt.text(idea: "a clock")
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        for needle in [home, "/Users/", "~/", "$HOME", "Library/", "CLAUDE_CONFIG_DIR", "dev-mods", "settings.json",
                       "pluginConfigs", "enabledPlugins", "snapshot", "seats.json", "band.json"] {
            #expect(!text.contains(needle), "\(needle)")
        }
        // Apart from the idea, the same text every time.
        #expect(ModPrompt.text(idea: "a clock").replacingOccurrences(of: "a clock", with: "")
            == ModPrompt.text(idea: "a timer").replacingOccurrences(of: "a timer", with: ""))
    }
}
