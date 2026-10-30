import Foundation

/// One container from Docker's list call. Only these fields are kept. `name` is the label a card
/// shows (the Compose service, which replicas share); `key` is the container's own first name,
/// unique per daemon and stable across a Compose recreate, and is what the board tells runners
/// apart by.
struct DockerContainer: Equatable, Sendable {
    var id: String
    var image: String
    var name: String
    var running: Bool
    var key: String
}

/// The answer to `GET /containers/json` (spec section 15; Windows' DockerContainers). The list
/// call carries no environment; Sanduhr reads id, image, names, state and the Compose service
/// label from it and nothing else. A runner is a running container whose image's own name (no
/// registry, owner, tag or digest) contains one of `runnerImageWords`. Pure.
enum DockerContainers {
    static let maxBytes = 4 * 1024 * 1024

    /// The runner's name on a card at most this long.
    static let nameLength = 40

    static let runnerImageWords = ["actions-runner", "github-runner", "gh-runner"]

    static func parse(_ data: Data) -> [DockerContainer] {
        guard data.count <= maxBytes,
              let root = try? JSONSerialization.jsonObject(with: data) as? [Any] else { return [] }
        var list: [DockerContainer] = []
        for item in root {
            guard let c = item as? [String: Any], let id = c["Id"] as? String, isID(id) else { continue }
            let image = c["Image"] as? String ?? ""
            let service = (c["Labels"] as? [String: Any]).flatMap { $0["com.docker.compose.service"] as? String }
            let first = (c["Names"] as? [Any])?.first as? String
            let own = first.map { String($0.drop(while: { $0 == "/" })) }
            let name = WatcherLimits.line(service ?? own, cap: nameLength) ?? String(id.prefix(12))
            let key = own.map { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? id : $0 } ?? id
            list.append(DockerContainer(id: id, image: image, name: name, running: c["State"] as? String == "running",
                                        key: key))
        }
        return list
    }

    /// The running runners. When two share a label (scaled Compose replicas) both show their
    /// container name instead, so cards and the status line tell them apart.
    static func runners(_ all: [DockerContainer]) -> [DockerContainer] {
        let runners = all.filter { $0.running && isRunnerImage($0.image) }
        var counts: [String: Int] = [:]
        for c in runners { counts[c.name, default: 0] += 1 }
        return runners.map { c in
            guard counts[c.name, default: 0] > 1 else { return c }
            var shown = c
            shown.name = WatcherLimits.line(c.key, cap: nameLength) ?? c.name
            return shown
        }
    }

    static func isRunnerImage(_ image: String) -> Bool {
        var s = Substring(image)
        if let at = s.firstIndex(of: "@") { s = s[..<at] }
        if let slash = s.lastIndex(of: "/") { s = s[s.index(after: slash)...] }
        if let colon = s.firstIndex(of: ":") { s = s[..<colon] }
        let name = s.lowercased()
        return !name.isEmpty && runnerImageWords.contains { name.contains($0) }
    }

    /// Exactly 64 lowercase hex characters (the same rule the socket client applies to a log request).
    static func isID(_ s: String) -> Bool {
        let u = s.utf8
        return u.count == 64 && u.allSatisfy { ($0 >= 0x30 && $0 <= 0x39) || ($0 >= 0x61 && $0 <= 0x66) }
    }
}

enum RunnerStatusKind: Sendable { case off, watching, noRunners, dockerDown, dockerDenied, dockerRefused }

/// The one line under "Show my CI runners' jobs".
struct RunnerStatus: Equatable, Sendable {
    static let maxText = 120

    var kind: RunnerStatusKind
    var runners: [String] = []

    static let off = RunnerStatus(kind: .off)

    var text: String {
        switch kind {
        case .watching:
            Self.clip(runners.count == 1
                ? "Watching 1 runner: \(runners[0])"
                : "Watching \(runners.count) runners: \(runners.joined(separator: ", "))")
        case .noRunners: "No runner containers are running."
        case .dockerDown: "Docker isn't running."
        case .dockerDenied: "Docker refused the connection. Check that your account can use Docker's socket."
        case .dockerRefused: "Docker answered but refused the request."
        case .off: ""
        }
    }

    private static func clip(_ s: String) -> String {
        s.count <= maxText ? s : String(s.prefix(maxText - 1)) + "…"
    }
}
