import Foundation

/// The meters mod's options (item 75, slice D): the `userConfig` its manifest declares, which
/// Claude Code hands to `register(on, options)` from `pluginConfigs["sanduhr-meters@inline"]
/// .options` in a Claude Code folder's own settings.json (user scope only: project and local
/// entries are ignored, so a cloned repository can't set them). The mod reads them when it loads,
/// so a change applies to sessions started after it.
struct MetersOptions: Equatable, Sendable {
    typealias Bars = BandFrame.Bars
    typealias Style = BandFrame.Style
    enum Motion: String, CaseIterable, Sendable { case on, off }

    var bars: Bars = .both
    var style: Style = .compact
    /// A few words before the meters; empty shows none.
    var label = ""
    var motion: Motion = .on

    static let defaults = MetersOptions()

    var isDefault: Bool { normalized == .defaults }

    /// As the mod reads it: the label trimmed.
    var normalized: MetersOptions {
        var o = self
        o.label = label.trimmingCharacters(in: .whitespacesAndNewlines)
        return o
    }

    /// Read as the mod reads them: a value it doesn't know is its default.
    init(bars: Bars = .both, style: Style = .compact, label: String = "", motion: Motion = .on) {
        self.bars = bars
        self.style = style
        self.label = label
        self.motion = motion
    }

