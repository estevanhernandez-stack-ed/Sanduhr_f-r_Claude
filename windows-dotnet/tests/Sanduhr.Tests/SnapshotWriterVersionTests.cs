using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>writer_version must be the WIDGET's version, not Core's unversioned
/// "1.0.0" — the tell behind the 2026-09-13 stale-snapshot confusion (a dev
/// build's file mistaken for the installed release's).</summary>
public class SnapshotWriterVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Injected_writer_version_is_stamped_on_ok_and_error_writes()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        var writer = new SnapshotWriter(path, writerVersion: "3.4.0");

        writer.WriteOk(new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = 1.0 } }, "Max", "Este", Now);
        Assert.Equal("3.4.0", (string?)JsonNode.Parse(File.ReadAllText(path))!["writer_version"]);

        writer.WriteError(SnapshotContract.ErrorNetwork, "Max", "Este", Now.AddMinutes(5));
        Assert.Equal("3.4.0", (string?)JsonNode.Parse(File.ReadAllText(path))!["writer_version"]);
    }

    [Fact]
    public void Default_writer_version_is_still_a_dotted_version_string()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        new SnapshotWriter(path).WriteOk(new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = 1.0 } }, null, null, Now);
        var v = (string?)JsonNode.Parse(File.ReadAllText(path))!["writer_version"];
        Assert.Matches(@"^\d+\.\d+\.\d+$", v);
    }
}
