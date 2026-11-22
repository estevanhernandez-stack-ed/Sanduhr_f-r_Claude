import Foundation
import Testing
@testable import Sanduhr

/// Mac agent notifications, item 6: which agents are followed, and which path a Claude Code post takes.
/// Temp folders and fakes only: no real FSEvents stream, timer, agent home or Darwin name is touched.

private let claudeOnlySetup = SetupSettings(desktop: true, usage: [SetupSettings.claude: true])
private let claudeAndCodex = SetupSettings(desktop: true, usage: [SetupSettings.claude: true, SetupSettings.codex: true])

private func sources(antigravity: Bool = false) -> AgentSourceSettings {
    var s = AgentSourceSettings()
    s.antigravityOn = antigravity
    return s
}

@Suite("Agent notifications wiring: who is followed")
struct AgentWiringWantsTests {
    @Test func aTwoFortySixMacFollowsNothing() {
        // Claude only, the hook not updated, Antigravity off: 2.46.
        let w = AgentNotificationsWiring.wants(setup: claudeOnlySetup, sources: sources(), demo: false, hookInstalled: false)
        #expect(w == (claude: false, codex: false, antigravity: false))
        let none = AgentNotificationsWiring.wants(setup: nil, sources: sources(), demo: false, hookInstalled: false)
        #expect(none == (claude: false, codex: false, antigravity: false))
    }

    @Test func claudeCodeIsOnOnlyWithTheUpdatedHook() {
        let w = AgentNotificationsWiring.wants(setup: claudeOnlySetup, sources: sources(), demo: false, hookInstalled: true)
        #expect(w == (claude: true, codex: false, antigravity: false))
        // No setup reads as Claude on, as everywhere else.
        let bare = AgentNotificationsWiring.wants(setup: nil, sources: sources(), demo: false, hookInstalled: true)
        #expect(bare.claude)
    }

    @Test func claudeCodeFollowsTheHookNotTheClaudeUsageSource() {
        // Usage off (a Just the Desk Mac, or Codex only) with the updated hook: Claude Code is still on.
        let codexOnly = SetupSettings(desktop: true, usage: [SetupSettings.claude: false, SetupSettings.codex: true])
        let w = AgentNotificationsWiring.wants(setup: codexOnly, sources: sources(), demo: false, hookInstalled: true)
        #expect(w == (claude: true, codex: true, antigravity: false))
        let none = SetupSettings(desktop: true, usage: [SetupSettings.claude: false])
        #expect(AgentNotificationsWiring.wants(setup: none, sources: sources(), demo: false, hookInstalled: true).claude)
        #expect(!AgentNotificationsWiring.wants(setup: none, sources: sources(), demo: false, hookInstalled: false).claude)
    }

    @Test func codexAndAntigravityFollowTheirSources() {
        let w = AgentNotificationsWiring.wants(setup: claudeAndCodex, sources: sources(antigravity: true), demo: false,
                                               hookInstalled: false)
        #expect(w == (claude: false, codex: true, antigravity: true))
        let agy = AgentNotificationsWiring.wants(setup: claudeOnlySetup, sources: sources(antigravity: true), demo: false,
                                                 hookInstalled: false)
        #expect(agy == (claude: false, codex: false, antigravity: true))
    }

    @Test func demoModeFollowsNothing() {
        let w = AgentNotificationsWiring.wants(setup: claudeAndCodex, sources: sources(antigravity: true), demo: true,
                                               hookInstalled: true)
        #expect(w == (claude: false, codex: false, antigravity: false))
    }

    @Test func theHookCountsOnlyWhenCurrentInAtLeastOneFolder() {
        let status: [String: IntegrationStatus] = ["a": .outdated, "b": .notInstalled, "c": .installed, "d": .unreadable]
        #expect(AgentNotificationsWiring.hookInstalled(folders: ["a", "b", "c"]) { status[$0] ?? .notInstalled })
        #expect(!AgentNotificationsWiring.hookInstalled(folders: ["a", "b", "d"]) { status[$0] ?? .notInstalled })
        #expect(!AgentNotificationsWiring.hookInstalled(folders: []) { _ in .installed })
    }
}

@Suite("Agent notifications wiring: the Darwin post's path")
struct AgentWiringRouteTests {
    private func post(_ event: ClaudeCodeEvent, _ result: ClaudeCodePostResult) -> (read: Int, legacy: [ClaudeCodeEvent]) {
        var read = 0
        var legacy: [ClaudeCodeEvent] = []
        AgentNotificationsWiring.handlePost(event, posted: { read += 1; return result }, legacyGlow: { legacy.append($0) })
        return (read, legacy)
    }

