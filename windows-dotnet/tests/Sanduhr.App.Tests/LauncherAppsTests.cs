using System.IO;
using System.Text;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Start menu through the real shell. These run on any Windows box: they assert only what holds for whatever
/// apps it has (the list passes the Launcher's own gate, the first entry resolves and has an icon, an unknown ID is
/// absent). With <c>SANDUHR_LAUNCHER_DUMP</c> set to a file path the list is also written there for a look.
/// </summary>
public class LauncherAppsTests
{
    [Fact]
    public void TheStartMenuListPassesTheGateAndResolves()
    {
        using var apps = new LauncherApps();
        var list = apps.Enumerate();

        Assert.All(list, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.True(LauncherTarget.Validate(LauncherKind.App, a.Aumid).Ok, a.Aumid);
        });
        Assert.Equal(list.Count, list.Select(a => a.Aumid).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(a => a.Aumid), list.Select(a => a.Aumid));

        if (Environment.GetEnvironmentVariable("SANDUHR_LAUNCHER_DUMP") is { Length: > 0 } dump)
        {
            var sb = new StringBuilder();
            foreach (var a in list)
            {
                var icon = apps.Icon(a.Aumid, 64);
                sb.AppendLine($"{a.Name}\t{a.Aumid}\t{(icon is null ? "no icon" : $"{icon.PixelWidth}x{icon.PixelHeight} {icon.Format}")}\t{(apps.Exists(a.Aumid) ? "exists" : "MISSING")}");
            }
            File.WriteAllText(dump, sb.ToString());
        }

        if (list.Count == 0) return;   // a bare machine has nothing to resolve
        Assert.True(apps.Exists(list[0].Aumid));
    }

    [Fact]
    public void DesktopProgramsAreListedByTheirPathFormAndOpenedOnlyByListing()
    {
        using var apps = new LauncherApps();
        var list = apps.Enumerate();
        var paths = list.Where(a => LauncherTarget.IsPathForm(a.Aumid)).ToList();
        if (Environment.GetEnvironmentVariable("SANDUHR_LAUNCHER_COUNT") is { Length: > 0 } file)
            File.WriteAllText(file, $"total={list.Count} pathForm={paths.Count}");
        Assert.All(paths, a => Assert.EndsWith(".exe", a.Aumid, StringComparison.OrdinalIgnoreCase));
        if (paths.Count > 0) Assert.True(apps.Exists(paths[0].Aumid));

        // Well-formed, but not in the listing: refused even if the shell can resolve it.
        Assert.False(apps.Exists(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe.NotListed.exe"));
        Assert.False(apps.Exists(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\..\cmd.exe"));
        Assert.False(apps.Exists(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.bat"));
    }

    [Fact]
    public void AnUnknownOrRefusedIdIsAbsent()
    {
        using var apps = new LauncherApps();
        Assert.False(apps.Exists("Sanduhr.Test.NoSuchApp_0000000000000!Nothing"));
        Assert.False(apps.Exists(@"C:\Windows\System32\cmd.exe"));   // refused before the shell is asked
        Assert.False(apps.Exists("a b"));
        Assert.Null(apps.Icon(@"C:\Windows\System32\cmd.exe", 64));
    }

    [Fact]
    public void OpeningARefusedTargetNeverReachesTheShell()
    {
        var calls = new List<string>();
        bool Exists(string id) { calls.Add(id); return true; }
        Assert.Equal(LauncherApps.OpenResult.Refused, LauncherApps.Open(new LauncherButton(LauncherKind.App, @"C:\Windows\notepad.exe"), Exists));
        Assert.Equal(LauncherApps.OpenResult.Refused, LauncherApps.Open(new LauncherButton(LauncherKind.Link, "http://example.com"), Exists));
        Assert.Equal(LauncherApps.OpenResult.Refused, LauncherApps.Open(new LauncherButton(LauncherKind.Link, "javascript:alert(1)"), Exists));
        Assert.Equal(LauncherApps.OpenResult.Refused, LauncherApps.Open(new LauncherButton(LauncherKind.Link, "file:///C:/Windows/notepad.exe"), Exists));
        Assert.Empty(calls);
    }

    [Fact]
    public void AnAppTheStartMenuNoLongerHasIsMissingNotLaunched()
    {
        Assert.Equal(LauncherApps.OpenResult.Missing,
            LauncherApps.Open(new LauncherButton(LauncherKind.App, "Gone.App_0123456789abc!App"), _ => false));
    }
}
