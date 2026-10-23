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
            new[]
            {
                "CcLogReader.cs",
                // Root discovery only (2026-10-11 ruling, decision 6h5YrnXr0YVjPqlqqi8i): folder
                // names under the profile. Consent (mcp_roots) still gates every root.
                "ClaudeCodeHomes.cs",
                // Watchers and Desk messages (3.6.0, Este's ruling 2026-10-21). All pure text, JSON and
                // regex; no IO, network, credentials or process. The server validates with the widget's
                // own code and queues request files only; the widget owns messages.txt and the board.
                //   DeskMessages, MessageEffects, LetterMap: the line grammar and the "today" pick.
                //   MessageProposal: the line checks and the request decode (the widget's authority).
                //   Watchers, WatcherRequest: the limits, the commands, the request and switch files.
                "DeskArrangement.cs", "DeskDesign.cs", "DeskDesignValidator.cs", "DeskFont.cs", "DeskInk.cs",
                "DeskMessages.cs", "DeskPlate.cs",
                // get_desk and propose_desk (3.7.0, the agent-designed Desk). Nine files, all pure (text, JSON, regex and
                // math; no IO, network, credentials or process), so the server validates a design with the widget's own
                // code. The widget owns every write that matters; the server queues request files and reads desk-state.json.
                //   DeskDesign, DeskDesignValidator: the design contract, its rules (contrast, fonts, Start-menu
                //     membership), Merge and Diff.
                //   WallpaperSummary: the summary types and their JSON, read from desk-state.json.
                //   DeskArrangement: the piece keys, the anchors, the size range.
                //   DeskInk: the ink parsing and luminance (InkHex, DeskInk).
                //   DeskPlate: the plate's color and opacity. DeskFont: the bundled family names.
                //   LauncherTarget, LauncherModel: the Launcher button shape rules (a button opens only a Start-menu app
                //     or an https link; membership is checked against desk-state.json's list).
                "LauncherModel.cs", "LauncherTarget.cs",
                "LetterMap.cs", "MessageEffects.cs", "MessageProposal.cs",
                // propose_now_playing_looks (3.7.0, a look per song). One file, pure (text, JSON, regex and math; no IO, network,
                // credentials or process): the look model, the song key (FNV-1a 64), the checks with the contrast rule, the request
                // decode and the result shape, so the server refuses exactly what the widget would. The widget owns every write
                // (the saved looks, the handoff files, the file for the mod); the server queues the request file only.
                "NowPlayingLook.cs",
                "Pacing.cs",
                // propose_widget (3.8, widgets made with Claude). Two files, pure (bytes in, findings out: JSON, regex, PNG
                // chunks and CRCs, in-memory zlib; no disk, network, credentials or process), so the server refuses exactly
                // the pack the widget's install would. The rasterizer and every write stay with the widget.
                //   PackJson: the strict reader (duplicate keys, ints apart from floats) and the reference's message format.
                //   PackValidator: every rule of the sanduhr-widgets pack format, composing ThemeLint for theme packs.
                "PackJson.cs", "PackValidator.cs",
                "SnapshotContract.cs",
                // propose_theme: the lint and the model it builds, both pure. The
                // widget owns every write under the themes folder.
                "ThemeLint.cs", "ThemeModel.cs", "TierModel.cs",
                // Abilities wave: vault READS only — the drift watchlist bans
                // MCP-side vault writes; VaultStore's write methods have no call
                // sites in the server (cold-verified tool surface).
                "VaultJson.cs", "VaultModels.cs", "VaultReader.cs", "VaultStore.cs",
                "WallpaperSummary.cs",
                "WatcherRequest.cs", "Watchers.cs",
            },
            linked);
    }

    /// <summary>
    /// The linked files are the trust boundary, so each one is read for what it can do. Any of these
    /// names in a linked file would let the server reach out of its lane. Checks the files the 3.6.0 and
    /// 3.7.0 widenings added (the older slice is covered by the type checks below and the cold verify).
    /// </summary>
    [Theory]
    [InlineData("Watchers.cs")]
    [InlineData("WatcherRequest.cs")]
    [InlineData("MessageProposal.cs")]
    [InlineData("DeskMessages.cs")]
    [InlineData("MessageEffects.cs")]
    [InlineData("LetterMap.cs")]
    [InlineData("DeskDesign.cs")]
    [InlineData("DeskDesignValidator.cs")]
    [InlineData("WallpaperSummary.cs")]
    [InlineData("DeskArrangement.cs")]
    [InlineData("DeskInk.cs")]
    [InlineData("DeskPlate.cs")]
    [InlineData("DeskFont.cs")]
    [InlineData("LauncherTarget.cs")]
    [InlineData("LauncherModel.cs")]
    [InlineData("NowPlayingLook.cs")]
    [InlineData("PackJson.cs")]
    [InlineData("PackValidator.cs")]
    public void Files_linked_for_watchers_desk_messages_and_desk_designs_are_pure(string file)
    {
        string path = Path.Combine(Path.GetDirectoryName(McpCsprojPath())!, "..", "Sanduhr.Core", file);
        string text = File.ReadAllText(path);
        foreach (var forbidden in new[]
        {
            "HttpClient", "System.Net", "Socket", "WebView", "Process.Start", "ProcessStartInfo", "DllImport",
            "LibraryImport", "File.", "Directory.", "FileStream", "StreamReader", "StreamWriter", "Environment.",
            "Registry", "Credential",
        })
        {
            Assert.DoesNotContain(forbidden, text);
        }
    }

    /// <summary>The widget's half of the design handoff, the state builder, the saved-design store and the wallpaper
    /// renderer stay out of the server: it validates, queues and reads, and never writes a design or draws a wallpaper.</summary>
    [Fact]
    public void The_widgets_half_of_the_desk_design_handoff_is_not_in_the_mcp_assembly()
    {
        var typeNames = typeof(Sanduhr.Mcp.McpServer).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        foreach (var name in new[] { "DeskDesignHandoff", "DeskDesignLibrary", "SavedDesign", "WallpaperRender", "WallpaperSampler", "DeskSettings", "SettingsNames" })
            Assert.DoesNotContain(name, typeNames);
        Assert.Contains("DeskDesign", typeNames);   // and the validator it composes is
    }

    /// <summary>The widget catalog client (3.8) is the app's alone: the server never fetches, verifies, unzips or installs a
    /// pack. It validates a proposed pack with PackValidator and nothing more.</summary>
    [Fact]
    public void The_widget_catalog_client_is_not_in_the_mcp_assembly()
    {
        var typeNames = typeof(Sanduhr.Mcp.McpServer).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        foreach (var name in new[] { "WidgetCatalog", "WidgetCatalogClient", "WidgetZip", "WidgetStore", "WidgetRegistry", "WidgetCatalogService" })
            Assert.DoesNotContain(name, typeNames);
        string csproj = File.ReadAllText(McpCsprojPath());
        foreach (var file in new[] { "WidgetCatalog", "WidgetZip", "WidgetStore" })
            Assert.DoesNotContain(file, csproj);
    }

    /// <summary>The widget's half of the song looks (the saved-looks file, the handoff, the file for the mod) stays out of the
    /// server: it checks, queues the request file and reads desk-state.json, and never saves a look.</summary>
    [Fact]
    public void The_widgets_half_of_the_song_looks_is_not_in_the_mcp_assembly()
    {
        var typeNames = typeof(Sanduhr.Mcp.McpServer).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        foreach (var name in new[] { "NowPlayingLookStore", "NowPlayingLookHandoff", "NowPlayingLookPaths", "MessageFileIo" })
            Assert.DoesNotContain(name, typeNames);
        Assert.Contains("NowPlayingLookCheck", typeNames);   // and the checks are
    }

    /// <summary>The server validates a widget pack and never draws one: the validator is in, the pixel-sheet rasterizer
    /// is not.</summary>
    [Fact]
    public void The_pack_validator_is_in_the_mcp_assembly_and_the_rasterizer_is_not()
    {
        var typeNames = typeof(Sanduhr.Mcp.McpServer).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.Contains("PackValidator", typeNames);
        Assert.DoesNotContain("PixelSheet", typeNames);
        Assert.DoesNotContain("PixelSheetImage", typeNames);
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
