using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/ThemeModel.cs — ported from <c>themes.py</c>. Covers the
/// 6 built-in palettes (count + a known palette's values + the Matrix/Blueprint
/// extras), the usage-color ramp, and the user-theme load/validate/merge flow.
/// </summary>
public class ThemeModelTests
{
    [Fact]
    public void Six_builtins_in_python_order()
    {
        Assert.Equal(6, ThemeCatalog.BuiltIns.Count);
        Assert.Equal(
            new[] { "obsidian", "aurora", "ember", "mint", "matrix", "blueprint" },
            ThemeCatalog.BuiltInOrder);
        foreach (var key in ThemeCatalog.BuiltInOrder)
            Assert.True(ThemeCatalog.BuiltIns.ContainsKey(key));
    }

    [Fact]
    public void Obsidian_values_match_python()
    {
        var t = ThemeCatalog.BuiltIns["obsidian"];
        Assert.Equal("Obsidian", t.Name);
        Assert.Equal("#0d0d0d", t.Bg);
        Assert.Equal("#1a1a1c", t.GlassOnMica);
        Assert.Equal("#6c63ff", t.Accent);
        Assert.Equal("#ff6b6b", t.PaceMarker);
        Assert.Equal(0.85, t.GlassAlpha);
        Assert.Equal(0.30, t.BorderAlpha);
        Assert.Null(t.BorderTint);
        Assert.Equal(4, t.AccentBloom.Blur);
        Assert.Equal(0.35, t.AccentBloom.Alpha);
        Assert.Null(t.InnerHighlight);
        Assert.False(t.OptsOutOfMica);
    }

    [Fact]
    public void Aurora_carries_border_tint_and_inner_highlight()
    {
        var t = ThemeCatalog.BuiltIns["aurora"];
        Assert.Equal("#38bdf8", t.BorderTint);
        Assert.NotNull(t.InnerHighlight);
        Assert.Equal("#38bdf8", t.InnerHighlight!.Color);
        Assert.Equal(0.20, t.InnerHighlight.Alpha);
        Assert.Equal(0.50, t.BorderAlpha);
    }

    [Fact]
    public void Matrix_opts_out_with_terminal_extras()
    {
        var t = ThemeCatalog.BuiltIns["matrix"];
        Assert.True(t.OptsOutOfMica);
        Assert.Equal(1.0, t.GlassAlpha);
        Assert.Equal("Cascadia Code", t.MonospaceFont);
        Assert.Equal("Consolas", t.MonospaceFallback);
        Assert.Equal(2, t.CardCornerRadius);
        Assert.Equal(1800, t.BreathPeriodMs);
        Assert.Equal("#00ff41", t.Accent);
    }

    [Fact]
    public void Blueprint_sets_bg_grid()
    {
        var t = ThemeCatalog.BuiltIns["blueprint"];
        Assert.True(t.BgGrid);
        Assert.Equal(0.70, t.BorderAlpha);
        Assert.Equal(8, t.AccentBloom.Blur);
    }

    [Theory]
    [InlineData(0, "#4ade80")]
    [InlineData(49, "#4ade80")]
    [InlineData(50, "#facc15")]
    [InlineData(74, "#facc15")]
    [InlineData(75, "#fb923c")]
    [InlineData(89, "#fb923c")]
    [InlineData(90, "#f87171")]
    [InlineData(100, "#f87171")]
    public void Usage_color_ramp_matches_python(int pct, string expected)
    {
        Assert.Equal(expected, ThemeCatalog.UsageColor(pct));
    }

