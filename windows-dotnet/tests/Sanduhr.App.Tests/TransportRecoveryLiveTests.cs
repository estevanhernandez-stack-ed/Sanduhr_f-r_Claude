using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Issue #331, against a real WebView2: the transport's browser process is killed, the client reports the
/// loss once and tears the dead host down, and the next request brings up a new browser. The page is
/// about:blank on a scratch profile, so nothing reaches claude.ai and no real profile is touched.
/// </summary>
public class TransportRecoveryLiveTests
{
    /// <summary>Runs <paramref name="body"/> on an STA thread with a running dispatcher, like the app's UI thread.</summary>
    private static void OnUiThread(Func<Task> body)
    {
        Exception? failure = null;
        Sta.Run(() =>
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try { await body(); }
                catch (Exception e) { failure = e; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
        });
        if (failure is not null)
            throw new Xunit.Sdk.XunitException("UI-thread body failed: " + failure);
    }

    private static bool RuntimePresent()
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); return true; }
        catch (WebView2RuntimeNotFoundException) { return false; }
    }

    private static async Task<bool> Until(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(100);
        }
        return true;
    }

    [DesktopFact]
    public void A_killed_browser_is_reported_once_and_the_next_request_rebuilds_it()
    {
        if (!RuntimePresent()) return; // no runtime on this machine: nothing to prove

        var root = Path.Combine(Path.GetTempPath(), "sanduhr-transport-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        var losses = new List<string>();
        int firstPid = 0, secondPid = 0;
        bool aliveAfterLoss = true;
        try
        {
            OnUiThread(async () =>
            {
                using var client = new WebView2ApiClient("not-a-real-key", null, Path.Combine(root, "webview2-fetch"))
                {
                    StartPage = "about:blank",
                    LogPath = Path.Combine(root, "fetch-debug.log"),
                };
                client.TransportLost += losses.Add;

                await client.EnsureBrowserAsync();
                firstPid = client.BrowserProcessId;
                Assert.True(client.BrowserAlive);
                Assert.NotEqual(0, firstPid);

                using (var browser = Process.GetProcessById(firstPid))
                    browser.Kill(entireProcessTree: true); // what End task, a crash or a runtime update does

                Assert.True(await Until(() => losses.Count > 0, TimeSpan.FromSeconds(15)), "the loss was never reported");
                await Task.Delay(1000); // the environment's own exit report lands too; it must not count twice
                aliveAfterLoss = client.BrowserAlive;

                await client.EnsureBrowserAsync(); // the next tick's request
                secondPid = client.BrowserProcessId;
                Assert.True(client.BrowserAlive);
            });

            Assert.Single(losses);
            Assert.False(aliveAfterLoss);
            Assert.NotEqual(0, secondPid);
            Assert.NotEqual(firstPid, secondPid);
            var log = File.ReadAllText(Path.Combine(root, "fetch-debug.log"));
            Assert.Contains("transport: browser gone kind=", log);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [DesktopFact]
    public void A_release_for_restart_is_not_reported_as_a_loss()
    {
        if (!RuntimePresent()) return;

        var root = Path.Combine(Path.GetTempPath(), "sanduhr-transport-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        var losses = new List<string>();
        int pid = 0;
        bool exited = false;
        try
        {
            OnUiThread(async () =>
            {
                using var client = new WebView2ApiClient("not-a-real-key", null, Path.Combine(root, "webview2-fetch"))
                {
                    StartPage = "about:blank",
                    LogPath = Path.Combine(root, "fetch-debug.log"),
                };
                client.TransportLost += losses.Add;
                await client.EnsureBrowserAsync();
                pid = client.BrowserProcessId;
                using var browser = Process.GetProcessById(pid);

                client.Release();

                exited = await Until(() => browser.HasExited, TimeSpan.FromSeconds(10));
                await Task.Delay(500);
                Assert.False(client.BrowserAlive);
            });

            Assert.True(exited, "the released browser did not exit by itself");
            Assert.Empty(losses);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
