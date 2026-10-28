import Foundation

/// A reason a proposal was refused, in the shape Windows' DeskReason has: the field it is about and a
/// sentence. `description` is `field: message`; `toJson` is `{field, message}`.
struct DeskReason: Equatable, Sendable {
    let field: String
    let message: String

    init(_ field: String, _ message: String) {
        self.field = field
        self.message = message
    }

    var description: String { field + ": " + message }

    func toJson() -> PackObject { WidgetJson.object([("field", .string(field)), ("message", .string(message))]) }
}

/// A widget pack Claude proposed, built in memory and passed by `PackValidator` (strict): its files
/// (relative POSIX path to bytes, `pack.json` and `LICENSE` included) and the validator's manifest.
struct WidgetProposalPack {
    let files: [String: Data]
    let manifest: PackManifest

    var id: String { manifest.id }
    var kind: String { manifest.kind }
    var name: String { manifest.name }
}

/// What `WidgetProposal.build` made of the tool's arguments: the pack, or the reasons it was refused.
struct WidgetBuild {
    let pack: WidgetProposalPack?
    let reasons: [DeskReason]

    var ok: Bool { pack != nil }
}

/// What `WidgetProposal.decode` made of a request file.
enum WidgetRequestKind: Equatable, Sendable { case proposal, refused, discard }

/// One decoded request: a proposal (the pack checked again), a refusal with reasons, or nothing to answer.
struct WidgetRequest {
    let kind: WidgetRequestKind
    let id: String?
    let pack: WidgetProposalPack?
    let note: String?
    let reasons: [DeskReason]

    static let discard = WidgetRequest(kind: .discard, id: nil, pack: nil, note: nil, reasons: [])
}

/// What the widget tells the MCP server's caller about a proposed widget.
struct WidgetAnswer: Equatable, Sendable {
    let status: String
    let reason: String?
    let reasons: [DeskReason]?
    let packId: String?
    let companion: Bool?

    init(_ status: String, reason: String? = nil, reasons: [DeskReason]? = nil, packId: String? = nil, companion: Bool? = nil) {
        self.status = status
        self.reason = reason
        self.reasons = reasons
        self.packId = packId
        self.companion = companion
    }

    static let previewing = "previewing"
    static let applied = "applied"
    static let rejected = "rejected"

    /// The statuses the widget may give (the server passes nothing else on).
    static let statuses = [previewing, applied, rejected]

    static func refused(_ reasons: [DeskReason]) -> WidgetAnswer { WidgetAnswer(rejected, reasons: reasons) }

    /// What a Sanduhr with no review wired answers a good proposal (tests, the harness).
    static let notReady = WidgetAnswer(rejected, reason: "not_ready",
        reasons: [DeskReason("widget", "not_ready: this version of Sanduhr checks widgets but cannot preview them here")])
}

/// Widgets made with Claude (spec 3.8 section 5, `propose_widget`), the pure half both sides share; a port of
/// Windows' WidgetProposal.cs. The MCP server turns the tool's arguments into a whole pack in memory with
/// `build`: `pack.json` generated from the given fields (the id namespaced `my-`), a `LICENSE` text for the
/// chosen license (CC0-1.0 by default), and the kind's JSON files (a sprite's `sprite.json` and its pixel
/// sheets, `palettes.json`, or `effects.json`). Data only: no PNG and no font is ever accepted, and a theme
/// goes through `propose_theme`. The pack must pass `PackValidator` in strict mode (the trademark rule
/// included) before anything is written. The server then queues `widget-request.json` (`requestJson`) and
/// the widget reads it with `decode`, which checks everything again: the widget never trusts the server.
///
/// Pure: JSON in, bytes and reasons out. No disk, no network, no process.
enum WidgetProposal {
    static let requestFile = "widget-request.json"
    static let resultFile = "widget-result.json"
    static let schemaVersion = 1

    /// A request older than this (the widget was not running) is dropped unanswered, as the design handoff does.
    static let maxAge: TimeInterval = 600

