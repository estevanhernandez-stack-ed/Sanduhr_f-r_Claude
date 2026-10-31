import Foundation

// Live now playing bars (spec 14): the pure half. What the tap needs to run, the protocol the real
// Core Audio tap (CoreAudioProcessTap.swift, the only file that touches Core Audio) and the tests'
// fake both implement, the controller that starts and stops it, and the inference that tells a
// denied permission from a quiet song. Nothing here records, keeps or sends audio: the only thing
// that ever leaves the tap is one peak, a Float between 0 and 1.

/// The settings key for the switch (UserDefaults.desk). Off by default. Turning it on is the only
/// moment macOS can show the System Audio Recording prompt.
enum AudioBarsPrefs {
    static let levelKey = "nowPlaying.barsLevel"
    /// System Settings, Privacy & Security, Screen & System Audio Recording. Apple documents no
    /// anchor for the audio-only list, so this opens the pane that holds it. Never private TCC calls.
    static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!
    /// The line that offers it, on the Desk and in Settings.
    static let allowTitle = "Allow in System Settings"
}

/// A level source for one app's output. The real one is a Core Audio process tap; tests use a fake.
/// Called from the main thread; `takePeak` and the wake handler cross to the audio thread inside.
protocol AudioLevelTap: AnyObject {
    /// Taps `pid` only. False when the tap could not be built (no such audio process, macOS older
    /// than 14.2, Core Audio refused). Never throws and never retries on its own.
    func start(pid: Int) -> Bool
    /// Ends everything the tap built: IO, aggregate device and tap. Safe to call when stopped.
    func stop()
    /// The largest absolute sample since the last call (0 to 1), then zero again.
    func takePeak() -> Float
    /// True while the player's process is producing output (`kAudioProcessPropertyIsRunningOutput`).
    func playerIsOutputting(pid: Int) -> Bool
    /// Calls `handler` once, on the main queue, the next time a callback sees a peak of at least
    /// `AudioBars.idleThreshold`; the idle driver sleeps between. Replaces any earlier handler.
    func armWake(_ handler: @escaping () -> Void)
}

/// What decides whether a tap exists.
struct AudioTapInputs: Equatable {
    /// The Settings switch.
    var switchOn = false
    /// The Desk is on screen and can be seen (not covered, asleep or at the screen saver).
    var deskShowing = false
    /// Now playing sits on the Desk, where the bars are drawn.
    var linePlaced = false
    var playing = false
    /// The playing app's process id, from the system now playing.
    var pid: Int?

    /// The pid to tap, or nil for no tap: every condition must hold, and a switch that is off
    /// never yields one.
    var target: Int? {
        guard switchOn, deskShowing, linePlaced, playing, let pid, pid > 0 else { return nil }
        return pid
    }
}

/// Starts, stops and re-taps one `AudioLevelTap` from the inputs. Idempotent: the same inputs
/// again do nothing. Pure bookkeeping around the protocol.
final class AudioLevelController {
    private let tap: AudioLevelTap
    /// The pid being tapped, nil when torn down.
    private(set) var tapped: Int?
    /// A pid whose tap failed; not tried again until the target changes, so a stream of unrelated
    /// updates never creates and destroys devices in a loop.
    private var failed: Int?

    init(tap: AudioLevelTap) { self.tap = tap }

    /// Brings the tap in line with `inputs`. Returns true when one is running afterwards.
    @discardableResult
    func apply(_ inputs: AudioTapInputs) -> Bool {
        guard let target = inputs.target else {
            failed = nil
            stop()
            return false
        }
        if tapped == target { return true }
        if failed == target { return false }
        // A new pid: the old tap goes first, then the new one is built.
        stop()
        if tap.start(pid: target) {
            tapped = target
            failed = nil
            return true
        }
        tap.stop()
        failed = target
        return false
    }

    /// Tears everything down now (switch off, pause, Desk hidden, quit).
    func stop() {
        guard tapped != nil else { return }
        tap.stop()
        tapped = nil
    }
}

/// Tells a denied System Audio Recording permission from silence. A denied tap returns no error,
/// only buffers of exact zeros, so the one tell is: the player says it is producing output
/// (`kAudioProcessPropertyIsRunningOutput`) while every peak has been exactly zero for
/// `deniedAfter`. Any nonzero peak clears it. Silence while the player is not running output (a
/// paused tab, a muted video) is not denial. The check can misfire on a player that outputs
/// digital silence for longer than `deniedAfter`; the cost is a quiet line, never a re-prompt.
struct AudioDeniedInference: Equatable {
    /// Seconds of exact zeros, with the player running output, before the state shows.
    static let deniedAfter: TimeInterval = 3

    private var zeroSince: Date?
    private(set) var denied = false

    mutating func observe(peak: Float, playerOutputting: Bool, now: Date) {
        if peak != 0 || !playerOutputting {
            zeroSince = nil
            denied = false
            return
        }
        if zeroSince == nil { zeroSince = now }
        if let since = zeroSince, now.timeIntervalSince(since) >= Self.deniedAfter { denied = true }
    }

    mutating func reset() {
        zeroSince = nil
        denied = false
    }
}

/// What the Desk line and Settings show for the bars.
enum AudioBarsLook: Equatable {
    /// Switch off, or no tap yet: the position bar alone, exactly as before.
    case positionOnly
    /// Bars above the position bar.
    case bars
    /// Permission missing: the position bar and the quiet "Allow in System Settings" line.
    case denied

    static func of(switchOn: Bool, denied: Bool) -> AudioBarsLook {
        guard switchOn else { return .positionOnly }
        return denied ? .denied : .bars
    }
}
