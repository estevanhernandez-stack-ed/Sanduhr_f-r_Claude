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
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
    }

    [Fact]
    public void Expired_embedded_points_at_browser_reauth_with_paste_escape()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired, AccountOrigin.Embedded);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
        Assert.DoesNotContain("DevTools", p.Subtitle, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_manual_leads_with_key_paste()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired, AccountOrigin.Manual);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Paste a new key", p.PrimaryLabel);
        Assert.Equal("Use browser sign-in instead", p.SecondaryLabel);
        Assert.Contains("sessionKey", p.Subtitle);
    }

    [Fact]
    public void Blocked_embedded_explains_the_cloudflare_refresh()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked, AccountOrigin.Embedded);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
    }

    [Fact]
    public void Blocked_manual_leads_with_key_paste_and_mentions_cf()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked, AccountOrigin.Manual);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Paste a new key", p.PrimaryLabel);
        Assert.Equal("Use browser sign-in instead", p.SecondaryLabel);
        Assert.Contains("cf_clearance", p.Subtitle);
    }

    [Fact]
    public void None_has_no_card_copy()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SignInPromptCopy.For(SignInReason.None));
}
