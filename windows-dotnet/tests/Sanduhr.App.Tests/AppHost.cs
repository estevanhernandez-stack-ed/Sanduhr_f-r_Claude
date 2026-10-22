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

    public static void Run(Action work)
    {
        ExceptionDispatchInfo? failure = null;
        Dispatch.Value.Invoke(() =>
        {
            try { work(); }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
        });
        failure?.Throw();
    }
}
