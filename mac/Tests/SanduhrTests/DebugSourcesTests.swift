import Foundation
import Testing
@testable import Sanduhr

private final class SmokeSourcesHost: SetupHost {
    var drawersOn = false
    var drawersEverSorted = false
    var drawerFiles = 0
    var addOnsInstalled = false
    var usageOn = true
    var hasAccount = true

    func turnOffDrawers() -> SetupDrawersOff { SetupDrawersOff(off: true, line: nil) }
    func turnOnDrawers() {}
    func sortNow() -> String? { nil }
    func setUsage(_ on: Bool) {}
    func reloadDesk() {}
}

/// The smoke hooks for the agent sources (2.46.0): the `sources` action's words and refusals, and the state.yaml
/// blocks that report the group, the Codex service, the widget layout, the menu bar, the footer and the Vault.
@MainActor
@Suite("Debug sources hooks")
struct DebugSourcesTests {
    private let setupStore = MemoryDefaults()
    private let deskStore = MemoryDefaults()
    private let appStore = MemoryDefaults()
    private let host = SmokeSourcesHost()
    private let home: URL

    init() throws {
        home = FileManager.default.temporaryDirectory.appendingPathComponent("debug-sources-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
    }

    private func makeModel(setup: SetupSettings? = SetupSettings(desktop: true, usage: ["claude": true]),
                           vaultRoots: Set<AgentSource> = []) -> SourcesSettingsModel {
        if let setup { SetupStore.save(setup, to: setupStore) }
        deskStore.set(true, forKey: DeskController.enabledKey)
        let service = SetupService(storage: .defaults(setup: setupStore, desk: deskStore), host: host)
        let setupStore = self.setupStore
        return SourcesSettingsModel(service: service, store: appStore, loadSetup: { SetupStore.load(from: setupStore) },
                                    home: home, environment: [:], hasVaultRoot: { vaultRoots.contains($0) })
    }

    private func steps(_ arg: String) -> [DebugAction.SourcesStep]? {
        if case .success(.sources(let s)) = DebugLink.action("sources", arg: arg) { return s }
        return nil
    }

    // MARK: Parsing

    @Test func theSourcesWordsParseInOrder() {
        #expect(steps("antigravity=on") == [.antigravity(true)])
        #expect(steps("ANTIGRAVITY=Off") == [.antigravity(false)])
        #expect(steps("primary=codex") == [.primary(.codex)])
        #expect(steps("color=codex:#2255CC") == [.color(.codex, "#2255CC")])
        #expect(steps("color=Codex:reset") == [.color(.codex, nil)])
        #expect(steps("mcp=antigravity:on mcp=codex:off") == [.mcp(.antigravity, true), .mcp(.codex, false)])
        #expect(steps("antigravity=on,primary=claude color=claude:#112233 color=antigravity:#445566")
                == [.antigravity(true), .primary(.claude), .color(.claude, "#112233"), .color(.antigravity, "#445566")])
        // A color that is not hex reaches the model, which refuses it as the Sources box does.
        #expect(steps("color=codex:red") == [.color(.codex, "red")])
        #expect(DebugAction.names.contains("sources"))
    }

    @Test func theSourcesActionRefusesWhatItCannotRead() {
        for arg in [nil, "", "antigravity", "antigravity=maybe", "primary=antigravity", "primary=gemini", "primary",
                    "color=codex", "color=codex:", "color=gemini:#112233", "mcp=claude:on", "mcp=codex", "mcp=codex:maybe",
                    "codex=on", "antigravity=on private"] {
            let result = DebugLink.action("sources", arg: arg)
            if case .success = result { Issue.record("accepted \(arg ?? "nil")") }
        }
        if case .failure(let e) = DebugLink.action("sources", arg: "primary=antigravity") {
            #expect(e.message.contains("Antigravity has no limits"))
        } else { Issue.record("antigravity primary accepted") }
    }

