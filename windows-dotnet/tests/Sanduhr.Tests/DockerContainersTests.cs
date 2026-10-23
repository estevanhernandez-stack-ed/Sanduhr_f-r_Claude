using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The container list (spec ci-watchers-3.9 sections 2, 3 and 5): only the fields the list
/// call returns, runners picked by image name, and the status line under the switch.</summary>
public class DockerContainersTests
{
    private const string Id1 = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Id2 = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string Id3 = "3333333333333333333333333333333333333333333333333333333333333333";

    // The shape of GET /v1.43/containers/json on Neb, 2026-10-23, trimmed to the fields Sanduhr reads.
    private static readonly string Json = $$$"""
        [
          {"Id":"{{{Id1}}}","Names":["/ci-runners-alpha-1-1"],"Image":"dunder-gh-runner:2.337.0","State":"running",
           "Labels":{"com.docker.compose.project":"ci-runners","com.docker.compose.service":"alpha-1"}},
          {"Id":"{{{Id2}}}","Names":["/ci-runners-egress-proxy-1"],"Image":"dunder-egress-proxy:1","State":"running",
           "Labels":{"com.docker.compose.service":"egress-proxy"}},
          {"Id":"{{{Id3}}}","Names":["/my-runner"],"Image":"ghcr.io/actions/actions-runner:latest","State":"exited","Labels":{}}
        ]
        """;

    [Fact]
    public void ParsesTheListCall()
    {
        var all = DockerContainers.Parse(Encoding.UTF8.GetBytes(Json));
        Assert.Equal(
            new[]
            {
                new DockerContainer(Id1, "dunder-gh-runner:2.337.0", "alpha-1", true, "ci-runners-alpha-1-1"),
                new DockerContainer(Id2, "dunder-egress-proxy:1", "egress-proxy", true, "ci-runners-egress-proxy-1"),
                new DockerContainer(Id3, "ghcr.io/actions/actions-runner:latest", "my-runner", false, "my-runner"),
            },
            all);
    }

    [Fact]
    public void AnIdWithATrailingNewlineIsSkipped()
    {
        var json = $$$"""[{"Id":"{{{Id1}}}\n","Image":"dunder-gh-runner:1","State":"running","Labels":{}}]""";
        Assert.Empty(DockerContainers.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void RunnersAreRunningContainersWithARunnerImage() =>
        Assert.Equal(["alpha-1"], DockerContainers.Runners(DockerContainers.Parse(Encoding.UTF8.GetBytes(Json))).Select(c => c.Name));

    [Fact]
    public void ReplicasThatShareALabelShowTheirContainerNames()
    {
        var json = $$$"""
            [
              {"Id":"{{{Id1}}}","Names":["/ci-runner-1"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}},
              {"Id":"{{{Id2}}}","Names":["/ci-runner-2"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}},
              {"Id":"{{{Id3}}}","Names":["/ci-other-1"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"other"}}
            ]
            """;
        var runners = DockerContainers.Runners(DockerContainers.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(["ci-runner-1", "ci-runner-2", "other"], runners.Select(c => c.Name));
        Assert.Equal(["ci-runner-1", "ci-runner-2", "ci-other-1"], runners.Select(c => c.Key));
    }

    [Fact]
    public void TheKeyIsTheFirstContainerNameOrTheId()
    {
        var json = $$$"""[{"Id":"{{{Id1}}}","Image":"gh-runner","State":"running","Labels":{}}]""";
        Assert.Equal(Id1, DockerContainers.Parse(Encoding.UTF8.GetBytes(json))[0].Key);
    }

    [Theory]
    [InlineData("dunder-gh-runner:2.337.0", true)]
    [InlineData("dunder-gh-runner:2.338.0", true)]
    [InlineData("ghcr.io/actions/actions-runner:2.321.0", true)]
    [InlineData("myoung34/github-runner:latest", true)]
    [InlineData("summerwind/actions-runner@sha256:0123abcd", true)]
    [InlineData("localhost:5000/team/GH-Runner", true)]
    [InlineData("dunder-egress-proxy:1", false)]
    [InlineData("postgres:16", false)]
    [InlineData("registry.gh-runner.example.com/postgres:16", false)] // only the image's own name counts
    [InlineData("", false)]
    public void RunnerImages(string image, bool expected) => Assert.Equal(expected, DockerContainers.IsRunnerImage(image));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"Id\":\"short\",\"Image\":\"dunder-gh-runner:1\",\"State\":\"running\"}]")]
    public void MalformedIsEmpty(string json) => Assert.Empty(DockerContainers.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ANameIsOneShortLine()
    {
        var json = $$$"""[{"Id":"{{{Id1}}}","Names":["/x"],"Image":"gh-runner","State":"running","Labels":{"com.docker.compose.service":"{{{new string('n', 80)}}}"}}]""";
        Assert.True(DockerContainers.Parse(Encoding.UTF8.GetBytes(json))[0].Name.Length <= 40);
    }

    [Fact]
    public void StatusLines()
    {
        Assert.Equal("", RunnerStatus.Off.Text);
        Assert.Equal("Watching 1 runner: alpha-1", new RunnerStatus(RunnerStatusKind.Watching, ["alpha-1"]).Text);
        Assert.Equal("Watching 2 runners: echo-1, echo-2", new RunnerStatus(RunnerStatusKind.Watching, ["echo-1", "echo-2"]).Text);
        Assert.Equal("No runner containers are running.", new RunnerStatus(RunnerStatusKind.NoRunners, []).Text);
        Assert.Equal("Docker isn't running.", new RunnerStatus(RunnerStatusKind.DockerDown, []).Text);
        Assert.Equal("Docker answered but refused the request.", new RunnerStatus(RunnerStatusKind.DockerRefused, []).Text);
        Assert.Equal("Docker refused the connection. Your Windows account needs to be in the docker-users group.",
            new RunnerStatus(RunnerStatusKind.DockerDenied, []).Text);
    }

    [Fact]
    public void ALongRunnerListIsClipped()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"runner-number-{i}").ToList();
        var text = new RunnerStatus(RunnerStatusKind.Watching, names).Text;
        Assert.StartsWith("Watching 30 runners: runner-number-1, ", text);
        Assert.True(text.Length <= RunnerStatus.MaxText);
        Assert.EndsWith("…", text);
    }
}
