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
    }

    [Fact]
    public void ARowSwitchedOnWithoutFilesIsListedButNotInstalledOrOn()
    {
        var items = new[] { Item("a"), Item("ghost", ModState.On, path: null), Item("gone", ModState.Missing) };
        var t = ModsPageText.Tally(items, Switches(("a@market", true), ("ghost@market", true), ("gone@market", true)));
        Assert.Equal(new PluginTally(3, 1, 1, 2, 0), t);
        Assert.Equal("Plugins (1 installed, 1 on)", ModsPageText.ListTitle(t));
    }

    private static ModItem NoteItem(string name, ModState state, string? path, string? key) =>
        new(name, null, "", path, ModOriginKind.Installed, "m", key, false, state, ModTouches.None, false);

    private static FolderMods NoteFolder(string name, IReadOnlyDictionary<string, bool> switches, params ModItem[] mods) =>
        new(name, name, new FolderReport(name, true, SettingsState.Readable, null, [], switches, mods,
            new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(30, false, null)), null, IntegrationStatus.NotInstalled);

    [Fact]
    public void The_note_counts_plugins_without_a_switch_and_without_files_over_every_folder_in_plain_words()
    {
        var a = NoteFolder("a", new Dictionary<string, bool> { ["x@m"] = true, ["ghost@m"] = true },
            NoteItem("x", ModState.On, @"C:\x", "x@m"), NoteItem("inline", ModState.On, @"C:\i", null), NoteItem("ghost", ModState.On, null, "ghost@m"));
        var b = NoteFolder("b", new Dictionary<string, bool>(), NoteItem("skills", ModState.On, @"C:\s", "skills@skills-dir"));

        Assert.Equal(SettingsNames.NoSwitchNote(2) + " " + SettingsNames.NoFilesNote(1), ModsPageText.PlainNote([a, b]));
        Assert.Equal("", ModsPageText.PlainNote([NoteFolder("c", new Dictionary<string, bool> { ["x@m"] = true }, NoteItem("x", ModState.On, @"C:\x", "x@m"))]));
    }

    [Fact]
    public void A_plugin_in_two_folders_counts_once_in_the_note()
    {
        var noSwitch = new Dictionary<string, bool>();
        var ghostOn = new Dictionary<string, bool> { ["ghost@m"] = true };
        var one = NoteFolder("a", ghostOn, NoteItem("alpha", ModState.On, @"C:\a", "alpha@skills-dir"), NoteItem("ghost", ModState.On, null, "ghost@m"));
        var two = NoteFolder("b", ghostOn, NoteItem("alpha", ModState.On, @"C:\a", "alpha@skills-dir"), NoteItem("ghost", ModState.On, null, "ghost@m"));
        var three = NoteFolder("c", noSwitch, NoteItem("alpha", ModState.On, @"C:\a", "alpha@skills-dir"));

        Assert.Equal(SettingsNames.NoSwitchNote(1) + " " + SettingsNames.NoFilesNote(1), ModsPageText.PlainNote([one, two, three]));
    }

    [Fact]
    public void An_unreadable_folder_adds_nothing_to_the_no_switch_note()
    {
        var f = NoteFolder("a", new Dictionary<string, bool>(), NoteItem("x", ModState.On, @"C:\x", "x@m"));
        f = f with { Report = f.Report with { Settings = new SettingsState(SettingsKind.Unreadable, 3, 7, "bad") } };
        Assert.Equal("", ModsPageText.PlainNote([f]));
    }

    [Fact]
    public void A_plugin_counts_in_one_sentence_only_and_no_files_wins()
    {
        // Slice 4 row 20 (owner taste 4): files and no entry in folder A, switched on with no files in folder B.
        var a = NoteFolder("a", new Dictionary<string, bool>(), NoteItem("x", ModState.On, @"C:\x", "x@m"));
        var b = NoteFolder("b", new Dictionary<string, bool> { ["x@m"] = true }, NoteItem("x", ModState.On, null, "x@m"));
        var note = ModsPageText.PlainNote([a, b]);
        Assert.Contains(SettingsNames.NoFilesNote(1), note);
        Assert.DoesNotContain(SettingsNames.NoSwitchNote(1), note);
    }

    [Fact]
    public void The_files_missing_count_is_of_plugins_switched_on_not_off_or_only_listed()
    {
        // "switched on in settings.json, but its files aren't there" must be true of every plugin it counts.
        var f = NoteFolder("a", new Dictionary<string, bool> { ["off@m"] = false, ["on@m"] = true },
            NoteItem("off", ModState.Off, null, "off@m"), NoteItem("listed", ModState.On, null, "listed@m"), NoteItem("on", ModState.On, null, "on@m"));

        Assert.Equal(SettingsNames.NoFilesNote(1), ModsPageText.PlainNote([f]));
        var onlyOff = NoteFolder("b", new Dictionary<string, bool> { ["off@m"] = false }, NoteItem("off", ModState.Off, null, "off@m"));
        Assert.Equal("", ModsPageText.PlainNote([onlyOff]));
    }

    [Fact]
    public void AnEmptyFolderSaysNone()
    {
        Assert.Equal("Plugins (none)", ModsPageText.ListTitle(ModsPageText.Tally([], Switches())));
        Assert.Equal("None", ModsPageText.SwitchSummary(0, 0));
        Assert.Equal("41 on, 2 off", ModsPageText.SwitchSummary(41, 2));
    }

    [Fact]
    public void ARowReadsAsNameVersionStateAndModTag()
    {
        Assert.Equal("626labs, version 0.1.0, on, mod", ModsPageText.RowName("626labs", "0.1.0", "On", true));
        Assert.Equal("alpha, version 1.2.0, off", ModsPageText.RowName("alpha", "v1.2.0", "Off", false));
        Assert.Equal("ghost, on, no files here", ModsPageText.RowName("ghost", null, "On, no files here", false, hasFiles: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("Unknown")]
    [InlineData("(unknown)")]
    public void AnUnknownVersionShowsNothingButTheAccessibleNameSaysSo(string? version)
    {
        Assert.Equal("", ModsPageText.VersionText(version));
        Assert.Null(ModInventory.CleanVersion(version));
        Assert.Equal("tool, version unknown, on", ModsPageText.RowName("tool", version, "On", false));
    }

    [Theory]
    [InlineData("0.1.0", "v0.1.0")]
    [InlineData("v2.0.1", "v2.0.1")]
    [InlineData(" 1.0 ", "v1.0")]
    [InlineData("a1b2c3d", "va1b2c3d")]
    public void AKnownVersionKeepsOneV(string version, string shown) => Assert.Equal(shown, ModsPageText.VersionText(version));
}
