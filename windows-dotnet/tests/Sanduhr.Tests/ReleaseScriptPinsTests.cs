using System.Xml.Linq;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 5.0 spec 7: the release scripts are read as text and pinned where a quiet edit would ship the wrong thing.
/// The x64 Velopack channel is the one that matters most: every x64 install reads releases.win.json.
/// </summary>
public class ReleaseScriptPinsTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Script(string name) => File.ReadAllText(Path.Combine(Root(), "scripts", name));

    /// <summary>The script's lines that are not comments.</summary>
    internal static List<string> Code(string text) =>
        [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith('#'))];

    [Fact]
    public void Msix_script_accepts_exactly_the_two_runtimes()
        => Assert.Contains(Code(Script("build-msix.ps1")), l => l.Trim() == "[ValidateSet('win-x64', 'win-arm64')]");

    [Fact]
    public void Msix_script_stamps_the_architecture_into_the_staged_manifest()
        => Assert.Contains(Code(Script("build-msix.ps1")), l => l.Trim() == "$m.Package.Identity.ProcessorArchitecture = $arch");

    [Fact]
    public void Msix_script_names_the_package_with_its_architecture()
        => Assert.Contains(Code(Script("build-msix.ps1")), l => l.Contains("\"Sanduhr-$flavor-$arch-v$ver.msix\"", StringComparison.Ordinal));

    [Fact]
    public void Msix_script_runs_the_architecture_gate()
        => Assert.Contains(Code(Script("build-msix.ps1")), l => l.Contains("check-architecture.ps1", StringComparison.Ordinal));

    [Fact]
    public void Velopack_script_accepts_exactly_the_two_runtimes()
        => Assert.Contains(Code(Script("build-velopack-release.ps1")), l => l.Trim() == "[ValidateSet('win-x64', 'win-arm64')]");

    [Fact]
    public void Velopack_script_names_a_runtime_and_a_channel_only_for_arm64()
    {
        // x64 must pack exactly as it always has: no --channel (it stays on Velopack's default, "win") and no --runtime.
        var code = Code(Script("build-velopack-release.ps1"));
        const string armOnly = "if ($Runtime -eq 'win-arm64') { $vpkArgs += @('--runtime', 'win-arm64', '--channel', 'win-arm64') }";
        var channel = Assert.Single(code, l => l.Contains("--channel", StringComparison.Ordinal));
        Assert.Equal(armOnly, channel.Trim());
        var runtime = Assert.Single(code, l => l.Contains("'--runtime'", StringComparison.Ordinal));
        Assert.Equal(armOnly, runtime.Trim());
    }

    [Fact]
    public void Velopack_script_runs_the_architecture_gate()
        => Assert.Contains(Code(Script("build-velopack-release.ps1")), l => l.Contains("check-architecture.ps1", StringComparison.Ordinal));

    [Fact]
    public void The_pack_id_is_frozen()
        => Assert.Contains(Code(Script("build-velopack-release.ps1")), l => l.Contains("'--packId',", StringComparison.Ordinal) && l.Contains("'626Labs.Sanduhr'", StringComparison.Ordinal));

    [Fact]
    public void The_update_checker_names_no_channel()
    {
        // An install follows the channel it was packaged with. Naming one here would move every install to it.
        var text = File.ReadAllText(Path.Combine(Root(), "src", "Sanduhr.App", "Updates", "UpdateChecker.cs"));
        Assert.Contains("new UpdateManager(source)", text);
        Assert.DoesNotContain("ExplicitChannel", text);
        Assert.DoesNotContain("UpdateOptions", text);
    }

    // The CI-only scripts start Sanduhr against the real data folders, stop running copies and install over them.
    // Their refusal off a runner is pinned as text, by line order, and never by running them: a failing test
    // would stop the owner's app or install over it.
    private const string CiOnlyRefusal = "$env:GITHUB_ACTIONS -ne 'true'";

    private static int IndexOf(List<string> code, Func<string, bool> match, string what)
    {
        var i = code.FindIndex(l => match(l));
        Assert.True(i >= 0, $"No line {what}.");
        return i;
    }

    [Fact]
    public void Install_switch_refuses_off_a_runner_before_it_stops_anything()
    {
        var code = Code(Script("check-install-switch.ps1"));
        var refusal = IndexOf(code, l => l.Contains(CiOnlyRefusal, StringComparison.Ordinal) && l.Contains("if (", StringComparison.Ordinal), "with the GITHUB_ACTIONS refusal");
        // Script level only: the definition and the calls inside functions are indented or start with 'function'.
        var firstStop = IndexOf(code, l => l.StartsWith("Stop-Sanduhr", StringComparison.Ordinal), "calling Stop-Sanduhr at script level");
        Assert.True(refusal < firstStop, $"The GITHUB_ACTIONS refusal (code line {refusal}) must come before the first Stop-Sanduhr (code line {firstStop}).");
    }

    [Fact]
    public void Install_switch_has_no_uninstall_first_path()
        => Assert.DoesNotContain("UninstallFirst", Script("check-install-switch.ps1"), StringComparison.Ordinal);

    [Fact]
    public void Launch_check_refuses_off_a_runner_before_it_starts_anything()
    {
        var code = Code(Script("smoke-launch.ps1"));
        var refusal = IndexOf(code, l => l.Contains(CiOnlyRefusal, StringComparison.Ordinal) && l.Contains("if (", StringComparison.Ordinal), "with the GITHUB_ACTIONS refusal");
        var addType = IndexOf(code, l => l.TrimStart().StartsWith("Add-Type", StringComparison.Ordinal), "with Add-Type");
        var start = IndexOf(code, l => l.Contains("Start-Process", StringComparison.Ordinal), "with Start-Process");
        Assert.True(refusal < addType, $"The GITHUB_ACTIONS refusal (code line {refusal}) must come before Add-Type (code line {addType}).");
        Assert.True(refusal < start, $"The GITHUB_ACTIONS refusal (code line {refusal}) must come before Start-Process (code line {start}).");
    }

    [Fact]
    public void Velopack_clean_only_needs_a_release_dir_before_it_cleans()
    {
        // -CleanOnly is for tests. Without -ReleaseDir it would clean the real dist\release and exit.
        var code = Code(Script("build-velopack-release.ps1"));
        var guard = IndexOf(code, l => l.Contains("$CleanOnly -and -not $ReleaseDir", StringComparison.Ordinal) && l.Contains("throw", StringComparison.Ordinal), "throwing on -CleanOnly without -ReleaseDir");
        var clean = IndexOf(code, l => l.Contains("Clear-ChannelFiles -Dir", StringComparison.Ordinal), "running the clean");
        Assert.True(guard < clean, $"The -CleanOnly guard (code line {guard}) must come before the clean (code line {clean}).");
    }

    [Fact]
    public void The_committed_manifest_stays_x64()
    {
        var manifest = XDocument.Load(Path.Combine(Root(), "src", "Sanduhr.App", "Package.appxmanifest"));
        var identity = manifest.Root!.Element(Foundation + "Identity")!;
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));
    }
}
