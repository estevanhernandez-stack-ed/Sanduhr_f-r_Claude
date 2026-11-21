import Foundation
import Testing
@testable import Sanduhr

/// The agent source model: names, defaults, Primary, the last-source rule and the Codex probe.
/// In-memory defaults and temp homes only; nothing reads a real ~/.codex.
@Suite("Agent sources")
struct AgentSourceTests {
    private let claudeOnly = SetupSettings(desktop: true, usage: ["claude": true])
    private let both = SetupSettings(desktop: true, usage: ["claude": true, "codex": true])
    private let codexOnly = SetupSettings(desktop: true, usage: ["claude": false, "codex": true])
    private let none = SetupSettings(desktop: true, usage: ["claude": false])

    // MARK: names

    @Test func namesAndFlags() {
        #expect(AgentSource.allCases == [.claude, .codex, .antigravity])
        #expect(AgentSource.allCases.map(\.displayName) == ["Claude", "Codex", "Antigravity"])
        #expect(AgentSource.codex.shortName == "Codex")
        #expect(AgentSource.antigravity.shortName == "Agy")
        #expect(AgentSource.allCases.map(\.hasLimits) == [true, true, false])
        #expect(AgentSource.withLimits == [.claude, .codex])
    }

    // MARK: defaults

    @Test func defaultsAreClaudeOnAndTheRestOff() {
        let s = AgentSourceSettings.default
        #expect(s.isOn(.claude, setup: nil))
        #expect(!s.isOn(.codex, setup: nil))
        #expect(!s.isOn(.antigravity, setup: nil))
        #expect(s.primary(setup: nil) == .claude)
        #expect(AgentSource.allCases.map { s.color(of: $0) } == ["#E0875A", "#2FB5A3", "#7B8CF0"])
        // A stored Claude-only setup (the shape 2.45.0 writes) reads the same.
        #expect(s.isOn(.claude, setup: claudeOnly) && !s.isOn(.codex, setup: claudeOnly))
        #expect(s.primary(setup: claudeOnly) == .claude)
    }

    @Test func aSetupWithoutClaudesEntryKeepsClaudeOn() {
        // Damage never turns the account's meters off (SetupStore.claudeUsageOn's rule).
        #expect(AgentSourceSettings.default.isOn(.claude, setup: SetupSettings(desktop: true, usage: [:])))
    }

    @Test func codexOnComesFromTheSetup() {
        #expect(AgentSourceSettings.default.isOn(.codex, setup: both))
        #expect(!AgentSourceSettings.default.isOn(.claude, setup: codexOnly))
    }

    // MARK: Primary

    @Test func primaryFallsBackToTheOnlySourceOn() {
        let s = AgentSourceSettings.default
        #expect(s.primary(setup: codexOnly) == .codex)
        #expect(s.primary(setup: both) == .claude)
        #expect(s.primary(setup: none) == .claude)
        var codexLeads = s
        codexLeads.savedPrimary = .codex
        #expect(codexLeads.primary(setup: both) == .codex)
        #expect(codexLeads.primary(setup: claudeOnly) == .claude)   // Codex was turned off while Primary
    }

    @Test func antigravityIsNeverPrimary() {
        var s = AgentSourceSettings.default
        s.antigravityOn = true
        #expect(s.settingPrimary(.antigravity, setup: both) == nil)
        s.savedPrimary = .antigravity   // damage or a hand edit
        #expect(s.primary(setup: claudeOnly) == .claude)
        #expect(s.primary(setup: codexOnly) == .codex)
    }

    @Test func aSourceThatIsOffCantLead() {
        let s = AgentSourceSettings.default
        #expect(s.settingPrimary(.codex, setup: claudeOnly) == nil)
        #expect(s.settingPrimary(.codex, setup: both)?.savedPrimary == .codex)
        #expect(s.settingPrimary(.claude, setup: both)?.savedPrimary == .claude)
    }

    // MARK: colors

    @Test func colorsTakeHexAndRefuseTheRest() {
        let s = AgentSourceSettings.default
        let blue = s.settingColor(.codex, to: " #112233 ")
        #expect(blue?.color(of: .codex) == "#112233")
        #expect(s.settingColor(.codex, to: "teal") == nil)
        #expect(s.settingColor(.codex, to: "#123") == nil)
        #expect(s.settingColor(.codex, to: "112233") == nil)
        #expect(blue?.resettingColor(.codex).color(of: .codex) == "#2FB5A3")
    }

