using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 8: Reset's folder list and About's caption (spec, Reset; WF20, R12). Each line is exactly true in the
/// state it shows in (decision 6); the part's label and the rows' kinds come from the same four words (decision 7). Keeping is per kind.</summary>
public class SettingsSlice8NamesTests
{
    [Fact]
    public void About_says_what_Reset_removes_including_themes_and_widgets()
    {
        Assert.Equal("Removes your accounts, history, session archive, themes, widgets, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.",
            SettingsNames.ResetSanduhrCaption);
    }

    [Fact]
    public void The_part_label_lists_the_four_kinds_the_rows_use_in_the_same_order()
    {
        Assert.Equal([IntegrationKind.Statusline, IntegrationKind.Mcp, IntegrationKind.Meters, IntegrationKind.Hooks], SettingsNames.ResetKindOrder);
        Assert.Equal($"Sanduhr's entries in Claude Code ({SettingsNames.ResetFolderKinds(SettingsNames.ResetKindOrder)})",
            SettingsNames.ResetClaudeCodeEntries);
        Assert.Equal("statusline, meters mod", SettingsNames.ResetFolderKinds([IntegrationKind.Meters, IntegrationKind.Statusline]));
        Assert.Equal("MCP server", SettingsNames.ResetFolderKinds([IntegrationKind.Mcp]));
        Assert.Equal("", SettingsNames.ResetFolderKinds([]));
    }

    [Fact]
    public void The_list_and_its_lines_are_the_plans_words()
    {
        Assert.Equal("Claude Code folders to reset", SettingsNames.ResetFoldersName);
        Assert.Equal("No Claude Code folder holds anything of Sanduhr's.", SettingsNames.ResetNoFolders);
        Assert.Equal("Unticked folders keep Sanduhr's entries and the files they need. Remove them later in Settings, Claude Code.", SettingsNames.ResetFoldersKept);
        Assert.Equal("With this part unticked, every folder keeps Sanduhr's entries and the files they need.", SettingsNames.ResetFoldersPartOff);
        Assert.Equal("Their meters stay empty until you sign in to Sanduhr again.", SettingsNames.ResetKeptSignIn);
        Assert.Equal("With the settings reset, their meters come back once you open Settings, Claude Code.", SettingsNames.ResetKeptSettings);
        Assert.Equal("With the settings reset, its MCP server stays but answers without your usage until you add it again in Settings, Claude Code.", SettingsNames.ResetKeptMcpSettings);
        Assert.Equal("A folder you added yourself leaves that page with the settings: add it there again to remove its entries.", SettingsNames.ResetKeptAdded);
        Assert.Equal("Sanduhr's record of what it installed can't be read, so some entries can't be listed or removed here. Sanduhr keeps its files for them; remove the entries in Settings, Claude Code, or by hand.",
            SettingsNames.ResetReceiptsUnreadable);
    }

    [Fact]
    public void Each_kept_line_belongs_to_one_state_and_names_only_that_state()
    {
        // Meters kept, Accounts ticked: the sign-in line. Never mentions settings, MCP or added folders.
        Assert.Contains("meters", SettingsNames.ResetKeptSignIn);
        Assert.Contains("sign in", SettingsNames.ResetKeptSignIn);
        Assert.DoesNotContain("MCP", SettingsNames.ResetKeptSignIn);
        Assert.DoesNotContain("settings", SettingsNames.ResetKeptSignIn);
        // Meters kept, Settings ticked: they come back. Says nothing of MCP.
        Assert.Contains("meters come back", SettingsNames.ResetKeptSettings);
        Assert.DoesNotContain("MCP", SettingsNames.ResetKeptSettings);
        // MCP server kept, Settings ticked: it does NOT come back by itself, so the line must not say it does.
        Assert.Contains("MCP server", SettingsNames.ResetKeptMcpSettings);
        Assert.Contains("add it again", SettingsNames.ResetKeptMcpSettings);
        Assert.DoesNotContain("come back", SettingsNames.ResetKeptMcpSettings);
        Assert.DoesNotContain("meters", SettingsNames.ResetKeptMcpSettings);
        // Hand-added folder kept, Settings ticked.
        Assert.Contains("added yourself", SettingsNames.ResetKeptAdded);
        Assert.DoesNotContain("meters", SettingsNames.ResetKeptAdded);
        Assert.DoesNotContain("MCP", SettingsNames.ResetKeptAdded);
        // The general line promises nothing per kind beyond keeping entries and files.
        Assert.DoesNotContain("meters", SettingsNames.ResetFoldersKept);
        Assert.DoesNotContain("all", SettingsNames.ResetFoldersKept.Split(' '));
    }

