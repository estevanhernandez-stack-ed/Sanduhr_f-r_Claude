import Foundation
import Testing
@testable import Sanduhr

/// The notch glow hooks' install (item 51): Sanduhr's entries join `hooks.Notification` and
/// `hooks.Stop` in a folder's settings.json, and Remove takes out exactly those. On made-up
/// homes only.
@Suite("Integration installer, notch glow hooks")
struct IntegrationHooksTests {
    /// The hooks post a Darwin notification: never a launch, whichever Sanduhr runs hears it.
    private let waitingCommand = "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting || true"
    /// Item 66: the Stop hook also hands over the background work while a Sanduhr runs and
    /// watchers.json allows it, then posts.
    private let doneCommand = "d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && "
        + "/usr/bin/grep -qs '\"background\":true' \"$d/watchers.json\" && "
        + "/usr/bin/osascript -l JavaScript -e '" + IntegrationInstaller.stopTasksScript + "' \"$d\" >/dev/null 2>&1; "
        + "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.done || true"
    /// Item 51's commands, as installs before item 66 hold them (item 66 kept the waiting one).
    private let item51WaitingCommand = "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=waiting' || true"
    private let item51DoneCommand = "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=done' || true"
    /// Item 66's Stop command, which opened the link (and launched an app that wasn't running).
    private let item66DoneCommand = "/usr/bin/pgrep -xq Sanduhr && { d=\"$HOME/Library/Application Support/Sanduhr\"; "
        + "/usr/bin/grep -qs '\"background\":true' \"$d/watchers.json\" && "
        + "/usr/bin/osascript -l JavaScript -e '" + IntegrationInstaller.stopTasksScript + "' \"$d\" >/dev/null 2>&1; "
        + "/usr/bin/open -g 'sanduhr://claude-code?event=done'; } || true"
    /// A command as it sits inside a JSON string in the file.
    private func inJSON(_ command: String) -> String {
        let quoted = String(decoding: JSONEdit.quoted(command), as: UTF8.self)
        return String(quoted.dropFirst().dropLast())
    }
    private var doneJSON: String { inJSON(doneCommand) }

    /// A settings.json holding Sanduhr's two entries with these commands, as Install writes them.
    private func installed(waiting: String, done: String) -> String {
        """
        {
          "hooks": {
            "Notification": [
              {
                "matcher": "permission_prompt|idle_prompt|elicitation_dialog",
                "hooks": [
                  {
                    "type": "command",
                    "command": "\(inJSON(waiting))",
                    "async": true,
                    "timeout": 5
                  }
                ]
              }
            ],
            "Stop": [
              {
                "hooks": [
                  {
                    "type": "command",
                    "command": "\(inJSON(done))",
                    "async": true,
                    "timeout": 5
                  }
                ]
              }
            ]
          }
        }

        """
    }

    private func hooks(_ r: IntegrationRig, _ relative: String) -> [String: Any] {
        r.json(relative)["hooks"] as? [String: Any] ?? [:]
    }

