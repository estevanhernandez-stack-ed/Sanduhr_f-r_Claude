import AppKit
import Foundation
import Testing
@testable import Sanduhr

/// A tap that records what it was asked and builds nothing: no Core Audio, no permission.
final class FakeAudioTap: AudioLevelTap {
    var log: [String] = []
    var startResult = true
    var nextPeak: Float = 0
    var outputting = true
    var wake: (() -> Void)?

    func start(pid: Int) -> Bool { log.append("start \(pid)"); return startResult }
    func stop() { log.append("stop") }
    func takePeak() -> Float { let p = nextPeak; nextPeak = 0; return p }
    func playerIsOutputting(pid: Int) -> Bool { outputting }
    func armWake(_ handler: @escaping () -> Void) { log.append("arm"); wake = handler }
    var starts: [String] { log.filter { $0.hasPrefix("start") } }
}

final class FakeTicker: AudioBarsTicker {
    var running = false
    var interval: TimeInterval = 0
    var starts = 0
    var tick: (() -> Void)?
    func start(interval: TimeInterval, _ tick: @escaping () -> Void) {
        running = true; self.interval = interval; starts += 1; self.tick = tick
    }
    func stop() { running = false }
}

private func on(_ pid: Int? = 4242) -> AudioTapInputs {
    AudioTapInputs(switchOn: true, deskShowing: true, linePlaced: true, playing: true, pid: pid)
}

@Suite("Audio level controller")
struct AudioLevelControllerTests {
    @Test func startsOnlyWhenEveryConditionHolds() {
        let tap = FakeAudioTap()
        let c = AudioLevelController(tap: tap)
        for off in [{ (i: inout AudioTapInputs) in i.switchOn = false },
                    { $0.deskShowing = false }, { $0.linePlaced = false },
                    { $0.playing = false }, { $0.pid = nil }, { $0.pid = 0 }] {
            var i = on()
            off(&i)
            #expect(c.apply(i) == false)
        }
        #expect(tap.starts.isEmpty)
        #expect(c.apply(on()) == true)
        #expect(tap.starts == ["start 4242"])
    }

    @Test func neverStartsWithTheSwitchOffWhateverElseIs() {
        let tap = FakeAudioTap()
        let c = AudioLevelController(tap: tap)
        var i = on()
        i.switchOn = false
        for pid in [1, 99, 4242] { i.pid = pid; c.apply(i) }
        #expect(tap.log.isEmpty)
        #expect(i.target == nil)
    }

    @Test func theSameInputsAgainDoNothing() {
        let tap = FakeAudioTap()
        let c = AudioLevelController(tap: tap)
        c.apply(on()); c.apply(on()); c.apply(on())
        #expect(tap.log == ["start 4242"])
    }

    @Test func aNewPidTearsDownThenTapsTheNewOne() {
        let tap = FakeAudioTap()
        let c = AudioLevelController(tap: tap)
        c.apply(on(100))
        c.apply(on(200))
        #expect(tap.log == ["start 100", "stop", "start 200"])
        #expect(c.tapped == 200)
    }

    @Test func pauseSwitchOffAndDeskHiddenEachTearDown() {
        for change in [{ (i: inout AudioTapInputs) in i.playing = false },
                       { $0.switchOn = false }, { $0.deskShowing = false },
                       { $0.linePlaced = false }, { $0.pid = nil }] {
            let tap = FakeAudioTap()
            let c = AudioLevelController(tap: tap)
            c.apply(on())
            var i = on()
            change(&i)
            #expect(c.apply(i) == false)
            #expect(tap.log == ["start 4242", "stop"])
            #expect(c.tapped == nil)
            // Already down: a second pass stops nothing again.
            c.apply(i)
            #expect(tap.log == ["start 4242", "stop"])
        }
    }

    @Test func playingAgainTapsAgain() {
        let tap = FakeAudioTap()
        let c = AudioLevelController(tap: tap)
        c.apply(on())
        var paused = on(); paused.playing = false
        c.apply(paused)
        c.apply(on())
        #expect(tap.starts.count == 2)
    }

    @Test func aFailedTapIsNotRetriedUntilTheTargetChanges() {
        let tap = FakeAudioTap()
        tap.startResult = false
        let c = AudioLevelController(tap: tap)
        #expect(c.apply(on(7)) == false)
        c.apply(on(7)); c.apply(on(7))
        #expect(tap.starts == ["start 7"])
        c.apply(on(8))
        #expect(tap.starts == ["start 7", "start 8"])
        #expect(c.tapped == nil)
    }
}

@Suite("Audio denied inference")
struct AudioDeniedInferenceTests {
    let t0 = Date(timeIntervalSince1970: 1_792_000_000)

