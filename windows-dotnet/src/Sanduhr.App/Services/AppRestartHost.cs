using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The running app's half of Restart to update (<see cref="IRestartHost"/>): the 3.7.2 WebView2 teardown,
/// the single-instance hand-off, Windows' restart registration and the self-relaunch. App supplies the
/// pieces that reach its windows and view model.
/// </summary>
internal sealed class AppRestartHost : IRestartHost
{
    private readonly Func<Task> _releaseBrowsers;
    private readonly Action _resumeBrowsers;
    private readonly SingleInstance? _instance;
    private readonly Action _relaunchAndQuit;
    private readonly Action<string>? _log;

    public AppRestartHost(Func<Task> releaseBrowsers, Action resumeBrowsers, SingleInstance? instance, Action relaunchAndQuit, Action<string>? log)
    {
        _releaseBrowsers = releaseBrowsers;
        _resumeBrowsers = resumeBrowsers;
        _instance = instance;
        _relaunchAndQuit = relaunchAndQuit;
        _log = log;
    }

    public Task ReleaseBrowsersAsync() => _releaseBrowsers();

    public void ResumeBrowsers() => _resumeBrowsers();

    // A launch while the update runs (Windows' relaunch, or the person's) waits for this process to
    // exit and then takes over, instead of being told "already running" by a process about to close.
    public void HoldInstance() => _instance?.StopAnswering();

    public void ResumeInstance() => _instance?.ResumeAnswering();

    public bool RegisterRestart() => RestartNative.RegisterForUpdate();

    public void UnregisterRestart() => RestartNative.Unregister();

    public void RelaunchAndQuit() => _relaunchAndQuit();

    public void Log(string message) => _log?.Invoke(message);
}