    /// The request file's cap. A Claude-made pack is JSON only, so this is far above a real one.
    static let maxRequestBytes = 4 * 1024 * 1024

    /// Every Claude-made pack id starts with this, so it can never take a catalog pack's id.
    static let idPrefix = "my-"

    static let defaultLicense = "CC0-1.0"
    static let defaultVersion = "1.0.0"
    static let defaultAuthor = "Made here"

    /// The pack-format level that installs Claude-made widgets (spec section 1). It is the format's,
    /// never the app's version.
    static let sanduhrMin = "3.8.0"

    /// The kinds Claude may propose here. `theme` has its own tool, and `font` is a binary.
    static let kinds = ["sprite", "palette", "effects"]

    /// The licenses a Claude-made pack may carry (FORMAT section 4, less the font license).
    static let licenses = ["CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"]

    private static let argNames = ["kind", "pack", "sprite", "sheets", "palettes", "effects", "note"]
    private static let packArgNames = ["id", "name", "description", "version", "author", "license"]

    /// `id` with the `my-` prefix (once).
    static func namespacedId(_ id: String) -> String { id.hasPrefix(idPrefix) ? id : idPrefix + id }

    // MARK: - The server's half: arguments to a pack

    /// Builds and validates the pack the tool's arguments describe. `now` stamps the LICENSE year (UTC).
    static func build(args: PackObject?, now: Date) -> WidgetBuild {
        let args = args ?? PackObject()
        var reasons: [DeskReason] = []
        let unknown = ordinalSorted(args.keys.filter { !argNames.contains($0) })
        if !unknown.isEmpty {
            reasons.append(DeskReason("arguments", "unknown argument(s): " + unknown.prefix(5).map { clip($0, 24) }.joined(separator: ", ")
                + ". The arguments are kind, pack, sprite, sheets, palettes, effects and note. Claude never supplies a PNG or a font."))
        }

        let kind = str(args["kind"])
        switch kind {
        case nil:
            reasons.append(DeskReason("kind", "must be one of sprite, palette, effects"))
        case "theme"?:
            reasons.append(DeskReason("kind", "a theme goes through propose_theme (it previews and saves the widget theme the same way); "
                + "propose_widget makes sprite, palette and effects packs"))
        case "font"?:
            reasons.append(DeskReason("kind", "fonts are binaries and are never proposed; the user installs a font pack from the catalog"))
        case let k?:
            if !kinds.contains(k) { reasons.append(DeskReason("kind", "must be one of sprite, palette, effects")) }
        }

        if let noteNode = present(args["note"]) {
            let note = str(noteNode)
            if note == nil || note!.utf16.count > 200 || note!.unicodeScalars.contains(where: { $0.properties.generalCategory == .control }) {
                reasons.append(DeskReason("note", "must be one line of at most 200 characters"))
            }
        }

        // pack.json fields
        var id: String?, name: String?, description: String?
        var version: String? = defaultVersion, author: String? = defaultAuthor, license: String? = defaultLicense
        if let pack = present(args["pack"])?.object {
            let extra = ordinalSorted(pack.keys.filter { !packArgNames.contains($0) })
            if !extra.isEmpty {
                reasons.append(DeskReason("pack", "unknown key(s): " + extra.prefix(5).map { clip($0, 24) }.joined(separator: ", ")
                    + ". pack.json's files, kind, schema_version and sanduhr_min are written by Sanduhr."))
            }
            id = str(pack["id"])
            if let given = id { id = namespacedId(given) }
            else { reasons.append(DeskReason("pack.id", "must be a lowercase kebab-case string; Sanduhr adds the my- prefix")) }
            name = str(pack["name"])
            if name == nil { reasons.append(DeskReason("pack.name", "must be a string of 1 to 40 characters")) }
            description = str(pack["description"])
            if description == nil { reasons.append(DeskReason("pack.description", "must be a string of 1 to 200 characters")) }
            if let v = present(pack["version"]) {
                version = str(v)
                if version == nil { reasons.append(DeskReason("pack.version", "must be MAJOR.MINOR.PATCH")) }
            }
            if let a = present(pack["author"]) {
                author = str(a)
                if author == nil { reasons.append(DeskReason("pack.author", "must be a string of 1 to 80 characters")) }
            }
            if let l = present(pack["license"]) {
                license = str(l)
                if license == nil || !licenses.contains(license!) {
                    reasons.append(DeskReason("pack.license", "must be one of " + licenses.joined(separator: ", ") + " (default " + defaultLicense + ")"))
                }
            }
        } else {
            reasons.append(DeskReason("pack", "must be an object: {id, name, description, version?, author?, license?}"))
        }

        // the kind's files
        var files: [String: Data] = [:]
        if let kind, kinds.contains(kind) {
            for other in ["sprite", "sheets", "palettes", "effects"] {
                let belongs = (kind == "sprite" && (other == "sprite" || other == "sheets"))
                    || (kind == "palette" && other == "palettes") || (kind == "effects" && other == "effects")
                if !belongs && present(args[other]) != nil {
                    reasons.append(DeskReason(other, "does not belong to a \(kind) pack"))
                }
            }
            switch kind {
            case "sprite": buildSprite(args, &files, &reasons)
            case "palette": buildList(args["palettes"], "palettes", "palettes.json", &files, &reasons)
            default: buildList(args["effects"], "effects", "effects.json", &files, &reasons)
            }
        }
        guard reasons.isEmpty, let kind, let id, let name, let description, let version, let author, let license else {
            return WidgetBuild(pack: nil, reasons: reasons)
        }

        let listed = ordinalSorted(Array(files.keys)).map { PackValue.string($0) }
        let manifest = WidgetJson.object([
            ("schema_version", WidgetJson.int(1)),
            ("id", .string(id)),
            ("name", .string(name)),
            ("version", .string(version)),
            ("author", .string(author)),
            ("license", .string(license)),
            ("kind", .string(kind)),
            ("sanduhr_min", .string(sanduhrMin)),
            ("description", .string(description)),
            ("files", .array(listed)),
        ])
        files["pack.json"] = utf8(WidgetJson.indented(.object(manifest)) + "\n")
        files["LICENSE"] = utf8(licenseText(license, author, utcYear(now)))
        return check(files, expectedId: id)
    }

