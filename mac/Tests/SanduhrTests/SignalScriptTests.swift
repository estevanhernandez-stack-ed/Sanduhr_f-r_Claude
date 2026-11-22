import Foundation
import Testing
@testable import Sanduhr

/// The Claude Code hook's script (2.47): run for real with `osascript` against a temp folder and
/// made-up hook input. Never the installed command (no `pgrep`, no Darwin post), never Sanduhr's
/// real folder, and Sanduhr needn't be running.
@Suite("Claude Code hook, signal script")
struct SignalScriptTests {
    /// The script, pinned. A change here is a change to what every installed hook runs.
    static let pinned = "function run(argv){ObjC.import(\"Foundation\");var d=argv[0],v=argv[1],m=Date.now();"
        + "if([\"prompt\",\"waiting\",\"done\"].indexOf(v)<0)return;"
        + "var i=$.NSFileHandle.fileHandleWithStandardInput.readDataToEndOfFile;var j=null;"
        + "try{j=JSON.parse(ObjC.unwrap($.NSString.alloc.initWithDataEncoding(i,4)))}catch(x){}"
        + "if(!j||typeof j!==\"object\")j={};"
        + "var c=function(x,n){if(typeof x!==\"string\")return null;x=x.slice(0,n);"
        + "var k=x.charCodeAt(x.length-1);return k>=55296&&k<=56319?x.slice(0,-1):x};"
        + "var f=$.NSFileManager.defaultManager;"
        + "var w=function(t,p,o){var n=m+\"-\"+ObjC.unwrap($.NSUUID.UUID.UUIDString);var a=d+\"/.\"+p+n+t;"
        + "if(f.createFileAtPathContentsAttributes(a,$(JSON.stringify(o)).dataUsingEncoding(4),"
        + "$({NSFilePosixPermissions:384}))){f.moveItemAtPathToPathError(a,d+\"/\"+p+n+\".json\",null)}};"
        + "var l=ObjC.deepUnwrap(f.contentsOfDirectoryAtPathError(d,null))||[];"
        + "if(l.filter(function(x){return x.indexOf(\"cc-signal-\")===0&&x.slice(-5)===\".json\"}).length<100)"
        + "{w(\".json.tmp\",\"cc-signal-\",{v:1,event:v,cwd:c(j.cwd,1024),session:c(j.session_id,128),at:m})}"
        + "if(v!==\"done\")return;"
        + "var b=ObjC.unwrap($.NSString.stringWithContentsOfFileEncodingError(d+\"/watchers.json\",4,null));"
        + "if(typeof b!==\"string\"||b.indexOf(\"\\\"background\\\":true\")<0)return;"
        + "var t=(Array.isArray(j.background_tasks)?j.background_tasks:[])"
        + ".filter(function(x){return x&&typeof x===\"object\"}).slice(0,20)"
        + ".map(function(x){return{id:c(x.id,64),type:c(x.type,20),status:c(x.status,20),"
        + "description:c(x.description,120),name:c(x.name,60)}});"
        + "var e=ObjC.unwrap($.NSProcessInfo.processInfo.environment.objectForKey(\"CLAUDE_CONFIG_DIR\"));"
        + "w(\".tmp\",\"watch-stop-\",{schema_version:1,session:c(j.session_id,64),folder:typeof e===\"string\"?e:null,tasks:t})}"

    // MARK: Rig

    private final class Folder {
        let url: URL
        init() {
            url = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-signal-\(UUID().uuidString)")
            try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        }
        deinit { try? FileManager.default.removeItem(at: url) }
        var names: [String] { ((try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []).sorted() }
        func names(_ prefix: String) -> [String] { names.filter { $0.hasPrefix(prefix) } }
        func data(_ name: String) throws -> Data { try Data(contentsOf: url.appendingPathComponent(name)) }
        func write(_ name: String, _ text: String) {
            FileManager.default.createFile(atPath: url.appendingPathComponent(name).path, contents: Data(text.utf8))
        }
    }

