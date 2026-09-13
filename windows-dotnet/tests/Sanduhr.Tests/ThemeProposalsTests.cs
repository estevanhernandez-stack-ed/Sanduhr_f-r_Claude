using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ThemeProposalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 20, 0, 0, TimeSpan.Zero);

    private static JsonObject CleanTheme(string name = "Nebula") => (JsonObject)JsonNode.Parse($$"""
        {
          "name": "{{name}}",
          "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c",
          "title_bg": "#161616", "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a",
          "text": "#e8e4dc", "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
          "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff"
        }
        """)!;

    private static ThemeHandoff.Request Req(JsonObject theme, string? saveAs = null, bool apply = true)
        => new("id1", theme, saveAs, apply, Now);

    private static bool Reserved(string key) => ThemeCatalog.BuiltIns.ContainsKey(key);

    [Fact]
    public void Applied_writes_the_file_and_names_the_previous_theme()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.Path, "themes");

        var o = ThemeProposals.Process(Req(CleanTheme()), dir, "obsidian", Reserved);

        Assert.Equal("applied", (string?)o.Payload["status"]);
        Assert.Equal("nebula", o.ApplyKey);
        Assert.Equal("nebula", (string?)o.Payload["key"]);
        Assert.Equal("Nebula", (string?)o.Payload["name"]);
        Assert.Equal("obsidian", (string?)o.Payload["previous_key"]);
        Assert.Equal(Path.Combine(dir, "nebula.json"), o.SavedPath);
        Assert.True(File.Exists(o.SavedPath!));
        // The saved file loads through the same loader the strip uses.
        Assert.Equal("Nebula", ThemeCatalog.LoadUserThemes(dir)["nebula"].Name);
        Assert.Empty((JsonArray)o.Payload["findings"]!);
    }

    [Fact]
    public void Saved_without_apply_has_no_apply_key()
    {
        using var tmp = new TempDir();
        var o = ThemeProposals.Process(Req(CleanTheme(), "night-2", apply: false), Path.Combine(tmp.Path, "themes"), "mint", Reserved);
        Assert.Equal("saved", (string?)o.Payload["status"]);
        Assert.Null(o.ApplyKey);
        Assert.Equal("night-2", (string?)o.Payload["key"]);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "themes", "night-2.json")));
    }

    [Fact]
    public void Broken_theme_is_rejected_with_findings_and_no_file()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.Path, "themes");
        var theme = CleanTheme();
        theme["text"] = "#12";
        var o = ThemeProposals.Process(Req(theme), dir, "obsidian", Reserved);
        Assert.Equal("rejected", (string?)o.Payload["status"]);
        Assert.Equal("invalid_theme", (string?)o.Payload["reason"]);
        Assert.Contains((JsonArray)o.Payload["findings"]!, f => (string?)f!["field"] == "text");
        Assert.Null(o.ApplyKey);
        Assert.False(Directory.Exists(dir) && Directory.GetFiles(dir).Length > 0);
    }

    [Fact]
    public void A_built_in_name_is_reserved()
    {
        using var tmp = new TempDir();
        var o = ThemeProposals.Process(Req(CleanTheme("Matrix")), Path.Combine(tmp.Path, "themes"), "obsidian", Reserved);
        Assert.Equal("rejected", (string?)o.Payload["status"]);
        Assert.Equal("reserved_name", (string?)o.Payload["reason"]);
        // ...but save_as routes around it.
        var o2 = ThemeProposals.Process(Req(CleanTheme("Matrix"), "matrix-2"), Path.Combine(tmp.Path, "themes"), "obsidian", Reserved);
        Assert.Equal("applied", (string?)o2.Payload["status"]);
        Assert.Equal("matrix-2", o2.ApplyKey);
    }

    [Fact]
    public void Warnings_ride_along_with_an_applied_theme()
    {
        using var tmp = new TempDir();
        var theme = CleanTheme();
        theme["sparkline"] = "#ff8800";
        var o = ThemeProposals.Process(Req(theme), Path.Combine(tmp.Path, "themes"), "obsidian", Reserved);
        Assert.Equal("applied", (string?)o.Payload["status"]);
        Assert.Contains((JsonArray)o.Payload["findings"]!, f => (string?)f!["field"] == "sparkline" && (string?)f!["level"] == "warning");
    }

    [Fact]
    public void A_second_proposal_with_the_same_key_overwrites()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.Path, "themes");
        ThemeProposals.Process(Req(CleanTheme()), dir, "obsidian", Reserved);
        var second = CleanTheme();
        second["accent"] = "#00ff88";
        second["sparkline"] = "#00ff88";
        var o = ThemeProposals.Process(Req(second), dir, "nebula", Reserved);
        Assert.Equal("applied", (string?)o.Payload["status"]);
        Assert.Equal("#00ff88", ThemeCatalog.LoadUserThemes(dir)["nebula"].Accent);
        Assert.Equal("nebula", (string?)o.Payload["previous_key"]);
    }
}
