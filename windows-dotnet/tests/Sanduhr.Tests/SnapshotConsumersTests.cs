using Sanduhr.Core;

namespace Sanduhr.Tests;

public class SnapshotConsumersTests
{
    private static readonly ConsumerState[] None = [ConsumerState.NotInstalled, ConsumerState.NotInstalled];

    [Fact]
    public void Nothing_anywhere_turns_the_snapshot_off()
        => Assert.Equal(SnapshotDecision.Off, SnapshotConsumers.Decide(None, []));

    [Fact]
    public void An_installed_piece_keeps_it_on()
        => Assert.Equal(SnapshotDecision.On,
            SnapshotConsumers.Decide([ConsumerState.NotInstalled, ConsumerState.Installed], []));

    [Fact]
    public void An_unreadable_folder_keeps_the_current_state()
        => Assert.Equal(SnapshotDecision.Keep,
            SnapshotConsumers.Decide([ConsumerState.NotInstalled, ConsumerState.Unreadable], []));

    [Fact]
    public void An_installed_piece_beats_an_unreadable_folder()
        => Assert.Equal(SnapshotDecision.On,
            SnapshotConsumers.Decide([ConsumerState.Unreadable, ConsumerState.Installed], []));

    [Theory]
    [InlineData(IntegrationKind.Statusline)]
    [InlineData(IntegrationKind.Meters)]
    public void A_receipt_for_a_forgotten_folder_keeps_it_on(IntegrationKind kind)
        => Assert.Equal(SnapshotDecision.On, SnapshotConsumers.Decide(None, [kind]));

    [Theory]
    [InlineData(IntegrationKind.Mcp)]
    [InlineData(IntegrationKind.Hooks)]
    public void Receipts_of_other_kinds_do_not_count(IntegrationKind kind)
        => Assert.Equal(SnapshotDecision.Off, SnapshotConsumers.Decide(None, [kind]));

    [Fact]
    public void An_unreadable_receipts_file_keeps_the_current_state()
        => Assert.Equal(SnapshotDecision.Keep, SnapshotConsumers.Decide(None, [], receiptsUnknown: true));

    [Fact]
    public void Statuses_map_to_states()
    {
        Assert.Equal(ConsumerState.Installed, SnapshotConsumers.Of(StatuslineStatus.Outdated));
        Assert.Equal(ConsumerState.Unreadable, SnapshotConsumers.Of(StatuslineStatus.Unreadable));
        Assert.Equal(ConsumerState.NotInstalled, SnapshotConsumers.Of(StatuslineStatus.Other));
        Assert.Equal(ConsumerState.Installed, SnapshotConsumers.Of(IntegrationStatus.Installed));
        Assert.Equal(ConsumerState.Unreadable, SnapshotConsumers.Of(IntegrationStatus.Unreadable));
        Assert.Equal(ConsumerState.NotInstalled, SnapshotConsumers.Of(IntegrationStatus.NotInstalled));
    }