    [Fact]
    public void Load_user_themes_keys_by_lowercased_stem_and_applies_defaults()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "themes");
        Directory.CreateDirectory(dir);
        // File stem "Sunset" -> key "sunset"; only required fields supplied.
        File.WriteAllText(Path.Combine(dir, "Sunset.json"), MinimalTheme("Sunset"));

        var loaded = ThemeCatalog.LoadUserThemes(dir);

        Assert.True(loaded.ContainsKey("sunset"));
        var t = loaded["sunset"];
        Assert.Equal("Sunset", t.Name);
        // Omitted dials fall back to _DEFAULT_GLASS_TUNING.
        Assert.Equal(0.80, t.GlassAlpha);
        Assert.Equal(0.40, t.BorderAlpha);
        Assert.Null(t.BorderTint);
        Assert.Equal(4, t.AccentBloom.Blur);
        Assert.Equal(0.45, t.AccentBloom.Alpha);
        Assert.Null(t.InnerHighlight);
        Assert.False(t.OptsOutOfMica);
        Assert.False(t.BgGrid);
    }

    [Fact]
    public void Load_user_themes_parses_optional_dials()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "themes");
        Directory.CreateDirectory(dir);
        var json = MinimalTheme("Custom");
        var obj = JsonNode.Parse(json)!.AsObject();
        obj["glass_alpha"] = 0.72;
        obj["border_tint"] = "#abcdef";
        obj["accent_bloom"] = new JsonObject { ["blur"] = 7, ["alpha"] = 0.6 };
        obj["inner_highlight"] = new JsonObject { ["color"] = "#112233", ["alpha"] = 0.25 };
        obj["opts_out_of_mica"] = true;
        obj["card_corner_radius"] = 3;
        File.WriteAllText(Path.Combine(dir, "custom.json"), obj.ToJsonString());

        var t = ThemeCatalog.LoadUserThemes(dir)["custom"];

        Assert.Equal(0.72, t.GlassAlpha);
        Assert.Equal("#abcdef", t.BorderTint);
        Assert.Equal(7, t.AccentBloom.Blur);
        Assert.Equal(0.6, t.AccentBloom.Alpha);
        Assert.Equal("#112233", t.InnerHighlight!.Color);
        Assert.Equal(0.25, t.InnerHighlight.Alpha);
        Assert.True(t.OptsOutOfMica);
        Assert.Equal(3, t.CardCornerRadius);
    }

    [Fact]
    public void Load_user_themes_skips_missing_field_files_and_warns()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "themes");
        Directory.CreateDirectory(dir);
        // Missing every field except name.
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{\"name\": \"Broken\"}");

        var warnings = new List<string>();
        var loaded = ThemeCatalog.LoadUserThemes(dir, warnings.Add);

        Assert.Empty(loaded);
        Assert.Single(warnings);
        Assert.Contains("missing required fields", warnings[0]);
    }

    [Fact]
    public void Load_user_themes_skips_invalid_json_and_warns()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "themes");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "garbage.json"), "{ not valid json ");

        var warnings = new List<string>();
        var loaded = ThemeCatalog.LoadUserThemes(dir, warnings.Add);

        Assert.Empty(loaded);
        Assert.Single(warnings);
    }

    [Fact]
    public void Load_user_themes_creates_dir_and_returns_empty_when_none()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "themes");
        Assert.False(Directory.Exists(dir));

        var loaded = ThemeCatalog.LoadUserThemes(dir);

        Assert.True(Directory.Exists(dir)); // created on scan, parity with Python
        Assert.Empty(loaded);
    }

    // ---- theme packs (3.9.1): the installed packs' themes join the list under <packId>:<stem> ----------------------------------

    private static (string Root, WidgetStore Store) ThemePackStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "sanduhr-packthemes-" + Guid.NewGuid().ToString("N")[..10]);
        var store = new WidgetStore(Path.Combine(root, "widgets"), WidgetTestKit.EncodePng);
        var files = WidgetPackFixtures.Load("good", "good-theme");
        var v = PackValidator.Validate(files, "good-theme", strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        Assert.True(store.Install(files, v.Manifest!, WidgetSource.Catalog).Ok);
        return (root, store);
    }

    [Fact]
    public void An_installed_theme_pack_joins_the_merged_map_keyed_by_pack_and_stem()
    {
        var (root, store) = ThemePackStore();
        try
        {
            string themesDir = Path.Combine(root, "themes");
            Directory.CreateDirectory(themesDir);
            File.WriteAllText(Path.Combine(themesDir, "sunset.json"), MinimalTheme("Sunset"));

            var map = PackThemes.Merged(themesDir, store);

            Assert.Equal("Fixture Dark", map["good-theme:fixture-dark"].Name);
            Assert.True(map.ContainsKey("sunset"));
            foreach (var key in ThemeCatalog.BuiltInOrder) Assert.True(map.ContainsKey(key));
            Assert.Equal(6, ThemeCatalog.BuiltIns.Count); // the built-ins table itself never grows
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_pack_theme_that_fails_the_lint_is_skipped_and_the_rest_still_load()
    {
        var (root, store) = ThemePackStore();
        try
        {
            string dir = Path.Combine(store.PackDir("good-theme"), "themes");
            var broken = new JsonObject { ["name"] = "Broken", ["bg"] = "#000000" }; // required colors missing
            File.WriteAllText(Path.Combine(dir, "broken.json"), broken.ToJsonString());
            var warnings = new List<string>();

            var map = PackThemes.Merged(Path.Combine(root, "themes"), store, warnings.Add);

            Assert.True(map.ContainsKey("good-theme:fixture-dark"));
            Assert.False(map.ContainsKey("good-theme:broken"));
            Assert.Contains(warnings, w => w.Contains("good-theme") && w.Contains("broken"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_theme_pack_without_a_themes_folder_adds_nothing_and_none_is_created()
    {
        var (root, store) = ThemePackStore();
        try
        {
            Directory.Delete(Path.Combine(store.PackDir("good-theme"), "themes"), true);

            var map = PackThemes.Load(store.InstalledOfKind("theme"));

            Assert.Empty(map);
            Assert.False(Directory.Exists(Path.Combine(store.PackDir("good-theme"), "themes")));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>A theme JSON with exactly the required color fields, no dials.</summary>
    private static string MinimalTheme(string name)
    {
        var obj = new JsonObject
        {
            ["name"] = name,
            ["bg"] = "#101010",
            ["glass"] = "#202020",
            ["glass_on_mica"] = "#1a1a1a",
            ["title_bg"] = "#161616",
            ["border"] = "#333333",
            ["text"] = "#eeeeee",
            ["text_secondary"] = "#bbbbbb",
            ["text_dim"] = "#888888",
            ["text_muted"] = "#555555",
            ["accent"] = "#ff00ff",
            ["bar_bg"] = "#2a2a2a",
            ["footer_bg"] = "#111111",
            ["pace_marker"] = "#ff6b6b",
            ["sparkline"] = "#ff00ff",
        };
        return obj.ToJsonString();
    }

    [Fact]
    public void A_user_theme_file_with_font_and_desk_loads_them()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sanduhr-theme-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var o = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
            o["name"] = "Dusk";
            o["font"] = "Consolas";
            o["desk"] = JsonNode.Parse("""{"bars": ["#6c63ff", "#e8e4dc"]}""");
            File.WriteAllText(Path.Combine(dir, "dusk.json"), o.ToJsonString());
            var dusk = ThemeCatalog.LoadUserThemes(dir)["dusk"];
            Assert.Equal("Consolas", dusk.Font);
            Assert.Equal(["#6c63ff", "#e8e4dc"], dusk.Desk!.Bars);
            Assert.Null(ThemeCatalog.BuiltIns["obsidian"].Desk);   // no built-in sets one
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
