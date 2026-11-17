using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public class VaultServiceAgentTests
{
    private static readonly IReadOnlyDictionary<string, bool> None = new Dictionary<string, bool>();

    [Fact]
    public void A_claude_home_needs_an_explicit_yes() =>
        Assert.False(VaultService.Consented(".claude", None, activeAgents: [], prompted: false));

    [Fact]
    public void An_active_agent_is_on_until_purged()
    {
        Assert.True(VaultService.Consented("codex", None, activeAgents: ["codex"], prompted: false));
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { ["codex"] = false }, activeAgents: ["codex"], prompted: false));
    }

    [Fact]
    public void An_agent_whose_switch_is_off_is_never_consented() =>
        Assert.False(VaultService.Consented("antigravity", new Dictionary<string, bool> { ["antigravity"] = true }, activeAgents: [], prompted: false));

    [Fact]
    public void A_declined_archive_leaves_an_agent_off_by_default()
    {
        // Answered the prompt, archived no Claude Code folder: the archive was declined.
        Assert.False(VaultService.Consented("codex", None, activeAgents: ["codex"], prompted: true));
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { [".claude"] = false }, activeAgents: ["codex"], prompted: true));
        // A Claude Code folder archived: the agent follows it.
        Assert.True(VaultService.Consented("codex", new Dictionary<string, bool> { [".claude"] = true }, activeAgents: ["codex"], prompted: true));
        // Another agent's yes is not a Claude Code folder's.
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { ["antigravity"] = true }, activeAgents: ["codex", "antigravity"], prompted: true));
    }

    [Fact]
    public void An_explicit_yes_wins_over_a_declined_archive() =>
        Assert.True(VaultService.Consented("codex", new Dictionary<string, bool> { ["codex"] = true }, activeAgents: ["codex"], prompted: true));

    [Fact]
    public void The_tombstone_wins_even_with_a_claude_folder_archived() =>
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { [".claude"] = true, ["codex"] = false },
            activeAgents: ["codex"], prompted: false));
}

/// <summary>A cycle with nothing to ingest still finishes: subscribers hear it and drop what is no longer consented (F7).</summary>
[Collection("SettingsWindow")]
public sealed class VaultServiceIngestTests : AppWindowFixture
{
    [Fact]
    public void A_cycle_with_nothing_consented_still_raises_IngestCompleted()
    {
        var vault = new VaultService(new SettingsStore(ScratchPaths()), new CcLogReader(Profile, () => []));
        int raised = 0;
        vault.IngestCompleted += () => raised++;

        vault.TriggerIngest();

        Assert.Equal(1, raised);
        Assert.Empty(vault.ConsentedRootNames());
    }
}
