using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class WritePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Running_and_untouched_may_write() => Assert.True(WritePolicy.MayWrite(false, false, false));

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Stopped_reset_or_cancelled_writes_nothing(bool stopped, bool reset, bool cancelled)
        => Assert.False(WritePolicy.MayWrite(stopped, reset, cancelled));

    [Theory]
    [InlineData("webview2-0123abcd", true)]
    [InlineData("webview2-0123ABCD", false)]
    [InlineData("webview2-fetch", false)]
    [InlineData("webview2", false)]
    [InlineData("webview2-0123abc", false)]
    [InlineData("webview2-0123abcde", false)]
    public void Account_profile_names_are_eight_lowercase_hex(string name, bool expected)
        => Assert.Equal(expected, ResetPlan.IsAccountProfileName(name));

    [Fact]
    public void AccountProfileDirs_lists_only_account_folders()
    {
        var paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));
        Assert.Empty(ResetPlan.AccountProfileDirs(paths));
        foreach (var n in new[] { "webview2-0123abcd", "webview2-fetch", "webview2", "webview2-ffffffff", "other" })
            Directory.CreateDirectory(Path.Combine(paths.AppDataDir, n));
        var found = ResetPlan.AccountProfileDirs(paths).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["webview2-0123abcd", "webview2-ffffffff"], found);
    }
}
