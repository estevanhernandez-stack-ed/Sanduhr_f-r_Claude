import Foundation
import Testing
@testable import Sanduhr

/// The one runner behind `claude`, git and ditto: output captured apart, a timeout that stops the
/// child, a launch that fails reported as such, stdin delivered.
@Suite("Running a tool")
struct ProcessRunnerTests {
    @Test func stdoutAndStatusComeBack() {
        let r = ProcessRunner.run("/bin/echo", ["hi"], timeout: 5)
        #expect(r.status == 0 && r.ok)
        #expect(r.stdout == "hi\n" && r.stderr.isEmpty)
        #expect(!r.timedOut && !r.launchFailed)
    }

    @Test func stderrIsKeptApartFromStdout() {
        let r = ProcessRunner.run("/bin/sh", ["-c", "echo err 1>&2; exit 3"], timeout: 5)
        #expect(r.status == 3 && !r.ok)
        #expect(r.stdout.isEmpty && r.stderr == "err\n")
    }

    @Test func aSlowChildIsStoppedAtTheTimeout() {
        let start = Date()
        let r = ProcessRunner.run("/bin/sleep", ["5"], timeout: 0.3)
        #expect(r.timedOut && !r.ok && r.status == -1)
        #expect(Date().timeIntervalSince(start) < 2)
    }

    @Test func aMissingExecutableFailsToLaunchInsteadOfHanging() {
        let r = ProcessRunner.run("/nonexistent/tool", [], timeout: 5)
        #expect(r.launchFailed && !r.ok && r.status == -1)
        #expect(!r.timedOut)
    }

    @Test func stdinReachesTheChild() {
        let r = ProcessRunner.run("/bin/cat", [], stdin: Data("from stdin\n".utf8), timeout: 5)
        #expect(r.status == 0 && r.stdout == "from stdin\n")
    }

    @Test func environmentAndFolderAreTheChildsOwn() {
        let r = ProcessRunner.run("/bin/sh", ["-c", "echo $RUNNER_PROBE; /bin/pwd -P"], cwd: URL(fileURLWithPath: "/usr/bin"),
                                  environment: ["RUNNER_PROBE": "set", "PATH": "/usr/bin:/bin"], timeout: 5)
        #expect(r.ok)
        #expect(r.stdout == "set\n/usr/bin\n")
    }
}
