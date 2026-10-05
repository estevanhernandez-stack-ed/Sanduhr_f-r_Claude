import Foundation

/// Claude's suggested themes (item 55), the pure half: the request the MCP server's
/// `propose_theme` writes (the Windows tool's theme-request.json), the checks the app runs again
/// (ThemeLint; the app is the authority), where the theme is saved, and the result the server
/// waits for. ThemeProposalHandoff does the files.
///
/// The server mirrors the file names and the built-in keys (`mac/integrations/sanduhr_mcp.py`);
/// a test on each side pins them.
struct ThemeProposal: Equatable, Identifiable {
    let id: String
    let requestedAt: Date
    /// The theme as it will be written: the request's object with the gaps the Mac's loader
    /// cannot take filled in (`normalized`), pretty-printed with sorted keys.
    let file: Data
    let name: String
    /// The theme's `description`: the line its gallery card's tooltip shows, and the banner's note.
    let summary: String?
    /// `save_as`, or the name slugged: the key asked for, before a collision moves it.
    let key: String
    let apply: Bool
    /// The lint's warnings (a proposal with errors never gets here).
    let findings: [ThemeFinding]

    static let requestFile = "theme-request.json"
    static let resultFile = "theme-result.json"
    /// A proposal is interactive: one older than this (Sanduhr was not running) is dropped, as on
    /// Windows.
    static let maxAge: TimeInterval = 600
    /// A request file larger than this is never valid (the server sends themes under 16 KB).
    static let maxRequestBytes = 64 * 1024

    /// The compiled-in themes' keys: a proposal never takes one.
    static var reservedKeys: Set<String> { Set(ThemeRegistry.builtIn.map(\.id)) }

    /// The theme as the widget would draw it, for the preview card.
    func preview(id: String? = nil) -> Theme? { UserThemes.decodeTheme(file, id: id ?? key) }

    // MARK: - Reading the request

    enum Decoded: Equatable {
        case proposal(ThemeProposal)
        /// Readable enough to answer: the result names the id.
        case refused(id: String, Outcome)
        /// Not a request (unreadable, no id) or too old: dropped without an answer.
        case discard
    }

    static func decode(_ data: Data, now: Date, reserved: Set<String> = reservedKeys) -> Decoded {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let id = root["id"] as? String, !id.isEmpty, id.count <= 64 else { return .discard }
        guard let stamp = root["requested_at"] as? String, let at = MessageProposal.parseDate(stamp) else {
            return .refused(id: id, .refusal("invalid_params", "requested_at is missing or not a date."))
        }
        if now.timeIntervalSince(at) > maxAge { return .discard }
        guard (ThemeLint.number(root["schema_version"]) ?? 1) == 1 else {
            return .refused(id: id, .refusal("invalid_params", "This Sanduhr reads requests of schema 1."))
        }
        guard let theme = root["theme"] as? [String: Any] else {
            return .refused(id: id, .refusal("invalid_params", "theme must be an object: the theme JSON per docs/themes/template.json."))
        }
        var saveAs: String?
        if let raw = ThemeLint.present(root["save_as"]) {
            guard let s = raw as? String, ThemeLint.isValidKey(s) else {
                return .refused(id: id, .refusal("invalid_params", "save_as must be 1-40 lowercase letters, digits or hyphens, not starting with a hyphen (omit it to slug the name)."))
            }
            saveAs = s
        }
        // As on Windows: anything but false applies.
        let apply = !(ThemeLint.isBool(root["apply"]) && (root["apply"] as? Bool) == false)

        let findings = ThemeLint.lint(theme)
        guard ThemeLint.ok(findings) else {
            return .refused(id: id, .refusal("invalid_theme", "Fix the fields named in findings and call again.", findings: findings))
        }
        let name = (theme["name"] as? String) ?? ""
        let key = saveAs ?? ThemeLint.slug(name)
        guard !reserved.contains(key) else {
            return .refused(id: id, .refusal("reserved_name", "'\(key)' is a built-in theme; pick another name or pass save_as.", findings: findings))
        }
        let normal = normalized(theme)
        guard let file = try? JSONSerialization.data(withJSONObject: normal, options: [.prettyPrinted, .sortedKeys]),
              UserThemes.decodeTheme(file, id: key) != nil else {
            return .refused(id: id, .refusal("invalid_theme", "Sanduhr could not read the theme; check the field types.", findings: findings))
        }
        return .proposal(ThemeProposal(id: id, requestedAt: at, file: file, name: name,
                                       summary: ThemeGalleryItem.cleaned(normal["description"] as? String),
                                       key: key, apply: apply, findings: findings))
    }

