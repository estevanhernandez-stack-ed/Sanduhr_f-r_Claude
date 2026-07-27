using System.Text.RegularExpressions;

namespace Sanduhr.Mcp.Tests;

/// <summary>
/// Pins the trust boundary (design review must-fix #11): sanduhr-mcp must not
/// be ABLE to touch credentials or the network transport. The csproj source-links
/// an allowlisted Core slice instead of referencing Sanduhr.Core; widening the
/// list or adding a project reference is a design decision that must break CI,
/// not a quiet convenience.
/// </summary>
public class TrustBoundaryTests
{
    private static string McpCsprojPath()
    {
        // tests/Sanduhr.Mcp.Tests/bin/{cfg}/{tfm}/ -> windows-dotnet/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.Mcp", "Sanduhr.Mcp.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.Mcp", "Sanduhr.Mcp.csproj");
    }

    [Fact]
    public void Mcp_project_never_references_another_project()
    {
        string csproj = File.ReadAllText(McpCsprojPath());
        Assert.DoesNotContain("<ProjectReference", csproj);
        Assert.DoesNotContain("<PackageReference", csproj);
    }

    [Fact]
    public void Source_link_list_is_exactly_the_allowlisted_slice()
    {
        string csproj = File.ReadAllText(McpCsprojPath());
        var linked = Regex.Matches(csproj, "Compile Include=\"[^\"]*\\\\([A-Za-z0-9.]+\\.cs)\"")
            .Select(m => m.Groups[1].Value)
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(
            new[] { "CcLogReader.cs", "Pacing.cs", "SnapshotContract.cs", "TierModel.cs" },
            linked);
    }

    [Fact]
    public void Credential_types_do_not_exist_in_the_mcp_assembly()
    {
        var mcp = typeof(Sanduhr.Mcp.McpServer).Assembly;
        var typeNames = mcp.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("CredentialStore", typeNames);
        Assert.DoesNotContain("WindowsCredentialManager", typeNames);
        Assert.DoesNotContain("ClaudeApiClient", typeNames);
        Assert.DoesNotContain("WebView2ApiClient", typeNames);
    }
}
