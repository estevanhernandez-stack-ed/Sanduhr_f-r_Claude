import Foundation
import Testing
@testable import Sanduhr

/// The Claude Code hook's install (item 51, 2.47): Sanduhr's entries join `hooks.UserPromptSubmit`,
/// `hooks.Notification` and `hooks.Stop` in a folder's settings.json, and Remove takes out exactly
/// those. On made-up homes only.
@Suite("Integration installer, Claude Code hook")
struct IntegrationHooksTests {
    /// 2.47: each event runs the signal script while a Sanduhr runs (it writes the folder and
    /// session to a file, and on done item 66's report), then posts its Darwin notification.
    private func command(_ event: String) -> String {
        "d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && "
            + "/usr/bin/osascript -l JavaScript -e '" + SignalScriptTests.pinned + "' \"$d\" \(event) >/dev/null 2>&1; "
            + "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.\(event) || true"
    }
    private var promptCommand: String { command("prompt") }
    private var waitingCommand: String { command("waiting") }
    private var doneCommand: String { command("done") }

    /// 2.46's Stop script (item 66), as installed hooks hold it.
    static let v246StopScript = "function run(argv){ObjC.import(\"Foundation\");var d=argv[0];"
        + "var i=$.NSFileHandle.fileHandleWithStandardInput.readDataToEndOfFile;"
        + "var j=JSON.parse(ObjC.unwrap($.NSString.alloc.initWithDataEncoding(i,4)));"
        + "var s=function(v,n){return typeof v===\"string\"?v.slice(0,n):null};"
        + "var t=(Array.isArray(j.background_tasks)?j.background_tasks:[])"
        + ".filter(function(x){return x&&typeof x===\"object\"}).slice(0,20)"
        + ".map(function(x){return{id:s(x.id,64),type:s(x.type,20),status:s(x.status,20),"
        + "description:s(x.description,120),name:s(x.name,60)}});"
        + "var e=ObjC.unwrap($.NSProcessInfo.processInfo.environment.objectForKey(\"CLAUDE_CONFIG_DIR\"));"
        + "var o={schema_version:1,session:s(j.session_id,64),folder:typeof e===\"string\"?e:null,tasks:t};"
        + "var n=Date.now()+\"-\"+ObjC.unwrap($.NSUUID.UUID.UUIDString);var f=$.NSFileManager.defaultManager;"
        + "var p=d+\"/.watch-stop-\"+n+\".tmp\";"
        + "if(f.createFileAtPathContentsAttributes(p,$(JSON.stringify(o)).dataUsingEncoding(4),"
        + "$({NSFilePosixPermissions:384}))){f.moveItemAtPathToPathError(p,d+\"/watch-stop-\"+n+\".json\",null)}}"
    /// 2.46's commands (since 0fdc7bc0): a bare post for waiting, and the Stop command that gated
    /// item 66's report on a shell grep of watchers.json. No prompt entry.
    private let v246WaitingCommand = "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting || true"
    private let v246DoneCommand = "d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && "
        + "/usr/bin/grep -qs '\"background\":true' \"$d/watchers.json\" && "
        + "/usr/bin/osascript -l JavaScript -e '" + IntegrationHooksTests.v246StopScript + "' \"$d\" >/dev/null 2>&1; "
        + "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.done || true"
    /// Item 51's commands, as installs before item 66 hold them (item 66 kept the waiting one).
    private let item51WaitingCommand = "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=waiting' || true"
    private let item51DoneCommand = "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=done' || true"
    /// Item 66's Stop command, which opened the link (and launched an app that wasn't running).
    private let item66DoneCommand = "/usr/bin/pgrep -xq Sanduhr && { d=\"$HOME/Library/Application Support/Sanduhr\"; "
        + "/usr/bin/grep -qs '\"background\":true' \"$d/watchers.json\" && "
        + "/usr/bin/osascript -l JavaScript -e '" + IntegrationHooksTests.v246StopScript + "' \"$d\" >/dev/null 2>&1; "
        + "/usr/bin/open -g 'sanduhr://claude-code?event=done'; } || true"
    /// A command as it sits inside a JSON string in the file.
    private func inJSON(_ command: String) -> String {
        let quoted = String(decoding: JSONEdit.quoted(command), as: UTF8.self)
        return String(quoted.dropFirst().dropLast())
    }
    private var doneJSON: String { inJSON(doneCommand) }

