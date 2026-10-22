using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

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
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", _oldDev);
        ClaudeCodeProfile.ProfileOverride = null;
        ClaudeCodeProfile.ConfigDirOverride = null;
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

    protected static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
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

    /// <summary>Every UI Automation name under the window, as read now.</summary>
    protected static List<string> NamesOf(FrameworkElement root)
    {
        root.UpdateLayout();
        Pump();
        return Walk(UIElementAutomationPeer.CreatePeerForElement(root)).Select(p => p.GetName() ?? "").ToList();
    }

    /// <summary>The names that read as a type or a record dump.</summary>
    protected static List<string> LeaksIn(IEnumerable<string> names) => names.Where(Leaks).Distinct().ToList();
}
