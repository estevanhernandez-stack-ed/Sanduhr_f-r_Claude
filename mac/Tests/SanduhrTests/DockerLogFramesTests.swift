import Foundation
import Testing
@testable import Sanduhr

/// Docker's multiplexed log stream (no TTY) into whole lines (spec section 15). Ported from
/// Windows' DockerLogFramesTests.
@Suite("Docker log frames")
struct DockerLogFramesTests {
    private func frame(_ text: String, stream: UInt8 = 1) -> [UInt8] {
        let payload = Array(text.utf8)
        let n = UInt32(payload.count)
        return [stream, 0, 0, 0, UInt8(n >> 24 & 0xFF), UInt8(n >> 16 & 0xFF), UInt8(n >> 8 & 0xFF), UInt8(n & 0xFF)] + payload
    }

    private let lines = [
        "2026-10-22T02:00:03.736011454Z 2026-10-22 02:00:03Z: Running job: compile",
        "2026-10-22T02:00:18.054757098Z 2026-10-22 02:00:18Z: Job compile completed with result: Succeeded",
    ]

    private var stream: [UInt8] { frame(lines[0] + "\n") + frame(lines[1] + "\n", stream: 2) }

    @Test func wholeFramesInOneRead() {
        var d = DockerLogFrames()
        let got = d.push(stream)
        #expect(got == lines)
    }

    @Test func everyByteSplitGivesTheSameLines() {
        let bytes = stream
        for cut in 1..<bytes.count {
            var d = DockerLogFrames()
            let got = d.push(bytes[..<cut]) + d.push(bytes[cut...])
            #expect(got == lines, "cut at \(cut)")
        }
    }

    @Test func byteAtATime() {
        var d = DockerLogFrames()
        let got = stream.flatMap { d.push([$0]) }
        #expect(got == lines)
    }

    @Test func lineAcrossTwoFrames() {
        var d = DockerLogFrames()
        let first = d.push(frame("2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running "))
        #expect(first.isEmpty)
        let got = d.push(frame("job: compile\n"))
        #expect(got == ["2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: compile"])
    }

    @Test func carriageReturnsAndEmptyFrames() {
        var d = DockerLogFrames()
        let got = d.push(frame("") + frame("a\r\n\nb\n"))
        #expect(got == ["a", "", "b"])
    }

    @Test func oversizedFrameIsSkippedAndTheNextOneReads() {
        var big = [UInt8](repeating: UInt8(ascii: "x"), count: 8 + DockerLogFrames.maxFrame + 1)
        let n = UInt32(DockerLogFrames.maxFrame + 1)
        big.replaceSubrange(0..<8, with: [1, 0, 0, 0, UInt8(n >> 24 & 0xFF), UInt8(n >> 16 & 0xFF), UInt8(n >> 8 & 0xFF), UInt8(n & 0xFF)])
        var d = DockerLogFrames()
        let got = d.push(big + frame("ok\n"))
        #expect(got == ["ok"])
    }

    @Test func stdinOrUnknownStreamIsSkipped() {
        var d = DockerLogFrames()
        let got = d.push(frame("secret\n", stream: 0) + frame("ok\n", stream: 1))
        #expect(got == ["ok"])
    }

    @Test func overlongLineIsDropped() {
        var d = DockerLogFrames()
        let long = String(repeating: "x", count: DockerLogFrames.maxLine + 10) + "\n"
        let got = d.push(frame(long) + frame("after\n"))
        #expect(got == ["after"])
    }

    @Test func anUnframedTtyStreamReadsAsPlainLines() {
        let text = Array((lines[0] + "\r\n" + lines[1] + "\r\n").utf8)
        var whole = DockerLogFrames()
        let all = whole.push(text)
        #expect(all == lines)
        var d = DockerLogFrames()
        let bytewise = text.flatMap { d.push([$0]) }
        #expect(bytewise == lines)
    }

    @Test func aHeaderWithAReservedByteSetIsNotAFrame() {
        var d = DockerLogFrames()
        let got = d.push([1, 0, 0, 1, 0, 0, 0, 3] + Array("ok\n".utf8))
        #expect(got.count == 1 && got[0].hasSuffix("ok"))
    }

    @Test func aPartialLineBeforeASkippedFrameIsNotSplicedOntoTheNext() {
        var d = DockerLogFrames()
        let got = d.push(frame("partial ") + frame("secret\n", stream: 0) + frame("rest\n") + frame("ok\n"))
        #expect(got == ["ok"])
    }

    @Test func aSkippedFrameBetweenWholeLinesLosesNothing() {
        var d = DockerLogFrames()
        let got = d.push(frame("a\n") + frame("secret\n", stream: 0) + frame("b\n"))
        #expect(got == ["a", "b"])
    }
}
