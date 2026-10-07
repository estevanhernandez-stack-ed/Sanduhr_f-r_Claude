import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// Item 51: the public `sanduhr://claude-code` link, the rate limit, the frontmost-terminal rule
/// and the switches. No windows, no real defaults, no links opened.
@Suite("Claude Code glow")
struct ClaudeCodeGlowTests {
    let t0 = Date(timeIntervalSince1970: 1_791_018_000)
    func at(_ s: Double) -> Date { t0.addingTimeInterval(s) }
    let bothOn = NotchGlowSwitches(claudeWaiting: true, claudeDone: true)

    private func allow(_ l: inout ClaudeCodeGlowLimiter, _ e: ClaudeCodeEvent, now: Date) -> Bool {
        l.allow(e, now: now)
    }

    private func event(_ s: String) -> ClaudeCodeEvent? { URL(string: s).flatMap(ClaudeCodeLink.event) }

    // MARK: The link

    @Test func theTwoEventsParse() {
        #expect(event("sanduhr://claude-code?event=waiting") == .waiting)
        #expect(event("sanduhr://claude-code?event=done") == .done)
        #expect(event("sanduhr://claude-code/?event=done") == .done)
        #expect(event("SANDUHR://Claude-Code?event=done") == .done)
        #expect(ClaudeCodeLink.url(.waiting) == "sanduhr://claude-code?event=waiting")
        #expect(ClaudeCodeLink.url(.done) == "sanduhr://claude-code?event=done")
    }

