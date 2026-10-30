import Foundation
import Testing
@testable import Sanduhr

/// The rule that does not move (spec section 15; Windows' DockerPipeBoundaryTests): Sanduhr lists
/// containers and follows a runner's log, and makes no other Docker call. A container's
/// environment, where a runner keeps its registration token, is never asked for. These read the
/// Sources tree, so a new file that talks to Docker fails here.
@Suite("Docker socket boundary")
struct DockerSocketBoundaryTests {
    private static let sources = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Sources/Sanduhr")

    private static func swiftFiles() throws -> [URL] {
        let all = try #require(FileManager.default.enumerator(at: sources, includingPropertiesForKeys: nil))
            .compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" }
        // A moved tree must fail this test, not narrow it.
        #expect(all.count > 50, "the boundary scan would go vacuous")
        return all
    }

    private static func text(_ name: String) throws -> String {
        try String(contentsOf: sources.appendingPathComponent("Services/\(name)"), encoding: .utf8)
    }

    private static func count(of needle: String, in text: String) -> Int {
        text.components(separatedBy: needle).count - 1
    }

    @Test func theSocketPathsAreNamedInOneFileOnly() throws {
        func files(naming token: String) throws -> [String] {
            try Self.swiftFiles()
                .filter { (try? String(contentsOf: $0, encoding: .utf8))?.contains(token) == true }
                .map(\.lastPathComponent)
        }
        for token in ["docker.sock", "Host: docker"] {
            #expect(try files(naming: token) == ["DockerSocket.swift"], "\(token)")
        }
        // Windows' pipe name and Docker's TCP ports: no way in at all.
        for token in ["docker_engine", ":2375", ":2376"] {
            #expect(try files(naming: token).isEmpty, "\(token)")
        }
    }

    @Test func onlyOneFileOpensAUnixSocketAndItNamesNoPath() throws {
        let files = try Self.swiftFiles()
            .filter { (try? String(contentsOf: $0, encoding: .utf8))?.contains(".unix(path") == true }
            .map(\.lastPathComponent)
        #expect(files == ["DockerUnixConnection.swift"])
        let pipe = try Self.text("DockerUnixConnection.swift")
        for banned in ["GET ", "HTTP/1", "containers", ".sock"] { #expect(!pipe.contains(banned), "\(banned)") }
    }

    @Test func theRequestEnumHasExactlyTwoCases() throws {
        let source = try Self.text("DockerSocket.swift")
        let body = try #require(source.range(of: "enum DockerRequest")).upperBound
        let end = try #require(source.range(of: "var bytes: Data?", range: body..<source.endIndex)).lowerBound
        let cases = source[body..<end].components(separatedBy: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { $0.hasPrefix("case ") }
        #expect(cases == ["case list", "case logs(String)"])
    }

    @Test func theSourceMakesNoOtherCall() throws {
        let source = try Self.text("DockerSocket.swift")
        // Two path templates, one of them the list's, and no read of a single container.
        #expect(Self.count(of: "containers/", in: source) == 2)
        #expect(Self.count(of: "/json", in: source) == 1)
        #expect(source.contains("\"/containers/json\""))
        #expect(Self.count(of: "GET ", in: source) == 1) // the one request line, shared by both
        let lower = source.lowercased()
        for banned in ["inspect", "exec", "post", "delete", "patch", "start", "stop", "kill", "attach",
                       "archive", "export", "urlsession", "urlrequest", "httpmethod"] {
            #expect(!lower.contains(banned), "\(banned)")
        }
    }

    @Test func sendTakesTheEnumAndIsTheOnlyWayBytesLeave() throws {
        let source = try Self.text("DockerSocket.swift")
        // One `send`, its parameter the enum.
        #expect(Self.count(of: "func send(", in: source) == 1)
        #expect(source.contains("func send(_ request: DockerRequest) async throws -> DockerExchange"))
        // Every call of it passes the list case or a logs request.
        let calls = source.components(separatedBy: "send(").dropFirst()
            .filter { !$0.hasPrefix("_ request: DockerRequest)") }
        #expect(calls.count == 2)
        #expect(calls.allSatisfy { $0.hasPrefix(".list)") || $0.hasPrefix(".logs(id))") }, "\(calls.map { String($0.prefix(14)) })")
        // The only write to the connection is inside it, and writes the request's bytes.
        #expect(Self.count(of: ".write(", in: source) == 1)
        let sendBody = try #require(source.range(of: "func send(_ request: DockerRequest)"))
        let writeAt = try #require(source.range(of: "try await connection.write(bytes)"))
        #expect(writeAt.lowerBound > sendBody.upperBound)
        #expect(source.contains("guard let bytes = request.bytes else { throw DockerFailure.refused }"))
        // And nothing else in the tree writes to a DockerConnection.
        for file in try Self.swiftFiles() where file.lastPathComponent != "DockerSocket.swift" {
            let other = try String(contentsOf: file, encoding: .utf8)
            #expect(!other.contains("connection.write("), "\(file.lastPathComponent)")
        }
    }

    @Test func noRedirectIsFollowed() throws {
        let source = try Self.text("DockerSocket.swift")
        #expect(source.contains("(200..<300).contains(exchange.head.status)"))
        #expect(!source.lowercased().contains("location"))
        // Two connections opened in one file only, one per call.
        #expect(Self.count(of: "try await open()", in: source) == 1)
    }

    @Test func theLogsIDRuleIsTheSharedOne() throws {
        let source = try Self.text("DockerSocket.swift")
        #expect(source.contains("guard DockerContainers.isID(id) else { return nil }"))
        #expect(DockerContainers.isID(String(repeating: "a", count: 64)))
        #expect(!DockerContainers.isID(String(repeating: "a", count: 63)))
    }
}
