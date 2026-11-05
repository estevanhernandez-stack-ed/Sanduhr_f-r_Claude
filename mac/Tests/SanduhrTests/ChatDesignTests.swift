import Foundation
import Testing
@testable import Sanduhr

/// Design it with an AI chat (setup spec, section 12): the window model over a fake pasteboard, the Desk's own validator and
/// review (a scratch world, no Desk window) and a drawer service on temp folders. Nothing here touches the real Desktop.
@MainActor
private struct Chat {
    let world = DeskDesignReviewTests.World()
    let fx = DrawerFixture()
    let scratch = DrawerScratchDefaults()
    let service: DrawerService
    let model: ChatDesignViewModel
    let tidied = Box()
    let copied = Box()
    var validated: [PackValue] { boxed.items }
    let boxed = PackBox()

    final class Box { var n = 0; var text: String? }
    final class PackBox { var items: [PackValue] = [] }

    init(enabled: Bool = false, withDesk: Bool = true, canCopy: Bool = true) {
        if enabled { var s = DrawerSettings(); s.enabled = true; DrawerPrefs.save(s, to: scratch.defaults) }
        let service = DrawerService(defaults: scratch.defaults, desktop: fx.desktop, home: fx.home, store: fx.store, turnDeskOn: {})
        self.service = service
        let tidy = tidied, copy = copied, seen = boxed, world = world
        let applier = ChatDrawerApplier(service: service, tidy: { tidy.n += 1 })
        model = ChatDesignViewModel(
            copy: { copy.text = $0; return canCopy },
            validate: { seen.items.append($0); return world.handoff.validatePasted($0) },
            previewDesk: withDesk ? { world.handoff.proposePasted($0) } : nil,
            drawers: applier)
    }

    func paste(_ text: String) { model.pasteText = text }
}

private let deskOnly = #"{"desk":{"name":"Calm","plate":true}}"#
private let rulesOnly = #"{"drawers":{"rules":[{"kind":"documents","drawer":"Papers"}]}}"#
private let lookOnly = #"{"drawers":{"look":"glass","opacity":70,"arrange":{"corner":"bl"}}}"#
private let all = #"{"desk":{"name":"Calm","plate":true},"drawers":{"rules":[{"kind":"documents","drawer":"Papers"}],"look":"bare"}}"#

@MainActor
@Suite("Design it with an AI chat: the window model")
struct ChatDesignTests {
    @Test("each button enables only for the part the paste held and passed")
    func enablement() {
        let c = Chat()
        #expect(!c.model.canPreview && !c.model.canUseRules && !c.model.canUseDrawerSettings)
        c.paste(deskOnly)
        #expect(c.model.canPreview && !c.model.canUseRules && !c.model.canUseDrawerSettings)
        c.paste(rulesOnly)
        #expect(!c.model.canPreview && c.model.canUseRules && !c.model.canUseDrawerSettings)
        c.paste(lookOnly)
        #expect(!c.model.canPreview && !c.model.canUseRules && c.model.canUseDrawerSettings)
        c.paste(all)
        #expect(c.model.canPreview && c.model.canUseRules && c.model.canUseDrawerSettings)
        #expect(c.model.summary == SetupCopy.chatReady)
        c.paste("")
        #expect(!c.model.canPreview && c.model.summary.isEmpty && c.model.findings.isEmpty)
    }

