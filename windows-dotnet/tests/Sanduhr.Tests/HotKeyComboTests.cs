using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class HotKeyComboTests
{
    private const HotKeyModifiers C = HotKeyModifiers.Control;
    private const HotKeyModifiers A = HotKeyModifiers.Alt;
    private const HotKeyModifiers S = HotKeyModifiers.Shift;
    private const HotKeyModifiers W = HotKeyModifiers.Win;

    [Fact]
    public void Display_orders_modifiers_ctrl_alt_shift_win()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+J", new HotKeyCombo(0x4A, C | A | S | W).Display);
        Assert.Equal("Ctrl+Alt+S", new HotKeyCombo(0x53, A | C).Display);
    }

    [Theory]
    [InlineData(0x74, "F5")]
    [InlineData(0x87, "F24")]
    [InlineData(0x20, "Space")]
    [InlineData(0xBD, "-")]
    [InlineData(0x63, "Num 3")]
    [InlineData(0x35, "5")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x21, "PageUp")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xE5, "Key 0xE5")]
    [InlineData(0x07, "Key 0x07")]
    public void Key_names(int vk, string name) =>
        Assert.Equal(name, HotKeyCombo.KeyName(vk));

    [Theory]
    [InlineData("Ctrl+Alt+S", 0x53, 2 | 1)]
    [InlineData("win+shift+f5", 0x74, 8 | 4)]
    [InlineData("CONTROL ALT J", 0x4A, 2 | 1)]
    [InlineData("Windows+Space", 0x20, 8)]
    [InlineData("ctrl+num 3", 0x63, 2)]
    [InlineData("Ctrl+Alt+-", 0xBD, 2 | 1)]
    [InlineData("ctrl alt Num 0", 0x60, 2 | 1)]
    public void Parse_reads_forms(string text, int vk, int mods) =>
        Assert.Equal(new HotKeyCombo(vk, (HotKeyModifiers)mods), HotKeyCombo.Parse(text));

    [Theory]
    [InlineData(0x53, 2 | 1)]
    [InlineData(0x4A, 2 | 1 | 4 | 8)]
    [InlineData(0x74, 8)]
    [InlineData(0x63, 2)]
    [InlineData(0xDE, 1)]
    [InlineData(0x2E, 2 | 4)]
    public void Parse_round_trips_display(int vk, int mods)
    {
        var combo = new HotKeyCombo(vk, (HotKeyModifiers)mods);
        Assert.Equal(combo, HotKeyCombo.Parse(combo.Display));
        Assert.Equal(combo, HotKeyCombo.Parse(combo.Display.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+")]
    [InlineData("ctrl++")]
    [InlineData("+s")]
    [InlineData("ctrl+banana")]
    [InlineData("hyper+s")]
    [InlineData("ctrl+alt")]
    public void Parse_rejects_unknown(string text) =>
        Assert.Null(HotKeyCombo.Parse(text));

    [Fact]
    public void Required_modifier_is_ctrl_alt_or_win()
    {
        Assert.True(new HotKeyCombo(0x53, C).HasRequiredModifier);
        Assert.True(new HotKeyCombo(0x53, A).HasRequiredModifier);
        Assert.True(new HotKeyCombo(0x53, W | S).HasRequiredModifier);
        Assert.False(new HotKeyCombo(0x53, S).HasRequiredModifier);
        Assert.False(new HotKeyCombo(0x53, HotKeyModifiers.None).HasRequiredModifier);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0xA0)]
    [InlineData(0xA1)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    public void Modifier_keys_are_recognised(int vk) => Assert.True(HotKeyCombo.IsModifierKey(vk));

    [Theory]
    [InlineData(0x53)]
    [InlineData(0x0F)]
    [InlineData(0x13)]
    [InlineData(0x5D)]
    [InlineData(0xA6)]
    public void Other_keys_are_not_modifier_keys(int vk) => Assert.False(HotKeyCombo.IsModifierKey(vk));

    // Check ----------------------------------------------------------------

    private static readonly IReadOnlyDictionary<HotKeyAction, HotKeyCombo> Current =
        new Dictionary<HotKeyAction, HotKeyCombo> { [HotKeyAction.Settings] = HotKeys.Default(HotKeyAction.Settings) };

    [Fact]
    public void Default_settings_is_ctrl_alt_s()
    {
        var d = HotKeys.Default(HotKeyAction.Settings);
        Assert.Equal(0x53, d.VirtualKey);
        Assert.Equal(C | A, d.Modifiers);
        Assert.Equal("Ctrl+Alt+S", d.Display);
    }

    [Fact]
    public void Label_and_name()
    {
        Assert.Equal("Ctrl+Alt+S opens Settings",
            HotKeys.Label(HotKeyAction.Settings, HotKeys.Default(HotKeyAction.Settings)));
        Assert.Equal("Shortcut to open Settings", HotKeys.Name(HotKeyAction.Settings));
    }

    [Theory]
    [InlineData(0x53, 4)]
    [InlineData(0x53, 0)]
    public void Check_refuses_without_a_required_modifier(int vk, int mods)
    {
        var r = HotKeys.Check(new HotKeyCombo(vk, (HotKeyModifiers)mods), HotKeyAction.Settings, Current);
        Assert.Equal(new HotKeyRefusal("modifier", "Add Ctrl, Alt or Win: Shift alone isn't enough."), r);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x5B)]
    [InlineData(0xA2)]
    public void Check_refuses_a_pure_modifier_key(int vk)
    {
        var r = HotKeys.Check(new HotKeyCombo(vk, C | A), HotKeyAction.Settings, Current);
        Assert.Equal("modifier", r?.Kind);
    }

    [Fact]
    public void Check_accepts_a_good_combo_and_never_reports_taken_against_itself()
    {
        Assert.Null(HotKeys.Check(new HotKeyCombo(0x4A, C | A), HotKeyAction.Settings, Current));
        Assert.Null(HotKeys.Check(HotKeys.Default(HotKeyAction.Settings), HotKeyAction.Settings, Current));
    }

    [Fact]
    public void Check_reports_taken_when_another_action_holds_the_combo()
    {
        // Only one real action exists until Join (S6); a second enum value stands in for it.
        const HotKeyAction other = (HotKeyAction)99;
        var combo = HotKeys.Default(HotKeyAction.Settings);
        var held = new Dictionary<HotKeyAction, HotKeyCombo>
        {
            [HotKeyAction.Settings] = combo,
            [other] = new HotKeyCombo(0x4A, C | A),
        };
        var r = HotKeys.Check(new HotKeyCombo(0x4A, C | A), HotKeyAction.Settings, held);
        Assert.Equal("taken", r?.Kind);
        Assert.StartsWith("Ctrl+Alt+J already ", r!.Note);
        Assert.EndsWith(".", r.Note);

        var settingsHolds = new Dictionary<HotKeyAction, HotKeyCombo> { [HotKeyAction.Settings] = combo };
        var r2 = HotKeys.Check(combo, other, settingsHolds);
        Assert.Equal(new HotKeyRefusal("taken", "Ctrl+Alt+S already opens Settings."), r2);
    }

    [Fact]
    public void Modifier_refusal_outranks_taken()
    {
        var shiftOnly = new HotKeyCombo(0x53, S);
        var held = new Dictionary<HotKeyAction, HotKeyCombo> { [(HotKeyAction)99] = shiftOnly };
        Assert.Equal("modifier", HotKeys.Check(shiftOnly, HotKeyAction.Settings, held)?.Kind);
    }

    [Fact]
    public void Os_note_and_caption()
    {
        var r = HotKeys.OsTakenNote(new HotKeyCombo(0x4A, C | A));
        Assert.Equal("os", r.Kind);
        Assert.Equal("Another app already uses Ctrl+Alt+J. Pick another.", r.Note);

        Assert.Equal("Works in every app whenever Sanduhr runs. While it's on, Ctrl+Alt+S goes to Sanduhr instead of the app in front.",
            HotKeys.Caption(Current));
        var custom = new Dictionary<HotKeyAction, HotKeyCombo> { [HotKeyAction.Settings] = new HotKeyCombo(0x74, W) };
        Assert.Contains("Win+F5 goes to Sanduhr", HotKeys.Caption(custom));
    }

    // Decode / Encode --------------------------------------------------------

    private static HotKeySetting Dec(string? json) =>
        HotKeys.Decode(HotKeyAction.Settings, json is null ? null : JsonNode.Parse(json));

    private static readonly HotKeyCombo Def = HotKeys.Default(HotKeyAction.Settings);

    [Fact]
    public void Decode_missing_or_odd_is_off_with_default()
    {
        Assert.Equal(new HotKeySetting(false, Def), Dec(null));
        Assert.Equal(new HotKeySetting(false, Def), Dec("5"));
        Assert.Equal(new HotKeySetting(false, Def), Dec("\"x\""));
        Assert.Equal(new HotKeySetting(false, Def), Dec("{}"));
    }

    [Fact]
    public void Decode_on_without_keys_uses_default()
    {
        Assert.Equal(new HotKeySetting(true, Def), Dec("{\"on\":true}"));
        Assert.Equal(new HotKeySetting(false, Def), Dec("{\"on\":\"yes\"}"));
    }

    [Fact]
    public void Decode_reads_a_custom_combo()
    {
        Assert.Equal(new HotKeySetting(true, new HotKeyCombo(0x4A, C | W)), Dec("{\"on\":true,\"vk\":74,\"mods\":10}"));
        Assert.Equal(new HotKeySetting(false, new HotKeyCombo(0x74, A)), Dec("{\"vk\":116,\"mods\":1}"));
    }

    [Theory]
    [InlineData("{\"on\":true,\"vk\":74}")]
    [InlineData("{\"on\":true,\"mods\":2}")]
    [InlineData("{\"on\":true,\"vk\":0,\"mods\":2}")]
    [InlineData("{\"on\":true,\"vk\":255,\"mods\":2}")]
    [InlineData("{\"on\":true,\"vk\":-3,\"mods\":2}")]
    [InlineData("{\"on\":true,\"vk\":74,\"mods\":4}")]
    [InlineData("{\"on\":true,\"vk\":74,\"mods\":0}")]
    [InlineData("{\"on\":true,\"vk\":74,\"mods\":99}")]
    [InlineData("{\"on\":true,\"vk\":17,\"mods\":2}")]
    [InlineData("{\"on\":true,\"vk\":\"J\",\"mods\":2}")]
    public void Decode_bad_keys_fall_back_to_default_and_keep_on(string json) =>
        Assert.Equal(new HotKeySetting(true, Def), Dec(json));

    [Fact]
    public void Encode_default_omits_vk_and_mods()
    {
        var o = HotKeys.Encode(HotKeyAction.Settings, new HotKeySetting(true, Def));
        Assert.Equal("{\"on\":true}", o.ToJsonString());
    }

    [Fact]
    public void Encode_custom_writes_vk_and_mods_and_round_trips()
    {
        var s = new HotKeySetting(true, new HotKeyCombo(0x4A, C | W));
        var o = HotKeys.Encode(HotKeyAction.Settings, s);
        Assert.Equal(74, (int)o["vk"]!);
        Assert.Equal(10, (int)o["mods"]!);
        Assert.Equal(s, HotKeys.Decode(HotKeyAction.Settings, o));
        var off = new HotKeySetting(false, Def);
        Assert.Equal(off, HotKeys.Decode(HotKeyAction.Settings, HotKeys.Encode(HotKeyAction.Settings, off)));
    }
}
