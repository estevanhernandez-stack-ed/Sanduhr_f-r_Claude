import Foundation
import CZstd

/// Decompresses zstd data with the decoder built into the app (CZstd). Streams through
/// `ZSTD_decompressStream`, so the output is checked against `cap` as it grows and a bomb is
/// refused before it is held in memory. Concatenated frames decode as one stream, which is how
/// Codex's archived rollouts can be written.
enum Zstd {
    enum Failure: Error, Equatable, CustomStringConvertible {
        /// The output would pass `cap` bytes.
        case capExceeded(cap: Int)
        /// Not a zstd frame, or a frame the decoder rejected (the decoder's own wording).
        case badFrame(String)
        /// The input ended inside a frame, or was empty.
        case truncated

        var description: String {
            switch self {
            case .capExceeded(let cap): return "zstd output is larger than \(cap) bytes"
            case .badFrame(let why): return "not valid zstd data: \(why)"
            case .truncated: return "zstd data ends before the frame does"
            }
        }
    }

    /// The default ceiling on decompressed size: 64 MB, the same as Windows' reader.
    static let defaultCap = 64 * 1024 * 1024

    static func decompress(_ data: Data, cap: Int = Zstd.defaultCap) throws -> Data {
        var out = Data()
        try decompress(data, cap: cap) { out.append(contentsOf: $0) }
        return out
    }

    /// The same decode, handing each piece of output to `sink` as it comes instead of holding the
    /// whole result: memory stays at one output buffer however large the cap. The cap counts every
    /// byte handed out, and a piece that would pass it throws before it reaches `sink`.
    static func decompress(_ data: Data, cap: Int, sink: (UnsafeRawBufferPointer) throws -> Void) throws {
        guard !data.isEmpty else { throw Failure.truncated }
        guard let stream = ZSTD_createDStream() else { throw Failure.badFrame("the decoder could not allocate") }
        defer { ZSTD_freeDStream(stream) }

        var total = 0
        var chunk = [UInt8](repeating: 0, count: max(Int(ZSTD_DStreamOutSize()), 1))
        // Zero when the last frame ended cleanly; the decoder's "bytes still wanted" hint otherwise.
        var hint = 0

        try data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) in
            var input = ZSTD_inBuffer(src: raw.baseAddress, size: raw.count, pos: 0)
            var drained = false
            // Keep calling while input is left, and once more after it is spent if the last call
            // filled the output buffer: the decoder may still hold output it had no room for.
            while input.pos < input.size || !drained {
                let before = input.pos
                var produced = 0
                try chunk.withUnsafeMutableBytes { (dst: UnsafeMutableRawBufferPointer) in
                    var output = ZSTD_outBuffer(dst: dst.baseAddress, size: dst.count, pos: 0)
                    let ret = ZSTD_decompressStream(stream, &output, &input)
                    if ZSTD_isError(ret) != 0 {
                        throw Failure.badFrame(String(cString: ZSTD_getErrorName(ret)))
                    }
                    hint = ret
                    produced = output.pos
                    if total + produced > cap { throw Failure.capExceeded(cap: cap) }
                    total += produced
                    if produced > 0 { try sink(UnsafeRawBufferPointer(rebasing: UnsafeRawBufferPointer(dst)[0..<produced])) }
                    drained = output.pos < output.size
                }
                // No input taken and no output given means the decoder is waiting for more.
                if input.pos == before && produced == 0 { break }
            }
        }
        if hint != 0 { throw Failure.truncated }
    }
}