    private static func buildSprite(_ args: PackObject, _ files: inout [String: Data], _ reasons: inout [DeskReason]) {
        guard let sprite = present(args["sprite"])?.object else {
            reasons.append(DeskReason("sprite", "must be an object: sprite.json (schema_version 2) with its stages or sheet, animations, moods, triggers and roam"))
            return
        }
        let copy = WidgetJson.defaulting(sprite, "schema_version", WidgetJson.int(2))
        for path in sheetPaths(copy) where path.lowercased().hasSuffix(".png") {
            reasons.append(DeskReason("sprite", "names the PNG sheet \(clip(path, 40)); Claude never supplies an image. Draw it as a .json pixel sheet in sheets"))
        }
        files["sprite.json"] = utf8(WidgetJson.indented(.object(copy)) + "\n")

        guard let sheets = present(args["sheets"])?.object, sheets.count > 0 else {
            reasons.append(DeskReason("sheets", "must be an object of pixel sheets: path (ending .json) to {palette, frame_w, frame_h, frames}"))
            return
        }
        if sheets.count > 6 { reasons.append(DeskReason("sheets", "at most 6 pixel sheets (one per stage)")) }
        for path in sheets.keys {
            if !path.hasSuffixOrdinal(".json") || !PackValidator.safeRelPath(path) || path == "sprite.json" {
                reasons.append(DeskReason("sheets", "\(clip(path, 40)): a sheet is a .json pixel sheet with a safe relative path (no PNG, no font)"))
                continue
            }
            guard let so = sheets[path]?.object else {
                reasons.append(DeskReason("sheets", "\(clip(path, 40)): must be an object {palette, frame_w, frame_h, frames}"))
                continue
            }
            files[path] = utf8(WidgetJson.indented(.object(so)) + "\n")
        }
    }

