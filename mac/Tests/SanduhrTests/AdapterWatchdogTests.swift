import Foundation
import Testing
@testable import Sanduhr

/// The now-playing adapter's watchdog, run for real through /usr/bin/perl with stand-in scripts:
/// it passes output and exit status through, and stops its child once its parent is gone.
@Suite("Adapter watchdog")
struct AdapterWatchdogTests {
    private func script(_ body: String) throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("watchdog-\(UUID().uuidString).pl")
        try body.write(to: url, atomically: true, encoding: .utf8)
        return url
    }

    private func run(_ arguments: [String]) throws -> (status: Int32, output: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/perl")
        p.arguments = arguments
        let out = Pipe()
        p.standardOutput = out
        try p.run()
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(decoding: data, as: UTF8.self))
    }

    @Test func outputAndExitStatusPassThrough() throws {
        let child = try script("$| = 1; print \"hello \", join(\",\", @ARGV), \"\\n\"; exit 3;")
        defer { try? FileManager.default.removeItem(at: child) }
        let result = try run(AdapterWatchdog.arguments([child.path, "a", "b"]))
        #expect(result.output == "hello a,b\n")
        #expect(result.status == 3)
    }

    @Test func theScriptCompiles() throws {
        #expect(try run(["-c", "-e", AdapterWatchdog.script]).status == 0)
    }

    /// A shell starts the watchdog in the background and exits at once, as Sanduhr would by
    /// crashing: the child is gone within the watchdog's two-second check.
    @Test func theChildStopsWhenTheParentIsGone() throws {
        let pidFile = FileManager.default.temporaryDirectory
            .appendingPathComponent("watchdog-\(UUID().uuidString).pid")
        let child = try script("open(my $f, '>', '\(pidFile.path)'); print $f $$; close $f; sleep 60;")
        defer {
            try? FileManager.default.removeItem(at: child)
            try? FileManager.default.removeItem(at: pidFile)
        }
        let sh = Process()
        sh.executableURL = URL(fileURLWithPath: "/bin/sh")
        // The shell lingers a moment so the watchdog records it as its parent before it goes.
        sh.arguments = ["-c", "/usr/bin/perl \"$@\" >/dev/null 2>&1 & sleep 0.5", "sh"]
            + AdapterWatchdog.arguments([child.path])
        try sh.run()
        sh.waitUntilExit()

        var pid: pid_t = 0
        for _ in 0..<40 where pid == 0 {
            Thread.sleep(forTimeInterval: 0.05)
            pid = pid_t((try? String(contentsOf: pidFile, encoding: .utf8)) ?? "") ?? 0
        }
        #expect(pid > 0)
        var alive = true
        for _ in 0..<50 where alive {
            Thread.sleep(forTimeInterval: 0.1)
            alive = kill(pid, 0) == 0
        }
        if alive { kill(pid, SIGKILL) }
        #expect(!alive)
    }
}
