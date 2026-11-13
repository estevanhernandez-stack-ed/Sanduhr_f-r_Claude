using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeLineParserTests
{
    private static BurnEvent? Parse(string line, out bool bad) => new ClaudeLineParser().Parse(line, out bad);

    [Fact]
    public void An_assistant_line_becomes_an_event_with_cache_model_cwd_and_skill()
    {
        var e = Parse("""{"type":"assistant","timestamp":"2026-11-10T10:00:00Z","cwd":"C:\\p\\api","attributionSkill":"s","message":{"model":"m","usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":7,"cache_creation_input_tokens":3}}}""", out var bad);
        Assert.False(bad);
        Assert.NotNull(e);
        Assert.Equal((100L, 50L, 7L, 3L), (e!.Value.Input, e.Value.Output, e.Value.CacheRead, e.Value.CacheCreation));
        Assert.Equal(("m", @"C:\p\api", "s"), (e.Value.Model, e.Value.Cwd, e.Value.Skill));
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T10:00:00Z"), e.Value.Timestamp);
    }

    [Theory]
    [InlineData("""{"type":"user","message":"assistant"}""")]
    [InlineData("""{"type":"assistant","message":{"model":"m"}}""")]
    [InlineData("""{"type":"assistant","message":{"usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"summary"}""")]
    public void Lines_without_a_timed_usage_carry_no_event_and_are_not_bad(string line)
    {
        Assert.Null(Parse(line, out var bad));
        Assert.False(bad);
    }

    [Theory]
    [InlineData("""{"type":"assistant", broken""")]
    [InlineData("""["assistant"]""")]
    public void Assistant_looking_lines_that_do_not_parse_are_bad(string line)
    {
        Assert.Null(Parse(line, out var bad));
        Assert.True(bad);
    }

    [Fact]
    public void Claude_parsing_keeps_no_state() => Assert.Null(new ClaudeLineParser().SaveState());
}
