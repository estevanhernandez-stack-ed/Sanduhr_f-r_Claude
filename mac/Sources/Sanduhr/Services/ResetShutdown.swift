import Foundation

/// Reset Sanduhr… is running or has run in this process (Settings v2, slice 5). From the moment
/// the sheet's button is pressed until Sanduhr quits, every writer that would put a file back in
/// `~/Library/Application Support/Sanduhr/` or `Desk/` (the folder watchers' re-arm, the hand-off
/// files, history, snapshots, seat files, mcp-access.json, the vault, the band, messages.txt's
/// starter) does nothing. The reset's own steps (the installer, the credential stores) are not
/// gated: they are the reset.
///
/// `begin()` is the app's switch and is never turned back off. Tests use `$testOverride` instead,
/// which holds only inside its `withValue` scope, so a test can't switch writers off for the
/// tests running beside it.
enum ResetGate {
    private static let lock = NSLock()
    private static var started = false

    @TaskLocal static var testOverride = false

    static var isOn: Bool {
        if testOverride { return true }
        lock.lock(); defer { lock.unlock() }
        return started
    }

    /// The sheet's Reset and Quit, before anything is stopped or removed.
    static func begin() {
        lock.lock(); started = true; lock.unlock()
    }
}

/// Everything that keeps working on its own while Sanduhr runs, stopped before a reset so it
/// neither writes back what the reset removes nor keeps a removed account's key in memory. Each
/// entry stops one service and says whether it is stopped; `run()` stops them in order and
/// returns the names of any that didn't stop.
struct ResetShutdown {
    struct Step {
        let name: String
        let stop: () -> Void
        let isStopped: () -> Bool
    }

    var steps: [Step]

    @discardableResult
    func run() -> [String] {
        for s in steps { s.stop() }
        return steps.filter { !$0.isStopped() }.map(\.name)
    }
}
