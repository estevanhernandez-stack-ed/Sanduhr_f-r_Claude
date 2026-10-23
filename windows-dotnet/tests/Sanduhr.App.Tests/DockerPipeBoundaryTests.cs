using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Sanduhr.App.Desk;

namespace Sanduhr.App.Tests;

/// <summary>The rule that does not move (spec ci-watchers-3.9 section 2): Sanduhr lists containers and
/// follows a runner's log, and makes no other Docker call. Inspect returns a container's environment,
/// where a runner keeps its registration token.</summary>
public class DockerPipeBoundaryTests
{
    private static string AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private const string Id = "1111111111111111111111111111111111111111111111111111111111111111";

    [Fact]
    public void TheTwoCalls()
    {
        Assert.Equal("v1.43/containers/json", DockerPipe.PathFor(DockerPipe.Call.ListContainers));
        Assert.Equal($"v1.43/containers/{Id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200",
            DockerPipe.PathFor(DockerPipe.Call.FollowLogs, Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1111111111111111111111111111111111111111111111111111111111111111/json?")]
    [InlineData("../json")]
    [InlineData("1111111111111111111111111111111111111111111111111111111111111111\n")]
    public void ALogsCallTakesOnlyAFullContainerId(string? id) =>
        Assert.Throws<ArgumentException>(() => DockerPipe.PathFor(DockerPipe.Call.FollowLogs, id));

    [Fact]
    public void TheCallEnumHasExactlyTwoValues() =>
        Assert.Equal(["ListContainers", "FollowLogs"], Enum.GetNames<DockerPipe.Call>());

    [Fact]
    public void TheSourceMakesNoOtherCall()
    {
        var source = File.ReadAllText(Path.Combine(AppSource(), "Desk", "DockerPipe.cs"));
        Assert.Equal(2, Regex.Matches(source, "containers/").Count);
        Assert.DoesNotMatch(@"containers/\{[^}]*\}/json", source);
        foreach (var banned in new[] { "/exec", "/attach", "/archive", "/start", "/stop", "/kill", "/inspect", "/export",
                     "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync", "SendAsync", "HttpMethod." })
            Assert.DoesNotContain(banned, source);

        // Structure, not words: the client has three uses, GetAsync only ever takes a PathFor result,
        // and the only /json is the list template.
        var members = Regex.Matches(source, @"_http\.(\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(new HashSet<string> { "GetAsync", "DefaultRequestHeaders", "Dispose" }, members);
        Assert.Empty(Regex.Matches(source, @"GetAsync\((?!PathFor\(Call\.)"));
        Assert.Single(Regex.Matches(source, "/json"));
        Assert.Contains("containers/json", source);
        foreach (var banned in new[] { "new HttpMethod", "HttpRequestMessage" })
            Assert.DoesNotContain(banned, source);
    }

    [Fact]
    public void NothingElseInSrcTalksToDocker()
    {
        var src = Path.GetDirectoryName(AppSource())!;
        var sep = Path.DirectorySeparatorChar;
        var tokens = new[] { "docker_engine", "containers/", "NamedPipeClientStream", "ConnectCallback", ":2375", ":2376" };
        var projects = new[] { "Sanduhr.App", "Sanduhr.Core", "Sanduhr.Mcp" }.Select(d => Path.Combine(src, d)).ToList();
        // A moved project must fail this test, not narrow it.
        foreach (var project in projects) Assert.True(Directory.Exists(project), $"{project} is missing: the boundary scan would go vacuous.");
        var exempt = "src/Sanduhr.App/Desk/DockerPipe.cs";
        var others = projects
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => Path.GetRelativePath(Path.GetDirectoryName(src)!, f).Replace(sep, '/') != exempt)
            .Where(f => File.ReadAllText(f) is var s && tokens.Any(s.Contains))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(others);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(src)!, exempt)), "The exempt file moved: update the path above.");
    }

    [Fact]
    public void DeniedIsTheGroupProblemAndEverythingElseIsDown()
    {
        Assert.Equal(DockerFailure.Denied, DockerPipe.Classify(new HttpRequestException("x", new UnauthorizedAccessException())));
        Assert.Equal(DockerFailure.Denied, DockerPipe.Classify(new UnauthorizedAccessException()));
        Assert.Equal(DockerFailure.Down, DockerPipe.Classify(new HttpRequestException("x", new TimeoutException())));
        Assert.Equal(DockerFailure.Down, DockerPipe.Classify(new IOException()));
        // An HTTP status answer (EnsureSuccessStatusCode) is Docker answering, not Docker being down.
        Assert.Equal(DockerFailure.Refused, DockerPipe.Classify(new HttpRequestException("x", null, System.Net.HttpStatusCode.BadRequest)));
        Assert.Equal(DockerFailure.Refused, DockerPipe.Classify(new HttpRequestException("x", null, System.Net.HttpStatusCode.InternalServerError)));
    }
}