    @Test func aBadStoredColorReadsAsTheDefault() {
        var s = AgentSourceSettings.default
        s.colors[.claude] = "orange"
        #expect(s.color(of: .claude) == "#E0875A")
    }

    // MARK: stored form

    @Test func roundTripsThroughDefaults() {
        let store = MemoryDefaults()
        #expect(AgentSourceSettings.load(from: store) == .default)
        var s = AgentSourceSettings.default
        s.savedPrimary = .codex
        s.antigravityOn = true
        s.colors[.codex] = "#445566"
        s.save(to: store)
        #expect(store.values["sources.primary"] as? String == "codex")
        #expect(store.values["sources.codex.color"] as? String == "#445566")
        #expect(store.values["sources.antigravity.on"] as? Bool == true)
        #expect(AgentSourceSettings.load(from: store) == s)
        // Resetting the color removes its key.
        s.resettingColor(.codex).save(to: store)
        #expect(store.values["sources.codex.color"] == nil)
    }

    @Test func damagedValuesKeepTheDefaults() {
        let store = MemoryDefaults()
        store.set("antigravity", forKey: "sources.primary")   // never offered
        store.set("not a color", forKey: "sources.claude.color")
        store.set("#AABBCC", forKey: "sources.codex.color")
        store.set("yes", forKey: "sources.antigravity.on")
        let s = AgentSourceSettings.load(from: store)
        #expect(s.savedPrimary == .claude)
        #expect(s.color(of: .claude) == "#E0875A")
        #expect(s.color(of: .codex) == "#AABBCC")
        #expect(!s.antigravityOn)
    }

    // MARK: the last-source rule

    @Test func theLastLimitsSourceStaysOnWhileTheDeskIsOff() {
        let refusal = AgentSourceRules.refusalTurningOff(.claude, isOn: { $0 == .claude }, deskRuns: false)
        #expect(refusal == "Keep one on, or turn on the Desk first.")
        #expect(AgentSourceRules.refusalTurningOff(.codex, isOn: { $0 == .codex }, deskRuns: false) == AgentSourceRules.keepOne)
    }

    @Test func turningOffIsFineWhenSomethingIsLeft() {
        #expect(AgentSourceRules.refusalTurningOff(.claude, isOn: { _ in true }, deskRuns: false) == nil)
        #expect(AgentSourceRules.refusalTurningOff(.codex, isOn: { $0 == .codex || $0 == .claude }, deskRuns: false) == nil)
        #expect(AgentSourceRules.refusalTurningOff(.claude, isOn: { $0 == .claude }, deskRuns: true) == nil)
    }

    @Test func antigravityIsNeverRefusedAndDoesntCountAsLeft() {
        #expect(AgentSourceRules.refusalTurningOff(.antigravity, isOn: { $0 == .antigravity }, deskRuns: false) == nil)
        // Antigravity on does not keep Claude's turn-off allowed: it has no limits to show.
        #expect(AgentSourceRules.refusalTurningOff(.claude, isOn: { $0 == .claude || $0 == .antigravity }, deskRuns: false) != nil)
    }

    // MARK: the Codex probe

    @Test func codexIsInstalledWhenItsFolderIsThere() throws {
        let home = FileManager.default.temporaryDirectory.appendingPathComponent("agent-src-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: home) }
        #expect(!CodexInstall.isInstalled(home: home))
        try FileManager.default.createDirectory(at: home.appendingPathComponent(".codex"), withIntermediateDirectories: true)
        #expect(CodexInstall.isInstalled(home: home))
    }

    @Test func aFileNamedCodexIsNotAnInstall() throws {
        let home = FileManager.default.temporaryDirectory.appendingPathComponent("agent-src-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: home) }
        try Data().write(to: home.appendingPathComponent(".codex"))
        #expect(!CodexInstall.isInstalled(home: home))
    }

    @Test func codexHomeFromTheEnvironmentCounts() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("agent-src-\(UUID().uuidString)", isDirectory: true)
        let elsewhere = root.appendingPathComponent("elsewhere", isDirectory: true)
        try FileManager.default.createDirectory(at: elsewhere, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let home = root.appendingPathComponent("home", isDirectory: true)
        #expect(!CodexInstall.isInstalled(home: home, environment: [:]))
        #expect(!CodexInstall.isInstalled(home: home, environment: ["CODEX_HOME": ""]))
        #expect(CodexInstall.isInstalled(home: home, environment: ["CODEX_HOME": elsewhere.path]))
    }
}
