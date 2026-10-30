using System.Diagnostics;
using System.IO;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A throwaway Desktop for drawer tests under the temp folder (never the real Desktop): Desktop, Public and Desktop\Drawers.</summary>
internal sealed class DrawerSandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sanduhr-drawers-" + Guid.NewGuid().ToString("N")[..8]);
    public string Desktop => Path.Combine(Root, "Desktop");
    public string Public => Path.Combine(Root, "Public");
    public string Drawers => Path.Combine(Desktop, "Drawers");
    public DrawerRoots Roots => new(Desktop, Public, Drawers);

    public DrawerSandbox()
    {
        Directory.CreateDirectory(Desktop);
        Directory.CreateDirectory(Public);
    }

    public string File(string relative, string text = "x")
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, text);
        return p;
    }

    public string Dir(string relative) => Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;

    /// <summary>A junction (no administrator needed), or false where mklink is not available.</summary>
    public bool Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a locked test file; the temp folder is the OS's */ }
    }
}
