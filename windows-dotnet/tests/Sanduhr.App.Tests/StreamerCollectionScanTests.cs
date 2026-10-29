using System.IO;
using System.Text.RegularExpressions;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 7 (preflight P6): StreamerMode is process-wide, so a test that sets or reads it must run in the
/// SettingsWindow collection, or it races the classes that run in parallel. The six classes that read the mask through the
/// Mods, Companion and Desk pages are named too: they carry the collection from slice 7 on.
/// </summary>
public sealed class StreamerCollectionScanTests
{
    private const string Collection = "[Collection(\"SettingsWindow\")]";

    private static readonly string[] ReadMask =
    [
        "WidgetCatalogServiceTests", "CompanionPieceTests", "DeskFollowThemeTests", "DeskReviewPlacementTests",
        "OledCareTests", "WidgetReviewServiceTests",
    ];

    private static string TestDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sanduhr.App.Tests.csproj"))
               && !File.Exists(Path.Combine(dir.FullName, "tests", "Sanduhr.App.Tests", "Sanduhr.App.Tests.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.Exists(Path.Combine(dir!.FullName, "Sanduhr.App.Tests.csproj"))
            ? dir.FullName : Path.Combine(dir.FullName, "tests", "Sanduhr.App.Tests");
    }

    private static readonly string[] Exempt = ["AppWindowFixture.cs", "StreamerCollectionScanTests.cs"];

    [Fact]
    public void Every_test_file_that_names_StreamerMode_is_in_the_SettingsWindow_collection()
    {
        var bad = new List<string>();
        int seen = 0;
        foreach (var file in Directory.EnumerateFiles(TestDir(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || Exempt.Contains(Path.GetFileName(file)))
                continue;
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\bStreamerMode\b"))
                continue;
            seen++;
            if (!text.Contains(Collection))
                bad.Add(Path.GetFileName(file));
        }
        Assert.True(seen >= 1, "the scan found no file that names StreamerMode, so it is not looking where it should");
        Assert.Empty(bad);
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public void The_classes_that_read_the_mask_are_in_the_collection(string name)
    {
        var file = Directory.EnumerateFiles(TestDir(), name + ".cs", SearchOption.AllDirectories).Single();
        var text = File.ReadAllText(file);
        var m = Regex.Match(text, @"(?<attrs>(?:\[[^\]\r\n]+\]\s*)*)(?:public\s+)?(?:sealed\s+)?class\s+" + name + @"\b");
        Assert.True(m.Success, name);
        Assert.Contains(Collection, m.Groups["attrs"].Value);
    }

    public static IEnumerable<object[]> Classes() => ReadMask.Select(c => new object[] { c });
}