    @Test("nothing is enabled while anything in the paste is wrong, and the fix count says how many")
    func errorsBlockEverything() {
        let c = Chat()
        c.paste(#"{"desk":{"name":"Calm","plate":"yes"},"drawers":{"rules":[{"kind":"documents","drawer":"Papers"}]}}"#)
        #expect(!c.model.canPreview && !c.model.canUseRules && !c.model.canUseDrawerSettings)
        #expect(c.model.findings.contains { $0.field == "plate" && $0.isError })
        #expect(c.model.summary == SetupCopy.chatFixCount(c.model.findings.count))
        c.paste("not a block")
        #expect(c.model.summary == SetupCopy.chatFixCount(1))
    }

    @Test("a desk-less Mac cannot preview")
    func noDesk() {
        let c = Chat(withDesk: false)
        c.paste(deskOnly)
        #expect(!c.model.canPreview)
    }

    @Test("Copy prompt puts the prompt on the pasteboard and says so; a busy pasteboard says that")
    func copyPrompt() {
        let c = Chat()
        c.model.copyPrompt()
        #expect(c.copied.text == SetupPrompt.build())
        #expect(c.model.status == SetupCopy.chatPromptCopied)
        let busy = Chat(canCopy: false)
        busy.model.copyPrompt()
        #expect(busy.model.status == ChatDesignViewModel.copyBusy)
    }

    @Test("the rules and settings lines say what the buttons will do before they are pressed")
    func lines() {
        let c = Chat()
        c.paste(all)
        #expect(c.model.rulesLine == SetupCopy.chatRulesLine(rules: 1, drawerNames: ["Papers"]))
        #expect(c.model.drawerSettingsLine.contains("Look: bare"))
        c.paste(lookOnly)
        #expect(c.model.drawerSettingsLine.contains("bottom left"))
    }

    @Test("Preview hands the desk half to the validator and holds a neutral review, writing no result file")
    func previewOpensNeutralReview() throws {
        let c = Chat()
        var shown = 0
        c.model.onReviewShown = { shown += 1 }
        c.paste(all)
        #expect(c.validated.count == 1, "the desk half, and only it, went to the validator")
        c.model.preview()
        #expect(c.model.status == SetupCopy.chatDeskShown)
        #expect(shown == 1)
        let review = try #require(c.world.handoff.model)
        #expect(review.heading == DeskDesignReviewCopy.pastedHeading)
        #expect(!review.heading.contains("Claude"))
        #expect(!FileManager.default.fileExists(atPath: c.world.resultFile.path))
        #expect(c.world.answers.isEmpty && c.world.suggested.isEmpty)
        c.world.handoff.dismiss()
        #expect(c.world.handoff.pending == nil)
        #expect(!FileManager.default.fileExists(atPath: c.world.resultFile.path), "dismissing a pasted design answers nothing")
        #expect(c.world.answers.isEmpty && c.world.decided.isEmpty)
    }

    @Test("Apply on a pasted design saves it and still writes no result file")
    func applySaves() {
        let c = Chat()
        c.paste(#"{"desk":{"plate":true}}"#)
        c.model.preview()
        c.world.handoff.apply()
        #expect(c.world.defaults.object(forKey: "plate") != nil || c.world.defaults.bool(forKey: "plate") == true)
        #expect(c.world.handoff.pending == nil)
        #expect(!FileManager.default.fileExists(atPath: c.world.resultFile.path))
    }

    @Test("a bad desk half is a finding naming its field, and nothing can be previewed")
    func badDeskNamesField() {
        let c = Chat()
        c.paste(#"{"desk":{"ink":"loud"}}"#)
        #expect(c.validated.count == 1)
        #expect(c.model.findings.contains { $0.field == "ink" && $0.isError })
        #expect(!c.model.canPreview)
        c.model.preview()
        #expect(c.world.handoff.pending == nil)
    }

    @Test("a busy Desk says why and holds nothing")
    func busyDesk() {
        let c = Chat()
        c.world.host.deskBusy = true
        c.paste(deskOnly)
        c.model.preview()
        #expect(c.model.status.hasPrefix("Not shown"))
        #expect(c.world.handoff.pending == nil)
    }

    @Test("Use these drawer rules replaces the rules, turns drawers on and opens a review, moving nothing")
    func rulesReview() throws {
        let c = Chat()
        c.fx.write("Home/Desktop/a.pdf")
        c.paste(rulesOnly)
        c.model.useRules()
        #expect(c.model.status == SetupCopy.chatRulesShown)
        #expect(c.service.settings.enabled)
        #expect(c.service.settings.rules.count == 1 && c.service.settings.rules[0].kind == .documents)
        let review = try #require(c.service.pending)
        #expect(review.fileCount == 1)
        #expect(FileManager.default.fileExists(atPath: c.fx.desk("a.pdf")), "nothing moves before Apply")
        c.service.apply()
        #expect(!FileManager.default.fileExists(atPath: c.fx.desk("a.pdf")))
    }

    @Test("Use these drawer rules is refused with a line while a drawers review is pending")
    func rulesRefusedWhilePending() throws {
        let c = Chat(enabled: true)
        c.fx.write("Home/Desktop/a.pdf")
        c.paste(rulesOnly)
        c.model.useRules()
        let first = try #require(c.service.pending)
        c.paste(#"{"drawers":{"rules":[{"kind":"images","drawer":"Pics"}]}}"#)
        c.model.useRules()
        #expect(c.model.status == SetupCopy.chatRulesReviewPending)
        #expect(!SetupCopy.chatRulesReviewPending.contains("\u{2014}"))
        #expect(c.service.settings.rules.count == 1 && c.service.settings.rules[0].kind == .documents)
        #expect(c.service.pending?.fileCount == first.fileCount)
    }

    @Test("rules that match nothing are saved and say so, with drawers left on")
    func rulesMatchNothing() {
        let c = Chat(enabled: true)
        c.paste(rulesOnly)
        c.model.useRules()
        #expect(c.model.status == SetupCopy.chatRulesSavedNothing)
        #expect(c.service.pending == nil)
        #expect(c.service.settings.rules.count == 1)
    }

    @Test("Use these drawer settings applies only the named parts, tidies, and Undo restores the old ones")
    func settingsAndUndo() {
        let c = Chat(enabled: true)
        c.service.updateSettings {
            $0.look = .outline; $0.opacity = 40; $0.tidyFlow = .row; $0.tidyCorner = .topLeft
            $0.places = ["papers": DrawerPlace(screen: "s", x: 1, y: 2)]
        }
        c.paste(lookOnly)
        c.model.useDrawerSettings()
        let s = c.service.settings
        #expect(s.look == .glass && s.opacity == 70 && s.tidyCorner == .bottomLeft)
        #expect(s.tidyFlow == .row, "the part the paste left out stays the user's own")
        #expect(c.tidied.n == 1)
        #expect(c.model.canUndo)
        // Tidy moves places; Undo puts them back with the rest.
        c.service.updateSettings { $0.places["papers"] = DrawerPlace(screen: "s", x: 9, y: 9) }
        c.model.undoDrawerSettings()
        let back = c.service.settings
        #expect(back.look == .outline && back.opacity == 40 && back.tidyCorner == .topLeft && back.tidyFlow == .row)
        #expect(back.places["papers"] == DrawerPlace(screen: "s", x: 1, y: 2))
        #expect(!c.model.canUndo)
    }

    @Test("a look with no arrangement does not tidy")
    func lookWithoutTidy() {
        let c = Chat(enabled: true)
        c.paste(#"{"drawers":{"look":"outline"}}"#)
        c.model.useDrawerSettings()
        #expect(c.service.settings.look == .outline)
        #expect(c.tidied.n == 0)
    }

    @Test("the chat window's own strings have no em dash and name no product")
    func copy() {
        for s in [ChatDesignViewModel.settingsDone, ChatDesignViewModel.settingsArranged, ChatDesignViewModel.undone,
                  ChatDesignViewModel.copyBusy, DeskDesignReviewCopy.pastedHeading] {
            #expect(!s.contains("\u{2014}") && !s.contains("Claude"), "\(s)")
        }
    }
}
