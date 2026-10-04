import Foundation
import Testing
@testable import Sanduhr

extension IntegrationRig {
    var installer: IntegrationInstaller { IntegrationInstaller(home: home.path, scripts: scripts) }
}

private let claudeJSON = """
{
  "numStartups": 12,
  "projects": {
    "/Users/someone/p": {
      "allowedTools": []
    }
  },
  "mcpServers": {
    "other": {
      "type": "stdio",
      "command": "node",
      "args": ["x.js"]
    }
  },
  "oauthAccount": {
    "organizationUuid": "org-1"
  }
}

"""

private let settingsJSON = """
{
  "model": "opus",
  "permissions": {
    "allow": ["Bash(ls:*)"]
  }
}
"""

@Suite("Integration installer")
struct IntegrationInstallerTests {
    @Test func mcpInDefaultHomeGoesToTheClaudeJsonBesideItAndRoundTrips() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", claudeJSON)
        let folder = r.home.at(".claude")
        let before = r.snapshot()

        #expect(r.installer.status(.mcp, folder: folder) == .notInstalled)
        #expect(try r.installer.install(.mcp, folder: folder, python: r.python) == .installed)

        let root = r.json(".claude.json")
        let entry = (root["mcpServers"] as? [String: Any])?["sanduhr"] as? [String: Any]
        #expect(entry?["type"] as? String == "stdio")
        #expect(entry?["command"] as? String == r.python)
        #expect(entry?["args"] as? [String] == [r.support.path + "/current/sanduhr_mcp.py"])
        #expect(Set(entry?.keys.map { $0 } ?? []) == ["type", "command", "args"])
        // Every other key is as it was, and the other server stays.
        #expect((root["mcpServers"] as? [String: Any])?["other"] != nil)
        #expect(root["numStartups"] as? Int == 12)
        #expect(r.text(".claude.json")!.hasPrefix(String(claudeJSON.prefix(150))))
        // The backup holds the original; nothing else in the home changed.
        #expect(r.bytes(".claude.json.sanduhr-backup") == before[".claude.json"])
        var after = r.snapshot()
        after.removeValue(forKey: ".claude.json")
        after.removeValue(forKey: ".claude.json.sanduhr-backup")
        var rest = before
        rest.removeValue(forKey: ".claude.json")
        #expect(after == rest)
        #expect(r.installer.status(.mcp, folder: folder) == .installed)
        #expect(r.installer.installedFolders() == [folder])

