using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What a mod draws in Claude Code, found by reading its source.</summary>
public enum ModSurface { Band, Pane, Status, Toast, Commands }

/// <summary>What a mod's code can reach beyond drawing, found by reading its source.</summary>
public enum ModCapability { Process, Http, Files }

/// <summary>What the static scan found a plugin draws and uses, in a fixed order.</summary>
public sealed record ModTouches(
    IReadOnlyList<ModSurface> Surfaces,
    IReadOnlyList<ModCapability> Capabilities,
    IReadOnlyList<string> Commands)
{
    public static ModTouches None { get; } = new([], [], []);

    public bool IsEmpty => Surfaces.Count == 0 && Capabilities.Count == 0;
}

/// <summary>Where a mod or plugin loads from.</summary>
public enum ModOriginKind
{
    /// <summary>Listed in the folder's <c>env.CLAUDE_CODE_PLUGIN_DIRS</c> (an <c>@inline</c> plugin).</summary>
    PluginDirs,
    /// <summary>In <c>plugins/installed_plugins.json</c> or <c>enabledPlugins</c> under <c>name@marketplace</c>.</summary>
    Installed,
    /// <summary>A plugin folder under the Claude Code folder's <c>skills/</c>.</summary>
    SkillsDir,
}

public enum ModState { On, Off, Missing }

/// <summary>One mod or plugin a Claude Code folder loads, as the Mods page lists it.</summary>
public sealed record ModItem(
    string Name,
    string? Version,
    string Description,
    string? Path,
    ModOriginKind Origin,
    string? Marketplace,
    string? Key,
    bool IsMod,
    ModState State,
    ModTouches Touches,
    bool IsSanduhr);

public enum SettingsKind { Missing, Readable, Unreadable }

/// <summary>
/// <c>settings.json</c> read or not. Unreadable carries the parse position (1-based line and column) and the
/// parser's message; a file with comments or trailing commas is Unreadable, never empty.
/// </summary>
public sealed record SettingsState(SettingsKind Kind, int Line = 0, int Column = 0, string Message = "")
{
    public static SettingsState Missing { get; } = new(SettingsKind.Missing);
    public static SettingsState Readable { get; } = new(SettingsKind.Readable);
}

/// <summary>The folder's statusline command, masked, and whether it is Sanduhr's.</summary>
public sealed record StatuslineInfo(string Command, bool IsSanduhr);

/// <summary>One hook event: matcher groups configured, and how many of them are Sanduhr's.</summary>
public sealed record HookEventSummary(string Event, int Groups, int Sanduhr);

/// <summary>MCP server names (user scope) and the number of projects with their own servers (local scope, count only).</summary>
public sealed record McpSummary(IReadOnlyList<string> UserServers, int LocalProjectCount);

public sealed record ClaudeMdInfo(bool Exists, int Lines, long Bytes);

/// <summary>
/// <c>cleanupPeriodDays</c>, or null when unset. Warning is true when it is unset in a readable folder:
/// transcripts older than 30 days are deleted at the next start.
/// </summary>
public sealed record RetentionInfo(int? CleanupPeriodDays, bool Warning, string? Message);

/// <summary>Everything the read-only Mods and Config page shows for one Claude Code folder.</summary>
public sealed record FolderReport(
    string Folder,
    bool Exists,
    SettingsState Settings,
    StatuslineInfo? Statusline,
    IReadOnlyList<HookEventSummary> Hooks,
    IReadOnlyDictionary<string, bool> EnabledPlugins,
    IReadOnlyList<ModItem> Mods,
    McpSummary Mcp,
    ClaudeMdInfo ClaudeMd,
    RetentionInfo Retention);

/// <summary>
/// The mods and plugins a Claude Code folder loads, and the rest of its config at a glance, found by reading
/// files only: <c>settings.json</c>, <c>plugins/installed_plugins.json</c>, <c>skills/</c>, each plugin's
/// manifest, <c>hooks.json</c> and source as text, the folder's <c>.claude.json</c> for MCP server names, and
/// <c>CLAUDE.md</c> for its size. No mod code runs, nothing is written, <c>.credentials.json</c> is never
/// opened and nothing under <c>oauthAccount</c> is touched. Every IO failure degrades to a missing value.
/// Port of the Mac's <c>ModInventory.swift</c> and <c>ModStatusEntries.swift</c>.
/// </summary>
public static class ModInventory
{
    public const string SanduhrModName = "sanduhr-meters";
    public const string RetentionWarning = "Transcripts older than 30 days are deleted at the next start";

