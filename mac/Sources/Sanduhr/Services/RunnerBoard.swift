import Foundation

/// A runner's parsed log line applied to the watcher board (Windows' RunnerBoard). The board's own
/// runner operations (WatcherBoard.runnerStart and friends) take plain values; this maps a line to one.
extension WatcherBoard {
    @discardableResult
    mutating func applyRunner(key: String, runner: String, line: RunnerLine, now: Date) -> RunnerChange {
        switch line {
        case let .jobStarted(job, at): runnerStart(key: key, runner: runner, job: job, startedAt: at, now: now)
        case let .jobEnded(job, result, _): runnerEnd(key: key, job: job, result: result, now: now)
        case .online: runnerOnline(key: key, now: now)
        }
    }
}
