using System.Text.RegularExpressions;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>About's third-party notices list every package the App ships, and only those.</summary>
public class ThirdPartyNoticesTests
{
    private static string AppProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Sanduhr.App.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Sanduhr.App.csproj");
    }

    /// <summary>The App's PackageReferences, minus build-only ones (PrivateAssets all), which ship nothing.</summary>
    private static IReadOnlyList<string> ShippedPackages()
    {
        var xml = File.ReadAllText(AppProject());
        var shipped = new List<string>();
        foreach (Match m in Regex.Matches(xml, "<PackageReference Include=\"([^\"]+)\"[^>]*?(/>|>(.*?)</PackageReference>)",
                     RegexOptions.Singleline))
        {
            if (m.Groups[3].Value.Contains("<PrivateAssets>all</PrivateAssets>", StringComparison.Ordinal))
                continue;
            shipped.Add(m.Groups[1].Value);
        }
        return shipped;
    }

    [Fact]
    public void EveryShippedPackage_IsListed_AndNothingElse()
    {
        var shipped = ShippedPackages();
        Assert.NotEmpty(shipped);
        Assert.Equal(shipped.OrderBy(p => p, StringComparer.Ordinal),
            ThirdPartyNotices.Packages.Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryPackage_HasALicense_AndAnHttpsPage() =>
        Assert.All(ThirdPartyNotices.Packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.License), p.Name);
            Assert.StartsWith("https://", p.Url);
        });
}
