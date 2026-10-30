import Foundation

/// One runner's log, followed (Windows' RunnerFollow). Docker sends the last lines first; lines
/// stamped more than `replayMargin` before the follow opened are that replay. They only rebuild
/// which job is open: a job that ended in the replay puts up no card, and a job still open when
/// the replay ends comes out once, with its real start. The replay ends at the first live line or
/// when the caller says so (`endReplay`, a moment after the stream opened, for an idle runner that
/// prints nothing new). When the replay ends with no job open the runner is idle, so `endReplay`
/// says so with an `.online`: a card left open by an earlier follow (the stream dropped mid-job and
/// the job ended meanwhile) then ends instead of staying running. Pure; the caller serializes calls.
struct RunnerFollow: Sendable {
    static let replayMargin: TimeInterval = 30

    private let opened: Date
    private var open: RunnerLine?
    private(set) var live = false

    init(opened: Date) { self.opened = opened }

    /// What the board should apply for `line`: nothing during the replay, the line itself once
    /// live (after the open job, when this line ends the replay).
    mutating func feed(_ line: RunnerLine) -> [RunnerLine] {
        if live { return [line] }
        if line.at >= opened - Self.replayMargin { return endReplay() + [line] }
        switch line {
        case .jobStarted: open = line
        case .jobEnded, .online: open = nil
        }
        return []
    }

    /// Ends the replay: the job still open, if any, once; with none open, the runner is idle
    /// (`.online`, stamped when the follow opened). Empty on every later call.
    mutating func endReplay() -> [RunnerLine] {
        if live { return [] }
        live = true
        defer { open = nil }
        return [open ?? .online(at: opened)]
    }
}
