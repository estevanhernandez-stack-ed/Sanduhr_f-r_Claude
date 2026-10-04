import AppKit
import Observation
import os

/// Play/pause, next and previous for whatever plays, sent from Sanduhr's own process through
/// MediaRemote's `MRMediaRemoteSendCommand` (not gated like reading is; the spike, item 52).
/// The framework is private, so it is opened at run time and a missing symbol only means the
/// controls do nothing.
enum MediaRemoteControl {
    enum Command: UInt32 {
        case play = 0
        case pause = 1
        case togglePlayPause = 2
        case nextTrack = 4
        case previousTrack = 5
    }

    private typealias SendCommand = @convention(c) (UInt32, CFDictionary?) -> Bool

    private static let sendCommand: SendCommand? = {
        guard let handle = dlopen("/System/Library/PrivateFrameworks/MediaRemote.framework/MediaRemote", RTLD_LAZY),
              let symbol = dlsym(handle, "MRMediaRemoteSendCommand") else { return nil }
        return unsafeBitCast(symbol, to: SendCommand.self)
    }()

    static var available: Bool { sendCommand != nil }

    @discardableResult
    static func send(_ command: Command) -> Bool {
        sendCommand?(command.rawValue, nil) ?? false
    }
}

/// Now playing (item 53): reads what plays on the Mac and hands it to the notch and the Desk.
/// Off by default (Settings, Desk, Now Playing), and it runs only while Desk does. When on:
///
/// 1. `test`: the bundled mediaremote-adapter's self-check, once at start and after each wake,
///    through `/usr/bin/perl` (MediaRemote answers Apple-signed hosts only).
/// 2. On success, `stream` as a child process: JSON lines on every change, no polling
///    (NowPlayingStream, NowPlayingTracker). An exit restarts it with a growing wait
///    (NowPlayingSupervisor); several quick exits in a row switch to the fallback.
/// 3. On failure: Music's and Spotify's change notifications, no prompt (NowPlayingFallback);
///    with "Ask Music and Spotify directly" also AppleScript to them when the fallback starts.
///
/// Nothing leaves the Mac. Titles and artists stay in memory; the log says only the source and
/// exit codes. `SANDUHR_NOWPLAYING_TEST=fail` in the environment fails the self-test, for
/// checking the fallback by hand (docs/mac-smoke-test.md).
@Observable
final class NowPlayingController {
    static let shared = NowPlayingController()
    private static let log = Logger(subsystem: "com.626labs.sanduhr", category: "nowplaying")

    /// The track to draw, after the hide rules (nil: nothing, excluded, or paused and hidden).
    private(set) var visible: NowPlayingInfo?
    /// What plays, before the hide rules (state.yaml `state`).
    private(set) var state: NowPlayingState = .none
    private(set) var source: NowPlayingSource = .off
    /// The self-test is running.
    private(set) var checking = false
    /// The last self-test failed (Settings says the system now playing isn't available).
    private(set) var adapterFailed = false
    /// Apps seen playing since launch (bundle ids), for Settings' app list. Memory only.
    private(set) var seenApps: [String] = []
    /// Running: switched on and Desk running.
    private(set) var active = false

    @ObservationIgnored private var tracker = NowPlayingTracker()
    @ObservationIgnored private var supervisor = NowPlayingSupervisor()
    @ObservationIgnored private var prefs = NowPlayingPrefs()
    @ObservationIgnored private var testProcess: Process?
    @ObservationIgnored private var streamProcess: Process?
    @ObservationIgnored private var streamStarted = Date()
    @ObservationIgnored private var outputBuffer = Data()
    /// Bumped on every start and stop, so callbacks from an old process are ignored.
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var pending: [DispatchWorkItem] = []
    @ObservationIgnored private var settleItem: DispatchWorkItem?
    @ObservationIgnored private var fallbackObservers: [NSObjectProtocol] = []
    @ObservationIgnored private var observing = false

    private init() {}

    // MARK: Switching

