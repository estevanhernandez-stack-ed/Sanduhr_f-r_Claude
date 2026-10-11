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
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

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

    [Fact]
    public void Desktop6_namespace_is_ignorable()
    {
        var root = LoadManifest().Root!;
        var prefix = root.GetPrefixOfNamespace(Desktop6);
        Assert.Equal("desktop6", prefix);
        var ignorable = ((string?)root.Attribute("IgnorableNamespaces") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("desktop6", ignorable);
    }
}
