using System.Xml.Linq;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Pins the Store (MSIX) manifest's AppData virtualization opt-out. Without
/// <c>desktop6:FileSystemWriteVirtualization=disabled</c> plus the
/// <c>unvirtualizedResources</c> capability, a packaged install writes new files under
/// %APPDATA%\Sanduhr into its private LocalCache, where Claude Code, sanduhr-mcp and the
/// statusline (all outside the package) can't see them. Dropping either one must break CI.
/// </summary>
public class PackageManifestTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";
    private static readonly XNamespace Virtualization = "http://schemas.microsoft.com/appx/manifest/virtualization/windows10";
    private static readonly XNamespace Rescap ="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static XDocument LoadManifest()
    {
        // tests/Sanduhr.Tests/bin/{cfg}/{tfm}/ -> windows-dotnet/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Package.appxmanifest"));
    }

    [Fact]
    public void Properties_disable_file_system_write_virtualization()
    {
        var properties = LoadManifest().Root!.Element(Foundation + "Properties");
        Assert.NotNull(properties);
        var fsv = properties!.Element(Desktop6 + "FileSystemWriteVirtualization");
        Assert.NotNull(fsv);
        Assert.Equal("disabled", fsv!.Value.Trim());
    }

    [Fact]
    public void Capabilities_declare_unvirtualizedResources_and_runFullTrust()
    {
        var names = LoadManifest().Root!
            .Element(Foundation + "Capabilities")!
            .Elements(Rescap + "Capability")
            .Select(c => (string?)c.Attribute("Name"))
            .ToList();
        Assert.Contains("unvirtualizedResources", names);
        Assert.Contains("runFullTrust", names);
    }

    [Theory]
    [InlineData("desktop6", "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6")]
    [InlineData("virtualization", "http://schemas.microsoft.com/appx/manifest/virtualization/windows10")]
    public void Virtualization_namespaces_are_bound_and_ignorable(string prefix, string uri)
    {
        var root = LoadManifest().Root!;
        Assert.Equal(prefix, root.GetPrefixOfNamespace(uri));
        var ignorable = ((string?)root.Attribute("IgnorableNamespaces") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(prefix, ignorable);
    }

    [Fact]
    public void Newer_Windows_unvirtualizes_only_the_two_Sanduhr_folders()
    {
        // Build 20348+ honors this list instead of the blanket desktop6 property, so the
        // opt-out stays scoped to the folders external readers need.
        var dirs = LoadManifest().Root!
            .Element(Foundation + "Properties")!
            .Element(Virtualization + "FileSystemWriteVirtualization")?
            .Element(Virtualization + "ExcludedDirectories")?
            .Elements(Virtualization + "ExcludedDirectory")
            .Select(e => e.Value.Trim())
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();
        Assert.NotNull(dirs);
        Assert.Equal(
            new[] { @"$(KnownFolder:LocalAppData)\Sanduhr", @"$(KnownFolder:RoamingAppData)\Sanduhr" },
            dirs);
    }

    [Fact]
    public void Floor_is_the_first_build_that_honors_the_opt_out()
    {
        // desktop6:FileSystemWriteVirtualization is honored from Windows 10 1903 (18362).
        // Below that the Store build would run virtualized and the external readers break.
        var tdf = LoadManifest().Root!
            .Element(Foundation + "Dependencies")!
            .Element(Foundation + "TargetDeviceFamily")!;
        Assert.Equal("10.0.18362.0", (string?)tdf.Attribute("MinVersion"));
    }
}
