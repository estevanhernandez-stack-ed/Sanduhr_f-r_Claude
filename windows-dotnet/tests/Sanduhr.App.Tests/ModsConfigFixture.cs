using System.IO;
using System.Text;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>A scratch profile with Claude Code folders in it, for the Mods &amp; Config page. Never touches the real profile or AppData.</summary>
public abstract class ModsConfigFixture : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "sanduhr-modspage-" + Guid.NewGuid().ToString("N")[..10]);
    protected string Profile => Path.Combine(Root, "profile");

    protected ModsConfigFixture()
    {
        Directory.CreateDirectory(Profile);
        ModsConfigViewModel.ForgetSessionChoice();
    }

    public void Dispose()
    {
        ModsConfigViewModel.ForgetSessionChoice();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }

    /// <summary>A Claude Code home (it has a projects folder) with the given settings.json text.</summary>
    protected string Home(string name, string? settings = null)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        if (settings is not null) File.WriteAllText(Path.Combine(dir, "settings.json"), settings, new UTF8Encoding(false));
        return dir;
    }

    protected void Put(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    protected ModsConfigViewModel Page(string? configDir = null, string? activeAccountFolder = null, params string[] added) =>
        new(new ModsConfigHost(Profile, configDir, () => added,
            Path.Combine(Root, "appdata"), Path.Combine(Root, "external"), Path.Combine(Root, "legacy"),
            () => activeAccountFolder));
}
