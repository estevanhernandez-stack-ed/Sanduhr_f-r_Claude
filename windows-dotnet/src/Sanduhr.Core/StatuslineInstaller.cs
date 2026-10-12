using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What one Claude Code folder's <c>statusLine</c> is, as the Claude Code page shows it.</summary>
public enum StatuslineStatus
{
    /// <summary>No settings file, or no <c>statusLine</c> in it.</summary>
    NotInstalled,
    /// <summary>Sanduhr's entry, pointing at the installed script, which is the app's current one.</summary>
    Installed,
    /// <summary>Sanduhr's entry, but at another script path or with a script that isn't the app's current one.</summary>
    Outdated,
    /// <summary>Someone else's <c>statusLine</c>.</summary>
    Other,
    /// <summary>The settings file exists and can't be read as a JSON object; nothing is touched.</summary>
    Unreadable,
}

/// <summary>A Sanduhr statusline command taken apart: the script, the user's command it chains (combine), how the
/// lines join and which segments it prints.</summary>
public sealed record StatuslineCommand(string ScriptPath, string? Chain, string Join, IReadOnlyList<string> Segments);

/// <summary>
/// Installs / removes the Claude Code statusline integration: copies the
/// embedded script to the Sanduhr bin folder and registers it in ONE chosen
/// Claude Code home's <c>settings.json</c> under the <c>statusLine</c> key.
///
/// With a <c>sanduhrDir</c> the registration runs on the splice engine (Stage 4, 4.4):
/// <see cref="JsonEdit"/> changes only the <c>statusLine</c> value and leaves every other byte, the
/// edit is committed through <see cref="ConfigCommit"/> (re-read, compare-and-swap, backup in
/// <c>sanduhrDir\backups</c>, never beside the file), and a receipt in
/// <c>sanduhrDir\integrations\installs.json</c> records the value Sanduhr replaced byte for byte, so
/// <see cref="Remove"/> restores it exactly. A malformed settings.json is NEVER overwritten.
///
/// Without one, <see cref="Register"/> / <see cref="Deregister"/> keep their earlier behaviour (whole-file
/// rewrite, backup beside the file), for callers that predate the engine.
///
/// Remove only acts on a <c>statusLine</c> that is still Sanduhr's: the exact command shape
/// <see cref="TryParseCommand"/> reads. A foreign statusline, or a command of the user's that merely
/// mentions the script, is left untouched.
/// </summary>
public sealed class StatuslineInstaller
{
    public const string ScriptName = "sanduhr-statusline.ps1";
    public const string KeyName = "statusLine";
    public const string JoinLine = "line";
    public const string JoinSame = "same";

    /// <summary>The segments Sanduhr's line prints without <c>-Mine</c>, in the order they print.</summary>
    public static IReadOnlyList<string> DefaultSegments { get; } = ["session", "weekly", "resets"];

    private const string CommandPrefix = "powershell -NoProfile -ExecutionPolicy Bypass -File \"";

    private readonly string _homeDir;
    private readonly string _binDir;
    private readonly string? _sanduhrDir;
    private readonly Action<string>? _logBestEffort;

    /// <param name="homeDir">The user-profile directory the CC homes live under
    /// (injected for tests; app code passes the real profile).</param>
    /// <param name="binDir">Where the statusline script is installed
    /// (<see cref="Paths.StatuslineBinDir"/> in app code).</param>
    /// <param name="logBestEffort">Optional failure logger — operation +
    /// exception type only.</param>
    /// <param name="sanduhrDir">The Sanduhr app-data folder (<c>%APPDATA%\Sanduhr</c> in app code): config
    /// backups go to <c>backups</c> under it and receipts to <c>integrations\installs.json</c>. Absent: the
    /// earlier behaviour, for callers that don't use the splice engine.</param>
    public StatuslineInstaller(string homeDir, string binDir, Action<string>? logBestEffort = null, string? sanduhrDir = null)
    {
        _homeDir = homeDir;
        _binDir = binDir;
        _logBestEffort = logBestEffort;
        _sanduhrDir = sanduhrDir;
    }

    /// <summary>The installed script path.</summary>
    public string ScriptPath => Path.Combine(_binDir, ScriptName);

