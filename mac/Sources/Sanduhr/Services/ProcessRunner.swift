import Foundation

/// The one way the app runs a command line tool and waits for it: `claude` (the CLI and Check),
/// git (the marketplace), ditto (packs). Stdout and stderr drain on their own threads, so a child
/// that fills either pipe never blocks; stdin is closed, or fed `stdin` and then closed. A run
/// past `timeout` is terminated, then killed after `grace` if it ignored that, and reports
/// `timedOut` with its output dropped. Blocks the calling thread: call it off the main one.
enum ProcessRunner {
    /// How long a stopped run gets to exit on SIGTERM before SIGKILL.
    static let grace: TimeInterval = 1

    struct Result: Equatable, Sendable {
        var status: Int32
        var stdout: String
        var stderr: String
        /// It ran past `timeout` and was stopped; `status` is -1 and the output is empty.
        var timedOut = false
        /// It never started (no such file, not executable); `status` is -1.
        var launchFailed = false

        var ok: Bool { status == 0 && !timedOut && !launchFailed }
    }

    /// Runs `executable` with `arguments`, in `cwd` (nil: this process's) with `environment`
    /// (nil: this process's), and waits up to `timeout` seconds for it to finish.
    static func run(_ executable: String, _ arguments: [String], cwd: URL? = nil,
                    environment: [String: String]? = nil, stdin: Data? = nil,
                    timeout: TimeInterval) -> Result {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: executable)
        p.arguments = arguments
        if let cwd { p.currentDirectoryURL = cwd }
        if let environment { p.environment = environment }
        let output = Pipe(), errors = Pipe()
        let input = stdin == nil ? nil : Pipe()
        p.standardInput = input ?? FileHandle.nullDevice
        p.standardOutput = output
        p.standardError = errors
        let box = Captured()
        let done = DispatchGroup()
        do { try p.run() } catch { return Result(status: -1, stdout: "", stderr: "", launchFailed: true) }
        if let input, let stdin {
            // Off this thread: a child that reads nothing would otherwise stall the write.
            DispatchQueue.global(qos: .userInitiated).async {
                try? input.fileHandleForWriting.write(contentsOf: stdin)
                try? input.fileHandleForWriting.close()
            }
        }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async {
            box.err = errors.fileHandleForReading.readDataToEndOfFile()
            done.leave()
        }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async {
            box.out = output.fileHandleForReading.readDataToEndOfFile()
            done.leave()
        }
        if done.wait(timeout: .now() + timeout) == .timedOut {
            p.terminate()
            if done.wait(timeout: .now() + grace) == .timedOut { kill(p.processIdentifier, SIGKILL) }
            p.waitUntilExit()
            return Result(status: -1, stdout: "", stderr: "", timedOut: true)
        }
        p.waitUntilExit()
        return Result(status: p.terminationStatus, stdout: String(decoding: box.out, as: UTF8.self),
                      stderr: String(decoding: box.err, as: UTF8.self))
    }

    /// What the drain threads fill; read only after the group is done.
    private final class Captured: @unchecked Sendable {
        var out = Data()
        var err = Data()
    }
}
