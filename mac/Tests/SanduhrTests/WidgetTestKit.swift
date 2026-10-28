import Compression
import Foundation
@testable import Sanduhr

/// One entry of a hand-built zip. Every field can lie, so the reader's refusals can be tested one
/// at a time. Ported from Windows' WidgetTestKit.cs.
struct ZipSpec {
    var name: String
    var data: [UInt8]
    var method: UInt16 = 0
    var flags: UInt16 = 0
    /// Host 3 (Unix), version 2.0, as Python's zipfile writes.
    var madeBy: UInt16 = (3 << 8) | 20
    /// A regular file, mode 0644, as build_catalog.py writes.
    var external: UInt32 = 0x81A4 << 16
    var declaredSize: UInt32?
    var localDeclaredSize: UInt32?
    var crc: UInt32?
    var localName: String?
    /// Point this entry's central record at another entry's local header (overlap).
    var sharesLocalOf: Int?

    init(_ name: String, _ data: [UInt8]) {
        self.name = name
        self.data = data
    }
}

enum WidgetTestKit {
    static var fixtures: String {
        Bundle.module.resourceURL!.appendingPathComponent("Fixtures").path
    }

    static func bytes(_ s: String) -> [UInt8] { Array(s.utf8) }

    /// A zip with exactly these entries, in this order, stored (unless a spec says otherwise).
    static func zip(_ list: [ZipSpec], declaredCount: UInt16? = nil) -> Data {
        var body: [UInt8] = []
        var offsets: [UInt32] = []
        for s in list {
            offsets.append(UInt32(body.count))
            let name = latin1(s.localName ?? s.name)
            let crc = s.crc ?? PackValidator.crc32(s.data, 0, s.data.count)
            let size = s.localDeclaredSize ?? s.declaredSize ?? UInt32(s.data.count)
            var h = [UInt8](repeating: 0, count: 30)
            put32(&h, 0, 0x04034b50); put16(&h, 4, 20); put16(&h, 6, s.flags); put16(&h, 8, s.method)
            put16(&h, 10, 0); put16(&h, 12, 0x21); put32(&h, 14, crc); put32(&h, 18, size); put32(&h, 22, size)
            put16(&h, 26, UInt16(name.count)); put16(&h, 28, 0)
            body += h; body += name; body += s.data
        }
        let cdStart = UInt32(body.count)
        for (i, s) in list.enumerated() {
            let name = latin1(s.name)
            let crc = s.crc ?? PackValidator.crc32(s.data, 0, s.data.count)
            let size = s.declaredSize ?? UInt32(s.data.count)
            var c = [UInt8](repeating: 0, count: 46)
            put32(&c, 0, 0x02014b50); put16(&c, 4, s.madeBy); put16(&c, 6, 20); put16(&c, 8, s.flags); put16(&c, 10, s.method)
            put16(&c, 12, 0); put16(&c, 14, 0x21); put32(&c, 16, crc); put32(&c, 20, size); put32(&c, 24, size)
            put16(&c, 28, UInt16(name.count)); put16(&c, 30, 0); put16(&c, 32, 0); put16(&c, 34, 0); put16(&c, 36, 0)
            put32(&c, 38, s.external); put32(&c, 42, s.sharesLocalOf.map { offsets[$0] } ?? offsets[i])
            body += c; body += name
        }
        let cdSize = UInt32(body.count) - cdStart
        var e = [UInt8](repeating: 0, count: 22)
        let n = declaredCount ?? UInt16(list.count)
        put32(&e, 0, 0x06054b50); put16(&e, 8, n); put16(&e, 10, n); put32(&e, 12, cdSize); put32(&e, 16, cdStart)
        body += e
        return Data(body)
    }

    /// A pack folder's files as build_catalog.py zips them: sorted, stored, regular files.
    static func zipPack(_ files: [String: Data]) -> Data {
        zip(PackValidator.ordinalSorted(Array(files.keys)).map { ZipSpec($0, [UInt8](files[$0]!)) })
    }

    /// Raw deflate, the zip method 8 body.
    static func deflate(_ raw: [UInt8]) -> [UInt8] {
        var out = [UInt8](repeating: 0, count: raw.count + 1024)
        let n = compression_encode_buffer(&out, out.count, raw, raw.count, nil, COMPRESSION_ZLIB)
        return Array(out[0..<n])
    }

    private static func latin1(_ s: String) -> [UInt8] {
        s.unicodeScalars.map { UInt8(truncatingIfNeeded: $0.value) }
    }

    private static func put16(_ b: inout [UInt8], _ at: Int, _ v: UInt16) {
        b[at] = UInt8(v & 0xFF); b[at + 1] = UInt8(v >> 8)
    }

    private static func put32(_ b: inout [UInt8], _ at: Int, _ v: UInt32) {
        for i in 0..<4 { b[at + i] = UInt8((v >> (8 * UInt32(i))) & 0xFF) }
    }
}