    init(options: [String: Any]) {
        bars = (options["bars"] as? String).flatMap(Bars.init(rawValue:)) ?? .both
        style = (options["style"] as? String).flatMap(Style.init(rawValue:)) ?? .compact
        label = (options["label"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        motion = (options["motion"] as? String).flatMap(Motion.init(rawValue:)) ?? .on
    }

    /// The `options` object as written, every field named.
    var written: [String: String] {
        let o = normalized
        return ["bars": o.bars.rawValue, "style": o.style.rawValue, "label": o.label, "motion": o.motion.rawValue]
    }

    /// The member's value: `{ "options": { … } }`, in the manifest's order.
    var value: JSONEdit.Value {
        let o = normalized
        return .object([JSONEdit.Pair("options", .object([
            JSONEdit.Pair("bars", .string(o.bars.rawValue)),
            JSONEdit.Pair("style", .string(o.style.rawValue)),
            JSONEdit.Pair("motion", .string(o.motion.rawValue)),
            JSONEdit.Pair("label", .string(o.label)),
        ]))])
    }
}

/// The mod's options in one folder's settings.json.
struct MetersOptionsState: Equatable, Sendable {
    /// What the mod gets: the member's options, or the defaults without one.
    var values = MetersOptions.defaults
    /// `pluginConfigs` has a member for the mod.
    var isSet = false
    /// Sanduhr's receipt wrote that member and it is still what Sanduhr wrote.
    var isOurs = false

    /// Set by someone else (Claude Code's /config, or by hand): Save leaves it.
    var isTheirs: Bool { isSet && !isOurs }
}

/// The `pluginConfigs` member Save wrote, so changing it again, Reset or Remove undoes exactly
/// that. Sanduhr writes the member only where there was none, so undo removes it.
struct OptionsReceipt: Codable, Equatable, Sendable {
    /// The options as written.
    var written: [String: String]
    /// `pluginConfigs` didn't exist: undo removes it once it is empty again.
    var createdParent = false
    /// What sat between the braces of the object the member went into when it was empty.
    var emptyInner: String?
}

/// What Save did in one folder.
enum OptionsOutcome: Equatable, Sendable {
    case written
    /// Back to the defaults: Sanduhr's member came out, the file is as it was before.
    case removed
    case unchanged
    /// The folder already had options Sanduhr didn't write: they stay.
    case keptTheirs
}

extension IntegrationInstaller {
    static let pluginConfigsKey = "pluginConfigs"

    func ownModOptions(folder: String) -> MetersOptionsState {
        let file = configFile(.meters, folder: folder)
        guard let data = FileManager.default.contents(atPath: file), let root = try? JSONEdit.root(data) else {
            return MetersOptionsState()
        }
        let receipt = loadReceipts().first { $0.file == file && $0.kind == .meters }?.options
        return Self.optionsState(root, receipt: receipt)
    }

    static func optionsState(_ root: [String: Any], receipt: OptionsReceipt?) -> MetersOptionsState {
        guard let configs = root[pluginConfigsKey] as? [String: Any], let member = configs[ownModKey] else {
            return MetersOptionsState()
        }
        var state = MetersOptionsState(isSet: true)
        if let o = member as? [String: Any], let options = o["options"] as? [String: Any] {
            state.values = MetersOptions(options: options)
        }
        if let receipt { state.isOurs = isOurs(member, receipt) }
        return state
    }

    /// The member is exactly what the receipt says Sanduhr wrote.
    static func isOurs(_ member: Any, _ receipt: OptionsReceipt) -> Bool {
        guard let o = member as? [String: Any], o.count == 1, let options = o["options"] as? [String: Any] else { return false }
        return NSDictionary(dictionary: options).isEqual(to: receipt.written)
    }

    /// Save: writes `options` into the folder's settings.json, or takes Sanduhr's member out when
    /// they are the defaults. A member Sanduhr didn't write is left as it is.
    @discardableResult
    func setOwnModOptions(_ options: MetersOptions, folder: String) throws -> OptionsOutcome {
        let file = configFile(.meters, folder: folder)
        let state = ownModOptions(folder: folder)
        if state.isTheirs { return .keptTheirs }
        if options.isDefault {
            guard state.isOurs else { return .unchanged }
            try undoModOptions(folder: folder)
            return .removed
        }
        if state.isOurs && state.values == options.normalized { return .unchanged }
        var receipts = loadReceipts()
        let i = receipts.firstIndex { $0.file == file && $0.kind == .meters }
        let prior = i.flatMap { receipts[$0].options }
        for _ in 0..<3 {
            guard let original = try read(file) else { throw Failure.malformed(file: file) }
            let (bytes, made) = try malformedNamed(file) {
                try planOptions(bytes: original, options: options, prior: state.isOurs ? prior : nil)
            }
            guard try commit(file, original: original, bytes: bytes, backup: true) else { continue }
            if let i {
                receipts[i].options = made
            } else {
                receipts.append(IntegrationReceipt(folder: AccountData.normalized(folder), file: file, kind: .meters,
                                                   options: made))
            }
            saveReceipts(receipts)
            return .written
        }
        throw Failure.writeFailed(file: file)
    }

    /// Takes out the member Save wrote, back to the file's old bytes. A member someone changed
    /// since is left as it is; Save's record goes either way.
    func undoModOptions(folder: String) throws {
        let file = configFile(.meters, folder: folder)
        var receipts = loadReceipts()
        guard let i = receipts.firstIndex(where: { $0.file == file && $0.kind == .meters }),
              let made = receipts[i].options else { return }
        var done = false
        for _ in 0..<3 {
            guard let original = try read(file) else { done = true; break }
            guard let edit = try malformedNamed(file, { try planUndoOptions(bytes: original, made) }) else {
                done = true
                break
            }
            if try commit(file, original: original, bytes: edit, backup: false) {
                done = true
                break
            }
        }
        guard done else { throw Failure.writeFailed(file: file) }
        receipts[i].options = nil
        // A receipt that held only Save's options has nothing left to undo.
        if !receipts[i].recordsList && receipts[i].switched == nil { receipts.remove(at: i) }
        saveReceipts(receipts)
    }

    /// The edit, and the receipt. Replacing Sanduhr's own member keeps the first write's record
    /// of what the file was (`prior`).
    private func planOptions(bytes b: [UInt8], options: MetersOptions, prior: OptionsReceipt?) throws -> ([UInt8], OptionsReceipt) {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        var made = OptionsReceipt(written: options.written)
        let result: [UInt8]
        if let m = top.member(Self.pluginConfigsKey) {
            guard let configs = root[Self.pluginConfigsKey] as? [String: Any] else { throw Failure.malformed(file: "") }
            // Only Sanduhr's own member is ever replaced.
            if let member = configs[Self.ownModKey] {
                guard let prior, Self.isOurs(member, prior) else { throw Failure.malformed(file: "") }
            }
            let r = JSONEdit.set(b, in: try JSONEdit.object(b, at: m.valueStart), key: Self.ownModKey, value: options.value)
            made.createdParent = prior?.createdParent ?? false
            made.emptyInner = prior?.emptyInner ?? r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
            result = r.bytes
        } else {
            let r = JSONEdit.set(b, in: top, key: Self.pluginConfigsKey,
                                 value: .object([JSONEdit.Pair(Self.ownModKey, options.value)]))
            made.createdParent = true
            made.emptyInner = r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
            result = r.bytes
        }
        try verifyOptions(before: b, after: result, expect: options.written)
        return (result, made)
    }

    /// The undo's edit, nil when the member isn't what Save wrote any more.
    private func planUndoOptions(bytes b: [UInt8], _ made: OptionsReceipt) throws -> [UInt8]? {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        guard let m = top.member(Self.pluginConfigsKey), let configs = root[Self.pluginConfigsKey] as? [String: Any],
              let member = configs[Self.ownModKey], Self.isOurs(member, made) else { return nil }
        let object = try JSONEdit.object(b, at: m.valueStart)
        let inner = made.emptyInner.map { Array($0.utf8) }
        var result: [UInt8]
        if made.createdParent {
            result = JSONEdit.remove(b, in: object, key: Self.ownModKey)
            let after = try JSONEdit.topObject(result)
            if let pm = after.member(Self.pluginConfigsKey), (try JSONEdit.object(result, at: pm.valueStart)).members.isEmpty {
                result = JSONEdit.remove(result, in: after, key: Self.pluginConfigsKey, emptyInner: inner)
            }
        } else {
            result = JSONEdit.remove(b, in: object, key: Self.ownModKey, emptyInner: inner)
        }
        try verifyOptions(before: b, after: result, expect: nil)
        return result
    }

    /// The edit changed Sanduhr's one member and nothing else (a `pluginConfigs` left empty counts
    /// as absent), and the member holds `expect` (absent for nil).
    private func verifyOptions(before: [UInt8], after: [UInt8], expect: [String: String]?) throws {
        let old = try parse(before)
        guard let new = try? JSONEdit.root(Data(after)) else { throw Failure.malformed(file: "") }
        func split(_ root: [String: Any]) -> (rest: [String: Any], others: [String: Any], value: Any?) {
            var rest = root
            var configs = rest.removeValue(forKey: Self.pluginConfigsKey) as? [String: Any] ?? [:]
            let value = configs.removeValue(forKey: Self.ownModKey)
            return (rest, configs, value)
        }
        let (oldRest, oldOthers, _) = split(old)
        let (newRest, newOthers, got) = split(new)
        guard NSDictionary(dictionary: newRest).isEqual(to: oldRest),
              NSDictionary(dictionary: newOthers).isEqual(to: oldOthers) else { throw Failure.malformed(file: "") }
        switch (got, expect) {
        case (nil, nil): return
        case (let g?, let e?):
            guard Self.isOurs(g, OptionsReceipt(written: e)) else { throw Failure.malformed(file: "") }
        default:
            throw Failure.malformed(file: "")
        }
    }
}