    /// Runs the script as the hook does (`osascript -l JavaScript -e <script> <folder> <event>`),
    /// with `input` on stdin. Returns the exit status.
    @discardableResult
    private func run(_ folder: Folder, _ event: String, input: Data, configDir: String? = nil) throws -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        p.arguments = ["-l", "JavaScript", "-e", IntegrationInstaller.signalScript, folder.url.path, event]
        var env = ["PATH": "/usr/bin:/bin"]
        if let configDir { env["CLAUDE_CONFIG_DIR"] = configDir }
        p.environment = env
        let pipe = Pipe()
        p.standardInput = pipe
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        pipe.fileHandleForWriting.write(input)
        try pipe.fileHandleForWriting.close()
        p.waitUntilExit()
        return p.terminationStatus
    }

    private func input(_ o: [String: Any]) throws -> Data {
        try JSONSerialization.data(withJSONObject: o)
    }

    private func object(_ data: Data) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    /// A prompt event's input: the prompt itself rides along, and must never reach the file.
    private func promptInput(cwd: String, session: String = "6f1d2c3b-0a9e-4b8c-9d7e-112233445566") throws -> Data {
        try input(["session_id": session, "cwd": cwd, "hook_event_name": "UserPromptSubmit",
                   "transcript_path": "/Users/someone/.claude/projects/x/SECRET-TRANSCRIPT.jsonl",
                   "permission_mode": "default", "prompt": "SECRET-PROMPT: ship it, don't \"wait\""])
    }

    // MARK: The text

    @Test func theScriptIsPinned() {
        #expect(IntegrationInstaller.signalScript == Self.pinned)
    }

