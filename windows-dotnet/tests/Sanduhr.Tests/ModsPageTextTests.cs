using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ModsPageTextTests
{
    private static ModItem Item(string name, ModState state = ModState.On, string? key = null, string? path = "C:\\p", bool isMod = false) =>
        new(name, "1.0.0", "", path, ModOriginKind.Installed, "market", key ?? $"{name}@market", isMod, state, ModTouches.None, false);

    private static Dictionary<string, bool> Switches(params (string Key, bool On)[] s) => s.ToDictionary(x => x.Key, x => x.On);

    [Fact]
    public void TheTitleCountsWhatIsInstalledAndWhatLoads()
    {
        var items = new[] { Item("a"), Item("b", ModState.Off), Item("c") };
        var t = ModsPageText.Tally(items, Switches(("a@market", true), ("b@market", false), ("c@market", true)));
        Assert.Equal(new PluginTally(3, 3, 2, 0, 0), t);
        Assert.Equal("Plugins (3 installed, 2 on)", ModsPageText.ListTitle(t));
        Assert.Equal("", ModsPageText.Reconcile(t, 2, 1));
    }

    [Fact]
    public void ExplainsWhyTheListCountsMoreThanTheSwitches()
    {
        // 43 switches (41 on, 2 off), 46 rows: three installed plugins have no switch.
        var items = new List<ModItem>();
        var sw = new Dictionary<string, bool>();
        for (int i = 0; i < 43; i++) { var it = Item("p" + i, i < 41 ? ModState.On : ModState.Off); items.Add(it); sw[it.Key!] = i < 41; }
        items.Add(Item("inline1", key: "inline1@inline"));
        items.Add(Item("inline2", key: "inline2@inline"));
        items.Add(Item("skill", key: "skill@skills-dir", state: ModState.Off));
        var t = ModsPageText.Tally(items, sw);
        Assert.Equal(46, t.Listed);
        Assert.Equal(46, t.Installed);
        Assert.Equal(43, t.On);
        Assert.Equal(3, t.NoSwitch);
        Assert.Equal("Plugins (46 installed, 43 on)", ModsPageText.ListTitle(t));
        var why = ModsPageText.Reconcile(t, 41, 2);
        Assert.StartsWith("settings.json has 43 plugin switches (41 on, 2 off). The list differs because 3 have no switch in settings.json", why);
    }

    [Fact]
    public void ARowSwitchedOnWithoutFilesIsListedButNotInstalledOrOn()
    {
        var items = new[] { Item("a"), Item("ghost", ModState.On, path: null), Item("gone", ModState.Missing) };
        var t = ModsPageText.Tally(items, Switches(("a@market", true), ("ghost@market", true), ("gone@market", true)));
        Assert.Equal(new PluginTally(3, 1, 1, 2, 0), t);
        Assert.Equal("Plugins (1 installed, 1 on)", ModsPageText.ListTitle(t));
        Assert.Contains("2 are switched in settings.json or listed as installed, but the files are not in this folder", ModsPageText.Reconcile(t, 3, 0));
    }

    [Fact]
    public void AnEmptyFolderSaysNone()
    {
        Assert.Equal("Plugins (none)", ModsPageText.ListTitle(ModsPageText.Tally([], Switches())));
        Assert.Equal("None", ModsPageText.SwitchSummary(0, 0));
        Assert.Equal("41 on, 2 off", ModsPageText.SwitchSummary(41, 2));
    }
}
