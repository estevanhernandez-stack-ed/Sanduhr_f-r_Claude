using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What a Claude Code folder has for one Sanduhr piece.</summary>
public enum IntegrationStatus
{
    NotInstalled,
    Installed,
    /// <summary>Sanduhr's entry is there but names another version (or its files are gone).</summary>
    Outdated,
    /// <summary>settings.json is not a JSON object, or the member Sanduhr edits is not what it should be.</summary>
    Unreadable,
}

/// <summary>Why an operation did not complete; <see cref="IntegrationFailure.None"/> on success.</summary>
public enum IntegrationFailure
{
    None,
    /// <summary>settings.json is not a JSON object (or a member Sanduhr edits is the wrong shape): nothing was written.</summary>
    Malformed,
    /// <summary>The mod's files are not inside this copy of the app, or could not be copied.</summary>
    ModFilesMissing,
    /// <summary>A read or write failed, or the file kept changing while Sanduhr tried.</summary>
    WriteFailed,
    /// <summary>A receipt that names this folder could not be read and was set aside: nothing was changed.</summary>
    ReceiptSetAside,
}

/// <summary>An expected outcome, success or not. Expected failures are values, never exceptions.</summary>
public sealed record IntegrationResult(bool Ok, string Message, IntegrationFailure Failure = IntegrationFailure.None)
{
    public static IntegrationResult Success(string message = "") => new(true, message);
    public static IntegrationResult Fail(IntegrationFailure failure, string message) => new(false, message, failure);
}

/// <summary>What an update of the mod's entry found.</summary>
public enum ModUpdateOutcome
{
    /// <summary>The entry already names the app's version, or there is no pinned entry.</summary>
    UpToDate,
    /// <summary>The entry now names the app's version.</summary>
    Updated,
}

/// <summary>What re-pointing one pre-3.5.0 registration did.</summary>
public enum RepointOutcome
{
    /// <summary>No registration of Sanduhr's names the old location (or it is no longer Sanduhr's).</summary>
    NothingToDo,
    Repointed,
    /// <summary>The registration is Sanduhr's and old, and could not be moved; a later run retries.</summary>
    Failed,
}

public sealed record ModUpdateResult(bool Ok, string Message, ModUpdateOutcome Outcome, IntegrationFailure Failure = IntegrationFailure.None);

/// <summary>Where Sanduhr's own mod stands in one Claude Code folder, read from its settings.json and Sanduhr's receipt.</summary>
public sealed record OwnModState(
    string Folder,
    bool Readable,
    string? Entry,
    string? Pinned,
    bool? Inline,
    bool? Switched,
    bool HasReceipt)
{
    public bool Listed => Entry is not null;
    /// <summary>Loads in new sessions, as far as this settings.json says.</summary>
    public bool IsOn => Listed && Inline != false;
}

/// <summary>One receipt's undo, for Reset's report.</summary>
public sealed record UndoReport(IntegrationKind Kind, string Folder, bool Ok, string Message);

/// <summary>
/// The meters mod and the glow hooks in a Claude Code folder's <c>settings.json</c>, installed and
/// undone through receipts. Port of the Mac's <c>IntegrationInstaller</c> (meters and hooks kinds) and
/// <c>ModSwitch</c>. The file is Claude Code's: every edit is spliced into the text with
/// <see cref="JsonEdit"/>, checked against the parsed original, and written through
/// <see cref="ConfigCommit"/> (compare and swap, backup of the first change). Remove returns the file
/// byte for byte when nobody else changed it, and takes out only what still points at Sanduhr's
/// files when somebody did. Logs carry the operation and the exception type, never a path.
/// </summary>
public sealed class ClaudeCodeIntegrations
{
    public const string ModName = "sanduhr-meters";
    public const string EnvKey = "env";
    public const string PluginDirsKey = "CLAUDE_CODE_PLUGIN_DIRS";
    /// <summary>Claude Code splits the plugin folders on the platform's path-list separator: ';' on Windows.</summary>
    public const char PluginDirsSeparator = ';';
    public const string EnabledPluginsKey = "enabledPlugins";
    public const string OwnModKey = ModName + "@inline";
    public const string HooksKey = "hooks";
    public const string WaitingMatcher = "permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input";
    public const int HookTimeoutSeconds = 5;
    public const string WaitingSignal = "glow-waiting.signal";
    public const string DoneSignal = "glow-done.signal";

