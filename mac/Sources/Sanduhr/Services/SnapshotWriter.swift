import Foundation

/// Writes ~/Library/Application Support/Sanduhr/snapshot.json after every fetch: the same
/// rendezvous file the Windows widget writes (schema_version 1), so other tools on this Mac
/// (a desktop clock, a Claude Code statusline) can show the meters without their own login.
/// Raw facts only, no derived pace; readers work out age and countdowns at read time.
/// Never holds the session key or account labels.
enum SnapshotWriter {
    static let schemaVersion = 1

    static var url: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Sanduhr", isDirectory: true)
            .appendingPathComponent("snapshot.json")
    }

    static func writeOk(_ usage: UsageResponse, now: Date = Date()) {
        var tiers: [[String: Any]] = []
        for tier in Tier.allCases {
            guard let t = usage.tiers[tier], let util = t.utilization else { continue }
            tiers.append([
                "key": tier.rawValue,
                "utilization": Int(util),
                "resets_at": t.resetsAt ?? NSNull(),
                "used": NSNull(),
                "limit": NSNull(),
            ])
        }
        write(status: "ok", errorKind: nil, tiers: tiers, now: now)
    }

    /// On a failed fetch keep the last good tiers, so readers can say "sign in again"
    /// rather than "is the widget running?".
    static func writeError(_ kind: String, now: Date = Date()) {
        var tiers: [[String: Any]] = []
        if let data = try? Data(contentsOf: url),
           let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let last = obj["tiers"] as? [[String: Any]] {
            tiers = last
        }
        write(status: "error", errorKind: kind, tiers: tiers, now: now)
    }

    private static func write(status: String, errorKind: String?, tiers: [[String: Any]], now: Date) {
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let root: [String: Any] = [
            "schema_version": schemaVersion,
            "writer_version": Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown",
            "captured_at": iso.string(from: now),
            "account_ref": NSNull(),
            "plan": NSNull(),
            "status": status,
            "error_kind": errorKind ?? NSNull(),
            "tiers": tiers,
        ]
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            let data = try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])
            try data.write(to: url, options: .atomic)
        } catch {
            NSLog("Sanduhr snapshot write failed (\(type(of: error)))")
        }
    }
}
