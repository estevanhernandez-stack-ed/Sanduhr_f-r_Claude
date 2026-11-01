using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class SetupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-setup-" + Guid.NewGuid().ToString("N")[..8]);
    public SetupStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }
    private SettingsStore Store() => new(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));

    [Fact]
    public void No_setup_key_loads_as_null_and_a_saved_one_comes_back()
    {
        var store = Store();
        Assert.Null(store.LoadSetup());
        store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool> { ["claude"] = true }));
        var back = store.LoadSetup()!;
        Assert.False(back.Desktop);
        Assert.True(back.ClaudeUsage);
    }

    [Fact]
    public void Migrating_Just_the_Desk_writes_usage_off_turns_the_Desk_on_and_leaves_the_old_keys()
    {
        var store = Store();
        store.SaveJustTheDesk(new JustTheDeskState(true, SilencedReminder: true));
        store.SaveDeskSignInReminder(false);
        Assert.True(store.MigrateSetup(hasAccount: false));
        var s = store.LoadSetup()!;
        Assert.True(s.Desktop);
        Assert.False(s.ClaudeUsage);
        Assert.True(store.LoadDesk().Enabled);
        Assert.True(store.LoadDeskSignInReminder());
        Assert.True(store.LoadJustTheDesk().On);   // left for a 4.x build reading the same file
    }

    [Fact]
    public void Migrating_an_account_with_the_Desk_off_keeps_desktop_on_and_the_Desk_off()
    {
        var store = Store();
        Assert.False(store.LoadDesk().Enabled);
        Assert.True(store.MigrateSetup(hasAccount: true));
        var s = store.LoadSetup()!;
        Assert.True(s.Desktop);              // turning the Desk on later in Settings draws the whole Desk
        Assert.True(s.ClaudeUsage);
        Assert.False(store.LoadDesk().Enabled);   // nothing changes on upgrade
        Assert.False(SetupChoices.Of(s, drawersOn: false, deskEnabled: store.LoadDesk().Enabled).Desktop);   // the screen shows the Desk's own switch
    }

    [Fact]
    public void An_unreadable_setup_entry_falls_back_to_desktop_on_even_with_the_Desk_off()
    {
        var paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFile)!);
        File.WriteAllText(paths.SettingsFile, "{\"setup\":{\"desktop\":\"yes\",\"usage\":{}}}");
        var s = new SettingsStore(paths).LoadSetup()!;
        Assert.True(s.Desktop);
        Assert.True(s.ClaudeUsage);
    }

    [Fact]
    public void Migration_runs_once_and_never_on_a_first_run()
    {
        var store = Store();
        Assert.False(store.MigrateSetup(hasAccount: false));   // first run: nothing written
        Assert.Null(store.LoadSetup());
        Assert.True(store.MigrateSetup(hasAccount: true));
        store.SaveSetup(store.LoadSetup()! with { Desktop = true });
        Assert.False(store.MigrateSetup(hasAccount: true));    // a setup exists: untouched
        Assert.True(store.LoadSetup()!.Desktop);
    }
}