    @Test func zerosWhileRunningOutputForNSecondsIsDenied() {
        var d = AudioDeniedInference()
        d.observe(peak: 0, playerOutputting: true, now: t0)
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(2.9))
        #expect(!d.denied)
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(AudioDeniedInference.deniedAfter))
        #expect(d.denied)
        #expect(AudioDeniedInference.deniedAfter == 3)
    }

    @Test func silenceWhileNotRunningOutputIsNotDenied() {
        var d = AudioDeniedInference()
        for s in stride(from: 0.0, through: 30, by: 0.5) {
            d.observe(peak: 0, playerOutputting: false, now: t0.addingTimeInterval(s))
        }
        #expect(!d.denied)
    }

    @Test func anyNonzeroPeakClearsIt() {
        var d = AudioDeniedInference()
        d.observe(peak: 0, playerOutputting: true, now: t0)
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(4))
        #expect(d.denied)
        d.observe(peak: 0.0001, playerOutputting: true, now: t0.addingTimeInterval(4.05))
        #expect(!d.denied)
        // The run starts over after the clear.
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(5))
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(7))
        #expect(!d.denied)
    }

    @Test func aGapInOutputRestartsTheRun() {
        var d = AudioDeniedInference()
        d.observe(peak: 0, playerOutputting: true, now: t0)
        d.observe(peak: 0, playerOutputting: false, now: t0.addingTimeInterval(2))
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(2.5))
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(4))
        #expect(!d.denied)
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(5.5))
        #expect(d.denied)
    }

    @Test func resetForgetsEverything() {
        var d = AudioDeniedInference()
        d.observe(peak: 0, playerOutputting: true, now: t0)
        d.observe(peak: 0, playerOutputting: true, now: t0.addingTimeInterval(5))
        d.reset()
        #expect(!d.denied)
        #expect(d == AudioDeniedInference())
    }

    @Test func looksFollowTheSwitchAndTheVerdict() {
        #expect(AudioBarsLook.of(switchOn: false, denied: true) == .positionOnly)
        #expect(AudioBarsLook.of(switchOn: true, denied: false) == .bars)
        #expect(AudioBarsLook.of(switchOn: true, denied: true) == .denied)
    }
}

@Suite("Audio bars driver")
struct AudioBarsDriverTests {
    final class Clock { var date = Date(timeIntervalSince1970: 1_792_000_000) }
    let clock = Clock()

    func make(tap: FakeAudioTap = FakeAudioTap(), ticker: FakeTicker = FakeTicker(),
              switchOn: Bool = true, reduce: Bool = false) -> (AudioBarsDriver, FakeAudioTap, FakeTicker) {
        let defaults = MemoryDefaults()
        defaults.set(switchOn, forKey: AudioBarsPrefs.levelKey)
        defaults.set("nowPlaying:bl", forKey: "layout")
        let clock = self.clock
        let d = AudioBarsDriver(tap: tap, ticker: ticker, defaults: defaults, now: { clock.date }, reduceMotion: { reduce })
        d.settingsChanged()
        return (d, tap, ticker)
    }

    func playing(pid: Int = 4242) -> NowPlayingInfo {
        var info = NowPlayingInfo()
        info.title = "Song"; info.playing = true; info.pid = pid
        return info
    }

    func advance(_ driver: AudioBarsDriver, _ seconds: Double, peak: Float = 0, tap: FakeAudioTap) {
        var left = seconds
        while left > 0 {
            clock.date = clock.date.addingTimeInterval(AudioBars.frameInterval)
            tap.nextPeak = peak
            driver.tick()
            left -= AudioBars.frameInterval
        }
    }

    @Test func nothingIsBuiltWithTheSwitchOff() {
        let (d, tap, ticker) = make(switchOn: false)
        d.setDeskShowing(true)
        d.setTrack(playing())
        #expect(tap.log.isEmpty)
        #expect(!ticker.running && ticker.starts == 0)
        #expect(!d.switchOn)
    }

    @Test func startsTickingWhenAllConditionsHoldAndStopsOnEach() {
        let (d, tap, ticker) = make()
        d.setDeskShowing(true)
        #expect(!ticker.running)
        d.setTrack(playing())
        #expect(ticker.running && tap.starts == ["start 4242"])
        #expect(ticker.interval == AudioBars.frameInterval)

        var paused = playing(); paused.playing = false
        d.setTrack(paused)
        #expect(!ticker.running && tap.log.last == "stop")

        d.setTrack(playing())
        d.setDeskShowing(false)
        #expect(!ticker.running && tap.log.last == "stop")

        d.setDeskShowing(true)
        #expect(ticker.running)
        d.setTrack(nil)
        #expect(!ticker.running && tap.log.last == "stop")
    }

