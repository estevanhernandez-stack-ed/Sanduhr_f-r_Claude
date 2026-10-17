import Foundation
import Testing
@testable import Sanduhr

/// One spelling for SHA-256 hex: the full digest, a prefix of it, and chunks hashed in order.
@Suite("SHA-256 hex")
struct DigestTests {
    static let abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"

    @Test func fullDigestIsLowercaseHex() {
        #expect(Digest.sha256Hex(Data("abc".utf8)) == Self.abc)
    }

    @Test func prefixKeepsThatManyCharacters() {
        #expect(Digest.sha256Hex(Data("abc".utf8), prefix: 12) == "ba7816bf8f01")
        #expect(Digest.sha256Hex(Data("abc".utf8), prefix: 8) == "ba7816bf")
    }

    @Test func chunksHashAsOneStream() {
        let parts = [Data("a".utf8), Data("bc".utf8)]
        #expect(Digest.sha256Hex(parts) == Self.abc)
        #expect(Digest.sha256Hex(parts, prefix: 12) == "ba7816bf8f01")
        #expect(Digest.sha256Hex([Data]()) == Digest.sha256Hex(Data()))
    }
}
