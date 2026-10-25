using System.Globalization;
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
    private readonly string _mcpSettingsPath;

    /// <summary>The folders this store reads and writes under (a scratch profile in tests).</summary>
    public Paths Paths { get; }

    public SettingsStore(Paths paths)
    {
        Paths = paths;
        _path = paths.SettingsFile;
        // The projection sanduhr-mcp reads, in %USERPROFILE%\.sanduhr (spec Plan B, B.2).
        _mcpSettingsPath = paths.McpSettingsFile;
        _logFile = paths.LogFile;
    }

    private readonly string _logFile;

    /// <summary>Path-free log line (the PRIVACY log contract: operation + exception type only). Never throws.</summary>
    private void LogLine(string line)
    {
        try { File.AppendAllText(_logFile, $"{DateTime.UtcNow:o} {line}{Environment.NewLine}"); }
        catch { /* logging must never break the caller */ }
    }

    /// <summary>Brings <c>mcp-settings.json</c> in line with the current settings.json (start-up, and after
    /// any write). Never throws.</summary>
    public void SyncMcpExport()
    {
        if (ResetGuard.Done)
            return;
        McpSettingsExport.Sync(Read(), _mcpSettingsPath, LogLine);
    }

    /// <summary>
    /// Reset Sanduhr…, "Claude Code entries" part: turns <c>mcp_enabled</c> and <c>statusline_enabled</c> off and drops
    /// <c>mcp_cc_home</c> and <c>statusline_cc_home</c>, so a Reset that leaves "Settings" unchecked doesn't bring the
    /// projection, the launcher or the script back at the next launch.
    ///
    /// This is the ONE write allowed while <see cref="ResetGuard.Done"/> is set (Reset marks it before its steps run),
    /// and it is narrow on purpose: it only edits an existing settings.json, changes only those four keys, and does NOT
    /// re-sync <c>mcp-settings.json</c> (the same step deletes that file). Never throws; false when nothing was written.
    /// </summary>
    public bool ClearClaudeCodeSwitchesForReset()
    {
        try
        {
            if (!File.Exists(_path))
                return false;
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root)
                return false;
            if (!ResetSettings.ClearClaudeCodeSwitches(root))
                return false;
            File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogLine($"reset: switches not cleared ({e.GetType().Name})");
            return false;
        }
    }

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
        if (ResetGuard.Done)
            return;   // Reset Sanduhr… has run: nothing writes settings.json again
        try
        {
            File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (IOException)
        {
            // Best-effort persistence — a failed write must never take down the UI.
            return;
        }
        McpSettingsExport.Sync(root, _mcpSettingsPath, LogLine);
        try { AnyWritten?.Invoke(); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* a subscriber's problem never fails a save */ }
    }

    /// <summary>Raised after any <see cref="SettingsStore"/> wrote settings.json, on whatever thread wrote it (the
    /// Desk design service listens, to keep <c>desk-state.json</c> current). Static: the app makes several stores.</summary>
    public static event Action? AnyWritten;

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

    /// <summary>Settings ▸ Themes editor mode (settings.json "themes_studio"):
    /// true = the token studio, false (default) = the paste box.</summary>
    public bool LoadThemesStudioMode()
    {
        var root = Read();
        try { return root["themes_studio"]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    public void SaveThemesStudioMode(bool on)
    {
        var root = Read();
        root["themes_studio"] = on;
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

    /// <summary>Which number the tray icon shows (settings.json "tray_mode"),
    /// defaulting to the highest visible tier.</summary>
    public TrayMode LoadTrayMode()
    {
        var root = Read();
        try { return TrayReadout.Parse(root["tray_mode"]?.GetValue<string>()); }
        catch { return TrayMode.Highest; }
    }

    public void SaveTrayMode(TrayMode mode)
    {
        var root = Read();
        root["tray_mode"] = TrayReadout.Raw(mode);
        Write(root);
    }

    /// <summary>First-seen dates and hidden-until-reset records for limits that come and go
    /// ("limit_first_seen", "limits_hidden_until_reset"), via Core's JSON helpers.</summary>
    public LimitState LoadLimitState()
    {
        var root = Read();
        return new LimitState(
            LimitLifetime.FirstSeenFromJson(root["limit_first_seen"]),
            LimitLifetime.HiddenFromJson(root["limits_hidden_until_reset"]));
    }

    public void SaveLimitState(LimitState state)
    {
        var root = Read();
        root["limit_first_seen"] = LimitLifetime.FirstSeenToJson(state.FirstSeen);
        root["limits_hidden_until_reset"] = LimitLifetime.HiddenToJson(state.HiddenUntilReset);
        Write(root);
    }

    /// <summary>Tiers whose warnings the user turned off ("alert_muted_tiers").</summary>
    public IReadOnlyList<string> LoadAlertMutedTiers() => LoadStringArray("alert_muted_tiers");

    public void SaveAlertMutedTiers(IEnumerable<string> tiers)
    {
        var root = Read();
        root["alert_muted_tiers"] = ToArray(tiers);
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

    /// <summary>Whether model-scoped weekly tiers (sonnet/opus/fable + dynamic
    /// per-model rows) chime like everything else. Defaults FALSE: a single
    /// model's sub-limit sitting at 100% isn't critical while the aggregate
    /// pools have headroom, so scoped crossings are visual-only unless the
    /// user opts back in.</summary>
    public bool LoadAlertSoundScoped()
    {
        var root = Read();
        try { return root["alert_sound_scoped"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveAlertSoundScoped(bool on)
    {
        var root = Read();
        root["alert_sound_scoped"] = on;
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

    // -- WS-E statusline bridge (spec 2026-07-12-statusline-mcp-design.md) ------

    /// <summary>Whether the Claude Code statusline snapshot writer is on
    /// (settings.json "statusline_enabled"). Default FALSE — the snapshot file
    /// is opt-in, created only through the consented install flow.</summary>
    public bool LoadStatuslineEnabled()
    {
        var root = Read();
        try { return root["statusline_enabled"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveStatuslineEnabled(bool on)
    {
        var root = Read();
        root["statusline_enabled"] = on;
        Write(root);
    }

    /// <summary>Which Claude Code home the statusline was registered in
    /// (settings.json "statusline_cc_home", e.g. ".claude-personal"). Recorded at
    /// install so removal targets the SAME home the consent chose.</summary>
    public string? LoadStatuslineCcHome()
    {
        var root = Read();
        try { return root["statusline_cc_home"]?.GetValue<string>(); } catch { return null; }
    }

    public void SaveStatuslineCcHome(string ccHomeName)
    {
        var root = Read();
        root["statusline_cc_home"] = ccHomeName;
        Write(root);
    }

    // -- WS-E MCP integration (registration slice) ------------------------------

    /// <summary>Whether the Sanduhr MCP server integration is installed
    /// (settings.json "mcp_enabled"). Default FALSE — consent-gated install.</summary>
    public bool LoadMcpEnabled()
    {
        var root = Read();
        try { return root["mcp_enabled"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveMcpEnabled(bool on)
    {
        var root = Read();
        root["mcp_enabled"] = on;
        Write(root);
    }

    /// <summary>Which CC home the MCP server was registered in
    /// (settings.json "mcp_cc_home") — removal targets the same home.</summary>
    public string? LoadMcpCcHome()
    {
        var root = Read();
        try { return root["mcp_cc_home"]?.GetValue<string>(); } catch { return null; }
    }

    public void SaveMcpCcHome(string ccHomeName)
    {
        var root = Read();
        root["mcp_cc_home"] = ccHomeName;
        Write(root);
    }

    /// <summary>Per-root MCP read consent ({".claude-personal": true, ...}) —
    /// the map the SERVER reads (settings.json "mcp_roots"). A root absent from
    /// the map is NOT consented; tool results become conversation content, so
    /// this is a separate, stricter gate than the vault's. Unchecked by default
    /// at install (the design review's tenant-wall requirement).</summary>
    public IReadOnlyDictionary<string, bool> LoadMcpRoots()
    {
        var root = Read();
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (root["mcp_roots"] is JsonObject map)
        {
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        }
        return result;
    }

    public void SaveMcpRoots(IReadOnlyDictionary<string, bool> roots)
    {
        var root = Read();
        var map = new JsonObject();
        foreach (var (name, on) in roots)
            map[name] = on;
        root["mcp_roots"] = map;
        Write(root);
    }

    /// <summary>Claude Code folders the user added by hand (settings.json "cc_folders_added", a list of
    /// full paths). They are installer targets on the Claude Code page and are never read by the MCP server.</summary>
    public IReadOnlyList<string> LoadCcFoldersAdded() => LoadStringArray("cc_folders_added");

    public void SaveCcFoldersAdded(IEnumerable<string> folders)
    {
        var root = Read();
        var list = folders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (list.Count == 0) root.Remove("cc_folders_added"); else root["cc_folders_added"] = ToArray(list);
        Write(root);
    }

    /// <summary>"Follow the account I'm using" (settings.json "follow_account"), off by default.</summary>
    public bool LoadFollowAccount()
    {
        var root = Read();
        try { return root["follow_account"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveFollowAccount(bool on)
    {
        var root = Read();
        root["follow_account"] = on;
        Write(root);
    }

    /// <summary>The Claude Code edge glow switches (settings.json "claude_code_glow"
    /// {"waiting", "done", "skip_terminal"}). Off, off, and skip-terminal on until set.</summary>
    public GlowSwitches LoadClaudeCodeGlow()
    {
        var o = Read()["claude_code_glow"] as JsonObject;
        return new GlowSwitches(
            Waiting: ReadBool(o, "waiting", false),
            Done: ReadBool(o, "done", false),
            SkipTerminal: ReadBool(o, "skip_terminal", true));
    }

    public void SaveClaudeCodeGlow(GlowSwitches switches)
    {
        var root = Read();
        // The switches only: the glow target's members (spec 6.8) beside them are kept.
        if (root["claude_code_glow"] is not JsonObject glow)
        {
            glow = new JsonObject();
            root["claude_code_glow"] = glow;
        }
        glow["waiting"] = switches.Waiting;
        glow["done"] = switches.Done;
        glow["skip_terminal"] = switches.SkipTerminal;
        Write(root);
    }

    /// <summary>Where the Claude Code glow lands (spec 6.8): "claude_code_glow" members "target",
    /// "edge_side" and "edge_monitor". Absent reads as the window in use (S4's behavior).</summary>
    public GlowTargetSettings LoadGlowTarget() => GlowTargetSettings.Decode(Read()["claude_code_glow"] as JsonObject);

    /// <summary>Writes the target members; the glow switches beside them are kept.</summary>
    public void SaveGlowTarget(GlowTargetSettings target)
    {
        var root = Read();
        if (root["claude_code_glow"] is not JsonObject glow)
        {
            glow = new JsonObject();
            root["claude_code_glow"] = glow;
        }
        target.WriteInto(glow);
        Write(root);
    }

    /// <summary>The Desk ink (spec 6.7): "desk_ink" {mode, color, shadow}; absent is Automatic.</summary>
    public DeskInkSettings LoadDeskInk() => DeskInkSettings.Decode(Read()["desk_ink"]);

    public void SaveDeskInk(DeskInkSettings ink)
    {
        var root = Read();
        root["desk_ink"] = ink.Encode();
        Write(root);
    }

    /// <summary>The Desk message (spec 6.2): "desk_message" {pin, rotate, mix, special, seconds, size,
    /// color, glow, font}; absent members are the Mac's defaults.</summary>
    public DeskMessageSettings LoadDeskMessage() => DeskMessageSettings.Decode(Read()["desk_message"]);

    public void SaveDeskMessage(DeskMessageSettings message)
    {
        var root = Read();
        root["desk_message"] = message.Encode();
        Write(root);
    }

    /// <summary>Now playing (spec 6.3): "now_playing" {widget, keep_paused, excluded, visualizer}.
    /// App ids only; a title is never stored.</summary>
    public NowPlayingSettings LoadNowPlaying() => NowPlayingSettings.Decode(Read()["now_playing"]);

    public void SaveNowPlaying(NowPlayingSettings np)
    {
        var root = Read();
        root["now_playing"] = np.Encode();
        Write(root);
    }

    /// <summary>The weather piece's place (spec 6.5): "desk_weather" {name, admin, country, lat, lon,
    /// unit}, coordinates rounded to two decimals.</summary>
    public DeskWeatherSettings LoadDeskWeather() => DeskWeatherSettings.Decode(Read()["desk_weather"]);

    public void SaveDeskWeather(DeskWeatherSettings weather)
    {
        var root = Read();
        root["desk_weather"] = weather.Encode();
        Write(root);
    }

    /// <summary>The Desk (spec 5.1): "desk_enabled", "desk_layout", "desk_margins"
    /// {"left", "right", "top", "bottom"}, "desk_font" and "desk_time_size". Absent or unreadable
    /// keys read as <see cref="DeskSettings.Default"/>: off, "clock:bl claude:bl", 52/52/40/60,
    /// EsteFont Pro, 112.</summary>
    public DeskSettings LoadDesk()
    {
        var root = Read();
        var d = DeskSettings.Default;
        var m = root["desk_margins"] as JsonObject;
        string? font = null;
        try { font = root["desk_font"]?.GetValue<string>(); } catch { }
        string layout = d.Layout;
        try { layout = root["desk_layout"]?.GetValue<string>() ?? d.Layout; } catch { }
        return new DeskSettings(
            ReadBool(root, "desk_enabled", false),
            layout,
            new DeskMarginSettings(
                ReadDouble(m, "left", d.Margins.Left), ReadDouble(m, "right", d.Margins.Right),
                ReadDouble(m, "top", d.Margins.Top), ReadDouble(m, "bottom", d.Margins.Bottom)),
            font,
            ReadDouble(root, "desk_time_size", d.TimeSize),
            DeskPlate.Read(root)).Normalized();
    }

    public void SaveDesk(DeskSettings desk)
    {
        var d = desk.Normalized();
        var root = Read();
        root["desk_enabled"] = d.Enabled;
        root["desk_layout"] = d.Layout;
        root["desk_margins"] = new JsonObject
        {
            ["left"] = d.Margins.Left,
            ["right"] = d.Margins.Right,
            ["top"] = d.Margins.Top,
            ["bottom"] = d.Margins.Bottom,
        };
        if (d.Font is null) root.Remove("desk_font");
        else root["desk_font"] = d.Font;
        root["desk_time_size"] = d.TimeSize;
        Write(root);
    }

    /// <summary>The Desk Launcher (spec, Desk Launcher): <c>desk_launcher</c> {orientation, buttons[]}. Absent, or
    /// an object with nothing usable in it, reads as no buttons in a row.</summary>
    public LauncherModel LoadLauncher() => LauncherModel.Parse(Read()["desk_launcher"]);

    public void SaveLauncher(LauncherModel launcher)
    {
        var root = Read();
        // No buttons in a row is the same as absent: the key goes away rather than hold an empty shell.
        if (launcher.IsEmpty && launcher.Orientation == LauncherOrientation.Row) root.Remove("desk_launcher");
        else root["desk_launcher"] = launcher.ToJson();
        Write(root);
    }

    /// <summary>"Plate behind the text": <c>desk_plate</c>, absent = off (<see cref="DeskPlate"/>).</summary>
    public bool LoadDeskPlate() => DeskPlate.Read(Read());

    public void SaveDeskPlate(bool on)
    {
        var root = Read();
        if (on) root["desk_plate"] = true;
        else root.Remove("desk_plate");
        Write(root);
    }

    /// <summary>A fresh install's first launch writes the plate on; an existing install keeps what it has.</summary>
    public void SeedDeskPlate(bool freshInstall)
    {
        var root = Read();
        if (DeskPlate.Seed(root, freshInstall)) Write(root);
    }

    /// <summary>"Clock and message take clicks" (spec 7.4): <c>desk_pieces_take_clicks</c>, absent = on.</summary>
    public bool LoadDeskPiecesTakeClicks() => ReadBool(Read(), DeskPieceClicks.Key, DeskPieceClicks.DefaultOn);

    public void SaveDeskPiecesTakeClicks(bool on)
    {
        var root = Read();
        root["desk_pieces_take_clicks"] = on;
        Write(root);
    }

    /// <summary>"OLED care" (spec 3.7.0): <c>desk_oled_care</c>, absent = off (<see cref="OledCare"/>).</summary>
    public bool LoadOledCare() => OledCare.Read(Read());

    public void SaveOledCare(bool on)
    {
        var root = Read();
        if (on) root["desk_oled_care"] = true;
        else root.Remove("desk_oled_care");
        Write(root);
    }

    /// <summary>The Desk companion (spec 3.8 section 4): <c>desk_companion</c> {pack, roam}; absent = no pack chosen, roaming on.</summary>
    public CompanionSettings LoadCompanion() => CompanionSettings.Decode(Read()["desk_companion"]);

    public void SaveCompanion(CompanionSettings companion)
    {
        var root = Read();
        root["desk_companion"] = companion.Encode();
        Write(root);
    }

    /// <summary>"Let Claude show watchers" (spec 6.9): <c>watchers_agents</c>, absent = off. The MCP tools
    /// <c>watch_start</c>, <c>watch_update</c> and <c>watch_end</c> write nothing while it is off.</summary>
    public bool LoadWatchersAgents() => ReadBool(Read(), "watchers_agents", false);

    public void SaveWatchersAgents(bool on)
    {
        var root = Read();
        if (on) root["watchers_agents"] = true;
        else root.Remove("watchers_agents");
        Write(root);
    }

    /// <summary>"Show my CI runners' jobs" (spec ci-watchers-3.9): <c>watchers_ci_runners</c>, absent = off.
    /// On, Sanduhr reads the log output of GitHub Actions runner containers through Docker's local pipe.</summary>
    public bool LoadWatchersCiRunners() => ReadBool(Read(), "watchers_ci_runners", false);

    public void SaveWatchersCiRunners(bool on)
    {
        var root = Read();
        if (on) root["watchers_ci_runners"] = true;
        else root.Remove("watchers_ci_runners");
        Write(root);
    }

    /// <summary>"Let Claude change the Desk directly" (spec, the agent-designed Desk): <c>desk_direct</c>, absent = off. On, a design Claude
    /// proposes is applied without the review bar (the Desk it replaces is still saved first).</summary>
    public bool LoadDeskDirect() => ReadBool(Read(), "desk_direct", false);

    public void SaveDeskDirect(bool on)
    {
        var root = Read();
        if (on) root["desk_direct"] = true;
        else root.Remove("desk_direct");
        Write(root);
    }

    /// <summary>The id of the saved design "Put my Desk back" switches to: <c>desk_put_back</c>, the Desk as it was before the last change.
    /// Null when there is none.</summary>
    public string? LoadDeskPutBack()
    {
        try { return Read()["desk_put_back"]?.GetValue<string>() is { } id && SavedDesign.IsValidId(id) ? id : null; }
        catch (Exception e) when (e is InvalidOperationException or FormatException) { return null; }
    }

    public void SaveDeskPutBack(string? id)
    {
        var root = Read();
        if (id is not null && SavedDesign.IsValidId(id)) root["desk_put_back"] = id;
        else root.Remove("desk_put_back");
        Write(root);
    }

    /// <summary>The Desk's sign-in reminder switch (spec 7.5): "desk_sign_in_reminder". Absent reads as on.</summary>
    public bool LoadDeskSignInReminder() => ReadBool(Read(), "desk_sign_in_reminder", SignInReminder.DefaultOn);

    public void SaveDeskSignInReminder(bool on)
    {
        var root = Read();
        root["desk_sign_in_reminder"] = on;
        Write(root);
    }

    /// <summary>Just the Desk (spec 7.5): "just_the_desk" and "just_the_desk_silenced_reminder". Both absent
    /// read as <see cref="JustTheDeskState.Off"/>.</summary>
    public JustTheDeskState LoadJustTheDesk()
    {
        var root = Read();
        bool on = ReadBool(root, "just_the_desk", false);
        return on ? new JustTheDeskState(true, ReadBool(root, "just_the_desk_silenced_reminder", false)) : JustTheDeskState.Off;
    }

    /// <summary>Writes Just the Desk. Off removes both keys; the silenced key is written only when true.</summary>
    public void SaveJustTheDesk(JustTheDeskState state)
    {
        var root = Read();
        if (state.On)
        {
            root["just_the_desk"] = true;
            if (state.SilencedReminder) root["just_the_desk_silenced_reminder"] = true;
            else root.Remove("just_the_desk_silenced_reminder");
        }
        else
        {
            root.Remove("just_the_desk");
            root.Remove("just_the_desk_silenced_reminder");
        }
        Write(root);
    }

    private static double ReadDouble(JsonObject? o, string key, double fallback)
    {
        try { return o?[key]?.GetValue<double>() ?? fallback; } catch { return fallback; }
    }

    private static bool ReadBool(JsonObject? o, string key, bool fallback)
    {
        try { return o?[key]?.GetValue<bool>() ?? fallback; } catch { return fallback; }
    }

    /// <summary>Linked Claude Code folders by account label (settings.json "account_folders"
    /// {label: folder path}). Blank paths are dropped.</summary>
    public Dictionary<string, string> LoadAccountFolders()
    {
        var root = Read();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["account_folders"] is JsonObject map)
        {
            foreach (var (label, node) in map)
            {
                try
                {
                    var path = node?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path)) result[label] = path;
                }
                catch { /* a non-string entry reads as unlinked */ }
            }
        }
        return result;
    }

    public void SaveAccountFolders(IReadOnlyDictionary<string, string> folders)
    {
        var root = Read();
        var map = new JsonObject();
        foreach (var (label, path) in folders)
            if (!string.IsNullOrWhiteSpace(path)) map[label] = path;
        if (map.Count == 0) root.Remove("account_folders"); else root["account_folders"] = map;
        Write(root);
    }

    /// <summary>The Sessions Ledger's "stack by project" preference (settings.json
    /// "ledger_group_by_project"), defaulting to TRUE — the project stack is the
    /// new default; the flat per-session view is one click away.</summary>
    public bool LoadLedgerGroupByProject()
    {
        var root = Read();
        try { return root["ledger_group_by_project"]?.GetValue<bool>() ?? true; } catch { return true; }
    }

    public void SaveLedgerGroupByProject(bool groupByProject)
    {
        var root = Read();
        root["ledger_group_by_project"] = groupByProject;
        Write(root);
    }

    // -- Publish usage (settings.json "publish" group) ---------------------------
    //
    // Shape, vocabulary, and the one-time publish_626 -> publish migration live
    // in Core's PublishSettingsJson (unit-tested without a disk). This store is
    // the file wrapper: read the whole object, mutate one group, write it back.

    public PublishSettings LoadPublishSettings()
    {
        var root = Read();
        if (PublishSettingsJson.Migrate(root))
            Write(root);   // one write, then the legacy group is gone
        return PublishSettingsJson.Read(root);
    }

    private void MutatePublish(Action<JsonObject> mutate)
    {
        var root = Read();
        PublishSettingsJson.Migrate(root);   // never write a fresh group beside a stale legacy one
        mutate(root);
        Write(root);
    }

    /// <summary>A global shortcut's stored state (<c>hotkey_settings</c>, a per-action map of
    /// {on, vk, mods} since S6; the flat S1 shape still reads as the Settings shortcut).
    /// Missing or odd reads as off with the default combo.</summary>
    public HotKeySetting LoadHotKeySetting(HotKeyAction action)
        => HotKeys.DecodeFrom(action, Read()["hotkey_settings"]);

    /// <summary>Writes one action's entry; every other entry in the map is kept.</summary>
    public void SaveHotKeySetting(HotKeyAction action, HotKeySetting setting)
    {
        var root = Read();
        root["hotkey_settings"] = HotKeys.EncodeInto(root["hotkey_settings"], action, setting);
        Write(root);
    }

    /// <summary>Runs <see cref="SettingsMigrations.RunAll"/> once at launch, before anything reads
    /// settings; writes only when a migration changed something. A missing file stays missing.</summary>
    public void RunMigrations()
    {
        if (!File.Exists(_path)) return;
        var root = Read();
        if (SettingsMigrations.RunAll(root)) Write(root);
    }

    /// <summary>The newest version whose What's New was shown or skipped; empty reads as null.</summary>
    public string? LoadWhatsNewLastSeen() => ReadString("whats_new_last_seen");

    public void SaveWhatsNewLastSeen(string version) => WriteString("whats_new_last_seen", version);

    /// <summary>The version a fresh install first ran; empty reads as null.</summary>
    public string? LoadWhatsNewInstalledVersion() => ReadString("whats_new_installed_version");

    public void SaveWhatsNewInstalledVersion(string version) => WriteString("whats_new_installed_version", version);

    /// <summary>"Don't show What's New after updates" (default false).</summary>
    public bool LoadWhatsNewHideAfterUpdates()
    {
        try { return Read()["whats_new_hide_after_updates"]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    public void SaveWhatsNewHideAfterUpdates(bool hide)
    {
        var root = Read();
        root["whats_new_hide_after_updates"] = hide;
        Write(root);
    }

    /// <summary>
    /// Where this install stands with the welcome tour, as <c>WelcomeTour.Format</c> writes it
    /// ("pending", "finished", "skipped", "not_offered"); empty reads as null (not yet decided).
    /// </summary>
    public string? LoadTourState() => ReadString(WelcomeTour.StateKey);

    public void SaveTourState(string state) => WriteString(WelcomeTour.StateKey, state);

    private string? ReadString(string key)
    {
        try
        {
            var s = Read()[key]?.GetValue<string>();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
    }

    private void WriteString(string key, string value)
    {
        var root = Read();
        root[key] = value;
        Write(root);
    }

    public void SavePublishEnabled(bool on) => MutatePublish(r => PublishSettingsJson.SetEnabled(r, on));

    public void SavePublishRoots(IReadOnlyDictionary<string, bool> roots)
        => MutatePublish(r => PublishSettingsJson.SetRoots(r, roots));

    public void SavePublishTime(TimeOnly time) => MutatePublish(r => PublishSettingsJson.SetTime(r, time));

    public void SavePublishTokenStored(bool stored)
        => MutatePublish(r => PublishSettingsJson.SetTokenStored(r, stored));

    public void SavePublishEndpointUrl(string url) => MutatePublish(r => PublishSettingsJson.SetEndpointUrl(r, url));

    public void SavePublishAuthScheme(PublishAuthScheme scheme)
        => MutatePublish(r => PublishSettingsJson.SetAuthScheme(r, scheme));

    public void SavePublishAuthHeaderName(string name)
        => MutatePublish(r => PublishSettingsJson.SetAuthHeaderName(r, name));

    public void SavePublishPreset(PublishPreset preset) => MutatePublish(r => PublishSettingsJson.SetPreset(r, preset));

    /// <summary>Record one attempt; see <see cref="PublishSettingsJson.SetAttempt"/>.</summary>
    public void SavePublishAttempt(DateTimeOffset at, string result, DateOnly? okDate)
        => MutatePublish(r => PublishSettingsJson.SetAttempt(r, at, result, okDate));
}
