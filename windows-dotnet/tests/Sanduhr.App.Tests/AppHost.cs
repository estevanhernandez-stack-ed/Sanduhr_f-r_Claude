using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Sanduhr.App.Tests;

/// <summary>
/// One real <see cref="Sanduhr.App.App"/> (its XAML resources, never <c>Run</c>) on a dedicated STA thread, for tests
/// that realize whole windows: Settings reaches for application-level templates and its icon through pack URIs. A process
/// can create one Application, so every test that needs it runs its work here.
/// </summary>
internal static class AppHost
{
    /// <summary>
    /// Before any test runs: WPF resolves "/Assets/Sanduhr.ico" against Application.ResourceAssembly, which in a test host
    /// is the host. Pointing it at the app's assembly once, up front, keeps the answer from depending on which test
    /// touched a pack URI first.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void PointResourcesAtTheApp() =>
        typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(null, typeof(Sanduhr.App.App).Assembly);

    private static readonly Lazy<Dispatcher> Dispatch = new(Start);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        ExceptionDispatchInfo? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                PointResourcesAtTheApp();
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                var app = new Sanduhr.App.App();
                app.InitializeComponent();
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
            ready.Set();
            if (failure is null) Dispatcher.Run();
        })
        { IsBackground = true, Name = "Sanduhr.App.Tests app thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        failure?.Throw();
        return dispatcher!;
    }

    /// <summary>
    /// Runs the work on the app thread. A failure in a run that routed real keyboard focus (<see cref="NoActivate.RouteKeyboard"/>)
    /// while an outside app held or took the foreground is rethrown as a skip (<see cref="SkipException"/>): the tests never
    /// change the foreground, so a change means interference. Any other failure rethrows as it was.
    /// </summary>
    public static void Run(Action work, ForegroundProbe? probe = null)
    {
        ExceptionDispatchInfo? failure = null;
        Dispatch.Value.Invoke(() =>
        {
            using var watch = ForegroundWatch.Begin(probe);
            try { work(); }
            catch (Exception e)
            {
                var reason = watch.RoutesFocus ? watch.Interference() : null;
                failure = ExceptionDispatchInfo.Capture(
                    reason is not null ? new SkipException(reason)
                    : watch.RoutesFocus ? new Xunit.Sdk.XunitException($"{e.Message}\n{watch.Diagnosis()}", e)
                    : e);
            }
        });
        failure?.Throw();
    }
}