    private static readonly (string Name, string Signal)[] HookEvents = [("Notification", WaitingSignal), ("Stop", DoneSignal)];
    private static readonly Regex StampName = new(@"^v[0-9A-Za-z.+\-]+-\d{14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _profileDir;
    private readonly string _sanduhrDir;
    private readonly string _externalDir;
    private readonly ConfigCommit _commit;
    private readonly ReceiptStore _receipts;
    private readonly Action<string>? _log;

    /// <summary>Test seam: runs after an edit is planned and before it is committed, with the settings path.</summary>
    internal Action<string>? BeforeCommit { get; set; }

    /// <summary>The mod's source folder (the app passes <c>AppContext.BaseDirectory\mods\sanduhr-meters</c>).</summary>
    public string? ModSourceDir { get; set; }

    /// <param name="profileDir">The user-profile directory Claude Code folders are discovered under.</param>
    /// <param name="sanduhrDir"><c>%APPDATA%\Sanduhr</c> in app code: backups and receipts live here.</param>
    /// <param name="modSourceDir">The mod's source folder; may be set later.</param>
    /// <param name="log">Operation and exception type only.</param>
    /// <param name="externalDir"><c>%USERPROFILE%\.sanduhr</c> in app code: the mod's copies (<c>mods\</c>) live here, where Claude Code can read them. Defaults to <paramref name="sanduhrDir"/>.</param>
    public ClaudeCodeIntegrations(string profileDir, string sanduhrDir, string? modSourceDir = null, Action<string>? log = null, string? externalDir = null)
    {
        _profileDir = profileDir;
        _sanduhrDir = sanduhrDir;
        _externalDir = externalDir ?? sanduhrDir;
        ModSourceDir = modSourceDir;
        _commit = new ConfigCommit(Path.Combine(sanduhrDir, "backups"));
        _receipts = new ReceiptStore(Path.Combine(sanduhrDir, "integrations", "installs.json"));
        _log = log;
    }

    public string ModsRoot => Path.Combine(_externalDir, "mods");

    /// <summary>
    /// Older mods roots whose entries are still Sanduhr's (the pre-3.5.0 <c>%APPDATA%\Sanduhr\mods</c>). An entry under one
    /// reads as Sanduhr's, Outdated, and is replaced in place by the next install. Nothing is ever copied there.
    /// </summary>
    public IReadOnlyList<string> LegacyModsRoots { get; set; } = [];
    public string ReceiptsPath => _receipts.Path;

    // ------------------------------------------------------------------ paths and entries

    public static string Normalize(string folder)
    {
        var full = Path.GetFullPath(folder);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd('\\', '/') : full;
    }

    /// <summary>The settings file for a Claude Code folder.</summary>
    public static string SettingsFile(string folder) => Path.Combine(Normalize(folder), "settings.json");

    private static bool SamePath(string a, string b)
        => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public static bool IsStampName(string name) => StampName.IsMatch(name);

    public string ModPathFor(string stamp) => Path.Combine(ModsRoot, stamp, ModName);

    /// <summary>One entry of the plugin folders list that is Sanduhr's mod: <c>&lt;externalDir&gt;\mods\&lt;stamp&gt;\sanduhr-meters</c>, or the same shape under a legacy root.</summary>
    public bool IsOurModEntry(string entry) => UnderRoot(entry, ModsRoot) || LegacyModsRoots.Any(r => UnderRoot(entry, r));

    /// <summary>An entry under a legacy mods root (and so not under the current one).</summary>
    public bool IsLegacyModEntry(string entry) => !UnderRoot(entry, ModsRoot) && LegacyModsRoots.Any(r => UnderRoot(entry, r));

    private static bool UnderRoot(string entry, string modsRoot)
    {
        var e = entry.Trim().Replace('/', '\\').TrimEnd('\\');
        var root = modsRoot.Replace('/', '\\').TrimEnd('\\') + "\\";
        if (!e.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = e[root.Length..].Split('\\');
        return rest.Length == 2 && IsStampName(rest[0]) && string.Equals(rest[1], ModName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The stamp a plugin folders entry pins; null when the entry is not Sanduhr's.</summary>
    public string? PinnedStampOf(string entry)
        => IsOurModEntry(entry) ? Path.GetFileName(Path.GetDirectoryName(entry.Trim().Replace('/', '\\').TrimEnd('\\'))) : null;

    /// <summary>The plugin folders list with Sanduhr's mod in it: an older entry of Sanduhr's is replaced where it stands (later ones dropped), else the mod goes last.</summary>
    public string AddingPluginDir(string ours, string value)
    {
        var parts = value.Split(PluginDirsSeparator);
        if (parts.Any(IsOurModEntry))
        {
            var outp = new List<string>();
            bool placed = false;
            foreach (var p in parts)
            {
                if (!IsOurModEntry(p)) outp.Add(p);
                else if (!placed) { outp.Add(ours); placed = true; }
            }
            return string.Join(PluginDirsSeparator, outp);
        }
        if (string.IsNullOrWhiteSpace(value)) return ours;
        return value + PluginDirsSeparator + ours;
    }

    /// <summary>The list without Sanduhr's mod, or null when it holds none.</summary>
    public string? RemovingPluginDir(string value)
    {
        var parts = value.Split(PluginDirsSeparator);
        if (!parts.Any(IsOurModEntry)) return null;
        return string.Join(PluginDirsSeparator, parts.Where(p => !IsOurModEntry(p)));
    }

    // ------------------------------------------------------------------ the mod's files

    private static bool IsShippedModFile(string relative)
    {
        var r = relative.Replace('\\', '/');
        var name = r[(r.LastIndexOf('/') + 1)..];
        if (r.StartsWith(".claude-plugin/types/", StringComparison.Ordinal) || r.Contains("/node_modules/", StringComparison.Ordinal)
            || r.StartsWith("node_modules/", StringComparison.Ordinal)) return false;
        if (name == ".gitignore" || name == ".DS_Store") return false;
        return !(name.EndsWith(".test.ts", StringComparison.Ordinal) || name.EndsWith(".test.tsx", StringComparison.Ordinal));
    }

    private List<string>? ShippedModFiles()
    {
        var src = ModSourceDir;
        if (string.IsNullOrEmpty(src) || !File.Exists(Path.Combine(src, ".claude-plugin", "plugin.json"))) return null;
        return Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => IsShippedModFile(Path.GetRelativePath(src, f))).ToList();
    }

    /// <summary>The stamp the source would copy to, or null when the source is missing or unreadable. Copies nothing.</summary>
    public string? CurrentModStamp()
    {
        try
        {
            return Transient<string?>(() =>
            {
            var files = ShippedModFiles();
            if (files is null || files.Count == 0) return null;
            string version;
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ModSourceDir!, ".claude-plugin", "plugin.json"))))
                version = doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "0";
            var newest = files.Max(f => File.GetLastWriteTimeUtc(f));
            var stamp = $"v{version}-{newest.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            return IsStampName(stamp) ? stamp : null;
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _log?.Invoke($"mod stamp failed ({e.GetType().Name})");
            return null;
        }
    }

    /// <summary>
    /// Copies the mod into <c>mods\&lt;stamp&gt;\sanduhr-meters</c> (built beside and moved into place whole); does nothing when that
    /// stamp is there. Returns the stamped mod folder, or null when the source is missing or the copy failed.
    /// </summary>
    public string? RefreshModFiles()
    {
        try
        {
            return Transient(RefreshCore);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"mod files refresh failed ({e.GetType().Name})");
            return null;
        }
    }

