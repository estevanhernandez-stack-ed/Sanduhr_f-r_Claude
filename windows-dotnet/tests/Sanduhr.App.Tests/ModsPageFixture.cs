using System.IO;
using System.Text;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>
/// A scratch profile with Claude Code folders, a scratch Sanduhr AppData and external folder, and a stand-in for the meters mod's files,
/// for the Claude Code cards and Mods &amp; Widgets (slice 4). Touches no process-wide static, never the real profile or AppData.
/// </summary>
[Collection("SettingsWindow")]
public abstract class ModsPageFixture : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "sanduhr-mods4-" + Guid.NewGuid().ToString("N")[..10]);
    protected string Profile => Path.Combine(Root, "profile");
    protected string ModSource => Path.Combine(Root, "app", "mods", "sanduhr-meters");

    protected ModsPageFixture()
    {
        Directory.CreateDirectory(Profile);
        Put(ModSource, ".claude-plugin/plugin.json", """{ "name": "sanduhr-meters", "version": "0.3.0" }""");
        Put(ModSource, "hooks/hooks.json", """{ "modules": ["./register.tsx"] }""");
        Put(ModSource, "hooks/register.tsx", "export {};");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A Claude Code home (it has a projects folder), with the given settings.json text when not null.</summary>
    protected string Home(string name, string? settings = null)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        if (settings is not null) File.WriteAllText(Path.Combine(dir, "settings.json"), settings, new UTF8Encoding(false));
        return dir;
    }

    protected static void Put(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    internal ClaudeCodeHost Host(string? configDir = null, string? activeAccountFolder = null, params string[] added) =>
        new(Profile, configDir, () => added, Path.Combine(Root, "appdata"), Path.Combine(Root, "external"), Path.Combine(Root, "legacy"),
            () => activeAccountFolder, ModSource);

    protected static string SettingsOf(string folder) => Path.Combine(folder, "settings.json");
}