    /// <summary>Source files read per mod, and the most bytes read from one.</summary>
    public const int FileLimit = 400;
    public const int ByteLimit = 512 * 1024;

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ts", ".tsx", ".js", ".mjs", ".cjs", ".jsx" };

    private static readonly string[] SanduhrSignals = [ClaudeCodeSignals.WaitingFileName, ClaudeCodeSignals.DoneFileName];

    // ------------------------------------------------------------ the folder report

    /// <summary>Reads <paramref name="folder"/> (a Claude Code home). Never throws.</summary>
    public static FolderReport Read(string folder, string profileDir)
    {
        var exists = Safe(() => Directory.Exists(folder), false);
        if (!exists)
            return new FolderReport(folder, false, SettingsState.Missing, null, [], new Dictionary<string, bool>(),
                [], new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(null, false, null));

        var (state, settings) = ReadSettings(folder);
        var enabled = EnabledPlugins(settings);
        var retention = RetentionOf(settings, state);
        return new FolderReport(
            folder, true, state,
            StatuslineOf(settings),
            HooksOf(settings),
            enabled,
            Safe(() => Mods(folder, settings, enabled, profileDir), []),
            Safe(() => McpOf(folder, profileDir), new McpSummary([], 0)),
            ClaudeMdOf(folder),
            retention);
    }

    /// <summary>How many plugins the folder's settings.json switches on. Settings only; 0 when it is missing or unreadable.</summary>
    public static int EnabledPluginCount(string folder)
    {
        var (_, settings) = ReadSettings(folder);
        return EnabledPlugins(settings).Count(p => p.Value);
    }

