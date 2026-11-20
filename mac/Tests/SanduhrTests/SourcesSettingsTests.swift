import Foundation
import Testing
@testable import Sanduhr

private final class SourcesHost: SetupHost {
    var calls: [String] = []
    var drawersOn = false
    var drawersEverSorted = false
    var drawerFiles = 0
    var addOnsInstalled = false
    var usageOn = true
    var hasAccount = true

    func turnOffDrawers() -> SetupDrawersOff { SetupDrawersOff(off: true, line: nil) }
    func turnOnDrawers() {}
    func sortNow() -> String? { nil }
    func setUsage(_ on: Bool) { calls.append("usage:\(on)"); usageOn = on }
    func reloadDesk() { calls.append("reload") }
}

/// Settings, Usage, Sources (agent sources, item 7): the rows, the switches that go through setup,
/// Antigravity's own key, Primary, colors, and the captions. In-memory defaults and temp homes.
@MainActor
@Suite("Sources settings")
struct SourcesSettingsTests {
    private let setupStore = MemoryDefaults()
    private let deskStore = MemoryDefaults()
    private let appStore = MemoryDefaults()
    private let host = SourcesHost()
    private let home: URL

    init() throws {
        home = FileManager.default.temporaryDirectory.appendingPathComponent("sources-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
    }

    private func makeModel(setup: SetupSettings? = SetupSettings(desktop: true, usage: ["claude": true]),
                           deskOn: Bool = true, vaultRoots: Set<AgentSource> = []) -> SourcesSettingsModel {
        if let setup { SetupStore.save(setup, to: setupStore) }
        deskStore.set(deskOn, forKey: DeskController.enabledKey)
        let service = SetupService(storage: .defaults(setup: setupStore, desk: deskStore), host: host)
        let setupStore = self.setupStore
        return SourcesSettingsModel(service: service, store: appStore,
                                    loadSetup: { SetupStore.load(from: setupStore) }, home: home, environment: [:],
                                    hasVaultRoot: { vaultRoots.contains($0) })
    }

    private func makeDir(_ parts: String...) throws {
        var url = home
        for p in parts { url.appendPathComponent(p, isDirectory: true) }
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    }

    private func row(_ model: SourcesSettingsModel, _ source: AgentSource) -> SourceRow {
        model.rows.first { $0.source == source }!
    }

    // MARK: rows and captions

    @Test func threeRowsInOrderWithTheDefaults() {
        let model = makeModel()
        #expect(model.rows.map(\.source) == [.claude, .codex, .antigravity])
        #expect(model.rows.map(\.isOn) == [true, false, false])
        #expect(model.rows.map(\.color) == ["#E0875A", "#2FB5A3", "#7B8CF0"])
        #expect(model.rows.map(\.isPrimary) == [true, false, false])
        #expect(model.rows.map(\.canBePrimary) == [true, false, false])
    }

    @Test func captionsAreWindowsWordedForTheMac() {
        #expect(SourcesCopy.codexCaption == "Reads ~/.codex. Activity and limits, no sign-in.")
        #expect(SourcesCopy.antigravityCaption == "Activity only, from the Antigravity CLI. Sanduhr can't see Antigravity's limits or the IDE's usage.")
        #expect(SourcesCopy.nothingFound == "Nothing found yet.")
        #expect(SourcesCopy.antigravityTrademark == "Antigravity and Gemini are trademarks of Google LLC. Sanduhr is not affiliated with or endorsed by Google.")
        let model = makeModel()
        #expect(row(model, .codex).caption == SourcesCopy.codexCaption)
        #expect(row(model, .antigravity).caption == SourcesCopy.antigravityCaption)
        #expect(row(model, .claude).caption == nil)
        // The trademark lines sit under their rows.
        #expect(row(model, .codex).trademark == SetupCopy.codexTrademark)
        #expect(row(model, .antigravity).trademark == SourcesCopy.antigravityTrademark)
        #expect(row(model, .claude).trademark == nil)
    }

    @Test func nothingFoundWhileTheFoldersAreAbsent() throws {
        let model = makeModel()
        #expect(row(model, .codex).nothingFound == SourcesCopy.nothingFound)
        #expect(row(model, .antigravity).nothingFound == SourcesCopy.nothingFound)
        #expect(row(model, .claude).nothingFound == nil)
        try makeDir(".codex")
        try makeDir(".gemini", "antigravity-cli")
        model.reload()
        // An empty Antigravity folder still reads as nothing; Codex's folder is enough.
        #expect(row(model, .codex).nothingFound == nil)
        #expect(row(model, .antigravity).nothingFound == SourcesCopy.nothingFound)
        try makeDir(".gemini", "antigravity-cli", "brain")
        model.reload()
        #expect(row(model, .antigravity).nothingFound == nil)
    }

    @Test func probesTakeAHomeAndAnEnvironment() throws {
        #expect(!SourcesProbe.codexFound(home: home))
        #expect(!SourcesProbe.antigravityFound(home: home))
        try makeDir(".codex")
        #expect(SourcesProbe.codexFound(home: home))
        // A file where the folder should be is not a folder.
        let gemini = home.appendingPathComponent(".gemini")
        try Data().write(to: gemini)
        #expect(!SourcesProbe.antigravityFound(home: home))
    }

    @Test func aSourceWithNothingFoundIsGreyedUnlessItIsAlreadyOn() {
        let model = makeModel()
        #expect(!row(model, .codex).canToggle)
        #expect(!row(model, .antigravity).canToggle)
        #expect(row(model, .claude).canToggle)
        let on = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        #expect(row(on, .codex).canToggle)   // left on from before: it can still be turned off
    }

    // MARK: the switches go through setup

    @Test func codexOnWritesTheSetupAndKeepsClaudeOn() throws {
        try makeDir(".codex")
        let model = makeModel()
        model.setOn(.codex, true)
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        #expect(row(model, .codex).isOn)
        #expect(row(model, .claude).isOn)
        #expect(model.message == nil)
        #expect(appStore.object(forKey: AgentSourceSettings.antigravityOnKey) == nil)
    }

    @Test func aMacNeverSetUpKeepsClaudeOnWhenCodexIsTurnedOn() throws {
        try makeDir(".codex")
        let model = makeModel(setup: nil)
        #expect(row(model, .claude).isOn)   // no setup reads as on
        model.setOn(.codex, true)
        let saved = SetupStore.load(from: setupStore)
        #expect(saved?.claudeUsage == true && saved?.codexUsage == true)
    }

    @Test func claudeOffGoesThroughSetupWithItsConsequences() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        model.setOn(.claude, false)
        #expect(SetupStore.load(from: setupStore)?.claudeUsage == false)
        #expect(SetupStore.load(from: setupStore)?.codexUsage == true)
        // The same sequence as setup's Continue: the save, the usage switch, the Desk reload.
        #expect(host.calls == ["usage:false", "reload"])
        // The line setup says for it: the account stays saved.
        #expect(model.message == SetupCopy.usageOffLine)
        #expect(!row(model, .claude).isOn)
    }

