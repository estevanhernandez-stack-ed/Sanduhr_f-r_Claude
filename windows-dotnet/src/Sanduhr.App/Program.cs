using Microsoft.Toolkit.Uwp.Notifications;
using Velopack;

namespace Sanduhr.App;

/// <summary>
/// Process entry point. Velopack requires <c>VelopackApp.Build().Run()</c> as the FIRST
/// thing Main does so its install / uninstall / restart-after-update hooks fire before WPF
/// spins up — on those hook invocations Velopack does its work and exits the process, so
/// the WPF app below never constructs. On a normal launch the hook is a no-op and we fall
/// through to building and running the WPF <see cref="App"/> exactly as the auto-generated
/// entry point would have.
/// <para>
/// The auto-generated WPF Main (App.g.cs) is suppressed by <c>&lt;StartupObject&gt;</c> in
/// the csproj pointing here. <see cref="App.OnStartup"/> still runs on <c>app.Run()</c>, so
/// all tray / window / fetch wiring is unchanged.
/// </para>
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // FIRST line of Main, per Velopack's contract. Handles the hook events Velopack passes
        // on its own command lines (--veloapp-install, --veloapp-updated, etc.) and returns
        // immediately on a normal run.
        VelopackApp.Build()
            .SetArgs(args)
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { ToastNotificationManagerCompat.Uninstall(); }
                catch { /* best-effort — uninstall must never fail on toast cleanup */ }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
