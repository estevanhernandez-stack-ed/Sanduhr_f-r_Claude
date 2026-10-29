using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ThemeDraftTests
{
    [Fact]
    public void Draft_from_a_built_in_round_trips_through_json_and_lints_clean()
    {
        var d = ThemeDraft.FromDefinition("matrix", ThemeCatalog.BuiltIns["matrix"]);

        Assert.Equal("matrix", d.SourceKey);
        Assert.Equal("Matrix", d.Name);
        Assert.Equal("#00ff41", d.GetColor("accent"));
        Assert.Equal(1.0, d.GlassAlpha);

        var lint = d.Lint();
        Assert.True(lint.Ok, string.Join("; ", lint.Findings.Select(f => f.Message)));
        Assert.Empty(lint.Findings);
        // Extras the studio never shows survive Save.
        var json = d.ToJson();
        Assert.True(json["opts_out_of_mica"]!.GetValue<bool>());
        Assert.Equal(ThemeCatalog.BuiltIns["matrix"].MonospaceFont, (string?)json["monospace_font"]);
        Assert.Equal(ThemeCatalog.BuiltIns["matrix"].CardCornerRadius, (int?)json["card_corner_radius"]?.GetValue<int>());
        Assert.Equal(ThemeCatalog.BuiltIns["matrix"].BorderTint, (string?)json["border_tint"]);
    }

    [Fact]
    public void Every_built_in_survives_the_draft_round_trip_field_for_field()
    {
        foreach (var (key, def) in ThemeCatalog.BuiltIns)
        {
            var back = ThemeLint.Lint(ThemeDraft.FromDefinition(key, def).ToJson()).Theme;
            Assert.NotNull(back);
            Assert.Equal(def with { }, back);   // records: value equality across every field
        }
    }

    [Fact]
    public void Typing_a_bad_hex_marks_the_row_but_keeps_the_last_good_color_for_preview()
    {
        var d = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]);

        Assert.False(d.SetColor("accent", "#6c6"));
        Assert.Equal("#6c6", d.GetColor("accent"));           // what the user typed stays visible
        Assert.False(d.IsColorValid("accent"));
        Assert.Equal("#6c63ff", ThemeDraft.ToHex(d.TryGetColor("accent")!.Value));

        var lint = d.Lint();
        Assert.False(lint.Ok);
        Assert.Contains(lint.Errors, f => f.Field == "accent");

        var preview = d.PreviewDefinition();
        Assert.NotNull(preview);
        Assert.Equal("#6c63ff", preview!.Accent);              // preview never breaks mid-keystroke

        Assert.True(d.SetColor("accent", " #FF8800 "));
        Assert.Equal("#FF8800", d.GetColor("accent"));
        Assert.Equal("#ff8800", d.PreviewDefinition()!.Accent.ToLowerInvariant());
    }

    [Fact]
    public void Blank_draft_has_no_preview_until_every_row_parsed_once()
    {
        var d = new ThemeDraft { Name = "New" };
        Assert.Null(d.PreviewDefinition());
        foreach (var (field, _, _) in ThemeDraft.ColorTokens)
            d.SetColor(field, "#123456");
        Assert.NotNull(d.PreviewDefinition());
        Assert.False(d.Lint().Ok == false && d.Lint().Errors.Any());   // all parse, so no errors
    }

    [Fact]
    public void Preview_tolerates_a_bad_name()
    {
        var d = ThemeDraft.FromDefinition("mint", ThemeCatalog.BuiltIns["mint"]);
        d.Name = "";
        Assert.False(d.Lint().Ok);
        Assert.Equal("Draft", d.PreviewDefinition()!.Name);
    }

    [Fact]
    public void Dials_are_rounded_into_the_document()
    {
        var d = ThemeDraft.FromDefinition("aurora", ThemeCatalog.BuiltIns["aurora"]);
        d.GlassAlpha = 0.8333333;
        d.BorderAlpha = 0.456789;
        var json = d.ToJson();
        Assert.Equal(0.83, json["glass_alpha"]!.GetValue<double>());
        Assert.Equal(0.46, json["border_alpha"]!.GetValue<double>());
    }

    [Fact]
    public void Unknown_token_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ThemeDraft().SetColor("nope", "#000000"));
    }

    [Fact]
    public void Token_list_is_the_lint_schema_in_studio_order()
    {
        var fields = ThemeDraft.ColorTokens.Select(t => t.Field).ToHashSet();
        var required = ThemeCatalog.RequiredColorFields.Where(f => f != "name").ToHashSet();
        Assert.Equal(required, fields);
        Assert.All(ThemeDraft.ColorTokens, t => Assert.False(string.IsNullOrWhiteSpace(t.Job)));
    }

    // ---- theme match: the canonical serializer carries font and desk ----

    private static ThemeDefinition Matched() => ThemeCatalog.BuiltIns["obsidian"] with
    {
        Name = "Obsidian + Desk",
        Font = "Consolas",
        Desk = new ThemeDesk { Ink = ["#e8e4dc", "#6c63ff"], Plate = false, Shadow = "#070707", Font = "EsteFont Pro" },
    };

    [Fact]
    public void Font_and_desk_survive_the_draft_round_trip()
    {
        var back = ThemeLint.Lint(ThemeDraft.FromDefinition("x", Matched()).ToJson()).Theme;
        Assert.Equal(Matched(), back);
    }

    [Fact]
    public void A_theme_without_font_or_desk_writes_neither_and_keeps_its_canonical_string()
    {
        var json = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        Assert.False(json.ContainsKey("font"));
        Assert.False(json.ContainsKey("desk"));
        // Saved designs compare themes by this string: a theme from before theme match is still the same theme.
        Assert.DoesNotContain("desk", DesignTheme.Canonical(ThemeCatalog.BuiltIns["obsidian"]));
    }

    [Fact]
    public void The_draft_edits_font_and_desk_and_an_empty_one_writes_nothing()
    {
        var d = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]);
        d.Font = "Segoe UI";
        d.Desk = new ThemeDesk { Bars = ["#6c63ff"] };
        var json = d.ToJson();
        Assert.Equal("Segoe UI", (string?)json["font"]);
        Assert.Equal("#6c63ff", (string?)json["desk"]!["bars"]![0]);
        d.Desk = new ThemeDesk();
        d.Font = "";
        Assert.False(d.ToJson().ContainsKey("desk"));
        Assert.False(d.ToJson().ContainsKey("font"));
    }

    [Fact]
    public void Designs_and_get_desk_keep_a_matched_theme_whole()
    {
        // DeskState.Current and DesignTheme.Canonical both go through the draft: a field it dropped would vanish from every design.
        var canonical = DesignTheme.Canonical(ThemeDraft.FromDefinition("x", Matched()).ToJson());
        Assert.Equal("Consolas", (string?)canonical["font"]);
        Assert.False((bool)canonical["desk"]!["plate"]!);
    }

    [Fact]
    public void A_design_plan_keeps_font_and_desk_through_the_apply_path()
    {
        // DesignTheme.Plan (DeskDesignApply) lints the theme and canonicalizes it through the draft.
        var theme = ThemeDraft.FromDefinition("x", Matched()).ToJson();
        var plan = DesignTheme.Plan(theme, _ => null);
        Assert.NotNull(plan);
        Assert.Equal("Consolas", plan!.Definition.Font);
        Assert.Equal(Matched().Desk, plan.Definition.Desk);
        Assert.Contains("\"desk\"", DesignTheme.Canonical(plan.Definition));
    }

    [Fact]
    public void The_draft_lint_passes_the_font_check_through()
    {
        var d = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]);
        d.Font = "Nope Sans";
        Assert.Contains(d.Lint(_ => false).Warnings, f => f.Field == "font");
        Assert.Empty(d.Lint().Findings);
    }
}
