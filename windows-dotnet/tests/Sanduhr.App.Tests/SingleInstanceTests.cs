using System.IO;
using Sanduhr.App.Services;

namespace Sanduhr.App.Tests;

/// <summary>
/// Two tray icons were seen once: two Sanduhrs on one data folder, each with its own fetch transport on the same
/// WebView2 profiles. The claim keeps one per data folder. A mutex belongs to its thread, so each claim here runs on
/// its own thread, as two processes would.
/// </summary>
public class SingleInstanceTests
{
    private static string Folder() => Path.Combine(Path.GetTempPath(), "sanduhr-instance-" + Guid.NewGuid().ToString("N")[..10]);

    /// <summary>Claims on a thread of its own and holds the claim until <paramref name="release"/> is set.</summary>
    private static (Thread Thread, Task<SingleInstance> Claimed) HoldOnThread(string dir, ManualResetEventSlim release, Action? onShow = null, TimeSpan? handoff = null)
    {
        var claimed = new TaskCompletionSource<SingleInstance>();
        var thread = new Thread(() =>
        {
            var instance = SingleInstance.Claim(dir, handoff ?? TimeSpan.FromSeconds(5));
            if (onShow is not null) instance.Listen(onShow);
            claimed.SetResult(instance);
            release.Wait();
            instance.Dispose();
        }) { IsBackground = true };
        thread.Start();
        return (thread, claimed.Task);
    }

    [Fact]
    public void Dev_profile_and_installed_app_never_share_a_claim()
    {
        Assert.NotEqual(SingleInstance.NameFor(@"C:\Users\someone\AppData\Roaming\Sanduhr"), SingleInstance.NameFor(@"C:\temp\sanduhr-dev"));
        Assert.Equal(SingleInstance.NameFor(@"C:\Users\Someone\AppData\Roaming\Sanduhr\"), SingleInstance.NameFor(@"c:\users\someone\appdata\roaming\sanduhr"));
        Assert.StartsWith(@"Local\Sanduhr.Instance.", SingleInstance.NameFor(@"C:\x"));
    }

    [Fact]
    public async Task A_second_launch_shows_the_first_and_does_not_start()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        using var shown = new ManualResetEventSlim();
        var (thread, claimed) = HoldOnThread(dir, release, onShow: shown.Set);
        var first = await claimed;
        Assert.True(first.IsPrimary);

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5));

        Assert.False(second.IsPrimary);
        Assert.True(shown.Wait(1000), "the running instance was not asked to show its widget");
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task A_launch_during_a_quit_waits_for_it_and_takes_over()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var (thread, claimed) = HoldOnThread(dir, release, onShow: () => { });
        var first = await claimed;
        first.StopAnswering(); // quitting: waiting for its browser processes

        _ = Task.Delay(300).ContinueWith(_ => release.Set());
        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(10));

        Assert.True(second.IsPrimary);
        thread.Join();
    }

    [Fact]
    public async Task Other_data_folders_are_not_blocked()
    {
        using var release = new ManualResetEventSlim();
        var (thread, claimed) = HoldOnThread(Folder(), release);
        Assert.True((await claimed).IsPrimary);

        using var other = SingleInstance.Claim(Folder(), TimeSpan.FromSeconds(1));

        Assert.True(other.IsPrimary);
        release.Set();
        thread.Join();
    }

    [Fact]
    public void An_instance_that_died_holding_the_claim_does_not_block_the_next()
    {
        var dir = Folder();
        var thread = new Thread(() => SingleInstance.Claim(dir, TimeSpan.Zero)); // never disposed: the mutex is abandoned
        thread.Start();
        thread.Join();

        using var next = SingleInstance.Claim(dir, TimeSpan.FromSeconds(2));

        Assert.True(next.IsPrimary);
    }
}
