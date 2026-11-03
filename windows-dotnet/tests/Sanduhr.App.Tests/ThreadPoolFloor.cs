namespace Sanduhr.App.Tests;

/// <summary>
/// The thread pool starts with as many threads as cores and adds more slowly, so on a two-core CI runner a burst of
/// blocking pool work held a pool callback for seconds: SingleInstance's answer and a Task.Delay continuation each ran
/// past a 10 s hand-off. A floor of ready threads for this assembly keeps pool callbacks prompt; it changes no product code.
/// </summary>
internal static class ThreadPoolFloor
{
    private const int Floor = 32;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Raise()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(Math.Max(workers, Floor), Math.Max(io, Floor));
    }
}