    @Test func aSourcesLinkReachesTheAction() {
        let url = URL(string: "sanduhr://debug/action?name=sources&arg=primary%3Dcodex%20color%3Dcodex%3A%232255CC&dir=/tmp/x")!
        let request = DebugLink.parse(url)
        #expect(request.error == nil)
        #expect(request.command == .action(.sources([.primary(.codex), .color(.codex, "#2255CC")]), dir: "/tmp/x"))
    }

    // MARK: Applying

    @Test func antigravityColorAndConsentLandThroughTheModel() {
        let model = makeModel()
        #expect(DebugSources.apply([.antigravity(true), .color(.antigravity, "#445566"), .mcp(.antigravity, true)], to: model) == nil)
        let row = model.rows.first { $0.source == .antigravity }!
        #expect(row.isOn)
        #expect(row.color == "#445566")
        #expect(row.mcpAgent?.isOn == true)
        #expect(MCPAgentConsent.load(from: appStore).isOn(.antigravity))
        #expect(DebugSources.apply([.color(.antigravity, nil), .antigravity(false)], to: model) == nil)
        #expect(model.rows.first { $0.source == .antigravity }!.isDefaultColor)
    }

    @Test func aRefusalIsTheActionsError() {
        let model = makeModel()
        // Not hex: the group's own line.
        #expect(DebugSources.apply([.color(.codex, "red")], to: model) == SourcesCopy.colorRefused)
        #expect(AgentSourceSettings.load(from: appStore).colors[.codex] == nil)
        // Codex is off: it can't lead, and its consent switch isn't offered.
        #expect(DebugSources.apply([.primary(.codex)], to: model)?.contains("can't be primary") == true)
        #expect(DebugSources.apply([.mcp(.codex, true)], to: model)?.contains("not offered") == true)
        #expect(!MCPAgentConsent.load(from: appStore).isOn(.codex))
        // The first refusal stops the rest.
        #expect(DebugSources.apply([.color(.codex, "red"), .antigravity(true)], to: model) != nil)
        #expect(!AgentSourceSettings.load(from: appStore).antigravityOn)
    }

