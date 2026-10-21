using System.Runtime.ExceptionServices;

namespace Sanduhr.App.Tests;

/// <summary>Runs WPF work on a dedicated STA thread and rethrows whatever it threw.</summary>
internal static class Sta
{
    public static void Run(Action work)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
