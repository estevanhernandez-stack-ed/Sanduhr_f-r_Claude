using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Design it with an AI chat (setup spec, section 3): the paste check by field, Preview, Use these drawer rules, and the window.
/// In the SettingsWindow collection, since a test turns streamer mode on and the classes there read it.</summary>
[Collection("SettingsWindow")]
public sealed class ChatDesignTests : AppWindowFixture
{
    private static readonly DeskValidation Good = new(DeskDesignStatus.Ok, [], [], [], DeskDesign.Parse(JsonNode.Parse("{\"name\":\"Calm\"}")).Design!);

    private static ChatDesignViewModel Make(List<string> calls, DeskValidation? verdict = null) => new(
        () => ["1920×1080, main"],
        v => { calls.Add("preview"); return new DeskAnswer(DeskAnswer.Previewing); },
        _ => verdict ?? Good,
        (rules, names) => { calls.Add("rules:" + rules.Count + ":" + string.Join("|", names)); return null; });

    private static ChatDesignViewModel MakeWithDrawerSettings(List<string> calls, string? refusal = null) => new(
        () => ["1920×1080, main"],
        v => { calls.Add("preview"); return new DeskAnswer(DeskAnswer.Previewing); },
        _ => Good,
        (rules, names) => { calls.Add("rules:" + rules.Count); return null; },
        look =>
        {
            calls.Add("drawer-settings:" + (look.Look is { } k ? DrawerStyle.Word(k) : "-") + ":" + (look.Opacity?.ToString() ?? "-")
                + ":" + (look.Arrange is { } a ? DrawerStyle.Word(a.Corner) + "/" + DrawerStyle.Word(a.Flow) + $"/{look.HasCorner}/{look.HasFlow}" : "-"));
            return refusal;
        });

    [Fact]
    public void A_good_desk_part_enables_Preview_and_previews_through_the_review()
    {
        var calls = new List<string>();
        var vm = Make(calls);
        vm.PasteText = "```json\n{\"desk\":{\"name\":\"Calm\"}}\n```";
        Assert.True(vm.CanPreview);
        Assert.False(vm.CanUseRules);
        Assert.Equal(SetupCopy.ChatReady, vm.Summary);
        vm.PreviewCommand.Execute(null);
        Assert.Equal(["preview"], calls);
        Assert.Equal(SetupCopy.ChatDeskShown, vm.Status);
    }

    [Fact]
    public void Problems_are_listed_by_field_and_Preview_stays_off()
    {
        var calls = new List<string>();
        var vm = Make(calls, new DeskValidation(DeskDesignStatus.Rejected, [new DeskReason("layout.clock.anchor", "must be one of tl, tc")], [], [], null));
        vm.PasteText = "{\"desk\":{\"layout\":{\"clock\":{\"anchor\":\"zz\"}}}}";
        Assert.False(vm.CanPreview);
        Assert.Contains(vm.Findings, f => f.Field == "layout.clock.anchor" && f.IsError);
        Assert.Equal(SetupCopy.ChatFixCount(1), vm.Summary);
        vm.PreviewCommand.Execute(null);
        Assert.Empty(calls);
    }

    [Fact]
    public void A_reader_problem_is_listed_and_nothing_is_usable()
    {
        var calls = new List<string>();
        var vm = Make(calls);
        vm.PasteText = """{"desk":{"name":"A"},"drawers":{"rules":[{"kind":"images","drawer":"a/b"}]}}""";
        Assert.False(vm.CanPreview);
        Assert.False(vm.CanUseRules);
        Assert.Contains(vm.Findings, f => f.Field == "drawers.rules[0].drawer" && f.IsError);
    }