    /// palettes.json or effects.json: the object as given, or a bare list wrapped as `{schema_version: 1, key: list}`.
    private static func buildList(_ given: PackValue?, _ key: String, _ file: String, _ files: inout [String: Data], _ reasons: inout [DeskReason]) {
        let doc: PackObject
        switch present(given) {
        case .array(let list)?:
            doc = WidgetJson.object([("schema_version", WidgetJson.int(1)), (key, .array(list))])
        case .object(let o)?:
            doc = WidgetJson.defaulting(o, "schema_version", WidgetJson.int(1))
        default:
            reasons.append(DeskReason(key, "must be \(file)'s object {schema_version: 1, \(key): [...]} or the list alone"))
            return
        }
        files[file] = utf8(WidgetJson.indented(.object(doc)) + "\n")
    }

    /// Every sheet path sprite.json names (top-level `sheet` and each stage's).
    private static func sheetPaths(_ sprite: PackObject) -> [String] {
        var out: [String] = []
        if let single = str(sprite["sheet"]) { out.append(single) }
        if let stages = sprite["stages"]?.array {
            for st in stages { if let s = str(st.object?["sheet"]) { out.append(s) } }
        }
        return out
    }

    // MARK: - Both halves: the rules on a built pack

    /// The rules a Claude-made pack meets, on its files: JSON and the LICENSE only (no PNG, no font, nothing else),
    /// the id namespaced `my-` and equal to `expectedId`, a kind Claude may propose, and `PackValidator` in strict
    /// mode (which carries the trademark rule). The server runs it before writing the request; the widget runs it
    /// again on the request.
    static func check(_ files: [String: Data], expectedId: String?) -> WidgetBuild {
        var reasons: [DeskReason] = []
        for rel in ordinalSorted(Array(files.keys)) {
            if rel == "pack.json" || rel == "LICENSE" { continue }
            if !rel.hasSuffixOrdinal(".json") {
                reasons.append(DeskReason("files", "\(clip(rel, 40)): a Claude-made pack holds JSON only; no PNG, no font, nothing else"))
            }
        }
        if expectedId == nil || !expectedId!.hasPrefix(idPrefix) {
            reasons.append(DeskReason("pack.id", "a Claude-made pack's id starts with my-"))
        }
        if !reasons.isEmpty { return WidgetBuild(pack: nil, reasons: reasons) }

        let verdict = PackValidator.validate(files, folderName: expectedId, strict: true)
        guard verdict.ok, let manifest = verdict.manifest else {
            return WidgetBuild(pack: nil, reasons: verdict.errors.prefix(24).map { DeskReason("pack", $0) })
        }
        if !kinds.contains(manifest.kind) {
            return WidgetBuild(pack: nil, reasons: [DeskReason("kind", "a Claude-made pack is a sprite, a palette or effects")])
        }
        return WidgetBuild(pack: WidgetProposalPack(files: files, manifest: manifest), reasons: [])
    }

    // MARK: - The request file

    /// The request file: `{schema_version, id, requested_at, pack_id, note?, files: {path: text}}`. Every file is
    /// text (JSON, or the LICENSE), so the request is data only.
    static func requestJson(requestId: String, pack: WidgetProposalPack, note: String?, now: Date) -> PackObject {
        let files = PackObject()
        for rel in ordinalSorted(Array(pack.files.keys)) {
            files.add(rel, .string(String(decoding: pack.files[rel]!, as: UTF8.self)))
        }
        var fields: [(String, PackValue)] = [
            ("schema_version", WidgetJson.int(schemaVersion)),
            ("id", .string(requestId)),
            ("requested_at", .string(stamp(now))),
            ("pack_id", .string(pack.id)),
        ]
        if let note, note.unicodeScalars.contains(where: { !$0.properties.isWhitespace }) { fields.append(("note", .string(note))) }
        fields.append(("files", .object(files)))
        return WidgetJson.object(fields)
    }

