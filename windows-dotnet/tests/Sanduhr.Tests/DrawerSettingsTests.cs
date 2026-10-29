using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerSettingsTests
{
    [Fact]
    public void A_missing_key_is_off_with_the_default_rules()
    {
        var s = DrawerSettings.Decode(null);
        Assert.False(s.Enabled);
        Assert.False(s.AutoTidy);
        Assert.False(s.AutoTidyUnlocked);
        Assert.Null(s.Root);
        Assert.Empty(s.Drawers);
        Assert.Equal(DrawerRules.Defaults, s.Rules);
    }

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var s = DrawerSettings.Default with
        {
            Enabled = true,
            Root = @"D:\Sorted",
            Drawers = [new DrawerDef("screenshots", "Screenshots", @"D:\Sorted\Screenshots", true), new DrawerDef("downloads", "Downloads", @"C:\Users\pat\Downloads", false)],
            Rules = [new DrawerRule(DrawerRuleKind.NameContains, "screenshots", Text: "shot"), new DrawerRule(DrawerRuleKind.OlderThan, "screenshots", On: false, Days: 7)],
            AutoTidy = true,
            AutoTidyUnlocked = true,
        };
        var back = DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal(s.Enabled, back.Enabled);
        Assert.Equal(s.Root, back.Root);
        Assert.Equal(s.Drawers, back.Drawers);
        Assert.Equal(s.Rules, back.Rules);
        Assert.True(back.AutoTidy);
        Assert.True(back.AutoTidyUnlocked);
    }

    [Fact]
    public void Auto_tidy_stays_off_until_unlocked()
    {
        var json = JsonNode.Parse("""{"enabled": true, "auto_tidy": true, "auto_tidy_unlocked": false}""");
        Assert.False(DrawerSettings.Decode(json).AutoTidy);
    }

    [Fact]
    public void Damaged_entries_are_dropped_one_by_one()
    {
        var json = JsonNode.Parse("""
        {
          "enabled": true,
          "drawers": [
            {"id": "ok", "name": "OK", "home": "C:\\Users\\pat\\Drawers\\OK", "owned": true},
            {"id": "Bad Id", "name": "X", "home": "C:\\x", "owned": true},
            {"id": "con", "name": "CON", "home": "C:\\x", "owned": true},
            {"id": "nohome", "name": "No home"},
            "not an object"
          ],
          "rules": [
            {"kind": "images", "drawer": "ok"},
            {"kind": "teleport", "drawer": "ok"},
            {"kind": "older_than", "drawer": "ok", "days": -4},
            {"kind": "documents"}
          ]
        }
        """);
        var s = DrawerSettings.Decode(json);
        Assert.Equal(["ok"], s.Drawers.Select(d => d.Id));
        Assert.Equal([DrawerRuleKind.Images, DrawerRuleKind.OlderThan], s.Rules.Select(r => r.Kind));
        Assert.Equal(1, s.Rules[1].Days);
    }

    [Fact]
    public void An_empty_rules_array_means_the_user_deleted_them_all()
    {
        var s = DrawerSettings.Decode(JsonNode.Parse("""{"rules": []}"""));
        Assert.Empty(s.Rules);
    }

    [Fact]
    public void The_default_root_is_a_folder_on_the_Desktop() =>
        Assert.Equal(@"C:\Users\pat\OneDrive\Desktop\Drawers", DrawerSettings.DefaultRoot(@"C:\Users\pat\OneDrive\Desktop"));

    [Fact]
    public void RootOr_falls_back_to_the_Desktop_default()
    {
        Assert.Equal(@"C:\Users\pat\Desktop\Drawers", DrawerSettings.Default.RootOr(@"C:\Users\pat\Desktop"));
        Assert.Equal(@"D:\Sorted", (DrawerSettings.Default with { Root = @"D:\Sorted" }).RootOr(@"C:\Users\pat\Desktop"));
    }

    [Fact]
    public void Paths_keeps_the_journal_with_the_settings()
    {
        var p = new Paths(appDataBase: @"C:\t\appdata", homeDir: @"C:\t\home", localAppDataBase: @"C:\t\local");
        Assert.Equal(Path.Combine(p.AppDataDir, "drawers-journal.json"), p.DrawerJournalFile);
    }
}
