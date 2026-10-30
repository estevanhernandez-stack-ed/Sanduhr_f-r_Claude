import Foundation

/// Docker's log stream for a container without a TTY (spec section 15; Windows' DockerLogFrames):
/// frames of an 8-byte header (the stream, 1 stdout or 2 stderr; three zero bytes; the payload
/// length, big-endian) and the payload, decoded into whole lines. Frames and lines may split
/// anywhere. A frame over `maxFrame` or from any other stream is skipped, and so is the line a
/// skipped frame interrupts; a line over `maxLine` is dropped. A header whose three reserved bytes
/// are not zero means the stream is not framed (a container with a TTY sends raw text): from there
/// on the bytes are read as plain lines. Pure.
struct DockerLogFrames: Sendable {
    static let maxFrame = 64 * 1024
    static let maxLine = 16 * 1024

    private var header: [UInt8] = []
    private var payloadLeft = 0
    private var skipping = false
    private var raw = false
    private var line: [UInt8] = []
    private var lineTooLong = false

    /// The lines `data` completes, without their line ends.
    mutating func push(_ data: some Sequence<UInt8>) -> [String] {
        var lines: [String] = []
        var rest = ArraySlice(data)
        if raw {
            append(rest, &lines)
            return lines
        }
        while !rest.isEmpty {
            if payloadLeft == 0 {
                let take = min(8 - header.count, rest.count)
                header.append(contentsOf: rest.prefix(take))
                rest = rest.dropFirst(take)
                if header.count < 8 { break }
                let h = header
                header.removeAll(keepingCapacity: true)
                if h[1] != 0 || h[2] != 0 || h[3] != 0 {
                    // Not a frame header: the stream is raw text (TTY). Read the bytes consumed so far as text too.
                    raw = true
                    append(h[...], &lines)
                    append(rest, &lines)
                    return lines
                }
                payloadLeft = Int(h[4]) << 24 | Int(h[5]) << 16 | Int(h[6]) << 8 | Int(h[7])
                skipping = !(h[0] == 1 || h[0] == 2) || payloadLeft > Self.maxFrame
                // A line cut off by a skipped frame would be spliced onto the next frame's text: drop it.
                if skipping && !line.isEmpty { lineTooLong = true }
                continue
            }
            let n = min(payloadLeft, rest.count)
            if !skipping { append(rest.prefix(n), &lines) }
            payloadLeft -= n
            rest = rest.dropFirst(n)
        }
        return lines
    }

    private mutating func append(_ bytes: ArraySlice<UInt8>, _ lines: inout [String]) {
        for b in bytes {
            if b == 0x0A {
                if !lineTooLong {
                    var end = line.count
                    while end > 0 && line[end - 1] == 0x0D { end -= 1 }
                    lines.append(String(decoding: line[..<end], as: UTF8.self))
                }
                line.removeAll(keepingCapacity: true)
                lineTooLong = false
            } else if line.count < Self.maxLine {
                line.append(b)
            } else {
                lineTooLong = true
            }
        }
    }
}
