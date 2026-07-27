using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Installs / removes the Claude Code statusline integration: copies the
/// embedded script to the Sanduhr bin folder and registers it in ONE chosen
/// Claude Code home's <c>settings.json</c> under the <c>statusLine</c> key.
///
/// Config-write safety (WS-E, load-bearing): the target file is someone else's
/// config. Re-read immediately before write, mutate ONLY the owned key,
/// preserve unknown keys byte-for-byte at the JSON level, take a timestamped
/// backup beside the file, and swap via temp + atomic move. A malformed
/// settings.json is NEVER overwritten — the install fails visibly instead.
///
/// Deregistration only removes a <c>statusLine</c> that points at Sanduhr's own
/// script; a foreign statusline is left untouched.
/// </summary>
public sealed class StatuslineInstaller
{
    /// <summary>The Claude Code config homes Sanduhr knows how to detect —
    /// mirrors CcLogReader's log roots (a CC home and its projects/ log root are
    /// the same directory family).</summary>
    private static readonly string[] KnownHomeNames = { ".claude", ".claude-personal" };

    private readonly string _homeDir;
    private readonly string _binDir;
    private readonly Action<string>? _logBestEffort;

    /// <param name="homeDir">The user-profile directory the CC homes live under
    /// (injected for tests; app code passes the real profile).</param>
    /// <param name="binDir">Where the statusline script is installed
    /// (<see cref="Paths.StatuslineBinDir"/> in app code).</param>
    /// <param name="logBestEffort">Optional failure logger — operation +
    /// exception type only.</param>
    public StatuslineInstaller(string homeDir, string binDir, Action<string>? logBestEffort = null)
    {
        _homeDir = homeDir;
        _binDir = binDir;
        _logBestEffort = logBestEffort;
    }

    /// <summary>The installed script path.</summary>
    public string ScriptPath => Path.Combine(_binDir, "sanduhr-statusline.ps1");

    /// <summary>Claude Code homes that exist under the profile dir, in stable
    /// order. More than one → the consent dialog MUST ask which one (a silent
    /// default into an employer tenant is the breach the design review named).</summary>
    public IReadOnlyList<string> DetectCcHomes()
    {
        var found = new List<string>();
        foreach (var name in KnownHomeNames)
        {
            if (Directory.Exists(Path.Combine(_homeDir, name)))
                found.Add(name);
        }
        return found;
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
        => $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\"";

    /// <summary>Full path of a CC home's settings.json.</summary>
    public string SettingsPathFor(string ccHomeName)
        => Path.Combine(_homeDir, ccHomeName, "settings.json");

    /// <summary>Register the statusline in <paramref name="ccHomeName"/>'s
    /// settings.json. Returns false (and writes nothing) when the existing file
    /// can't be parsed — never stomp a config we can't round-trip.</summary>
    public bool Register(string ccHomeName)
    {
        string path = SettingsPathFor(ccHomeName);
        JsonObject root;
        try
        {
            if (File.Exists(path))
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject parsed)
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

        root["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = BuildCommand(),
        };
        return WriteSettingsSafely(path, root, "register");
    }

    /// <summary>Remove the statusLine entry from <paramref name="ccHomeName"/>'s
    /// settings.json — but ONLY when it points at Sanduhr's script. Returns true
    /// when the end state is "no Sanduhr statusline registered" (including the
    /// no-op cases: file missing, key missing, foreign statusline).</summary>
    public bool Deregister(string ccHomeName)
    {
        string path = SettingsPathFor(ccHomeName);
        try
        {
            if (!File.Exists(path))
                return true;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
                return true; // nothing we can safely edit; nothing of ours provably present
            if (!IsOurs(root))
                return true; // absent or foreign — leave it alone
            root.Remove("statusLine");
            return WriteSettingsSafely(path, root, "deregister");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _logBestEffort?.Invoke($"statusline deregister failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>True when the home's settings.json carries Sanduhr's statusline.</summary>
    public bool IsRegistered(string ccHomeName)
    {
        try
        {
            string path = SettingsPathFor(ccHomeName);
            if (!File.Exists(path))
                return false;
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root && IsOurs(root);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsOurs(JsonObject root)
    {
        try
        {
            return root["statusLine"] is JsonObject sl
                && (string?)sl["command"] is { } cmd
                && cmd.Contains("sanduhr-statusline.ps1", StringComparison.OrdinalIgnoreCase);
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
