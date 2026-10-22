using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public sealed class ModsFolderChoiceTests : IDisposable
{
    private static readonly string[] Folders = [@"C:\u\.claude", @"C:\u\.claude-personal", @"C:\u\.claude-work"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-modschoice-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    private static int None(string _) => 0;

    [Fact]
    public void NothingListedPicksNothing() =>
        Assert.Null(ModsFolderChoice.Pick([], @"C:\u\.claude", @"C:\u\.claude", @"C:\u\.claude", None));

    [Fact]
    public void TheFolderYouPickedEarlierBeatsEverythingElse() =>
        Assert.Equal(@"C:\u\.claude-work",
            ModsFolderChoice.Pick(Folders, @"C:\u\.claude-work", @"C:\u\.claude-personal", @"C:\u\.claude", None));

    [Fact]
    public void ClaudeConfigDirBeatsTheLinkedAccountAndThePluginCount() =>
        Assert.Equal(@"C:\u\.claude-personal",
            ModsFolderChoice.Pick(Folders, null, @"C:\u\.claude-personal", @"C:\u\.claude-work", p => p.EndsWith("work") ? 40 : 0));

    [Fact]
    public void ConfigDirMatchesWhateverTheCaseOrTrailingSlashAndReturnsTheListedPath() =>
        Assert.Equal(@"C:\u\.claude-personal",
            ModsFolderChoice.Pick(Folders, null, @"c:\U\.CLAUDE-PERSONAL\", null, None));

    [Fact]
    public void AConfigDirThatIsNotListedFallsThroughToTheLinkedAccount() =>
        Assert.Equal(@"C:\u\.claude-work",
            ModsFolderChoice.Pick(Folders, null, @"C:\elsewhere\claude", @"C:\u\.claude-work", None));

    [Fact]
    public void NoEnvNoLinkPicksTheFolderWithTheMostEnabledPlugins()
    {
        var counts = new Dictionary<string, int> { [Folders[0]] = 0, [Folders[1]] = 41, [Folders[2]] = 3 };
        Assert.Equal(Folders[1], ModsFolderChoice.Pick(Folders, null, null, null, p => counts[p]));
    }

    [Fact]
    public void TiesAndAllZeroKeepTheFirst()
    {
        Assert.Equal(Folders[0], ModsFolderChoice.Pick(Folders, null, null, null, None));
        Assert.Equal(Folders[1], ModsFolderChoice.Pick(Folders, null, "", " ", p => p == Folders[1] || p == Folders[2] ? 5 : 0));
    }

    [Fact]
    public void AFolderThatCannotBeReadCountsAsZero() =>
        Assert.Equal(Folders[2], ModsFolderChoice.Pick(Folders, null, null, null,
            p => p == Folders[2] ? 2 : throw new IOException("locked")));

    [Fact]
    public void EnabledPluginCountReadsOnlyTheSwitchesThatAreOn()
    {
        var folder = Path.Combine(_root, ".claude-x");
        Directory.CreateDirectory(folder);
        Assert.Equal(0, ModInventory.EnabledPluginCount(folder));
        File.WriteAllText(Path.Combine(folder, "settings.json"),
            """{"enabledPlugins":{"a@m":true,"b@m":false,"c@m":true}}""", new UTF8Encoding(false));
        Assert.Equal(2, ModInventory.EnabledPluginCount(folder));
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ // comment\n}", new UTF8Encoding(false));
        Assert.Equal(0, ModInventory.EnabledPluginCount(folder));
        Assert.Equal(0, ModInventory.EnabledPluginCount(Path.Combine(_root, "nope")));
    }
}