    private static (SettingsState, JsonElement?) ReadSettings(string folder)
    {
        var path = System.IO.Path.Combine(folder, "settings.json");
        byte[] bytes;
        try
        {
            if (!File.Exists(path)) return (SettingsState.Missing, null);
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            return (new SettingsState(SettingsKind.Unreadable, 0, 0, e.Message), null);
        }
        try
        {
            // Strict on purpose: comments and trailing commas are errors, as for Claude Code.
            using var doc = JsonDocument.Parse(bytes, JsonEdit.StrictOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (new SettingsState(SettingsKind.Unreadable, 1, 1, "The top level is not a JSON object."), null);
            return (SettingsState.Readable, doc.RootElement.Clone());
        }
        catch (JsonException e)
        {
            return (new SettingsState(SettingsKind.Unreadable,
                (int)(e.LineNumber ?? 0) + 1, (int)(e.BytePositionInLine ?? 0) + 1, e.Message), null);
        }
    }

    private static StatuslineInfo? StatuslineOf(JsonElement? settings)
    {
        if (Member(settings, "statusLine") is not { ValueKind: JsonValueKind.Object } sl) return null;
        var command = Member(sl, "command") is { ValueKind: JsonValueKind.String } c ? c.GetString() ?? "" : "";
        return new StatuslineInfo(ConfigMask.MaskString(command), command.Contains("sanduhr-statusline.ps1", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<HookEventSummary> HooksOf(JsonElement? settings)
    {
        var list = new List<HookEventSummary>();
        if (Member(settings, "hooks") is not { ValueKind: JsonValueKind.Object } hooks) return list;
        foreach (var ev in hooks.EnumerateObject())
        {
            if (ev.Value.ValueKind != JsonValueKind.Array) continue;
            int groups = 0, ours = 0;
            foreach (var group in ev.Value.EnumerateArray())
            {
                groups++;
                if (IsSanduhrGroup(group)) ours++;
            }
            list.Add(new HookEventSummary(ev.Name, groups, ours));
        }
        return list;
    }

    private static bool IsSanduhrGroup(JsonElement group)
    {
        if (Member(group, "hooks") is not { ValueKind: JsonValueKind.Array } inner) return false;
        foreach (var h in inner.EnumerateArray())
            if (Member(h, "command") is { ValueKind: JsonValueKind.String } c
                && c.GetString() is { } cmd
                && SanduhrSignals.Any(s => cmd.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private static IReadOnlyDictionary<string, bool> EnabledPlugins(JsonElement? settings)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (Member(settings, "enabledPlugins") is not { ValueKind: JsonValueKind.Object } o) return map;
        foreach (var p in o.EnumerateObject())
            if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                map[p.Name] = p.Value.GetBoolean();
        return map;
    }

    private static RetentionInfo RetentionOf(JsonElement? settings, SettingsState state)
    {
        int? days = null;
        if (Member(settings, "cleanupPeriodDays") is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var d))
            days = d;
        var warn = days is null && state.Kind != SettingsKind.Unreadable;
        return new RetentionInfo(days, warn, warn ? RetentionWarning : null);
    }

    private static McpSummary McpOf(string folder, string profileDir)
    {
        var file = ClaudeCodeHomes.ConfigFile(folder, profileDir);
        if (!File.Exists(file)) return new McpSummary([], 0);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            var names = new List<string>();
            if (Member(root, "mcpServers") is { ValueKind: JsonValueKind.Object } servers)
                names.AddRange(servers.EnumerateObject().Select(p => p.Name));
            names.Sort(StringComparer.Ordinal);
            int local = 0;
            if (Member(root, "projects") is { ValueKind: JsonValueKind.Object } projects)
                foreach (var p in projects.EnumerateObject())
                    if (Member(p.Value, "mcpServers") is { ValueKind: JsonValueKind.Object } s && s.EnumerateObject().Any())
                        local++;
            return new McpSummary(names, local);
        }
        catch
        {
            return new McpSummary([], 0);
        }
    }

    private static ClaudeMdInfo ClaudeMdOf(string folder)
    {
        try
        {
            var path = System.IO.Path.Combine(folder, "CLAUDE.md");
            if (!File.Exists(path)) return new ClaudeMdInfo(false, 0, 0);
            var bytes = File.ReadAllBytes(path);
            int lines = 0;
            foreach (var b in bytes) if (b == (byte)'\n') lines++;
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n') lines++;
            return new ClaudeMdInfo(true, lines, bytes.Length);
        }
        catch
        {
            return new ClaudeMdInfo(false, 0, 0);
        }
    }

    // ------------------------------------------------------------ mods

    private static IReadOnlyList<ModItem> Mods(string folder, JsonElement? settings,
        IReadOnlyDictionary<string, bool> enabled, string profileDir)
    {
        var items = new List<ModItem>();
        void Add(ModItem item)
        {
            if (item.Path is { } p && items.Any(i => PathEq(i.Path, p))) return;
            items.Add(item);
        }

        foreach (var dir in PluginDirs(settings, profileDir))
            Add(ItemAt(dir, ModOriginKind.PluginDirs, null, "inline", enabled, null));

        var records = InstalledRecords(folder);
        var keys = new SortedSet<string>(records.Keys, StringComparer.Ordinal);
        foreach (var k in enabled.Keys.Where(IsMarketplaceKey)) keys.Add(k);
        foreach (var key in keys)
        {
            var at = key.LastIndexOf('@');
            var market = key[(at + 1)..];
            var fallback = key[..at];
            var on = enabled.TryGetValue(key, out var v) && v;
            if (!records.TryGetValue(key, out var record))
            {
                // Turned on or off here, but no files for it in this folder (synced or gone).
                Add(new ModItem(fallback, null, "", null, ModOriginKind.Installed, market, key, false,
                    on ? ModState.On : ModState.Off, ModTouches.None, false));
                continue;
            }
            var found = ItemAt(record.Path, ModOriginKind.Installed, market, null, enabled, fallback);
            found = found with { Key = key, Version = found.Version ?? record.Version };
            if (found.State != ModState.Missing) found = found with { State = on ? ModState.On : ModState.Off };
            Add(found);
        }

        var skills = System.IO.Path.Combine(folder, "skills");
        foreach (var dir in Subfolders(skills))
            if (File.Exists(System.IO.Path.Combine(dir, ".claude-plugin", "plugin.json")))
                Add(ItemAt(dir, ModOriginKind.SkillsDir, null, "skills-dir", enabled, null));
        return items;
    }

    /// <summary>The folders <c>env.CLAUDE_CODE_PLUGIN_DIRS</c> lists, in order, <c>~/</c> expanded. Split on ';'.</summary>
    public static IReadOnlyList<string> PluginDirs(JsonElement? settings, string home)
    {
        var list = new List<string>();
        if (Member(Member(settings, "env"), "CLAUDE_CODE_PLUGIN_DIRS") is not { ValueKind: JsonValueKind.String } s) return list;
        foreach (var raw in (s.GetString() ?? "").Split(';'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
                p = System.IO.Path.Combine(home, p[2..]);
            list.Add(p);
        }
        return list;
    }

    private sealed record InstalledRecord(string Path, string? Version);

    private static Dictionary<string, InstalledRecord> InstalledRecords(string folder)
    {
        var map = new Dictionary<string, InstalledRecord>(StringComparer.Ordinal);
        try
        {
            var file = System.IO.Path.Combine(folder, "plugins", "installed_plugins.json");
            if (!File.Exists(file)) return map;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            if (Member(doc.RootElement, "plugins") is not { ValueKind: JsonValueKind.Object } plugins) return map;
            foreach (var p in plugins.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() == 0) continue;
                var first = p.Value[0];
                if (Member(first, "installPath") is { ValueKind: JsonValueKind.String } ip && ip.GetString() is { Length: > 0 } path)
                    map[p.Name] = new InstalledRecord(path,
                        Member(first, "version") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null);
            }
        }
        catch
        {
            // Unreadable list: no installed plugins.
        }
        return map;
    }

    /// <summary>A marketplace plugin's key: <c>name@market</c>, not the <c>@inline</c> or <c>@skills-dir</c> switches.</summary>
    public static bool IsMarketplaceKey(string key)
    {
        var at = key.LastIndexOf('@');
        if (at <= 0) return false;
        var market = key[(at + 1)..];
        return market.Length > 0 && market != "inline" && market != "skills-dir";
    }

    private static ModItem ItemAt(string path, ModOriginKind origin, string? marketplace, string? keySuffix,
        IReadOnlyDictionary<string, bool> enabled, string? fallbackName)
    {
        var exists = Safe(() => Directory.Exists(path), false);
        var manifest = exists ? Manifest(path) : null;
        var name = Member(manifest, "name") is { ValueKind: JsonValueKind.String } n && !string.IsNullOrEmpty(n.GetString())
            ? n.GetString()!
            : fallbackName ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        var key = keySuffix is null ? null : $"{name}@{keySuffix}";
        var sanduhr = name == SanduhrModName;
        if (!exists)
            return new ModItem(name, null, "", path, origin, marketplace, key, false, ModState.Missing, ModTouches.None, sanduhr);

        var isMod = IsMod(path);
        var state = key is not null && enabled.TryGetValue(key, out var on) && !on ? ModState.Off : ModState.On;
        return new ModItem(
            name,
            Member(manifest, "version") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null,
            Member(manifest, "description") is { ValueKind: JsonValueKind.String } d ? d.GetString() ?? "" : "",
            path, origin, marketplace, key, isMod, state, Touches(path, isMod, manifest), sanduhr);
    }

    private static JsonElement? Manifest(string dir)
    {
        try
        {
            var file = System.IO.Path.Combine(dir, ".claude-plugin", "plugin.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A plugin folder that is a mod: its <c>hooks/hooks.json</c> lists <c>modules</c>.</summary>
    public static bool IsMod(string dir)
    {
        try
        {
            var file = System.IO.Path.Combine(dir, "hooks", "hooks.json");
            if (!File.Exists(file)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("modules", out _);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> Subfolders(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateDirectories(dir)
                .Where(p => !System.IO.Path.GetFileName(p).StartsWith('.'))
                .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ------------------------------------------------------------ the static scan

    /// <summary>
    /// What the plugin at <paramref name="dir"/> draws and uses. A mod's source is read as text; any plugin's
    /// <c>commands/*.md</c> are slash commands, its settings-style hooks run programs or call URLs, and an MCP
    /// server (<c>.mcp.json</c>, manifest <c>mcpServers</c>) is a program. Nothing runs.
    /// </summary>
    public static ModTouches Touches(string dir, bool isMod, JsonElement? manifest = null)
    {
        var surfaces = new HashSet<ModSurface>();
        var caps = new HashSet<ModCapability>();
        var commands = new HashSet<string>(CommandFiles(dir), StringComparer.Ordinal);
        try
        {
            if (isMod)
                ForEachSource(dir, text =>
                {
                    surfaces.UnionWith(SurfacesIn(text));
                    caps.UnionWith(CapabilitiesIn(text));
                    commands.UnionWith(CommandNamesIn(text));
                    return false;
                });
            var hooks = System.IO.Path.Combine(dir, "hooks", "hooks.json");
            if (File.Exists(hooks))
            {
                var text = File.ReadAllText(hooks);
                if (Regex.IsMatch(text, @"""type""\s*:\s*""command""")) caps.Add(ModCapability.Process);
                if (Regex.IsMatch(text, @"""type""\s*:\s*""http""")) caps.Add(ModCapability.Http);
            }
            manifest ??= Manifest(dir);
            if (File.Exists(System.IO.Path.Combine(dir, ".mcp.json")) || Member(manifest, "mcpServers") is not null)
                caps.Add(ModCapability.Process);
        }
        catch
        {
            // A scan that fails part way reports what it found.
        }
        if (commands.Count > 0) surfaces.Add(ModSurface.Commands);
        return new ModTouches(
            Enum.GetValues<ModSurface>().Where(surfaces.Contains).ToList(),
            Enum.GetValues<ModCapability>().Where(caps.Contains).ToList(),
            commands.OrderBy(c => c, StringComparer.Ordinal).ToList());
    }

    /// <summary>The surfaces a mod's source text draws.</summary>
    public static ISet<ModSurface> SurfacesIn(string text)
    {
        var o = new HashSet<ModSurface>();
        if (text.Contains("AbovePrompt", StringComparison.Ordinal)) o.Add(ModSurface.Band);
        if (Regex.IsMatch(text, """component\s*:\s*['"`]Pane['"`]""") || text.Contains("ui.open", StringComparison.Ordinal))
            o.Add(ModSurface.Pane);
        if (text.Contains("ui.status", StringComparison.Ordinal)) o.Add(ModSurface.Status);
        if (text.Contains("ui.toast", StringComparison.Ordinal)) o.Add(ModSurface.Toast);
        if (CommandNamesIn(text).Count > 0) o.Add(ModSurface.Commands);
        return o;
    }

    /// <summary>The capabilities a mod's source text uses through <c>$</c>.</summary>
    public static ISet<ModCapability> CapabilitiesIn(string text)
    {
        var o = new HashSet<ModCapability>();
        if (Regex.IsMatch(text, @"\bprocess\.(run|spawn)\b")) o.Add(ModCapability.Process);
        if (Regex.IsMatch(text, @"\bhttp\.fetch\b")) o.Add(ModCapability.Http);
        if (Regex.IsMatch(text, @"\bfs\.(read|write|list|stat|exists|remove|rename|mkdir|watch)\b")) o.Add(ModCapability.Files);
        return o;
    }

    private static readonly Regex[] CommandPatterns =
    [
        new("""command\.run['"`]\s*,\s*\{[^}]*?command\s*:\s*['"`]([^'"`]+)['"`]""", RegexOptions.CultureInvariant),
        new("""command\.register\(\s*\{[^}]*?name\s*:\s*['"`]([^'"`]+)['"`]""", RegexOptions.CultureInvariant),
    ];

    /// <summary>The slash commands a mod's source serves: <c>on('command.run', { command: 'x' }, ...)</c> and <c>$.command.register({ name: 'x' })</c>.</summary>
    public static ISet<string> CommandNamesIn(string text)
    {
        var o = new HashSet<string>(StringComparer.Ordinal);
        foreach (var re in CommandPatterns)
            foreach (Match m in re.Matches(text))
                if (m.Groups.Count > 1) o.Add(m.Groups[1].Value);
        return o;
    }

    private static IEnumerable<string> CommandFiles(string dir)
    {
        try
        {
            var d = System.IO.Path.Combine(dir, "commands");
            if (!Directory.Exists(d)) return [];
            return Directory.EnumerateFiles(d, "*.md")
                .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Calls <paramref name="body"/> with each of the mod's source files as text until it returns true. Never
    /// node_modules, hidden entries (the engine's <c>.claude-plugin/types</c>), declaration files (<c>.d.ts</c>)
    /// or tests (<c>*.test.*</c>, <c>*.spec.*</c>). At most <see cref="FileLimit"/> files of
    /// <see cref="ByteLimit"/> bytes each.
    /// </summary>
    private static void ForEachSource(string dir, Func<string, bool> body)
    {
        int read = 0;
        bool Walk(string d)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(d).OrderBy(e => e, StringComparer.Ordinal).ToList(); }
            catch { return false; }
            foreach (var entry in entries)
            {
                var name = System.IO.Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules") continue;
                try
                {
                    var attrs = File.GetAttributes(entry);
                    if (attrs.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (attrs.HasFlag(FileAttributes.Directory))
                    {
                        if (Walk(entry)) return true;
                        if (read > FileLimit) return true;
                        continue;
                    }
                    var lower = name.ToLowerInvariant();
                    if (!SourceExtensions.Contains(System.IO.Path.GetExtension(lower)) || lower.EndsWith(".d.ts", StringComparison.Ordinal)
                        || lower.Contains(".test.", StringComparison.Ordinal) || lower.Contains(".spec.", StringComparison.Ordinal))
                        continue;
                    if (new FileInfo(entry).Length > ByteLimit) continue;
                    read++;
                    if (read > FileLimit) return true;
                    if (body(File.ReadAllText(entry, Encoding.UTF8))) return true;
                }
                catch
                {
                    // One unreadable file does not stop the scan.
                }
            }
            return false;
        }
        Walk(dir);
    }

    // ------------------------------------------------------------ helpers

    private static JsonElement? Member(JsonElement? e, string name)
        => e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) ? v : null;

    private static bool PathEq(string? a, string? b)
        => a is not null && b is not null
           && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch { return fallback; }
    }
}
