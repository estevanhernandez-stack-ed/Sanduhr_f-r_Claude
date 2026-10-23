using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Restart to update (3.8 spec 8b). The Store's StoreContext and Velopack's UpdateManager stay in the
/// app behind <see cref="IUpdateInstaller"/>; the order (browsers closed before the install, restart
/// registered, second launches let through) and the undo on a failed or cancelled install are tested
/// here against fakes.
/// </summary>
public class RestartToUpdateTests
{
    private sealed class FakeHost : IRestartHost
    {
        public List<string> Steps { get; } = new();
        public bool RegisterResult { get; set; } = true;
        public bool ThrowOnRelease { get; set; }

        public Task ReleaseBrowsersAsync()
        {
            if (ThrowOnRelease) throw new InvalidOperationException("boom");
            Steps.Add("release");
            return Task.CompletedTask;
        }
        public void ResumeBrowsers() => Steps.Add("resume-browsers");
        public void HoldInstance() => Steps.Add("hold");
        public void ResumeInstance() => Steps.Add("resume-instance");
        public bool RegisterRestart() { Steps.Add("register"); return RegisterResult; }
        public void UnregisterRestart() => Steps.Add("unregister");
        public void RelaunchAndQuit() => Steps.Add("relaunch");
        public void Log(string message) { }
    }

    private sealed class FakeInstaller(UpdateInstallResult result, FakeHost host, string channel = "store") : IUpdateInstaller
    {
        public string Channel => channel;
        public int Calls { get; private set; }
        public Exception? Throw { get; set; }

        public Task<UpdateInstallResult> InstallAsync()
        {
            Calls++;
            host.Steps.Add("install");
            if (Throw is not null) throw Throw;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Nothing_happens_without_an_offer()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host);

        Assert.Equal(RestartOutcome.Ignored, await flow.AcceptAsync());
        Assert.Empty(host.Steps);
        Assert.Null(flow.Notice);
    }

    [Fact]
    public void The_offer_asks_before_doing_anything()
    {
        var host = new FakeHost();
        var installer = new FakeInstaller(UpdateInstallResult.Installing, host);
        var flow = new RestartToUpdate(host);

        Assert.True(flow.Offer("3.8.1", installer));

        Assert.Equal("Sanduhr 3.8.1 is ready. Restart to update?", flow.Notice);
        Assert.Equal(0, installer.Calls);
        Assert.Empty(host.Steps);
        Assert.False(flow.Offer("3.8.1", installer)); // the same version again is not news
        Assert.True(flow.Offer("3.8.2", installer));
    }

    /// <summary>The installer closes the process: the wait for that never ends inside a test.</summary>
    private static Task Forever() => new TaskCompletionSource().Task;