    @Test func codexOffSaysSetupsLine() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        model.setOn(.codex, false)
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true, "codex": false]))
        #expect(model.message == SetupCopy.codexOffLine)
    }

    @Test func claudeOffAndOnAgainLeavesTheClaudeOnlyShapeAlone() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true]))
        model.setOn(.claude, false)
        model.setOn(.claude, true)
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true]))
    }

    @Test func theLastSourceIsRefusedWhileTheDeskIsOff() {
        let model = makeModel(deskOn: false)
        model.setOn(.claude, false)
        #expect(model.message == AgentSourceRules.keepOne)
        #expect(SetupStore.load(from: setupStore)?.claudeUsage == true)   // nothing saved
        #expect(host.calls.isEmpty)
        #expect(row(model, .claude).isOn)
    }

    @Test func theLastSourceMayGoWhileTheDeskRuns() {
        let model = makeModel(deskOn: true)
        model.setOn(.claude, false)
        #expect(model.message != AgentSourceRules.keepOne)
        #expect(SetupStore.load(from: setupStore)?.claudeUsage == false)
    }

    @Test func aRefusalClearsOnTheNextChange() throws {
        try makeDir(".codex")
        let model = makeModel(deskOn: false)
        model.setOn(.claude, false)
        #expect(model.message == AgentSourceRules.keepOne)
        model.setOn(.codex, true)
        #expect(model.message == nil)
    }

    @Test func claudeMayGoOffWithCodexOnAndTheDeskOff() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]), deskOn: false)
        model.setOn(.claude, false)
        #expect(SetupStore.load(from: setupStore)?.claudeUsage == false)
        #expect(model.message != AgentSourceRules.keepOne)
    }

    @Test func antigravityHasItsOwnKeyAndIsNeverRefused() {
        let model = makeModel(deskOn: false)
        model.setOn(.antigravity, true)
        #expect(appStore.object(forKey: AgentSourceSettings.antigravityOnKey) as? Bool == true)
        #expect(row(model, .antigravity).isOn)
        // Setup is untouched.
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true]))
        #expect(host.calls.isEmpty)
        model.setOn(.antigravity, false)
        #expect(appStore.object(forKey: AgentSourceSettings.antigravityOnKey) as? Bool == false)
    }

    @Test func changesAreAnnounced() throws {
        try makeDir(".codex")
        let model = makeModel()
        var changed = 0
        var applied = 0
        model.onChanged = { changed += 1 }
        model.onApplied = { _ in applied += 1 }
        model.setOn(.codex, true)
        #expect(applied == 1 && changed >= 1)
        let before = changed
        model.setOn(.antigravity, true)
        #expect(applied == 1 && changed == before + 1)
        model.setColor(.codex, "#112233")
        #expect(changed == before + 2)
    }

    // MARK: Primary

    @Test func primaryIsChosenAmongLimitsSourcesThatAreOn() {
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        #expect(row(model, .codex).canBePrimary)
        model.setPrimary(.codex)
        #expect(appStore.string(forKey: AgentSourceSettings.primaryKey) == "codex")
        #expect(row(model, .codex).isPrimary && !row(model, .claude).isPrimary)
        #expect(!row(model, .antigravity).canBePrimary)
    }

    @Test func primaryRefusesAnOffSourceAndAntigravity() {
        let model = makeModel()
        model.setPrimary(.codex)
        model.setPrimary(.antigravity)
        #expect(appStore.object(forKey: AgentSourceSettings.primaryKey) == nil)
        #expect(row(model, .claude).isPrimary)
    }

    @Test func primaryFallsToTheOnlySourceOn() {
        var settings = AgentSourceSettings.default
        settings.savedPrimary = .codex
        settings.save(to: appStore)
        let model = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": false]))
        #expect(row(model, .claude).isPrimary && !row(model, .codex).isPrimary)
    }

    // MARK: colors

    @Test func aColorIsSavedAndResetToItsDefault() {
        let model = makeModel()
        #expect(model.setColor(.codex, "#112233"))
        #expect(row(model, .codex).color == "#112233")
        #expect(!row(model, .codex).isDefaultColor)
        #expect(AgentSourceSettings.load(from: appStore).color(of: .codex) == "#112233")
        model.resetColor(.codex)
        #expect(row(model, .codex).color == "#2FB5A3" && row(model, .codex).isDefaultColor)
        #expect(appStore.object(forKey: AgentSourceSettings.colorKey(.codex)) == nil)
    }

    @Test func aBadColorIsRefusedAndTheSavedOneStays() {
        let model = makeModel()
        #expect(model.setColor(.claude, "#445566"))
        #expect(!model.setColor(.claude, "red"))
        #expect(model.colorMessage[.claude] == SourcesCopy.colorRefused)
        #expect(row(model, .claude).color == "#445566")
        #expect(model.setColor(.claude, "#778899"))
        #expect(model.colorMessage[.claude] == nil)
    }

    @Test func hexRoundTripsThroughAColorWell() {
        #expect(SourcesColor.hex(red: 0.8784, green: 0.5294, blue: 0.3529) == "#E0875A")
        #expect(SourcesColor.hex(red: 0, green: 1, blue: 0.5) == "#00FF80")
        #expect(SourcesColor.hex(red: 2, green: -1, blue: 0) == "#FF0000")   // clamped
    }

    // MARK: Claude only

    @Test func aClaudeOnlyMacChangesNothingByOpeningThePage() {
        let model = makeModel()
        model.reload()
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": true]))
        #expect(appStore.values.isEmpty)
        #expect(host.calls.isEmpty)
    }

    @Test func switchingAntigravityOnAgainClearsAnEraseStop() throws {
        let model = makeModel()
        model.setOn(.antigravity, true)
        VaultAgentStop.stop(.antigravity, in: appStore)   // its record erased while on
        model.setOn(.antigravity, true)
        #expect(VaultAgentStop.isStopped(.antigravity, in: appStore))   // on to on keeps the stop
        model.setOn(.antigravity, false)
        #expect(VaultAgentStop.isStopped(.antigravity, in: appStore))   // off keeps it too
        model.setOn(.antigravity, true)
        #expect(!VaultAgentStop.isStopped(.antigravity, in: appStore))
    }

    // MARK: Let Claude read an agent's project totals (item 13)

    @Test func theConsentSwitchWordsAreTheWindowsOnes() {
        #expect(SourcesCopy.mcpAgentSwitch(.codex) == "Let Claude read Codex's project totals")
        #expect(SourcesCopy.mcpAgentSwitch(.antigravity) == "Let Claude read Antigravity's project totals")
    }

    @Test func theConsentSwitchIsHiddenUntilTheSourceIsOnOrHasARecord() {
        let model = makeModel()
        #expect(row(model, .claude).mcpAgent == nil)
        #expect(row(model, .codex).mcpAgent == nil)
        #expect(row(model, .antigravity).mcpAgent == nil)
        // Codex on: its switch shows, off by default.
        let codexOn = makeModel(setup: SetupSettings(desktop: true, usage: ["claude": true, "codex": true]))
        #expect(row(codexOn, .codex).mcpAgent == MCPAgentSwitch(isOn: false, label: "Let Claude read Codex's project totals"))
        #expect(row(codexOn, .antigravity).mcpAgent == nil)
        // A source switched off with a Vault record still shows its switch.
        let record = makeModel(vaultRoots: [.antigravity])
        #expect(!row(record, .antigravity).isOn)
        #expect(row(record, .antigravity).mcpAgent?.isOn == false)
        #expect(row(record, .codex).mcpAgent == nil)
    }

    @Test func togglingTheConsentWritesItsKeyAndAsksForAResync() {
        let model = makeModel(vaultRoots: [.codex, .antigravity])
        var resynced = 0
        model.onConsentChanged = { resynced += 1 }
        #expect(appStore.object(forKey: "mcp.agents.codex") == nil)   // opening the page wrote nothing
        model.setMCPAgent(.codex, true)
        #expect(appStore.object(forKey: "mcp.agents.codex") as? Bool == true)
        #expect(appStore.object(forKey: "mcp.agents.antigravity") == nil)
        #expect(resynced == 1)
        #expect(row(model, .codex).mcpAgent?.isOn == true)
        #expect(MCPAgentConsent.load(from: appStore) == MCPAgentConsent.off.setting(.codex, true))
        model.setMCPAgent(.codex, false)
        #expect(appStore.object(forKey: "mcp.agents.codex") as? Bool == false)
        #expect(resynced == 2)
        #expect(MCPAgentConsent.load(from: appStore) == .off)
    }

    @Test func claudeHasNoConsentSwitchToToggle() {
        let model = makeModel()
        var resynced = 0
        model.onConsentChanged = { resynced += 1 }
        model.setMCPAgent(.claude, true)
        #expect(resynced == 0)
        #expect(appStore.values.isEmpty)
    }
}
