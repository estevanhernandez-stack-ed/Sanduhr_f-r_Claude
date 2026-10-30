import Foundation
import Testing
@testable import Sanduhr

/// The Docker socket client against a fake connection (spec section 15; Windows' DockerPipe and its
/// boundary tests): the exact request bytes, the id rule, chunked decoding, the header limit. No
/// real socket.
@Suite("Docker socket")
struct DockerSocketTests {
    static let id = String(repeating: "1", count: 64)

    private func socket(_ connections: [FakeConnection], limit: Duration = .seconds(10),
                        tried: TriedPaths = TriedPaths()) -> DockerSocket {
        let queue = ConnectionQueue(connections)
        return DockerSocket(home: "/Users/test", limit: limit) { path in
            tried.add(path)
            return try queue.next()
        }
    }

    // MARK: Request bytes

    @Test func theListRequestIsExactlyThis() {
        let text = String(decoding: DockerRequest.list.bytes ?? Data(), as: UTF8.self)
        #expect(text == "GET /containers/json HTTP/1.1\r\nHost: docker\r\nUser-Agent: Sanduhr\r\nConnection: close\r\n\r\n")
    }

    @Test func theLogsRequestIsExactlyThis() {
        let text = String(decoding: DockerRequest.logs(Self.id).bytes ?? Data(), as: UTF8.self)
        #expect(text == "GET /containers/\(Self.id)/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200 HTTP/1.1\r\nHost: docker\r\nUser-Agent: Sanduhr\r\nConnection: close\r\n\r\n")
    }

    @Test(arguments: [
        "", "abc", String(repeating: "1", count: 63), String(repeating: "1", count: 65),
        String(repeating: "A", count: 64), String(repeating: "g", count: 64),
        String(repeating: "1", count: 64) + "/json?", "../json", String(repeating: "1", count: 64) + "\n",
        String(repeating: "1", count: 63) + "\u{661}",
    ])
    func aLogsRequestTakesOnlyAFullContainerID(_ id: String) {
        #expect(DockerRequest.logs(id).bytes == nil)
    }

    @Test func aBadIDSendsNothing() async {
        let c = FakeConnection()
        let s = socket([c])
        await #expect(throws: DockerFailure.refused) { _ = try await s.followLogs(id: "../json") }
        #expect(c.writes.isEmpty)
    }

    @Test func theRequestGoesOutOnceAndTheListCameBack() async throws {
        let body = Data("[]".utf8)
        let c = FakeConnection(replies: [HTTPFixture.response(headers: ["Content-Length: 2"], body: body)])
        let data = try await socket([c]).listContainers()
        #expect(data == body)
        #expect(c.writes == [String(decoding: DockerRequest.list.bytes ?? Data(), as: UTF8.self)])
        #expect(c.isClosed)
    }

    // MARK: Paths

    @Test func theSocketsAreTriedInOrderAndTheFirstThatAnswersIsUsed() async throws {
        let tried = TriedPaths()
        let good = FakeConnection(replies: [HTTPFixture.response(headers: ["Content-Length: 2"], body: Data("[]".utf8))])
        let s = socket([], tried: tried, first: [DockerFailure.down], then: good)
        _ = try await s.listContainers()
        #expect(tried.paths == DockerSocket.candidatePaths(home: "/Users/test"))
        #expect(tried.paths.count == 2)
        #expect(tried.paths[0].hasPrefix("/Users/test/"))
    }

    @Test func noSocketIsDockerDownAndADeniedOneIsDenied() async {
        let down = DockerSocket(home: "/h") { _ in throw DockerFailure.down }
        await #expect(throws: DockerFailure.down) { _ = try await down.listContainers() }
        let denied = DockerSocket(home: "/h") { path in
            if path.hasPrefix("/h/") { throw DockerFailure.denied }
            throw DockerFailure.down
        }
        await #expect(throws: DockerFailure.denied) { _ = try await denied.listContainers() }
    }

    // MARK: Statuses

    @Test(arguments: [301, 302, 307, 400, 404, 500])
    func anyNonSuccessStatusIsARefusalAndNothingIsFollowed(_ status: Int) async {
        let c = FakeConnection(replies: [HTTPFixture.response(status, headers: ["Location: http://elsewhere/", "Content-Length: 0"])])
        let s = socket([c])
        await #expect(throws: DockerFailure.refused) { _ = try await s.listContainers() }
        #expect(c.writes.count == 1) // no second request, no redirect
        #expect(c.isClosed)
    }

    @Test func somethingThatIsNotHTTPIsARefusal() async {
        let c = FakeConnection(replies: [Data("SSH-2.0-OpenSSH\r\n\r\n".utf8)])
        await #expect(throws: DockerFailure.refused) { _ = try await socket([c]).listContainers() }
    }

    @Test func aPeerThatClosesWithoutAnsweringIsDown() async {
        let c = FakeConnection()
        await #expect(throws: DockerFailure.down) { _ = try await socket([c]).listContainers() }
    }

    @Test func aListOverTheLimitIsRefused() async {
        let big = Data(repeating: 0x20, count: DockerContainers.maxBytes + 1)
        let c = FakeConnection(replies: [HTTPFixture.response(headers: ["Content-Length: \(big.count)"], body: big)])
        await #expect(throws: DockerFailure.refused) { _ = try await socket([c]).listContainers() }
    }

