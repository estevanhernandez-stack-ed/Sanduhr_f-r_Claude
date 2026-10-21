using System.Xml.Linq;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Pins the Store (MSIX) manifest's plan B shape. Microsoft denied <c>rescap:unvirtualizedResources</c>
/// (2026-10-20), so 3.5.0 declares <c>runFullTrust</c> only, leaves AppData virtualized, and keeps every
/// file an outside process reads in %USERPROFILE%\.sanduhr. Bringing back the opt-out, a second
/// capability or the virtualization namespaces must break CI.
/// </summary>
public class PackageManifestTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private const string Desktop6Uri = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";
    private const string VirtualizationUri = "http://schemas.microsoft.com/appx/manifest/virtualization/windows10";

    private static XDocument LoadManifest()
    {
        // tests/Sanduhr.Tests/bin/{cfg}/{tfm}/ -> windows-dotnet/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Package.appxmanifest"));
    }

    private static List<XElement> Capabilities()
        => LoadManifest().Root!.Element(Foundation + "Capabilities")!.Elements().ToList();

    [Fact]
    public void No_unvirtualizedResources_capability()
    {
        var names = Capabilities().Select(c => (string?)c.Attribute("Name")).ToList();
        Assert.DoesNotContain("unvirtualizedResources", names);
    }

    [Fact]
    public void RunFullTrust_is_the_only_capability()
    {
        // Any namespace (rescap, uap, device...) counts: one capability, and it is runFullTrust.
        var caps = Capabilities();
        var single = Assert.Single(caps);
        Assert.Equal("Capability", single.Name.LocalName);
        Assert.Equal("runFullTrust", (string?)single.Attribute("Name"));
    }

    [Fact]
    public void Properties_declare_no_file_system_write_virtualization()
    {
        var properties = LoadManifest().Root!.Element(Foundation + "Properties");
        Assert.NotNull(properties);
        Assert.DoesNotContain(properties!.Descendants(), e => e.Name.LocalName == "FileSystemWriteVirtualization");
        Assert.DoesNotContain(properties.Descendants(), e => e.Name.LocalName == "ExcludedDirectories");
        Assert.DoesNotContain(LoadManifest().Descendants(), e => e.Name.LocalName == "FileSystemWriteVirtualization");
    }

    [Theory]
    [InlineData("desktop6", Desktop6Uri)]
    [InlineData("virtualization", VirtualizationUri)]
    public void Virtualization_namespaces_are_not_declared(string prefix, string uri)
    {
        var root = LoadManifest().Root!;
        Assert.Null(root.GetPrefixOfNamespace(uri));
        Assert.DoesNotContain(root.Attributes().Where(a => a.IsNamespaceDeclaration), a => a.Value == uri);
        var ignorable = ((string?)root.Attribute("IgnorableNamespaces") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(prefix, ignorable);
    }

    [Fact]
    public void Floor_is_Windows_10_1809()
    {
        var tdf = LoadManifest().Root!
            .Element(Foundation + "Dependencies")!
            .Element(Foundation + "TargetDeviceFamily")!;
        Assert.Equal("10.0.17763.0", (string?)tdf.Attribute("MinVersion"));
    }
}