    /// A settings.json holding Sanduhr's entries with these commands, as Install writes them (2.46
    /// and before wrote no prompt entry).
    private func installed(prompt: String? = nil, waiting: String, done: String) -> String {
        let promptList = prompt.map {
            "\n    \"UserPromptSubmit\": [\n      {\n        \"hooks\": [\n          {\n"
                + "            \"type\": \"command\",\n            \"command\": \"\(inJSON($0))\",\n"
                + "            \"async\": true,\n            \"timeout\": 5\n          }\n        ]\n      }\n    ],"
        } ?? ""
        return """
        {
          "hooks": {\(promptList)
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
            let want = installed(prompt: promptCommand, waiting: waitingCommand, done: doneCommand)
                .replacingOccurrences(of: "{\n  \"hooks\"", with: "{\n  \"model\": \"opus\",\n  \"hooks\"")
            #expect(r.text(".claude/settings.json") == want)
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
            let w = inJSON(waitingCommand)
            #expect(r.text(".claude/settings.json") == #"{"model":"opus","hooks":{"UserPromptSubmit":[{"hooks":[{"type":"command","command":""# + inJSON(promptCommand) + #"","async":true,"timeout":5}]}],"Notification":[{"matcher":"permission_prompt|idle_prompt|elicitation_dialog","hooks":[{"type":"command","command":""# + w + #"","async":true,"timeout":5}]}],"Stop":[{"hooks":[{"type":"command","command":""# + doneJSON + #"","async":true,"timeout":5}]}]}}"#)
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
        #expect(after["Notification"] == nil && after["UserPromptSubmit"] == nil)
        let left = after["Stop"] as? [[String: Any]] ?? []
        #expect(left.count == 1)
        #expect(((left[0]["hooks"] as? [[String: Any]])?.first?["command"] as? String) == "say done")
    }

    /// The commands of one event's list in a settings file, Sanduhr's or not.
    private func commands(_ r: IntegrationRig, _ event: String) -> [String] {
        (hooks(r, ".claude/settings.json")[event] as? [[String: Any]] ?? [])
            .flatMap { ($0["hooks"] as? [[String: Any]] ?? []).compactMap { $0["command"] as? String } }
    }

    /// Every older generation: item 51's two link commands, item 66's link Stop command beside
    /// either waiting one, and 2.46's bare post and shell-grep Stop. Each reads as outdated (it has
    /// no prompt entry, and its commands aren't today's); Install rewrites both commands in place and
    /// adds the prompt entry, installing again changes nothing, and Remove takes everything out.
    private var generations: [(waiting: String, done: String)] {
        [(item51WaitingCommand, item51DoneCommand), (item51WaitingCommand, item66DoneCommand),
         (v246WaitingCommand, item66DoneCommand), (v246WaitingCommand, v246DoneCommand),
         (item51WaitingCommand, v246DoneCommand)]
    }

    @Test func everyOlderGenerationIsOutdatedAndUpdatedInPlace() throws {
        for (waiting, done) in generations {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            let old = installed(waiting: waiting, done: done)
            r.home.file(".claude/settings.json", old)
            let folder = r.home.at(".claude")
            #expect(r.installer.status(.hooks, folder: folder) == .outdated)
            #expect(r.installer.installedCounts(folders: [folder]).3 == 1)
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.installer.status(.hooks, folder: folder) == .installed)
            #expect(commands(r, "UserPromptSubmit") == [promptCommand])
            #expect(commands(r, "Notification") == [waitingCommand])
            #expect(commands(r, "Stop") == [doneCommand])
            let text = r.text(".claude/settings.json") ?? ""
            #expect(!text.contains("sanduhr://") && !text.contains("/usr/bin/grep"))
            // The two old commands are rewritten where they stood: the start of the file is as it was.
            let head = String(old.prefix(while: { $0 != "[" }))
            #expect(text.hasPrefix(head))
            let once = r.bytes(".claude/settings.json")
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.bytes(".claude/settings.json") == once)
            try r.installer.remove(.hooks, folder: folder)
            #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
            let h = hooks(r, ".claude/settings.json")
            #expect(((h["Notification"] as? [Any]) ?? []).isEmpty && ((h["Stop"] as? [Any]) ?? []).isEmpty)
            #expect(h["UserPromptSubmit"] == nil)
        }
    }

    /// Remove without updating first takes out each older generation exactly.
    @Test func removeTakesOutEveryOlderGenerationAsItIs() throws {
        for (waiting, done) in generations {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", installed(waiting: waiting, done: done))
            let folder = r.home.at(".claude")
            try r.installer.remove(.hooks, folder: folder)
            #expect(r.installer.status(.hooks, folder: folder) == .notInstalled)
            #expect(commands(r, "Notification").isEmpty && commands(r, "Stop").isEmpty)
        }
    }

    /// A 2.47 install missing one event's entry (a hand edit), or holding today's commands under
    /// 2.46's Stop, is outdated too and Install completes it.
    @Test func aPartOfTodaysInstallIsOutdated() throws {
        for file in [installed(waiting: waitingCommand, done: doneCommand),
                     installed(prompt: promptCommand, waiting: waitingCommand, done: v246DoneCommand),
                     installed(prompt: promptCommand, waiting: v246WaitingCommand, done: doneCommand)] {
            let r = IntegrationRig()
            defer { r.cleanUp() }
            r.home.dir(".claude/projects")
            r.home.file(".claude/settings.json", file)
            let folder = r.home.at(".claude")
            #expect(r.installer.status(.hooks, folder: folder) == .outdated)
            try r.installer.install(.hooks, folder: folder, python: "")
            #expect(r.installer.status(.hooks, folder: folder) == .installed)
        }
        let r = IntegrationRig()
        defer { r.cleanUp() }
        r.home.dir(".claude/projects")
        r.home.file(".claude/settings.json", installed(prompt: promptCommand, waiting: waitingCommand, done: doneCommand))
        #expect(r.installer.status(.hooks, folder: r.home.at(".claude")) == .installed)
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

    /// No command launches anything: no `open`, no link; each posts its own name, last, whatever
    /// the script did.
    @Test func theCommandsPostANotificationAndNeverOpenALink() {
        #expect(IntegrationInstaller.hookEvents.map(\.name) == ["UserPromptSubmit", "Notification", "Stop"])
        #expect(IntegrationInstaller.hookEvents.map(\.event) == [.prompt, .waiting, .done])
        for event in ClaudeCodeEvent.allCases {
            let command = IntegrationInstaller.hookCommand(event)
            #expect(command.hasSuffix(">/dev/null 2>&1; /usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.\(event.rawValue) || true"))
            #expect(!command.contains("open"))
            #expect(!command.contains("sanduhr://"))
        }
        #expect(FileManager.default.isExecutableFile(atPath: "/usr/bin/notifyutil"))
        #expect(IntegrationInstaller.hookCommand(.prompt) == promptCommand)
        #expect(IntegrationInstaller.hookCommand(.waiting) == waitingCommand)
        #expect(IntegrationInstaller.hookCommand(.done) == doneCommand)
    }

    /// The commands' shape (2.47): the script runs only while a Sanduhr runs (`pgrep`, so no file
    /// waits for a later launch), with the folder and the event; the switch check for item 66's
    /// report moved into the script (one read of stdin), so no shell grep; the post always happens.
    @Test func theScriptRunsOnlyWhileSanduhrRuns() {
        for event in ClaudeCodeEvent.allCases {
            let c = IntegrationInstaller.hookCommand(event)
            #expect(c.hasPrefix("d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && /usr/bin/osascript -l JavaScript -e '"))
            #expect(c.contains("' \"$d\" \(event.rawValue) >/dev/null 2>&1; "))
            #expect(!c.contains("grep -qs"))
            // The script is the only thing in single quotes.
            #expect(c.components(separatedBy: "'").count == 3)
            // Each parses as shell (checked only: `sh -n` runs nothing).
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/bin/sh")
            p.arguments = ["-n", "-c", c]
            p.standardError = FileHandle.nullDevice
            try? p.run()
            p.waitUntilExit()
            #expect(p.terminationStatus == 0, "\(event)")
        }
        // The switch text the script matches is exactly what Sanduhr writes.
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
        #expect(IntegrationKind.hooks.title == "Claude Code hook")
        #expect(IntegrationKind.hooks.keyPath == "hooks.UserPromptSubmit, hooks.Notification and hooks.Stop")
    }

    @Test func recognizingOurCommand() {
        for c in [promptCommand, waitingCommand, doneCommand, v246WaitingCommand, v246DoneCommand] {
            #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": c]))
        }
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": item51WaitingCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": item66DoneCommand]))
        #expect(IntegrationInstaller.isOurHookCommand(["type": "command", "command": "open sanduhr://claude-code?event=done"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "command", "command": "/usr/bin/notifyutil -p com.example.done"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "command", "command": "open sanduhr://settings"]))
        #expect(!IntegrationInstaller.isOurHookCommand(["type": "http", "command": waitingCommand]))
        #expect(!IntegrationInstaller.isOurHookGroup(["hooks": []]))
        #expect(!IntegrationInstaller.isOurHookGroup("x"))
        #expect(IntegrationInstaller.hookCommand(.prompt) == promptCommand)
    }

    /// The Install question says exactly what the hook reads and keeps (2.47): Claude Code's input,
    /// of which only the folder and the session id are kept, in a file Sanduhr deletes as it reads it.
    @Test func theConsentCopySaysWhatTheHookReadsAndKeeps() {
        #expect(HooksConsentCopy.lead("~/.claude") == "Claude Code sessions using ~/.claude tell Sanduhr when you send a prompt, when they wait on you (a permission prompt or a question) and when a turn finishes, so Sanduhr can time the turn, notify you and glow the notch.")
        #expect(HooksConsentCopy.keeps == "The hook reads what Claude Code hands it, which includes your prompt, and keeps only the session's folder and its session id. It writes those two to a small file in Sanduhr's folder, which Sanduhr deletes as it reads it, and nothing else. Nothing leaves this Mac, and while Sanduhr isn't running the hook writes nothing.")
        #expect(HooksConsentCopy.background == "Only with Watchers, Show Claude Code's background work on: when a turn finishes, the hook also hands Sanduhr the session's background tasks (each one's kind, status and short description, and the Claude Code folder), never a command. Sanduhr reads that and deletes it at once.")
        #expect(HooksConsentCopy.writes(file: "~/.claude/settings.json") == "Sanduhr adds one entry to each of hooks.UserPromptSubmit, hooks.Notification and hooks.Stop in ~/.claude/settings.json, keeping every hook already there, and keeps a copy of the file as it was, with .sanduhr-backup added to its name. Remove takes out only those three entries.")
    }

    @Test func receiptsWrittenBeforeTheHooksStillRead() throws {
        let old = #"[{"createdFile":false,"createdParent":false,"file":"/f/settings.json","folder":"/f","kind":"meters","createdKey":true}]"#
        let receipts = try JSONDecoder().decode([IntegrationReceipt].self, from: Data(old.utf8))
        #expect(receipts.first?.hooks == nil)
        #expect(receipts.first?.kind == .meters)
    }
}
