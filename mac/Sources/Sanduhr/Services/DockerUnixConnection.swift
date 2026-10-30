import Foundation
import Network

/// A byte pipe over a Unix domain socket (NWConnection). It knows no path and no request: the path
/// comes in, bytes go through. DockerSocketBoundaryTests keeps the socket paths and every request
/// in DockerSocket.swift. Local only: nothing here touches the network.
final class DockerUnixConnection: DockerConnection, @unchecked Sendable {
    private let connection: NWConnection

    private init(_ connection: NWConnection) { self.connection = connection }

    /// The connect's one answer: the first state that settles it wins, later ones are ignored.
    private final class Settled: @unchecked Sendable {
        private let lock = NSLock()
        private var done = false
        func take() -> Bool { lock.withLock { let was = done; done = true; return was } }
    }

    /// Connects to the socket at `path`. A missing socket or a refused connection is
    /// DockerFailure.down; permission denied is DockerFailure.denied.
    static func open(_ path: String) async throws -> DockerConnection {
        guard FileManager.default.fileExists(atPath: path) else { throw DockerFailure.down }
        let connection = NWConnection(to: .unix(path: path), using: .tcp)
        let queue = DispatchQueue(label: "com.626labs.sanduhr.docker")
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<DockerConnection, Error>) in
                let settled = Settled()
                connection.stateUpdateHandler = { state in
                    switch state {
                    case .ready:
                        guard !settled.take() else { return }
                        connection.stateUpdateHandler = nil
                        continuation.resume(returning: DockerUnixConnection(connection))
                    case let .waiting(error), let .failed(error):
                        guard !settled.take() else { return }
                        connection.cancel()
                        continuation.resume(throwing: classify(error))
                    case .cancelled:
                        guard !settled.take() else { return }
                        continuation.resume(throwing: DockerFailure.down)
                    default:
                        break
                    }
                }
                connection.start(queue: queue)
            }
        } onCancel: {
            connection.cancel()
        }
    }

    private static func classify(_ error: NWError) -> DockerFailure {
        if case let .posix(code) = error, code == .EACCES || code == .EPERM { return .denied }
        return .down
    }

    func write(_ data: Data) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            connection.send(content: data, completion: .contentProcessed { error in
                if error != nil { continuation.resume(throwing: DockerFailure.down) } else { continuation.resume() }
            })
        }
    }

    func read() async throws -> Data {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
                connection.receive(minimumIncompleteLength: 1, maximumLength: 16 * 1024) { data, _, _, error in
                    if error != nil {
                        continuation.resume(throwing: DockerFailure.down)
                    } else if let data, !data.isEmpty {
                        continuation.resume(returning: data)
                    } else {
                        continuation.resume(returning: Data())
                    }
                }
            }
        } onCancel: {
            connection.cancel()
        }
    }

    func close() { connection.cancel() }
}
