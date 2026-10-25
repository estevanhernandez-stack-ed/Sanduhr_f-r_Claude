using System.IO;
using System.IO.MemoryMappedFiles;
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
    private static (Thread Thread, Task<SingleInstance> Claimed) HoldOnThread(string dir, ManualResetEventSlim release, Action? onShow = null, TimeSpan? handoff = null,
        Action<string?>? onMessage = null)
    {
        var claimed = new TaskCompletionSource<SingleInstance>();
        var thread = new Thread(() =>
        {
            var instance = SingleInstance.Claim(dir, handoff ?? TimeSpan.FromSeconds(5));
            if (onMessage is not null) instance.Listen(onMessage);
            else if (onShow is not null) instance.Listen(onShow);
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
    public async Task A_restart_to_update_that_did_not_happen_answers_launches_again()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        using var shown = new ManualResetEventSlim();
        var (thread, claimed) = HoldOnThread(dir, release, onShow: shown.Set);
        var first = await claimed;

        first.StopAnswering();  // Restart to update: the relaunch must be able to take over
        first.ResumeAnswering(); // the install was cancelled: Sanduhr keeps running

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5));
        Assert.False(second.IsPrimary);
        Assert.True(shown.Wait(1000), "the running instance did not answer after the restart was called off");
        release.Set();
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

    [Fact]
    public async Task A_second_launch_hands_its_settings_link_to_the_first()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
        Assert.True((await claimed).IsPrimary);

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), "desk#pieces");

        Assert.False(second.IsPrimary);
        Assert.Equal("desk#pieces", await got.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task A_plain_launch_after_a_link_hands_over_nothing()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        using var got = new System.Collections.Concurrent.BlockingCollection<string>();
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.Add(m ?? "(none)"));
        Assert.True((await claimed).IsPrimary);

        using (SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), "about#version")) { }
        using (SingleInstance.Claim(dir, TimeSpan.FromSeconds(5))) { }

        Assert.True(got.TryTake(out var first, 2000));
        Assert.Equal("about#version", first);
        Assert.True(got.TryTake(out var second, 2000));
        Assert.Equal("(none)", second);
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task A_link_too_long_for_the_mailbox_is_dropped_not_cut()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
        Assert.True((await claimed).IsPrimary);

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), new string('a', SingleInstance.MaxMessageBytes + 1));

        Assert.False(second.IsPrimary);
        Assert.Null(await got.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.Set();
        thread.Join();
    }

    // The hand-off never stops or delays a launch. Each test below uses its own folder, so its own kernel-object names
    // (NameFor hashes the folder): none touches the running Sanduhr's.

    [Fact]
    public async Task A_foreign_or_too_small_mapping_under_the_mailbox_name_falls_back_to_the_plain_show()
    {
        var dir = Folder();
        // Something else already owns the mailbox name with a 16-byte file-backed mapping (a page-file one would round up to a
        // page), so a 1024-byte view over it cannot be made.
        Directory.CreateDirectory(dir);
        FileStream? file = null;
        MemoryMappedFile? foreign = null;
        using var release = new ManualResetEventSlim();
        Thread? thread = null;
        try
        {
            file = new FileStream(Path.Combine(dir, "foreign.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            foreign = MemoryMappedFile.CreateFromFile(file, SingleInstance.NameFor(dir) + ".Message", 16,
                MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
            var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var (holder, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
            thread = holder;
            Assert.True((await claimed).IsPrimary, "a primary that cannot set up its mailbox still runs");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(10), "desk#pieces");

            Assert.False(second.IsPrimary);
            Assert.Null(await got.Task.WaitAsync(TimeSpan.FromSeconds(2))); // the plain show, no link
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"the fall-back took {clock.Elapsed}");
        }
        finally
        {
            release.Set();
            thread?.Join();
            foreign?.Dispose();
            file?.Dispose();
            try { Directory.Delete(dir, true); }
            catch { /* a stray temp folder is not a test failure */ }
        }
    }

    [Fact]
    public async Task A_busy_post_mutex_skips_the_post_quickly()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
        Assert.True((await claimed).IsPrimary);

        using var held = HoldPostMutex(dir);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(10), "desk#pieces");
        var took = clock.Elapsed;

        Assert.False(second.IsPrimary);
        Assert.Null(await got.Task.WaitAsync(TimeSpan.FromSeconds(2))); // asked plainly, the link was not posted
        Assert.True(took < TimeSpan.FromSeconds(3), $"a busy .Post mutex held the launch for {took}");
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task The_whole_hand_off_runs_under_one_deadline()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var (thread, claimed) = HoldOnThread(dir, release, onShow: () => { });
        var first = await claimed;
        first.StopAnswering(); // quitting: it holds the claim and never answers

        using var held = HoldPostMutex(dir); // and another launch is mid-hand-off
        var handoff = TimeSpan.FromSeconds(1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var third = SingleInstance.Claim(dir, handoff, "desk#pieces");
        var took = clock.Elapsed;

        Assert.False(third.IsPrimary); // never got the claim, and gave up at the deadline
        Assert.True(took >= TimeSpan.FromMilliseconds(800), $"gave up early, after {took}");
        Assert.True(took < handoff + TimeSpan.FromMilliseconds(500), $"waited {took} on a 1 s deadline");
        release.Set();
        thread.Join();
    }

    /// <summary>Holds the folder's <c>.Post</c> mutex on a thread of its own (a mutex belongs to its thread).</summary>
    private static IDisposable HoldPostMutex(string dir)
    {
        var taken = new ManualResetEventSlim();
        var free = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var post = new Mutex(false, SingleInstance.NameFor(dir) + ".Post");
            post.WaitOne();
            taken.Set();
            free.Wait();
            post.ReleaseMutex();
        }) { IsBackground = true };
        thread.Start();
        Assert.True(taken.Wait(2000), "the test could not take the .Post mutex");
        return new Releaser(() => { free.Set(); thread.Join(); taken.Dispose(); free.Dispose(); });
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