    [Fact]
    public void Rules_say_they_replace_the_drawer_rules_and_use_opens_the_sort()
    {
        var calls = new List<string>();
        var vm = Make(calls);
        vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""";
        Assert.True(vm.CanUseRules);
        Assert.False(vm.CanPreview);
        Assert.Contains("replace", vm.RulesLine);
        Assert.Contains("Nothing moves until you Apply", vm.RulesLine);
        vm.UseRulesCommand.Execute(null);
        Assert.Equal(["rules:1:Photos"], calls);   // the chat's name for the drawer travels with the rule
        Assert.Equal(SetupCopy.ChatRulesShown, vm.Status);
    }

    [Fact]
    public void A_refusal_to_open_the_sort_is_the_status_line()
    {
        var vm = new ChatDesignViewModel(() => [], null, _ => Good, (_, _) => DrawerReview.Busy);
        vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""";
        vm.UseRulesCommand.Execute(null);
        Assert.Equal(DrawerReview.Busy, vm.Status);
    }

    [Fact]
    public void Two_blocks_say_the_first_good_one_is_used()
    {
        var vm = Make([]);
        vm.PasteText = "{\"desk\":{\"name\":\"A\"}} {\"desk\":{\"name\":\"B\"}}";
        Assert.Equal(SetupCopy.ChatManyBlocks(2), vm.Summary);
        Assert.Contains("first", vm.Summary);
    }

    [Fact]
    public void The_prompt_lists_the_monitors_by_name()
    {
        var vm = Make([]);
        Assert.Contains("1920×1080, main", vm.PromptText);
    }

    [Fact]
    public void Clearing_the_box_clears_the_check()
    {
        var vm = Make([]);
        vm.PasteText = "{\"desk\":{\"name\":\"A\"}}";
        Assert.True(vm.CanPreview);
        vm.PasteText = "";
        Assert.False(vm.CanPreview);
        Assert.Empty(vm.Findings);
        Assert.Equal("", vm.Summary);
    }

    [Fact]
    public void Without_a_Desk_service_Preview_stays_off_though_the_paste_is_fine()
    {
        var vm = new ChatDesignViewModel(() => [], null, _ => Good, (_, _) => null);
        vm.PasteText = "{\"desk\":{\"name\":\"A\"}}";
        Assert.False(vm.CanPreview);
        Assert.Empty(vm.Findings);
    }

    [Fact]
    public void A_refused_preview_says_why_in_the_status_line()
    {
        var vm = new ChatDesignViewModel(() => [], _ => DeskAnswer.Refused([new DeskReason("design", "finish the drawers review on your Desk first")]),
            _ => Good, (_, _) => null);
        vm.PasteText = "{\"desk\":{\"name\":\"A\"}}";
        vm.PreviewCommand.Execute(null);
        Assert.Equal("Not shown: design finish the drawers review on your Desk first.", vm.Status);
    }

    // ---- drawer settings ----

