import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice F2: a kept mod switched on and off per folder through the plugin folders list,
/// by receipt, undone byte for byte. On made-up homes only.
@Suite("Mod Studio, kept mods per folder")
struct KeptModSwitchTests {
    private func kept(_ r: IntegrationRig, version: String = "0.1.0") throws -> (KeptMods, KeptMod) {
        let root = "dev/ci-band"
        r.home.dir("\(root)/.claude-plugin")
        r.home.dir("\(root)/hooks")
        r.home.file("\(root)/.claude-plugin/plugin.json", #"{"name": "ci-band", "version": "\#(version)"}"#)
        r.home.file("\(root)/hooks/hooks.json", #"{"modules": ["./register.tsx"]}"#)
        r.home.file("\(root)/hooks/register.tsx", "// \(version)")
        let store = KeptMods(dir: URL(fileURLWithPath: r.home.at("Support/Sanduhr/mods")))
        return (store, try store.keep(r.home.at(root), session: "0123456789abcdef"))
    }

    private func list(_ r: IntegrationRig) -> [String] {
        let env = r.json(".claude/settings.json")["env"] as? [String: Any]
        return (env?["CLAUDE_CODE_PLUGIN_DIRS"] as? String)?.components(separatedBy: ":") ?? []
    }

    @Test func onAndOffRoundTripByteForByte() throws {
        for settings in ["{\n  \"model\": \"opus\"\n}\n", "{}\n", "{\"env\":{}}",
                         "{\n  \"env\": {\n    \"FOO\": \"1\"\n  }\n}\n",
                         "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/mine/a:/mine/b\"\n  }\n}\n"] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", settings)
            let folder = r.home.at(".claude")
            let (store, mod) = try kept(r)
            let before = r.snapshot()
            try store.turnOn(mod, folder: folder, installer: r.installer)
            #expect(list(r).last == store.path(mod), "\(settings)")
            #expect(store.state(mod, folder: folder, installer: r.installer) == .on)
            #expect(store.loadReceipts().map(\.name) == ["ci-band"])
            try store.turnOff(mod.name, folder: folder, installer: r.installer)
            #expect(r.snapshot() == before, "\(settings)")
            #expect(store.state(mod, folder: folder, installer: r.installer) == .off)
            #expect(store.loadReceipts().isEmpty)
        }
    }

    @Test func aNewVersionReplacesTheOldWhereItStands() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let settings = "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/mine/a\"\n  }\n}\n"
        r.home.file(".claude/settings.json", settings)
        let folder = r.home.at(".claude")
        let (store, one) = try kept(r)
        let before = r.snapshot()
        try store.turnOn(one, folder: folder, installer: r.installer)
        // The user adds a folder of their own after it.
        let file = r.home.at(".claude/settings.json")
        let added = try String(contentsOfFile: file, encoding: .utf8).replacingOccurrences(of: store.path(one), with: store.path(one) + ":/mine/c")
        try added.write(toFile: file, atomically: true, encoding: .utf8)
        let (_, two) = try kept(r, version: "0.2.0")
        #expect(store.state(two, folder: folder, installer: r.installer) == .otherVersion(one.stamp))
        try store.turnOn(two, folder: folder, installer: r.installer)
        #expect(list(r) == ["/mine/a", store.path(two), "/mine/c"])
        // Off keeps the user's own addition; with it gone too, the file is as it was.
        try store.turnOff(two.name, folder: folder, installer: r.installer)
        #expect(list(r) == ["/mine/a", "/mine/c"])
        let restored = try String(contentsOfFile: file, encoding: .utf8).replacingOccurrences(of: ":/mine/c", with: "")
        try restored.write(toFile: file, atomically: true, encoding: .utf8)
        #expect(r.snapshot()[".claude/settings.json"] == before[".claude/settings.json"])
    }

    @Test func sanduhrsOwnModAndOtherEntriesAreLeftAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\"\n}\n")
        let folder = r.home.at(".claude")
        try r.installer.install(.meters, folder: folder, python: "")
        let (store, mod) = try kept(r)
        try store.turnOn(mod, folder: folder, installer: r.installer)
        #expect(list(r) == [r.scripts.installedModPath, store.path(mod)])
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        try store.turnOff(mod.name, folder: folder, installer: r.installer)
        #expect(list(r) == [r.scripts.installedModPath])
    }

    @Test func onlyKeptCopiesCountAsEntries() throws {
        let store = KeptMods(dir: URL(fileURLWithPath: "/S/mods"))
        #expect(store.keptEntry(" /S/mods/ci-band/0123456789ab ")! == ("ci-band", "0123456789ab"))
        for other in ["/S/mods/ci-band", "/S/mods/ci-band/0123456789ab/x", "/S/mods/CI/0123456789ab",
                      "/S/mods/ci-band/not-a-stamp", "/elsewhere/ci-band/0123456789ab"] {
            #expect(store.keptEntry(other) == nil, "\(other)")
        }
    }
}
