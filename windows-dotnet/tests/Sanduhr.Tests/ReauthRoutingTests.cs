using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Truth table for the WS-A origin-aware recovery-card routing. The
/// primary action matches the account's origin (embedded accounts re-auth in
/// the browser, manual accounts re-paste); the secondary is always the OTHER
/// method, in place. FirstRun keeps add semantics on both.</summary>
public class ReauthRoutingTests
{
    [Theory]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Embedded, AuthFlow.EmbeddedAdd)]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Manual, AuthFlow.EmbeddedAdd)]
    [InlineData(SignInReason.Expired, AccountOrigin.Embedded, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Expired, AccountOrigin.Manual, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Embedded, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Manual, AuthFlow.ManualReauth)]
    public void Primary_flow(SignInReason reason, AccountOrigin origin, AuthFlow expected)
        => Assert.Equal(expected, ReauthRouting.Primary(reason, origin));

    [Theory]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Embedded, AuthFlow.ManualAdd)]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Manual, AuthFlow.ManualAdd)]
    [InlineData(SignInReason.Expired, AccountOrigin.Embedded, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Expired, AccountOrigin.Manual, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Embedded, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Manual, AuthFlow.EmbeddedReauth)]
    public void Secondary_flow(SignInReason reason, AccountOrigin origin, AuthFlow expected)
        => Assert.Equal(expected, ReauthRouting.Secondary(reason, origin));
}