    /// At launch, when Desk starts or stops, and when the switch flips: runs only while both are on.
    func apply() {
        observeOnce()
        prefs = NowPlayingPrefs.saved(in: .desk)
        let wanted = prefs.enabled && DeskController.shared.running
        if wanted, !active { start() }
        if !wanted, active { stop() }
        publish()
    }

    /// Quitting: the child processes go with Sanduhr.
    func shutdown() {
        if active { stop() }
    }

    private func start() {
        active = true
        Self.log.info("now playing on")
        runTest()
    }

    private func stop() {
        Self.log.info("now playing off")
        active = false
        teardown()
        source = .off
        checking = false
    }

    /// Stops every process, timer and observer of the current run.
    private func teardown() {
        generation += 1
        pending.forEach { $0.cancel() }
        pending = []
        settleItem?.cancel(); settleItem = nil
        testProcess?.terminate(); testProcess = nil
        if let p = streamProcess {
            (p.standardOutput as? Pipe)?.fileHandleForReading.readabilityHandler = nil
            p.terminate()
        }
        streamProcess = nil
        outputBuffer = Data()
        stopFallback()
        tracker.reset()
        supervisor.reset()
    }

    private func observeOnce() {
        guard !observing else { return }
        observing = true
        // After a wake the adapter may have died with the display; check it again.
        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.restartAfterWake() }
        // Hide while paused, excluded apps, the AppleScript switch: Settings writes defaults.
        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.prefsChanged() }
    }

    private func prefsChanged() {
        let old = prefs
        let new = NowPlayingPrefs.saved(in: .desk)
        guard new != old else { return }
        if new.enabled != old.enabled { apply(); return }
        prefs = new
        if new.askApps, !old.askApps, active, source == .fallback { askApps() }
        publish()
    }

    private func restartAfterWake() {
        guard active else { return }
        Self.log.info("wake: checking the adapter again")
        teardown()
        source = .off
        runTest()
        publish()
    }

    // MARK: The adapter

    /// Where the bundled adapter lives in Sanduhr.app (mac/build.sh), nil when missing.
    struct AdapterPaths {
        let script: String
        let framework: String
        let testClient: String

        static func inBundle(_ bundle: Bundle = .main) -> AdapterPaths? {
            let contents = bundle.bundleURL.appendingPathComponent("Contents")
            let paths = AdapterPaths(
                script: contents.appendingPathComponent("Resources/NowPlaying/mediaremote-adapter.pl").path,
                framework: contents.appendingPathComponent("Frameworks/MediaRemoteAdapter.framework").path,
                testClient: contents.appendingPathComponent("Helpers/MediaRemoteAdapterTestClient").path)
            let fm = FileManager.default
            guard fm.fileExists(atPath: paths.script), fm.fileExists(atPath: paths.framework),
                  fm.isExecutableFile(atPath: paths.testClient) else { return nil }
            return paths
        }
    }

    static let perl = "/usr/bin/perl"
    /// A `test` that has not finished by then counts as failed.
    static let testTimeout: TimeInterval = 10

    private func runTest() {
        checking = true
        let gen = generation
        guard ProcessInfo.processInfo.environment["SANDUHR_NOWPLAYING_TEST"] != "fail",
              let paths = AdapterPaths.inBundle(),
              FileManager.default.isExecutableFile(atPath: Self.perl) else {
            testFinished(passed: false, status: -1, gen: gen)
            return
        }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: Self.perl)
        p.arguments = [paths.script, paths.framework, paths.testClient, "test"]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        p.terminationHandler = { [weak self] process in
            let status = process.terminationStatus
            DispatchQueue.main.async { self?.testFinished(passed: status == 0, status: status, gen: gen) }
        }
        do {
            try p.run()
            testProcess = p
            later(Self.testTimeout, gen: gen) { [weak self] in
                guard let self, let t = self.testProcess, t.isRunning else { return }
                Self.log.error("adapter test timed out")
                t.terminate()
            }
        } catch {
            testFinished(passed: false, status: -2, gen: gen)
        }
    }

    private func testFinished(passed: Bool, status: Int32, gen: Int) {
        guard gen == generation, active else { return }
        testProcess = nil
        checking = false
        adapterFailed = !passed
        if passed {
            Self.log.info("adapter test passed")
            startStream()
        } else {
            Self.log.error("adapter test failed (exit \(status, privacy: .public)): using the fallback")
            startFallback()
        }
        publish()
    }

    private func startStream() {
        guard let paths = AdapterPaths.inBundle() else { startFallback(); return }
        source = .adapter
        let gen = generation
        let p = Process()
        p.executableURL = URL(fileURLWithPath: Self.perl)
        p.arguments = [paths.script, paths.framework, "stream", "--no-artwork", "--micros"]
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        out.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let chunk = handle.availableData
            guard !chunk.isEmpty else { handle.readabilityHandler = nil; return }
            DispatchQueue.main.async { self?.received(chunk, gen: gen) }
        }
        p.terminationHandler = { [weak self] process in
            let status = process.terminationStatus
            DispatchQueue.main.async { self?.streamExited(status: status, gen: gen) }
        }
        do {
            try p.run()
            streamProcess = p
            streamStarted = Date()
        } catch {
            Self.log.error("adapter stream did not start: using the fallback")
            startFallback()
        }
    }

    private func received(_ chunk: Data, gen: Int) {
        guard gen == generation else { return }
        let now = Date()
        var changed = false
        for line in NowPlayingStream.lines(appending: chunk, to: &outputBuffer) {
            guard let update = NowPlayingStream.parse(line) else { continue }
            scheduleSettle(tracker.receive(update, now: now))
            changed = true
        }
        if changed { publish() }
    }

    private func streamExited(status: Int32, gen: Int) {
        guard gen == generation, active, source == .adapter else { return }
        streamProcess = nil
        outputBuffer = Data()
        let ranFor = Date().timeIntervalSince(streamStarted)
        switch supervisor.streamExited(ranFor: ranFor) {
        case .restart(let wait):
            Self.log.info("adapter stream exited (\(status, privacy: .public)), restarting in \(Int(wait), privacy: .public)s")
            later(wait, gen: generation) { [weak self] in
                guard let self, self.active, self.source == .adapter, self.streamProcess == nil else { return }
                self.startStream()
            }
        case .fallback:
            Self.log.error("adapter stream keeps exiting: using the fallback")
            tracker.reset()
            startFallback()
            publish()
        }
    }

    // MARK: The fallback

    private func startFallback() {
        source = .fallback
        tracker.reset()
        let center = DistributedNotificationCenter.default()
        for entry in NowPlayingFallback.notifications {
            let app = entry.app
            fallbackObservers.append(center.addObserver(
                forName: Notification.Name(entry.name), object: nil, queue: .main
            ) { [weak self] note in
                guard let self, self.active, self.source == .fallback else { return }
                let info = NowPlayingFallback.info(from: note.userInfo ?? [:], app: app, now: Date())
                self.fallbackTrack(info, from: app)
            })
        }
        if prefs.askApps { askApps() }
    }

    private func stopFallback() {
        let center = DistributedNotificationCenter.default()
        fallbackObservers.forEach { center.removeObserver($0) }
        fallbackObservers = []
    }

    /// A track from one of the two apps. Nothing from the app that is showing clears it; nothing
    /// from the other app leaves the showing one alone.
    private func fallbackTrack(_ info: NowPlayingInfo, from app: String) {
        let showingApp = tracker.latest.bundleID
        if info.state == .none, showingApp != nil, showingApp != app { return }
        scheduleSettle(tracker.replace(info, now: Date()))
        publish()
    }

    /// "Ask Music and Spotify directly": the notifications only push, so at the start of the
    /// fallback (and when the switch turns on) ask the running ones for their track. Apps that
    /// are not running are not asked, so neither is launched. The first ask per app raises
    /// macOS's Automation prompt.
    private func askApps() {
        let gen = generation
        for app in [NowPlayingFallback.music, NowPlayingFallback.spotify]
        where !NSRunningApplication.runningApplications(withBundleIdentifier: app).isEmpty {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            p.arguments = ["-e", NowPlayingFallback.script(for: app)]
            let out = Pipe()
            p.standardOutput = out
            p.standardError = FileHandle.nullDevice
            p.terminationHandler = { [weak self] _ in
                let data = out.fileHandleForReading.readDataToEndOfFile()
                let text = String(decoding: data, as: UTF8.self)
                DispatchQueue.main.async {
                    guard let self, gen == self.generation, self.source == .fallback else { return }
                    let info = NowPlayingFallback.info(fromScript: text, app: app, now: Date())
                    // A quiet app does not clear what the notifications already showed.
                    if info.state != .none { self.fallbackTrack(info, from: app) }
                }
            }
            do { try p.run() } catch { Self.log.error("could not ask a music app") }
        }
    }

    // MARK: Showing

    private func scheduleSettle(_ wait: TimeInterval?) {
        settleItem?.cancel()
        settleItem = nil
        guard let wait else { return }
        let gen = generation
        let item = DispatchWorkItem { [weak self] in
            guard let self, gen == self.generation else { return }
            self.tracker.settle(now: Date())
            self.publish()
        }
        settleItem = item
        DispatchQueue.main.asyncAfter(deadline: .now() + wait + 0.05, execute: item)
    }

    private func later(_ wait: TimeInterval, gen: Int, _ work: @escaping () -> Void) {
        let item = DispatchWorkItem { [weak self] in
            guard let self, gen == self.generation else { return }
            work()
        }
        pending.append(item)
        DispatchQueue.main.asyncAfter(deadline: .now() + wait, execute: item)
    }

    /// Hands the track to the notch and the Desk, after the hide rules.
    private func publish() {
        let shown = active ? tracker.shown : nil
        let newState = shown?.state ?? .none
        if state != newState { state = newState }
        let seen = NowPlayingApps.seen(seenApps, adding: shown?.bundleID)
        if seen != seenApps { seenApps = seen }
        let v = prefs.visible(shown)
        if v != visible { visible = v }
        let model = DeskController.shared.model
        if model.nowPlaying != v { model.nowPlaying = v }
        let line = prefs.deskLine && v != nil
        if model.nowPlayingDeskLine != line { model.nowPlayingDeskLine = line }
    }

    // MARK: Controls

    func togglePlayPause() { MediaRemoteControl.send(.togglePlayPause) }
    func next() { MediaRemoteControl.send(.nextTrack) }
    func previous() { MediaRemoteControl.send(.previousTrack) }

    /// The two-finger menu on the notch's and the Desk's now playing: Previous, Play/Pause, Next,
    /// Now Playing Settings….
    func menu() -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        let playing = state == .playing
        let items: [(String, Selector)] = [
            ("Previous", #selector(NowPlayingMenuTarget.previous)),
            (playing ? "Pause" : "Play", #selector(NowPlayingMenuTarget.toggle)),
            ("Next", #selector(NowPlayingMenuTarget.next)),
        ]
        for (title, action) in items {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = NowPlayingMenuTarget.shared
            item.isEnabled = MediaRemoteControl.available
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let settings = NSMenuItem(title: "Now Playing Settings…", action: #selector(NowPlayingMenuTarget.settings), keyEquivalent: "")
        settings.target = NowPlayingMenuTarget.shared
        menu.addItem(settings)
        return menu
    }
}

/// The menu's target: NSMenuItem needs an object with selectors.
final class NowPlayingMenuTarget: NSObject {
    static let shared = NowPlayingMenuTarget()
    @objc func previous() { NowPlayingController.shared.previous() }
    @objc func toggle() { NowPlayingController.shared.togglePlayPause() }
    @objc func next() { NowPlayingController.shared.next() }
    @objc func settings() {
        // Menu items act on the main thread.
        MainActor.assumeIsolated { SettingsWindowController.shared.show(.nowPlaying) }
    }
}
