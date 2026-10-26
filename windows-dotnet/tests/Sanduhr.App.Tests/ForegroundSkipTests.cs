using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>
/// A focus test that fails while an outside app has the foreground is a skip, not a failure; a wrong result with the
/// foreground untouched still fails. The foreground query is a seam, so a fact can play the outside app.
/// </summary>
public class ForegroundSkipTests
{
    private static readonly IntPtr Ours = new(1), Theirs = new(2);

    private static ForegroundProbe Probe(Func<IntPtr> query, int pid = 4242) =>
        new(query, h => h == Theirs ? (pid, "afk-tool") : (Environment.ProcessId, "testhost"));

    private static Exception Outcome(ForegroundProbe probe, Action work) =>
        Assert.ThrowsAny<Exception>(() => AppHost.Run(work, probe));

    [Fact]
    public void A_focus_test_that_fails_after_the_foreground_changed_is_a_skip_naming_the_process()
    {
        var fg = Ours;
        var e = Outcome(Probe(() => fg), () =>
        {
            ForegroundWatch.Current!.MarkFocusTest();
            fg = Theirs;
            Assert.Fail("focused nothing");
        });
        Assert.IsType<SkipException>(e);
        Assert.Equal("Skipped: the foreground changed to afk-tool (4242) during the test; focus results are not reliable.", e.Message);
    }

    [Fact]
    public void A_change_that_reverted_before_the_failure_is_still_a_skip()
    {
        var e = Outcome(Probe(() => Ours), () =>
        {
            ForegroundWatch.Current!.MarkFocusTest();
            ForegroundWatch.Current.Note(Theirs);   // what the foreground hook reports
            Assert.Fail("focused nothing");
        });
        Assert.IsType<SkipException>(e);
        Assert.Contains("afk-tool", e.Message);
    }

    [Fact]
    public void A_wrong_focus_with_the_foreground_untouched_still_fails()
    {
        var e = Outcome(Probe(() => Ours), () =>
        {
            ForegroundWatch.Current!.MarkFocusTest();
            Assert.Fail("focused nothing");
        });
        Assert.IsNotType<SkipException>(e);
        Assert.Contains("focused nothing", e.Message);
        // The failure carries what the foreground did, so the next real one says whether an outside app was involved.
        Assert.Contains("[foreground at start: testhost", e.Message);
        Assert.Contains("foreground events during the test: 0]", e.Message);
    }

    [Fact]
    public void A_change_in_a_test_that_never_routes_focus_still_fails()
    {
        var fg = Ours;
        var e = Outcome(Probe(() => fg), () => { fg = Theirs; Assert.Fail("not a focus test"); });
        Assert.IsNotType<SkipException>(e);
    }

    [Fact]
    public void The_foreground_moving_to_our_own_process_is_not_interference()
    {
        var fg = Ours;
        var e = Outcome(Probe(() => fg, pid: Environment.ProcessId), () =>
        {
            ForegroundWatch.Current!.MarkFocusTest();
            fg = Theirs;
            Assert.Fail("focused nothing");
        });
        Assert.IsNotType<SkipException>(e);
    }
}
