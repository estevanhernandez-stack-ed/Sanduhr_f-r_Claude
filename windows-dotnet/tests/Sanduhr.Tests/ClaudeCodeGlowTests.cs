using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeCodeGlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);
    private static readonly GlowSwitches BothOn = new(Waiting: true, Done: true);

    [Fact]
    public void OneGlowPerKindEveryTwentySeconds()
    {
        var l = new ClaudeCodeGlowLimiter();
        Assert.True(l.Allow(ClaudeCodeEvent.Waiting, At(0)));
        Assert.False(l.Allow(ClaudeCodeEvent.Waiting, At(1)));
        Assert.False(l.Allow(ClaudeCodeEvent.Waiting, At(19.9)));
        Assert.True(l.Allow(ClaudeCodeEvent.Waiting, At(20)));
        Assert.False(l.Allow(ClaudeCodeEvent.Waiting, At(39)));
    }

    [Fact]
    public void DoneIsHeldBackJustAfterAWaitingGlow()
    {
        var l = new ClaudeCodeGlowLimiter();
        Assert.True(l.Allow(ClaudeCodeEvent.Waiting, At(0)));
        Assert.False(l.Allow(ClaudeCodeEvent.Done, At(4.9)));
        Assert.True(l.Allow(ClaudeCodeEvent.Done, At(5)));
        Assert.False(l.Allow(ClaudeCodeEvent.Done, At(10)));
        var m = new ClaudeCodeGlowLimiter();
        Assert.True(m.Allow(ClaudeCodeEvent.Done, At(0)));
        Assert.True(m.Allow(ClaudeCodeEvent.Waiting, At(1)));
    }

    [Fact]
    public void OnlyAGlowThatHappensCounts()
    {
        var l = new ClaudeCodeGlowLimiter();
        Assert.True(l.Allow(ClaudeCodeEvent.Done, At(0)));
        Assert.False(l.Allow(ClaudeCodeEvent.Done, At(10)));
        Assert.True(l.Allow(ClaudeCodeEvent.Done, At(20)));
    }

    [Fact]
    public void AClockSetBackAllowsIt()
    {
        var l = new ClaudeCodeGlowLimiter();
        Assert.True(l.Allow(ClaudeCodeEvent.Waiting, At(100)));
        Assert.True(l.Allow(ClaudeCodeEvent.Waiting, At(50)));
    }

    [Fact]
    public void OffByDefaultAndTerminalOptionOn()
    {
        var s = new GlowSwitches();
        Assert.False(s.Waiting);
        Assert.False(s.Done);
        Assert.True(s.SkipTerminal);
        var l = new ClaudeCodeGlowLimiter();
        Assert.False(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Waiting, s, null, ref l, T0));
        Assert.False(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Done, s, null, ref l, T0));
        Assert.Equal(new ClaudeCodeGlowLimiter(), l);
    }

    [Fact]
    public void EachSwitchGovernsOnlyItsEvent()
    {
        var l = new ClaudeCodeGlowLimiter();
        var waiting = new GlowSwitches(Waiting: true);
        Assert.True(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Waiting, waiting, null, ref l, T0));
        Assert.False(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Done, waiting, null, ref l, At(30)));
        var done = new GlowSwitches(Done: true);
        Assert.True(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Done, done, null, ref l, At(30)));
        Assert.False(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Waiting, done, null, ref l, At(60)));
    }

    [Fact]
    public void ATerminalInFrontSkipsTheGlowUnlessTheOptionIsOff()
    {
        var l = new ClaudeCodeGlowLimiter();
        Assert.False(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Waiting, BothOn, "WindowsTerminal", ref l, T0));
        // Skipped, so not remembered: the same event from another app glows at once.
        Assert.True(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Waiting, BothOn, "chrome", ref l, At(1)));
        var off = BothOn with { SkipTerminal = false };
        Assert.True(ClaudeCodeGlowRules.Decide(ClaudeCodeEvent.Done, off, "WindowsTerminal", ref l, At(30)));
    }

    [Fact]
    public void TheTerminalList()
    {
        string[] expected =
        [
            "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "wezterm-gui", "alacritty",
            "kitty", "mintty", "Hyper", "Tabby", "warp", "Code", "Code - Insiders", "Cursor", "Zed", "idea64",
            "rider64", "devenv", "windsurf",
        ];
        Assert.Equal(expected.Length, ClaudeCodeGlowRules.TerminalProcessNames.Count);
        foreach (var n in expected)
        {
            Assert.True(ClaudeCodeGlowRules.IsTerminal(n), n);
            Assert.True(ClaudeCodeGlowRules.IsTerminal(n.ToUpperInvariant()), n);
            Assert.True(ClaudeCodeGlowRules.IsTerminal(n + ".exe"), n);
        }
        foreach (var n in new[] { "chrome", "explorer", "Sanduhr", "", " " })
            Assert.False(ClaudeCodeGlowRules.IsTerminal(n), n);
        Assert.False(ClaudeCodeGlowRules.IsTerminal(null));
    }

    [Fact]
    public void SignalFileNames()
    {
        Assert.Equal("glow-waiting.signal", ClaudeCodeSignals.WaitingFileName);
        Assert.Equal("glow-done.signal", ClaudeCodeSignals.DoneFileName);
        Assert.Equal(ClaudeCodeEvent.Waiting, ClaudeCodeSignals.EventForFileName("glow-waiting.signal"));
        Assert.Equal(ClaudeCodeEvent.Done, ClaudeCodeSignals.EventForFileName("GLOW-DONE.SIGNAL"));
        Assert.Null(ClaudeCodeSignals.EventForFileName("glow-stop.signal"));
        Assert.Null(ClaudeCodeSignals.EventForFileName(null));
        foreach (var e in Enum.GetValues<ClaudeCodeEvent>())
            Assert.Equal(e, ClaudeCodeSignals.EventForFileName(ClaudeCodeSignals.FileName(e)));
    }
}
