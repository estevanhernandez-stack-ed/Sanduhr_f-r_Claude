import Foundation
import Testing
@testable import Sanduhr

/// Item 44: the per-account data choices, the one-folder-one-account rule, and their following
/// renames and removals. In-memory defaults and made-up paths only.
@Suite("Account data choices")
struct AccountDataTests {
    @Test func defaultsAreTheSpecs() {
        let d = MemoryDefaults()
        let c = AccountData.choices(for: "Work", in: d)
        #expect(c == AccountDataChoices(activity: .off, names: .names, share: .off, folder: nil))
        #expect(c == .defaults)
        #expect(d.object(forKey: AccountData.key) == nil)
    }

    @Test func eachChoiceIsKeptPerAccount() {
        let d = MemoryDefaults()
        AccountData.setActivity(.record, for: "Work", in: d)
        AccountData.setNames(.hidden, for: "Work", in: d)
        AccountData.setShare(.meters, for: "Home", in: d)
        #expect(AccountData.choices(for: "Work", in: d)
                == AccountDataChoices(activity: .record, names: .hidden, share: .off))
        #expect(AccountData.choices(for: "Home", in: d) == AccountDataChoices(share: .meters))
        let raw = d.object(forKey: AccountData.key) as? [String: [String: String]]
        #expect(raw?["Work"] == ["activity": "record", "names": "hidden", "share": "off"])
        #expect(raw?["Home"] == ["activity": "off", "names": "names", "share": "meters"])
    }

    @Test func storedValuesUseTheSpecNames() {
        #expect(ActivityChoice.allCases.map(\.rawValue) == ["off", "live", "record"])
        #expect(ProjectNamesChoice.allCases.map(\.rawValue) == ["names", "hidden", "full"])
        #expect(ShareChoice.allCases.map(\.rawValue) == ["off", "meters", "activity"])
        #expect(ActivityChoice.allCases.map(\.title) == ["Not tracked", "Live only", "Keep a record"])
        #expect(ProjectNamesChoice.allCases.map(\.title) == ["Names", "Hidden", "Full paths"])
        #expect(ShareChoice.allCases.map(\.title) == ["Off", "Meters", "Meters and activity"])
    }

    @Test func backToTheDefaultsDropsTheEntry() {
        let d = MemoryDefaults()
        AccountData.setShare(.activity, for: "Work", in: d)
        AccountData.setShare(.off, for: "Work", in: d)
        #expect(d.object(forKey: AccountData.key) == nil)
    }

    @Test func unknownValuesReadAsTheDefaults() {
        let d = MemoryDefaults()
        d.set(["Work": ["activity": "everything", "names": "live", "share": "meters", "folder": ""]],
              forKey: AccountData.key)
        #expect(AccountData.choices(for: "Work", in: d) == AccountDataChoices(share: .meters))
        d.set("not a dictionary", forKey: AccountData.key)
        #expect(AccountData.choices(for: "Work", in: d) == .defaults)
    }

    @Test func noAccountsKeepsTheChoicesUnderPersonal() {
        let d = MemoryDefaults()
        AccountData.setActivity(.live, for: nil, in: d)
        #expect(AccountData.choices(for: "Personal", in: d).activity == .live)
    }

    // MARK: Links

    @Test func anAccountLinksOneFolder() {
        let d = MemoryDefaults()
        #expect(AccountData.link("/Users/u/.claude", to: "Work", in: d) == .linked)
        #expect(AccountData.link("/Users/u/.claude-work", to: "Work", in: d) == .linked)
        #expect(AccountData.choices(for: "Work", in: d).folder == "/Users/u/.claude-work")
        // The first folder is free again.
        #expect(AccountData.account(linkedTo: "/Users/u/.claude", in: d) == nil)
        #expect(AccountData.link("/Users/u/.claude", to: "Home", in: d) == .linked)
    }

    @Test func aFolderLinksOneAccount() {
        let d = MemoryDefaults()
        AccountData.link("/Users/u/.claude-work", to: "Work", in: d)
        #expect(AccountData.link("/Users/u/.claude-work/", to: "Home", in: d) == .linkedElsewhere("Work"))
        // Refused: nothing changed.
        #expect(AccountData.choices(for: "Home", in: d).folder == nil)
        #expect(AccountData.account(linkedTo: "/Users/u/.claude-work", in: d) == "Work")
        // Relinking the same account to it is fine.
        #expect(AccountData.link("/Users/u/.claude-work", to: "Work", in: d) == .linked)
    }

    @Test func movingAFolderUnlinksItsAccount() {
        let d = MemoryDefaults()
        AccountData.link("/Users/u/.claude-work", to: "Work", in: d)
        AccountData.setActivity(.live, for: "Work", in: d)
        #expect(AccountData.link("/Users/u/.claude-work", to: "Home", move: true, in: d) == .linked)
        #expect(AccountData.account(linkedTo: "/Users/u/.claude-work", in: d) == "Home")
        #expect(AccountData.choices(for: "Work", in: d) == AccountDataChoices(activity: .live))
    }

    @Test func pathsCompareStandardized() {
        let d = MemoryDefaults()
        AccountData.link("/Users/u/x/../.claude-a/", to: "Work", in: d)
        #expect(AccountData.choices(for: "Work", in: d).folder == "/Users/u/.claude-a")
        #expect(AccountData.account(linkedTo: "/Users/u/.claude-a", in: d) == "Work")
    }

    @Test func unlinkKeepsTheOtherChoices() {
        let d = MemoryDefaults()
        AccountData.link("/Users/u/.claude", to: "Work", in: d)
        AccountData.setShare(.meters, for: "Work", in: d)
        AccountData.unlink("Work", in: d)
        #expect(AccountData.choices(for: "Work", in: d) == AccountDataChoices(share: .meters))
        AccountData.setShare(.off, for: "Work", in: d)
        #expect(d.object(forKey: AccountData.key) == nil)
    }

    // MARK: Rename and remove

    @Test func choicesAndTheLinkFollowARename() throws {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "k1", "sessionKey:Home": "k2"],
                                labels: ["Work", "Home"], active: "Work")
        AccountData.link("/Users/u/.claude-work", to: "Work", in: f.defaults)
        AccountData.setActivity(.record, for: "Work", in: f.defaults)
        AccountData.setShare(.meters, for: "Home", in: f.defaults)
        try f.registry.rename("Work", to: "Client")
        #expect(AccountData.choices(for: "Client", in: f.defaults)
                == AccountDataChoices(activity: .record, folder: "/Users/u/.claude-work"))
        #expect(AccountData.choices(for: "Work", in: f.defaults) == .defaults)
        #expect(AccountData.account(linkedTo: "/Users/u/.claude-work", in: f.defaults) == "Client")
        #expect(AccountData.choices(for: "Home", in: f.defaults).share == .meters)
    }

    @Test func removeForgetsTheChoicesAndFreesTheFolder() {
        let f = RegistryFixture(keychain: ["sessionKey:Work": "k1", "sessionKey:Home": "k2"],
                                labels: ["Work", "Home"], active: "Home")
        AccountData.link("/Users/u/.claude-work", to: "Work", in: f.defaults)
        AccountData.setShare(.activity, for: "Work", in: f.defaults)
        f.registry.remove("Work")
        #expect(AccountData.choices(for: "Work", in: f.defaults) == .defaults)
        #expect(AccountData.account(linkedTo: "/Users/u/.claude-work", in: f.defaults) == nil)
        #expect(f.defaults.object(forKey: AccountData.key) == nil)
    }

    // MARK: state.yaml

    @Test func stateYAMLShowsChoicesNeverThePath() {
        let c = AccountDataChoices(activity: .live, names: .hidden, share: .meters,
                                   folder: "/Users/someone/.claude-secret")
        let yaml = YAMLEmitter.emit(.map([YAMLPair("data", DebugState.accountDataYAML(c))]))
        #expect(yaml == "data:\n  activity: live\n  names: hidden\n  share: meters\n  folder_linked: true\n")
        #expect(!yaml.contains("secret"))
        let none = YAMLEmitter.emit(.map([YAMLPair("data", DebugState.accountDataYAML(.defaults))]))
        #expect(none.contains("folder_linked: false"))
    }
}
