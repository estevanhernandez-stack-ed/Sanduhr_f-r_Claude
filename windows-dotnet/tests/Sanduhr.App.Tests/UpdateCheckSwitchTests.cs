using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 6: "Check for updates once a day" (settings.json "update_check") gates the GitHub probe. Off, the GitHub build asks nothing: not at
/// launch, not on the widget's tick. The Store build has no switch and ignores it. Check now asks once whatever the switch says. The probe is the only code
/// that asks GitHub for a release (NetworkDisclosureTests pins Velopack's four calls to its file, and the last test here pins who builds it), so a counting
/// probe that counts zero is zero HTTP.
/// </summary>
[Collection("SettingsWindow")]
public sealed class UpdateCheckSwitchTests : AppWindowFixture
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now = T0;
    }

    private sealed class CountingProbe(UpdateProbeResult answer) : IUpdateProbe
    {
        public int Asked;
        public TaskCompletionSource? Gate;

        public async Task<UpdateProbeResult> FindAsync()
        {
            Interlocked.Increment(ref Asked);
            if (Gate is { } gate)
                await gate.Task.ConfigureAwait(false);
            return answer;
        }
    }

    private sealed class NoInstall : IUpdateInstaller
    {
        public string Channel => "test";
        public Task<UpdateInstallResult> InstallAsync() => Task.FromResult(UpdateInstallResult.Installing);
    }

    private string StampPath => Path.Combine(Root, "local", "last-update-check.txt");

    private (UpdateChecker Checker, CountingProbe Probe, Clock Clock, SettingsStore Store, List<string> Offered) Make(bool on,
        UpdateProbeResult? answer = null)
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveUpdateCheck(on);
        var probe = new CountingProbe(answer ?? new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        var clock = new Clock();
        var offered = new List<string>();
        var checker = new UpdateChecker(store.LoadUpdateCheck, probe, StampPath, (v, _) => { lock (offered) offered.Add(v); }, () => clock.Now);
        return (checker, probe, clock, store, offered);
    }

    private static Task Settled(UpdateChecker checker) => checker.Running ?? Task.CompletedTask;

    [Fact]
    public async Task With_the_switch_off_nothing_asks_at_launch_or_on_the_tick()
    {
        var (checker, probe, clock, _, _) = Make(on: false);

        await checker.RunIfDueAsync();
        clock.Now = T0.AddDays(3);
        checker.Tick();
        await Settled(checker);

        Assert.Equal(0, probe.Asked);
        Assert.False(File.Exists(StampPath));
    }

    [Fact]
    public async Task With_the_switch_on_it_asks_at_launch_then_once_a_day_on_the_tick()
    {
        var (checker, probe, clock, _, _) = Make(on: true);

        await checker.RunIfDueAsync();
        Assert.Equal(1, probe.Asked);
        Assert.Equal(T0, UpdateCheck.ParseStamp(File.ReadAllText(StampPath)));

        clock.Now = T0.AddHours(23);
        checker.Tick();
        await Settled(checker);
        Assert.Equal(1, probe.Asked);

        clock.Now = T0.AddHours(25);
        checker.Tick();
        await Settled(checker);
        Assert.Equal(2, probe.Asked);
    }

    [Fact]
    public async Task Turning_it_off_stops_the_next_tick()
    {
        var (checker, probe, clock, store, _) = Make(on: true);
        await checker.RunIfDueAsync();
        var stamped = File.GetLastWriteTimeUtc(StampPath);

        store.SaveUpdateCheck(false);
        clock.Now = T0.AddDays(2);
        checker.Tick();
        await Settled(checker);

        Assert.Equal(1, probe.Asked);
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(StampPath));
    }

    [Fact]
    public async Task Check_now_asks_once_with_the_switch_off_and_offers_what_it_found()
    {
        var (checker, probe, _, _, offered) = Make(on: false, new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1", new NoInstall()));

        var result = await checker.CheckNowAsync();

        Assert.Equal(1, probe.Asked);
        Assert.Equal(UpdateProbeStatus.Ready, result.Status);
        Assert.Equal(["4.0.1"], offered);
    }

    [Fact]
    public async Task Check_now_shares_a_probe_already_running()
    {
        var (checker, probe, _, _, offered) = Make(on: true, new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1", new NoInstall()));
        probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var launch = checker.RunIfDueAsync();
        var now = checker.CheckNowAsync();
        probe.Gate.SetResult();
        await launch;
        var result = await now;

        Assert.Equal(1, probe.Asked);
        Assert.Equal(UpdateProbeStatus.Ready, result.Status);
        Assert.Equal(["4.0.1"], offered);
    }

    [Fact]
    public async Task An_unreadable_stamp_still_lets_it_ask()
    {
        var (checker, probe, _, _, _) = Make(on: true);
        Directory.CreateDirectory(Path.GetDirectoryName(StampPath)!);
        File.WriteAllText(StampPath, "garbage");

        await checker.RunIfDueAsync();

        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    public async Task With_the_switch_off_the_tick_holds_the_last_read_and_refreshes_it_only_every_few_minutes()
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveUpdateCheck(false);
        int reads = 0;
        var probe = new CountingProbe(new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        var clock = new Clock();
        var checker = new UpdateChecker(() => { reads++; return store.LoadUpdateCheck(); }, probe, StampPath, (_, _) => { }, () => clock.Now);

        // The stamp is absent, so a check is due on every tick; the switch is read once, not once per tick.
        for (int i = 0; i < 20; i++)
        {
            checker.Tick();
            clock.Now += TimeSpan.FromSeconds(10);
        }
        Assert.Equal(1, reads);
        Assert.Equal(0, probe.Asked);

        // Turned on: the held read keeps the tick quiet until the refresh is due, then the next tick sees it and asks.
        store.SaveUpdateCheck(true);
        checker.Tick();
        Assert.Equal(1, reads);
        clock.Now = T0 + UpdateChecker.EnabledRefresh;
        checker.Tick();
        await Settled(checker);
        Assert.Equal(2, reads);   // the refresh's read; the probe's launch rule reads once more only when it runs
        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    public void The_Store_build_ignores_update_check_so_a_carried_over_false_cannot_silence_it()
    {
        int asked = 0;
        // The query never answers, so the offers (null here) are never reached and nothing touches the real Store.
        var service = new StoreUpdateService(null!, () => 0, isPackaged: () => true)
        {
            Query = () => { asked++; return new TaskCompletionSource<string?>().Task; },
        };

        service.Tick(T0);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void The_GitHub_build_never_asks_the_Store_whatever_the_switch_says()
    {
        int asked = 0;
        var service = new StoreUpdateService(null!, () => 0, isPackaged: () => false)
        {
            Query = () => { asked++; return Task.FromResult<string?>(null); },
        };

        service.Tick(T0);
        service.Tick(T0.AddDays(1));

        Assert.Equal(0, asked);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    /// <summary>"Not anywhere else": one Velopack probe is built, in App, straight into the one checker; only the checker asks it; App wires the
    /// switch into the checker alone (the Store service takes none).</summary>
    [Fact]
    public void Nothing_but_the_checker_asks_the_probe()
    {
        var files = Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

        var builders = files.Where(f => f.Value.Contains("new VelopackUpdateProbe(", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.Equal(["App.xaml.cs"], builders);
        var app = files["App.xaml.cs"];
        var line = app.Split('\n').Single(l => l.Contains("new VelopackUpdateProbe(", StringComparison.Ordinal));
        Assert.Contains("new UpdateChecker(", line);

        var askers = files.Where(f => f.Value.Contains(".FindAsync(", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.Equal(["UpdateChecker.cs"], askers);
        Assert.Contains("new UpdateChecker(updateSwitch.LoadUpdateCheck", app);
        Assert.DoesNotContain("enabled: updateSwitch", app);
        Assert.DoesNotContain("CheckForUpdatesAsync(", app);
    }
}
