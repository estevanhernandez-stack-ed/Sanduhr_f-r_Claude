import Foundation
import Testing
@testable import Sanduhr

/// The built-in zstd decoder (CZstd, zstd v1.5.7's decoder-only single file) and its Swift wrapper.
/// The fixture is a made-up text file compressed with the zstd command line tool at level 19.
/// No network, no real Codex rollout, nothing outside the test bundle.

@Suite("Zstd: decompress")
struct ZstdTests {
    private func fixture(_ name: String) throws -> Data {
        let url = try #require(Bundle.module.url(forResource: "Fixtures/zstd/\(name)", withExtension: nil))
        return try Data(contentsOf: url)
    }

    /// The known text, with line endings normalised so a CRLF checkout compares the same.
    private func sampleText() throws -> String {
        try #require(String(data: fixture("sample.txt"), encoding: .utf8)).replacingOccurrences(of: "\r\n", with: "\n")
    }

    @Test func decompressesTheFixtureToItsKnownText() throws {
        let packed = try fixture("sample.txt.zst")
        let text = try sampleText()
        #expect(packed.count < text.utf8.count)
        let out = try Zstd.decompress(packed)
        #expect(String(data: out, encoding: .utf8) == text)
        #expect(text.hasPrefix("Rollout fixture for the zstd decoder tests."))
    }

    @Test func refusesWhenTheOutputPassesTheCap() throws {
        let packed = try fixture("sample.txt.zst")
        let size = try sampleText().utf8.count
        #expect(throws: Zstd.Failure.capExceeded(cap: 100)) { try Zstd.decompress(packed, cap: 100) }
        #expect(throws: Zstd.Failure.capExceeded(cap: size - 1)) { try Zstd.decompress(packed, cap: size - 1) }
    }

    @Test func aCapEqualToTheOutputIsFine() throws {
        let packed = try fixture("sample.txt.zst")
        let size = try sampleText().utf8.count
        #expect(try Zstd.decompress(packed, cap: size).count == size)
    }

    @Test func throwsOnGarbageBytes() {
        let garbage = Data((0..<200).map { UInt8(($0 * 37 + 11) % 251) })
        #expect(throws: Zstd.Failure.self) { try Zstd.decompress(garbage) }
        do {
            _ = try Zstd.decompress(garbage)
            Issue.record("garbage decoded")
        } catch let failure as Zstd.Failure {
            guard case .badFrame = failure else {
                Issue.record("garbage should be a bad frame, got \(failure)")
                return
            }
        } catch {
            Issue.record("unexpected error \(error)")
        }
    }

    @Test func throwsOnATruncatedFrame() throws {
        let packed = try fixture("sample.txt.zst")
        for keep in [packed.count - 1, packed.count / 2, 8] {
            #expect(throws: Zstd.Failure.truncated) { try Zstd.decompress(packed.prefix(keep)) }
        }
    }

    @Test func emptyInputIsNotAFrame() {
        #expect(throws: Zstd.Failure.truncated) { try Zstd.decompress(Data()) }
    }

    @Test func decodesConcatenatedFrames() throws {
        let packed = try fixture("sample.txt.zst")
        let text = try sampleText()
        let out = try Zstd.decompress(packed + packed + packed)
        #expect(String(data: out, encoding: .utf8) == text + text + text)
    }

    @Test func theCapCoversConcatenatedFramesTogether() throws {
        let packed = try fixture("sample.txt.zst")
        let size = try sampleText().utf8.count
        // One frame fits under the cap; three together do not.
        #expect(try Zstd.decompress(packed, cap: size * 2).count == size)
        #expect(throws: Zstd.Failure.capExceeded(cap: size * 2)) { try Zstd.decompress(packed + packed + packed, cap: size * 2) }
    }

    @Test func aTruncatedSecondFrameThrows() throws {
        let packed = try fixture("sample.txt.zst")
        #expect(throws: Zstd.Failure.truncated) { try Zstd.decompress(packed + packed.prefix(packed.count / 2)) }
    }

    @Test func junkAfterAValidFrameThrows() throws {
        let packed = try fixture("sample.txt.zst")
        #expect(throws: Zstd.Failure.self) { try Zstd.decompress(packed + Data([0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02])) }
    }

    @Test func aLargerThanOneChunkOutputRoundTrips() throws {
        // `big.txt.zst` is 20,000 repeats of one line (580,000 bytes in 98): several output chunks,
        // so the loop that drains the decoder's buffer after the input is spent runs.
        let line = "sand falls, the glass turns.\n"
        let out = try Zstd.decompress(try fixture("big.txt.zst"))
        #expect(out.count == 20_000 * line.utf8.count)
        #expect(String(data: out.prefix(line.utf8.count), encoding: .utf8) == line)
        #expect(String(data: out.suffix(line.utf8.count), encoding: .utf8) == line)
    }
}