    private string? RefreshCore()
    {
        {
            var stamp = CurrentModStamp();
            if (stamp is null) return null;
            var target = ModPathFor(stamp);
            if (File.Exists(Path.Combine(target, ".claude-plugin", "plugin.json"))) return target;
            var files = ShippedModFiles()!;
            Directory.CreateDirectory(ModsRoot);
            var staging = Path.Combine(ModsRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var f in files)
                {
                    var dest = Path.Combine(staging, ModName, Path.GetRelativePath(ModSourceDir!, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest, overwrite: true);
                }
                var stampDir = Path.Combine(ModsRoot, stamp);
                if (Directory.Exists(stampDir)) Directory.Delete(stampDir, recursive: true); // a half-made folder from before
                Directory.Move(staging, stampDir);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return target;
        }
    }

    /// <summary>
    /// Deletes stamp folders (and leftovers of an interrupted refresh) that no receipt pins and no known settings.json lists.
    /// Known settings files: the receipts' files, the discovered folders' settings.json and <paramref name="settingsFiles"/>.
    /// A receipt that could not be read stops the prune: it may pin a stamp. A locked folder is skipped. Returns the number removed.
    /// </summary>
    public int PruneModFiles(IEnumerable<string>? settingsFiles = null)
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(ModsRoot)) return 0;
            var ledger = LoadReceipts();
            var asides = Directory.Exists(Path.GetDirectoryName(_receipts.Path)!)
                ? Directory.EnumerateFiles(Path.GetDirectoryName(_receipts.Path)!, Path.GetFileName(_receipts.Path) + "*" + ReceiptLog.AsideSuffix).Any()
                : false;
            var stamps = Directory.GetDirectories(ModsRoot).Select(d => Path.GetFileName(d)!).ToList();
            foreach (var leftover in stamps.Where(s => s.StartsWith(".staging-", StringComparison.Ordinal)))
                removed += TryDelete(Path.Combine(ModsRoot, leftover)) ? 1 : 0;
            if (ledger.Skipped > 0 || asides) return removed;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in ledger.Records) if (r.Pinned is { } p) keep.Add(p);
            var files = new List<string>(ledger.Records.Select(r => r.File));
            try
            {
                files.AddRange(ClaudeCodeHomes.Discover(_profileDir, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))
                    .Select(h => Path.Combine(Path.IsPathRooted(h) ? h : Path.Combine(_profileDir, h), "settings.json")));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"mod prune discovery failed ({e.GetType().Name})");
                return removed; // can't see every settings file: keep everything
            }
            if (settingsFiles is not null) files.AddRange(settingsFiles);
            var texts = new List<string>();
            foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { if (File.Exists(f)) texts.Add(File.ReadAllText(f)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _log?.Invoke($"mod prune read failed ({e.GetType().Name})");
                    return removed; // a settings file we couldn't read may name a stamp
                }
            }
            foreach (var s in stamps.Where(IsStampName))
            {
                if (keep.Contains(s) || texts.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                if (TryDelete(Path.Combine(ModsRoot, s))) removed++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"mod prune failed ({e.GetType().Name})");
        }
        return removed;
    }

    private static bool TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // ------------------------------------------------------------------ hooks: the entries

    /// <summary>The command a glow hook runs: touches <c>%USERPROFILE%\.sanduhr\&lt;signal&gt;</c> when that folder exists.</summary>
    public static string HookCommand(string signal)
        => "$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d '" + signal + "'), '') }";

    /// <summary>The command pre-3.5.0 builds wrote (touches <c>%APPDATA%\Sanduhr\&lt;signal&gt;</c>). Still recognised as Sanduhr's: Status reads it Outdated, Install replaces it, Remove takes it out.</summary>
    public static string LegacyHookCommand(string signal)
        => "$d = Join-Path $env:APPDATA 'Sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d '" + signal + "'), '') }";

    private static bool IsLegacyHookGroup(JsonNode? group)
    {
        if (!IsOurHookGroup(group)) return false;
        var o = (JsonObject)group!;
        var cmd = Str(((JsonObject)((JsonArray)o["hooks"]!)[0]!)["command"]);
        return HookEvents.Any(e => string.Equals(cmd, LegacyHookCommand(e.Signal), StringComparison.Ordinal));
    }

    // The per-hook "shell" ("bash" or "powershell") and "async" (true runs the hook in the background)
    // fields and the timeout in seconds are from the Claude Code hooks docs:
    // https://code.claude.com/docs/en/hooks . Not yet exercised against a live Claude Code session.
    /// <summary>Sanduhr's group for one event: a matcher group (Notification only) with one async PowerShell command hook.</summary>
    public static JsonEdit.Value HookGroup(string eventName)
    {
        var signal = HookEvents.First(e => e.Name == eventName).Signal;
        var hook = JsonEdit.Value.Obj(
            new JsonEdit.Pair("type", JsonEdit.Value.Str("command")),
            new JsonEdit.Pair("shell", JsonEdit.Value.Str("powershell")),
            new JsonEdit.Pair("command", JsonEdit.Value.Str(HookCommand(signal))),
            new JsonEdit.Pair("async", JsonEdit.Value.Bool(true)),
            new JsonEdit.Pair("timeout", JsonEdit.Value.Int(HookTimeoutSeconds)));
        var pairs = new List<JsonEdit.Pair>();
        if (eventName == "Notification") pairs.Add(new JsonEdit.Pair("matcher", JsonEdit.Value.Str(WaitingMatcher)));
        pairs.Add(new JsonEdit.Pair("hooks", JsonEdit.Value.Arr(hook)));
        return JsonEdit.Value.Obj([.. pairs]);
    }

    private static JsonEdit.Value HooksValue()
        => JsonEdit.Value.Obj([.. HookEvents.Select(e => new JsonEdit.Pair(e.Name, JsonEdit.Value.Arr(HookGroup(e.Name))))]);

    public static bool IsOurHookCommand(JsonNode? hook)
    {
        if (hook is not JsonObject o || !(Str(o["type"]) == "command") || Str(o["command"]) is not { } cmd) return false;
        return (cmd.Contains(WaitingSignal, StringComparison.Ordinal) || cmd.Contains(DoneSignal, StringComparison.Ordinal))
            && cmd.Contains("Sanduhr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A group is Sanduhr's only when it matches Sanduhr's exact shape: the Notification matcher (none for
    /// Stop) and exactly one hook, type command, shell powershell, whose command equals
    /// <see cref="HookCommand"/> for that event's signal byte for byte. A user's near-copy (an extra hook,
    /// a different matcher or command) is theirs and is never claimed or removed. async and timeout are
    /// not compared, so an older Sanduhr group still reads as ours (Outdated, not foreign).
    /// </summary>
    public static bool IsOurHookGroup(JsonNode? group)
    {
        if (group is not JsonObject o || o["hooks"] is not JsonArray hs || hs.Count != 1 || hs[0] is not JsonObject hook) return false;
        if (Str(hook["type"]) != "command" || Str(hook["shell"]) != "powershell" || Str(hook["command"]) is not { } cmd) return false;
        foreach (var (name, signal) in HookEvents)
        {
            if (!string.Equals(cmd, HookCommand(signal), StringComparison.Ordinal)
                && !string.Equals(cmd, LegacyHookCommand(signal), StringComparison.Ordinal)) continue;
            var matcher = o["matcher"];
            return name == "Notification" ? Str(matcher) == WaitingMatcher : matcher is null;
        }
        return false;
    }

    // ------------------------------------------------------------------ status

    /// <summary>What <paramref name="folder"/> has for <paramref name="kind"/> (<see cref="IntegrationKind.Meters"/> or <see cref="IntegrationKind.Hooks"/>).</summary>
    public IntegrationStatus Status(IntegrationKind kind, string folder)
    {
        RequireKind(kind);
        var file = SettingsFile(folder);
        byte[]? data;
        try { data = ReadFile(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return IntegrationStatus.Unreadable; }
        if (data is null) return IntegrationStatus.NotInstalled;
        JsonObject root;
        try { root = ParseObject(data); }
        catch (IntegrationException) { return IntegrationStatus.Unreadable; }
        return kind == IntegrationKind.Meters ? MetersStatus(root) : HooksStatus(root);
    }

    private IntegrationStatus MetersStatus(JsonObject root)
    {
        if (root[EnvKey] is null) return IntegrationStatus.NotInstalled;
        if (root[EnvKey] is not JsonObject env) return IntegrationStatus.Unreadable;
        if (env[PluginDirsKey] is null) return IntegrationStatus.NotInstalled;
        if (Str(env[PluginDirsKey]) is not { } list) return IntegrationStatus.Unreadable;
        var ours = list.Split(PluginDirsSeparator).Where(IsOurModEntry).Select(p => p.Trim()).ToList();
        if (ours.Count == 0) return IntegrationStatus.NotInstalled;
        if (ours.Count != 1 || !Directory.Exists(ours[0]) || IsLegacyModEntry(ours[0])) return IntegrationStatus.Outdated;
        var stamp = CurrentModStamp();
        return stamp is null || SamePath(ours[0], ModPathFor(stamp)) ? IntegrationStatus.Installed : IntegrationStatus.Outdated;
    }

    private static IntegrationStatus HooksStatus(JsonObject root)
    {
        if (root[HooksKey] is null) return IntegrationStatus.NotInstalled;
        if (root[HooksKey] is not JsonObject hooks) return IntegrationStatus.Unreadable;
        bool ours = false, current = true;
        foreach (var (name, _) in HookEvents)
        {
            if (hooks[name] is null) { current = false; continue; }
            if (hooks[name] is not JsonArray list) return IntegrationStatus.Unreadable;
            var mine = list.Where(IsOurHookGroup).ToList();
            if (mine.Count > 0) ours = true;
            if (mine.Count != 1 || !JsonNode.DeepEquals(mine[0], ToNode(HookGroup(name)))) current = false;
        }
        if (!ours) return IntegrationStatus.NotInstalled;
        return current ? IntegrationStatus.Installed : IntegrationStatus.Outdated;
    }

    /// <summary>Where Sanduhr's own mod stands in <paramref name="folder"/>.</summary>
    public OwnModState OwnModStateOf(string folder)
    {
        var norm = Normalize(folder);
        var file = SettingsFile(folder);
        bool hasReceipt = false;
        bool? switched = null;
        var rec = LoadReceipts().Records.FirstOrDefault(r => r.Kind == IntegrationKind.Meters && SamePath(r.File, file));
        if (rec is not null) { hasReceipt = true; switched = rec.Switched?.Value; }
        byte[]? data;
        try { data = ReadFile(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new OwnModState(norm, false, null, null, null, switched, hasReceipt);
        }
        if (data is null) return new OwnModState(norm, true, null, null, null, switched, hasReceipt);
        JsonObject root;
        try { root = ParseObject(data); }
        catch (IntegrationException) { return new OwnModState(norm, false, null, null, null, switched, hasReceipt); }
        string? entry = null;
        if (root[EnvKey] is JsonObject env && Str(env[PluginDirsKey]) is { } list)
            entry = list.Split(PluginDirsSeparator).Select(p => p.Trim()).FirstOrDefault(IsOurModEntry);
        bool? inline = null;
        if (root[EnabledPluginsKey] is JsonObject enabled) inline = BoolOf(enabled[OwnModKey]);
        return new OwnModState(norm, true, entry, entry is null ? null : PinnedStampOf(entry), inline, switched, hasReceipt);
    }

    // ------------------------------------------------------------------ install

    /// <summary>Installs the meters mod (copy + plugin folders entry) or the glow hooks into <paramref name="folder"/>.</summary>
    public IntegrationResult Install(IntegrationKind kind, string folder)
    {
        RequireKind(kind);
        return Run("install", () =>
        {
            string? stamp = null;
            if (kind == IntegrationKind.Meters)
            {
                if (RefreshModFiles() is null || CurrentModStamp() is not { } s) throw new IntegrationException(IntegrationFailure.ModFilesMissing, "The meters mod's files are not in this copy of Sanduhr.");
                stamp = s;
            }
            InstallEntry(kind, folder, stamp);
            return "";
        });
    }

    private void InstallEntry(IntegrationKind kind, string folder, string? stamp)
    {
        var file = SettingsFile(folder);
        var ledger = LoadReceipts();
        var receipts = ledger.Records.ToList();
        var ours = stamp is null ? "" : ModPathFor(stamp);
        IntegrationReceipt? made = null;
        var outcome = CommitLoop(file, original =>
        {
            var prior = receipts.FirstOrDefault(r => r.Kind == kind && SamePath(r.File, file));
            var b = original ?? "{}\n"u8.ToArray();
            var plan = Guarded(file, () => kind == IntegrationKind.Meters
                ? PlanInstallMeters(b, ours, stamp!, prior, original is null)
                : PlanInstallHooks(b, prior, original is null));
            made = plan.Receipt with { Folder = Normalize(folder), File = file };
            BeforeCommit?.Invoke(file);
            if (original is not null && plan.Bytes.AsSpan().SequenceEqual(original)) return null; // already as wanted
            return new Edit(plan.Bytes);
        }, backup: true);
        if (outcome == CommitOutcome.Conflict) throw new IntegrationException(IntegrationFailure.WriteFailed, $"{file} kept changing; nothing was written.");
        receipts.RemoveAll(r => r.Kind == kind && SamePath(r.File, file));
        receipts.Add(made!);
        SaveReceipts(receipts);
    }

    private sealed record Plan(byte[] Bytes, IntegrationReceipt Receipt);

    private Plan PlanInstallMeters(byte[] b, string ours, string stamp, IntegrationReceipt? prior, bool createdFile)
    {
        var root = ParseObject(b);
        var top = JsonEdit.TopObject(b);
        var receipt = new IntegrationReceipt { Folder = "", File = "", Kind = IntegrationKind.Meters, CreatedFile = createdFile };
        byte[] result;
        if (top.Member(EnvKey) is { } m)
        {
            if (root[EnvKey] is not JsonObject env) throw Malformed();
            var envObject = JsonEdit.Object(b, m.ValueStart);
            if (envObject.Member(PluginDirsKey) is { } km)
            {
                if (Str(env[PluginDirsKey]) is not { } list) throw Malformed();
                // Updating Sanduhr's own entry keeps what the first install recorded; a receipt holding only a switch recorded no list.
                if (prior is not null && RemovingPluginDir(list) is not null) receipt = prior;
                else receipt = receipt with { Previous = Utf8(JsonEdit.Text(b, km)) };
                var updated = AddingPluginDir(ours, list);
                result = updated == list ? b : JsonEdit.Set(b, envObject, PluginDirsKey, JsonEdit.Value.Str(updated)).Bytes;
            }
            else
            {
                var r = JsonEdit.Set(b, envObject, PluginDirsKey, JsonEdit.Value.Str(ours));
                receipt = receipt with { CreatedKey = true, EmptyInner = r.EmptyInner is null ? null : Utf8(r.EmptyInner) };
                result = r.Bytes;
            }
        }
        else
        {
            var r = JsonEdit.Set(b, top, EnvKey, JsonEdit.Value.Obj(new JsonEdit.Pair(PluginDirsKey, JsonEdit.Value.Str(ours))));
            receipt = receipt with { CreatedParent = true, CreatedKey = true, EmptyInner = r.EmptyInner is null ? null : Utf8(r.EmptyInner) };
            result = r.Bytes;
        }
        VerifyMeters(b, result, ours);
        // The version the entry names now, and a switch made earlier, which stays.
        return new Plan(result, receipt with { Pinned = stamp, Switched = prior?.Switched });
    }

    private Plan PlanInstallHooks(byte[] b, IntegrationReceipt? prior, bool createdFile)
    {
        var root = ParseObject(b);
        var top = JsonEdit.TopObject(b);
        var receipt = new IntegrationReceipt { Folder = "", File = "", Kind = IntegrationKind.Hooks, CreatedFile = createdFile };
        string? hooksInner = null;
        var createdEvents = new List<string>();
        var arrayInners = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var result = b;
        if (top.Member(HooksKey) is null)
        {
            var r = JsonEdit.Set(b, top, HooksKey, HooksValue());
            receipt = receipt with { CreatedParent = true, EmptyInner = r.EmptyInner is null ? null : Utf8(r.EmptyInner) };
            createdEvents.AddRange(HookEvents.Select(e => e.Name));
            result = r.Bytes;
        }
        else
        {
            if (root[HooksKey] is not JsonObject hooks) throw Malformed();
            // Updating Sanduhr's own entries keeps what the first install recorded.
            if (prior is not null && HookEvents.Any(e => hooks[e.Name] is JsonArray l && l.Any(IsOurHookGroup)))
            {
                receipt = prior;
                if (prior.Hooks is { } h)
                {
                    hooksInner = h.HooksInner;
                    createdEvents.AddRange(h.CreatedEvents);
                    foreach (var kv in h.ArrayInners) arrayInners[kv.Key] = kv.Value;
                }
            }
            foreach (var e in HookEvents)
                result = AddHookGroup(e.Name, result, ref hooksInner, createdEvents, arrayInners);
        }
        var hr = new HookReceipt { HooksInner = hooksInner, CreatedEvents = createdEvents };
        foreach (var kv in arrayInners) hr.ArrayInners[kv.Key] = kv.Value;
        VerifyHooks(b, result, installed: true);
        return new Plan(result, receipt with { Hooks = hr });
    }

    private static JsonEdit.ObjectSpan? HooksObject(byte[] b)
    {
        var top = JsonEdit.TopObject(b);
        return top.Member(HooksKey) is { } m ? JsonEdit.Object(b, m.ValueStart) : null;
    }

    private static JsonEdit.ArraySpan? HookList(byte[] b, JsonEdit.ObjectSpan hooks, string name)
        => hooks.Member(name) is { } m ? JsonEdit.Array(b, m.ValueStart) : null;

    private static List<int> OurIndexes(byte[] b, JsonEdit.ArraySpan list)
        => Enumerable.Range(0, list.Elements.Count).Where(i => IsOurHookGroup(ParseElement(b, list.Elements[i]))).ToList();

    private static JsonNode? ParseElement(byte[] b, JsonEdit.Element e)
    {
        try { return JsonNode.Parse(b.AsMemory(e.Start, e.End - e.Start).Span, documentOptions: JsonEdit.StrictOptions); }
        catch (JsonException) { return null; }
    }

    private static byte[] AddHookGroup(string name, byte[] b, ref string? hooksInner, List<string> createdEvents, SortedDictionary<string, string> arrayInners)
    {
        var hooks = HooksObject(b) ?? throw Malformed();
        var group = HookGroup(name);
        if (HookList(b, hooks, name) is not { } list)
        {
            var r = JsonEdit.Set(b, hooks, name, JsonEdit.Value.Arr(group));
            if (r.EmptyInner is not null && hooksInner is null) hooksInner = Utf8(r.EmptyInner);
            if (!createdEvents.Contains(name)) createdEvents.Add(name);
            return r.Bytes;
        }
        var mine = OurIndexes(b, list);
        if (mine.Count == 0)
        {
            var r = JsonEdit.Append(b, list, group);
            if (r.EmptyInner is not null && !arrayInners.ContainsKey(name)) arrayInners[name] = Utf8(r.EmptyInner);
            return r.Bytes;
        }
        var outp = b;
        // Extra copies of Sanduhr's entry go, last first, so the earlier offsets hold.
        foreach (var i in Enumerable.Reverse(mine.Skip(1).ToList()))
        {
            var h = HooksObject(outp); var l = h is null ? null : HookList(outp, h, name);
            if (l is null) break;
            outp = JsonEdit.RemoveElement(outp, l, i);
        }
        var h2 = HooksObject(outp) ?? throw Malformed();
        var l2 = HookList(outp, h2, name) ?? throw Malformed();
        var current = ParseElement(outp, l2.Elements[mine[0]]);
        if (current is not null && JsonNode.DeepEquals(current, ToNode(group))) return outp;
        return JsonEdit.ReplaceElement(outp, l2, mine[0], group);
    }

    // ------------------------------------------------------------------ remove

    /// <summary>Undoes <paramref name="kind"/> in <paramref name="folder"/> from its receipt (the meters switch first).</summary>
    public IntegrationResult Remove(IntegrationKind kind, string folder)
    {
        RequireKind(kind);
        return Run("remove", () =>
        {
            if (kind == IntegrationKind.Meters) UndoModSwitchCore(folder);
            var ledger = LoadReceipts();
            var receipts = ledger.Records.ToList();
            var norm = Normalize(folder);
            var files = receipts.Where(r => r.Kind == kind && SamePath(r.Folder, norm)).Select(r => r.File)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
            {
                if (ledger.FileUnreadable || SetAsideFile(ledger.Unreadable, kind, norm) is not null)
                    throw new IntegrationException(IntegrationFailure.ReceiptSetAside,
                        "Sanduhr's record of what it installed here could not be read, so nothing was changed.");
                files.Add(SettingsFile(folder));
            }
            foreach (var file in files)
            {
                var receipt = receipts.FirstOrDefault(r => r.Kind == kind && SamePath(r.File, file));
                // A file Sanduhr made (for either piece) goes again once nothing else is in it.
                bool createdByUs = receipts.Any(r => r.CreatedFile && SamePath(r.File, file));
                var outcome = CommitLoop(file, original =>
                {
                    if (original is null) return null;
                    var edit = Guarded(file, () => PlanRemove(kind, original, receipt));
                    if (edit is null) return null;
                    BeforeCommit?.Invoke(file);
                    bool delete = createdByUs && JsonEdit.TopObject(edit).Members.Count == 0;
                    return new Edit(delete ? null : edit);
                }, backup: false);
                if (outcome == CommitOutcome.Conflict) throw new IntegrationException(IntegrationFailure.WriteFailed, $"{file} kept changing; nothing was written.");
                receipts.RemoveAll(r => r.Kind == kind && SamePath(r.File, file));
                // The file Sanduhr made is still there with the other piece in it: that piece now answers for it.
                if (createdByUs && File.Exists(file))
                    for (int i = 0; i < receipts.Count; i++)
                        if (SamePath(receipts[i].File, file) && !receipts[i].CreatedFile) receipts[i] = receipts[i] with { CreatedFile = true };
                SaveReceipts(receipts);
            }
            return "";
        });
    }

    private static string? SetAsideFile(IEnumerable<byte[]> unreadable, IntegrationKind kind, string normFolder)
    {
        var kindName = kind switch { IntegrationKind.Meters => "meters", IntegrationKind.Hooks => "hooks", IntegrationKind.Statusline => "statusline", _ => "mcp" };
        foreach (var raw in unreadable)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var o = doc.RootElement;
                if (o.ValueKind != JsonValueKind.Object) continue;
                if (!o.TryGetProperty("kind", out var k) || k.GetString() != kindName) continue;
                if (!o.TryGetProperty("folder", out var f) || f.ValueKind != JsonValueKind.String || !SamePath(Normalize(f.GetString()!), normFolder)) continue;
                return o.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String ? file.GetString() : normFolder;
            }
            catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException) { }
        }
        return null;
    }

    private byte[]? PlanRemove(IntegrationKind kind, byte[] b, IntegrationReceipt? receipt)
    {
        var root = ParseObject(b);
        var top = JsonEdit.TopObject(b);
        return kind == IntegrationKind.Meters ? PlanRemoveMeters(b, root, top, receipt) : PlanRemoveHooks(b, root, top, receipt);
    }

    private byte[]? PlanRemoveMeters(byte[] b, JsonObject root, JsonEdit.ObjectSpan top, IntegrationReceipt? receipt)
    {
        if (top.Member(EnvKey) is not { } m || root[EnvKey] is not JsonObject env || Str(env[PluginDirsKey]) is not { } list
            || RemovingPluginDir(list) is not { } remaining) return null;
        var envObject = JsonEdit.Object(b, m.ValueStart);
        var inner = receipt?.EmptyInner is { } ei ? Encoding.UTF8.GetBytes(ei) : null;
        byte[] result;
        bool isEmpty = string.IsNullOrWhiteSpace(remaining);
        if (isEmpty && (receipt?.CreatedKey ?? receipt?.Previous is null))
        {
            if (receipt?.CreatedParent == true)
            {
                result = JsonEdit.Remove(b, envObject, PluginDirsKey);
                var after = JsonEdit.TopObject(result);
                if (after.Member(EnvKey) is { } em && JsonEdit.Object(result, em.ValueStart).Members.Count == 0)
                    result = JsonEdit.Remove(result, after, EnvKey, inner);
            }
            else result = JsonEdit.Remove(b, envObject, PluginDirsKey, inner);
        }
        else if (receipt?.Previous is { } previous && PreviousString(previous) == remaining)
        {
            result = JsonEdit.Restore(b, envObject, PluginDirsKey, Encoding.UTF8.GetBytes(previous));
        }
        else result = JsonEdit.Set(b, envObject, PluginDirsKey, JsonEdit.Value.Str(remaining)).Bytes;
        VerifyMeters(b, result, null);
        return result;
    }

    private static string? PreviousString(string jsonText)
    {
        try { return JsonNode.Parse(jsonText) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; }
        catch (JsonException) { return null; }
    }

    private byte[]? PlanRemoveHooks(byte[] b, JsonObject root, JsonEdit.ObjectSpan top, IntegrationReceipt? receipt)
    {
        if (root[HooksKey] is not JsonObject hooksNode || !HookEvents.Any(e => hooksNode[e.Name] is JsonArray l && l.Any(IsOurHookGroup))) return null;
        var made = receipt?.Hooks;
        var result = b;
        foreach (var e in Enumerable.Reverse(HookEvents))
        {
            var h = HooksObject(result);
            var list = h is null ? null : HookList(result, h, e.Name);
            if (list is null) continue;
            var mine = OurIndexes(result, list);
            if (mine.Count == 0) continue;
            byte[]? inner = made is not null && made.ArrayInners.TryGetValue(e.Name, out var ai) ? Encoding.UTF8.GetBytes(ai) : null;
            foreach (var i in Enumerable.Reverse(mine))
            {
                var h2 = HooksObject(result); var l = h2 is null ? null : HookList(result, h2, e.Name);
                if (l is null) break;
                result = JsonEdit.RemoveElement(result, l, i, inner);
            }
            if (made?.CreatedEvents.Contains(e.Name) != true) continue;
            var h3 = HooksObject(result); var l3 = h3 is null ? null : HookList(result, h3, e.Name);
            if (l3 is null || l3.Elements.Count != 0) continue;
            result = JsonEdit.Remove(result, h3!, e.Name, made.HooksInner is { } hi ? Encoding.UTF8.GetBytes(hi) : null);
        }
        if (receipt?.CreatedParent == true && HooksObject(result) is { } hp && hp.Members.Count == 0)
            result = JsonEdit.Remove(result, JsonEdit.TopObject(result), HooksKey, receipt.EmptyInner is { } ei ? Encoding.UTF8.GetBytes(ei) : null);
        VerifyHooks(b, result, installed: false);
        return result;
    }

    // ------------------------------------------------------------------ the mod switch

    /// <summary>
    /// Switches Sanduhr's mod on or off for <paramref name="folder"/>. Off writes
    /// <c>enabledPlugins["sanduhr-meters@inline"]: false</c> and keeps the entry; On undoes that (or overrides a false of the user's
    /// own with true, recording it), and pins the app's version when the list has no entry.
    /// </summary>
    public IntegrationResult SwitchOwnMod(string folder, bool on) => Run("mod switch", () =>
    {
        var state = OwnModStateOf(folder);
        if (!state.Readable) throw new IntegrationException(IntegrationFailure.Malformed, NotObject(SettingsFile(folder)));
        if (on)
        {
            if (state.Switched == false) UndoModSwitchCore(folder);
            else if (state.Inline == false) WriteModSwitch(true, folder);
            if (!state.Listed)
            {
                if (RefreshModFiles() is null || CurrentModStamp() is not { } stamp)
                    throw new IntegrationException(IntegrationFailure.ModFilesMissing, "The meters mod's files are not in this copy of Sanduhr.");
                InstallEntry(IntegrationKind.Meters, folder, stamp);
            }
        }
        else
        {
            if (!state.Listed) return "";
            if (state.Switched == true)
            {
                // Sanduhr's true overrode the user's own false: putting that back turns it off.
                UndoModSwitchCore(folder);
                if (OwnModStateOf(folder).IsOn) WriteModSwitch(false, folder);
            }
            else if (state.Inline != false) WriteModSwitch(false, folder);
        }
        return "";
    });

    /// <summary>Moves a pinned entry to the app's version. The old version's files stay until <see cref="PruneModFiles"/>. The App asks for consent (new capabilities) before calling.</summary>
    public ModUpdateResult UpdateOwnMod(string folder)
    {
        string? message = null;
        ModUpdateOutcome outcome = ModUpdateOutcome.UpToDate;
        var res = Run("mod update", () =>
        {
            var state = OwnModStateOf(folder);
            if (!state.Readable) throw new IntegrationException(IntegrationFailure.Malformed, NotObject(SettingsFile(folder)));
            if (!state.Listed || state.Pinned is not { } old) return "";
            if (RefreshModFiles() is null || CurrentModStamp() is not { } now)
                throw new IntegrationException(IntegrationFailure.ModFilesMissing, "The meters mod's files are not in this copy of Sanduhr.");
            if (string.Equals(now, old, StringComparison.OrdinalIgnoreCase)) return "";
            InstallEntry(IntegrationKind.Meters, folder, now);
            outcome = ModUpdateOutcome.Updated;
            return "";
        });
        message = res.Message;
        return new ModUpdateResult(res.Ok, message, outcome, res.Failure);
    }

    /// <summary>
    /// Re-points a pre-3.5.0 registration (meters: a plugin folders entry under a legacy mods root; hooks: a group running the
    /// <see cref="LegacyHookCommand"/>) at the current location, through the same install engine (backup, receipts). Anything
    /// that is not a legacy entry of Sanduhr's is <see cref="RepointOutcome.NothingToDo"/>. Never throws.
    /// </summary>
    public RepointOutcome RepointLegacy(IntegrationKind kind, string folder)
    {
        try
        {
            RequireKind(kind);
            if (kind == IntegrationKind.Meters)
            {
                var state = OwnModStateOf(folder);
                if (!state.Readable || state.Entry is not { } entry || !IsLegacyModEntry(entry)) return RepointOutcome.NothingToDo;
                if (RefreshModFiles() is null || CurrentModStamp() is not { } stamp) return RepointOutcome.Failed;
                var res = Run("repoint", () => { InstallEntry(IntegrationKind.Meters, folder, stamp); return ""; });
                return res.Ok ? RepointOutcome.Repointed : RepointOutcome.Failed;
            }
            if (!HasLegacyHookGroup(folder)) return RepointOutcome.NothingToDo;
            return Install(IntegrationKind.Hooks, folder).Ok ? RepointOutcome.Repointed : RepointOutcome.Failed;
        }
        catch (Exception e)
        {
            _log?.Invoke($"repoint failed ({e.GetType().Name})");
            return RepointOutcome.Failed;
        }
    }

    private bool HasLegacyHookGroup(string folder)
    {
        var data = ReadFile(SettingsFile(folder));
        if (data is null) return false;
        JsonObject root;
        try { root = ParseObject(data); }
        catch (IntegrationException) { return false; }
        return root[HooksKey] is JsonObject hooks
            && HookEvents.Any(e => hooks[e.Name] is JsonArray l && l.Any(IsLegacyHookGroup));
    }

    /// <summary>Takes out what the switch wrote, back to the member's old bytes. A member someone changed since is left as it is; the switch's record goes either way.</summary>
    public IntegrationResult UndoModSwitch(string folder) => Run("mod switch undo", () => { UndoModSwitchCore(folder); return ""; });

    private void UndoModSwitchCore(string folder)
    {
        var file = SettingsFile(folder);
        var receipts = LoadReceipts().Records.ToList();
        int i = receipts.FindIndex(r => r.Kind == IntegrationKind.Meters && SamePath(r.File, file));
        if (i < 0 || receipts[i].Switched is not { } made) return;
        var outcome = CommitLoop(file, original =>
        {
            if (original is null) return null;
            var edit = Guarded(file, () => PlanUndoSwitch(original, made));
            if (edit is null) return null;
            BeforeCommit?.Invoke(file);
            return new Edit(edit);
        }, backup: false);
        if (outcome == CommitOutcome.Conflict) throw new IntegrationException(IntegrationFailure.WriteFailed, $"{file} kept changing; nothing was written.");
        receipts[i] = receipts[i] with { Switched = null };
        // A receipt that held only the switch has nothing left to undo.
        if (receipts[i].Previous is null && receipts[i].CreatedKey is null) receipts.RemoveAt(i);
        SaveReceipts(receipts);
    }

    private void WriteModSwitch(bool value, string folder)
    {
        var file = SettingsFile(folder);
        var receipts = LoadReceipts().Records.ToList();
        SwitchReceipt? made = null;
        var outcome = CommitLoop(file, original =>
        {
            if (original is null) throw new IntegrationException(IntegrationFailure.Malformed, $"{file} does not exist.");
            var (bytes, m) = Guarded(file, () => PlanSwitch(original, value));
            made = m;
            BeforeCommit?.Invoke(file);
            return new Edit(bytes);
        }, backup: true);
        if (outcome == CommitOutcome.Conflict) throw new IntegrationException(IntegrationFailure.WriteFailed, $"{file} kept changing; nothing was written.");
        int i = receipts.FindIndex(r => r.Kind == IntegrationKind.Meters && SamePath(r.File, file));
        if (i >= 0) receipts[i] = receipts[i] with { Switched = made };
        else receipts.Add(new IntegrationReceipt { Folder = Normalize(folder), File = file, Kind = IntegrationKind.Meters, Switched = made });
        SaveReceipts(receipts);
    }

    private (byte[], SwitchReceipt) PlanSwitch(byte[] b, bool value)
    {
        var root = ParseObject(b);
        var top = JsonEdit.TopObject(b);
        byte[] result;
        SwitchReceipt made;
        if (top.Member(EnabledPluginsKey) is { } m)
        {
            if (root[EnabledPluginsKey] is not JsonObject) throw Malformed();
            var r = JsonEdit.Set(b, JsonEdit.Object(b, m.ValueStart), OwnModKey, JsonEdit.Value.Bool(value));
            made = new SwitchReceipt
            {
                Key = OwnModKey, Value = value,
                Previous = r.Previous is null ? null : Utf8(r.Previous),
                EmptyInner = r.EmptyInner is null ? null : Utf8(r.EmptyInner),
            };
            result = r.Bytes;
        }
        else
        {
            var r = JsonEdit.Set(b, top, EnabledPluginsKey, JsonEdit.Value.Obj(new JsonEdit.Pair(OwnModKey, JsonEdit.Value.Bool(value))));
            made = new SwitchReceipt { Key = OwnModKey, Value = value, CreatedParent = true, EmptyInner = r.EmptyInner is null ? null : Utf8(r.EmptyInner) };
            result = r.Bytes;
        }
        VerifySwitch(b, result, OwnModKey, JsonValue.Create(value));
        return (result, made);
    }

    private byte[]? PlanUndoSwitch(byte[] b, SwitchReceipt made)
    {
        var root = ParseObject(b);
        var top = JsonEdit.TopObject(b);
        if (top.Member(EnabledPluginsKey) is not { } m || root[EnabledPluginsKey] is not JsonObject enabled
            || BoolOf(enabled[made.Key]) != made.Value) return null;
        var obj = JsonEdit.Object(b, m.ValueStart);
        var inner = made.EmptyInner is { } ei ? Encoding.UTF8.GetBytes(ei) : null;
        byte[] result;
        JsonNode? expect = null;
        if (made.Previous is { } previous)
        {
            result = JsonEdit.Restore(b, obj, made.Key, Encoding.UTF8.GetBytes(previous));
            try { expect = JsonNode.Parse(previous, documentOptions: JsonEdit.StrictOptions); } catch (JsonException) { }
        }
        else if (made.CreatedParent)
        {
            result = JsonEdit.Remove(b, obj, made.Key);
            var after = JsonEdit.TopObject(result);
            if (after.Member(EnabledPluginsKey) is { } em && JsonEdit.Object(result, em.ValueStart).Members.Count == 0)
                result = JsonEdit.Remove(result, after, EnabledPluginsKey, inner);
        }
        else result = JsonEdit.Remove(b, obj, made.Key, inner);
        VerifySwitch(b, result, made.Key, expect);
        return result;
    }

    // ------------------------------------------------------------------ undo all

    /// <summary>
    /// Walks the receipts of kinds meters and hooks (only those in <paramref name="folders"/> when given) and removes each,
    /// reporting per receipt. One failing receipt does not stop the rest.
    /// </summary>
    public IReadOnlyList<UndoReport> UndoAll(IEnumerable<string>? folders = null)
    {
        var only = folders?.Select(Normalize).ToList();
        var targets = LoadReceipts().Records
            .Where(r => r.Kind is IntegrationKind.Meters or IntegrationKind.Hooks)
            .Where(r => only is null || only.Any(f => SamePath(f, r.Folder)))
            .Select(r => (r.Kind, Folder: r.Folder))
            .Distinct().ToList();
        var reports = new List<UndoReport>();
        foreach (var (kind, folder) in targets)
        {
            var res = Remove(kind, folder);
            reports.Add(new UndoReport(kind, folder, res.Ok, res.Message));
        }
        return reports;
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class IntegrationException(IntegrationFailure failure, string message) : Exception(message)
    {
        public IntegrationFailure Failure { get; } = failure;
    }

    private sealed record Edit(byte[]? Bytes);

    private static string NotObject(string file) => $"{file} is not a JSON object, so Sanduhr left it alone.";

    private static IntegrationException Malformed() => new(IntegrationFailure.Malformed, "settings.json is not shaped as Sanduhr expects, so it was left alone.");

    private static void RequireKind(IntegrationKind kind)
    {
        if (kind is not (IntegrationKind.Meters or IntegrationKind.Hooks))
            throw new ArgumentOutOfRangeException(nameof(kind), "ClaudeCodeIntegrations handles the meters and hooks kinds.");
    }

    private IntegrationResult Run(string operation, Func<string> body)
    {
        try { return IntegrationResult.Success(body()); }
        catch (IntegrationException e) { return IntegrationResult.Fail(e.Failure, e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"{operation} failed ({e.GetType().Name})");
            return IntegrationResult.Fail(IntegrationFailure.WriteFailed, "Sanduhr could not read or write the file.");
        }
    }

    /// <summary>Runs a planning step, naming <paramref name="file"/> when the text is not what a JSON edit needs.</summary>
    private static T Guarded<T>(string file, Func<T> body)
    {
        try { return body(); }
        catch (JsonEditException) { throw new IntegrationException(IntegrationFailure.Malformed, NotObject(file)); }
        catch (IntegrationException e) when (e.Failure == IntegrationFailure.Malformed) { throw new IntegrationException(IntegrationFailure.Malformed, NotObject(file)); }
    }

    /// <summary>Compare-and-swap loop over <see cref="ConfigCommit.Commit"/>, up to three tries. The plan returns null for nothing to do, an edit with null bytes to delete the file.</summary>
    private CommitOutcome CommitLoop(string file, Func<byte[]?, Edit?> plan, bool backup)
    {
        int busy = 0;
        for (int n = 0; n < 3; n++)
        {
            byte[]? original;
            try { original = ReadFile(file); }
            catch (UnauthorizedAccessException) { throw new IntegrationException(IntegrationFailure.WriteFailed, $"{file} could not be read."); }
            var edit = plan(original);
            if (edit is null) return CommitOutcome.NothingToDo;
            bool done;
            try { done = _commit.Commit(file, original, edit.Bytes, backup); }
            catch (JsonEditException) { throw new IntegrationException(IntegrationFailure.Malformed, NotObject(file)); }
            catch (IOException) when (++busy <= TransientTries)
            {
                // A scanner or Claude Code has the file for a moment: start over without using up a conflict try.
                Thread.Sleep(30 * busy);
                n--;
                continue;
            }
            if (done) return CommitOutcome.Written;
        }
        return CommitOutcome.Conflict;
    }

    private const int TransientTries = 4;

    private ReceiptLog.Decoded<IntegrationReceipt> LoadReceipts() => Transient(() => _receipts.Load());

    /// <summary>The file's bytes (null when absent); a sharing violation is waited out a few times before it is reported.</summary>
    private byte[]? ReadFile(string file)
    {
        for (int i = 1; ; i++)
        {
            try { return _commit.Read(file); }
            catch (IOException) when (i <= TransientTries) { Thread.Sleep(30 * i); }
        }
    }

    /// <summary>Runs an IO step, waiting out a sharing violation a few times.</summary>
    private static T Transient<T>(Func<T> step)
    {
        for (int i = 1; ; i++)
        {
            try { return step(); }
            catch (IOException) when (i <= TransientTries) { Thread.Sleep(30 * i); }
        }
    }

    private void SaveReceipts(List<IntegrationReceipt> receipts)
    {
        if (!Transient(() => _receipts.Save(receipts)))
            throw new IntegrationException(IntegrationFailure.WriteFailed, "Sanduhr could not save its record of the change.");
    }

    private static JsonObject ParseObject(byte[] b)
    {
        var body = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? b.AsSpan(3) : b.AsSpan();
        try
        {
            if (JsonNode.Parse(body, documentOptions: JsonEdit.StrictOptions) is JsonObject o) return o;
        }
        catch (JsonException) { }
        throw new IntegrationException(IntegrationFailure.Malformed, "The text is not a JSON object.");
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? BoolOf(JsonNode? n) => n?.GetValueKind() switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };

    private static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);

    private static JsonNode ToNode(JsonEdit.Value v) => v switch
    {
        JsonEdit.StringValue s => JsonValue.Create(s.Text)!,
        JsonEdit.BoolValue t => JsonValue.Create(t.Flag),
        JsonEdit.IntValue i => JsonValue.Create(i.Number),
        JsonEdit.ArrayValue a => new JsonArray([.. a.Items.Select(ToNode)]),
        JsonEdit.ObjectValue o => ToObject(o),
        _ => throw new InvalidOperationException(),
    };

    private static JsonObject ToObject(JsonEdit.ObjectValue o)
    {
        var obj = new JsonObject();
        foreach (var p in o.Pairs) obj[p.Key] = ToNode(p.Value);
        return obj;
    }

    // ------------------------------------------------------------------ checks: the edit changed Sanduhr's member and nothing else

    private void VerifyMeters(byte[] before, byte[] after, string? expected)
    {
        var old = ParseObject(before);
        var now = ParseObject(after);
        (JsonObject Left, List<string> Dirs) Split(JsonObject root)
        {
            var rest = (JsonObject)root.DeepClone();
            List<string> dirs = [];
            if (rest[EnvKey] is JsonObject env)
            {
                if (Str(env[PluginDirsKey]) is { } list) dirs = [.. list.Split(PluginDirsSeparator)];
                env.Remove(PluginDirsKey);
                if (env.Count == 0) rest.Remove(EnvKey);
            }
            return (rest, dirs);
        }
        var (oldRest, oldDirs) = Split(old);
        var (newRest, newDirs) = Split(now);
        if (!JsonNode.DeepEquals(newRest, oldRest)) throw Malformed();
        List<string> Others(List<string> d) => [.. d.Where(x => !IsOurModEntry(x) && !string.IsNullOrWhiteSpace(x))];
        if (!Others(oldDirs).SequenceEqual(Others(newDirs))) throw Malformed();
        var ours = newDirs.Where(IsOurModEntry).Select(x => x.Trim()).ToList();
        if (!ours.SequenceEqual(expected is null ? [] : new[] { expected })) throw Malformed();
    }

    private static void VerifyHooks(byte[] before, byte[] after, bool installed)
    {
        var old = ParseObject(before);
        var now = ParseObject(after);
        (JsonObject Left, JsonObject Others, Dictionary<string, JsonArray> Lists) Split(JsonObject root)
        {
            var rest = (JsonObject)root.DeepClone();
            JsonObject hooks = new();
            if (rest[HooksKey] is { } hv)
            {
                if (hv is not JsonObject h) throw Malformed();
                hooks = h;
                rest.Remove(HooksKey);
            }
            var lists = new Dictionary<string, JsonArray>();
            foreach (var (name, _) in HookEvents)
            {
                var v = hooks[name];
                if (v is not null && v is not JsonArray) throw Malformed();
                var arr = v as JsonArray ?? new JsonArray();
                hooks.Remove(name);
                lists[name] = arr;
            }
            return (rest, hooks, lists);
        }
        var (oldRest, oldOthers, oldLists) = Split(old);
        var (newRest, newOthers, newLists) = Split(now);
        if (!JsonNode.DeepEquals(newRest, oldRest) || !JsonNode.DeepEquals(newOthers, oldOthers)) throw Malformed();
        foreach (var (name, _) in HookEvents)
        {
            var o = new JsonArray([.. oldLists[name].Where(g => !IsOurHookGroup(g)).Select(g => g?.DeepClone())]);
            var n = new JsonArray([.. newLists[name].Where(g => !IsOurHookGroup(g)).Select(g => g?.DeepClone())]);
            if (!JsonNode.DeepEquals(o, n)) throw Malformed();
            var mine = new JsonArray([.. newLists[name].Where(IsOurHookGroup).Select(g => g?.DeepClone())]);
            var want = installed ? new JsonArray(ToNode(HookGroup(name))) : new JsonArray();
            if (!JsonNode.DeepEquals(mine, want)) throw Malformed();
        }
    }

    private static void VerifySwitch(byte[] before, byte[] after, string key, JsonNode? expect)
    {
        var old = ParseObject(before);
        var now = ParseObject(after);
        (JsonObject Left, JsonObject Others, JsonNode? Value) Split(JsonObject root)
        {
            var rest = (JsonObject)root.DeepClone();
            var enabled = rest[EnabledPluginsKey] as JsonObject ?? new JsonObject();
            rest.Remove(EnabledPluginsKey);
            var value = enabled[key]?.DeepClone();
            enabled.Remove(key);
            return (rest, enabled, value);
        }
        var (oldRest, oldOthers, _) = Split(old);
        var (newRest, newOthers, got) = Split(now);
        if (!JsonNode.DeepEquals(newRest, oldRest) || !JsonNode.DeepEquals(newOthers, oldOthers)) throw Malformed();
        if (!JsonNode.DeepEquals(got, expect)) throw Malformed();
    }
}
