import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice D: the meters mod's options, written by the Mod Studio's Layout Save into
/// `pluginConfigs["sanduhr-meters@inline"].options` by receipt, and undone byte for byte. On
/// made-up homes only.
@Suite("Mod Studio, the mod's options")
struct ModOptionsTests {
    private func member(_ r: IntegrationRig, _ relative: String) -> [String: Any]? {
        ((r.json(relative)["pluginConfigs"] as? [String: Any])?["sanduhr-meters@inline"] as? [String: Any])?["options"] as? [String: Any]
    }

    private static let settings = """
    {
      "model": "opus",
      "pluginConfigs": {
        "deployer@acme-tools": {
          "options": {
            "api_endpoint": "https://api.example.com"
          }
        }
      }
    }

    """

    private var full: MetersOptions { MetersOptions(bars: .session, style: .full, label: "  Personal ", motion: .off) }

    @Test func saveAndResetRoundTripByteForByte() throws {
        for settings in [Self.settings, "{\n  \"model\": \"opus\"\n}\n", "{\"model\":\"opus\",\"pluginConfigs\":{}}",
                         "{\n  \"pluginConfigs\": {\n  }\n}\n", "{}\n"] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", settings)
            let folder = r.home.at(".claude")
            let before = r.snapshot()

            #expect(try r.installer.setOwnModOptions(full, folder: folder) == .written)
            #expect(member(r, ".claude/settings.json") as? [String: String]
                == ["bars": "session", "style": "full", "label": "Personal", "motion": "off"])
            var state = r.installer.ownModOptions(folder: folder)
            #expect(state.isOurs && state.values == full.normalized)

            // Saved again with a change: replaced in place, still Sanduhr's.
            var other = full
            other.bars = .weekly
            #expect(try r.installer.setOwnModOptions(other, folder: folder) == .written)
            #expect(try r.installer.setOwnModOptions(other, folder: folder) == .unchanged)
            state = r.installer.ownModOptions(folder: folder)
            #expect(state.isOurs && state.values.bars == .weekly)

            // Back to the defaults: the file is what it was, and the record is gone.
            #expect(try r.installer.setOwnModOptions(.defaults, folder: folder) == .removed)
            #expect(r.snapshot() == before, "\(settings)")
            #expect(r.installer.loadReceipts().isEmpty)
        }
    }

    @Test func optionsSetInClaudeCodeAreLeftAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", """
        {
          "pluginConfigs": {
            "sanduhr-meters@inline": {
              "options": { "style": "full" }
            }
          }
        }

        """)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        let state = r.installer.ownModOptions(folder: folder)
        #expect(state.isTheirs && state.values.style == .full && state.values.bars == .both)
        #expect(try r.installer.setOwnModOptions(full, folder: folder) == .keptTheirs)
        #expect(try r.installer.setOwnModOptions(.defaults, folder: folder) == .keptTheirs)
        #expect(r.snapshot() == before)
    }

    @Test func aMemberChangedSinceIsTheirsAndUndoLeavesIt() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\"\n}\n")
        let folder = r.home.at(".claude")
        try r.installer.setOwnModOptions(full, folder: folder)
        // /config changes one option.
        let file = r.home.at(".claude/settings.json")
        let changed = try String(contentsOfFile: file, encoding: .utf8).replacingOccurrences(of: "\"off\"", with: "\"on\"")
        try changed.write(toFile: file, atomically: true, encoding: .utf8)
        #expect(r.installer.ownModOptions(folder: folder).isTheirs)
        #expect(try r.installer.setOwnModOptions(.defaults, folder: folder) == .keptTheirs)
        try r.installer.undoModOptions(folder: folder)
        #expect(r.bytes(".claude/settings.json") == Data(changed.utf8))
        #expect(r.installer.loadReceipts().isEmpty)
    }

    @Test func removeTakesOutTheSwitchTheOptionsAndTheEntry() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.install(.meters, folder: folder, python: "")
        try r.installer.setOwnModOptions(full, folder: folder)
        try r.installer.switchOwnMod(on: false, folder: folder)
        // An Update (a reinstall) keeps the options' record.
        try r.installer.install(.meters, folder: folder, python: "")
        #expect(r.installer.ownModOptions(folder: folder).isOurs)
        // Switching back on keeps the options and their record.
        try r.installer.switchOwnMod(on: true, folder: folder)
        #expect(r.installer.ownModOptions(folder: folder).isOurs)
        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
        #expect(r.installer.loadReceipts().isEmpty)
    }

    @Test func theModReadsUnknownValuesAsItsDefaults() {
        let o = MetersOptions(options: ["bars": "hourly", "style": "full", "label": 4, "motion": "off"])
        #expect(o == MetersOptions(bars: .both, style: .full, label: "", motion: .off))
        #expect(MetersOptions(label: "  ").isDefault)
        // The keys the manifest declares, and nothing else.
        let manifest = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/mods/sanduhr-meters/.claude-plugin/plugin.json")
        let declared = ((try? JSONSerialization.jsonObject(with: Data(contentsOf: manifest))) as? [String: Any])?["userConfig"] as? [String: Any]
        #expect(Set(declared?.keys.map { $0 } ?? []) == Set(MetersOptions.defaults.written.keys))
    }
}