    @Test func aPostWithFilesGoesTheNewPathAndNeverTheLegacyGlow() {
        for event in ClaudeCodeEvent.allCases {
            let r = post(event, .hadFiles)
            #expect(r.read == 1)
            #expect(r.legacy.isEmpty)
        }
    }

    @Test func aBarePostGlowsLegacyForWaitingAndDone() {
        #expect(post(.waiting, .bare).legacy == [.waiting])
        #expect(post(.done, .bare).legacy == [.done])
    }

    @Test func aBarePromptDoesNothing() {
        let r = post(.prompt, .bare)
        #expect(r.read == 1)   // the files are still read: a new hook's prompt can arrive with its file
        #expect(r.legacy.isEmpty)
    }

    @Test func everyPostReadsTheFilesExactlyOnce() {
        #expect(post(.waiting, .bare).read == 1)
        #expect(post(.done, .hadFiles).read == 1)
    }

    @Test func aRealSignalFileTakesTheNewPathEndToEnd() {
        let fx = WatchFixture()
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: true)
        fx.signal("prompt", session: "s1", at: fx.now.addingTimeInterval(-1))
        fx.signal("waiting", session: "s1")
        var legacy: [ClaudeCodeEvent] = []
        AgentNotificationsWiring.handlePost(.waiting, posted: { fx.watcher.claudeCodePosted() }, legacyGlow: { legacy.append($0) })
        #expect(legacy.isEmpty)
        #expect(fx.kinds() == [.started, .waiting])

        // No file: an older hook's post.
        AgentNotificationsWiring.handlePost(.done, posted: { fx.watcher.claudeCodePosted() }, legacyGlow: { legacy.append($0) })
        #expect(legacy == [.done])
        #expect(fx.kinds() == [.started, .waiting])
    }
}

@Suite("Agent notifications wiring: idle cost")
struct AgentWiringIdleTests {
    @Test func aTwoFortySixMacCreatesNoStreamAndNoTimer() {
        let fx = WatchFixture()
        let w = AgentNotificationsWiring.wants(setup: claudeOnlySetup, sources: sources(), demo: false, hookInstalled: false)
        fx.watcher.update(codexOn: w.codex, antigravityOn: w.antigravity, claudeOn: w.claude)
        #expect(fx.streams.made.isEmpty)
        #expect(fx.ticker.starts == 0 && !fx.ticker.running)
        #expect(fx.watcher.followedFolders.isEmpty)
        // Setting the same switches again, as every sync does, still costs nothing.
        fx.watcher.update(codexOn: w.codex, antigravityOn: w.antigravity, claudeOn: w.claude)
        #expect(fx.streams.made.isEmpty)
        #expect(fx.ticker.starts == 0)
    }

    @Test func claudeCodeAloneUsesNoStreamAndNoTimer() {
        let fx = WatchFixture()
        let w = AgentNotificationsWiring.wants(setup: claudeOnlySetup, sources: sources(), demo: false, hookInstalled: true)
        fx.watcher.update(codexOn: w.codex, antigravityOn: w.antigravity, claudeOn: w.claude)
        #expect(fx.streams.made.isEmpty)
        #expect(fx.ticker.starts == 0)
    }

    @Test func demoModeCreatesNoStreamAndNoTimer() {
        let fx = WatchFixture()
        let w = AgentNotificationsWiring.wants(setup: claudeAndCodex, sources: sources(antigravity: true), demo: true,
                                               hookInstalled: true)
        fx.watcher.update(codexOn: w.codex, antigravityOn: w.antigravity, claudeOn: w.claude)
        #expect(fx.streams.made.isEmpty)
        #expect(fx.ticker.starts == 0)
    }

    @Test func codexOnStartsOneStreamAndSwitchingItOffStopsIt() {
        let fx = WatchFixture()
        fx.watcher.update(codexOn: true, antigravityOn: false, claudeOn: false)
        #expect(fx.streams.live.count == 1)
        fx.watcher.update(codexOn: false, antigravityOn: false, claudeOn: false)
        #expect(fx.streams.live.isEmpty)
        #expect(!fx.ticker.running)
    }

    @Test func aHomeThatAppearsLaterIsFollowedOnTheNextUpdate() {
        let fx = WatchFixture()
        try? FileManager.default.removeItem(at: fx.agyHome)
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(fx.streams.made.isEmpty)
        try! FileManager.default.createDirectory(at: fx.agyHome.appendingPathComponent("brain", isDirectory: true),
                                                 withIntermediateDirectories: true)
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(fx.streams.live.count == 1)
        // And once followed, another update does not make a second stream.
        fx.watcher.update(codexOn: false, antigravityOn: true, claudeOn: false)
        #expect(fx.streams.live.count == 1 && fx.streams.made.count == 1)
    }
}