    @Test func primaryFollowsACodexThatIsOn() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        #expect(DebugSources.apply([.primary(.codex)], to: model) == nil)
        #expect(AgentSourceSettings.load(from: appStore).primary(setup: SetupStore.load(from: setupStore)) == .codex)
    }

    // MARK: state.yaml

    @Test func theSourcesBlockReportsTheResolvedPrimaryAndEachConsent() {
        let setup = SetupSettings(desktop: true, usage: ["claude": true, "codex": true])
        var settings = AgentSourceSettings()
        settings.savedPrimary = .codex
        settings.colors[.codex] = "#2255CC"
        settings.antigravityOn = true
        let consent = MCPAgentConsent.off.setting(.antigravity, true)
        let block = SourcesDebug.of(setup: setup, settings: settings, consent: consent)
        let yaml = YAMLEmitter.emit(DebugState.sourcesYAML(block))
        #expect(yaml == """
        primary: codex
        claude:
          "on": true
          color: "#E0875A"
          mcp: null
        codex:
          "on": true
          color: "#2255CC"
          mcp: false
        antigravity:
          "on": true
          color: "#7B8CF0"
          mcp: true

        """)
        // A saved Primary that is off resolves to Claude.
        let off = SourcesDebug.of(setup: SetupSettings(desktop: true, usage: ["claude": true]), settings: settings, consent: .off)
        #expect(off.primary == .claude)
    }

    @Test func theCodexBlockIsPercentsAndTimesOnly() {
        let c = CodexDebug(running: true, asOf: Date(timeIntervalSince1970: 0), freshTiers: [.fiveHour],
                           emptyState: nil, fiveHour: 40, sevenDay: 12)
        let yaml = YAMLEmitter.emit(DebugState.codexYAML(c))
        #expect(yaml == """
        running: true
        as_of: "1970-01-01T00:00:00Z"
        fresh_tiers:
          - five_hour
        empty_state: null
        tiers:
          five_hour: 40
          seven_day: 12

        """)
        let empty = YAMLEmitter.emit(DebugState.codexYAML(CodexDebug(emptyState: "no_limits_yet")))
        #expect(empty.contains("empty_state: no_limits_yet\ntiers:\n  five_hour: null\n  seven_day: null\n"))
    }

    @Test func theWidgetSourcesBlockFollowsTheLayout() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        func usage(_ five: Double, _ week: Double) -> UsageResponse {
            UsageResponse(tiers: [.fiveHour: TierUsage(utilization: five, resetsAt: nil),
                                  .sevenDay: TierUsage(utilization: week, resetsAt: nil)])
        }
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        let codex = CodexWidgetReading(usage: usage(40, 12), asOf: now, freshTiers: [.fiveHour, .sevenDay])
        let both = WidgetSources.build(WidgetSourcesInput(
            claudeOn: true, claudeUsage: usage(20, 10), codexOn: true, codex: codex, collapsed: [.codex],
            now: now, calendar: calendar, locale: Locale(identifier: "en_US")))
        let block = WidgetSourcesDebug.of(both)
        #expect(block.combined == ["five_hour", "weekly"])
        #expect(block.sections.map(\.source) == [.claude, .codex])
        #expect(block.sections.map(\.collapsed) == [false, true])
        #expect(block.sections.map(\.content) == ["cards", "collapsed"])
        #expect(block.sections[0].asOf == nil)
        #expect(block.sections[1].asOf?.hasPrefix("as of ") == true)
        // One source: today's widget, a headerless Claude section and no combined bars.
        let lone = WidgetSourcesDebug.of(WidgetSources.build(WidgetSourcesInput(claudeOn: true, claudeUsage: usage(20, 10), codexOn: false)))
        #expect(lone.combined.isEmpty)
        #expect(lone.sections.map(\.source) == [.claude])
        #expect(lone.sections[0].collapsed == false)
        let yaml = YAMLEmitter.emit(DebugState.widgetSourcesYAML(lone))
        #expect(yaml.hasPrefix("combined: []\nsections:\n"))
        #expect(yaml.contains("source: claude\n    collapsed: false\n    content: cards\n"))
    }

    @Test func theVaultAgentsListTheRootsPresentWithTheirLastIngest() {
        let metas: [AgentSource: VaultRootMeta] = [.codex: VaultRootMeta(lastIngestTs: "2026-11-20T10:00:00Z", agent: "codex")]
        let rows = VaultAgentDebug.rows(roots: [.codex, .antigravity], meta: { metas[$0] })
        #expect(rows == [VaultAgentDebug(agent: .codex, lastIngestTs: "2026-11-20T10:00:00Z"),
                         VaultAgentDebug(agent: .antigravity, lastIngestTs: nil)])
        let yaml = YAMLEmitter.emit(DebugState.vaultAgentsYAML(rows))
        #expect(yaml.contains("agent: codex\n  last_ingest_ts: \"2026-11-20T10:00:00Z\"\n"))
        #expect(yaml.contains("agent: antigravity\n  last_ingest_ts: null\n"))
        #expect(YAMLEmitter.emit(DebugState.vaultAgentsYAML([])) == "[]\n")
    }

    @Test func theMenuBarAndFooterBlocksAreTextOnly() {
        let yaml = YAMLEmitter.emit(DebugState.menuBarDisplayYAML(MenuBarDisplayDebug(text: "40%", tooltip: "Codex week 70%.")))
        #expect(yaml == "text: \"40%\"\ntooltip: Codex week 70%.\n")
        #expect(YAMLEmitter.emit(DebugState.menuBarDisplayYAML(MenuBarDisplayDebug())) == "text: null\ntooltip: null\n")
        var s = DebugStateInput()
        s.footer = "Updated 4:10 PM · Float · Codex +3.0k"
        s.sources = SourcesDebug.of(setup: nil, settings: AgentSourceSettings(), consent: .off)
        let state = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(state.contains("\nfooter: \"Updated 4:10 PM · Float · Codex +3.0k\"\nvault_agents: []\n"))
        // No path or home anywhere in the new blocks.
        let tail = state.components(separatedBy: "\nsources:\n").last ?? ""
        #expect(!tail.contains("/Users/") && !tail.contains("~/"))
    }
}