    // MARK: Header limit

    @Test func theHeadersHaveALimitAndAnswerNothingIsDown() async {
        let c = FakeConnection(holdOpen: true)
        let s = socket([c], limit: .milliseconds(60))
        let started = ContinuousClock.now
        await #expect(throws: DockerFailure.down) { _ = try await s.listContainers() }
        #expect(ContinuousClock.now - started < .seconds(5))
        #expect(c.isClosed)
    }

    @Test func theLimitIsTenSeconds() {
        #expect(DockerSocket.headerLimit == .seconds(10))
    }

    @Test func headersInTimeAreNotCutOffByTheLimit() async throws {
        let c = FakeConnection(replies: [HTTPFixture.response(headers: ["Content-Length: 2"], body: Data("[]".utf8))])
        let data = try await socket([c], limit: .seconds(5)).listContainers()
        #expect(data == Data("[]".utf8))
    }

    // MARK: Chunked bodies

    @Test func aChunkedListIsDecodedWhereverTheChunksSplit() async throws {
        let body = Data(#"[{"Id":"x"}]"#.utf8)
        let wire = HTTPFixture.response(headers: ["Transfer-Encoding: chunked"],
                                        body: HTTPFixture.chunk(body.prefix(4)) + HTTPFixture.chunk(body.dropFirst(4)) + HTTPFixture.lastChunk)
        // One byte at a time: the head, the sizes and the data all split.
        let c = FakeConnection(replies: wire.map { Data([$0]) })
        let data = try await socket([c]).listContainers()
        #expect(data == body)
    }

    @Test func aFollowedLogArrivesInPiecesAndEndsWithTheStream() async throws {
        let a = Data("one\n".utf8), b = Data("two\n".utf8)
        let c = FakeConnection(replies: [
            HTTPFixture.response(headers: ["Transfer-Encoding: chunked"], body: HTTPFixture.chunk(a)),
            HTTPFixture.chunk(b), HTTPFixture.lastChunk,
        ])
        let stream = try await socket([c]).followLogs(id: Self.id)
        var got = Data()
        for try await piece in stream { got.append(piece) }
        #expect(got == a + b)
        #expect(c.writes == [String(decoding: DockerRequest.logs(Self.id).bytes ?? Data(), as: UTF8.self)])
    }

    @Test func cancellingTheConsumerClosesTheConnection() async throws {
        let c = FakeConnection(replies: [HTTPFixture.response(headers: ["Transfer-Encoding: chunked"])], holdOpen: true)
        let stream = try await socket([c]).followLogs(id: Self.id)
        let task = Task { for try await _ in stream {} }
        try await Task.sleep(for: .milliseconds(30))
        task.cancel()
        _ = try? await task.value
        for _ in 0..<200 where !c.isClosed { try await Task.sleep(for: .milliseconds(10)) }
        #expect(c.isClosed)
    }

    @Test func theChunkedDecoderTakesExtensionsTrailersAndRefusesNonHex() throws {
        var d = HTTPBodyDecoder(.chunked)
        let out = try d.push(Data("3;ext=1\r\nabc\r\n0\r\nX-Trailer: 1\r\n\r\n".utf8))
        #expect(out == Data("abc".utf8))
        #expect(d.finished)
        var bad = HTTPBodyDecoder(.chunked)
        #expect(throws: DockerFailure.refused) { _ = try bad.push(Data("zz\r\n".utf8)) }
    }

    @Test func aFixedLengthBodyEndsAtItsLength() throws {
        var d = HTTPBodyDecoder(.length(3))
        #expect(try d.push(Data("ab".utf8)) == Data("ab".utf8))
        #expect(!d.finished)
        #expect(try d.push(Data("cdef".utf8)) == Data("c".utf8))
        #expect(d.finished)
    }
}

/// The paths a fake connector was asked for.
final class TriedPaths: @unchecked Sendable {
    private let lock = NSLock()
    private var list: [String] = []
    var paths: [String] { lock.withLock { list } }
    func add(_ p: String) { lock.withLock { list.append(p) } }
}

/// Hands out connections in order, or throws what it was told to.
final class ConnectionQueue: @unchecked Sendable {
    private let lock = NSLock()
    private var items: [Result<FakeConnection, DockerFailure>]
    init(_ connections: [FakeConnection]) { items = connections.map { .success($0) } }
    init(items: [Result<FakeConnection, DockerFailure>]) { self.items = items }
    func next() throws -> FakeConnection {
        let item: Result<FakeConnection, DockerFailure>? = lock.withLock { items.isEmpty ? nil : items.removeFirst() }
        guard let item else { throw DockerFailure.down }
        return try item.get()
    }
}

extension DockerSocketTests {
    /// A socket whose first path fails with `first` and whose second answers with `then`.
    fileprivate func socket(_ connections: [FakeConnection], tried: TriedPaths, first: [DockerFailure],
                            then: FakeConnection) -> DockerSocket {
        let queue = ConnectionQueue(items: first.map { .failure($0) } + [.success(then)])
        return DockerSocket(home: "/Users/test") { path in
            tried.add(path)
            return try queue.next()
        }
    }
}
