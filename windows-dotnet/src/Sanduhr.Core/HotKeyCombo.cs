using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Win32 modifier flags (<c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c>, <c>MOD_WIN</c>).</summary>
[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global shortcut's keys: a Win32 virtual-key code and the modifier flags
/// <c>RegisterHotKey</c> takes. Semantics follow mac/.../HotKeyCombo.swift.
/// </summary>
public readonly record struct HotKeyCombo(int VirtualKey, HotKeyModifiers Modifiers)
{
    private const HotKeyModifiers Required = HotKeyModifiers.Alt | HotKeyModifiers.Control | HotKeyModifiers.Win;

    /// <summary>At least one of Ctrl, Alt or Win: Shift alone would take a capital letter from every app.</summary>
    public bool HasRequiredModifier => (Modifiers & Required) != 0;

    /// <summary>"Ctrl+Alt+S": Ctrl, Alt, Shift, Win, then the key name.</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");
            parts.Add(KeyName(VirtualKey));
            return string.Join('+', parts);
        }
    }

    /// <summary>Shift, Ctrl, Alt, Win (both sides): never a combo's key.</summary>
    public static bool IsModifierKey(int vk) =>
        vk is (>= 0x10 and <= 0x12) or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>The name shown for a virtual key; "Key 0x{vk:X2}" when it has none.</summary>
    public static string KeyName(int vk) =>
        Names.TryGetValue(vk, out var n) ? n : $"Key 0x{vk:X2}";

    private static readonly Dictionary<int, string> Names = BuildNames();
    private static readonly Dictionary<string, int> Codes = Names.ToDictionary(
        kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> BuildNames()
    {
        var d = new Dictionary<int, string>();
        for (var c = 'A'; c <= 'Z'; c++) d[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) d[c] = c.ToString();
        for (var i = 1; i <= 24; i++) d[0x6F + i] = $"F{i}";
        for (var i = 0; i <= 9; i++) d[0x60 + i] = $"Num {i}";
        d[0x20] = "Space"; d[0x0D] = "Enter"; d[0x09] = "Tab"; d[0x08] = "Backspace";
        d[0x2E] = "Delete"; d[0x2D] = "Insert"; d[0x24] = "Home"; d[0x23] = "End";
        d[0x21] = "PageUp"; d[0x22] = "PageDown";
        d[0x25] = "Left"; d[0x26] = "Up"; d[0x27] = "Right"; d[0x28] = "Down";
        d[0x1B] = "Esc";
        d[0xBA] = ";"; d[0xBB] = "="; d[0xBC] = ","; d[0xBD] = "-"; d[0xBE] = ".";
        d[0xBF] = "/"; d[0xC0] = "`"; d[0xDB] = "["; d[0xDC] = "\\"; d[0xDD] = "]"; d[0xDE] = "'";
        return d;
    }

    /// <summary>
    /// "Ctrl+Alt+S", "win shift f5", "ctrl+num 3": modifier words (ctrl/control, alt, shift,
    /// win/windows) then a key name, separated by '+' or spaces, case ignored. Null for an
    /// unknown word or an empty slot ("ctrl++").
    /// </summary>
    public static HotKeyCombo? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = new List<string>();
        foreach (var segment in text.Split('+'))
        {
            var pieces = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0) return null;
            words.AddRange(pieces);
        }

        // "Num 3" is two words: take the last two together when they name a key.
        var keyWordCount = 1;
        if (words.Count >= 2 && Codes.ContainsKey($"{words[^2]} {words[^1]}")) keyWordCount = 2;
        var keyText = string.Join(' ', words.Skip(words.Count - keyWordCount));
        if (!Codes.TryGetValue(keyText, out var vk)) return null;

        var mods = HotKeyModifiers.None;
        foreach (var w in words.Take(words.Count - keyWordCount))
        {
            switch (w.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotKeyModifiers.Control; break;
                case "alt": mods |= HotKeyModifiers.Alt; break;
                case "shift": mods |= HotKeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotKeyModifiers.Win; break;
                default: return null;
            }
        }
        return new HotKeyCombo(vk, mods);
    }
}

/// <summary>The shortcuts Sanduhr owns. Join arrives with meetings (S6).</summary>
public enum HotKeyAction
{
    Settings,
}

/// <summary>Why a recorded combo was refused. Kind: "modifier" | "taken" | "os".</summary>
public sealed record HotKeyRefusal(string Kind, string Note);

