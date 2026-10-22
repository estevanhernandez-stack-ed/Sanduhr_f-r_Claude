using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// settings.json must never lose a key's value without a migration. The scan reads the App's
/// SettingsStore.cs and Core's PublishSettings.cs for the key literals they use; the frozen list
/// below must stay a subset of what the scan finds. Other readers of settings.json found on this
/// branch: CredentialStore (writes "theme", already listed), sanduhr-mcp's McpConfig/ToolLogic
/// (read "mcp_roots" and the "publish" group, both listed).
/// </summary>
public class SettingsKeysContractTests
{
    // Keys as of 3.4.x plus parity Stages 1, 2, 5 (the Desk) and 6 (Desk content, the glow target). A key may be added; removing or renaming one needs a forward migration in SettingsMigrations with its own test, then move the key here to the migration's list.
    // "publish.*" keys live in the nested "publish" group (PublishSettingsJson).
    // Not frozen: "window" (legacy read fallback for "geom") and "publish_626" (migrated into "publish").
    private static readonly string[] Frozen =
    [
        "alert_muted_tiers",
        "alert_projection",
        "alert_reset",
        "alert_snake_full",
        "alert_sound",
        "alert_sound_scoped",
        "alert_urgent_pct",
        "alert_warn_pct",
        "alerts_enabled",
        "cc_folders_added",
        "claude_code_glow",
        "claude_code_glow.done",
        "claude_code_glow.edge_monitor",
        "claude_code_glow.edge_side",
        "claude_code_glow.skip_terminal",
        "claude_code_glow.target",
        "claude_code_glow.waiting",
        "desk_direct",
        "desk_enabled",
        "desk_font",
        "desk_ink",
        "desk_launcher",
        "desk_layout",
        "desk_margins",
        "desk_message",
        "desk_oled_care",
        "desk_pieces_take_clicks",
        "desk_plate",
        "desk_put_back",
        "desk_sign_in_reminder",
        "desk_time_size",
        "desk_weather",
        "focus_mode_duration",
        "geom",
        "hidden_tiers",
        "hotkey_settings",
        "hotkey_settings.settings",
        "hotkey_settings.settings.mods",
        "hotkey_settings.settings.on",
        "hotkey_settings.settings.vk",
        "just_the_desk",
        "just_the_desk_silenced_reminder",
        "ledger_group_by_project",
        "limit_first_seen",
        "limits_hidden_until_reset",
        "local_cc_show_breakdowns",
        "mcp_cc_home",
        "mcp_enabled",
        "mcp_roots",
        "now_playing",
        "pinned",
        "publish",
        "publish.auth_header_name",
        "publish.auth_scheme",
        "publish.body_format",
        "publish.enabled",
        "publish.endpoint_url",
        "publish.last_attempt_at",
        "publish.last_ok_at",
        "publish.last_ok_date",
        "publish.last_result",
        "publish.preset",
        "publish.roots",
        "publish.time",
        "publish.token_stored",
        "snake_high_score",
        "sparkline_style",
        "statusline_cc_home",
        "statusline_enabled",
        "theme",
        "themes_expanded",
        "themes_studio",
        "tier_order",
        "tour_state",
        "tray_mode",
        "vault_prompted",
        "vault_roots",
        "vault_store_full_paths",
        "watchers_agents",
        "whats_new_hide_after_updates",
        "whats_new_installed_version",
        "whats_new_last_seen",
    ];

    private static string WindowsDotnet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadSrc(params string[] parts) =>
        File.ReadAllText(Path.Combine([WindowsDotnet(), "src", .. parts]));

    private static HashSet<string> ScanKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        // SettingsStore: root["k"], Read()["k"], and the helpers that take a key literal.
        var store = ReadSrc("Sanduhr.App", "Services", "SettingsStore.cs");
        foreach (Match m in Regex.Matches(store, "(?:\\broot|Read\\(\\))\\[\"([a-z_0-9]+)\"\\]"))
            keys.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(store, "(?:LoadStringArray|ReadString|WriteString)\\(\"([a-z_0-9]+)\""))
            keys.Add(m.Groups[1].Value);
        // Keys passed by constant: the tour state.
        if (store.Contains("WelcomeTour.StateKey", StringComparison.Ordinal))
            keys.Add(WelcomeTour.StateKey);
        // "window" is the legacy read fallback, not a key the store owns.
        keys.Remove("window");

        // PublishSettingsJson: the "publish" group and its members (g["k"] and Group(root)["k"];
        // the legacy group's legacy["k"] reads are migration sources and are skipped).
        var publish = ReadSrc("Sanduhr.Core", "PublishSettings.cs");
        foreach (Match m in Regex.Matches(publish, "const string GroupKey = \"([a-z_0-9]+)\""))
            keys.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(publish, "(?:\\bg|Group\\(root\\))\\[\"([a-z_0-9]+)\"\\]"))
            keys.Add("publish." + m.Groups[1].Value);

        // The Claude Code glow group: the switches (SettingsStore, glow["k"] and ReadBool(o, "k")) and
        // the target members (GlowTargetSettings, glow["k"] and Str("k")).
        // The shortcut map (S6): each action's word and the members its entry is written with.
        var hotkeys = ReadSrc("Sanduhr.Core", "HotKeyCombo.cs");
        foreach (Match w in Regex.Matches(hotkeys, @"HotKeyAction\.\w+ => ""([a-z]+)"","))
        {
            keys.Add("hotkey_settings." + w.Groups[1].Value);
            foreach (Match m in Regex.Matches(hotkeys, @"\[""(on|vk|mods)""\] ="))
                keys.Add($"hotkey_settings.{w.Groups[1].Value}.{m.Groups[1].Value}");
        }

        var glowTarget = ReadSrc("Sanduhr.Core", "GlowTargetSettings.cs");
        foreach (var src in new[] { store, glowTarget })
            foreach (Match m in Regex.Matches(src, @"(?:\bglow\[|Str\(|ReadBool\(o, )""([a-z_]+)"""))
                keys.Add("claude_code_glow." + m.Groups[1].Value);
        return keys;
    }

    [Fact]
    public void Frozen_list_is_sorted_and_unique()
    {
        Assert.Equal(Frozen.OrderBy(k => k, StringComparer.Ordinal), Frozen);
        Assert.Equal(Frozen.Length, Frozen.Distinct().Count());
    }

    [Fact]
    public void Every_frozen_key_is_still_read_or_written()
    {
        var found = ScanKeys();
        var missing = Frozen.Where(k => !found.Contains(k)).ToList();
        Assert.True(missing.Count == 0,
            "Keys gone from the code without a migration: " + string.Join(", ", missing));
    }
}
