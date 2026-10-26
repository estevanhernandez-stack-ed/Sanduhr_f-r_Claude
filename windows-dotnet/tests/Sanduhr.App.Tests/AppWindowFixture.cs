using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>
/// A fact that is skipped, with a message, on GitHub's hosted runner. There the items of a list get no automation
/// names at all, though every one is named on a desktop session; until that is understood these run locally only.
/// Skippable too: a focus test that meets an outside app taking the foreground ends as a skip (see <see cref="AppHost.Run"/>).
/// </summary>
public sealed class DesktopFactAttribute : SkippableFactAttribute
{
    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Skip = "List items get no automation names on the hosted runner; this test runs on a desktop session only.";
    }
}

/// <summary>
/// Real windows over an invented Claude Code profile and a scratch AppData (SANDUHR_DEV_PROFILE), with the sample
/// accounts of demo mode, read the way Narrator reads them. Everything runs on <see cref="AppHost"/>'s thread. Nothing
/// here touches the real profile, settings or vault; Credential Manager is read, never written.
/// </summary>
public abstract class AppWindowFixture : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "sanduhr-names-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly string? _oldDev = Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE");

    private readonly Sanduhr.App.Desk.NowPlayingHub? _oldHub = Sanduhr.App.Desk.NowPlayingHub.Current;
    private readonly Sanduhr.App.Desk.NowPlayingLookService? _oldLooks = Sanduhr.App.Desk.NowPlayingLookService.Current;

    /// <summary>Settings reads the app's one Now playing hub and look service, which follow the PC's live media session.
    /// A test never has them: this clears both, so a hub left by another test (or a player on the PC) cannot reach a window.</summary>
    protected static void NoLiveMedia()
    {
        Sanduhr.App.Desk.NowPlayingHub.Current = null;
        Sanduhr.App.Desk.NowPlayingLookService.Current = null;
    }

    protected string Profile => Path.Combine(Root, "profile");

    /// <summary>A Claude Code home (it has a projects folder) under the scratch profile, with the given settings.json text.</summary>
    protected string Home(string name, string settings = "{}")
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    protected AppWindowFixture()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", Path.Combine(Root, "dev"));
        Directory.CreateDirectory(Path.Combine(Profile, ".claude", "projects"));
        ClaudeCodeProfile.ProfileOverride = Profile;
        ClaudeCodeProfile.ConfigDirOverride = "";
        ClaudeCodeViewModel.ForgetSessionChoice();
        ModsConfigViewModel.ForgetSessionChoice();
        // Settings ▸ Widgets fetches the catalog when it opens: never the real network from a test.
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        NoLiveMedia();
    }

    /// <summary>A network that is never there: every request fails as if the PC were offline.</summary>
    internal sealed class OfflineHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            throw new System.Net.Http.HttpRequestException("offline in tests");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", _oldDev);
        ClaudeCodeProfile.ProfileOverride = null;
        ClaudeCodeProfile.ConfigDirOverride = null;
        WidgetCatalogService.HandlerOverride = null;
        Sanduhr.App.Desk.NowPlayingHub.Current = _oldHub;
        Sanduhr.App.Desk.NowPlayingLookService.Current = _oldLooks;
        ClaudeCodeViewModel.ForgetSessionChoice();
        ModsConfigViewModel.ForgetSessionChoice();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }

    /// <summary>A widget in demo mode: sample accounts and sample meters.</summary>
    protected static WidgetViewModel DemoWidget()
    {
        NoLiveMedia();
        var widget = new WidgetViewModel();
        widget.StartDemo();
        return widget;
    }

    /// <summary>An account registry in memory: nothing reaches Credential Manager.</summary>
    internal sealed class MemoryCredentials : Sanduhr.Core.ICredentialManager
    {
        private readonly Dictionary<string, string> _store = new();
        public string Service => Sanduhr.Core.AccountStore.Service;
        public string? GetPassword(string slot) => _store.GetValueOrDefault(slot);
        public void SetPassword(string slot, string value) => _store[slot] = value;
        public bool DeletePassword(string slot) => _store.Remove(slot);
    }

    /// <summary>Folders under this test's scratch root, so no test reaches the real AppData.</summary>
    protected Sanduhr.Core.Paths ScratchPaths() =>
        new(Path.Combine(Root, "appdata"), Path.Combine(Root, "home"), Path.Combine(Root, "local"));

    /// <summary>A real (not demo) widget over scratch folders and an in-memory registry holding
    /// <paramref name="labels"/>, the first of them active.</summary>
    protected (WidgetViewModel Widget, Sanduhr.Core.AccountStore Accounts) ScratchWidget(params string[] labels)
    {
        NoLiveMedia();
        var accounts = new Sanduhr.Core.AccountStore(new MemoryCredentials());
        foreach (var label in labels)
            accounts.AddAccount(label, "placeholder-" + label);
        if (labels.Length > 0)
            accounts.SetActive(labels[0]);
        return (new WidgetViewModel(ScratchPaths(), accounts), accounts);
    }

    /// <summary>A name that is a type or a record's text rather than words for a person.</summary>
    internal static bool Leaks(string name) =>
        name.Contains("ViewModel") || name.Contains("Sanduhr.App") || name.Contains("Sanduhr.Core") || name.Contains("System.")
        || name.Contains("{ ") || name.Contains(" }") || name.Contains("<>z__");

    /// <summary>
    /// The peer tree, depth first. A peer for an item whose container went away (a list refilled since the last walk, an
    /// item collected on a slow runner) throws from GetName; that peer is stale, not a name, so it is skipped. A peer
    /// that answers is always checked.
    /// </summary>
    protected static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        IList<AutomationPeer>? children = null;
        // A peer keeps the children it first listed, and a page selected since then is not among them unless a UI
        // Automation client is listening. Drop the cache so each walk reads the tree as it is now.
        try { peer.ResetChildrenCache(); } catch (NullReferenceException) { /* a stale item peer */ }
        try { children = peer.GetChildren(); }
        catch (NullReferenceException) { /* a stale item peer */ }
        foreach (var child in children ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    /// <summary>Turns virtualization off under <paramref name="root"/> so every item of every list has a container to name.</summary>
    private static void RealizeEveryItem(DependencyObject root)
    {
        if (root is ItemsControl list) VirtualizingPanel.SetIsVirtualizing(list, false);
        int n = root is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < n; i++)
            RealizeEveryItem(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
    }

    protected static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    protected static void OffScreen(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
    }

    /// <summary>Every UI Automation name under the window, as read now, with every list item realized.</summary>
    protected static List<string> NamesOf(FrameworkElement root)
    {
        // Twice: realizing a list's items can bring more lists into the tree.
        for (int pass = 0; pass < 2; pass++)
        {
            RealizeEveryItem(root);
            root.UpdateLayout();
            Pump();
        }
        var names = new List<string>();
        foreach (var peer in Walk(UIElementAutomationPeer.CreatePeerForElement(root)))
        {
            try { names.Add(peer.GetName() ?? ""); }
            catch (NullReferenceException) { /* a stale item peer: nothing to name */ }
        }
        return names;
    }

    /// <summary>The names that read as a type or a record dump.</summary>
    protected static List<string> LeaksIn(IEnumerable<string> names) => names.Where(Leaks).Distinct().ToList();
}