/// <summary>One action's stored state: on/off and its keys.</summary>
public sealed record HotKeySetting(bool On, HotKeyCombo Combo);

public static class HotKeys
{
    public static HotKeyCombo Default(HotKeyAction action) => action switch
    {
        HotKeyAction.Settings => new HotKeyCombo(0x53, HotKeyModifiers.Control | HotKeyModifiers.Alt),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>"{combo} opens Settings".</summary>
    public static string Label(HotKeyAction action, HotKeyCombo combo) => $"{combo.Display} {LabelTail(action)}";

    /// <summary>"Shortcut to open Settings": the control's accessible name.</summary>
    public static string Name(HotKeyAction action) => action switch
    {
        HotKeyAction.Settings => "Shortcut to open Settings",
        _ => "Shortcut",
    };

    // An action added later without a tail still reads as a sentence.
    private static string LabelTail(HotKeyAction action) => action switch
    {
        HotKeyAction.Settings => "opens Settings",
        _ => "is in use",
    };

    public static HotKeyRefusal? Check(HotKeyCombo combo, HotKeyAction action,
        IReadOnlyDictionary<HotKeyAction, HotKeyCombo> current)
    {
        if (!combo.HasRequiredModifier || HotKeyCombo.IsModifierKey(combo.VirtualKey))
            return new HotKeyRefusal("modifier", "Add Ctrl, Alt or Win: Shift alone isn't enough.");

        foreach (var (other, held) in current)
        {
            if (other != action && held == combo)
                return new HotKeyRefusal("taken", $"{combo.Display} already {LabelTail(other)}.");
        }
        return null;
    }

    /// <summary>The refusal for a failed <c>RegisterHotKey</c> (ERROR_HOTKEY_ALREADY_REGISTERED).</summary>
    public static HotKeyRefusal OsTakenNote(HotKeyCombo combo) =>
        new("os", $"Another app already uses {combo.Display}. Pick another.");

    public static string Caption(IReadOnlyDictionary<HotKeyAction, HotKeyCombo> current)
    {
        var combo = current.TryGetValue(HotKeyAction.Settings, out var c) ? c : Default(HotKeyAction.Settings);
        return $"Works in every app whenever Sanduhr runs. While it's on, {combo.Display} goes to Sanduhr instead of the app in front.";
    }

    /// <summary>
    /// Reads <c>hotkey_settings</c> ({"on","vk","mods"}). Missing or odd reads as off with the
    /// default combo; a half-written entry, a vk outside 1..0xFE, mods without a required
    /// modifier or a pure modifier key reads as the default combo.
    /// </summary>
    public static HotKeySetting Decode(HotKeyAction action, JsonNode? node)
    {
        var def = Default(action);
        if (node is not JsonObject obj) return new HotKeySetting(false, def);

        var on = obj.TryGetPropertyValue("on", out var onNode)
                 && onNode is JsonValue ov && ov.TryGetValue<bool>(out var b) && b;

        var hasVk = obj.TryGetPropertyValue("vk", out var vkNode) && vkNode is not null;
        var hasMods = obj.TryGetPropertyValue("mods", out var modsNode) && modsNode is not null;
        if (!hasVk || !hasMods) return new HotKeySetting(on, def);

        if (vkNode is not JsonValue vv || !vv.TryGetValue<int>(out var vk) ||
            modsNode is not JsonValue mv || !mv.TryGetValue<int>(out var mods))
            return new HotKeySetting(on, def);

        if (vk is < 1 or > 0xFE || mods is < 0 or > 15 || HotKeyCombo.IsModifierKey(vk))
            return new HotKeySetting(on, def);

        var combo = new HotKeyCombo(vk, (HotKeyModifiers)mods);
        return new HotKeySetting(on, combo.HasRequiredModifier ? combo : def);
    }

    /// <summary>The default combo is stored as nothing: <c>vk</c> and <c>mods</c> are omitted.</summary>
    public static JsonObject Encode(HotKeyAction action, HotKeySetting setting)
    {
        var o = new JsonObject { ["on"] = setting.On };
        if (setting.Combo != Default(action))
        {
            o["vk"] = setting.Combo.VirtualKey;
            o["mods"] = (int)setting.Combo.Modifiers;
        }
        return o;
    }
}
