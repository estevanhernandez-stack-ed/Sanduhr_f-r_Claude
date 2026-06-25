using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SignInPromptCopyTests
{
    [Fact]
    public void FirstRun_sells_the_no_devtools_flow()
    {
        var p = SignInPromptCopy.For(SignInReason.FirstRun);
        Assert.Equal("Track your Claude usage", p.Headline);
        Assert.Contains("no DevTools", p.Subtitle);
        Assert.Equal("Sign in to Claude", p.PrimaryLabel);
    }

    [Fact]
    public void Expired_points_at_re_auth_not_key_paste()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.DoesNotContain("DevTools", p.Subtitle, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocked_explains_the_cloudflare_refresh()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
    }

    [Fact]
    public void None_has_no_card_copy()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SignInPromptCopy.For(SignInReason.None));
}
