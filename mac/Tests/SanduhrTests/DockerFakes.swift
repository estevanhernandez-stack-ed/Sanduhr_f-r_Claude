import Foundation
@testable import Sanduhr

/// A scripted Docker connection for the socket and service tests: no real socket anywhere.
/// `replies` are what `read` hands out in order; once they run out it either ends (an empty read)
/// or, with `holdOpen`, waits until `push` or `close`.
final class FakeConnection: DockerConnection, @unchecked Sendable {
    private let lock = NSLock()
    private var replies: [Data]
    private var waiter: CheckedContinuation<Data, Error>?
    private(set) var written: [Data] = []
    private(set) var closed = false
    private let holdOpen: Bool

    init(replies: [Data] = [], holdOpen: Bool = false) {
        self.replies = replies
        self.holdOpen = holdOpen
    }

    var writes: [String] { lock.withLock { written.map { String(decoding: $0, as: UTF8.self) } } }
    var isClosed: Bool { lock.withLock { closed } }

    func write(_ data: Data) async throws { lock.withLock { written.append(data) } }

    func read() async throws -> Data {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Data, Error>) in
            lock.lock()
            if closed { lock.unlock(); c.resume(throwing: DockerFailure.down); return }
            if !replies.isEmpty { let d = replies.removeFirst(); lock.unlock(); c.resume(returning: d); return }
            if !holdOpen { lock.unlock(); c.resume(returning: Data()); return }
            waiter = c
            lock.unlock()
        }
    }

    func push(_ data: Data) {
        lock.lock()
        if let w = waiter { waiter = nil; lock.unlock(); w.resume(returning: data) } else { replies.append(data); lock.unlock() }
    }

    func close() {
        lock.lock()
        closed = true
        let w = waiter
        waiter = nil
        lock.unlock()
        w?.resume(throwing: DockerFailure.down)
    }
}

/// An HTTP response as bytes: a status line, headers and a body as given.
enum HTTPFixture {
    static func response(_ status: Int = 200, headers: [String] = [], body: Data = Data()) -> Data {
        var head = "HTTP/1.1 \(status) X\r\n"
        for h in headers { head += h + "\r\n" }
        return Data((head + "\r\n").utf8) + body
    }

    static func chunk(_ data: Data) -> Data {
        Data((String(data.count, radix: 16) + "\r\n").utf8) + data + Data("\r\n".utf8)
    }

    static let lastChunk = Data("0\r\n\r\n".utf8)
}