    [Fact]
    public void Every_line_names_places_the_one_way_and_none_uses_an_em_dash()
    {
        foreach (var text in new[]
        {
            SettingsNames.ResetSanduhrCaption, SettingsNames.ResetFoldersName, SettingsNames.ResetNoFolders, SettingsNames.ResetFoldersKept, SettingsNames.ResetFoldersPartOff,
            SettingsNames.ResetKeptSignIn, SettingsNames.ResetKeptSettings, SettingsNames.ResetKeptMcpSettings, SettingsNames.ResetKeptAdded,
            SettingsNames.ResetReceiptsUnreadable,
        })
        {
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.DoesNotContain('—', text);
            if (text != SettingsNames.ResetFoldersName) // a spoken label, not a sentence
                Assert.EndsWith(".", text);
        }
    }

    /// <summary>docs/PRIVACY.md says what an unticked folder keeps (decision 16), in the dialog's words, and drops "so they keep working"
    /// (preflight P2): a reset Settings part removes the MCP consent, so a kept server answers without usage.</summary>
    [Fact]
    public void PRIVACY_says_what_an_unticked_folder_keeps()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "PRIVACY.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var privacy = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "PRIVACY.md")).Replace("\r\n", "\n");
        // The Windows rows only: the Mac paragraph is another surface's claim.
        foreach (var line in privacy.Split('\n').Where(l => l.StartsWith("| **Reset Sanduhr…**") || l.StartsWith("- **Clear local storage:**")))
        {
            Assert.DoesNotContain("so they keep working", line);
            Assert.DoesNotContain("can't be removed from Sanduhr later", line);
        }
        // The Clear local storage bullet.
        Assert.Contains("Its first part lists each Claude Code folder that holds Sanduhr's entries, each with its own checkbox. "
            + "A folder you untick keeps its entries exactly as they are, and Sanduhr keeps the files they run and its record of them, "
            + "so Settings, Claude Code can remove them later. "
            + "If you also reset Settings, a kept MCP server stays registered but answers without your usage until you add it again in Settings, Claude Code.", privacy);
        // The Reset row.
        Assert.Contains("A folder you untick keeps its entries exactly as they are, and Sanduhr keeps the files those entries run "
            + "(the statusline script, the MCP launcher and server, the meters mod, each while an entry of its kind is kept) and its record of them, "
            + "so Settings, Claude Code can remove them later.", privacy);
        Assert.Contains("Sanduhr also keeps its record and the meters mod's copy while its record of what it installed can't be read or an entry failed to come out. "
            + "The MCP server has no such record, so Sanduhr takes it out of every folder except the ones you unticked, and when each of those worked and no MCP entry is kept "
            + "it removes the MCP launcher and server copies (all but a copy a running Claude Code session holds), whatever the state of the record and whatever else failed. "
            + "It removes the statusline script only when no statusline entry is kept, that record can be read and no statusline entry failed to come out or had to be left. "
            + "When that record can't be read or is missing, Sanduhr removes only the statusline entries it lists and leaves any other statusline entry as it is.", privacy);
        Assert.Contains("The Settings part removes `mcp-settings.json` and the MCP switch whatever is kept, so a kept MCP server stays registered "
            + "but answers without your usage until you add it again in Settings, Claude Code.", privacy);
    }
}
