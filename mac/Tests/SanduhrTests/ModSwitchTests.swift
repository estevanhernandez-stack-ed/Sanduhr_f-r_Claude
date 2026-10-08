import Foundation
import Testing
@testable import Sanduhr

/// Sanduhr's own mod on the Mods page (item 64, slice 2): the per-folder switch, Update between
/// pinned versions and Remove, all by receipt and byte for byte. On made-up homes only; nothing
/// reads the real ~/.claude* or ~/Library.
@Suite("Mods page, Sanduhr's own mod")
struct ModSwitchTests {
    private func dirs(_ r: IntegrationRig, _ relative: String) -> String? {
        (r.json(relative)["env"] as? [String: Any])?["CLAUDE_CODE_PLUGIN_DIRS"] as? String
    }

    private func inline(_ r: IntegrationRig, _ relative: String) -> Any? {
        (r.json(relative)["enabledPlugins"] as? [String: Any])?["sanduhr-meters@inline"]
    }

    private static let settings = """
    {
      "model": "opus",
      "enabledPlugins": {
        "helper@market": true
      }
    }

    """

    @Test func offAndOnRoundTripByteForByteAndRemoveRestoresTheOriginal() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        // Installed from Integrations: the entry follows the `current` link.
        try r.installer.install(.meters, folder: folder, python: "")
        let installed = r.bytes(".claude/settings.json")
        var state = r.installer.ownModState(folder: folder)
        #expect(state.isOn && state.pinned == nil && state.inline == nil)

        try r.installer.switchOwnMod(on: false, folder: folder)
        #expect(inline(r, ".claude/settings.json") as? Bool == false)
        #expect(dirs(r, ".claude/settings.json") == r.support.path + "/current/mods/sanduhr-meters")
        #expect((r.json(".claude/settings.json")["enabledPlugins"] as? [String: Any])?["helper@market"] as? Bool == true)
        state = r.installer.ownModState(folder: folder)
        #expect(!state.isOn && state.switched == false && state.listed)
        // Off again changes nothing.
        let off = r.bytes(".claude/settings.json")
        try r.installer.switchOwnMod(on: false, folder: folder)
        #expect(r.bytes(".claude/settings.json") == off)

        try r.installer.switchOwnMod(on: true, folder: folder)
        #expect(r.bytes(".claude/settings.json") == installed)
        #expect(r.installer.ownModState(folder: folder).switched == nil)