    /// Installs, checks the file with `check`, removes and expects every byte back.
    private func roundTrip(_ settings: String?, check: (IntegrationRig, String) throws -> Void = { _, _ in }) throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        if let settings { r.home.file(".claude/settings.json", settings) }
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
        #expect(try r.installer.install(.hooks, folder: folder, python: "") == .installed)
        #expect(r.installer.status(.hooks, folder: folder) == .installed)
        try check(r, folder)
        try r.installer.remove(.hooks, folder: folder)
        #expect(r.snapshot() == before)
        #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
        #expect(!FileManager.default.fileExists(atPath: r.support.path))
    }

    @Test func theEntryShape() throws {
        try roundTrip("{\n  \"model\": \"opus\"\n}\n") { r, _ in
            #expect(r.text(".claude/settings.json") == """
            {
              "model": "opus",
              "hooks": {
                "Notification": [
                  {
                    "matcher": "permission_prompt|idle_prompt|elicitation_dialog",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "\(waitingCommand)",
                        "async": true,
                        "timeout": 5
                      }
                    ]
                  }
                ],
                "Stop": [
                  {
                    "hooks": [
                      {
                        "type": "command",
                        "command": "\(doneJSON)",
                        "async": true,
                        "timeout": 5
                      }
                    ]
                  }
                ]
              }
            }

            """)
            // The hooks need no scripts copied out of the app; only the install record.
            #expect(!FileManager.default.fileExists(atPath: r.support.path + "/current"))
            #expect(r.bytes(".claude.json") == nil)
        }
    }

    @Test func createsTheFileAndDeletesItAgain() throws {
        try roundTrip(nil) { r, _ in
            #expect((hooks(r, ".claude/settings.json")["Stop"] as? [Any])?.count == 1)
        }
    }

    @Test func anEmptyFileObjectRoundTrips() throws {
        try roundTrip("{}")
        try roundTrip("{ }\n")
        try roundTrip("{\n}\n")
    }

    @Test func anEmptyHooksObjectAndEmptyListsRoundTrip() throws {
        try roundTrip("{\n  \"hooks\": {}\n}\n")
        try roundTrip("{\n  \"hooks\": { }\n}\n")
        try roundTrip("{\n  \"hooks\": {\n    \"Notification\": [],\n    \"Stop\": [ ]\n  }\n}\n") { r, _ in
            let h = hooks(r, ".claude/settings.json")
            #expect((h["Notification"] as? [Any])?.count == 1)
            #expect((h["Stop"] as? [Any])?.count == 1)
        }
    }

    @Test func existingHooksStayAndOursGoLast() throws {
        let settings = """
        {
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "Bash",
                "hooks": [
                  { "type": "command", "command": "~/bin/check.sh" }
                ]
              }
            ],
            "Stop": [
              {
                "hooks": [
                  { "type": "command", "command": "say done" }
                ]
              }
            ]
          },
          "model": "opus"
        }

        """
        try roundTrip(settings) { r, _ in
            let h = hooks(r, ".claude/settings.json")
            #expect(h["PreToolUse"] != nil)
            let stop = h["Stop"] as? [[String: Any]] ?? []
            #expect(stop.count == 2)
            #expect(((stop.first?["hooks"] as? [[String: Any]])?.first?["command"] as? String) == "say done")
            #expect(IntegrationInstaller.isOurHookGroup(stop.last))
            #expect((h["Notification"] as? [Any])?.count == 1)
            #expect(r.json(".claude/settings.json")["model"] as? String == "opus")
            // The original is kept beside it.
            #expect(r.bytes(".claude/settings.json.sanduhr-backup") == Data(settings.utf8))
        }
    }

    @Test func compactFilesRoundTrip() throws {
        try roundTrip(#"{"hooks":{"Notification":[{"hooks":[{"type":"command","command":"afplay x.aiff"}]}]},"model":"opus"}"#)
        try roundTrip(#"{"model":"opus"}"#) { r, _ in
            let w = waitingCommand
            #expect(r.text(".claude/settings.json") == #"{"model":"opus","hooks":{"Notification":[{"matcher":"permission_prompt|idle_prompt|elicitation_dialog","hooks":[{"type":"command","command":""# + w + #"","async":true,"timeout":5}]}],"Stop":[{"hooks":[{"type":"command","command":""# + doneJSON + #"","async":true,"timeout":5}]}]}}"#)
        }
        try roundTrip("{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"a\"}]}, {\"hooks\":[{\"type\":\"command\",\"command\":\"b\"}]}]}}\n")
    }

    @Test func tabsAndFourSpacesRoundTrip() throws {
        try roundTrip("{\n\t\"hooks\": {\n\t\t\"Stop\": [\n\t\t\t{ \"hooks\": [] }\n\t\t]\n\t}\n}\n")
        try roundTrip("{\n    \"env\": {\n        \"A\": \"1\"\n    }\n}\n")
    }

    @Test func aGroupOfTheUsersThatAlsoRunsOurCommandIsTheirs() throws {
        let settings = """
        {
          "hooks": {
            "Stop": [
              {
                "hooks": [
                  { "type": "command", "command": "say done" },
                  { "type": "command", "command": "open -g 'sanduhr://claude-code?event=done'" }
                ]
              }
            ]
          }
        }

        """
        try roundTrip(settings) { r, _ in
            let stop = hooks(r, ".claude/settings.json")["Stop"] as? [[String: Any]] ?? []
            #expect(stop.count == 2)
            #expect(!IntegrationInstaller.isOurHookGroup(stop[0]))
            #expect(IntegrationInstaller.isOurHookGroup(stop[1]))
        }
    }

    @Test func anOlderEntryIsOutdatedAndUpdatedInPlace() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let old = """
        {
          "hooks": {
            "Stop": [
              {
                "hooks": [
                  { "type": "command", "command": "open -g 'sanduhr://claude-code?event=done'" }
                ]
              },
              {
                "hooks": [
                  { "type": "command", "command": "say done" }
                ]
              }
            ]
          }
        }

        """
        r.home.file(".claude/settings.json", old)
        let folder = r.home.at(".claude")
        #expect(r.installer.status(.hooks, folder: folder) == .outdated)
        try r.installer.install(.hooks, folder: folder, python: "")
        #expect(r.installer.status(.hooks, folder: folder) == .installed)
        let stop = hooks(r, ".claude/settings.json")["Stop"] as? [[String: Any]] ?? []
        #expect(stop.count == 2)
        // Rewritten where it stood, ahead of the user's own.
        #expect(((stop[0]["hooks"] as? [[String: Any]])?.first?["command"] as? String) == doneCommand)
        #expect(((stop[1]["hooks"] as? [[String: Any]])?.first?["command"] as? String) == "say done")
        // Installing again changes nothing.
        let once = r.bytes(".claude/settings.json")
        try r.installer.install(.hooks, folder: folder, python: "")
        #expect(r.bytes(".claude/settings.json") == once)
        // Remove takes out Sanduhr's entries and the list it made, and leaves the user's.
        try r.installer.remove(.hooks, folder: folder)
        let after = hooks(r, ".claude/settings.json")
        #expect(after["Notification"] == nil)
        let left = after["Stop"] as? [[String: Any]] ?? []
        #expect(left.count == 1)
        #expect(((left[0]["hooks"] as? [[String: Any]])?.first?["command"] as? String) == "say done")
    }

    /// Installs that opened `sanduhr://claude-code` (item 51's two commands, item 66's Stop
    /// command beside item 51's waiting one) are outdated: Install rewrites both commands in place,
    /// every other byte stays, installing again changes nothing, and Remove takes everything out.
    @Test func installsThatOpenedTheLinkAreOutdatedAndUpdatedInPlace() throws {
        for (waiting, done) in [(item51WaitingCommand, item51DoneCommand), (item51WaitingCommand, item66DoneCommand),
                                (waitingCommand, item66DoneCommand), (item51WaitingCommand, doneCommand)] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", installed(waiting: waiting, done: done))
            let folder = r.home.at(".claude")
            #expect(r.installer.status(.hooks, folder: folder) == .outdated)
            #expect(r.installer.installedCounts(folders: [folder]).3 == 1)
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.installer.status(.hooks, folder: folder) == .installed)
            #expect(r.text(".claude/settings.json") == installed(waiting: waitingCommand, done: doneCommand))
            #expect(r.text(".claude/settings.json")?.contains("sanduhr://") == false)
            let once = r.bytes(".claude/settings.json")
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.bytes(".claude/settings.json") == once)
            try r.installer.remove(.hooks, folder: folder)
            #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
            let h = hooks(r, ".claude/settings.json")
            #expect(((h["Notification"] as? [Any]) ?? []).isEmpty && ((h["Stop"] as? [Any]) ?? []).isEmpty)
        }
    }

    /// Remove without updating first takes out an old install's commands exactly, and leaves the
    /// user's own hooks around them.
    @Test func removeTakesOutAnOldInstallAsItIs() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let user = #"{ "hooks": [ { "type": "command", "command": "say done" } ] }"#
        let old = installed(waiting: item51WaitingCommand, done: item66DoneCommand)
            .replacingOccurrences(of: "\"Stop\": [\n", with: "\"Stop\": [\n      \(user),\n")
        r.home.file(".claude/settings.json", old)
        let folder = r.home.at(".claude")
        #expect(r.installer.status(.hooks, folder: folder) == .outdated)
        try r.installer.remove(.hooks, folder: folder)
        #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
        let text = r.text(".claude/settings.json") ?? ""
        #expect(!text.contains("sanduhr://") && !text.contains("notifyutil"))
        let stop = hooks(r, ".claude/settings.json")["Stop"] as? [[String: Any]] ?? []
        #expect(stop.count == 1)
        #expect(((stop[0]["hooks"] as? [[String: Any]])?.first?["command"] as? String) == "say done")
    }

    /// Neither command launches anything: no `open`, no link; each posts its own name.
    @Test func theCommandsPostANotificationAndNeverOpenALink() {
        for event in ClaudeCodeEvent.allCases {
            let command = IntegrationInstaller.hookCommand(event)
            #expect(command.hasSuffix("/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.\(event.rawValue) || true"))
            #expect(!command.contains("open"))
            #expect(!command.contains("sanduhr://"))
        }
        #expect(FileManager.default.isExecutableFile(atPath: "/usr/bin/notifyutil"))
        #expect(IntegrationInstaller.hookCommand(.waiting) == waitingCommand)
        #expect(IntegrationInstaller.hookCommand(.done) == doneCommand)
    }

    /// The Stop command's shape (item 66): the notification is always posted; the report needs
    /// a Sanduhr running and the background switch; no single quote inside the script.
    @Test func theStopCommandHandsOverBackgroundWorkOnlyWithTheSwitch() {
        let stop = IntegrationInstaller.hookCommand(.done)
        #expect(stop.hasPrefix("d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && /usr/bin/grep"))
        #expect(stop.hasSuffix(">/dev/null 2>&1; /usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.done || true"))
        #expect(stop.contains("/usr/bin/grep -qs '\"background\":true' \"$d/\(WatcherStore.switchFile)\" && /usr/bin/osascript"))
        #expect(!IntegrationInstaller.stopTasksScript.contains("'"))
        #expect(!IntegrationInstaller.stopTasksScript.contains("command"))
        #expect(!IntegrationInstaller.stopTasksScript.contains("last_assistant_message"))
        #expect(!IntegrationInstaller.stopTasksScript.contains("transcript"))
        // Both commands parse as shell (checked only: `sh -n` runs nothing).
        for event in ClaudeCodeEvent.allCases {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/bin/sh")
            p.arguments = ["-n", "-c", IntegrationInstaller.hookCommand(event)]
            p.standardError = FileHandle.nullDevice
            try? p.run()
            p.waitUntilExit()
            #expect(p.terminationStatus == 0, "\(event)")
        }
        #expect(IntegrationInstaller.hookCommand(.waiting) == waitingCommand)
        // The switch text the hook matches is exactly what Sanduhr writes.
        let on = String(decoding: WatcherStore.switchesJSON(agents: false, background: true), as: UTF8.self)
        #expect(on == #"{"agents":false,"background":true,"schema_version":1}"#)
        let off = String(decoding: WatcherStore.switchesJSON(agents: true, background: false), as: UTF8.self)
        #expect(!off.contains(#""background":true"#))
    }

    @Test func installTwiceThenRemoveIsStillByteForByte() throws {
        try roundTrip("{\n  \"hooks\": {}\n}\n") { r, folder in
            let once = r.bytes(".claude/settings.json")
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.bytes(".claude/settings.json") == once)
        }
    }

    @Test func otherHookEventsKeepTheirPlaceWhenOursComeOut() throws {
        let settings = "{\n  \"hooks\": {\n    \"Notification\": [\n      {\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"terminal-notifier -message hi\"\n          }\n        ]\n      }\n    ],\n    \"SessionStart\": []\n  }\n}\n"
        try roundTrip(settings) { r, _ in
            let h = hooks(r, ".claude/settings.json")
            #expect((h["Notification"] as? [Any])?.count == 2)
            #expect((h["SessionStart"] as? [Any])?.isEmpty == true)
            #expect((h["Stop"] as? [Any])?.count == 1)
        }
    }

    @Test func malformedFilesAndWrongTypesAreLeftAlone() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let folder = r.home.at(".claude")
        for bad in ["{\"hooks\": []}\n", "{\"hooks\": {\"Stop\": {}}}\n", "{\"hooks\": {\"Notification\": \"x\"}}\n",
                    "{\"hooks\": {},}\n", "not json"] {
            r.home.file(".claude/settings.json", bad)
            #expect(r.installer.status(.hooks, folder: folder) == .unreadable)
            #expect(throws: IntegrationInstaller.Failure.self) {
                try r.installer.install(.hooks, folder: folder, python: "")
            }
            #expect(r.text(".claude/settings.json") == bad)
            #expect(r.bytes(".claude/settings.json.sanduhr-backup") == nil)
        }
    }

    @Test func removeWithNothingOfOursChangesNothing() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        let settings = "{\n  \"hooks\": {\n    \"Stop\": [\n      { \"hooks\": [ { \"type\": \"command\", \"command\": \"say done\" } ] }\n    ]\n  }\n}\n"
        r.home.file(".claude/settings.json", settings)
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        try r.installer.remove(.hooks, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func allFourKindsCountAndComeOutIndependently() throws {
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", "{\n  \"model\": \"opus\"\n}\n")
        let folder = r.home.at(".claude")
        let before = r.snapshot()
        for kind in IntegrationKind.allCases { try r.installer.install(kind, folder: folder, python: r.python) }
        #expect(r.installer.installedCounts(folders: [folder]) == (1, 1, 1, 1))
        try r.installer.remove(.hooks, folder: folder)
        #expect(r.json(".claude/settings.json")["hooks"] == nil)
        #expect(r.json(".claude/settings.json")["statusLine"] != nil)
        #expect(r.installer.installedCounts(folders: [folder]) == (1, 1, 1, 0))
        try r.installer.install(.hooks, folder: folder, python: "")
        try r.installer.remove(.statusline, folder: folder)
        try r.installer.remove(.meters, folder: folder)
        try r.installer.remove(.mcp, folder: folder)
        #expect(r.installer.status(.hooks, folder: folder) == .installed)
        try r.installer.remove(.hooks, folder: folder)
        #expect(r.snapshot() == before)
    }

    @Test func theKindNeedsNoPythonAndNoScripts() {
        #expect(!IntegrationKind.hooks.needsPython)
        #expect(!IntegrationKind.hooks.needsScripts)
        #expect(IntegrationKind.hooks.title == "Claude Code glow hook")
        #expect(IntegrationKind.hooks.keyPath == "hooks.Notification and hooks.Stop")
    }

    @Test func recognizingOurCommand() {
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": waitingCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": doneCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": item51WaitingCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": item66DoneCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": "open sanduhr://claude-code?event=done"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "command", "command": "/usr/bin/notifyutil -p com.example.done"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "command", "command": "open sanduhr://settings"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "http", "command": waitingCommand]))
        #expect(!IntegrationInstaller.isOurHookGroup(["hooks": []]))
        #expect(!IntegrationInstaller.isOurHookGroup("x"))
        #expect(IntegrationInstaller.hookCommand(.waiting) == waitingCommand)
        #expect(IntegrationInstaller.hookCommand(.done) == doneCommand)
    }

    @Test func receiptsWrittenBeforeTheHooksStillRead() throws {
        let old = #"[{"createdFile":false,"createdParent":false,"file":"/f/settings.json","folder":"/f","kind":"meters","createdKey":true}]"#
        let receipts = try JSONDecoder().decode([IntegrationReceipt].self, from: Data(old.utf8))
        #expect(receipts.first?.hooks == nil)
        #expect(receipts.first?.kind == .meters)
    }
}
