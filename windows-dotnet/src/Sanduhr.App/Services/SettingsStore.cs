using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>The persisted window frame — x/y/w/h. Mirrors the Python
/// <c>settings["geom"]</c> dict written by <c>widget._save_settings</c>.</summary>
public readonly record struct WindowFrame(double X, double Y, double Width, double Height);

/// <summary>
/// Minimal read/write over <c>%APPDATA%\Sanduhr\settings.json</c> for the four
/// things the widget shell owns this milestone: the window frame, tier order,
/// the hidden-tier set, and the pinned flag. The full Settings UI is item 7.
///
/// <b>Data-compat (load-bearing):</b> the file is loaded as a whole JSON object
/// and only the keys this store manages are mutated, so the Python build's other
/// keys (<c>theme</c>, <c>snake_high_score</c>, <c>tip_dismissed</c>,
/// <c>focus_mode_duration</c>, …) survive a round-trip untouched. Same file,
/// same schema, zero migration — an existing user keeps their settings.
///
/// <b>The gotcha this guards:</b> the frame is only ever written by the move
/// handler (<see cref="SaveFrame"/>), never from a resize event — see
/// <c>MainWindow</c>. That mirrors the Python fix where persisting mid-resize
/// captured layout garbage (e.g. 420x123) and relaunched the widget invisible.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(Paths paths) => _path = paths.SettingsFile;

    private JsonObject Read()
    {
        try
        {
            if (File.Exists(_path))
                return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Corrupt / unreadable settings start fresh rather than crash launch.
        }
        return new JsonObject();
    }

    private void Write(JsonObject root)
    {
        try
        {
            File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (IOException)
        {
            // Best-effort persistence — a failed write must never take down the UI.
        }
    }

    /// <summary>The saved frame, or null when none has been persisted yet.
    /// Reads the modern "geom" key, falling back to the legacy "window" key
    /// (parity with <c>widget._restore_geometry</c>).</summary>
    public WindowFrame? LoadFrame()
    {
        var root = Read();
        var geom = root["geom"] as JsonObject ?? root["window"] as JsonObject;
        if (geom is null) return null;
        if (geom["x"] is null || geom["y"] is null || geom["w"] is null || geom["h"] is null)
            return null;
        try
        {
            return new WindowFrame(
                geom["x"]!.GetValue<double>(),
                geom["y"]!.GetValue<double>(),
                geom["w"]!.GetValue<double>(),
                geom["h"]!.GetValue<double>());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persist the frame under "geom" (and drop the legacy "window" key).
    /// MOVE-only — never call from a resize handler.</summary>
    public void SaveFrame(WindowFrame frame)
    {
        var root = Read();
        root["geom"] = new JsonObject
        {
            ["x"] = frame.X,
            ["y"] = frame.Y,
            ["w"] = frame.Width,
            ["h"] = frame.Height,
        };
        root.Remove("window");
        Write(root);
    }

    public bool LoadPinned()
    {
        var root = Read();
        try { return root["pinned"]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    public void SavePinned(bool pinned)
    {
        var root = Read();
        root["pinned"] = pinned;
        Write(root);
    }

    /// <summary>The saved theme key (settings.json "theme"), defaulting to
    /// "obsidian" when absent — parity with <c>widget.py</c>'s
    /// <c>settings.get("theme", "obsidian")</c>.</summary>
    public string LoadTheme()
    {
        var root = Read();
        try { return root["theme"]?.GetValue<string>() ?? "obsidian"; }
        catch { return "obsidian"; }
    }

    public void SaveTheme(string key)
    {
        var root = Read();
        root["theme"] = key;
        Write(root);
    }

    /// <summary>The widget's "Themes" header expand/collapse state (settings.json
    /// "themes_expanded"), defaulting to false (collapsed) so the widget rests with
    /// the swatch grid tucked under the thin header.</summary>
    public bool LoadThemesExpanded()
    {
        var root = Read();
        try { return root["themes_expanded"]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    public void SaveThemesExpanded(bool expanded)
    {
        var root = Read();
        root["themes_expanded"] = expanded;
        Write(root);
    }

    /// <summary>The saved sparkline style (settings.json "sparkline_style"),
    /// defaulting to Classic. A deliberate upgrade over the Python build, which reset
    /// the graph mode to classic each launch.</summary>
    public SparklineStyle LoadSparklineStyle()
    {
        var root = Read();
        try
        {
            return Enum.TryParse<SparklineStyle>(root["sparkline_style"]?.GetValue<string>(), out var s)
                ? s
                : SparklineStyle.Classic;
        }
        catch { return SparklineStyle.Classic; }
    }

    public void SaveSparklineStyle(SparklineStyle style)
    {
        var root = Read();
        root["sparkline_style"] = style.ToString();
        Write(root);
    }

    /// <summary>The Local CC tab's "show project &amp; skill breakdown" preference
    /// (settings.json "local_cc_show_breakdowns"), defaulting to true — parity with
    /// <c>local_cc_dialog</c>'s default-on breakdown tables.</summary>
    public bool LoadLocalCcShowBreakdowns()
    {
        var root = Read();
        try { return root["local_cc_show_breakdowns"]?.GetValue<bool>() ?? true; }
        catch { return true; }
    }

    public void SaveLocalCcShowBreakdowns(bool show)
    {
        var root = Read();
        root["local_cc_show_breakdowns"] = show;
        Write(root);
    }

    /// <summary>The focus-timer duration in minutes (settings.json
    /// "focus_mode_duration"), defaulting to 25 — parity with
    /// <c>widget.py</c>'s <c>settings.get("focus_mode_duration", 25)</c>.
    /// Clamped to the 1–120 range on read.</summary>
    public int LoadFocusDuration()
    {
        var root = Read();
        try
        {
            int v = root["focus_mode_duration"]?.GetValue<int>() ?? 25;
            return Math.Clamp(v, 1, 120);
        }
        catch { return 25; }
    }

    public void SaveFocusDuration(int minutes)
    {
        var root = Read();
        root["focus_mode_duration"] = Math.Clamp(minutes, 1, 120);
        Write(root);
    }

    /// <summary>The cooldown-snake high score (settings.json "snake_high_score"),
    /// defaulting to 0 — parity with <c>widget.py</c>'s
    /// <c>settings.get("snake_high_score", 0)</c>.</summary>
    public int LoadSnakeHighScore()
    {
        var root = Read();
        try { return root["snake_high_score"]?.GetValue<int>() ?? 0; }
        catch { return 0; }
    }

    public void SaveSnakeHighScore(int score)
    {
        var root = Read();
        root["snake_high_score"] = score;
        Write(root);
    }

    public IReadOnlyList<string> LoadTierOrder() => LoadStringArray("tier_order");

    public void SaveTierOrder(IEnumerable<string> order)
    {
        var root = Read();
        root["tier_order"] = ToArray(order);
        Write(root);
    }

    public IReadOnlyList<string> LoadHiddenTiers() => LoadStringArray("hidden_tiers");

    public void SaveHiddenTiers(IEnumerable<string> hidden)
    {
        var root = Read();
        root["hidden_tiers"] = ToArray(hidden);
        Write(root);
    }

    private IReadOnlyList<string> LoadStringArray(string key)
    {
        var root = Read();
        if (root[key] is not JsonArray arr) return Array.Empty<string>();
        var outp = new List<string>();
        foreach (var n in arr)
        {
            var s = n?.GetValue<string>();
            if (!string.IsNullOrEmpty(s)) outp.Add(s);
        }
        return outp;
    }

    private static JsonArray ToArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr;
    }

    // -- WS-B alerts (spec 2026-07-12-threshold-alerts-design.md §4) ------------

    /// <summary>Alert engine config. Defaults: enabled, warn 80, urgent 95,
    /// projection on, reset notification off.</summary>
    public AlertConfig LoadAlertConfig()
    {
        var root = Read();
        bool enabled = true; int warn = 80; int urgent = 95; bool projection = true; bool reset = false;
        try { enabled = root["alerts_enabled"]?.GetValue<bool>() ?? true; } catch { }
        try { warn = root["alert_warn_pct"]?.GetValue<int>() ?? 80; } catch { }
        try { urgent = root["alert_urgent_pct"]?.GetValue<int>() ?? 95; } catch { }
        try { projection = root["alert_projection"]?.GetValue<bool>() ?? true; } catch { }
        try { reset = root["alert_reset"]?.GetValue<bool>() ?? false; } catch { }
        warn = Math.Clamp(warn, 1, 99);
        urgent = Math.Clamp(urgent, 1, 99);
        if (warn >= urgent) { warn = 80; urgent = 95; }   // corrupt pair -> defaults
        return new AlertConfig(enabled, warn, urgent, projection, reset);
    }

    public void SaveAlertConfig(AlertConfig config)
    {
        var root = Read();
        root["alerts_enabled"] = config.Enabled;
        root["alert_warn_pct"] = config.WarnPct;
        root["alert_urgent_pct"] = config.UrgentPct;
        root["alert_projection"] = config.ProjectionEnabled;
        root["alert_reset"] = config.ResetEnabled;
        Write(root);
    }

    public bool LoadAlertSound()
    {
        var root = Read();
        try { return root["alert_sound"]?.GetValue<bool>() ?? true; } catch { return true; }
    }

    public void SaveAlertSound(bool on)
    {
        var root = Read();
        root["alert_sound"] = on;
        Write(root);
    }

    public bool LoadAlertSnakeFull()
    {
        var root = Read();
        try { return root["alert_snake_full"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveAlertSnakeFull(bool on)
    {
        var root = Read();
        root["alert_snake_full"] = on;
        Write(root);
    }

    // -- WS-C usage vault (spec 2026-07-12-usage-vault-design.md) ---------------

    /// <summary>Whether the first-run per-root consent dialog has been shown.
    /// Consent is PROMPTED, never silent — a silently-archived employer root is
    /// the tenant breach the design review named.</summary>
    public bool LoadVaultPrompted()
    {
        var root = Read();
        try { return root["vault_prompted"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveVaultPrompted(bool prompted)
    {
        var root = Read();
        root["vault_prompted"] = prompted;
        Write(root);
    }

    /// <summary>Per-root vault consent ({".claude": true, ...}). A root absent
    /// from the map is NOT consented. Consent-off is the erasure tombstone —
    /// purge flips it so the next cycle can't re-backfill.</summary>
    public IReadOnlyDictionary<string, bool> LoadVaultRoots()
    {
        var root = Read();
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (root["vault_roots"] is JsonObject map)
        {
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        }
        return result;
    }

    public void SaveVaultRoots(IReadOnlyDictionary<string, bool> roots)
    {
        var root = Read();
        var map = new JsonObject();
        foreach (var (name, on) in roots)
            map[name] = on;
        root["vault_roots"] = map;
        Write(root);
    }

    /// <summary>Hidden setting (no UI): store full cwd paths in session rows.
    /// Off by default — the vault stores basename + hash only.</summary>
    public bool LoadVaultStoreFullPaths()
    {
        var root = Read();
        try { return root["vault_store_full_paths"]?.GetValue<bool>() ?? false; } catch { return false; }
    }
}