    /// <summary>Claude Code homes under the profile dir, as
    /// <see cref="ClaudeCodeHomes.Discover"/> finds them (<c>.claude</c>, every
    /// <c>.claude-*</c> home with <c>projects/</c> or a <c>.claude.json</c>, and the
    /// <c>CLAUDE_CONFIG_DIR</c> folder when it is in the profile; never a backup
    /// copy), in stable order. More than one → the consent dialog MUST ask which one (a silent
    /// default into an employer tenant is the breach the design review named).</summary>
    public IReadOnlyList<string> DetectCcHomes()
    {
        return ClaudeCodeHomes.Discover(_homeDir, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
    }

    /// <summary>Write (or refresh) the script. Idempotent — called on every
    /// install and on app start while enabled, which is how the widget acts as
    /// the script's update channel. UTF-8 WITH BOM: PowerShell 5.1 mis-decodes
    /// BOM-less UTF-8 and the separator glyph is non-ASCII.</summary>
    public bool InstallScript()
    {
        try
        {
            Directory.CreateDirectory(_binDir);
            File.WriteAllText(ScriptPath, StatuslineScript.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline script install failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>Delete the installed script (integration removal).</summary>
    public void RemoveScript()
    {
        try
        {
            if (File.Exists(ScriptPath))
                File.Delete(ScriptPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline script remove failed ({e.GetType().Name})");
        }
    }

    /// <summary>The statusLine command registered in CC settings. -NoProfile keeps
    /// render latency flat; the explicit path pins the invocation to our script.</summary>
    public string BuildCommand()
        => BuildCommand(null, JoinLine, DefaultSegments);

    /// <summary>
    /// The command line for a mode. <paramref name="chain"/> is the user's own command (combine): it travels
    /// as base64 of its UTF-8 bytes after <c>-Chain</c>, with <c>-Join line|same</c>. <paramref name="segments"/>
    /// are reduced to the known names in the fixed order (session, weekly, resets); <c>-Mine</c> is added only
    /// when that differs from the default.
    /// </summary>
    public string BuildCommand(string? chain, string join, IReadOnlyList<string> segments)
    {
        var sb = new StringBuilder(CommandPrefix).Append(ScriptPath).Append('"');
        if (!string.IsNullOrWhiteSpace(chain))
        {
            sb.Append(" -Chain ").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(chain)));
            sb.Append(" -Join ").Append(join == JoinSame ? JoinSame : JoinLine);
        }
        var mine = NormalizeSegments(segments);
        if (!mine.SequenceEqual(DefaultSegments))
            sb.Append(" -Mine ").Append(string.Join(',', mine));
        return sb.ToString();
    }

    /// <summary>The known segment names in <paramref name="segments"/>, once each, in the fixed order; the
    /// default when none are left.</summary>
    public static IReadOnlyList<string> NormalizeSegments(IReadOnlyList<string>? segments)
    {
        if (segments is null) return DefaultSegments;
        var kept = DefaultSegments.Where(name => segments.Contains(name, StringComparer.Ordinal)).ToList();
        return kept.Count == 0 ? DefaultSegments : kept;
    }

    /// <summary>Full path of a CC home's settings.json.</summary>
    public string SettingsPathFor(string ccHomeName)
        => Path.Combine(_homeDir, ccHomeName, "settings.json");

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// Reads a command as Sanduhr writes it, exactly: <c>powershell -NoProfile -ExecutionPolicy Bypass -File
    /// "&lt;dir&gt;\sanduhr-statusline.ps1"</c>, then optionally <c>-Chain &lt;base64&gt; -Join line|same</c>,
    /// then optionally <c>-Mine &lt;names&gt;</c>. Anything else is null: a user's command that mentions the
    /// script (with a pipe, a second command, extra arguments) is theirs.
    /// </summary>
    public static StatuslineCommand? TryParseCommand(string? command)
    {
        if (command is null) return null;
        var c = command.Trim();
        if (!c.StartsWith(CommandPrefix, StringComparison.Ordinal)) return null;
        int close = c.IndexOf('"', CommandPrefix.Length);
        if (close < 0) return null;
        var script = c[CommandPrefix.Length..close];
        if (script.Length == 0 || !script.EndsWith(ScriptName, StringComparison.OrdinalIgnoreCase)) return null;
        var name = Path.GetFileName(script);
        if (!string.Equals(name, ScriptName, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = c[(close + 1)..];
        string? chain = null;
        string join = JoinLine;
        IReadOnlyList<string> segments = DefaultSegments;
        if (rest.Length > 0)
        {
            if (rest[0] != ' ') return null;
            var words = rest[1..].Split(' ');
            int i = 0;
            if (i < words.Length && words[i] == "-Chain")
            {
                if (words.Length < i + 4) return null;
                var payload = words[i + 1];
                if (words[i + 2] != "-Join" || (words[i + 3] != JoinLine && words[i + 3] != JoinSame)) return null;
                chain = DecodeChain(payload);
                if (chain is null) return null;
                join = words[i + 3];
                i += 4;
            }
            if (i < words.Length && words[i] == "-Mine")
            {
                if (words.Length < i + 2) return null;
                var picked = words[i + 1].Split(',');
                var normalized = NormalizeSegments(picked);
                if (!picked.SequenceEqual(normalized)) return null; // unknown, repeated or out-of-order names
                segments = normalized;
                i += 2;
            }
            if (i != words.Length) return null;
        }
        return new StatuslineCommand(script, chain, join, segments);
    }

    private static string? DecodeChain(string payload)
    {
        if (payload.Length == 0) return null;
        try
        {
            var bytes = Convert.FromBase64String(payload);
            if (Convert.ToBase64String(bytes) != payload) return null; // canonical form only
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The state of <paramref name="ccHomeName"/>'s <c>statusLine</c>.</summary>
    public StatuslineStatus Status(string ccHomeName)
    {
        try
        {
            var bytes = ReadFile(SettingsPathFor(ccHomeName));
            if (bytes is null) return StatuslineStatus.NotInstalled;
            if (!JsonEdit.IsObject(bytes)) return StatuslineStatus.Unreadable;
            var top = JsonEdit.TopObject(bytes);
            if (top.Member(KeyName) is not { } member) return StatuslineStatus.NotInstalled;
            var parsed = TryParseCommand(CommandOf(JsonEdit.Text(bytes, member)));
            if (parsed is null) return StatuslineStatus.Other;
            return IsCurrent(parsed) ? StatuslineStatus.Installed : StatuslineStatus.Outdated;
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            return StatuslineStatus.Unreadable;
        }
    }

    /// <summary>The command of someone else's <c>statusLine</c> in <paramref name="ccHomeName"/>, or null when
    /// there is none (no entry, Sanduhr's own entry, an entry without a command, an unreadable file).</summary>
    public string? TheirCommand(string ccHomeName)
    {
        try
        {
            var bytes = ReadFile(SettingsPathFor(ccHomeName));
            if (bytes is null || !JsonEdit.IsObject(bytes)) return null;
            if (JsonEdit.TopObject(bytes).Member(KeyName) is not { } member) return null;
            var command = CommandOf(JsonEdit.Text(bytes, member));
            if (string.IsNullOrWhiteSpace(command) || TryParseCommand(command) is not null) return null;
            return command;
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when the home's settings.json carries Sanduhr's statusline (current or not).</summary>
    public bool IsRegistered(string ccHomeName)
        => Status(ccHomeName) is StatuslineStatus.Installed or StatuslineStatus.Outdated;

    private bool IsCurrent(StatuslineCommand parsed)
    {
        if (!string.Equals(Path.GetFullPath(parsed.ScriptPath), Path.GetFullPath(ScriptPath), StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            if (!File.Exists(ScriptPath)) return false;
            var text = File.ReadAllText(ScriptPath, Encoding.UTF8);
            return string.Equals(text.Replace("\r\n", "\n"), StatuslineScript.Content.Replace("\r\n", "\n"), StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? CommandOf(byte[] valueBytes)
    {
        using var doc = JsonDocument.Parse(valueBytes);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("command", out var c)
            && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
    }

    private static byte[]? ReadFile(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    // ---------------------------------------------------------------- install

    /// <summary>
    /// Writes Sanduhr's <c>statusLine</c> into <paramref name="ccHomeName"/>'s settings.json and records a
    /// receipt. With someone else's entry already there, it is kept byte for byte in the receipt;
    /// <see cref="StatuslineMode.Combine"/> also chains their command inside Sanduhr's (a combine with nothing
    /// to chain is a replace). On an entry that is already Sanduhr's the first receipt stays, so the user's
    /// own line is still what Remove restores. Needs a <c>sanduhrDir</c>; false (nothing written) without one,
    /// when the file can't be read as a JSON object, or when it kept changing under the write.
    /// </summary>
    /// <param name="segments">Sanduhr's segments: session, weekly, resets.</param>
    /// <param name="join">"line" (own row) or "same" (appended to their last row); only used by combine.</param>
    public bool Install(string ccHomeName, StatuslineMode mode, IReadOnlyList<string> segments, string join)
    {
        if (_sanduhrDir is null)
        {
            _logBestEffort?.Invoke("statusline install needs the Sanduhr data folder");
            return false;
        }
        string path = Path.GetFullPath(SettingsPathFor(ccHomeName));
        var commit = new ConfigCommit(Path.Combine(_sanduhrDir, "backups"));
        var store = ReceiptStorePath();
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var original = commit.Read(path);
                var bytes = original ?? "{}\n"u8.ToArray();
                if (!JsonEdit.IsObject(bytes))
                {
                    _logBestEffort?.Invoke("statusline install aborted (settings.json not an object)");
                    return false;
                }
                var ledger = store.Load();
                var receipts = ledger.Records.ToList();
                var prior = receipts.FirstOrDefault(r => IsMine(r, path));

                var top = JsonEdit.TopObject(bytes);
                var receipt = new IntegrationReceipt
                {
                    File = path,
                    Folder = FolderOf(ccHomeName),
                    Kind = IntegrationKind.Statusline,
                    CreatedFile = original is null,
                };
                string? chain = null;
                if (top.Member(KeyName) is { } member)
                {
                    var valueBytes = JsonEdit.Text(bytes, member);
                    var command = CommandOf(valueBytes);
                    if (TryParseCommand(command) is { } ours)
                    {
                        // Updating Sanduhr's own entry keeps what the first install recorded.
                        if (prior is not null) receipt = prior;
                        if (mode == StatuslineMode.Combine)
                        {
                            // Their command: the one already chained, else the line the first install replaced.
                            chain = ours.Chain;
                            if (chain is null && prior?.Previous is { } was) chain = CommandOf(Encoding.UTF8.GetBytes(was));
                            if (string.IsNullOrWhiteSpace(chain)) chain = null;
                        }
                    }
                    else
                    {
                        receipt = receipt with { Previous = Encoding.UTF8.GetString(valueBytes) };
                        if (mode == StatuslineMode.Combine && !string.IsNullOrWhiteSpace(command)) chain = command;
                    }
                }
                receipt = receipt with
                {
                    Mode = chain is null ? StatuslineMode.Replace : StatuslineMode.Combine,
                    Folder = FolderOf(ccHomeName),
                    File = path,
                };

                var value = JsonEdit.Value.Obj(
                    new JsonEdit.Pair("type", JsonEdit.Value.Str("command")),
                    new JsonEdit.Pair("command", JsonEdit.Value.Str(BuildCommand(chain, join, segments))));
                var set = JsonEdit.Set(bytes, top, KeyName, value);
                if (set.EmptyInner is { } inner) receipt = receipt with { EmptyInner = Encoding.UTF8.GetString(inner) };

                if (!TryCommit(commit, path, original, set.Bytes, backup: true)) continue;

                receipts.RemoveAll(r => IsMine(r, path));
                receipts.Add(receipt);
                if (!store.Save(receipts))
                {
                    _logBestEffort?.Invoke("statusline install wrote the config but not its receipt");
                    return false;
                }
                return true;
            }
            _logBestEffort?.Invoke("statusline install gave up (settings.json kept changing)");
            return false;
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline install failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>Register the statusline in <paramref name="ccHomeName"/>'s
    /// settings.json: a replace with the default segments. Returns false (and writes
    /// nothing) when the existing file can't be parsed — never stomp a config we
    /// can't round-trip.</summary>
    public bool Register(string ccHomeName)
        => _sanduhrDir is null
            ? LegacyRegister(ccHomeName)
            : Install(ccHomeName, StatuslineMode.Replace, DefaultSegments, JoinLine);

    // ---------------------------------------------------------------- remove

    /// <summary>
    /// Takes Sanduhr's <c>statusLine</c> out of <paramref name="ccHomeName"/>'s settings.json, but only while
    /// it is still Sanduhr's: the user's own line comes back byte for byte (from the receipt), or the key goes
    /// (and the file, when Sanduhr created it and nothing else is in it). True when the end state is "no
    /// Sanduhr statusline registered", including the no-op cases (file missing, key missing, foreign entry).
    /// </summary>
    public bool Remove(string ccHomeName)
    {
        if (_sanduhrDir is null) return LegacyDeregister(ccHomeName);
        string path = Path.GetFullPath(SettingsPathFor(ccHomeName));
        var commit = new ConfigCommit(Path.Combine(_sanduhrDir, "backups"));
        var store = ReceiptStorePath();
        try
        {
            var ledger = store.Load();
            var receipts = ledger.Records.ToList();
            var receipt = receipts.FirstOrDefault(r => IsMine(r, path));
            bool done = false;
            for (int attempt = 0; attempt < 3 && !done; attempt++)
            {
                var original = commit.Read(path);
                if (original is null) { done = true; break; }
                if (!JsonEdit.IsObject(original))
                {
                    _logBestEffort?.Invoke("statusline remove skipped (settings.json not an object)");
                    return false;
                }
                var top = JsonEdit.TopObject(original);
                if (top.Member(KeyName) is not { } member
                    || TryParseCommand(CommandOf(JsonEdit.Text(original, member))) is null)
                {
                    done = true; // absent or no longer Sanduhr's: leave it alone
                    break;
                }
                byte[] result = receipt?.Previous is { } previous
                    ? JsonEdit.Restore(original, top, KeyName, Encoding.UTF8.GetBytes(previous))
                    : JsonEdit.Remove(original, top, KeyName,
                        receipt?.EmptyInner is { } inner ? Encoding.UTF8.GetBytes(inner) : null);
                bool delete = receipt?.CreatedFile == true && JsonEdit.TopObject(result).Members.Count == 0;
                done = TryCommit(commit, path, original, delete ? null : result, backup: false);
            }
            if (!done)
            {
                _logBestEffort?.Invoke("statusline remove gave up (settings.json kept changing)");
                return false;
            }
            if (receipt is not null)
            {
                receipts.RemoveAll(r => IsMine(r, path));
                store.Save(receipts);
            }
            return true;
        }
        catch (Exception e) when (e is JsonEditException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline remove failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>Remove the statusLine entry — see <see cref="Remove"/>.</summary>
    public bool Deregister(string ccHomeName) => Remove(ccHomeName);

    /// <summary>A commit that tries again when the file is briefly held (a scanner, Claude Code mid-write).
    /// False means the file changed under the write: the caller plans again.</summary>
    private static bool TryCommit(ConfigCommit commit, string path, byte[]? original, byte[]? bytes, bool backup)
    {
        for (int n = 0; ; n++)
        {
            try { return commit.Commit(path, original, bytes, backup); }
            catch (IOException) when (n < 2) { Thread.Sleep(100 * (n + 1)); }
        }
    }

    private ReceiptStore ReceiptStorePath()
        => new(Path.Combine(_sanduhrDir!, "integrations", "installs.json"));

    private string FolderOf(string ccHomeName)
    {
        var folder = Path.Combine(_homeDir, ccHomeName);
        return SeatFiles.NormalizeFolder(folder) ?? folder;
    }

    private static bool IsMine(IntegrationReceipt r, string path)
        => r.Kind == IntegrationKind.Statusline && string.Equals(r.File, path, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- earlier behaviour (no sanduhrDir)

    private bool LegacyRegister(string ccHomeName)
    {
        string path = SettingsPathFor(ccHomeName);
        JsonObject root;
        try
        {
            if (File.Exists(path))
            {
                if (JsonNode.Parse(File.ReadAllText(path), documentOptions: JsonEdit.StrictOptions) is not JsonObject parsed)
                {
                    _logBestEffort?.Invoke("statusline register aborted (settings.json not an object)");
                    return false;
                }
                root = parsed;
            }
            else
            {
                root = new JsonObject();
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline register aborted ({e.GetType().Name})");
            return false;
        }

        root[KeyName] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = BuildCommand(),
        };
        return WriteSettingsSafely(path, root, "register");
    }

    private bool LegacyDeregister(string ccHomeName)
    {
        string path = SettingsPathFor(ccHomeName);
        try
        {
            if (!File.Exists(path))
                return true;
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: JsonEdit.StrictOptions) is not JsonObject root)
                return true; // nothing we can safely edit; nothing of ours provably present
            if (!LegacyIsOurs(root))
                return true; // absent or foreign — leave it alone
            root.Remove(KeyName);
            return WriteSettingsSafely(path, root, "deregister");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline deregister failed ({e.GetType().Name})");
            return false;
        }
    }

    private static bool LegacyIsOurs(JsonObject root)
    {
        try
        {
            return root[KeyName] is JsonObject sl
                && (string?)sl["command"] is { } cmd
                && TryParseCommand(cmd) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Timestamped backup beside the file, then temp + atomic move.</summary>
    private bool WriteSettingsSafely(string path, JsonObject root, string operation)
    {
        try
        {
            if (File.Exists(path))
            {
                string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Copy(path, path + ".sanduhr-backup-" + stamp, overwrite: true);
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline {operation} write failed ({e.GetType().Name})");
            return false;
        }
    }
}