    [Fact]
    public async Task Yes_closes_the_browsers_registers_and_lets_the_relaunch_through_before_installing()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host) { WaitForInstallerAsync = Forever };
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.Installing, host));

        var accepting = flow.AcceptAsync();
        await Task.Yield();

        Assert.False(accepting.IsCompleted); // waiting to be closed by the installer
        Assert.Equal(new[] { "release", "hold", "register", "install" }, host.Steps);
        Assert.True(flow.Busy);
    }

    [Fact]
    public async Task A_process_the_installer_never_closed_undoes_and_keeps_the_offer()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host) { WaitForInstallerAsync = () => Task.CompletedTask };
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.Installing, host, "velopack"));

        Assert.Equal(RestartOutcome.StillRunning, await flow.AcceptAsync());

        Assert.Equal(new[] { "release", "hold", "register", "install", "unregister", "resume-instance", "resume-browsers" }, host.Steps);
        Assert.True(flow.Offered);
        Assert.False(flow.Busy);
    }

    [Theory]
    [InlineData(UpdateInstallResult.Canceled)]
    [InlineData(UpdateInstallResult.Failed)]
    public async Task A_cancelled_or_failed_install_keeps_Sanduhr_running_and_keeps_the_offer(UpdateInstallResult result)
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", new FakeInstaller(result, host));

        Assert.Equal(RestartOutcome.StillRunning, await flow.AcceptAsync());

        Assert.Equal(new[] { "release", "hold", "register", "install", "unregister", "resume-instance", "resume-browsers" }, host.Steps);
        Assert.True(flow.Offered);
        Assert.False(flow.Busy);
        Assert.Equal("3.8.1", flow.Version);
    }

    [Fact]
    public async Task An_installer_that_throws_is_a_failed_install()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.Installing, host) { Throw = new System.Runtime.InteropServices.COMException("store") });

        Assert.Equal(RestartOutcome.StillRunning, await flow.AcceptAsync());
        Assert.Contains("resume-browsers", host.Steps);
        Assert.True(flow.Offered);
    }

    [Fact]
    public async Task A_teardown_that_throws_undoes_only_what_it_did()
    {
        var host = new FakeHost { ThrowOnRelease = true };
        var installer = new FakeInstaller(UpdateInstallResult.Installing, host);
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", installer);

        Assert.Equal(RestartOutcome.StillRunning, await flow.AcceptAsync());

        Assert.Equal(0, installer.Calls);
        Assert.Equal(new[] { "resume-browsers" }, host.Steps);
    }

    [Fact]
    public async Task A_refused_restart_registration_still_installs_and_is_not_unregistered()
    {
        var host = new FakeHost { RegisterResult = false };
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.Canceled, host));

        await flow.AcceptAsync();

        Assert.Contains("install", host.Steps);
        Assert.DoesNotContain("unregister", host.Steps);
    }

    [Fact]
    public async Task Installed_without_being_closed_relaunches_itself()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.Completed, host));

        Assert.Equal(RestartOutcome.Restarting, await flow.AcceptAsync());
        Assert.Equal("relaunch", host.Steps[^1]);
    }

    [Fact]
    public async Task An_update_that_went_away_withdraws_the_offer()
    {
        var host = new FakeHost();
        var flow = new RestartToUpdate(host);
        flow.Offer("3.8.1", new FakeInstaller(UpdateInstallResult.NotAvailable, host));

        Assert.Equal(RestartOutcome.StillRunning, await flow.AcceptAsync());
        Assert.False(flow.Offered);
        Assert.Null(flow.Notice);
        Assert.Contains("resume-browsers", host.Steps);
    }

    [Fact]
    public async Task A_second_click_while_restarting_does_nothing()
    {
        var host = new FakeHost();
        var installer = new FakeInstaller(UpdateInstallResult.Installing, host);
        var flow = new RestartToUpdate(host) { WaitForInstallerAsync = Forever };
        flow.Offer("3.8.1", installer);

        _ = flow.AcceptAsync();
        Assert.Equal(RestartOutcome.Ignored, await flow.AcceptAsync());
        Assert.Equal(1, installer.Calls);
    }

    [Fact]
    public void Both_channels_use_one_button()
    {
        Assert.Equal("Restart to update", RestartToUpdate.ButtonText);
        Assert.DoesNotContain("Quit", RestartToUpdate.ButtonText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3, 8, 1, 0, "3.8.1")]
    [InlineData(3, 8, 0, 0, "3.8.0")]
    [InlineData(3, 8, 1, 2, "3.8.1.2")]
    public void Versions_read_like_Settings_shows_them(int major, int minor, int build, int revision, string expected)
        => Assert.Equal(expected, RestartToUpdate.DisplayVersion(major, minor, build, revision));

    [Fact]
    public void The_Store_build_relaunches_through_its_app_id_and_others_by_path()
    {
        Assert.Equal(("explorer.exe", @"shell:AppsFolder\626Labs.Sanduhr_abc123!Sanduhr"),
            AppRelaunch.Target(packaged: true, "626Labs.Sanduhr_abc123", @"C:\Program Files\WindowsApps\x\Sanduhr.exe"));
        Assert.Equal((@"C:\dev\Sanduhr.exe", ""), AppRelaunch.Target(packaged: false, null, @"C:\dev\Sanduhr.exe"));
        Assert.Null(AppRelaunch.Target(packaged: true, null, @"C:\x\Sanduhr.exe"));
        Assert.Null(AppRelaunch.Target(packaged: false, null, null));
    }
}
