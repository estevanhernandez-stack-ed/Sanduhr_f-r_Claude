import Foundation

/// The one secret mask for Mods & Config (item 71, slice 0): every view, diff, receipt display,
/// log line and state.yaml count that shows a Claude Code config value goes through it.
///
/// **By path.** A value is secret when its key path runs through `env` (any variable),
/// `headers` (MCP headers), `oauthAccount`, or any segment naming a token, key, secret,
/// password, credential, auth (not "author"), authorization, bearer or cookie (`apiKeyHelper`,
/// `OTEL_EXPORTER_OTLP_HEADERS`, `refresh_token`, `credentials`). The match is a
/// case-insensitive substring, so it is wider than the seats' merge driver (which matches
/// `api[_-]?key`, not `key`) and catches a plugin id with "key" in its name too: masking a
/// harmless value costs a click, showing a secret costs the secret. Names are kept and values
/// replaced, so a person sees *that* a variable is set, never what it is set to.
///
/// **By value.** Every other string is scanned too, because secrets also sit inside commands
/// (a statusline or hook command, an MCP server's args, a permission rule): the userinfo of a
/// URL, a secret-named query parameter, the value after a secret-named flag (`--token x`,
/// `--api-key=x`, or the next element of an args list), `NAME=value` with a secret-named NAME,
/// an `Authorization:` or `Cookie:` header, `Bearer x`, and key-shaped strings (`sk-ant-…`,
/// `sk-…`, `ghp_…`, `github_pat_…`).
///
/// A secret path is never editable, and a receipt never stores its value.
enum ConfigMask {
    /// What a masked value shows as.
    static let masked = "••••••"

    /// Segments whose whole subtree is secret, matched exactly (case-insensitive).
    static let secretParents: Set<String> = ["env", "headers", "oauthaccount"]

    /// Substrings that make a segment secret wherever it sits (case-insensitive). "auth" is
    /// handled apart, so "author" stays readable.
    static let secretWords = ["token", "key", "secret", "password", "passwd", "credential",
                              "authorization", "bearer", "cookie"]

    /// Whether the value at `path` (keys from the top of the file; array indexes as numbers) is
    /// secret.
    static func isSecret(_ path: [String]) -> Bool {
        path.contains(where: isSecretSegment)
    }

    static func isSecretSegment(_ segment: String) -> Bool {
        let s = segment.lowercased()
        if secretParents.contains(s) { return true }
        if secretWords.contains(where: { s.contains($0) }) { return true }
        return s.contains("auth") && !s.contains("author")
    }

    /// A flag (`--api-key`, `-token`) whose name is secret.
    static func isSecretFlag(_ s: String) -> Bool {
        guard s.hasPrefix("-"), !s.contains("=") else { return false }
        let name = s.drop { $0 == "-" }
        return !name.isEmpty && isSecretSegment(String(name))
    }

    /// Mods & Config may offer an edit at `path` only when it isn't secret.
    static func isEditable(_ path: [String]) -> Bool { !isSecret(path) }

    /// A copy of the JSON value `value` found at `path`, safe to show: objects keep their keys
    /// (a secret key's value is masked whatever its type), arrays keep their length, every leaf
    /// under a secret path is replaced by `masked`, and every other string goes through
    /// `maskString`. Accepts what `JSONSerialization` returns.
    static func mask(_ value: Any, at path: [String] = []) -> Any {
        if isSecret(path) {
            if let o = value as? [String: Any] {
                // Names only: which variables or headers are set, never their values.
                var out: [String: Any] = [:]
                for k in o.keys { out[k] = masked }
                return out
            }
            if let a = value as? [Any] { return a.map { _ in masked } }
            return masked
        }
        if let o = value as? [String: Any] {
            var out: [String: Any] = [:]
            for (k, v) in o { out[k] = mask(v, at: path + [k]) }
            return out
        }
        if let a = value as? [Any] {
            // An args list: the element after a secret-named flag is its value.
            var out: [Any] = []
            var afterFlag = false
            for (i, element) in a.enumerated() {
                if afterFlag, element is String {
                    out.append(masked)
                    afterFlag = false
                    continue
                }
                afterFlag = (element as? String).map(isSecretFlag) ?? false
                out.append(mask(element, at: path + [String(i)]))
            }
            return out
        }
        if let s = value as? String { return maskString(s) }
        return value
    }

