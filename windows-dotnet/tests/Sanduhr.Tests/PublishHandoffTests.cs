using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class PublishHandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static string RequestJson(string id, string date, DateTimeOffset requestedAt)
        => new JsonObject
        {
            ["id"] = id,
            ["date"] = date,
            ["requested_at"] = requestedAt.ToString("o"),
        }.ToJsonString();

    [Fact]
    public void File_names_are_the_ones_the_mcp_server_mirrors()
    {
        Assert.Equal("publish-request.json", PublishHandoff.RequestFileName);
        Assert.Equal("publish-result.json", PublishHandoff.ResultFileName);
    }

    [Fact]
    public void Missing_request_is_null()
    {
        using var tmp = new TempDir();
        Assert.Null(PublishHandoff.TryReadRequest(Path.Combine(tmp.Path, "publish-request.json"), Now));
    }

    [Fact]
    public void Well_formed_request_parses()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "publish-request.json");
        File.WriteAllText(path, RequestJson("abc", "2026-09-12", Now.AddMinutes(-1)));

        var r = PublishHandoff.TryReadRequest(path, Now);

        Assert.NotNull(r);
        Assert.Equal("abc", r!.Id);
        Assert.Equal(new DateOnly(2026, 9, 12), r.Date);
        Assert.True(File.Exists(path), "reading must not consume the request — the result write does");
    }

    [Fact]
    public void Malformed_or_expired_requests_are_deleted_and_ignored()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "publish-request.json");

        File.WriteAllText(path, "{ nope");
        Assert.Null(PublishHandoff.TryReadRequest(path, Now));
        Assert.False(File.Exists(path));

        File.WriteAllText(path, RequestJson("old", "2026-09-01", Now.AddDays(-2)));
        Assert.Null(PublishHandoff.TryReadRequest(path, Now));
        Assert.False(File.Exists(path));

        File.WriteAllText(path, RequestJson("bad-date", "09/12/2026", Now));
        Assert.Null(PublishHandoff.TryReadRequest(path, Now));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_result_lands_atomically_and_clears_the_request()
    {
        using var tmp = new TempDir();
        var req = Path.Combine(tmp.Path, "publish-request.json");
        var res = Path.Combine(tmp.Path, "publish-result.json");
        File.WriteAllText(req, RequestJson("abc", "2026-09-12", Now));

        PublishHandoff.WriteResult(res, req, "abc", new JsonObject { ["status"] = "ok", ["http_status"] = 200 });

        Assert.False(File.Exists(req));
        Assert.False(File.Exists(res + ".tmp"));
        var written = (JsonObject)JsonNode.Parse(File.ReadAllText(res))!;
        Assert.Equal("abc", (string?)written["id"]);
        Assert.Equal("ok", (string?)written["result"]!["status"]);
        Assert.NotNull(written["completed_at"]);
    }
}