    /// Reads a request and checks the pack again with the widget's own rules. Unreadable, without an id, or older
    /// than `maxAge` is dropped; a pack that breaks a rule is refused with the reasons; a good one is a proposal.
    static func decode(_ data: Data, now: Date) -> WidgetRequest {
        if data.count > maxRequestBytes { return .discard }
        var bytes = [UInt8](data)
        if bytes.starts(with: [0xEF, 0xBB, 0xBF]) { bytes.removeFirst(3) }   // Utf8JsonReader skips a byte-order mark
        guard let scalars = PackJson.decodeUTF8(bytes), case .success(let root) = PackJson.parse(scalars),
              let o = root.object else { return .discard }
        guard let id = str(o["id"]), id.utf16.count > 0, id.utf16.count <= 64 else { return .discard }
        guard let stampText = str(o["requested_at"]), let at = parseStamp(stampText) else {
            return refused(id, DeskReason("requested_at", "is missing or not a date"))
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard let sv = o["schema_version"]?.number, sv.isInt, sv.value == Double(schemaVersion) else {
            return refused(id, DeskReason("schema_version", "this Sanduhr reads widget requests of schema 1"))
        }
        let packId = str(o["pack_id"])
        var note = str(o["note"])
        if let n = note, n.utf16.count > 200 { note = String(decoding: Array(n.utf16.prefix(200)), as: UTF16.self) }
        guard let list = o["files"]?.object, list.count > 0, list.count <= PackValidator.maxFiles + 3 else {
            return refused(id, DeskReason("files", "must be an object of the pack's files"))
        }
        var files: [String: Data] = [:]
        for rel in list.keys {
            guard let text = str(list[rel]) else {
                return refused(id, DeskReason("files", "\(clip(rel, 40)): a Claude-made pack's files are text (JSON or the LICENSE)"))
            }
            files[rel] = utf8(text)
        }
        let result = check(files, expectedId: packId)
        if let pack = result.pack {
            return WidgetRequest(kind: .proposal, id: id, pack: pack, note: note, reasons: [])
        }
        return WidgetRequest(kind: .refused, id: id, pack: nil, note: nil, reasons: result.reasons)
    }

    private static func refused(_ id: String, _ reason: DeskReason) -> WidgetRequest {
        WidgetRequest(kind: .refused, id: id, pack: nil, note: nil, reasons: [reason])
    }

    /// The result file's contents: `{completed_at, id, result: {status, reason?, reasons?, pack_id?, companion?}}`.
    static func resultJson(id: String, answer: WidgetAnswer, now: Date) -> Data {
        var fields: [(String, PackValue)] = [("status", .string(answer.status))]
        if let reason = answer.reason { fields.append(("reason", .string(reason))) }
        if let reasons = answer.reasons, !reasons.isEmpty {
            fields.append(("reasons", .array(reasons.map { .object($0.toJson()) })))
        }
        if let packId = answer.packId { fields.append(("pack_id", .string(packId))) }
        if let companion = answer.companion { fields.append(("companion", .bool(companion))) }
        let root = WidgetJson.object([
            ("completed_at", .string(stamp(now))),
            ("id", .string(id)),
            ("result", .object(WidgetJson.object(fields))),
        ])
        return utf8(WidgetJson.compact(.object(root)))
    }

    // MARK: - LICENSE texts

    /// The LICENSE a Claude-made pack carries for `spdx`. Each text holds the phrase the validator looks for.
    static func licenseText(_ spdx: String, _ author: String, _ year: Int) -> String {
        let holder = "Copyright (c) \(year) \(author)"
        let parts: [String]
        switch spdx {
        case "MIT":
            parts = [
                "MIT License\n\n\(holder)\n\n",
                "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated ",
                "documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation ",
                "the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to ",
                "permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n",
                "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the ",
                "Software.\n\n",
                "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE ",
                "WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR ",
                "COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR ",
                "OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n",
            ]
        case "ISC":
            parts = [
                "ISC License\n\n\(holder)\n\n",
                "Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby ",
                "granted, provided that the above copyright notice and this permission notice appear in all copies.\n\n",
                "THE SOFTWARE IS PROVIDED \"AS IS\" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL ",
                "IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, ",
                "INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN ",
                "AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR ",
                "PERFORMANCE OF THIS SOFTWARE.\n",
            ]
        case "BSD-3-Clause":
            parts = [
                "BSD 3-Clause License\n\n\(holder)\n\n",
                "Redistribution and use in source and binary forms, with or without modification, are permitted provided that the ",
                "following conditions are met:\n\n",
                "1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following ",
                "disclaimer.\n\n",
                "2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following ",
                "disclaimer in the documentation and/or other materials provided with the distribution.\n\n",
                "3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote ",
                "products derived from this software without specific prior written permission.\n\n",
                "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\" AND ANY EXPRESS OR IMPLIED WARRANTIES, ",
                "INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE ",
                "DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, ",
                "SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF ",
                "THE POSSIBILITY OF SUCH DAMAGE.\n",
            ]
        case "Apache-2.0":
            parts = [
                "\(holder)\n\n",
                "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance ",
                "with the License. You may obtain a copy of the License at\n\n    http://www.apache.org/licenses/LICENSE-2.0\n\n",
                "Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on ",
                "an \"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the ",
                "specific language governing permissions and limitations under the License.\n",
            ]
        case "CC-BY-4.0":
            parts = [
                "\(holder)\n\n",
                "This work is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0). You are free ",
                "to share and adapt it for any purpose, provided you give appropriate credit, link to the license and indicate if ",
                "changes were made.\n\nThe full license text: https://creativecommons.org/licenses/by/4.0/legalcode\n",
            ]
        default:
            parts = [
                "CC0 1.0 Universal\n\n\(holder)\n\n",
                "To the extent possible under law, the author has waived all copyright and related or neighboring rights to this ",
                "work, dedicating it to the public domain worldwide. This work is distributed without any warranty.\n\n",
                "The full legal code: https://creativecommons.org/publicdomain/zero/1.0/legalcode\n",
            ]
        }
        return parts.joined()
    }

    // MARK: - Helpers

    /// The string a JSON value holds, or nil when it is anything else (absent, null, a number...).
    private static func str(_ v: PackValue?) -> String? { v?.string }

    /// A key's value, nil when it is absent or a JSON null (System.Text.Json reads a null as no node).
    private static func present(_ v: PackValue?) -> PackValue? {
        if case .null? = v { return nil }
        return v
    }

    private static func utf8(_ text: String) -> Data { Data(text.utf8) }

    /// `s` cut to `max` UTF-16 units with "..." after, as the C# prints it.
    private static func clip(_ s: String, _ max: Int) -> String {
        s.utf16.count <= max ? s : String(decoding: Array(s.utf16.prefix(max)), as: UTF16.self) + "..."
    }

    /// C#'s `StringComparer.Ordinal` order: by UTF-16 code units.
    static func ordinalSorted(_ items: [String]) -> [String] {
        items.sorted { Array($0.utf16).lexicographicallyPrecedes(Array($1.utf16)) }
    }

    private static func utcYear(_ date: Date) -> Int {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        return cal.component(.year, from: date)
    }

    static func stamp(_ date: Date) -> String { ISO8601.stamp(date) }

    private static func parseStamp(_ s: String) -> Date? { ISO8601.parse(s) }
}

extension String {
    /// C#'s `EndsWith(x, StringComparison.Ordinal)`: by code units, never by canonical equivalence.
    func hasSuffixOrdinal(_ suffix: String) -> Bool {
        let a = Array(utf16), b = Array(suffix.utf16)
        return a.count >= b.count && Array(a.suffix(b.count)) == b
    }
}

/// Builds and writes JSON trees the way System.Text.Json's `JsonObject` does, on the validator's own value types
/// (`PackValue`, `PackObject`): keys keep their order, a number is written as it was read, and the default encoder's
/// escaping applies (every non-ASCII character as `\uXXXX`, and `< > & ' + "` and the backtick escaped). Indented
/// output is two spaces, `": "`, and `\n` line ends.
enum WidgetJson {
    static func int(_ n: Int) -> PackValue {
        .number(PackNumber(value: Double(n), isInt: true, repr: String(n), raw: String(n)))
    }

