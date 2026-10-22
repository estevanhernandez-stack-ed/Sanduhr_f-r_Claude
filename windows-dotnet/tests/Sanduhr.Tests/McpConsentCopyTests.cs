using Sanduhr.Core;

namespace Sanduhr.Tests;

public class McpConsentCopyTests
{
    [Fact]
    public void NamesTheOneOtherFolderThatAlreadyHasTheServer()
    {
        var text = McpConsentCopy.WontChange(@"~\.claude", [@"~\.claude-personal"]);
        Assert.Equal(
            @"Installing here changes only ~\.claude. ~\.claude-personal already has Sanduhr's MCP server; installing here doesn't change it. "
            + "Account links (which folder belongs to which account) don't change.",
            text);
    }

    [Fact]
    public void ListsSeveralOthersInPlainEnglish()
    {
        var two = McpConsentCopy.WontChange(@"~\.claude", [@"~\.a", @"~\.b"]);
        Assert.Contains(@"~\.a and ~\.b already have Sanduhr's MCP server; installing here doesn't change them.", two);
        var three = McpConsentCopy.WontChange(@"~\.claude", [@"~\.a", @"~\.b", @"~\.c"]);
        Assert.Contains(@"~\.a, ~\.b and ~\.c already have", three);
    }

    [Fact]
    public void SaysNothingAboutOthersWhenNoneHaveItButStillPromisesTheAccountLinks()
    {
        var text = McpConsentCopy.WontChange(@"~\.claude", []);
        Assert.DoesNotContain("already has", text);
        Assert.DoesNotContain("already have", text);
        Assert.Contains("Account links (which folder belongs to which account) don't change.", text);
    }

    [Fact]
    public void IgnoresTheTargetItselfBlanksAndRepeats()
    {
        var text = McpConsentCopy.WontChange(@"~\.claude", [@"~\.CLAUDE", "", "  ", @"~\.x", @"~\.X"]);
        Assert.Contains(@"~\.x already has", text);
        Assert.DoesNotContain("already have ", text);
    }
}
