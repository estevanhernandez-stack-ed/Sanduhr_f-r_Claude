import Foundation

/// Why a Docker call failed (spec section 15; Windows' DockerFailure): not reachable, the socket
/// refused this account, or Docker answered with an error status or something that is not HTTP.
enum DockerFailure: Error, Equatable, Sendable { case down, denied, refused }

/// One byte pipe to Docker's socket. The real one is a Unix domain socket (DockerUnixConnection);
/// tests hand in a fake. Reads give whatever arrived, and an empty `Data` when the peer closed.
/// `close` ends it and fails a read in flight.
protocol DockerConnection: AnyObject, Sendable {
    func write(_ data: Data) async throws
    func read() async throws -> Data
    func close()
}

/// The status line and the two headers the client reads.
struct HTTPResponseHead: Equatable, Sendable {
    var status: Int
    var chunked: Bool
    var contentLength: Int?
}

/// Reads a response's head from bytes as they arrive. The head ends at the first blank line; what
/// follows it is the body's first bytes. Anything that is not an HTTP/1.x status line, or a head
/// over `maxHead`, is a refusal. Pure.
struct HTTPHeadParser: Sendable {
    static let maxHead = 16 * 1024

    private var buffer: [UInt8] = []

    /// The head and the body bytes that came with it, or nil while the head is incomplete.
    mutating func push(_ data: Data) throws -> (head: HTTPResponseHead, rest: Data)? {
        buffer.append(contentsOf: data)
        guard let end = Self.blankLine(in: buffer) else {
            if buffer.count > Self.maxHead { throw DockerFailure.refused }
            return nil
        }
        guard end <= Self.maxHead else { throw DockerFailure.refused }
        let text = String(decoding: buffer[..<end], as: UTF8.self)
        let rest = Data(buffer[(end + 4)...])
        buffer.removeAll()
        return (try Self.parse(text), rest)
    }

    private static func blankLine(in b: [UInt8]) -> Int? {
        guard b.count >= 4 else { return nil }
        for i in 0...(b.count - 4) where b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10 { return i }
        return nil
    }

    private static func parse(_ text: String) throws -> HTTPResponseHead {
        let lines = text.components(separatedBy: "\r\n")
        let first = lines[0].split(separator: " ", maxSplits: 2, omittingEmptySubsequences: true)
        guard first.count >= 2, first[0].hasPrefix("HTTP/1."), let status = Int(first[1]) else {
            throw DockerFailure.refused
        }
        var head = HTTPResponseHead(status: status, chunked: false, contentLength: nil)
        for line in lines.dropFirst() {
            guard let colon = line.firstIndex(of: ":") else { continue }
            let name = line[..<colon].lowercased()
            let value = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            if name == "transfer-encoding", value.lowercased().contains("chunked") { head.chunked = true }
            if name == "content-length" { head.contentLength = Int(value) }
        }
        return head
    }
}

/// A response body's bytes, decoded as they arrive: chunked transfer coding (hex size, data, CRLF,
/// a zero chunk, trailers), a fixed length, or everything until the peer closes. A chunk size
/// that is not hex is a refusal. Pure.
struct HTTPBodyDecoder: Sendable {
    enum Mode: Equatable, Sendable { case chunked, length(Int), untilClose }

    private enum State: Sendable { case size, data(Int), afterData(Int), trailer, done }

    private let mode: Mode
    private var state: State = .size
    private var line: [UInt8] = []
    private var counted = 0

    init(_ mode: Mode) { self.mode = mode }

    init(_ head: HTTPResponseHead) {
        self.init(head.chunked ? .chunked : head.contentLength.map { .length($0) } ?? .untilClose)
    }

    /// Whether the body is complete (a zero chunk, or the whole fixed length).
    var finished: Bool {
        switch mode {
        case .chunked: if case .done = state { true } else { false }
        case let .length(n): counted >= n
        case .untilClose: false
        }
    }

    /// The decoded bytes `data` completes.
    mutating func push(_ data: Data) throws -> Data {
        switch mode {
        case .untilClose: return data
        case let .length(n):
            let take = max(0, min(data.count, n - counted))
            counted += take
            return data.prefix(take)
        case .chunked: return try pushChunked([UInt8](data))
        }
    }

    private mutating func pushChunked(_ bytes: [UInt8]) throws -> Data {
        var out = Data()
        var i = 0
        while i < bytes.count {
            switch state {
            case .size:
                let b = bytes[i]; i += 1
                if b != 0x0A {
                    guard line.count < 64 else { throw DockerFailure.refused }
                    line.append(b)
                    continue
                }
                let text = String(decoding: line, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
                line.removeAll()
                guard let n = Int(text.split(separator: ";").first ?? "", radix: 16), n >= 0 else {
                    throw DockerFailure.refused
                }
                state = n == 0 ? .trailer : .data(n)
            case let .data(left):
                let take = min(left, bytes.count - i)
                out.append(contentsOf: bytes[i..<(i + take)])
                i += take
                state = left - take == 0 ? .afterData(2) : .data(left - take)
            case let .afterData(need):
                i += 1
                state = need == 1 ? .size : .afterData(need - 1)
            case .trailer:
                let b = bytes[i]; i += 1
                guard b == 0x0A else {
                    if b != 0x0D { guard line.count < 1024 else { throw DockerFailure.refused }; line.append(b) }
                    continue
                }
                if line.isEmpty { state = .done }
                line.removeAll()
            case .done:
                i = bytes.count
            }
        }
        return out
    }
}

/// An answered request: the status line is read and was a success; the body comes out in pieces.
/// One task reads it at a time. A closed peer ends the body, even mid-chunk (a followed log that
/// drops just ends; the next poll follows again).
final class DockerExchange: @unchecked Sendable {
    let head: HTTPResponseHead
    private let connection: DockerConnection
    private var decoder: HTTPBodyDecoder
    private var pending: Data

    init(connection: DockerConnection, head: HTTPResponseHead, rest: Data) {
        self.connection = connection
        self.head = head
        self.decoder = HTTPBodyDecoder(head)
        self.pending = rest
    }

    /// Reads from `connection` until the head is complete.
    static func readHead(_ connection: DockerConnection) async throws -> DockerExchange {
        var parser = HTTPHeadParser()
        while true {
            let raw = try await connection.read()
            if raw.isEmpty { throw DockerFailure.down }
            if let (head, rest) = try parser.push(raw) {
                return DockerExchange(connection: connection, head: head, rest: rest)
            }
        }
    }

    /// The next decoded piece of the body, nil at its end.
    func nextBody() async throws -> Data? {
        while true {
            if !pending.isEmpty {
                let raw = pending
                pending = Data()
                let out = try decoder.push(raw)
                if !out.isEmpty { return out }
            }
            if decoder.finished { return nil }
            let raw = try await connection.read()
            if raw.isEmpty { return nil }
            pending = raw
        }
    }

    func close() { connection.close() }
}
