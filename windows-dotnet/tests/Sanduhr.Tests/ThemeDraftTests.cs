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
}