    // MARK: Values

    private static let flag = "--?[A-Za-z0-9][A-Za-z0-9_.-]*"
    private static let word = #"("[^"]*"|'[^']*'|[^\s"'&]+)"#

    /// One pattern and how a match is rewritten. Each closure gets the match's groups (0 is the
    /// whole match) and returns the replacement, or nil to leave the match as it is.
    nonisolated(unsafe) private static let rules: [(NSRegularExpression, ([String]) -> String?)] = {
        func re(_ p: String) -> NSRegularExpression {
            // The patterns are constants; a typo is a programming error caught by the tests.
            try! NSRegularExpression(pattern: p, options: [.caseInsensitive])
        }
        return [
            // URL userinfo: scheme://user:pass@host
            (re(#"([a-z][a-z0-9+.-]*://)[^/\s@'"]+@"#), { "\($0[1])\(masked)@" }),
            // A secret-named query parameter: ?token=x, &api_key=x
            (re(#"([?&;])([^=&\s#'"]+)=([^&\s#'"]*)"#), { isSecretSegment($0[2]) ? "\($0[1])\($0[2])=\(masked)" : nil }),
            // An Authorization or Cookie header, whatever follows on the line.
            (re(#"\b(authorization|proxy-authorization|cookie|set-cookie|x-api-key)(\s*:\s*)([^'"\n\r]+)"#),
             { "\($0[1])\($0[2])\(masked)" }),
            // Bearer tokens.
            (re(#"\b(bearer)\s+[^\s'"]+"#), { "\($0[1]) \(masked)" }),
            // --flag=value with a secret-named flag.
            (re("(\(flag))=\(word)"), { isSecretFlag($0[1]) ? "\($0[1])=\(masked)" : nil }),
            // --flag value with a secret-named flag.
            (re(#"(\#(flag))(\s+)(?!-)\#(word)"#), { isSecretFlag($0[1]) ? "\($0[1])\($0[2])\(masked)" : nil }),
            // NAME=value with a secret-named NAME (shell assignments, env prefixes).
            (re(#"(?<![\w-])([A-Za-z_][A-Za-z0-9_]*)=\#(word)"#), { isSecretSegment($0[1]) ? "\($0[1])=\(masked)" : nil }),
            // Key-shaped strings.
            (re(#"\b(sk-ant-|sk-|ghp_|gho_|ghs_|github_pat_|xox[abp]-)[A-Za-z0-9_-]{8,}"#), { _ in masked }),
        ]
    }()

    /// `s` with every secret the value rules find replaced by `masked`.
    static func maskString(_ s: String) -> String {
        var out = s
        for (regex, rewrite) in rules {
            let ns = out as NSString
            var result = ""
            var last = 0
            for m in regex.matches(in: out, range: NSRange(location: 0, length: ns.length)) {
                let groups = (0..<m.numberOfRanges).map { i -> String in
                    let r = m.range(at: i)
                    return r.location == NSNotFound ? "" : ns.substring(with: r)
                }
                guard let replacement = rewrite(groups) else { continue }
                result += ns.substring(with: NSRange(location: last, length: m.range.location - last)) + replacement
                last = m.range.location + m.range.length
            }
            guard last > 0 else { continue }
            out = result + ns.substring(from: last)
        }
        return out
    }

    /// `value` at `path` as one line of display text: the masked JSON, keys sorted.
    static func display(_ value: Any, at path: [String] = []) -> String {
        let safe = mask(value, at: path)
        if let s = safe as? String { return s }
        guard JSONSerialization.isValidJSONObject(safe) || safe is NSNumber || safe is NSNull,
              let data = try? JSONSerialization.data(withJSONObject: safe,
                                                     options: [.sortedKeys, .withoutEscapingSlashes, .fragmentsAllowed]) else {
            return masked
        }
        return String(decoding: data, as: UTF8.self)
    }
}
