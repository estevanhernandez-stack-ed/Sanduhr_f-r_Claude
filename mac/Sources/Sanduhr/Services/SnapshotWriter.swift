import Foundation

/// Writes ~/Library/Application Support/Sanduhr/snapshot.json after every fetch: the same
/// rendezvous file the Windows widget writes (schema_version 1), so other tools on this Mac
/// (a desktop clock, a Claude Code statusline) can show the meters without their own login.
/// Raw facts only, no derived pace; readers work out age and countdowns at read time.
/// Per-seat meters (item 74) write the same schema to `snapshot.<account_ref>.json` (SeatSnapshots).
/// Never holds the session key or account labels: the account is named by `account_ref` only.
enum SnapshotWriter {
    static let schemaVersion = 1

    static var url: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Sanduhr", isDirectory: true)
            .appendingPathComponent("snapshot.json")
    }

    static func writeOk(_ usage: UsageResponse, accountRef: String?, plan: String? = nil, now: Date = Date(), to target: URL = url) {
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
        write(status: "ok", errorKind: nil, tiers: tiers, accountRef: accountRef, plan: plan, now: now, to: target)
    }

    /// On a failed fetch keep the last good tiers, so readers can say "sign in again"
    /// rather than "is the widget running?".
    static func writeError(_ kind: String, accountRef: String?, plan: String? = nil, now: Date = Date(), to target: URL = url) {
        var tiers: [[String: Any]] = []
        if let data = try? Data(contentsOf: target),
           let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let last = obj["tiers"] as? [[String: Any]] {
            tiers = last
        }
        write(status: "error", errorKind: kind, tiers: tiers, accountRef: accountRef, plan: plan, now: now, to: target)
    }

    /// After Sign Out: an auth error with no tiers, so readers say "sign in again" and drop the
    /// meters instead of showing the signed-out account's last numbers. `session_expired` is the
    /// shared set's auth kind (the statusline shows "reauth needed", the MCP server points to
    /// Settings, Credentials); the set is closed, so sign-out adds no kind of its own.
    static func writeSignedOut(accountRef: String?, now: Date = Date(), to target: URL = url) {
        write(status: "error", errorKind: "session_expired", tiers: [], accountRef: accountRef, plan: nil, now: now, to: target)
    }

    /// The error kind a failed fetch writes: `session_expired` and `cloudflare` ask for a new sign-in,
    /// anything else is `network`.
    static func errorKind(_ error: Error) -> String {
        switch error as? ClaudeAPI.APIError {
        case .unauthorized?: "session_expired"
        case .cloudflareChallenge?: "cloudflare"
        default: "network"
        }
    }

    /// An account switch removes the file at once, before anything else happens, so a reader
    /// never sees the new account's freshness on the old account's numbers (Windows WS-E). The
    /// next fetch writes it again with the new `account_ref`.
    static func delete(at target: URL = url) {
        do {
            try FileManager.default.removeItem(at: target)
        } catch CocoaError.fileNoSuchFile {
            // Nothing to delete.
        } catch {
            NSLog("Sanduhr snapshot delete failed (\(type(of: error)))")
        }
    }

    private static func write(status: String, errorKind: String?, tiers: [[String: Any]], accountRef: String?,
                              plan: String?, now: Date, to target: URL = url) {
        // Reset Sanduhr… ran: no snapshot comes back.
        guard !ResetGate.isOn else { return }
        let root: [String: Any] = [
            "schema_version": schemaVersion,
            "writer_version": Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown",
            "captured_at": ISO8601.stamp(now),
            "account_ref": accountRef ?? NSNull(),
            "plan": plan ?? NSNull(),
            "status": status,
            "error_kind": errorKind ?? NSNull(),
            "tiers": tiers,
        ]
        do {
            try FileManager.default.createDirectory(at: target.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            let data = try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])
            try data.write(to: target, options: .atomic)
        } catch {
            NSLog("Sanduhr snapshot write failed (\(type(of: error)))")
        }
    }
}

/// The account as snapshot.json names it: the first 4 bytes of the SHA-256 of the label's UTF-8,
/// lowercase hex, exactly Windows' `SnapshotContract.AccountRef`, so the statusline and
/// `sanduhr-mcp` detect a switch the same way on both platforms. Never the label itself.
enum AccountRef {
    static func of(_ label: String?) -> String? {
        guard let label, !label.isEmpty else { return nil }
        return Digest.sha256Hex(Data(label.utf8), prefix: 8)
    }
}