    @Test func aNewPidRetapsAndKeepsTicking() {
        let (d, tap, ticker) = make()
        d.setDeskShowing(true)
        d.setTrack(playing(pid: 1))
        d.setTrack(playing(pid: 2))
        #expect(tap.log == ["start 1", "stop", "start 2"])
        #expect(ticker.running)
    }

    @Test func turningTheSwitchOffEndsTheTapAtOnce() {
        let defaults = MemoryDefaults()
        defaults.set(true, forKey: AudioBarsPrefs.levelKey)
        defaults.set("nowPlaying:bl", forKey: "layout")
        let tap = FakeAudioTap(), ticker = FakeTicker()
        let d = AudioBarsDriver(tap: tap, ticker: ticker, defaults: defaults, now: Date.init, reduceMotion: { false })
        d.settingsChanged()
        d.setDeskShowing(true)
        d.setTrack(playing())
        #expect(ticker.running)
        defaults.set(false, forKey: AudioBarsPrefs.levelKey)
        d.settingsChanged()
        #expect(!ticker.running && tap.log == ["start 4242", "stop"])
    }

    @Test func theTimerStopsAtIdleAndAPeakWakesIt() {
        let (d, tap, ticker) = make()
        d.setDeskShowing(true)
        d.setTrack(playing())
        advance(d, 1, peak: 0.5, tap: tap)
        #expect(ticker.running)
        // Quiet (but not exact zero while the player is not outputting): 5 s later it sleeps.
        tap.outputting = false
        advance(d, 5.2, peak: 0, tap: tap)
        #expect(!ticker.running)
        #expect(tap.wake != nil)
        let starts = ticker.starts
        // Nothing ticks while asleep; a real peak from the tap wakes it.
        tap.wake?()
        #expect(ticker.running && ticker.starts == starts + 1)
        #expect(tap.starts.count == 1)
    }

    @Test func reduceMotionTicksCalmly() {
        let (d, _, ticker) = make(reduce: true)
        d.setDeskShowing(true)
        d.setTrack(playing())
        #expect(ticker.interval == AudioBarMotion.calmFrameInterval)
        _ = d
    }

    @Test func deniedShowsAfterZerosWhileOutputtingAndClearsOnSound() {
        let (d, tap, _) = make()
        d.setDeskShowing(true)
        d.setTrack(playing())
        tap.outputting = true
        advance(d, 2, peak: 0, tap: tap)
        #expect(!d.denied)
        advance(d, 1.5, peak: 0, tap: tap)
        #expect(d.denied)
        advance(d, 0.1, peak: 0.2, tap: tap)
        #expect(!d.denied)
    }

    @Test func silentButNotOutputtingIsNeverDenied() {
        let (d, tap, _) = make()
        d.setDeskShowing(true)
        d.setTrack(playing())
        tap.outputting = false
        advance(d, 4.5, peak: 0, tap: tap)
        #expect(!d.denied)
    }

    @Test func aNewTapForgetsTheLastVerdict() {
        let (d, tap, _) = make()
        d.setDeskShowing(true)
        d.setTrack(playing(pid: 1))
        advance(d, 3.5, peak: 0, tap: tap)
        #expect(d.denied)
        d.setTrack(playing(pid: 2))
        #expect(!d.denied)
    }

    @Test func framesGoToTheSinkAndRestAtIdle() {
        final class Sink: AudioBarsSink {
            var frames: [[Float]] = []
            func show(heights: [Float], caps: [Float], showsCaps: Bool) { frames.append(heights) }
        }
        let (d, tap, ticker) = make()
        let sink = Sink()
        d.attach(sink)
        d.setDeskShowing(true)
        d.setTrack(playing())
        advance(d, 0.3, peak: 0.9, tap: tap)
        #expect(sink.frames.last?.contains { $0 > 0 } == true)
        tap.outputting = false
        advance(d, 6, peak: 0, tap: tap)
        #expect(!ticker.running)
        #expect(sink.frames.last?.allSatisfy { $0 == 0 } == true)
    }
}

@Suite("Audio bars drawing")
struct AudioBarsDrawingTests {
    @Test func gradientRunsAcrossTheBarsAndSolidIsOneColor() {
        let stops = [NSColor(srgbRed: 1, green: 0, blue: 0, alpha: 1), NSColor(srgbRed: 0, green: 0, blue: 1, alpha: 1)]
        let solid = AudioBarColors.colors(stops: stops, count: 16, gradient: false)
        #expect(solid.count == 16 && Set(solid.map { $0.hashValue }).count == 1)
        let grad = AudioBarColors.colors(stops: stops, count: 16, gradient: true)
        let first = grad.first?.usingColorSpace(.sRGB)
        #expect(abs((first?.redComponent ?? 0) - 1) < 0.001)
        let last = grad.last?.usingColorSpace(.sRGB)
        #expect(abs((last?.blueComponent ?? 0) - 1) < 0.001)
        #expect(Set(grad.map { $0.hashValue }).count > 8)
        #expect(AudioBarColors.colors(stops: [], count: 16, gradient: true).isEmpty)
    }