    /// The theme with what the Mac's loader needs and the Windows schema leaves open: a partial
    /// accent_bloom or inner_highlight gets the defaults (as Windows reads them), breath_period_ms
    /// a whole number, a blank description goes.
    static func normalized(_ theme: [String: Any]) -> [String: Any] {
        var out = theme
        if let ms = ThemeLint.number(theme["breath_period_ms"]) { out["breath_period_ms"] = Int(ms.rounded()) }
        if var ab = theme["accent_bloom"] as? [String: Any] {
            if ThemeLint.number(ab["blur"]) == nil { ab["blur"] = 4 }
            if ThemeLint.number(ab["alpha"]) == nil { ab["alpha"] = 0.45 }
            out["accent_bloom"] = ab
        }
        if var ih = theme["inner_highlight"] as? [String: Any] {
            if ThemeLint.number(ih["alpha"]) == nil { ih["alpha"] = 0.20 }
            out["inner_highlight"] = ih
        }
        if let d = theme["description"] as? String {
            let t = d.trimmingCharacters(in: .whitespaces)
            if t.isEmpty { out.removeValue(forKey: "description") } else { out["description"] = t }
        }
        return out
    }

    // MARK: - Where it is saved

    struct Placement: Equatable {
        /// The file key it is saved under.
        let key: String
        /// The key asked for, when another theme of the user's had it.
        let renamedFrom: String?
        /// The file under `key` already holds this very theme: saving changes nothing.
        let sameAsExisting: Bool
    }

    /// Where the theme goes. A user theme is never overwritten: when the key's file holds a
    /// different theme, the next free key (`ocean-2`, `ocean-3`, … up to 99) is used instead. A
    /// file that already holds the same theme is reused, so proposing the same theme twice adds
    /// nothing. `existing` reads the user's theme file for a key, nil when there is none.
    static func placement(for p: ThemeProposal, reserved: Set<String> = reservedKeys,
                          existing: (String) -> Data?) -> Placement? {
        let mine = (try? JSONSerialization.jsonObject(with: p.file)) as? NSDictionary
        for n in 1...99 {
            let candidate = n == 1 ? p.key : suffixed(p.key, n)
            if reserved.contains(candidate) { continue }
            let renamed = n == 1 ? nil : p.key
            guard let data = existing(candidate) else {
                return Placement(key: candidate, renamedFrom: renamed, sameAsExisting: false)
            }
            if let theirs = (try? JSONSerialization.jsonObject(with: data)) as? NSDictionary, let mine, theirs.isEqual(mine) {
                return Placement(key: candidate, renamedFrom: renamed, sameAsExisting: true)
            }
        }
        return nil
    }

    /// `key-n`, the key shortened so the whole stays within 40 characters.
    static func suffixed(_ key: String, _ n: Int) -> String {
        let tail = "-\(n)"
        var base = String(key.prefix(40 - tail.count))
        while base.hasSuffix("-") { base.removeLast() }
        return base + tail
    }

    // MARK: - The result

    enum Status: String, Equatable {
        case applied, saved, rejected, error
        case pendingApproval = "pending_approval"
    }

    /// The typed tool result (the Windows ThemeProposals payload, plus `renamed_from`).
    struct Outcome: Equatable {
        var status: Status
        var reason: String?
        var remedy: String?
        var key: String?
        var name: String?
        var previousKey: String?
        var savedPath: String?
        var renamedFrom: String?
        var findings: [ThemeFinding] = []

        static func refusal(_ reason: String, _ remedy: String, findings: [ThemeFinding] = []) -> Outcome {
            Outcome(status: .rejected, reason: reason, remedy: remedy, findings: findings)
        }
    }

    /// The result file: `{id, completed_at, result: {status, reason, remedy, key, name,
    /// previous_key, saved_path, renamed_from, findings}}`, absent fields left out.
    static func resultJSON(id: String, _ o: Outcome, now: Date = Date()) -> Data {
        var result: [String: Any] = ["status": o.status.rawValue, "findings": ThemeLint.json(o.findings)]
        let optional: [(String, String?)] = [
            ("reason", o.reason), ("remedy", o.remedy), ("key", o.key), ("name", o.name),
            ("previous_key", o.previousKey), ("saved_path", o.savedPath), ("renamed_from", o.renamedFrom),
        ]
        for (k, v) in optional { if let v { result[k] = v } }
        let root: [String: Any] = ["id": id, "completed_at": HandoffFiles.stamp(now), "result": result]
        return (try? JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])) ?? Data()
    }
}
