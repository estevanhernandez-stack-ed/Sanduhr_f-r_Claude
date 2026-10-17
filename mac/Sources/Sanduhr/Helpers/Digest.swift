import CryptoKit
import Foundation

/// SHA-256 as lowercase hex, the one spelling behind every stamp, manifest hash and digest the
/// app writes: pack manifests, kept-mod stamps, the vault's ids, snapshot.json's account ref.
/// The Windows build spells them the same way, so a hash from either side compares as a string.
enum Digest {
    /// All 64 hex characters of the SHA-256 of `data`.
    static func sha256Hex(_ data: Data) -> String {
        hex(SHA256.hash(data: data))
    }

    /// The first `prefix` hex characters of the SHA-256 of `data` (two per byte).
    static func sha256Hex(_ data: Data, prefix: Int) -> String {
        String(sha256Hex(data).prefix(prefix))
    }

    /// The SHA-256 of `parts` fed to one hasher in order, so hashing ["a", "bc"] equals hashing
    /// "abc"; `prefix` keeps that many hex characters, nil keeps all 64.
    static func sha256Hex<S: Sequence>(_ parts: S, prefix: Int? = nil) -> String where S.Element == Data {
        var hasher = SHA256()
        for part in parts { hasher.update(data: part) }
        let full = hex(hasher.finalize())
        return prefix.map { String(full.prefix($0)) } ?? full
    }

    private static func hex(_ digest: SHA256Digest) -> String {
        digest.map { String(format: "%02x", $0) }.joined()
    }
}
