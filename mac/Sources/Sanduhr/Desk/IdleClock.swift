import AppKit
import CoreGraphics

/// How long since the last input of any kind (key, pointer, scroll): one shared reader, one shared timer.
/// `CGEventSource.secondsSinceLastEventType` needs no permission and says only how long ago, never what.
/// OLED care polls it while it is on and the Desk shows; the companion's idle trigger will subscribe later
/// without a second timer. With no subscriber there is no timer at all, and the timer runs at the quickest
/// interval any subscriber asked for; each subscriber is called no more often than its own.
@MainActor
final class IdleClock {
    static let shared = IdleClock()

    /// Seconds since the last input, nil when it cannot be read.
    typealias Reader = () -> TimeInterval?

    /// The system's answer for any input event type (`kCGAnyInputEventType`, ~0).
    nonisolated static func systemSeconds() -> TimeInterval? {
        guard let any = CGEventType(rawValue: ~0) else { return nil }
        let s = CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: any)
        return s.isFinite && s >= 0 ? s : nil
    }

    struct Token: Hashable { fileprivate let id: Int }

    private struct Subscriber {
        let every: TimeInterval
        var lastCalled: Date?
        let handler: (TimeInterval) -> Void
    }

    private let reader: Reader
    private let now: () -> Date
    private var subscribers: [Int: Subscriber] = [:]
    private var nextID = 0
    private var timer: Timer?
    private var timerInterval: TimeInterval = 0

    init(reader: @escaping Reader = IdleClock.systemSeconds, now: @escaping () -> Date = Date.init) {
        self.reader = reader
        self.now = now
    }

    /// True while the shared timer runs: only while someone is subscribed.
    var isPolling: Bool { timer != nil }
    var subscriberCount: Int { subscribers.count }
    /// The interval the shared timer runs at, 0 when it does not.
    var pollInterval: TimeInterval { timer == nil ? 0 : timerInterval }

    /// Seconds since the last input right now, nil when unreadable.
    func secondsSinceInput() -> TimeInterval? { reader() }

    /// Calls `handler` with the seconds since the last input, once now and then about every `every` seconds,
    /// until `unsubscribe`. The first call is immediate, so a subscriber that arrives while the person is
    /// already away learns it at once.
    @discardableResult
    func subscribe(every: TimeInterval, _ handler: @escaping (TimeInterval) -> Void) -> Token {
        nextID += 1
        let token = Token(id: nextID)
        subscribers[token.id] = Subscriber(every: max(1, every), lastCalled: nil, handler: handler)
        retime()
        fire(token.id)
        return token
    }

    func unsubscribe(_ token: Token) {
        guard subscribers.removeValue(forKey: token.id) != nil else { return }
        retime()
    }

    /// One look at the reader: every subscriber whose interval has passed hears it. The timer calls this;
    /// tests call it directly.
    func tick() {
        let due = subscribers.compactMap { id, s -> Int? in
            guard let last = s.lastCalled else { return id }
            return now().timeIntervalSince(last) >= s.every - 0.5 ? id : nil
        }
        guard !due.isEmpty, let seconds = reader() else { return }
        for id in due { call(id, seconds) }
    }

    private func fire(_ id: Int) {
        guard let seconds = reader() else { return }
        call(id, seconds)
    }

    private func call(_ id: Int, _ seconds: TimeInterval) {
        guard var s = subscribers[id] else { return }
        s.lastCalled = now()
        subscribers[id] = s
        s.handler(seconds)
    }

    /// The timer follows the quickest subscriber and is gone with the last one.
    private func retime() {
        guard let quickest = subscribers.values.map(\.every).min() else {
            timer?.invalidate()
            timer = nil
            timerInterval = 0
            return
        }
        if timer != nil, quickest == timerInterval { return }
        timer?.invalidate()
        timerInterval = quickest
        let t = Timer(timeInterval: quickest, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        t.tolerance = quickest * 0.1
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }
}
