using Microsoft.Win32;
using Sanduhr.Core;
using Xunit;

// The throwaway-key setup/teardown touches the registry directly; the suite runs
// on Windows. Scope the platform analyzer off this Windows-only test file.
#pragma warning disable CA1416

namespace Sanduhr.Tests;

/// <summary>
/// Tests for auto-start (Core/Startup.cs) — ported from <c>test_startup.py</c>.
/// Pure helpers plus a real registry round-trip against a <b>throwaway</b> HKCU
/// test key (cleaned in teardown), so no test ever touches the real
/// <c>…\Run\Sanduhr</c> value, and the packaged branch is exercised with an
/// injected predicate + opener so it never launches Windows Settings.
/// </summary>
public class StartupTests : IDisposable
{
    private const string TestRunKey = @"Software\Sanduhr\test-autostart";

    public void Dispose()
    {
        // Remove the throwaway tree; never touch the real Run key.
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Sanduhr", throwOnMissingSubKey: false); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void IsPackaged_detects_windowsapps_path()
    {
        const string packaged = @"C:\Program Files\WindowsApps\626LabsLLC.SanduhrfrClaude_3.0.0.0_x64__abc\Sanduhr.exe";
        const string unpackaged = @"C:\Users\este\AppData\Local\Sanduhr\Sanduhr.exe";
        Assert.True(Startup.IsPackaged(packaged));
        Assert.False(Startup.IsPackaged(unpackaged));
        Assert.False(Startup.IsPackaged(""));
    }

    [Fact]
    public void RunCommand_quotes_the_path()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Sanduhr\\Sanduhr.exe\"",
            Startup.RunCommand(@"C:\Program Files\Sanduhr\Sanduhr.exe"));
    }

    [Fact]
    public void Value_name_matches_installer()
    {
        // Guards against drift from the installer's [Registry] ValueName.
        Assert.Equal("Sanduhr", Startup.ValueName);
    }

    [Fact]
    public void Enable_disable_roundtrip_unpackaged()
    {
        var mgr = new StartupManager(
            runKeyPath: TestRunKey,
            isPackaged: () => false,
            executable: @"C:\Apps\Sanduhr\Sanduhr.exe");

        Assert.False(mgr.IsEnabledUnpackaged());

        mgr.SetEnabledUnpackaged(true);
        Assert.True(mgr.IsEnabledUnpackaged());

        // The written value is the quoted executable path.
        using (var key = Registry.CurrentUser.OpenSubKey(TestRunKey))
            Assert.Equal("\"C:\\Apps\\Sanduhr\\Sanduhr.exe\"", key!.GetValue("Sanduhr"));

        // Disabling is idempotent.
        mgr.SetEnabledUnpackaged(false);
        mgr.SetEnabledUnpackaged(false);
        Assert.False(mgr.IsEnabledUnpackaged());
    }

    [Fact]
    public void SetEnabled_packaged_opens_settings_without_writing()
    {
        bool opened = false;
        var mgr = new StartupManager(
            runKeyPath: TestRunKey,
            isPackaged: () => true,
            openSettings: () => opened = true);

        var outcome = mgr.SetEnabled(true);

        Assert.True(outcome.OpenedSettings);
        Assert.False(outcome.Applied);
        Assert.True(opened);
        // Packaged branch must NOT have written the throwaway key.
        Assert.Null(Registry.CurrentUser.OpenSubKey(TestRunKey));
    }

    [Fact]
    public void SetEnabled_unpackaged_writes_run_key()
    {
        var mgr = new StartupManager(
            runKeyPath: TestRunKey,
            isPackaged: () => false,
            executable: @"C:\X\Sanduhr.exe");

        var outcome = mgr.SetEnabled(true);

        Assert.True(outcome.Applied);
        Assert.False(outcome.OpenedSettings);
        Assert.True(mgr.IsEnabledUnpackaged());
    }

    [Fact]
    public void IsEnabled_reports_false_when_packaged()
    {
        var mgr = new StartupManager(isPackaged: () => true);
        Assert.False(mgr.IsEnabled());
    }
}
