using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class PastedSetupTests
{
    [Fact]
    public void A_fenced_block_inside_prose_is_read()
    {
        var r = PastedSetup.Read("Here you go!\n```json\n{\"desk\":{\"name\":\"Calm\"}}\n```\nEnjoy.");
        Assert.True(r.Ok);
        Assert.Equal(1, r.Blocks);
        Assert.Equal("Calm", (string?)r.Desk!["name"]);
        Assert.Null(r.Rules);
    }

    [Fact]
    public void With_two_blocks_the_first_good_one_is_used_and_counted()
    {
        var r = PastedSetup.Read("{not json} and {\"desk\":{\"name\":\"A\"}} then {\"desk\":{\"name\":\"B\"}}");
        Assert.True(r.Ok);
        Assert.Equal("A", (string?)r.Desk!["name"]);
        Assert.Equal(2, r.Blocks);
    }

    [Fact]
    public void A_block_with_neither_part_before_the_real_one_is_skipped_and_not_counted()
    {
        var r = PastedSetup.Read("Example: {\"hello\":1}\nAnswer: {\"desk\":{\"name\":\"B\"}}");
        Assert.True(r.Ok);
        Assert.Equal("B", (string?)r.Desk!["name"]);
        Assert.Equal(1, r.Blocks);
    }

    [Fact]
    public void Braces_inside_strings_and_stray_braces_in_prose_do_not_break_the_scan()
    {
        var r = PastedSetup.Read("Use { carefully. {\"desk\":{\"note\":\"a } and a { and a \\\" quote\"}} Done }");
        Assert.True(r.Ok);
        Assert.Equal("a } and a { and a \" quote", (string?)r.Desk!["note"]);
    }

    [Fact]
    public void Nothing_to_read_says_so_by_field()
    {
        var r = PastedSetup.Read("I could not help with that.");
        Assert.False(r.Ok);
        Assert.Equal("design", Assert.Single(r.Reasons).Field);
        Assert.Equal(0, r.Blocks);
        var empty = PastedSetup.Read("{\"hello\":1}");
        Assert.False(empty.Ok);
        Assert.False(PastedSetup.Read(null).Ok);
        Assert.False(PastedSetup.Read("").Ok);
    }

    [Fact]
    public void A_paste_of_only_braces_is_refused_quickly()
    {
        var r = PastedSetup.Read(new string('{', 150_000));
        Assert.False(r.Ok);
        Assert.False(PastedSetup.Read(new string('x', PastedSetup.MaxPasteChars + 1)).Ok);
    }

    [Fact]
    public void Drawer_rules_are_read_and_each_bad_one_is_named()
    {
        var r = PastedSetup.Read("""
            {"drawers":{"rules":[
              {"kind":"images","drawer":"Photos"},
              {"kind":"name_contains","text":"invoice","drawer":"Receipts"},
              {"kind":"older_than","days":60,"drawer":"Old stuff"}]}}
            """);
        Assert.True(r.Ok);
        Assert.Equal(3, r.Rules!.Count);
        Assert.Equal(new DrawerRule(DrawerRuleKind.Images, "photos"), r.Rules[0]);
        Assert.Equal("invoice", r.Rules[1].Text);
        Assert.Equal(60, r.Rules[2].Days);
        Assert.Equal(["Photos", "Receipts", "Old stuff"], r.RuleDrawerNames);

        var bad = PastedSetup.Read("""
            {"drawers":{"rules":[{"kind":"move_files","drawer":"X"},{"kind":"images","drawer":"CON"},
              {"kind":"name_contains","drawer":"Y"},{"kind":"older_than","days":0,"drawer":"Z"}]}}
            """);
        Assert.False(bad.Ok);
        Assert.Null(bad.Rules);
        Assert.Contains(bad.Reasons, x => x.Field == "drawers.rules[0].kind");
        Assert.Contains(bad.Reasons, x => x.Field == "drawers.rules[1].drawer");
        Assert.Contains(bad.Reasons, x => x.Field == "drawers.rules[2].text");
        Assert.Contains(bad.Reasons, x => x.Field == "drawers.rules[3].days");
    }

    [Fact]
    public void A_drawers_part_with_only_a_look_is_read_and_keeps_the_rules()
    {
        var r = PastedSetup.Read("""{"drawers":{"look":"glass","opacity":70,"arrange":{"corner":"bl","direction":"row"}}}""");
        Assert.True(r.Ok);
        Assert.Null(r.Rules);
        Assert.Equal(DrawerLookKind.Glass, r.DrawerLook!.Look);
        Assert.Equal(70, r.DrawerLook.Opacity);
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), r.DrawerLook.Arrange);
        Assert.True(r.DrawerLook.HasCorner);
        Assert.True(r.DrawerLook.HasFlow);
    }

    [Fact]
    public void Rules_and_a_look_in_one_drawers_part_are_both_read()
    {
        var r = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}],"look":"bare"}}""");
        Assert.True(r.Ok);
        Assert.Single(r.Rules!);
        Assert.Equal(DrawerLookKind.Bare, r.DrawerLook!.Look);
        Assert.Null(r.DrawerLook.Opacity);
        Assert.Null(r.DrawerLook.Arrange);
    }

    [Fact]
    public void A_rules_only_paste_has_no_drawer_look()
    {
        var r = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
        Assert.True(r.Ok);
        Assert.Null(r.DrawerLook);
    }

    [Fact]
    public void An_arrange_with_one_part_says_which_part_it_named()
    {
        var corner = PastedSetup.Read("""{"drawers":{"arrange":{"corner":"tl"}}}""").DrawerLook!;
        Assert.True(corner.HasCorner);
        Assert.False(corner.HasFlow);
        Assert.Equal(DrawerCorner.TopLeft, corner.Arrange!.Corner);
        var flow = PastedSetup.Read("""{"drawers":{"arrange":{"direction":"row"}}}""").DrawerLook!;
        Assert.False(flow.HasCorner);
        Assert.True(flow.HasFlow);
        Assert.Equal(DrawerFlow.Row, flow.Arrange!.Flow);
    }

    [Theory]
    [InlineData("""{"drawers":{"look":"neon"}}""", "drawers.look")]
    [InlineData("""{"drawers":{"look":3}}""", "drawers.look")]
    [InlineData("""{"drawers":{"opacity":19}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"opacity":101}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"opacity":5}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"opacity":70.5}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"opacity":"70"}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"arrange":{"corner":"middle"}}}""", "drawers.arrange.corner")]
    [InlineData("""{"drawers":{"arrange":{"direction":"diagonal"}}}""", "drawers.arrange.direction")]
    [InlineData("""{"drawers":{"arrange":{"speed":3,"corner":"tl"}}}""", "drawers.arrange.speed")]
    [InlineData("""{"drawers":{"arrange":{}}}""", "drawers.arrange")]
    [InlineData("""{"drawers":{"arrange":"tl"}}""", "drawers.arrange")]
    [InlineData("""{"drawers":{"colour":"red"}}""", "drawers.colour")]
    [InlineData("""{"drawers":{}}""", "drawers")]
    public void Bad_drawer_settings_are_refused_by_field(string json, string field)
    {
        var r = PastedSetup.Read(json);
        Assert.False(r.Ok);
        Assert.Contains(r.Reasons, x => x.Field == field);
        Assert.Null(r.DrawerLook);
    }

    [Fact]
    public void The_opacity_edges_and_a_whole_float_are_read()
    {
        foreach (var (text, want) in new[] { ("20", 20), ("100", 100), ("70.0", 70) })
            Assert.Equal(want, PastedSetup.Read("{\"drawers\":{\"opacity\":" + text + "}}").DrawerLook!.Opacity);
    }

    [Fact]
    public void Nothing_applies_partly_a_bad_look_holds_back_the_good_rules_and_the_good_opacity()
    {
        var r = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"Photos"}],"look":"neon","opacity":70}}""");
        Assert.False(r.Ok);
        Assert.Null(r.Rules);
        Assert.Null(r.DrawerLook);
        Assert.Contains(r.Reasons, x => x.Field == "drawers.look");
    }

    [Fact]
    public void A_bad_look_in_one_block_lets_the_next_good_block_through()
    {
        var r = PastedSetup.Read("""
            {"drawers":{"look":"neon"}}
            and then
            {"drawers":{"look":"outline"}}
            """);
        Assert.True(r.Ok);
        Assert.Equal(DrawerLookKind.Outline, r.DrawerLook!.Look);
        Assert.Equal(2, r.Blocks);
    }

    [Fact]
    public void Days_must_be_a_whole_number_from_one_to_3650()
    {
        static string One(string days) => "{\"drawers\":{\"rules\":[{\"kind\":\"older_than\",\"drawer\":\"Old\",\"days\":" + days + "}]}}";
        Assert.True(PastedSetup.Read(One("1")).Ok);
        Assert.True(PastedSetup.Read(One("3650")).Ok);
        Assert.True(PastedSetup.Read(One("60.0")).Ok);
        foreach (var bad in new[] { "0", "-5", "3651", "60.5", "\"60\"", "null" })
            Assert.Contains(PastedSetup.Read(One(bad)).Reasons, x => x.Field == "drawers.rules[0].days");
        Assert.Contains(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"older_than","drawer":"Old"}]}}""").Reasons, x => x.Field == "drawers.rules[0].days");
    }

    [Fact]
    public void The_name_kinds_need_text_the_other_kinds_ignore_it()
    {
        Assert.Contains(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"name_starts_with","drawer":"A"}]}}""").Reasons, x => x.Field == "drawers.rules[0].text");
        Assert.Contains(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"name_contains","drawer":"A","text":"   "}]}}""").Reasons, x => x.Field == "drawers.rules[0].text");
        Assert.Contains(PastedSetup.Read("{\"drawers\":{\"rules\":[{\"kind\":\"name_contains\",\"drawer\":\"A\",\"text\":\"" + new string('t', 61) + "\"}]}}").Reasons, x => x.Field == "drawers.rules[0].text");
        Assert.Contains(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"name_contains","drawer":"A","text":"a\\b"}]}}""").Reasons, x => x.Field == "drawers.rules[0].text");
        var r = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"A","text":"x"},{"kind":"name_starts_with","drawer":"B","text":" Inv "}]}}""");
        Assert.True(r.Ok);
        Assert.Null(r.Rules![0].Text);
        Assert.Equal("Inv", r.Rules[1].Text);
    }

    [Fact]
    public void A_file_path_anywhere_in_the_drawers_part_is_refused()
    {
        var r = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"C:\\Users\\me\\Pictures"}]}}""");
        Assert.False(r.Ok);
        Assert.Contains(r.Reasons, x => x.Field == "drawers.rules[0].drawer");
        foreach (var path in new[] { "../Pictures", "Pictures/Old", "\\\\\\\\server\\\\share", "~/Pictures" })
            Assert.False(PastedSetup.Read("{\"drawers\":{\"rules\":[{\"kind\":\"images\",\"drawer\":\"" + path + "\"}]}}").Ok, path);
        Assert.False(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"A","path":"C:\\x"}]}}""").Ok);
        Assert.False(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"A"}],"files":["a.txt"]}}""").Ok);
    }

    [Fact]
    public void More_than_twenty_rules_or_an_unknown_part_is_refused()
    {
        static string Rules(int n) => string.Join(",", Enumerable.Range(0, n).Select(i => $"{{\"kind\":\"images\",\"drawer\":\"D{i}\"}}"));
        Assert.True(PastedSetup.Read($"{{\"drawers\":{{\"rules\":[{Rules(20)}]}}}}").Ok);
        var many = PastedSetup.Read($"{{\"drawers\":{{\"rules\":[{Rules(21)}]}}}}");
        Assert.False(many.Ok);
        Assert.Contains(many.Reasons, x => x.Field == "drawers.rules");
        var extra = PastedSetup.Read("""{"desk":{},"files":["a.txt"]}""");
        Assert.False(extra.Ok);
        Assert.Contains(extra.Reasons, x => x.Field == "files");
    }

    [Fact]
    public void Empty_or_misshapen_parts_are_refused_not_skipped()
    {
        Assert.False(PastedSetup.Read("""{"drawers":{"rules":[]}}""").Ok);
        Assert.False(PastedSetup.Read("""{"drawers":{}}""").Ok);
        Assert.False(PastedSetup.Read("""{"drawers":[]}""").Ok);
        Assert.False(PastedSetup.Read("""{"desk":[]}""").Ok);
        Assert.False(PastedSetup.Read("""{"desk":null}""").Ok);
        Assert.False(PastedSetup.Read("""{"drawers":{"rules":["images"]}}""").Ok);
        Assert.Contains(PastedSetup.Read("""{"drawers":{"rules":"images"}}""").Reasons, x => x.Field == "drawers.rules");
    }

    [Fact]
    public void A_bad_drawers_part_refuses_the_whole_answer_so_nothing_applies_partly()
    {
        var r = PastedSetup.Read("""{"desk":{"name":"A"},"drawers":{"rules":[{"kind":"images","drawer":"Photos"},{"kind":"nope","drawer":"X"}]}}""");
        Assert.False(r.Ok);
        Assert.Null(r.Rules);
        Assert.Empty(r.RuleDrawerNames);
    }

    [Fact]
    public void Both_parts_together_are_read()
    {
        var r = PastedSetup.Read("""{"desk":{"name":"A"},"drawers":{"rules":[{"kind":"images","drawer":"Photos"}]}}""");
        Assert.True(r.Ok);
        Assert.NotNull(r.Desk);
        Assert.Single(r.Rules!);
    }

    // -- duplicate keys, the first good block, drawer ids, forgiving JSON -----------------------------------------

    [Fact]
    public void A_repeated_key_is_refused_not_thrown_at_top_level_inside_desk_and_inside_a_rule()
    {
        var top = PastedSetup.Read("""{"desk":{"name":"A"},"desk":{"name":"B"}}""");
        Assert.False(top.Ok);
        Assert.Equal("design", Assert.Single(top.Reasons).Field);

        var nested = PastedSetup.Read("""{"desk":{"name":"A","name":"B"}}""");
        Assert.False(nested.Ok);

        var inRule = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","kind":"documents","drawer":"A"}]}}""");
        Assert.False(inRule.Ok);

        var inDrawers = PastedSetup.Read("""{"drawers":{"rules":[{"kind":"images","drawer":"A"}],"rules":[]}}""");
        Assert.False(inDrawers.Ok);

        // A later block that is clean is still found past the broken one.
        var after = PastedSetup.Read("""{"desk":{"name":"A"},"desk":{"name":"B"}} then {"desk":{"name":"C"}}""");
        Assert.True(after.Ok);
        Assert.Equal("C", (string?)after.Desk!["name"]);
    }

    [Fact]
    public void The_first_good_block_is_used_not_the_first_candidate()
    {
        var r = PastedSetup.Read("""{"desk":[]} {"desk":{"name":"A"}}""");
        Assert.True(r.Ok);
        Assert.Equal("A", (string?)r.Desk!["name"]);
        Assert.Equal(2, r.Blocks);

        var none = PastedSetup.Read("""{"desk":[]} {"drawers":{"rules":[]}}""");
        Assert.False(none.Ok);
        Assert.Equal(2, none.Blocks);
        Assert.Contains(none.Reasons, x => x.Field == "desk");
        Assert.DoesNotContain(none.Reasons, x => x.Field == "drawers.rules");
    }

    [Fact]
    public void Distinct_drawer_names_get_distinct_ids_and_one_name_keeps_one_id()
    {
        string Rules(params string[] names) =>
            "{\"drawers\":{\"rules\":[" + string.Join(",", names.Select(n => "{\"kind\":\"images\",\"drawer\":\"" + n + "\"}")) + "]}}";

        var scripts = PastedSetup.Read(Rules("写真", "Фото"));
        Assert.True(scripts.Ok);
        Assert.NotEqual(scripts.Rules![0].DrawerId, scripts.Rules[1].DrawerId);

        var slug = PastedSetup.Read(Rules("My Photos", "My-Photos"));
        Assert.True(slug.Ok);
        Assert.NotEqual(slug.Rules![0].DrawerId, slug.Rules[1].DrawerId);
        Assert.Equal(["My Photos", "My-Photos"], slug.RuleDrawerNames);

        var same = PastedSetup.Read(Rules("Photos", "Photos", "photos"));
        Assert.True(same.Ok);
        Assert.Single(same.Rules!.Select(x => x.DrawerId).Distinct());
        Assert.All(same.Rules!, x => Assert.True(DrawerIds.IsValid(x.DrawerId)));
    }

    [Fact]
    public void Rule_text_may_not_hold_a_character_a_file_name_cannot()
    {
        foreach (var ch in new[] { "*", "?", "<", ">", "|" })
            Assert.Contains(PastedSetup.Read("{\"drawers\":{\"rules\":[{\"kind\":\"name_contains\",\"drawer\":\"A\",\"text\":\"a" + ch + "b\"}]}}").Reasons,
                x => x.Field == "drawers.rules[0].text");
        Assert.True(PastedSetup.Read("""{"drawers":{"rules":[{"kind":"name_contains","drawer":"A","text":"invoice 2026-10"}]}}""").Ok);
    }

    [Fact]
    public void Trailing_commas_and_comments_are_forgiven()
    {
        var r = PastedSetup.Read("""
            {
              // a look
              "desk": { "name": "Calm", },
              "drawers": { "rules": [ { "kind": "images", "drawer": "Photos", }, ], },
            }
            """);
        Assert.True(r.Ok, string.Join("; ", r.Reasons));
        Assert.Equal("Calm", (string?)r.Desk!["name"]);
        Assert.Single(r.Rules!);
    }

    [Fact]
    public void A_block_that_nearly_parsed_is_told_apart_from_no_block()
    {
        var broken = PastedSetup.Read("""{"desk":{"name":"A" "note":"x"}}""");
        Assert.False(broken.Ok);
        Assert.Contains("did not parse", Assert.Single(broken.Reasons).Message);
        Assert.Contains("no Sanduhr block", Assert.Single(PastedSetup.Read("nothing here").Reasons).Message);
    }

    [Fact]
    public void The_kind_words_are_the_enums_in_snake_case()
    {
        Assert.Equal(["screenshots", "images", "documents", "installers", "archives", "shortcuts", "older_than", "name_contains", "name_starts_with"], PastedSetup.KindWords);
    }

    // ---- what the paste window adds on top of the reader (Design it with an AI chat) ----

    private static DeskValidation Validate(string json) =>
        DeskDesign.Validate(System.Text.Json.Nodes.JsonNode.Parse(json), DeskDesignContext.Unknown);

    [Fact]
    public void A_piece_this_build_does_not_draw_is_refused_by_field_though_the_Desk_rules_accept_the_word()
    {
        var plain = Validate("""{"layout":{"meetings":{"anchor":"tr"}}}""");
        Assert.True(plain.Ok);   // the Mac draws it, so the layout grammar knows it
        var refused = PastedSetup.RefuseUndrawn(plain);
        Assert.Equal(DeskDesignStatus.Rejected, refused.Status);
        Assert.Null(refused.Design);
        Assert.Equal("layout.meetings", Assert.Single(refused.Reasons).Field);
        // Hidden is no way around it: the word itself is the piece this build lacks.
        Assert.Equal("layout.meetings", Assert.Single(PastedSetup.RefuseUndrawn(Validate("""{"layout":{"meetings":{"hidden":true}}}""")).Reasons).Field);
    }

    [Fact]
    public void A_drawn_piece_and_a_design_with_no_layout_pass_the_undrawn_check_untouched()
    {
        var clock = Validate("""{"layout":{"clock":{"anchor":"tr"}}}""");
        Assert.Same(clock, PastedSetup.RefuseUndrawn(clock));
        var none = Validate("""{"name":"Calm"}""");
        Assert.Same(none, PastedSetup.RefuseUndrawn(none));
        Assert.All(SetupPrompt.Pieces, piece =>
            Assert.True(PastedSetup.RefuseUndrawn(Validate("{\"layout\":{\"" + piece + "\":{\"hidden\":true}}}")).Ok, piece));
    }

    [Fact]
    public void The_findings_read_an_AI_chat_not_Claude_and_list_errors_before_warnings()
    {
        var found = PastedSetup.Findings(
            [new DeskReason("launcher.buttons[0].image", "must be the stored image name; Claude cannot supply a new image")],
            [new DeskReason("ink", "is hard to read")]);
        Assert.Equal(2, found.Count);
        Assert.True(found[0].IsError);
        Assert.Contains("an AI chat cannot supply", found[0].Text);
        Assert.False(found[1].IsError);
        Assert.DoesNotContain(found, f => f.Field.Contains("Claude") || f.Text.Contains("Claude"));
    }

    // ---- fields outside the prompt's list, and the neutral wording ----

    [Fact]
    public void A_desk_field_outside_the_prompts_list_is_refused_by_field_in_the_paste_path()
    {
        var r = PastedSetup.Read("""{"desk":{"name":"A","theme":{"name":"X"},"looks":[],"launcher":{"buttons":[]},"follows_theme":true}}""");
        Assert.False(r.Ok);
        Assert.Equal(["desk.theme", "desk.looks", "desk.launcher", "desk.follows_theme"], r.Reasons.Select(x => x.Field));
        foreach (var field in SetupPrompt.DeskFields)
            Assert.True(PastedSetup.Read("{\"desk\":{\"" + field + "\":null}}").Reasons.All(x => !x.Field.StartsWith("desk.", StringComparison.Ordinal)), field);
    }

    [Fact]
    public void A_verdict_built_another_way_is_held_to_the_same_field_list()
    {
        var plain = Validate("""{"name":"A","follows_theme":true}""");
        Assert.True(plain.Ok);
        var refused = PastedSetup.RefuseUndrawn(plain);
        Assert.Equal(DeskDesignStatus.Rejected, refused.Status);
        Assert.Equal("desk.follows_theme", Assert.Single(refused.Reasons).Field);
    }

    [Fact]
    public void Neutral_replaces_the_validators_phrasings_and_leaves_pasted_text_as_typed()
    {
        Assert.Equal("an AI chat cannot supply a new image", PastedSetup.Neutral("Claude cannot supply a new image"));
        Assert.Equal("must be \"all\" or \"main\"", PastedSetup.Neutral("must be \"all\", \"main\" or a monitor id from get_desk monitors"));
        Assert.DoesNotContain("get_desk", PastedSetup.Neutral("is not a monitor connected to this PC; use \"all\", \"main\" or an id from monitors in get_desk."));
        Assert.DoesNotContain("get_desk", PastedSetup.Neutral("is not an app this PC's Start menu lists; use an id from start_menu_apps in get_desk. A button opens only what the Start menu offers."));
        Assert.DoesNotContain("get_desk", PastedSetup.Neutral("must be the stored image name from get_desk, or null; Claude cannot supply a new image"));
        // Text the chat typed comes back as it was.
        const string echoed = "unknown member 'Claude'; use anchor, size and Claude Code";
        Assert.Equal(echoed, PastedSetup.Neutral(echoed));
    }

    [Fact]
    public void The_same_finding_from_the_reader_and_the_validator_shows_once()
    {
        var one = new DeskReason("desk.theme", "is not a field a chat can set");
        Assert.Single(PastedSetup.Findings([one], [one]), f => f.IsError);
        Assert.Single(PastedSetup.Findings([one, one], []));
    }
}
