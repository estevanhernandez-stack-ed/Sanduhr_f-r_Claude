using System.Text.Json.Nodes;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>propose_theme: lint here first (rejected means nothing was written),
/// then the request-file handoff to the widget with a typed result or a typed
/// "queued" when the widget does not answer. The file names and field names
/// mirror Core's ThemeHandoff literally.</summary>
public class ToolLogicThemeTests
{
    private static McpConfig ThemeConfig(TempDir tmp, TimeSpan? wait = null) => new()
    {
        SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
        VaultDir = Path.Combine(tmp.Path, "vault"),
        ConsentedRoots = Array.Empty<(string, string)>(),
        RootsFound = Array.Empty<string>(),
        ThemeRequestPath = Path.Combine(tmp.Path, "theme-request.json"),
        ThemeResultPath = Path.Combine(tmp.Path, "theme-result.json"),
        ThemeWaitTimeout = wait ?? TimeSpan.FromMilliseconds(300),
        ThemePollInterval = TimeSpan.FromMilliseconds(20),
    };

    private static JsonObject CleanTheme() => (JsonObject)JsonNode.Parse("""
        {
          "name": "Nebula",
          "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c",
          "title_bg": "#161616", "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a",
          "text": "#e8e4dc", "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
          "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff"
        }
        """)!;

    [Fact]
    public void Broken_theme_is_rejected_with_findings_and_writes_nothing()
    {
        using var tmp = new TempDir();
        var cfg = ThemeConfig(tmp);
        var theme = CleanTheme();
        theme["accent"] = "purple";
        theme.Remove("bar_bg");

        var r = new ToolLogic(cfg, () => Now).BuildProposeTheme(theme, null, true);

        Assert.Equal("rejected", (string?)r["status"]);
        Assert.Equal("invalid_theme", (string?)r["reason"]);
        var fields = ((JsonArray)r["findings"]!).Select(f => (string?)f!["field"]).ToList();
        Assert.Contains("accent", fields);
        Assert.Contains("bar_bg", fields);
        Assert.All((JsonArray)r["findings"]!, f => Assert.Equal("error", (string?)f!["level"]));
        Assert.False(File.Exists(cfg.ThemeRequestPath!));
    }

    [Fact]
    public void Missing_theme_and_bad_save_as_are_typed_invalid_params()
    {
        using var tmp = new TempDir();
        var logic = new ToolLogic(ThemeConfig(tmp), () => Now);
        Assert.Equal("invalid_params", (string?)logic.BuildProposeTheme(null, null, true)["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildProposeTheme(CleanTheme(), "Bad Name", true)["reason"]);
        Assert.Equal("invalid_params", (string?)logic.BuildProposeTheme(CleanTheme(), "-x", true)["reason"]);
        Assert.False(File.Exists(Path.Combine(tmp.Path, "theme-request.json")));
    }

    [Fact]
    public void Clean_theme_writes_the_request_the_widget_expects_and_returns_its_result()
    {
        using var tmp = new TempDir();
        var cfg = ThemeConfig(tmp, TimeSpan.FromSeconds(5));
        var logic = new ToolLogic(cfg, () => Now);

        // Play the widget: answer as soon as the request lands.
        var widget = Task.Run(async () =>
        {
            while (!File.Exists(cfg.ThemeRequestPath!))
                await Task.Delay(10);
            var req = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.ThemeRequestPath!))!;
            Assert.Equal("Nebula", (string?)req["theme"]!["name"]);
            Assert.Equal("nebula-x", (string?)req["save_as"]);
            Assert.False(req["apply"]!.GetValue<bool>());
            Assert.NotNull(req["requested_at"]);
            var result = new JsonObject
            {
                ["id"] = (string?)req["id"],
                ["result"] = new JsonObject
                {
                    ["status"] = "saved",
                    ["reason"] = null,
                    ["remedy"] = null,
                    ["key"] = "nebula-x",
                    ["name"] = "Nebula",
                    ["previous_key"] = "obsidian",
                },
            };
            File.WriteAllText(cfg.ThemeResultPath!, result.ToJsonString());
            File.Delete(cfg.ThemeRequestPath!);
        });

        var r = logic.BuildProposeTheme(CleanTheme(), "nebula-x", apply: false);
        widget.Wait();

        Assert.Equal("saved", (string?)r["status"]);
        Assert.Equal("nebula-x", (string?)r["key"]);
        Assert.Equal("obsidian", (string?)r["previous_key"]);
        Assert.NotNull(r["request_id"]);
        Assert.NotNull(r["findings"]);   // the lint's warnings ride along (none here)
        Assert.Empty((JsonArray)r["findings"]!);
    }

    [Fact]
    public void Silent_widget_returns_queued_and_leaves_the_request()
    {
        using var tmp = new TempDir();
        var cfg = ThemeConfig(tmp);

        var r = new ToolLogic(cfg, () => Now).BuildProposeTheme(CleanTheme(), null, true);

        Assert.Equal("queued", (string?)r["status"]);
        Assert.Equal("widget_not_responding", (string?)r["reason"]);
        Assert.Equal("Nebula", (string?)r["name"]);
        Assert.True(File.Exists(cfg.ThemeRequestPath!));
        var req = (JsonObject)JsonNode.Parse(File.ReadAllText(cfg.ThemeRequestPath!))!;
        Assert.Null(req["save_as"]);
        Assert.True(req["apply"]!.GetValue<bool>());
    }

    [Fact]
    public void Warnings_do_not_block_but_travel_with_the_result()
    {
        using var tmp = new TempDir();
        var cfg = ThemeConfig(tmp);
        var theme = CleanTheme();
        theme["text"] = "#5a5a5a";   // low contrast: a warning, not an error

        var r = new ToolLogic(cfg, () => Now).BuildProposeTheme(theme, null, true);

        Assert.Equal("queued", (string?)r["status"]);   // reached the handoff, so not rejected
        var findings = (JsonArray)r["findings"]!;
        Assert.Contains(findings, f => (string?)f!["field"] == "text" && (string?)f!["level"] == "warning");
    }

    [Fact]
    public void Unwired_config_is_a_typed_unavailable()
    {
        using var tmp = new TempDir();
        var cfg = Config(Path.Combine(tmp.Path, "snapshot.json"));   // no theme paths
        var r = new ToolLogic(cfg, () => Now).BuildProposeTheme(CleanTheme(), null, true);
        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("unavailable", (string?)r["reason"]);
    }

    [Fact]
    public void Key_rule_mirrors_core()
    {
        Assert.True(ToolLogic.IsValidThemeKey("nebula-2"));
        Assert.False(ToolLogic.IsValidThemeKey("Nebula"));
        Assert.False(ToolLogic.IsValidThemeKey("-a"));
        Assert.False(ToolLogic.IsValidThemeKey(new string('a', 41)));
    }
}
