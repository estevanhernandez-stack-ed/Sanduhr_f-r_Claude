using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// The meters mod ships from the Mac's source folder: the App project copies it into its output
/// as mods\sanduhr-meters\ and leaves tests, the repo's ignore file and Claude Code's generated
/// types out. Asserted on the project text and the source tree, so no App build is needed.
/// </summary>
public sealed class MetersModPayloadTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-dotnet", "src", "Sanduhr.App", "Sanduhr.App.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
    }

    private static string AppCsproj() => File.ReadAllText(Path.Combine(RepoRoot(), "windows-dotnet", "src", "Sanduhr.App", "Sanduhr.App.csproj"));

    private static string ModSource() => Path.Combine(RepoRoot(), "mac", "integrations", "mods", "sanduhr-meters");

    [Fact]
    public void The_source_folder_has_the_manifest_and_the_hooks()
    {
        Assert.True(File.Exists(Path.Combine(ModSource(), ".claude-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(ModSource(), "hooks", "hooks.json")));
    }

    [Fact]
    public void The_app_project_resolves_the_mod_path_from_its_own_folder()
    {
        // windows-dotnet\src\Sanduhr.App + ..\..\.. is the repo root.
        var appDir = Path.Combine(RepoRoot(), "windows-dotnet", "src", "Sanduhr.App");
        var resolved = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "mac", "integrations", "mods", "sanduhr-meters"));
        Assert.Equal(Path.GetFullPath(ModSource()), resolved);
        Assert.Contains(@"..\..\..\mac\integrations\mods\sanduhr-meters\**\*", AppCsproj());
    }

    [Fact]
    public void The_app_project_copies_the_mod_into_mods_sanduhr_meters_for_build_and_publish()
    {
        var text = AppCsproj();
        Assert.Contains(@"Link=""mods\sanduhr-meters\%(RecursiveDir)%(Filename)%(Extension)""", text);
        Assert.Contains(@"CopyToOutputDirectory=""PreserveNewest""", text);
        // Content items reach the publish output as well; a plain None/Copy item would not.
        Assert.Contains("<Content Include=\"..\\..\\..\\mac\\integrations\\mods\\sanduhr-meters", text);
    }

    [Theory]
    [InlineData(@"**\*.test.ts")]
    [InlineData(@"**\*.test.tsx")]
    [InlineData(@"sanduhr-meters\.gitignore")]
    [InlineData(@".claude-plugin\types\**")]
    public void The_app_project_leaves_out(string pattern)
    {
        var text = AppCsproj();
        var exclude = text[(text.IndexOf("Exclude=\"", text.IndexOf("sanduhr-meters", StringComparison.Ordinal), StringComparison.Ordinal) + 9)..];
        exclude = exclude[..exclude.IndexOf('"')];
        Assert.Contains(pattern, exclude);
    }

    [Fact]
    public void The_source_has_files_the_exclusions_are_for()
    {
        // If these ever vanish the exclusions above guard nothing.
        var files = Directory.EnumerateFiles(ModSource(), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(ModSource(), f)).ToList();
        Assert.Contains(files, f => f.EndsWith(".test.ts", StringComparison.Ordinal) || f.EndsWith(".test.tsx", StringComparison.Ordinal));
        Assert.Contains(".gitignore", files);
    }

    [Fact]
    public void Install_from_the_real_source_ships_everything_but_tests_and_ignore_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "sanduhr-payload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sut = new ClaudeCodeIntegrations(Path.Combine(root, "home"), Path.Combine(root, "Sanduhr"), ModSource());
            var path = sut.RefreshModFiles();
            Assert.NotNull(path);
            Assert.True(File.Exists(Path.Combine(path!, ".claude-plugin", "plugin.json")));
            Assert.True(File.Exists(Path.Combine(path!, "hooks", "hooks.json")));
            var shipped = Directory.EnumerateFiles(path!, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(path!, f)).ToList();
            Assert.DoesNotContain(shipped, f => f.Contains(".test.", StringComparison.Ordinal));
            Assert.DoesNotContain(".gitignore", shipped);
            Assert.DoesNotContain(shipped, f => f.StartsWith(@".claude-plugin\types", StringComparison.Ordinal));
            Assert.Matches(@"^v\d+\.\d+\.\d+-\d{14}$", Path.GetFileName(Path.GetDirectoryName(path)!));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
