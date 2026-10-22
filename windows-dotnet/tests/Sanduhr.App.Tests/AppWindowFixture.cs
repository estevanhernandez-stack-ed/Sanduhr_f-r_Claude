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
/// </summary>
public sealed class DesktopFactAttribute : FactAttribute
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

    protected string Profile => Path.Combine(Root, "profile");

    protected AppWindowFixture()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", Path.Combine(Root, "dev"));
        Directory.CreateDirectory(Path.Combine(Profile, ".claude", "projects"));
        ClaudeCodeProfile.ProfileOverride = Profile;
        ClaudeCodeProfile.ConfigDirOverride = "";
        ClaudeCodeViewModel.ForgetSessionChoice();
        ModsConfigViewModel.ForgetSessionChoice();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", _oldDev);
        ClaudeCodeProfile.ProfileOverride = null;
        ClaudeCodeProfile.ConfigDirOverride = null;
        ClaudeCodeViewModel.ForgetSessionChoice();
        ModsConfigViewModel.ForgetSessionChoice();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }

    /// <summary>A widget in demo mode: sample accounts and sample meters.</summary>
    protected static WidgetViewModel DemoWidget()
    {
        var widget = new WidgetViewModel();
        widget.StartDemo();
        return widget;
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