    /// It sits in single quotes in the command, and it reads only the folder, the session id and
    /// (on done) the background tasks: never the prompt, the last message, the transcript or a command.
    @Test func theScriptReadsOnlyWhatItKeeps() throws {
        let s = IntegrationInstaller.signalScript
        #expect(!s.contains("'"))
        for field in ["prompt\"]", "j.prompt", "last_assistant_message", "transcript", "command", "message"] {
            #expect(!s.contains(field), "\(field)")
        }
        let reads = Set(try NSRegularExpression(pattern: #"\bj\.([a-z_]+)"#)
            .matches(in: s, range: NSRange(s.startIndex..., in: s))
            .compactMap { Range($0.range(at: 1), in: s).map { String(s[$0]) } })
        #expect(reads == ["cwd", "session_id", "background_tasks"])
        #expect(s.contains(WatcherStore.stopPrefix) && s.contains(WatcherStore.switchFile))
        #expect(s.contains(ClaudeCodeSignalFile.namePrefix))
    }

    // MARK: The signal file

    /// Review Focus 2: a folder with spaces, quotes and non-ASCII, and a prompt in the input. The
    /// file holds exactly v, event, cwd, session and at, owner-only, under the name the watcher
    /// takes, and parses back to the same folder.
    @Test func aPromptWritesOnlyTheFolderAndTheSession() throws {
        let folder = Folder()
        let cwd = "/Users/someone/Prøjekt \"quoted\" café/日本"
        let before = Date()
        #expect(try run(folder, "prompt", input: try promptInput(cwd: cwd)) == 0)
        let names = folder.names
        #expect(names.count == 1)
        let name = try #require(names.first)
        #expect(ClaudeCodeSignalFile.isSignalName(name))
        #expect(!ClaudeCodeSignalFile.isTempName(name))
        let path = folder.url.appendingPathComponent(name).path
        let mode = (try FileManager.default.attributesOfItem(atPath: path)[.posixPermissions] as? NSNumber)?.intValue
        #expect(mode == 0o600)
        let data = try folder.data(name)
        let text = String(decoding: data, as: UTF8.self)
        #expect(!text.contains("SECRET"))
        let o = try object(data)
        #expect(Set(o.keys) == ["v", "event", "cwd", "session", "at"])
        #expect(o["v"] as? Int == 1)
        #expect(o["event"] as? String == "prompt")
        #expect(o["cwd"] as? String == cwd)
        let s = try #require(ClaudeCodeSignalFile.parse(data, now: Date()))
        #expect(s.event == "prompt" && s.cwd == cwd && s.session == "6f1d2c3b-0a9e-4b8c-9d7e-112233445566")
        #expect(s.at >= before.addingTimeInterval(-1) && s.at <= Date().addingTimeInterval(1))
        // The stamp in the name is the one in the file.
        let ms = (o["at"] as? NSNumber)?.int64Value ?? 0
        #expect(name.hasPrefix("cc-signal-\(ms)-"))
    }

    @Test func eachEventWritesItsOwnName() throws {
        let folder = Folder()
        for event in ["prompt", "waiting", "done"] {
            try run(folder, event, input: try promptInput(cwd: "/work/alpha"))
        }
        let events = try folder.names(ClaudeCodeSignalFile.namePrefix)
            .compactMap { ClaudeCodeSignalFile.parse(try folder.data($0), now: Date())?.event }
        #expect(events.sorted() == ["done", "prompt", "waiting"])
        // An event the hook never passes writes nothing.
        let other = Folder()
        try run(other, "stop", input: try promptInput(cwd: "/work/alpha"))
        try run(other, "", input: try promptInput(cwd: "/work/alpha"))
        #expect(other.names.isEmpty)
    }

    /// Clipped to what the watcher accepts: cwd 1024, session 128, never half a surrogate pair.
    @Test func longValuesAreClipped() throws {
        let folder = Folder()
        let cwd = "/" + String(repeating: "a", count: 1022) + "😀tail"   // the emoji straddles 1024
        try run(folder, "waiting", input: try input(["cwd": cwd, "session_id": String(repeating: "s", count: 300)]))
        let name = try #require(folder.names.first)
        let data = try folder.data(name)
        let o = try object(data)
        let kept = try #require(o["cwd"] as? String)
        #expect(kept == "/" + String(repeating: "a", count: 1022))
        #expect((o["session"] as? String)?.count == 128)
        let s = try #require(ClaudeCodeSignalFile.parse(data, now: Date()))
        #expect(s.cwd == kept)
        #expect(data.count < ClaudeCodeSignalFile.maxBytes)
    }

    /// Input that isn't what Claude Code sends still writes a signal, with no folder and no session
    /// (the watcher reads that as the `-` session): the turn's timing isn't lost.
    @Test func oddInputWritesASignalWithNoIdentity() throws {
        for bad in [Data("not json".utf8), Data(), Data("[1,2]".utf8),
                    try input(["cwd": 7, "session_id": ["x"]])] {
            let folder = Folder()
            try run(folder, "done", input: bad)
            let name = try #require(folder.names.first)
            let o = try object(try folder.data(name))
            #expect(o["cwd"] is NSNull && o["session"] is NSNull)
            let s = try #require(ClaudeCodeSignalFile.parse(try folder.data(name), now: Date()))
            #expect(s.session == ClaudeCodeSignalFile.noSession && s.cwd == nil)
        }
    }

    /// With 100 signal files waiting (a Sanduhr that stopped reading), it writes no more.
    @Test func itStopsAtAHundredWaitingFiles() throws {
        let folder = Folder()
        for k in 0..<99 { folder.write("cc-signal-1-\(k).json", "{}") }
        folder.write("cc-signal-old.json.tmp", "{}")       // not a waiting file
        folder.write(".cc-signal-2-x.json.tmp", "{}")      // nor this
        folder.write("watch-stop-1-x.json", "{}")          // nor this
        try run(folder, "prompt", input: try promptInput(cwd: "/work/alpha"))
        #expect(folder.names.filter(ClaudeCodeSignalFile.isSignalName).count == 100)
        try run(folder, "prompt", input: try promptInput(cwd: "/work/alpha"))
        try run(folder, "waiting", input: try promptInput(cwd: "/work/alpha"))
        #expect(folder.names.filter(ClaudeCodeSignalFile.isSignalName).count == 100)
        #expect(folder.names.filter(ClaudeCodeSignalFile.isTempName).count == 2)
    }

    // MARK: Done and item 66's report

    private func stopInput() throws -> Data {
        let url = try #require(Bundle.module.url(forResource: "stop-in-flight", withExtension: "json",
                                                 subdirectory: "Fixtures/watchers"))
        return try Data(contentsOf: url)
    }

    /// Without "Show Claude Code's background work" (no watchers.json, or it says false), done
    /// writes the signal and no report.
    @Test func doneWritesTheReportOnlyWithTheSwitch() throws {
        for switches in [nil, String(decoding: WatcherStore.switchesJSON(agents: true, background: false), as: UTF8.self)] {
            let folder = Folder()
            if let switches { folder.write(WatcherStore.switchFile, switches) }
            try run(folder, "done", input: try stopInput(), configDir: "/Users/someone/.claude-work")
            #expect(folder.names(WatcherStore.stopPrefix).isEmpty)
            #expect(folder.names(ClaudeCodeSignalFile.namePrefix).count == 1)
        }
        // Prompt and waiting never write it, switch or not.
        let on = Folder()
        on.write(WatcherStore.switchFile, String(decoding: WatcherStore.switchesJSON(agents: false, background: true), as: UTF8.self))
        try run(on, "prompt", input: try stopInput())
        try run(on, "waiting", input: try stopInput())
        #expect(on.names(WatcherStore.stopPrefix).isEmpty)
    }

    /// With the switch, done also writes item 66's report in exactly the shape WatcherRequest reads,
    /// from the same one read of stdin: the session, the Claude Code folder and the few task fields,
    /// owner-only, never the command or the conversation.
    @Test func doneWithTheSwitchWritesBothFromOneRead() throws {
        let folder = Folder()
        folder.write(WatcherStore.switchFile, String(decoding: WatcherStore.switchesJSON(agents: false, background: true), as: UTF8.self))
        try run(folder, "done", input: try stopInput(), configDir: "/Users/someone/.claude-work")
        let signal = try #require(folder.names(ClaudeCodeSignalFile.namePrefix).first)
        let s = try #require(ClaudeCodeSignalFile.parse(try folder.data(signal), now: Date()))
        #expect(s.event == "done" && s.cwd == "/Users/someone/app" && s.session == "6f1d2c3b-0a9e-4b8c-9d7e-112233445566")
        let reports = folder.names(WatcherStore.stopPrefix)
        #expect(reports.count == 1)
        let name = try #require(reports.first)
        #expect(name.hasSuffix(".json"))
        let path = folder.url.appendingPathComponent(name).path
        let mode = (try FileManager.default.attributesOfItem(atPath: path)[.posixPermissions] as? NSNumber)?.intValue
        #expect(mode == 0o600)
        let data = try folder.data(name)
        let text = String(decoding: data, as: UTF8.self)
        for secret in ["SECRET", "npm test", "command", "transcript", "last_assistant", "code-reviewer", "/Users/someone/app"] {
            #expect(!text.contains(secret), "leaked \(secret)")
        }
        let o = try object(data)
        #expect(Set(o.keys) == ["schema_version", "session", "folder", "tasks"])
        let r = try #require(WatcherRequest.decodeStop(data))
        #expect(r.folder == "/Users/someone/.claude-work")
        #expect(r.tasks.count == 4)
        #expect(r.session == "6f1d2c3b-0a9e-4b8c-9d7e-112233445566")
        // No temp file is left behind.
        #expect(!folder.names.contains { $0.hasPrefix(".") })
    }
}