    @Test func barsRestAsADotAndFillTheBoxAtOne() {
        let size = CGSize(width: 160, height: 20)
        #expect(AudioBarsGeometry.barFrame(index: 0, count: 16, size: size, height: 0).height == AudioBarsGeometry.restHeight)
        #expect(AudioBarsGeometry.barFrame(index: 15, count: 16, size: size, height: 1).height == 20)
        let a = AudioBarsGeometry.barFrame(index: 3, count: 16, size: size, height: 0.5)
        let b = AudioBarsGeometry.barFrame(index: 4, count: 16, size: size, height: 0.5)
        #expect(a.maxX < b.minX)
    }
}

/// The rule that does not move (spec 14, the Docker boundary's sibling): a Core Audio tap exists in
/// one file, is constructed in that file only, and nothing in the tree reaches for the private
/// permission calls. These read the Sources tree, so a new file that taps audio fails here.
@Suite("Audio tap boundary")
struct AudioTapBoundaryTests {
    private static let sources = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Sources/Sanduhr")

    private static func files(naming token: String) throws -> [String] {
        let all = try #require(FileManager.default.enumerator(at: sources, includingPropertiesForKeys: nil))
            .compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" }
        #expect(all.count > 50, "the boundary scan would go vacuous")
        return all.filter { (try? String(contentsOf: $0, encoding: .utf8))?.contains(token) == true }
            .map(\.lastPathComponent).sorted()
    }

    @Test func theRealTapIsConstructedInItsOwnFileOnly() throws {
        #expect(try Self.files(naming: "CoreAudioProcessTap(") == ["CoreAudioProcessTap.swift"])
        #expect(try Self.files(naming: "AudioLevelTapFactory.make(") == ["AudioBarsDriver.swift"])
    }

    @Test func coreAudioTapCallsLiveInOneFile() throws {
        for token in ["AudioHardwareCreateProcessTap", "AudioHardwareCreateAggregateDevice", "CATapDescription",
                      "AudioDeviceCreateIOProcIDWithBlock", "AudioDeviceStart"] {
            #expect(try Self.files(naming: token) == ["CoreAudioProcessTap.swift"], "\(token)")
        }
    }

    @Test func noPrivatePermissionCallAnywhere() throws {
        for token in ["TCCAccessPreflight", "TCCAccessRequest", "kTCCServiceAudioCapture", "tccutil"] {
            // The words may appear in a comment that forbids them; no code line may call them.
            let hits = try Self.files(naming: token)
            for name in hits {
                let url = try #require(FileManager.default.enumerator(at: Self.sources, includingPropertiesForKeys: nil))
                    .compactMap { $0 as? URL }.first { $0.lastPathComponent == name }
                let code = try String(contentsOf: #require(url), encoding: .utf8).components(separatedBy: "\n")
                    .filter { $0.contains(token) && !$0.trimmingCharacters(in: .whitespaces).hasPrefix("//") }
                #expect(code.isEmpty, "\(token) in \(name)")
            }
        }
    }

    @Test func theTapKeepsNothingButOnePeak() throws {
        let url = Self.sources.appendingPathComponent("Services/CoreAudioProcessTap.swift")
        let source = try String(contentsOf: url, encoding: .utf8)
        for banned in ["FileHandle", "write(to", "URLSession", "AVAudioFile", "ExtAudioFile", "Data(bytes", "NSData", "append("] {
            #expect(!source.contains(banned), "\(banned)")
        }
        #expect(source.contains("muteBehavior = .unmuted"))
        #expect(source.contains("isPrivate = true"))
    }
}

@Suite("Audio bars switch and copy")
struct AudioBarsPrefsTests {
    @Test func theKeysAndTheSettingsLink() {
        #expect(AudioBarsPrefs.levelKey == "nowPlaying.barsLevel")
        #expect(NowPlayingPrefs.barsGradientKey == "nowPlaying.barsGradient")
        #expect(AudioBarsPrefs.settingsURL.scheme == "x-apple.systempreferences")
    }

    @Test func infoPlistExplainsThePromptHonestly() throws {
        let plist = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Info.plist")
        let dict = try #require(NSDictionary(contentsOf: plist))
        let text = try #require(dict["NSAudioCaptureUsageDescription"] as? String)
        #expect(text.contains("never records, saves or sends audio"))
    }

    @Test func settingsFindsTheSwitch() {
        let hits = SettingsAnchor.entries(on: .nowPlaying)
        #expect(hits.contains { $0.anchor == SettingsAnchor.bars && $0.title == "Live audio bars" })
    }
}