        try r.installer.remove(.mcp, folder: folder)
        #expect(r.snapshot() == before)
        #expect(r.installer.status(.mcp, folder: folder) == .notInstalled)
        // Nothing installed anywhere: the scripts went too.
        #expect(!FileManager.default.fileExists(atPath: r.support.path))
    }

    @Test func configDirHomeKeepsItsClaudeJsonInsideAndAddsTheParentWhenMissing() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", claudeJSON)
        r.home.dir(".claude-work/projects")
        r.home.file(".claude-work/.claude.json", "{\n  \"numStartups\": 3\n}\n")
        let folder = r.home.at(".claude-work")
        let before = r.snapshot()

        try r.installer.install(.mcp, folder: folder, python: r.python)
        #expect(r.text(".claude-work/.claude.json") == """
        {
          "numStartups": 3,
          "mcpServers": {
            "sanduhr": {
              "type": "stdio",
              "command": "/bin/echo",
              "args": [
                "\(r.support.path)/current/sanduhr_mcp.py"
              ]
            }
          }
        }

        """)
        // The default home's ~/.claude.json was not touched.
        #expect(r.bytes(".claude.json") == before[".claude.json"])
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func aMissingFileIsCreatedAndRemovedAgain() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-new/projects")
        let folder = r.home.at(".claude-new")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python)
        try r.installer.install(.mcp, folder: folder, python: r.python)
        let sl = r.json(".claude-new/settings.json")["statusLine"] as? [String: Any]
        #expect(sl?["type"] as? String == "command")
        #expect(sl?["command"] as? String == IntegrationInstaller.statuslineCommand(
            python: r.python, script: r.support.path + "/current/sanduhr_statusline.py"))
        #expect(FileManager.default.fileExists(atPath: r.home.at(".claude-new/.claude.json")))
        #expect(!FileManager.default.fileExists(atPath: r.home.at(".claude-new/settings.json.sanduhr-backup")))
        try r.installer.remove(.statusline, folder: folder)
        // The MCP server is still installed, so the scripts stay.
        #expect(FileManager.default.fileExists(atPath: r.scripts.installedPath(IntegrationScripts.mcpScript)))
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func statuslineRoundTripsByteForByte() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", settingsJSON)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python)
        #expect(r.json(".claude/settings.json")["model"] as? String == "opus")
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
        // The statusline never goes to ~/.claude.json.
        #expect(r.bytes(".claude.json") == nil)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func someoneElsesStatuslineIsReplacedOnlyWhenAskedAndComesBack() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let mine = "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"~/bin/my-line.sh\",\n    \"padding\": 0\n  },\n  \"model\": \"opus\"\n}\n"
        r.home.file(".claude/settings.json", mine)
        let folder = r.home.at(".claude")
        let before = r.snapshot()

        #expect(r.installer.status(.statusline, folder: folder) == .other)
        #expect(r.installer.otherEntry(.statusline, folder: folder) == "~/bin/my-line.sh")
        #expect(try r.installer.install(.statusline, folder: folder, python: r.python)
                == .needsReplaceConsent(existing: "~/bin/my-line.sh"))
        #expect(r.snapshot() == before)

        try r.installer.install(.statusline, folder: folder, python: r.python, replaceOther: true)
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
        #expect(r.json(".claude/settings.json")["model"] as? String == "opus")
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func aStatuslineThatOnlyCallsTheScriptIsTheirs() {
        #expect(IntegrationInstaller.isOurStatusline(["command": "/usr/bin/python3 '/x/sanduhr_statusline.py'"]))
        #expect(IntegrationInstaller.isOurStatusline(["command": "python3 /x/sanduhr_statusline.py"]))
        #expect(!IntegrationInstaller.isOurStatusline(["command": "echo hi; python3 /x/sanduhr_statusline.py"]))
        #expect(!IntegrationInstaller.isOurStatusline(["command": "a | python3 /x/sanduhr_statusline.py"]))
        #expect(!IntegrationInstaller.isOurStatusline(["command": "my.sh"]))
        #expect(IntegrationInstaller.isOurMCP(["command": "python3", "args": ["/y/sanduhr_mcp.py"]]))
        #expect(!IntegrationInstaller.isOurMCP(["command": "node", "args": ["server.js"]]))
        #expect(IntegrationInstaller.shellQuoted("/usr/bin/python3") == "/usr/bin/python3")
        #expect(IntegrationInstaller.shellQuoted("/a b/it's") == "'/a b/it'\\''s'")
    }

    @Test func malformedConfigIsLeftUntouchedWithAClearError() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\",\n}\n")
        r.home.file(".claude.json", "{\"mcpServers\": [1, 2]}")
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        #expect(r.installer.status(.statusline, folder: folder) == .unreadable)
        #expect(r.installer.status(.mcp, folder: folder) == .unreadable)
        #expect(throws: IntegrationInstaller.Failure.malformed(file: r.home.at(".claude/settings.json"))) {
            try r.installer.install(.statusline, folder: folder, python: r.python)
        }
        #expect(throws: IntegrationInstaller.Failure.malformed(file: r.home.at(".claude.json"))) {
            try r.installer.install(.mcp, folder: folder, python: r.python)
        }
        #expect(throws: IntegrationInstaller.Failure.malformed(file: r.home.at(".claude/settings.json"))) {
            try r.installer.remove(.statusline, folder: folder)
        }
        #expect(r.snapshot() == before)
    }

    @Test func otherChangesAfterInstallSurviveRemove() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude.json", claudeJSON)
        let folder = r.home.at(".claude")
        try r.installer.install(.mcp, folder: folder, python: r.python)
        // Claude Code writes meanwhile: a counter and a new server after ours.
        var text = r.text(".claude.json")!
        text = text.replacingOccurrences(of: "\"numStartups\": 12", with: "\"numStartups\": 13")
        let after = text.range(of: "\"oauthAccount\"")!
        text.insert(contentsOf: "\"later\": true,\n  ", at: after.lowerBound)
        r.home.file(".claude.json", text)
        try r.installer.remove(.mcp, folder: folder)
        let expected = claudeJSON
            .replacingOccurrences(of: "\"numStartups\": 12", with: "\"numStartups\": 13")
            .replacingOccurrences(of: "\"oauthAccount\"", with: "\"later\": true,\n  \"oauthAccount\"")
        #expect(r.text(".claude.json") == expected)
        // The file isn't what the backup holds any more: the backup stays.
        #expect(r.text(".claude.json.sanduhr-backup") == claudeJSON)
    }

    @Test func reinstallAfterAnUpdateKeepsConfigsAndStillRoundTrips() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude-a/projects")
        r.home.file(".claude-a/settings.json", settingsJSON)
        let folder = r.home.at(".claude-a")
        let before = r.snapshot()
        try r.installer.install(.statusline, folder: folder, python: r.python)
        let installed = r.bytes(".claude-a/settings.json")
        let firstStamp = r.scripts.installedStamp

        // An app update ships new scripts: outdated until the launch refresh.
        r.ship(version: "2")
        #expect(r.installer.status(.statusline, folder: folder) == .outdated)
        r.scripts.refreshIfInstalled()
        #expect(r.scripts.installedStamp != firstStamp)
        #expect(r.installer.status(.statusline, folder: folder) == .installed)
        #expect(r.bytes(".claude-a/settings.json") == installed)
        #expect(try String(contentsOfFile: r.scripts.installedPath(IntegrationScripts.statuslineScript), encoding: .utf8)
                == "# sanduhr_statusline.py v2\n")

        // Install again (what the button does for an outdated entry): same bytes, same undo.
        try r.installer.install(.statusline, folder: folder, python: r.python)
        #expect(r.bytes(".claude-a/settings.json") == installed)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func anOlderScriptInstallIsUpdatedAndRemovedWhole() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        // What install.sh's `claude mcp add` leaves.
        let old = "{\n  \"mcpServers\": {\n    \"sanduhr\": {\n      \"type\": \"stdio\",\n      \"command\": \"/usr/bin/python3\",\n      \"args\": [\"/x/integrations/sanduhr_mcp.py\"],\n      \"env\": {}\n    }\n  },\n  \"n\": 1\n}\n"
        r.home.file(".claude.json", old)
        let folder = r.home.at(".claude")
        #expect(r.installer.status(.mcp, folder: folder) == .outdated)
        #expect(try r.installer.install(.mcp, folder: folder, python: r.python) == .installed)
        #expect(r.installer.status(.mcp, folder: folder) == .installed)
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.text(".claude.json") == "{\n  \"mcpServers\": {},\n  \"n\": 1\n}\n")
    }

    @Test func aSymlinkedConfigIsWrittenWhereItPoints() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.dir("dotfiles")
        r.home.file("dotfiles/settings.json", settingsJSON)
        try FileManager.default.createSymbolicLink(atPath: r.home.at(".claude/settings.json"),
                                                   withDestinationPath: r.home.at("dotfiles/settings.json"))
        let folder = r.home.at(".claude")
        try r.installer.install(.statusline, folder: folder, python: r.python)
        #expect((try? FileManager.default.destinationOfSymbolicLink(atPath: r.home.at(".claude/settings.json"))) != nil)
        #expect(r.json("dotfiles/settings.json")["statusLine"] != nil)
        try r.installer.remove(.statusline, folder: folder)
        #expect(r.text("dotfiles/settings.json") == settingsJSON)
    }

    @Test func withoutScriptsInTheAppNothingIsWritten() {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let folder = r.home.at(".claude")
        let installer = IntegrationInstaller(home: r.home.path, scripts: IntegrationScripts(source: nil, dir: r.support))
        #expect(throws: IntegrationInstaller.Failure.scriptsMissing) {
            try installer.install(.mcp, folder: folder, python: r.python)
        }
        #expect(r.snapshot().isEmpty)
    }
}
