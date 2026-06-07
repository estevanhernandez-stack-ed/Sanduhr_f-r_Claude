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
}
