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

    private static string FontsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "mac", "Resources", "Fonts")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "mac", "Resources", "Fonts");
    }

    /// <summary>The OpenType 'name' table as (nameId, string) pairs: platform 3 is UTF-16BE, platform 1 Latin-1.</summary>
    private static List<(int Id, string Text)> NameRecords(byte[] f)
    {
        static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

        int tables = U16(f, 4);
        int nameOffset = -1;
        for (int i = 0; i < tables; i++)
        {
            int rec = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(f, rec, 4) == "name") nameOffset = (int)U32(f, rec + 8);
        }
        Assert.True(nameOffset >= 0, "no name table");
        int count = U16(f, nameOffset + 2);
        int strings = nameOffset + U16(f, nameOffset + 4);
        var result = new List<(int, string)>();
        for (int i = 0; i < count; i++)
        {
            int r = nameOffset + 6 + i * 12;
            int platform = U16(f, r), id = U16(f, r + 6), len = U16(f, r + 8), off = U16(f, r + 10);
            var text = platform switch
            {
                3 => System.Text.Encoding.BigEndianUnicode.GetString(f, strings + off, len),
                1 => System.Text.Encoding.Latin1.GetString(f, strings + off, len),
                _ => null,
            };
            if (text is not null) result.Add((id, text));
        }
        return result;
    }

    [Fact]
    public void EveryFontFile_CarriesTheListedCopyrightLicenseAndFamily()
    {
        var folder = FontsFolder();
        foreach (var font in ThirdPartyNotices.Fonts)
        {
            Assert.NotEmpty(font.Files);
            foreach (var file in font.Files)
            {
                var names = NameRecords(File.ReadAllBytes(Path.Combine(folder, file)));
                Assert.Contains(names, n => n.Id == 0 && n.Text == font.Copyright);
                Assert.Contains(names, n => n.Id == 13 && n.Text == font.License);
                Assert.Contains(names, n => n.Id == 1 && n.Text == font.Name);
            }
        }
    }

    [Fact]
    public void EveryBundledFontFile_IsListedExactlyOnce()
    {
        var listed = ThirdPartyNotices.Fonts.SelectMany(f => f.Files).ToList();
        foreach (var file in BundledFonts.FileNames)
            Assert.Single(listed, l => l == file);
        Assert.Equal(BundledFonts.FileNames.Count, listed.Count);
    }

    [Fact]
    public void EveryPackage_HasALicense_AndAnHttpsPage() =>
        Assert.All(ThirdPartyNotices.Packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.License), p.Name);
            Assert.StartsWith("https://", p.Url);
        });
}
