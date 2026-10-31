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
}
