using System.Runtime.CompilerServices;

namespace Sanduhr.Mcp.Tests;

/// <summary>
/// The handoff tests run ToolLogic, whose wait for the widget's answer blocks a thread-pool thread
/// (Thread.Sleep polling, as the real single-session server does), while a fake widget answers from
/// <c>Task.Run</c> on the same pool. With xUnit running classes in parallel on a two-core CI runner, the
/// pool's slow thread injection let every blocked wait outlast the fake widgets' turn, and the server
/// timed out with "queued" (PR #310 and #312 CI). Raising the pool's minimum for this test assembly
/// removes the starvation; the server itself is unchanged.
/// </summary>
internal static class ThreadPoolWarmup
{
    [ModuleInitializer]
    internal static void Init()
    {
        ThreadPool.GetMinThreads(out var worker, out var io);
        ThreadPool.SetMinThreads(Math.Max(worker, 64), Math.Max(io, 64));
    }
}
