using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Tests for the pure embedded-sign-in logic — the signed-in-URL heuristic and
/// the cookie extraction RORORO unit-tests, adapted to claude.ai + sessionKey.
/// </summary>
public class ClaudeSignInTests
{
    [Theory]
    [InlineData("https://claude.ai/", true)]
    [InlineData("https://claude.ai", true)]
    [InlineData("https://claude.ai/new", true)]
    [InlineData("https://claude.ai/chats", true)]
    [InlineData("https://claude.ai/recents", true)]
    [InlineData("https://claude.ai/project/abc123", true)]
    // Auth surfaces are NOT signed-in.
    [InlineData("https://claude.ai/login", false)]
    [InlineData("https://claude.ai/login/", false)]
    [InlineData("https://claude.ai/login?return_to=/new", false)]
    [InlineData("https://claude.ai/oauth/authorize", false)]
    [InlineData("https://claude.ai/magic-link", false)]
    [InlineData("https://claude.ai/logout", false)]
    // Foreign hosts (OAuth providers, captcha, analytics) are NOT signed-in.
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth", false)]
    [InlineData("https://challenges.cloudflare.com/turnstile", false)]
    // Wrong scheme / junk.
    [InlineData("http://claude.ai/new", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSignedInUrl_classifies_claude_pages(string? url, bool expected)
    {
        Assert.Equal(expected, ClaudeSignIn.IsSignedInUrl(url));
    }

    [Theory]
    [InlineData("https://claude.ai/login", true)]
    [InlineData("https://claude.ai/", false)]
    [InlineData("https://claude.ai/new", false)]
    [InlineData("https://accounts.google.com/login", false)] // not a claude host
    public void IsLoginUrl_flags_claude_auth_surfaces(string url, bool expected)
    {
        Assert.Equal(expected, ClaudeSignIn.IsLoginUrl(url));
    }

    [Fact]
    public void Extract_pulls_sessionKey_and_cf_clearance()
    {
        var cookies = new Dictionary<string, string?>
        {
            ["sessionKey"] = "sk-ant-sid01-EXAMPLE",
            ["cf_clearance"] = "cf-EXAMPLE",
            ["lastActiveOrg"] = "org-123",
        };

        var result = ClaudeSignIn.Extract(cookies);

        Assert.Equal("sk-ant-sid01-EXAMPLE", result.SessionKey);
        Assert.Equal("cf-EXAMPLE", result.CfClearance);
        Assert.True(result.HasSession);
    }

    [Fact]
    public void Extract_without_sessionKey_reports_not_signed_in()
    {
        var cookies = new Dictionary<string, string?>
        {
            ["cf_clearance"] = "cf-EXAMPLE",
        };

        var result = ClaudeSignIn.Extract(cookies);

        Assert.Null(result.SessionKey);
        Assert.Equal("cf-EXAMPLE", result.CfClearance);
        Assert.False(result.HasSession);
    }

    [Fact]
    public void Extract_treats_blank_sessionKey_as_absent()
    {
        var cookies = new Dictionary<string, string?>
        {
            ["sessionKey"] = "   ",
        };

        var result = ClaudeSignIn.Extract(cookies);

        Assert.Null(result.SessionKey);
        Assert.False(result.HasSession);
    }

    [Fact]
    public void Extract_with_no_relevant_cookies_is_empty()
    {
        var result = ClaudeSignIn.Extract(new Dictionary<string, string?>());

        Assert.Null(result.SessionKey);
        Assert.Null(result.CfClearance);
        Assert.False(result.HasSession);
    }
}
