import Foundation
import Testing
@testable import Sanduhr

/// The meters mod's install (item 50): Sanduhr's mod folder joins `env.CLAUDE_CODE_PLUGIN_DIRS`
/// in a folder's settings.json, and Remove takes out exactly that. On made-up homes only.
@Suite("Integration installer, meters mod")
struct IntegrationMetersTests {
    private func dirs(_ r: IntegrationRig, _ relative: String) -> String? {
        (r.json(relative)["env"] as? [String: Any])?["CLAUDE_CODE_PLUGIN_DIRS"] as? String
    }

    @Test func createsEnvAndTheKeyThenRoundTripsByteForByte() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let settings = "{\n  \"model\": \"opus\",\n  \"permissions\": {\n    \"allow\": [\"Bash(ls:*)\"]\n  }\n}\n"
        r.home.file(".claude/settings.json", settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()

        #expect(r.installer.status(.meters, folder: folder) == .notInstalled)
        #expect(try r.installer.install(.meters, folder: folder, python: "") == .installed)
        let mod = r.support.path + "/current/mods/sanduhr-meters"
        #expect(r.text(".claude/settings.json") == """
        {
          "model": "opus",
          "permissions": {
            "allow": ["Bash(ls:*)"]
          },
          "env": {
            "CLAUDE_CODE_PLUGIN_DIRS": "\(mod)"
          }
        }

        """)
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        #expect(FileManager.default.fileExists(atPath: mod + "/.claude-plugin/plugin.json"))
        // Never in .claude.json.
        #expect(r.bytes(".claude.json") == nil)

        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
        #expect(r.installer.status(.meters, folder: folder) == .notInstalled)
        #expect(!FileManager.default.fileExists(atPath: r.support.path))
    }

    @Test func appendsToAnExistingListAndLeavesTheOtherModsAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-work/projects")
        let settings = "{\n  \"env\": {\n    \"FOO\": \"1\",\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/Users/x/mods/now-playing:~/mods/other\"\n  },\n  \"model\": \"opus\"\n}\n"
        r.home.file(".claude-work/settings.json", settings)
        let folder = r.home.at(".claude-work")
        let before = r.snapshot()
        let mod = r.support.path + "/current/mods/sanduhr-meters"

        try r.installer.install(.meters, folder: folder, python: "")
        #expect(dirs(r, ".claude-work/settings.json") == "/Users/x/mods/now-playing:~/mods/other:\(mod)")
        #expect((r.json(".claude-work/settings.json")["env"] as? [String: Any])?["FOO"] as? String == "1")
        #expect(r.text(".claude-work/settings.json.sanduhr-backup") == settings)
        // Installing again changes nothing.
        let installed = r.bytes(".claude-work/settings.json")
        try r.installer.install(.meters, folder: folder, python: "")
        #expect(r.bytes(".claude-work/settings.json") == installed)

        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func aMissingSettingsFileIsCreatedAndRemovedAgain() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-new/projects")
        let folder = r.home.at(".claude-new")
        let before = r.snapshot()
        try r.installer.install(.meters, folder: folder, python: "")
        #expect(dirs(r, ".claude-new/settings.json") == r.support.path + "/current/mods/sanduhr-meters")
        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func anEmptyEnvAndAnEnvWithoutTheKeyRoundTrip() throws {
        for settings in ["{\n  \"env\": {},\n  \"model\": \"opus\"\n}\n",
                         "{\"env\":{\"A\":\"b\"},\"model\":\"opus\"}",
                         "{\n  \"env\": {\n    \"A\": \"b\"\n  }\n}\n",
                         "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"\"\n  }\n}\n"] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", settings)
            let folder = r.home.at(".claude")
            let before = r.snapshot()
            try r.installer.install(.meters, folder: folder, python: "")
            #expect(r.installer.status(.meters, folder: folder) == .installed)
            #expect(dirs(r, ".claude/settings.json") == r.support.path + "/current/mods/sanduhr-meters")
            try r.installer.remove(.meters, folder: folder)
            #expect(r.snapshot() == before, "\(settings)")
        }
    }

    @Test func modsAddedAfterInstallSurviveRemove() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/a\"\n  }\n}\n")
        let folder = r.home.at(".claude")
        try r.installer.install(.meters, folder: folder, python: "")
        let mod = r.support.path + "/current/mods/sanduhr-meters"
        // The user adds a mod after Sanduhr's, and another env key.
        r.home.file(".claude/settings.json", "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/a:\(mod):/b\",\n    \"X\": \"1\"\n  }\n}\n")
        try r.installer.remove(.meters, folder: folder)
        #expect(r.text(".claude/settings.json") == "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/a:/b\",\n    \"X\": \"1\"\n  }\n}\n")
    }

    @Test func aKeySanduhrMadeStaysWhenTheUserAddedToIt() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\"\n}\n")
        let folder = r.home.at(".claude")
        try r.installer.install(.meters, folder: folder, python: "")
        let mod = r.support.path + "/current/mods/sanduhr-meters"
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\",\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"\(mod):/mine\"\n  }\n}\n")
        try r.installer.remove(.meters, folder: folder)
        #expect(dirs(r, ".claude/settings.json") == "/mine")
    }

    @Test func anOlderEntryIsUpdatedInPlaceAndACopyElsewhereIsTheirs() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let old = "/Users/x/Library/Application Support/Sanduhr/integrations/0123456789ab/mods/sanduhr-meters"
        let theirs = "/Users/x/Projects/Sanduhr/mac/integrations/mods/sanduhr-meters"
        r.home.file(".claude/settings.json", "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"/a:\(old):/b\"\n  }\n}\n")
        let folder = r.home.at(".claude")
        #expect(IntegrationInstaller.isOurModEntry(old))
        #expect(!IntegrationInstaller.isOurModEntry(theirs))
        #expect(r.installer.status(.meters, folder: folder) == .outdated)

        try r.installer.install(.meters, folder: folder, python: "")
        let mod = r.support.path + "/current/mods/sanduhr-meters"
        #expect(dirs(r, ".claude/settings.json") == "/a:\(mod):/b")
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        try r.installer.remove(.meters, folder: folder)
        #expect(dirs(r, ".claude/settings.json") == "/a:/b")

        r.home.file(".claude/settings.json", "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"\(theirs)\"\n  }\n}\n")
        #expect(r.installer.status(.meters, folder: folder) == .notInstalled)
    }

    @Test func anAppUpdateKeepsTheEntryAndTheFolderMovesBehindTheLink() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{}\n")
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.install(.meters, folder: folder, python: "")
        let installed = r.bytes(".claude/settings.json")
        r.ship(version: "2")
        #expect(r.installer.status(.meters, folder: folder) == .outdated)
        r.scripts.refreshIfInstalled()
        #expect(r.installer.status(.meters, folder: folder) == .installed)
        #expect(r.bytes(".claude/settings.json") == installed)
        #expect(try String(contentsOfFile: r.scripts.installedModPath + "/hooks/register.tsx", encoding: .utf8) == "// v2\n")
        try r.installer.remove(.meters, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func aMalformedEnvIsLeftAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"env\": [\"x\"]\n}\n")
        r.home.dir(".claude-b/projects")
        r.home.file(".claude-b/settings.json", "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": [\"/a\"]\n  }\n}\n")
        let before = r.snapshot()
        for f in [".claude", ".claude-b"] {
            let folder = r.home.at(f)
            #expect(r.installer.status(.meters, folder: folder) == .unreadable)
            #expect(throws: IntegrationInstaller.Failure.malformed(file: r.home.at("\(f)/settings.json"))) {
                try r.installer.install(.meters, folder: folder, python: "")
            }
        }
        #expect(r.snapshot() == before)
    }

    @Test func withoutTheModInTheAppNothingIsWritten() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        try FileManager.default.removeItem(at: r.app.appendingPathComponent(IntegrationScripts.modsName))
        r.home.dir(".claude/projects")
        let folder = r.home.at(".claude")
        #expect(throws: IntegrationInstaller.Failure.scriptsMissing) {
            try r.installer.install(.meters, folder: folder, python: "")
        }
        #expect(r.snapshot().isEmpty)
    }

    @Test func installingAllAndRemovingOneKeepsTheOthers() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\"\n}\n")
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        for kind in IntegrationKind.allCases { try r.installer.install(kind, folder: folder, python: r.python) }
        #expect(r.installer.installedCounts(folders: [folder]) == (1, 1, 1, 1))
        try r.installer.remove(.meters, folder: folder)
        #expect(r.json(".claude/settings.json")["statusLine"] != nil)
        #expect(r.json(".claude/settings.json")["env"] == nil)
        try r.installer.remove(.statusline, folder: folder)
        try r.installer.remove(.hooks, folder: folder)
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func receiptsWrittenBeforeTheModStillRead() throws {
        let old = #"[{"createdFile":false,"createdParent":false,"file":"/f/settings.json","folder":"/f","kind":"statusline"}]"#
        let receipts = try JSONDecoder().decode([IntegrationReceipt].self, from: Data(old.utf8))
        #expect(receipts.first?.createdKey == nil)
        #expect(receipts.first?.kind == .statusline)
    }
}
