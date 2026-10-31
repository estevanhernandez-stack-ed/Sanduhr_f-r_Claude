import AppKit
import Observation

/// Where the driver hands each frame: the layer view that draws the bars. Heights and caps are 0 to 1.
protocol AudioBarsSink: AnyObject {
    func show(heights: [Float], caps: [Float], showsCaps: Bool)
}

/// Ticks the driver. The real one is a main-run-loop Timer; tests use a counter.
protocol AudioBarsTicker: AnyObject {
    var running: Bool { get }
    func start(interval: TimeInterval, _ tick: @escaping () -> Void)
    func stop()
}

final class TimerTicker: AudioBarsTicker {
    private var timer: Timer?
    var running: Bool { timer != nil }

    func start(interval: TimeInterval, _ tick: @escaping () -> Void) {
        stop()
        let t = Timer(timeInterval: interval, repeats: true) { _ in tick() }
        t.tolerance = interval * 0.2
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }
}

/// The live now playing bars (spec 14): decides when the tap runs, reads its peak at the bars'
/// frame interval, steps AudioBars and AudioBarMotion and hands the heights to the layer view.
/// Everything is idle by default: with the switch off nothing is built, no timer exists and no
/// Core Audio call is made. The timer exists only while a tap delivers a level; at idle (5 seconds
/// under the threshold), paused, switch off or Desk hidden it stops and the layers hold still. A
/// sleeping driver is woken by the tap's first real peak (AudioLevelTap.armWake). Main thread only.
@Observable
final class AudioBarsDriver {
    static let shared = AudioBarsDriver()

    /// The permission looks missing (AudioDeniedInference). The Desk line and Settings show
    /// "Allow in System Settings"; nothing re-asks.
    private(set) var denied = false
    /// The Settings switch, mirrored for the views.
    private(set) var switchOn = false

    @ObservationIgnored private let controller: AudioLevelController
    @ObservationIgnored private let tap: AudioLevelTap
    @ObservationIgnored private let ticker: AudioBarsTicker
    @ObservationIgnored private let defaults: DefaultsStore
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private let reduceMotion: () -> Bool
    @ObservationIgnored private var inputs = AudioTapInputs()
    @ObservationIgnored private var bars = AudioBars()
    @ObservationIgnored private var motion = AudioBarMotion()
    @ObservationIgnored private var inference = AudioDeniedInference()
    @ObservationIgnored private var lastTapped: Int?
    @ObservationIgnored private weak var sink: AudioBarsSink?
    @ObservationIgnored private var observing = false

    /// `tap` defaults to the real one; it builds nothing until a tap starts.
    init(tap: AudioLevelTap = AudioLevelTapFactory.make(),
         ticker: AudioBarsTicker = TimerTicker(),
         defaults: DefaultsStore = UserDefaults.desk,
         now: @escaping () -> Date = Date.init,
         reduceMotion: @escaping () -> Bool = { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }) {
        self.tap = tap
        self.controller = AudioLevelController(tap: tap)
        self.ticker = ticker
        self.defaults = defaults
        self.now = now
        self.reduceMotion = reduceMotion
    }

    /// Whether this Mac can tap a process at all (macOS 14.2 or later).
    static var supported: Bool { AudioLevelTapFactory.isSupported }

    // MARK: Inputs

    /// The Desk is on screen and can be seen. DeskController, from its motion check.
    func setDeskShowing(_ showing: Bool) {
        guard inputs.deskShowing != showing else { return }
        inputs.deskShowing = showing
        refresh()
    }

    /// The track the line shows (nil: nothing, or hidden): its play state and its app's pid.
    func setTrack(_ info: NowPlayingInfo?) {
        let playing = info?.state == .playing
        let pid = info?.pid
        guard inputs.playing != playing || inputs.pid != pid else { return }
        inputs.playing = playing
        inputs.pid = pid
        refresh()
    }

    /// Reads the switch and the placement again (Settings, defaults changes). Cheap; changes only
    /// when an input changed.
    func settingsChanged() {
        observeSystemOnce()
        let on = defaults.bool(forKey: AudioBarsPrefs.levelKey) && Self.supported
        let placed = NowPlayingPlacement.saved(in: defaults).contains(.desk)
        if switchOn != on { switchOn = on }
        guard inputs.switchOn != on || inputs.linePlaced != placed else { return }
        inputs.switchOn = on
        inputs.linePlaced = placed
        refresh()
    }

    /// Quitting or resetting: the tap goes with the app.
    func shutdown() {
        inputs = AudioTapInputs()
        refresh()
    }

    // MARK: The line's view

    /// Hands frames to `sink` (the bars view), and the current ones at once.
    func attach(_ sink: AudioBarsSink) {
        self.sink = sink
        sink.show(heights: motion.heights, caps: motion.caps, showsCaps: motion.showsCaps)
    }

    func detach(_ sink: AudioBarsSink) {
        if self.sink === sink { self.sink = nil }
    }

    // MARK: Running

    private func refresh() {
        let running = controller.apply(inputs)
        if controller.tapped != lastTapped {
            // A new tap (or none): nothing carries over from the last app.
            lastTapped = controller.tapped
            resetFrames()
        }
        if running {
            if !ticker.running, !sleeping { startTicking() }
        } else {
            ticker.stop()
            sleeping = false
        }
    }

    /// Idle: the timer is off and the tap's wake handler is armed.
    @ObservationIgnored private var sleeping = false

    private func startTicking() {
        motion.calm = reduceMotion()
        ticker.start(interval: motion.calm ? AudioBarMotion.calmFrameInterval : AudioBars.frameInterval) { [weak self] in
            self?.tick()
        }
    }

    /// One frame: the peak since the last, into the bars; the bars into the motion; the motion to the
    /// layers. Past 5 quiet seconds the timer stops and the layers settle at rest.
    func tick() {
        guard let pid = controller.tapped else { ticker.stop(); return }
        let at = now()
        let peak = tap.takePeak()
        let outputting = peak == 0 ? tap.playerIsOutputting(pid: pid) : false
        inference.observe(peak: peak, playerOutputting: outputting, now: at)
        if inference.denied != denied { denied = inference.denied }
        bars.push(peak, now: at)
        motion.step(bars.levels)
        sink?.show(heights: motion.heights, caps: motion.caps, showsCaps: motion.showsCaps)
        if bars.isIdle(now: at) { sleep() }
    }

    private func sleep() {
        ticker.stop()
        sleeping = true
        bars.reset()
        motion.reset()
        sink?.show(heights: motion.heights, caps: motion.caps, showsCaps: motion.showsCaps)
        tap.armWake { [weak self] in self?.wake() }
    }

    private func wake() {
        guard sleeping, controller.tapped != nil else { return }
        sleeping = false
        startTicking()
    }

    private func resetFrames() {
        bars.reset()
        motion.reset()
        inference.reset()
        sleeping = false
        if denied { denied = false }
        sink?.show(heights: motion.heights, caps: motion.caps, showsCaps: motion.showsCaps)
    }

    // MARK: The system

    /// Defaults changes and Reduce Motion, observed once the driver is in use.
    private func observeSystemOnce() {
        guard !observing else { return }
        observing = true
        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.settingsChanged() }
        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            guard let self, self.ticker.running else { return }
            self.startTicking()
        }
    }
}
