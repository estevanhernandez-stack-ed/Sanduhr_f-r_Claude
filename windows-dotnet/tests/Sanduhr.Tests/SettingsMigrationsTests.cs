using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Spec 6.1: hotkey_settings moves from the flat S1 object to a per-action map without losing
/// a value, reads either shape, and the move runs once.</summary>
public class SettingsMigrationsTests
{
    private static JsonObject Root(string json) => (JsonObject)JsonNode.Parse(json)!;
    private static readonly HotKeyCombo Def = HotKeys.Default(HotKeyAction.Settings);

    [Theory]
    [InlineData("{\"on\":true,\"vk\":74,\"mods\":10}")]
    [InlineData("{\"on\":false}")]
    [InlineData("{\"on\":true}")]
    [InlineData("{\"vk\":116,\"mods\":1}")]
    [InlineData("{\"on\":\"yes\",\"vk\":\"J\",\"mods\":99}")]
    public void Flat_value_moves_into_the_map_exactly(string flat)
    {
        var root = Root($"{{\"theme\":\"obsidian\",\"hotkey_settings\":{flat}}}");
        var before = HotKeys.DecodeFrom(HotKeyAction.Settings, root["hotkey_settings"]);

        Assert.True(SettingsMigrations.HotKeyMap(root));

        Assert.Equal(JsonNode.Parse(flat)!.ToJsonString(), root["hotkey_settings"]!["settings"]!.ToJsonString());
        Assert.Equal(before, HotKeys.DecodeFrom(HotKeyAction.Settings, root["hotkey_settings"]));
        Assert.Equal("obsidian", (string)root["theme"]!);
    }

    [Fact]
    public void Runs_once()
    {
        var root = Root("{\"hotkey_settings\":{\"on\":true,\"vk\":74,\"mods\":10}}");
        Assert.True(SettingsMigrations.RunAll(root));
        var once = root.ToJsonString();
        Assert.False(SettingsMigrations.RunAll(root));
        Assert.Equal(once, root.ToJsonString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"hotkey_settings\":{\"settings\":{\"on\":true}}}")]
    [InlineData("{\"hotkey_settings\":{}}")]
    [InlineData("{\"hotkey_settings\":5}")]
    [InlineData("{\"hotkey_settings\":null}")]
    public void Absent_map_or_odd_is_left_alone(string json)
    {
        var root = Root(json);
        Assert.False(SettingsMigrations.HotKeyMap(root));
        Assert.Equal(Root(json).ToJsonString(), root.ToJsonString());
    }

    [Fact]
    public void Both_shapes_read_the_same_setting()
    {
        var flat = JsonNode.Parse("{\"on\":true,\"vk\":74,\"mods\":10}");
        var map = JsonNode.Parse("{\"settings\":{\"on\":true,\"vk\":74,\"mods\":10}}");
        var want = new HotKeySetting(true, new HotKeyCombo(0x4A, HotKeyModifiers.Control | HotKeyModifiers.Win));
        Assert.Equal(want, HotKeys.DecodeFrom(HotKeyAction.Settings, flat));
        Assert.Equal(want, HotKeys.DecodeFrom(HotKeyAction.Settings, map));
        Assert.Equal(new HotKeySetting(false, Def), HotKeys.DecodeFrom(HotKeyAction.Settings, null));
        Assert.Equal(new HotKeySetting(false, Def), HotKeys.DecodeFrom(HotKeyAction.Settings, JsonNode.Parse("{\"join\":{\"on\":true}}")));
    }

    [Fact]
    public void Writing_one_entry_keeps_the_others()
    {
        var map = JsonNode.Parse("{\"settings\":{\"on\":false},\"join\":{\"on\":true,\"vk\":74,\"mods\":1}}");
        var next = HotKeys.EncodeInto(map, HotKeyAction.Settings, new HotKeySetting(true, Def));
        Assert.Equal("{\"settings\":{\"on\":true},\"join\":{\"on\":true,\"vk\":74,\"mods\":1}}", next.ToJsonString());

        // Writing over a flat value turns it into the map in the same step.
        var fromFlat = HotKeys.EncodeInto(JsonNode.Parse("{\"on\":false}"), HotKeyAction.Settings, new HotKeySetting(true, Def));
        Assert.Equal("{\"settings\":{\"on\":true}}", fromFlat.ToJsonString());
    }

    [Fact]
    public void Action_words_are_never_flat_keys()
    {
        foreach (var a in Enum.GetValues<HotKeyAction>())
            Assert.DoesNotContain(HotKeys.Word(a), new[] { "on", "vk", "mods" });
    }
}