    [Fact]
    public void Drawer_settings_enable_their_button_and_apply_through_the_app()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"drawers":{"look":"outline","arrange":{"corner":"tl"}}}""";
        Assert.True(vm.CanUseDrawerSettings);
        Assert.False(vm.CanUseRules);
        Assert.False(vm.CanPreview);
        Assert.Equal(SetupCopy.ChatReady, vm.Summary);
        Assert.Contains("outline", vm.DrawerSettingsLine);
        Assert.Contains("top left", vm.DrawerSettingsLine);
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(["drawer-settings:outline:-:tl/column/True/False"], calls);
        Assert.Equal(SetupCopy.ChatDrawerSettingsArranged, vm.Status);
    }

    [Fact]
    public void A_look_without_an_arrangement_says_the_look_was_saved()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"drawers":{"look":"glass","opacity":70}}""";
        Assert.Equal("Look: glass, 70%. Using it changes how your drawers look and sit right away.", vm.DrawerSettingsLine);
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(SetupCopy.ChatDrawerSettingsDone, vm.Status);
    }

    [Fact]
    public void The_settings_line_names_only_the_parts_the_paste_gave()
    {
        var vm = MakeWithDrawerSettings([]);
        vm.PasteText = """{"drawers":{"arrange":{"direction":"row"}}}""";
        Assert.Equal("Lines drawers up from your usual corner, in a row. Using it changes how your drawers look and sit right away.", vm.DrawerSettingsLine);
        vm.PasteText = """{"drawers":{"opacity":40}}""";
        Assert.StartsWith("Opacity: 40%.", vm.DrawerSettingsLine);
        vm.PasteText = """{"drawers":{"look":"bare","arrange":{"corner":"br","direction":"column"}}}""";
        Assert.Equal("Look: bare. Lines drawers up from the bottom right, in a column. Using it changes how your drawers look and sit right away.", vm.DrawerSettingsLine);
    }

    [Fact]
    public void A_refused_apply_is_the_status_line()
    {
        var vm = MakeWithDrawerSettings([], DrawerReview.Busy);
        vm.PasteText = """{"drawers":{"look":"solid"}}""";
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(DrawerReview.Busy, vm.Status);
    }

    [Fact]
    public void Rules_and_a_look_offer_both_buttons_and_each_runs_alone()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}],"look":"bare"}}""";
        Assert.True(vm.CanUseRules);
        Assert.True(vm.CanUseDrawerSettings);
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(["drawer-settings:bare:-:-"], calls);
        vm.UseRulesCommand.Execute(null);
        Assert.Equal(["drawer-settings:bare:-:-", "rules:1"], calls);
    }

    [Fact]
    public void A_rules_only_paste_works_as_before_and_leaves_the_settings_button_off()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""";
        Assert.True(vm.CanUseRules);
        Assert.False(vm.CanUseDrawerSettings);
        Assert.Equal("", vm.DrawerSettingsLine);
        vm.UseDrawerSettingsCommand.Execute(null);   // nothing to use
        Assert.Empty(calls);
    }

    [Fact]
    public void Any_problem_disables_every_button_including_the_drawer_settings()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"desk":{"name":"A"},"drawers":{"look":"glass","opacity":500}}""";
        Assert.False(vm.CanPreview);
        Assert.False(vm.CanUseRules);
        Assert.False(vm.CanUseDrawerSettings);
        Assert.Equal("", vm.DrawerSettingsLine);
        Assert.Contains(vm.Findings, f => f.Field == "drawers.opacity" && f.IsError);
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Empty(calls);

        // A desk problem alone also holds back a good look.
        var bad = new DeskValidation(DeskDesignStatus.Rejected, [new DeskReason("layout.clock.anchor", "must be one of tl, tc")], [], [], null);
        var vm2 = new ChatDesignViewModel(() => [], null, _ => bad, (_, _) => null, _ => null);
        vm2.PasteText = """{"desk":{"layout":{"clock":{"anchor":"zz"}}},"drawers":{"look":"glass"}}""";
        Assert.False(vm2.CanUseDrawerSettings);
    }

    [Fact]
    public void Editing_the_paste_clears_the_drawer_settings()
    {
        var vm = MakeWithDrawerSettings([]);
        vm.PasteText = """{"drawers":{"look":"glass"}}""";
        Assert.True(vm.CanUseDrawerSettings);
        vm.PasteText = "";
        Assert.False(vm.CanUseDrawerSettings);
        Assert.Equal("", vm.DrawerSettingsLine);
    }

    [Fact]
    public void Without_the_app_wiring_the_drawer_button_says_there_is_nothing_to_apply_it_on()
    {
        var vm = new ChatDesignViewModel(() => [], null, _ => Good, (_, _) => null);
        vm.PasteText = """{"drawers":{"look":"glass"}}""";
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(SetupCopy.ChatNoDeskService, vm.Status);
    }

    // ---- a piece this build does not draw ----

    [Fact]
    public void A_meetings_piece_is_refused_by_field_through_the_real_rules()
    {
        var vm = new ChatDesignViewModel(() => [], _ => new DeskAnswer(DeskAnswer.Previewing),
            node => PastedSetup.RefuseUndrawn(DeskDesign.Validate(node, DeskDesignContext.Unknown)), (_, _) => null);
        vm.PasteText = "{\"desk\":{\"layout\":{\"meetings\":{\"anchor\":\"tr\"}}}}";
        Assert.False(vm.CanPreview);
        Assert.Contains(vm.Findings, f => f.Field == "layout.meetings" && f.IsError);
        // A piece Windows does draw passes the same rules.
        vm.PasteText = "{\"desk\":{\"layout\":{\"clock\":{\"anchor\":\"tr\"}}}}";
        Assert.True(vm.CanPreview);
    }

    // ---- names ----

    [Fact]
    public void A_reason_that_names_Claude_reads_an_AI_chat_in_the_findings()
    {
        var vm = Make([], new DeskValidation(DeskDesignStatus.Rejected, [new DeskReason("launcher.buttons[0].image", "Claude cannot supply a new image")], [], [], null));
        vm.PasteText = "{\"desk\":{\"name\":\"A\"}}";
        Assert.All(vm.Findings, f => Assert.DoesNotContain("Claude", f.Field + f.Message));
        Assert.Contains(vm.Findings, f => f.Message.StartsWith("an AI chat cannot", StringComparison.Ordinal));
    }

    [Fact]
    public void The_window_copy_names_no_product_and_has_no_em_dash()
    {
        foreach (var s in SetupCopy.Chat)
        {
            Assert.DoesNotContain("Claude", s);
            Assert.DoesNotContain("—", s);
            foreach (var mark in new[] { "ChatGPT", "Gemini", "Copilot", "OpenAI", "Codex", "Anthropic" }) Assert.DoesNotContain(mark, s);
        }
        Assert.Equal("Your AI chat's Desk design", DeskReviewCopy.PastedHeading);
        Assert.DoesNotContain("Claude", DeskReviewCopy.PastedHeading + DeskReviewCopy.PastedBarName);
    }

    [Fact]
    public void Drawer_names_show_as_codenames_while_streamer_mode_is_on()
    {
        try
        {
            var vm = Make([]);
            vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"},{"kind":"name_contains","text":"invoice","drawer":"Receipts"}]}}""";
            Assert.Contains("Photos", vm.RulesLine);
            Assert.Contains("Receipts", vm.RulesLine);

            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Root));
            vm.PasteText = "";
            vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"},{"kind":"name_contains","text":"invoice","drawer":"Receipts"}]}}""";
            Assert.DoesNotContain("Photos", vm.RulesLine);
            Assert.DoesNotContain("Receipts", vm.RulesLine);
            Assert.Contains("2 drawer rules", vm.RulesLine);
        }
        finally { StreamerMode.Reset(); }
    }

    // ---- the window ----

    [Fact]
    public void The_window_has_a_name_on_every_control_and_names_no_product() => AppHost.Run(() =>
    {
        var vm = Make([]);
        var w = new ChatDesignWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var names = NamesOf(w);
            Assert.Empty(LeaksIn(names));
            foreach (var expected in new[] { SetupCopy.DesignWithChat, SetupCopy.ChatCopyPrompt, SetupCopy.ChatPasteName, SetupCopy.ChatPreview,
                SetupCopy.ChatUseRules, SetupCopy.ChatUseDrawerSettings, SetupCopy.ChatClose })
                Assert.Contains(names, n => n == expected);
            Assert.DoesNotContain(names, n => n.Contains("Claude"));
            var unnamed = Walk(UIElementAutomationPeer.CreatePeerForElement(w))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.Edit)
                .Where(p => string.IsNullOrWhiteSpace(p.GetName()))
                .Select(p => p.GetType().Name)
                .ToList();
            Assert.Empty(unnamed);

            // Nothing pasted: Preview and Use are off; a good paste turns Preview on.
            Assert.False(((System.Windows.Controls.Button)w.FindName("PreviewButton")).IsEnabled);
            Assert.False(((System.Windows.Controls.Button)w.FindName("UseRulesButton")).IsEnabled);
            Assert.False(((System.Windows.Controls.Button)w.FindName("UseDrawerSettingsButton")).IsEnabled);
            vm.PasteText = "{\"desk\":{\"name\":\"A\"},\"drawers\":{\"rules\":[{\"kind\":\"images\",\"drawer\":\"Photos\"}],\"look\":\"glass\"}}";
            w.UpdateLayout();
            Assert.True(((System.Windows.Controls.Button)w.FindName("PreviewButton")).IsEnabled);
            Assert.True(((System.Windows.Controls.Button)w.FindName("UseRulesButton")).IsEnabled);
            Assert.True(((System.Windows.Controls.Button)w.FindName("UseDrawerSettingsButton")).IsEnabled);
            Assert.True(((System.Windows.Controls.TextBlock)w.FindName("RulesText")).IsVisible);
            Assert.True(((System.Windows.Controls.TextBlock)w.FindName("DrawerSettingsText")).IsVisible);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Close_closes_the_window() => AppHost.Run(() =>
    {
        var vm = Make([]);
        var w = new ChatDesignWindow(vm);
        OffScreen(w);
        w.Show();
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        vm.CloseCommand.Execute(null);
        Assert.True(closed);
    });

    // ---- Use these drawer rules ----

    private sealed class FakeDrawerHost : IDrawerReviewHost
    {
        public bool Other { get; set; }
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply { get; private set; }
        public bool OtherReviewOpen => Other;
        public void Preview(string layout) { }
        public void EndPreview() { }
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss) { Model = model; Apply = apply; }
        public void CloseBar() => Model = null;
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => null;
    }

    private sealed record Rig(DrawerService Drawers, DrawerReviewService Review, SettingsStore Store, FakeDrawerHost Host);

    private static Rig MakeRig(DrawerSandbox box, bool drawersOn, bool deskOn, string? root = null, params DrawerDef[] defs)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = deskOn, Layout = "clock:bl" });
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDrawers(DrawerSettings.Default with { Enabled = drawersOn, Root = root, Drawers = defs });
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var host = new FakeDrawerHost();
        return new Rig(drawers, new DrawerReviewService(drawers, store, host), store, host);
    }

    private static PastedSetupRead Rules(string json) => PastedSetup.Read(json);

    [Fact]
    public void Use_rules_replaces_the_rules_turns_drawers_and_the_Desk_on_and_opens_the_plan_under_the_chats_names() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: false, deskOn: false);
        try
        {
            box.File(@"Desktop\holiday.png");
            box.File(@"Desktop\invoice-march.pdf");
            var read = Rules("""{"drawers":{"rules":[{"kind":"images","drawer":"Holiday Photos"},{"kind":"name_contains","text":"invoice","drawer":"Receipts"}]}}""");
            bool reloaded = false;

            var said = rig.Review.UseRules(read.Rules!, read.RuleDrawerNames, () => reloaded = true);

            Assert.Null(said);                       // the review opened
            Assert.True(reloaded);
            Assert.True(rig.Review.Reviewing);
            Assert.True(rig.Store.LoadDrawers().Enabled);
            Assert.True(rig.Store.LoadDesk().Enabled);
            Assert.Equal(read.Rules!.Select(r => r.DrawerId), rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));
            // The new drawers carry the names the chat gave them, not the ids in words ("holiday-photos" would read "Holiday photos").
            Assert.Contains("Holiday Photos (new): 1 file, including holiday.png", rig.Host.Model!.Changes);
            Assert.Contains("Receipts (new): 1 file, including invoice-march.pdf", rig.Host.Model.Changes);
            // Nothing moved before Apply.
            Assert.True(File.Exists(Path.Combine(box.Desktop, "holiday.png")));
            Assert.True(File.Exists(Path.Combine(box.Desktop, "invoice-march.pdf")));
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void Use_rules_is_refused_busy_while_a_review_is_open_and_changes_nothing() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: false, deskOn: false);
        try
        {
            box.File(@"Desktop\holiday.png");
            var read = Rules("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");

            rig.Host.Other = true;   // a Desk design or a widget is under review
            Assert.Equal(DrawerReview.Busy, rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
            Assert.False(rig.Store.LoadDrawers().Enabled);
            Assert.False(rig.Store.LoadDesk().Enabled);
            Assert.Equal(DrawerRules.Defaults.Select(r => r.DrawerId), rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));

            rig.Host.Other = false;
            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));   // a drawers review of its own
            var second = Rules("""{"drawers":{"rules":[{"kind":"images","drawer":"Pictures"}]}}""");
            Assert.Equal(DrawerReview.Busy, rig.Review.UseRules(second.Rules!, second.RuleDrawerNames));
            Assert.Equal(["photos"], rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));   // the second try changed nothing
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void Rules_that_sort_nothing_are_saved_and_say_so() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: true, deskOn: true);
        try
        {
            var read = Rules("""{"drawers":{"rules":[{"kind":"archives","drawer":"Zips"}]}}""");
            Assert.Equal(SetupCopy.ChatRulesSavedNothing, rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
            Assert.False(rig.Review.Reviewing);
            Assert.Equal(["zips"], rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void The_sort_names_a_new_drawer_from_its_id_when_no_name_is_given() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: true, deskOn: true);
        try
        {
            box.File(@"Desktop\holiday.png");
            rig.Drawers.Save(rig.Drawers.Settings with { Rules = [new DrawerRule(DrawerRuleKind.Images, "old-photos", On: true)] });
            Assert.Null(rig.Review.SortNow());
            Assert.Contains("Old photos (new): 1 file, including holiday.png", rig.Host.Model!.Changes);   // NameFromId, as before
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void A_rule_naming_a_renamed_existing_drawer_uses_that_drawer_and_makes_no_second_one() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        // "images" is the built-in drawer id; the user renamed it Photos, so its folder is Drawers\Photos.
        var photos = new DrawerDef("images", "Photos", Path.Combine(box.Drawers, "Photos"), Owned: true);
        var rig = MakeRig(box, drawersOn: true, deskOn: true, defs: photos);
        try
        {
            box.File(@"Desktop\holiday.png");
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"photos"}]}}""");
            Assert.Equal("photos", read.Rules![0].DrawerId);   // the reader's own id for the chat's name

            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));

            Assert.Equal(["images"], rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));
            Assert.Equal(["images"], rig.Store.LoadDrawers().Drawers.Select(d => d.Id));
            Assert.Contains("Photos: 1 file, including holiday.png", rig.Host.Model!.Changes);   // the existing drawer, so no "(new)"
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void A_chat_name_that_matches_a_view_drawer_gets_an_owned_drawer_of_its_own() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        // A view shows a folder the user already has in place; a chat cannot see it, so a name match is chance.
        var view = new DrawerDef("documents", "Documents", Path.Combine(box.Root, "UserDocs"), Owned: false);
        var rig = MakeRig(box, drawersOn: true, deskOn: true, defs: view);
        try
        {
            box.File(@"Desktop\report.pdf");
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"documents","drawer":"Documents"}]}}""");
            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));

            Assert.Equal(["documents-2"], rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));   // not the view's id
            Assert.Contains("Documents (new): 1 file, including report.pdf", rig.Host.Model!.Changes);
            Assert.Equal(view.Home, rig.Store.LoadDrawers().Drawers.Single().Home);                     // the view is as it was
            Assert.True(File.Exists(Path.Combine(box.Desktop, "report.pdf")));                          // and nothing moved
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void A_chat_id_that_belongs_to_a_different_drawer_gets_a_free_one() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var photos = new DrawerDef("images", "Photos", Path.Combine(box.Drawers, "Photos"), Owned: true);
        var rig = MakeRig(box, drawersOn: true, deskOn: true, defs: photos);
        try
        {
            box.File(@"Desktop\holiday.png");
            // "Images" is the built-in id "images", which is the Photos drawer here: this rule is for a drawer of its own.
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Images"}]}}""");
            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
            Assert.Equal(["images-2"], rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));
            Assert.Contains("Images (new): 1 file, including holiday.png", rig.Host.Model!.Changes);
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void A_refused_plan_writes_nothing() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        // A Drawers folder that is the Desktop itself is unsafe: the check refuses every plan built on it.
        var rig = MakeRig(box, drawersOn: false, deskOn: false, root: box.Desktop);
        try
        {
            box.File(@"Desktop\holiday.png");
            string before = File.ReadAllText(Path.Combine(box.Root, "appdata", "Sanduhr", "settings.json"));
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
            bool reloaded = false;

            var said = rig.Review.UseRules(read.Rules!, read.RuleDrawerNames, () => reloaded = true);

            Assert.Contains("Drawers folder", said);
            Assert.False(reloaded);
            Assert.False(rig.Review.Reviewing);
            Assert.False(rig.Store.LoadDrawers().Enabled);
            Assert.False(rig.Store.LoadDesk().Enabled);
            Assert.Equal(DrawerRules.Defaults.Select(r => r.DrawerId), rig.Store.LoadDrawers().Rules.Select(r => r.DrawerId));
            Assert.Equal(before, File.ReadAllText(Path.Combine(box.Root, "appdata", "Sanduhr", "settings.json")));
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void Drawers_and_the_Desk_go_in_one_write_and_both_hold() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: false, deskOn: false);
        try
        {
            box.File(@"Desktop\holiday.png");
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
            var fresh = new SettingsStore(new Paths(appDataBase: Path.Combine(box.Root, "appdata")));
            Assert.True(fresh.LoadDrawers().Enabled);
            Assert.True(fresh.LoadDesk().Enabled);
            Assert.Equal("clock:bl", fresh.LoadDesk().Layout);   // the rest of the Desk is as it was
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void Use_rules_is_busy_while_a_sorts_result_bar_with_its_Undo_is_up() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: true, deskOn: true);
        try
        {
            box.File(@"Desktop\holiday.png");
            var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
            Assert.Null(rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
            rig.Host.Apply!();                        // Apply: the files move and the result bar (with Undo) stays up
            Assert.False(rig.Review.Reviewing);
            Assert.True(rig.Review.BarOpen);
            Assert.Equal(DrawerReview.Busy, rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
        }
        finally { rig.Drawers.Dispose(); }
    });

    [Fact]
    public void Use_rules_after_the_drawers_service_is_disposed_says_sorting_is_unavailable() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var rig = MakeRig(box, drawersOn: true, deskOn: true);
        rig.Drawers.Dispose();
        var read = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
        Assert.Equal(SettingsNames.DrawersSortUnavailable, rig.Review.UseRules(read.Rules!, read.RuleDrawerNames));
    });

    // ---- the whole paste is checked at once ----

    [Fact]
    public void A_desk_problem_and_a_drawers_problem_are_both_listed()
    {
        var vm = Make([], new DeskValidation(DeskDesignStatus.Rejected, [new DeskReason("layout.clock.anchor", "must be one of tl, tc")], [], [], null));
        vm.PasteText = """{"desk":{"layout":{"clock":{"anchor":"zz"}}},"drawers":{"rules":[{"kind":"images","drawer":"a/b"}]}}""";
        Assert.Contains(vm.Findings, f => f.Field == "layout.clock.anchor");
        Assert.Contains(vm.Findings, f => f.Field == "drawers.rules[0].drawer");
        Assert.Equal(SetupCopy.ChatFixCount(2), vm.Summary);
    }

    [Fact]
    public void The_rules_line_says_the_Desk_turns_on_too()
    {
        var vm = Make([]);
        vm.PasteText = """{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""";
        Assert.Contains("your Desk on", vm.RulesLine);
    }

    [Fact]
    public void A_desk_field_the_prompt_does_not_teach_is_listed_by_field_and_nothing_is_usable()
    {
        var vm = new ChatDesignViewModel(() => [], _ => new DeskAnswer(DeskAnswer.Previewing),
            node => PastedSetup.RefuseUndrawn(DeskDesign.Validate(node, DeskDesignContext.Unknown)), (_, _) => null);
        vm.PasteText = """{"desk":{"name":"A","follows_theme":true,"launcher":{"orientation":"row","buttons":[]}}}""";
        Assert.False(vm.CanPreview);
        Assert.Contains(vm.Findings, f => f.Field == "desk.follows_theme" && f.IsError);
        Assert.Contains(vm.Findings, f => f.Field == "desk.launcher" && f.IsError);
        Assert.Equal(vm.Findings.Count, vm.Findings.Select(f => f.Field + f.Message).Distinct().Count());   // none twice
    }
}