    [Fact]
    public void Statusline_receipt_folders_are_normalized_and_deduped()
    {
        IntegrationReceipt R(IntegrationKind k, string folder) =>
            new() { Kind = k, Folder = folder, File = folder + "\\settings.json" };
        var folders = SnapshotConsumers.StatuslineReceiptFolders([
            R(IntegrationKind.Statusline, @"C:\Users\x\.claude-gone"),
            R(IntegrationKind.Statusline, @"c:\users\x\.claude-gone\"),
            R(IntegrationKind.Meters, @"C:\Users\x\.claude-meters"),
        ]);
        Assert.Single(folders);
    }

    [Fact]
    public void Reset_removes_a_statusline_by_the_receipts_full_folder_path()
    {
        using var home = new TempDir();
        using var other = new TempDir();      // a folder outside the profile, no longer in any list
        using var sanduhr = new TempDir();
        var bin = Path.Combine(sanduhr.Path, "bin");
        var installer = new StatuslineInstaller(home.Path, bin, null, sanduhr.Path);
        Assert.True(installer.InstallScript());
        Assert.True(installer.Install(other.Path, StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.True(installer.IsRegistered(other.Path));

        var (receipts, unknown) = SnapshotConsumers.ReadReceipts(
            Path.Combine(sanduhr.Path, "integrations", "installs.json"));
        var folders = SnapshotConsumers.StatuslineReceiptFolders(receipts);
        Assert.False(unknown);
        var folder = Assert.Single(folders);
        Assert.True(installer.IsRegistered(folder));
        Assert.True(installer.Remove(folder));

        Assert.False(installer.IsRegistered(other.Path));
        Assert.Empty(SnapshotConsumers.ReadReceipts(Path.Combine(sanduhr.Path, "integrations", "installs.json")).Receipts);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void The_snapshot_is_wanted_while_the_statusline_or_the_mcp_is_on(bool statusline, bool mcp, bool wanted)
        => Assert.Equal(wanted, SnapshotConsumers.SnapshotWanted(statusline, mcp));

    [Fact]
    public void A_missing_receipts_file_reads_as_empty_and_known()
    {
        using var dir = new TempDir();
        var (receipts, unknown) = SnapshotConsumers.ReadReceipts(Path.Combine(dir.Path, "installs.json"));
        Assert.Empty(receipts);
        Assert.False(unknown);
    }

    [Fact]
    public void A_receipts_file_that_is_not_an_array_reads_as_unknown()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "installs.json");
        File.WriteAllText(path, "{ nope");
        var (_, unknown) = SnapshotConsumers.ReadReceipts(path);
        Assert.True(unknown);
    }

    // Slice 8 final review M2: the switch also means "the meters mod reads the snapshot", and the meters mod never runs the script.
    // So the widget writes the script at start only for a statusline: one is on record, the record can't be read, or the script
    // is already there (an update for a statusline Sanduhr has no receipt for).
    private static (string Root, string Receipts, string Script) ScriptRig()
    {
        var root = Path.Combine(Path.GetTempPath(), "sanduhr-scriptwanted-" + Guid.NewGuid().ToString("N"));
        var receipts = Path.Combine(root, "appdata", "integrations", "installs.json");
        var script = Path.Combine(root, "bin", StatuslineInstaller.ScriptName);
        Directory.CreateDirectory(Path.GetDirectoryName(receipts)!);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        return (root, receipts, script);
    }

    [Fact]
    public void A_meters_mod_alone_does_not_bring_the_script_back()
    {
        var (root, receipts, script) = ScriptRig();
        try
        {
            new ReceiptStore(receipts).Save([new IntegrationReceipt { Kind = IntegrationKind.Meters, Folder = Path.Combine(root, ".claude"), File = Path.Combine(root, ".claude", "settings.json") }]);
            Assert.False(SnapshotConsumers.ScriptWanted(statuslineEnabled: true, receipts, script));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_statusline_on_record_an_unreadable_record_or_a_script_already_there_keeps_it_updated()
    {
        var (root, receipts, script) = ScriptRig();
        try
        {
            new ReceiptStore(receipts).Save([new IntegrationReceipt { Kind = IntegrationKind.Statusline, Folder = Path.Combine(root, ".claude"), File = Path.Combine(root, ".claude", "settings.json") }]);
            Assert.True(SnapshotConsumers.ScriptWanted(true, receipts, script));

            File.WriteAllText(receipts, "{ not a list");
            Assert.True(SnapshotConsumers.ScriptWanted(true, receipts, script));

            new ReceiptStore(receipts).Save([]);
            File.WriteAllText(script, "# an older copy");
            Assert.True(SnapshotConsumers.ScriptWanted(true, receipts, script));

            Assert.False(SnapshotConsumers.ScriptWanted(statuslineEnabled: false, receipts, script));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void No_record_and_no_script_writes_nothing()
    {
        var (root, receipts, script) = ScriptRig();
        try { Assert.False(SnapshotConsumers.ScriptWanted(true, receipts, script)); }
        finally { Directory.Delete(root, true); }
    }
}
