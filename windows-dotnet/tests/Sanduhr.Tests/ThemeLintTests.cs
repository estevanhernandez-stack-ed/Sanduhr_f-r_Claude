using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ThemeLintTests
{
    /// <summary>A clean theme (Obsidian's values) as mutable JSON.</summary>
    private static JsonObject Clean() => (JsonObject)JsonNode.Parse("""
        {
          "name": "Test",
          "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c",
          "title_bg": "#161616", "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a",
          "text": "#e8e4dc", "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
          "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff",
          "glass_alpha": 0.85, "border_alpha": 0.30
        }
        """)!;

    private static IEnumerable<string> Fields(ThemeLintResult r, ThemeFindingLevel level)
        => r.Findings.Where(f => f.Level == level).Select(f => f.Field);

    // -- calibration ----------------------------------------------------------

    [Theory]
    [InlineData("obsidian")]
    [InlineData("aurora")]
    [InlineData("ember")]
    [InlineData("mint")]
    [InlineData("matrix")]
    [InlineData("blueprint")]
    public void Every_built_in_lints_with_no_findings(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        var json = new JsonObject
        {
            ["name"] = def.Name, ["bg"] = def.Bg, ["glass"] = def.Glass, ["glass_on_mica"] = def.GlassOnMica,
            ["title_bg"] = def.TitleBg, ["border"] = def.Border, ["footer_bg"] = def.FooterBg, ["bar_bg"] = def.BarBg,
            ["text"] = def.Text, ["text_secondary"] = def.TextSecondary, ["text_dim"] = def.TextDim, ["text_muted"] = def.TextMuted,
            ["accent"] = def.Accent, ["pace_marker"] = def.PaceMarker, ["sparkline"] = def.Sparkline,
            ["glass_alpha"] = def.GlassAlpha, ["border_alpha"] = def.BorderAlpha, ["border_tint"] = def.BorderTint,
            ["opts_out_of_mica"] = def.OptsOutOfMica,
        };

        var r = ThemeLint.Lint(json);

        Assert.True(r.Ok, string.Join("; ", r.Findings.Select(f => $"{f.Field}: {f.Message}")));
        Assert.Empty(r.Findings);
        Assert.Equal(def.Accent, r.Theme!.Accent);
    }

    [Fact]
    public void Clean_theme_is_ok_and_carries_defaults_for_omitted_dials()
    {
        var r = ThemeLint.Lint(Clean());
        Assert.True(r.Ok);
        Assert.Empty(r.Findings);
        Assert.Equal(0.85, r.Theme!.GlassAlpha);
        Assert.Equal(new AccentBloom(4, 0.45), r.Theme.AccentBloom);
        Assert.Null(r.Theme.InnerHighlight);
    }

    // -- schema errors ----------------------------------------------------------

    [Fact]
    public void Invalid_json_is_one_error_on_the_json_field()
    {
        var r = ThemeLint.Lint("{ not json");
        Assert.False(r.Ok);
        Assert.Equal("json", Assert.Single(r.Findings).Field);
        Assert.False(ThemeLint.Lint("[1,2]").Ok);
    }

    [Fact]
    public void Missing_required_color_is_an_error_naming_the_field()
    {
        var j = Clean();
        j.Remove("pace_marker");
        var r = ThemeLint.Lint(j);
        Assert.False(r.Ok);
        Assert.Null(r.Theme);
        Assert.Contains("pace_marker", Fields(r, ThemeFindingLevel.Error));
    }

    [Theory]
    [InlineData("#fff")]          // short form
    [InlineData("#ff00ff80")]     // alpha form
    [InlineData("ff00ff")]        // no hash
    [InlineData("#gg0000")]       // not hex
    [InlineData("red")]
    public void Malformed_hex_is_an_error_that_names_the_accepted_form(string bad)
    {
        var j = Clean();
        j["accent"] = bad;
        var r = ThemeLint.Lint(j);
        Assert.False(r.Ok);
        var f = Assert.Single(r.Errors);
        Assert.Equal("accent", f.Field);
        Assert.Contains("#rrggbb", f.Message);
    }

    [Fact]
    public void Non_string_color_is_an_error_not_a_crash()
    {
        var j = Clean();
        j["bg"] = 12;
        var r = ThemeLint.Lint(j);
        Assert.False(r.Ok);
        Assert.Contains("bg", Fields(r, ThemeFindingLevel.Error));
    }

    [Theory]
    [InlineData("glass_alpha", 1.5)]
    [InlineData("border_alpha", -0.1)]
    [InlineData("card_corner_radius", 99)]
    [InlineData("breath_period_ms", 10)]
    public void Dial_out_of_range_is_an_error(string field, double value)
    {
        var j = Clean();
        j[field] = value;
        var r = ThemeLint.Lint(j);
        Assert.False(r.Ok);
        Assert.Contains(field, Fields(r, ThemeFindingLevel.Error));
    }

    [Fact]
    public void Nested_dials_are_checked_with_dotted_field_names()
    {
        var j = Clean();
        j["accent_bloom"] = new JsonObject { ["blur"] = 40, ["alpha"] = 0.5 };
        j["inner_highlight"] = new JsonObject { ["color"] = "nope", ["alpha"] = 0.2 };
        var r = ThemeLint.Lint(j);
        Assert.False(r.Ok);
        Assert.Contains("accent_bloom.blur", Fields(r, ThemeFindingLevel.Error));
        Assert.Contains("inner_highlight.color", Fields(r, ThemeFindingLevel.Error));
    }

    [Fact]
    public void Name_rules()
    {
        var j = Clean();
        j["name"] = "";
        Assert.Contains("name", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Error));
        j["name"] = new string('x', 25);
        Assert.Contains("name", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Error));
        j.Remove("name");
        Assert.Contains("name", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Error));
    }

    [Fact]
    public void Present_null_border_tint_is_fine()
    {
        var j = Clean();
        j["border_tint"] = null;
        j["inner_highlight"] = null;
        Assert.True(ThemeLint.Lint(j).Ok);
    }

    // -- design-rule warnings ---------------------------------------------------

    [Fact]
    public void Light_base_warns_and_still_applies()
    {
        var j = Clean();
        j["glass_on_mica"] = "#f0f0f0";
        j["glass"] = "#f0f0f0";
        var r = ThemeLint.Lint(j);
        Assert.True(r.Ok);
        Assert.Contains("glass_on_mica", Fields(r, ThemeFindingLevel.Warning));
        Assert.Contains("glass", Fields(r, ThemeFindingLevel.Warning));
        Assert.DoesNotContain("bg", Fields(r, ThemeFindingLevel.Warning));
    }

    [Fact]
    public void Low_text_contrast_warns_with_the_measured_ratio()
    {
        var j = Clean();
        j["text"] = "#5a5a5a";
        var r = ThemeLint.Lint(j);
        Assert.True(r.Ok);
        var f = Assert.Single(r.Warnings, w => w.Field == "text");
        Assert.Contains(":1", f.Message);
        Assert.Contains("4.5", f.Message);
    }

    [Fact]
    public void Text_ramp_must_descend_in_luminance()
    {
        var j = Clean();
        j["text_dim"] = "#ffffff";   // brighter than text_secondary
        var r = ThemeLint.Lint(j);
        Assert.Contains("text_dim", Fields(r, ThemeFindingLevel.Warning));
    }

    [Fact]
    public void Text_ramp_must_share_a_hue_when_it_has_one()
    {
        var j = Clean();
        j["text"] = "#ffb0b0";        // pink
        j["text_secondary"] = "#b0ffb0"; // green, clearly a different hue
        j["text_dim"] = "#802020";
        j["text_muted"] = "#501010";
        var r = ThemeLint.Lint(j);
        Assert.Contains("text_secondary", Fields(r, ThemeFindingLevel.Warning));
        // Grays carry no hue: a neutral ramp never trips this rule.
        var g = Clean();
        g["text"] = "#eeeeee"; g["text_secondary"] = "#bbbbbb"; g["text_dim"] = "#777777"; g["text_muted"] = "#555555";
        Assert.DoesNotContain("text_secondary", Fields(ThemeLint.Lint(g), ThemeFindingLevel.Warning));
    }

    [Fact]
    public void Pace_marker_hidden_on_the_green_fill_warns_unless_the_hue_carries_it()
    {
        var j = Clean();
        j["pace_marker"] = "#4ade80";    // the green fill itself
        Assert.Contains("pace_marker", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Warning));
        j["pace_marker"] = "#fbbf24";    // Ember's amber: same brightness, complementary hue
        Assert.DoesNotContain("pace_marker", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Warning));
    }

    [Fact]
    public void Sparkline_and_border_tint_should_share_the_accent_hue()
    {
        var j = Clean();
        j["sparkline"] = "#ff8800";
        j["border_tint"] = "#00ff88";
        var r = ThemeLint.Lint(j);
        Assert.Contains("sparkline", Fields(r, ThemeFindingLevel.Warning));
        Assert.Contains("border_tint", Fields(r, ThemeFindingLevel.Warning));
    }

    [Fact]
    public void Mica_opt_out_with_translucent_glass_warns()
    {
        var j = Clean();
        j["opts_out_of_mica"] = true;
        Assert.Contains("glass_alpha", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Warning));
        j["glass_alpha"] = 1.0;
        Assert.DoesNotContain("glass_alpha", Fields(ThemeLint.Lint(j), ThemeFindingLevel.Warning));
    }

    // -- output ---------------------------------------------------------------

    [Fact]
    public void ToJson_shape_is_level_field_message()
    {
        var j = Clean();
        j.Remove("bg");
        var arr = ThemeLint.ToJson(ThemeLint.Lint(j).Findings);
        var one = (JsonObject)arr[0]!;
        Assert.Equal("error", (string?)one["level"]);
        Assert.Equal("bg", (string?)one["field"]);
        Assert.False(string.IsNullOrEmpty((string?)one["message"]));
    }

    // -- color math -------------------------------------------------------------

    [Fact]
    public void Color_math_matches_the_reference_values()
    {
        Assert.True(ThemeLint.TryParseHex("#ffffff", out var white));
        Assert.Equal(1.0, ThemeLint.Luminance(white), 3);
        Assert.True(ThemeLint.TryParseHex("#000000", out var black));
        Assert.Equal(21.0, ThemeLint.Contrast(ThemeLint.Luminance(white), ThemeLint.Luminance(black)), 1);
        Assert.True(ThemeLint.TryParseHex("#ff0000", out var red));
        Assert.Equal(0, ThemeLint.Hue(red), 1);
        Assert.True(ThemeLint.TryParseHex("#00ff00", out var green));
        Assert.Equal(120, ThemeLint.Hue(green), 1);
        Assert.Equal(120, ThemeLint.HueDistance(red, green)!.Value, 1);
        Assert.Null(ThemeLint.HueDistance(red, black));
        var mid = ThemeLint.Composite(black, 0.5, white);
        Assert.Equal(0.5, mid.R, 3);
    }

    // -- the catalog's validator now sits on the lint -------------------------------

    [Fact]
    public void TryValidate_reports_a_malformed_hex_instead_of_accepting_it()
    {
        var j = Clean();
        j["accent"] = "#zzz";
        var warnings = new List<string>();
        Assert.Null(ThemeCatalog.TryValidate("bad", j, warnings.Add));
        Assert.Contains(warnings, w => w.Contains("malformed field") && w.Contains("accent"));
    }
}
