using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: settings.json "update_check" is on when absent or not a bool, and both values round-trip.</summary>
[Collection("SettingsWindow")]
public sealed class UpdateCheckKeyTests : AppWindowFixture
{
    [Fact]
    public void Absent_is_on_and_off_round_trips()
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        Assert.True(store.LoadUpdateCheck());

        store.SaveUpdateCheck(false);
        Assert.False(new SettingsStore(paths).LoadUpdateCheck());
        Assert.False((bool?)JsonNode.Parse(File.ReadAllText(paths.SettingsFile))!["update_check"]);

        store.SaveUpdateCheck(true);
        Assert.True(new SettingsStore(paths).LoadUpdateCheck());
    }

    [Fact]
    public void A_non_bool_value_reads_as_on()
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFile)!);
        File.WriteAllText(paths.SettingsFile, "{\"update_check\":\"off\"}");
        Assert.True(new SettingsStore(paths).LoadUpdateCheck());
        File.WriteAllText(paths.SettingsFile, "{\"update_check\":0}");
        Assert.True(new SettingsStore(paths).LoadUpdateCheck());
    }
}
