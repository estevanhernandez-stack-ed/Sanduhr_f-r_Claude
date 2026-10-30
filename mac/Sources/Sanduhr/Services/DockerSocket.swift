import Foundation

// DockerSocketBoundaryTests pins this file: the only one that names Docker's socket paths, the only
// one that builds a request, and one call (`send`) is the only way bytes leave it. A new request
// belongs in DockerRequest, with a test, and nowhere else.

/// The two requests CI watchers make (spec section 15; Windows' DockerPipe.Call). Nothing builds a
/// request from outside this enum, so nothing can ask Docker for anything else: no call that reads a
/// container's environment (where a runner keeps its registration token), and none that changes one.
enum DockerRequest: Equatable, Sendable {
    /// The running containers.
    case list
    /// One container's log, followed, with timestamps and the last 200 lines first.
    case logs(String)

    /// The request's bytes, HTTP/1.1 with no body. Nil for a log request whose id is not exactly 64
    /// lowercase hex characters: it is not built at all.
    var bytes: Data? {
        let target: String
        switch self {
        case .list:
            target = "/containers/json"
        case let .logs(id):
            guard DockerContainers.isID(id) else { return nil }
            target = "/containers/\(id)/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200"
        }
        return Data("GET \(target) HTTP/1.1\r\nHost: docker\r\nUser-Agent: Sanduhr\r\nConnection: close\r\n\r\n".utf8)
    }
}

/// Docker Engine's API over its local Unix socket, as plain HTTP/1.1 (no network, no TCP). No
/// redirect is followed: a 3xx is a refusal like any other non-2xx status. `send` takes a
/// DockerRequest and nothing else.
final class DockerSocket: DockerAPI, @unchecked Sendable {
    /// How long the response headers may take (and the whole of a list answer).
    static let headerLimit: Duration = .seconds(10)

    private let paths: [String]
    private let connect: @Sendable (String) async throws -> DockerConnection
    private let limit: Duration

    /// `connect` opens one socket path; it throws DockerFailure.denied when the account may not use
    /// it and any other error when nothing listens there.
    init(home: String = NSHomeDirectory(), limit: Duration = DockerSocket.headerLimit,
         connect: @escaping @Sendable (String) async throws -> DockerConnection = { try await DockerUnixConnection.open($0) }) {
        self.paths = Self.candidatePaths(home: home)
        self.connect = connect
        self.limit = limit
    }

    /// Docker Desktop's per-user socket first, then the system one.
    static func candidatePaths(home: String) -> [String] {
        [(home as NSString).appendingPathComponent(".docker/run/docker.sock"), "/var/run/docker.sock"]
    }

    func listContainers() async throws -> Data {
        let exchange = try await send(.list)
        defer { exchange.close() }
        return try await bounded(closing: { exchange.close() }) {
            var body = Data()
            while let piece = try await exchange.nextBody() {
                body.append(piece)
                if body.count > DockerContainers.maxBytes { throw DockerFailure.refused }
            }
            return body
        }
    }

    func followLogs(id: String) async throws -> AsyncThrowingStream<Data, Error> {
        let exchange = try await send(.logs(id))
        return AsyncThrowingStream { continuation in
            // The header limit is behind us: the stream follows its consumer's task alone.
            let task = Task {
                do {
                    while let piece = try await exchange.nextBody() { continuation.yield(piece) }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
                exchange.close()
            }
            continuation.onTermination = { _ in
                task.cancel()
                exchange.close()
            }
        }
    }

    /// The one call that puts a request on the socket: its bytes, then the response's head, within
    /// the limit. A status outside 200 to 299 (a redirect included) is a refusal.
    func send(_ request: DockerRequest) async throws -> DockerExchange {
        guard let bytes = request.bytes else { throw DockerFailure.refused }
        let connection = try await open()
        do {
            try await connection.write(bytes)
            let exchange = try await bounded(closing: { connection.close() }) { try await DockerExchange.readHead(connection) }
            guard (200..<300).contains(exchange.head.status) else { throw DockerFailure.refused }
            return exchange
        } catch {
            connection.close()
            throw Self.failure(error)
        }
    }

    private func open() async throws -> DockerConnection {
        var failure = DockerFailure.down
        for path in paths {
            if Task.isCancelled { throw CancellationError() }
            do { return try await connect(path) } catch DockerFailure.denied { failure = .denied } catch {}
        }
        throw failure
    }

    /// Runs `work` against the limit. When the limit runs out `close` ends the connection, which fails
    /// the read in flight, and the call is Docker not answering.
    private func bounded<T: Sendable>(closing close: @escaping @Sendable () -> Void,
                                      _ work: @escaping @Sendable () async throws -> T) async throws -> T {
        let limit = self.limit
        return try await withThrowingTaskGroup(of: T.self) { group in
            group.addTask { try await work() }
            group.addTask {
                try await Task.sleep(for: limit)
                close()
                throw DockerFailure.down
            }
            defer { group.cancelAll() }
            guard let first = try await group.next() else { throw DockerFailure.down }
            return first
        }
    }

    private static func failure(_ error: Error) -> Error {
        if error is CancellationError || error is DockerFailure { return error }
        return DockerFailure.down
    }
}
