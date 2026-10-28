import Foundation

/// What the app tells the MCP server's caller about a proposed design.
struct DeskAnswer: Equatable {
    var status: String
    var reason: String?
    var reasons: [DeskReason]?
    var fonts: [MissingFont]?
    var changes: [String]?

    init(_ status: String, reason: String? = nil, reasons: [DeskReason]? = nil, fonts: [MissingFont]? = nil, changes: [String]? = nil) {
        self.status = status
        self.reason = reason
        self.reasons = reasons
        self.fonts = fonts
        self.changes = changes
    }

    static let previewing = "previewing"
    static let applied = "applied"
    static let rejected = "rejected"
    static let needsFont = "needs_font"

    /// The statuses the app may give (the server passes nothing else on).
    static let statuses = [previewing, applied, rejected, needsFont]

    /// The answer for a design that failed the rules.
    static func refused(_ reasons: [DeskReason]) -> DeskAnswer { DeskAnswer(rejected, reasons: reasons) }

    /// What a Sanduhr with no review wired answers a good proposal (tests, the harness).
    static let notReady = DeskAnswer(rejected, reason: "not_ready",
        reasons: [DeskReason("design", "not_ready: this version of Sanduhr checks designs but cannot preview them yet")])
}

/// What `DeskDesignHandoff.decode` made of a request file.
enum DeskRequestKind: Equatable { case proposal, refused, discard }

/// One decoded request: a proposal (the design checked again), a refusal with reasons, or nothing to answer.
struct DeskRequest {
    let kind: DeskRequestKind
    let id: String?
    let validation: DeskValidation?
    let reasons: [DeskReason]

    static let discard = DeskRequest(kind: .discard, id: nil, validation: nil, reasons: [])
}

/// Claude's proposed Desk designs, the file half (spec 8): a port of Windows' `DeskDesignHandoff` for the Mac.
/// The MCP server's `propose_desk` validates the design and drops `desk-request.json` into Sanduhr's folder;
/// the app reads it with `decode`, which checks the design again (the app is the authority; it never trusts the
/// server) and answers in `desk-result.json`. A request older than ten minutes (Sanduhr was not running) or
/// larger than the cap is dropped without an answer. Pure: bytes in, a verdict and bytes out. No UI and no apply
/// here; the review that previews and applies a design is the next item.
enum DeskDesignHandoff {
    static let requestFile = "desk-request.json"
    static let resultFile = "desk-result.json"
    static let stateFile = "desk-state.json"
    static let schemaVersion = 1

    /// A request older than this is dropped unanswered, as the other handoffs do.
    static let maxAge: TimeInterval = 600

    /// The request file's cap: the design's own cap plus the envelope.
    static let maxRequestBytes = DeskDesign.maxJsonBytes + 1024

    /// Reads a request: `{schema_version, id, requested_at, design}`. Unreadable, without an id, or too old is
    /// dropped; a design that breaks a rule is refused with the reasons; a valid one (or one that needs a font)
    /// is a proposal.
    static func decode(_ data: Data, now: Date, context: DeskDesignContext = .unknown) -> DeskRequest {
        if data.count > maxRequestBytes { return .discard }
        var bytes = [UInt8](data)
        if bytes.starts(with: [0xEF, 0xBB, 0xBF]) { bytes.removeFirst(3) }
        guard let scalars = PackJson.decodeUTF8(bytes), case .success(let root) = PackJson.parse(scalars),
              let o = root.object else { return .discard }
        guard let id = o["id"]?.string, id.utf16.count > 0, id.utf16.count <= 64 else { return .discard }
        guard let stampText = o["requested_at"]?.string, let at = ISO8601.parse(stampText) else {
            return refused(id, DeskReason("requested_at", "is missing or not a date"))
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard let sv = o["schema_version"]?.number, sv.isInt, sv.value == Double(schemaVersion) else {
            return refused(id, DeskReason("schema_version", "this Sanduhr reads requests of schema 1"))
        }
        let verdict = DeskDesign.validate(o["design"], context: context)
        if verdict.status == .rejected {
            return DeskRequest(kind: .refused, id: id, validation: verdict, reasons: verdict.reasons)
        }
        return DeskRequest(kind: .proposal, id: id, validation: verdict, reasons: [])
    }

    private static func refused(_ id: String, _ reason: DeskReason) -> DeskRequest {
        DeskRequest(kind: .refused, id: id, validation: nil, reasons: [reason])
    }

    /// The result file's contents for an answer: `{completed_at, id, result: {status, reason?, reasons?,
    /// fonts?, changes?}}`.
    static func resultJson(id: String, answer: DeskAnswer, now: Date) -> Data {
        var fields: [(String, PackValue)] = [("status", .string(answer.status))]
        if let reason = answer.reason { fields.append(("reason", .string(reason))) }
        if let reasons = answer.reasons, !reasons.isEmpty {
            fields.append(("reasons", .array(reasons.map { .object($0.toJson()) })))
        }
        if let fonts = answer.fonts, !fonts.isEmpty { fields.append(("fonts", .array(fonts.map { .object($0.toJson()) }))) }
        if let changes = answer.changes, !changes.isEmpty { fields.append(("changes", .array(changes.map { .string($0) }))) }
        let root = WidgetJson.object([
            ("completed_at", .string(ISO8601.stamp(now))),
            ("id", .string(id)),
            ("result", .object(WidgetJson.object(fields))),
        ])
        return Data(WidgetJson.compact(.object(root)).utf8)
    }
}
