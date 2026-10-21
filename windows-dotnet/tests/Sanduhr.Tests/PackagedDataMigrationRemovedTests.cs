using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 3.4.3's LocalCache-to-AppData copy never ships (plan B, B.4): the Store build's private data correctly
/// stays in package storage, and nothing at startup reaches into a LocalCache folder.
/// </summary>
public class PackagedDataMigrationRemovedTests
{
    [Fact]
    public void The_PackagedDataMigration_type_is_gone_from_Core()
        => Assert.Null(typeof(Paths).Assembly.GetType("Sanduhr.Core.PackagedDataMigration"));

    [Fact]
    public void App_startup_does_not_name_the_migration_or_a_LocalCache_copy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "App.xaml.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "App.xaml.cs"));
        Assert.DoesNotContain("PackagedDataMigration", source);
        Assert.DoesNotContain("LocalCache", source);
    }
}