    @Test func anythingElseIsNoEvent() {
        let rejected = [
            "sanduhr://claude-code",
            "sanduhr://claude-code?event=",
            "sanduhr://claude-code?event=WAITING",
            "sanduhr://claude-code?event=stop",
            "sanduhr://claude-code?event=waiting&event=done",
            "sanduhr://claude-code?event=waiting&prompt=hello",
            "sanduhr://claude-code?kind=waiting",
            "sanduhr://claude-code/session/1?event=waiting",
            "sanduhr://claude-code?event=waiting#x",
            "sanduhr://user@claude-code?event=waiting",
            "sanduhr://claude-code:8080?event=waiting",
            "sanduhr://debug/action?event=waiting",
            "estedesk://claude-code?event=waiting",
            "https://claude-code?event=waiting",
        ]
        for s in rejected { #expect(event(s) == nil, "\(s)") }
    }

    @Test func theHostIsRecognizedWhateverItCarries() {
        #expect(ClaudeCodeLink.isClaudeCode(URL(string: "sanduhr://claude-code?event=nope")!))
        #expect(!ClaudeCodeLink.isClaudeCode(URL(string: "sanduhr://settings")!))
        #expect(!ClaudeCodeLink.isClaudeCode(URL(string: "sanduhr://debug/glow")!))
        // Never mistaken for a debug link.
        #expect(!DebugLink.isDebug(URL(string: "sanduhr://claude-code?event=waiting")!))
    }

    // MARK: The notification (hooks post it instead of opening the link)

    @Test func theNotificationNames() {
        #expect(ClaudeCodeSignal.name(.waiting) == "com.626labs.sanduhr.claude-code.waiting")
        #expect(ClaudeCodeSignal.name(.done) == "com.626labs.sanduhr.claude-code.done")
        for e in ClaudeCodeEvent.allCases { #expect(ClaudeCodeSignal.event(ClaudeCodeSignal.name(e)) == e) }
        for bad in ["com.626labs.sanduhr.claude-code.", "com.626labs.sanduhr.claude-code.WAITING",
                    "com.626labs.sanduhr.claude-code.stop", "com.example.claude-code.done", "done"] {
            #expect(ClaudeCodeSignal.event(bad) == nil, "\(bad)")
        }
    }

    /// A real post, as the hook makes it (`/usr/bin/notifyutil -p`), reaches the handler with
    /// its event, once per post. Names under a made-up prefix, so a Sanduhr running on this Mac
    /// never hears them; after cancel nothing arrives.
    @Test func aPostedNotificationReachesTheHandler() throws {
        let prefix = "com.626labs.sanduhr.test.\(UUID().uuidString)."
        let box = Received()
        let signal = ClaudeCodeSignal(prefix: prefix, queue: DispatchQueue(label: "signal-test")) { box.add($0) }
        #expect(signal.registered == 2)
        func post(_ event: ClaudeCodeEvent) throws {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/notifyutil")
            p.arguments = ["-p", ClaudeCodeSignal.name(event, prefix: prefix)]
            try p.run()
            p.waitUntilExit()
            #expect(p.terminationStatus == 0)
        }
        func wait(for count: Int) {
            let deadline = Date().addingTimeInterval(5)
            while box.events.count < count, Date() < deadline { Thread.sleep(forTimeInterval: 0.02) }
        }
        try post(.waiting)
        wait(for: 1)
        try post(.done)
        wait(for: 2)
        #expect(box.events == [.waiting, .done])
        signal.cancel()
        #expect(signal.registered == 0)
        try post(.done)
        Thread.sleep(forTimeInterval: 0.2)
        #expect(box.events == [.waiting, .done])
    }

    private final class Received: @unchecked Sendable {
        private let lock = NSLock()
        private var list: [ClaudeCodeEvent] = []
        func add(_ e: ClaudeCodeEvent) { lock.withLock { list.append(e) } }
        var events: [ClaudeCodeEvent] { lock.withLock { list } }
    }

    // MARK: The rate limit

    @Test func oneGlowPerKindEveryTwentySeconds() {
        var l = ClaudeCodeGlowLimiter()
        #expect(allow(&l, .waiting, now: at(0)))
        #expect(!allow(&l, .waiting, now: at(1)))
        #expect(!allow(&l, .waiting, now: at(19.9)))
        #expect(allow(&l, .waiting, now: at(20)))
        #expect(!allow(&l, .waiting, now: at(39)))
    }

    @Test func doneIsHeldBackJustAfterAWaitingGlow() {
        var l = ClaudeCodeGlowLimiter()
        #expect(allow(&l, .waiting, now: at(0)))
        #expect(!allow(&l, .done, now: at(4.9)))
        #expect(allow(&l, .done, now: at(5)))
        #expect(!allow(&l, .done, now: at(10)))
        // Waiting is never held back by done.
        var m = ClaudeCodeGlowLimiter()
        #expect(allow(&m, .done, now: at(0)))
        #expect(allow(&m, .waiting, now: at(1)))
    }

    @Test func onlyAGlowThatHappensCounts() {
        var l = ClaudeCodeGlowLimiter()
        #expect(allow(&l, .done, now: at(0)))
        #expect(!allow(&l, .done, now: at(10)))      // refused: not remembered
        #expect(allow(&l, .done, now: at(20)))       // 20 s after the glow, not after the refusal
    }

    @Test func aClockSetBackAllowsIt() {
        var l = ClaudeCodeGlowLimiter()
        #expect(allow(&l, .waiting, now: at(100)))
        #expect(allow(&l, .waiting, now: at(50)))
    }

    // MARK: The rules

    @Test func offByDefaultAndTerminalOptionOn() {
        let s = NotchGlowSwitches(MemoryDefaults())
        #expect(!s.claudeWaiting && !s.claudeDone && s.claudeSkipTerminal)
        #expect(s == NotchGlowSwitches())
        var l = ClaudeCodeGlowLimiter()
        #expect(!ClaudeCodeGlowRules.decide(.waiting, switches: s, frontmost: nil, limiter: &l, now: t0))
        #expect(!ClaudeCodeGlowRules.decide(.done, switches: s, frontmost: nil, limiter: &l, now: t0))
        #expect(l == ClaudeCodeGlowLimiter())
    }

    @Test func switchesReadFromTheirKeys() {
        let d = MemoryDefaults()
        d.set(true, forKey: "notchGlowClaudeWaiting")
        d.set(false, forKey: "notchGlowClaudeSkipTerminal")
        #expect(NotchGlowSwitches(d) == NotchGlowSwitches(claudeWaiting: true, claudeSkipTerminal: false))
        d.set(true, forKey: "notchGlowClaudeDone")
        d.set(true, forKey: "notchGlowClaudeSkipTerminal")
        #expect(NotchGlowSwitches(d) == bothOn)
    }

    @Test func eachSwitchGovernsOnlyItsEvent() {
        var l = ClaudeCodeGlowLimiter()
        let waiting = NotchGlowSwitches(claudeWaiting: true)
        #expect(ClaudeCodeGlowRules.decide(.waiting, switches: waiting, frontmost: nil, limiter: &l, now: t0))
        #expect(!ClaudeCodeGlowRules.decide(.done, switches: waiting, frontmost: nil, limiter: &l, now: at(30)))
        let done = NotchGlowSwitches(claudeDone: true)
        #expect(ClaudeCodeGlowRules.decide(.done, switches: done, frontmost: nil, limiter: &l, now: at(30)))
        #expect(!ClaudeCodeGlowRules.decide(.waiting, switches: done, frontmost: nil, limiter: &l, now: at(60)))
        // The other glows' switches don't turn these on.
        let others = NotchGlowSwitches(alerts: true, meetings: true, camera: true)
        #expect(!ClaudeCodeGlowRules.decide(.waiting, switches: others, frontmost: nil, limiter: &l, now: at(90)))
    }

    @Test func aTerminalInFrontSkipsTheGlowUnlessTheOptionIsOff() {
        var l = ClaudeCodeGlowLimiter()
        #expect(!ClaudeCodeGlowRules.decide(.waiting, switches: bothOn, frontmost: "com.googlecode.iterm2", limiter: &l, now: t0))
        // Skipped, so not remembered: the same event from another app glows at once.
        #expect(ClaudeCodeGlowRules.decide(.waiting, switches: bothOn, frontmost: "com.apple.Safari", limiter: &l, now: at(1)))
        var off = bothOn
        off.claudeSkipTerminal = false
        #expect(ClaudeCodeGlowRules.decide(.done, switches: off, frontmost: "com.apple.Terminal", limiter: &l, now: at(30)))
    }

    @Test func theTerminalList() {
        let expected: Set<String> = [
            "com.apple.Terminal", "com.googlecode.iterm2", "com.mitchellh.ghostty",
            "dev.warp.Warp-Stable", "dev.warp.Warp-Preview", "dev.warp.Warp-Beta",
            "com.github.wez.wezterm", "org.alacritty", "io.alacritty", "net.kovidgoyal.kitty",
            "com.microsoft.VSCode", "com.microsoft.VSCodeInsiders", "com.todesktop.230313mzl4w4u92",
            "dev.zed.Zed", "dev.zed.Zed-Preview",
        ]
        #expect(ClaudeCodeGlowRules.terminalBundleIDs == expected)
        for id in expected { #expect(ClaudeCodeGlowRules.isTerminal(id)) }
        for id in ["com.apple.Safari", "com.apple.finder", "com.apple.terminal", "com.626labs.sanduhr", ""] {
            #expect(!ClaudeCodeGlowRules.isTerminal(id), "\(id)")
        }
        #expect(!ClaudeCodeGlowRules.isTerminal(nil))
    }

    @Test func theDebugGlowTakesTheNewKinds() {
        #expect(NotchGlowEvent.Kind(rawValue: "claude-waiting") == .claudeWaiting)
        #expect(NotchGlowEvent.Kind(rawValue: "claude-done") == .claudeDone)
        #expect(ClaudeCodeEvent.waiting.kind == .claudeWaiting)
        #expect(ClaudeCodeEvent.done.kind == .claudeDone)
    }

    // MARK: Where it glows

    @Test func screensWithoutANotchGlowAtTheTopCenterOnlyWhenAsked() {
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: false, hasIsland: false, hasNotch: false) == .none)
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: false, hasIsland: false, hasNotch: false,
                                      topFallback: true) == .top)
        // A notch, or the island, still wins.
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: false, hasIsland: false, hasNotch: true,
                                      topFallback: true) == .plain)
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: true, hasNotch: true,
                                      topFallback: true) == .island)
    }

    @Test func theTopSpotIsTheCameraLightsCoreCentered() {
        let spot = NotchGlowLayout.topSpot(screenWidth: 1920, barHeight: 24)
        #expect(spot == CGRect(x: 860, y: 0, width: CameraLightLayout.noNotchWidth, height: 24))
        let frame = NotchGlowLayout.plainFrame(screen: CGRect(x: 0, y: 0, width: 1920, height: 1080), notch: spot)
        #expect(frame.midX == 960)
        #expect(frame.maxY == 1080)
        #expect(frame.height == 24 + NotchGlowLayout.reach * 2)
    }
}
