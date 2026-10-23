using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Sanduhr.App.Services;

namespace Sanduhr.App.Tests;

/// <summary>
/// A WebView2 browser process left running after Sanduhr quits keeps the package in use, and the next update
/// fails with 0x80073D02. The guard ends adopted processes with Sanduhr (the job) and on a quit (the bounded wait).
/// A stand-in child process plays the browser here; the last test drives a real WebView2 on a desktop session.
/// </summary>
public class BrowserProcessGuardTests
{
    /// <summary>A child that runs about a minute unless ended.</summary>
    private static Process LongChild() =>
        Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;

    private static Process ShortChild() =>
        Process.Start(new ProcessStartInfo("ping.exe", "-n 2 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;

    [Fact]
    public void Closing_the_guard_ends_an_adopted_process_the_way_an_exit_or_crash_does()
    {
        using var child = LongChild();
        var guard = new BrowserProcessGuard();
        Assert.True(guard.Adopt(child.Id));

        guard.Dispose(); // what Windows does to the job handle when Sanduhr exits, crash or not

        Assert.True(child.WaitForExit(5000), "the adopted process outlived its guard");
    }

    [Fact]
    public async Task A_quit_ends_an_adopted_process_still_running_after_the_wait()
    {
        using var child = LongChild();
        using var guard = new BrowserProcessGuard();
        guard.Adopt(child.Id);

        int ended = await guard.WaitForExitAsync(TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, ended);
        Assert.True(child.WaitForExit(5000));
        Assert.Equal(0, guard.RunningCount);
    }

    [Fact]
    public async Task A_process_that_exits_by_itself_inside_the_wait_is_not_ended()
    {
        using var child = ShortChild();
        using var guard = new BrowserProcessGuard();
        guard.Adopt(child.Id);

        int ended = await guard.WaitForExitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(0, ended);
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task Only_adopted_processes_are_ever_ended()
    {
        using var adopted = LongChild();
        using var stranger = LongChild();
        try
        {
            using (var guard = new BrowserProcessGuard())
            {
                guard.Adopt(adopted.Id);
                await guard.WaitForExitAsync(TimeSpan.FromMilliseconds(200));
            }
            Assert.True(adopted.WaitForExit(5000));
            Assert.False(stranger.HasExited);
        }
        finally
        {
            try { stranger.Kill(); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void The_exit_path_wait_ends_survivors_too()
    {
        using var child = LongChild();
        using var guard = new BrowserProcessGuard();
        guard.Adopt(child.Id);

        Assert.Equal(1, guard.WaitForExit(TimeSpan.FromMilliseconds(200)));
        Assert.True(child.WaitForExit(5000));
    }

    [Fact]
    public void A_restart_to_update_holds_new_browsers_until_it_is_called_off()
    {
        using var guard = new BrowserProcessGuard();
        Assert.False(guard.ShuttingDown);

        guard.BeginHold();
        Assert.True(guard.ShuttingDown); // the transport refuses to start a browser

        guard.EndHold(); // the install was cancelled
        Assert.False(guard.ShuttingDown);

        guard.BeginShutdown();
        guard.BeginHold();
        guard.EndHold();
        Assert.True(guard.ShuttingDown); // a quit already begun stays begun
    }

    [Fact]
    public void Adopting_a_process_that_is_gone_is_refused_quietly()
    {
        using var guard = new BrowserProcessGuard();
        var child = ShortChild();
        int id = child.Id;
        child.WaitForExit();
        child.Dispose();

        // The id may already be reused by an unrelated process; either way nothing throws.
        _ = guard.Adopt(id);
        Assert.False(guard.Adopt(-1));
    }

    /// <summary>
    /// The real thing: a hidden WebView2 like the usage transport, disposed the way a quit disposes it. Its browser
    /// process must be gone within the quit's wait, by itself, with no user-data folder left in use.
    /// </summary>
    [DesktopFact]
    public void A_disposed_WebView2_leaves_no_browser_process_behind()
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { return; } // no runtime on this machine: nothing to prove

        var dataDir = Path.Combine(Path.GetTempPath(), "sanduhr-wv2-" + Guid.NewGuid().ToString("N")[..10]);
        using var guard = new BrowserProcessGuard();
        int browserPid = 0;
        int ended = -1;
        Exception? failure = null;
        try
        {
            Sta.Run(() =>
            {
                var host = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Width = 200, Height = 200 };
                var web = new WebView2();
                host.Content = web;
                host.Show();
                var frame = new System.Windows.Threading.DispatcherFrame();
                // WebView2 needs the event loop running: start inside the frame.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => _ = Run());
                System.Windows.Threading.Dispatcher.PushFrame(frame);

                async Task Run()
                {
                    try
                    {
                        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                        await web.EnsureCoreWebView2Async(env);
                        browserPid = (int)web.CoreWebView2.BrowserProcessId;
                        guard.Adopt(web.CoreWebView2);
                        web.Dispose();
                        host.Close();
                        ended = await guard.WaitForExitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (Exception e) { failure = e; }
                    finally { frame.Continue = false; }
                }
            });

            Assert.Null(failure);
            Assert.NotEqual(0, browserPid);
            Assert.Equal(0, ended); // it exited by itself; nothing had to be ended
            Assert.Equal(0, guard.RunningCount);
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