    static func object(_ fields: [(String, PackValue)]) -> PackObject {
        let o = PackObject()
        for (k, v) in fields { o.add(k, v) }
        return o
    }

    /// `o` with `key` set: in place when it is already there, else at the end.
    static func setting(_ o: PackObject, _ key: String, _ value: PackValue) -> PackObject {
        let out = PackObject()
        for k in o.keys { out.add(k, k == key ? value : o[k]!) }
        if !o.has(key) { out.add(key, value) }
        return out
    }

    /// `o` with `key` set only when it is absent or null (C#'s `??=`).
    static func defaulting(_ o: PackObject, _ key: String, _ value: PackValue) -> PackObject {
        if let existing = o[key], !existing.isNull { return o }
        return setting(o, key, value)
    }

    static func removing(_ o: PackObject, _ key: String) -> PackObject {
        let out = PackObject()
        for k in o.keys where k != key { out.add(k, o[k]!) }
        return out
    }

    static func compact(_ v: PackValue) -> String {
        var out = ""
        write(v, indent: nil, depth: 0, into: &out)
        return out
    }

    static func indented(_ v: PackValue) -> String {
        var out = ""
        write(v, indent: "  ", depth: 0, into: &out)
        return out
    }

    private static func write(_ v: PackValue, indent: String?, depth: Int, into out: inout String) {
        switch v {
        case .null: out += "null"
        case .bool(let b): out += b ? "true" : "false"
        case .number(let n): out += n.raw
        case .string(let s): out += quote(s)
        case .array(let items):
            if items.isEmpty { out += "[]"; return }
            out += "["
            for (i, item) in items.enumerated() {
                if i > 0 { out += "," }
                newline(indent, depth + 1, &out)
                write(item, indent: indent, depth: depth + 1, into: &out)
            }
            newline(indent, depth, &out)
            out += "]"
        case .object(let o):
            if o.count == 0 { out += "{}"; return }
            out += "{"
            for (i, k) in o.keys.enumerated() {
                if i > 0 { out += "," }
                newline(indent, depth + 1, &out)
                out += quote(k) + (indent == nil ? ":" : ": ")
                write(o[k]!, indent: indent, depth: depth + 1, into: &out)
            }
            newline(indent, depth, &out)
            out += "}"
        }
    }

    private static func newline(_ indent: String?, _ depth: Int, _ out: inout String) {
        guard let indent else { return }
        out += "\n" + String(repeating: indent, count: depth)
    }

    private static func quote(_ s: String) -> String {
        var out = "\""
        for unit in s.utf16 {
            switch unit {
            case 0x08: out += "\\b"
            case 0x09: out += "\\t"
            case 0x0A: out += "\\n"
            case 0x0C: out += "\\f"
            case 0x0D: out += "\\r"
            case 0x5C: out += "\\\\"
            case 0x20...0x7E where !"\"&'+<>`".utf16.contains(unit):
                out.unicodeScalars.append(Unicode.Scalar(unit)!)
            default:
                let h = String(unit, radix: 16, uppercase: true)
                out += "\\u" + String(repeating: "0", count: 4 - h.count) + h
            }
        }
        return out + "\""
    }
}