        try r.installer.switchOwnMod(on: false, folder: folder)
        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
        #expect(r.installer.loadReceipts().isEmpty)
    }

    @Test func offWithoutEnabledPluginsMakesItAndOnTakesItOutAgain() throws {
        for settings in ["{\n  \"model\": \"opus\"\n}\n", "{\"model\":\"opus\",\"enabledPlugins\":{}}",
                         "{\n  \"enabledPlugins\": {\n  }\n}\n"] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude-work/projects")
            r.home.file(".claude-work/settings.json", settings)
            let folder = r.home.at(".claude-work")
            try r.installer.install(.meters, folder: folder, python: "")
            let installed = r.bytes(".claude-work/settings.json")
            try r.installer.switchOwnMod(on: false, folder: folder)
            #expect(inline(r, ".claude-work/settings.json") as? Bool == false)
            try r.installer.switchOwnMod(on: true, folder: folder)
            #expect(r.bytes(".claude-work/settings.json") == installed)
        }
    }

    @Test func onInAFolderWithoutItPinsTheAppsVersionAndRemoveUndoesIt() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        #expect(!r.installer.ownModState(folder: folder).listed)

        try r.installer.switchOwnMod(on: true, folder: folder)
        let stamp = try #require(r.scripts.bundledStamp)
        #expect(dirs(r, ".claude/settings.json") == r.support.path + "/\(stamp)/mods/sanduhr-meters")
        let state = r.installer.ownModState(folder: folder)
        #expect(state.isOn && state.pinned == stamp)
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        #expect(r.installer.loadReceipts().first?.pinned == stamp)

        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
        #expect(!FileManager.default.fileExists(atPath: r.support.path))
    }

    @Test func theUsersOwnOffIsOverriddenAndPutBackExactly() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let mine = "{\n  \"enabledPlugins\": {\n    \"sanduhr-meters@inline\":    false\n  }\n}\n"
        r.home.file(".claude/settings.json", mine)
        let folder = r.home.at(".claude")
        try r.installer.install(.meters, folder: folder, python: "")
        let installed = r.bytes(".claude/settings.json")
        #expect(!r.installer.ownModState(folder: folder).isOn)

        try r.installer.switchOwnMod(on: true, folder: folder)
        #expect(inline(r, ".claude/settings.json") as? Bool == true)
        #expect(r.installer.ownModState(folder: folder).isOn)
        try r.installer.switchOwnMod(on: false, folder: folder)
        #expect(r.bytes(".claude/settings.json") == installed)
        #expect(r.installer.ownModState(folder: folder).switched == nil)
    }

    @Test func aMemberSomeoneChangedIsLeftAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.settings)
        let folder = r.home.at(".claude")
        try r.installer.install(.meters, folder: folder, python: "")
        try r.installer.switchOwnMod(on: false, folder: folder)
        // The user turns it on by hand in between.
        let edited = try #require(r.text(".claude/settings.json"))
            .replacingOccurrences(of: "\"sanduhr-meters@inline\": false", with: "\"sanduhr-meters@inline\": true")
        r.home.file(".claude/settings.json", edited)
        try r.installer.undoModSwitch(folder: folder)
        #expect(r.text(".claude/settings.json") == edited)
        #expect(r.installer.ownModState(folder: folder).switched == nil)
    }

    @Test func aProjectSettingThatKeepsItOnIsFound() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-work/projects")
        r.home.file(".claude-work/settings.json", Self.settings)
        let project = r.home.at("code/app")
        let other = r.home.at("code/other")
        r.home.dir("code/app/.claude")
        r.home.dir("code/other/.claude")
        r.home.file("code/app/.claude/settings.json", #"{"enabledPlugins":{"sanduhr-meters@inline":true}}"#)
        r.home.file("code/other/.claude/settings.local.json", #"{"enabledPlugins":{"sanduhr-meters@inline":false}}"#)
        r.home.file(".claude-work/.claude.json", #"{"projects":{"\#(project)":{},"\#(other)":{},"/nowhere":{}}}"#)
        let folder = r.home.at(".claude-work")
        try r.installer.install(.meters, folder: folder, python: "")
        try r.installer.switchOwnMod(on: false, folder: folder)

        let found = ModOverrides.find(key: IntegrationInstaller.ownModKey, folder: folder, home: r.home.path,
                                      managed: r.home.at("managed.json"))
        #expect(found == [ModOverride(source: .project(project), value: true),
                          ModOverride(source: .local(other), value: false)])
        #expect(ModOverrides.message(found, on: false, home: r.home.path)
            == "A project setting keeps it on in ~/code/app (.claude/settings.json).")
        #expect(ModOverrides.message(found, on: true, home: r.home.path)
            == "A project setting keeps it off in ~/code/other (.claude/settings.local.json).")
        #expect(ModOverrides.message([], on: false, home: r.home.path) == nil)

        r.home.file("managed.json", #"{"enabledPlugins":{"sanduhr-meters@inline":true}}"#)
        let managed = ModOverrides.find(key: IntegrationInstaller.ownModKey, folder: folder, home: r.home.path,
                                        managed: r.home.at("managed.json"))
        #expect(ModOverrides.message(managed, on: false, home: r.home.path)
            == "Your organization's managed settings keep it on.")
    }

    @Test func anUpdateWithANewCapabilityAsksAndOneWithoutDoesNot() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", Self.settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.switchOwnMod(on: true, folder: folder)
        let v1 = try #require(r.installer.ownModState(folder: folder).pinned)
        let scan: (String, String) -> [String] = { ModCapabilities.added(old: $0, new: $1, validate: { _ in nil }) }
        #expect(try r.installer.updateOwnMod(folder: folder, confirmed: false, added: scan) == .upToDate)

        // Version 2 runs programs, which version 1 didn't.
        r.ship(version: "2")
        let register = r.app.appendingPathComponent("mods/sanduhr-meters/hooks/register.tsx")
        try Data("await $.process.run(['date'])\n".utf8).write(to: register)
        let pinnedBytes = r.bytes(".claude/settings.json")
        #expect(try r.installer.updateOwnMod(folder: folder, confirmed: false, added: scan)
            == .needsConsent(added: ["runs programs"]))
        #expect(r.bytes(".claude/settings.json") == pinnedBytes)
        // The old version stays while its entry names it.
        #expect(FileManager.default.fileExists(atPath: r.scripts.pinnedModPath(v1)))

        #expect(try r.installer.updateOwnMod(folder: folder, confirmed: true, added: scan) == .updated)
        let v2 = try #require(r.scripts.bundledStamp)
        #expect(v2 != v1)
        #expect(dirs(r, ".claude/settings.json") == r.scripts.pinnedModPath(v2))
        #expect(r.installer.ownModState(folder: folder).pinned == v2)
        #expect(!FileManager.default.fileExists(atPath: r.scripts.pinnedModPath(v1)))

        // Version 3 changes a comment only: no question.
        r.ship(version: "3")
        try Data("// v3\nawait $.process.run(['date'])\n".utf8).write(to: register)
        #expect(try r.installer.updateOwnMod(folder: folder, confirmed: false, added: scan) == .updated)
        #expect(r.installer.ownModState(folder: folder).pinned == r.scripts.bundledStamp)

        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func pinnedVersionsSurviveARefreshForAnotherVersion() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{}")
        let folder = r.home.at(".claude")
        try r.installer.switchOwnMod(on: true, folder: folder)
        let v1 = try #require(r.installer.ownModState(folder: folder).pinned)
        r.ship(version: "2")
        try r.scripts.refresh()
        r.ship(version: "3")
        try r.scripts.refresh()
        #expect(FileManager.default.fileExists(atPath: r.scripts.pinnedModPath(v1)))
        // Integrations still sees it as installed: the Mods page updates it.
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        #expect(r.scripts.pinnedStamp(of: r.scripts.pinnedModPath(v1)) == v1)
        #expect(r.scripts.pinnedStamp(of: r.scripts.installedModPath) == nil)
    }

    @Test func theValidatorsCallsDecideWhenBothReportsAreThere() {
        var a = ModCheckReport()
        a.calls = ["$.fs.read", "$.ui.toast"]
        var b = a
        b.calls += ["$.http.fetch"]
        b.gating = ["tool.call"]
        let added = ModCapabilities.added(old: "/old", new: "/new", validate: { $0 == "/old" ? a : b })
        #expect(added == ["$.http.fetch", "gating tool.call"])
        #expect(ModCapabilities.added(old: "/old", new: "/new", validate: { _ in a }).isEmpty)
    }

    @Test func theInventoryMarksSanduhrsOwnMod() {
        let ours = ModItem(folder: "/f", name: "sanduhr-meters",
                           path: "/Users/x/Library/Application Support/Sanduhr/integrations/current/mods/sanduhr-meters",
                           origin: .pluginDirs, key: "sanduhr-meters@inline", isMod: true)
        let copy = ModItem(folder: "/f", name: "sanduhr-meters", path: "/Users/x/src/mods/sanduhr-meters",
                           origin: .pluginDirs, key: "sanduhr-meters@inline", isMod: true)
        let other = ModItem(folder: "/f", name: "watch", path: "/m/watch", origin: .pluginDirs)
        #expect(ours.ownership == .sanduhrs)
        #expect(copy.ownership == .copyOfSanduhrs)
        #expect(other.ownership == .others)
    }

    @Test func aSwitchReceiptWithoutCreatedParentStillReads() throws {
        let json = #"{"key":"sanduhr-meters@inline","value":false}"#.data(using: .utf8)!
        let r = try JSONDecoder().decode(SwitchReceipt.self, from: json)
        #expect(r.key == "sanduhr-meters@inline")
        #expect(r.value == false)
        #expect(r.createdParent == false)
        #expect(r.previous == nil)
    }
}
