using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>The Claude Code integrations a receipt can name (the Mac's <c>IntegrationKind</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntegrationKind>))]
public enum IntegrationKind
{
    /// <summary><c>mcpServers.sanduhr</c> in the folder's <c>.claude.json</c>.</summary>
    [JsonStringEnumMemberName("mcp")] Mcp,
    /// <summary><c>statusLine</c> in the folder's <c>settings.json</c>.</summary>
    [JsonStringEnumMemberName("statusline")] Statusline,
    /// <summary>The meters mod's folder in <c>env.CLAUDE_CODE_PLUGIN_DIRS</c>.</summary>
    [JsonStringEnumMemberName("meters")] Meters,
    /// <summary>Sanduhr's entries in <c>hooks.Notification</c> and <c>hooks.Stop</c>.</summary>
    [JsonStringEnumMemberName("hooks")] Hooks,
}

/// <summary>Whether the user's statusline was replaced or combined with Sanduhr's.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatuslineMode>))]
public enum StatuslineMode
{
    [JsonStringEnumMemberName("replace")] Replace,
    [JsonStringEnumMemberName("combine")] Combine,
}

/// <summary>
/// What Sanduhr did to one file, so Remove undoes exactly that. JSON names are the Mac's (its Swift
/// property names), and properties are declared in the alphabetical order the Mac's sorted-keys
/// encoder writes them. Optional members are left out when null.
/// </summary>
public sealed record IntegrationReceipt
{
    /// <summary>The file didn't exist: Remove deletes it again when nothing else was added.</summary>
    [JsonPropertyName("createdFile")] public bool CreatedFile { get; init; }

    /// <summary>The mod: <c>CLAUDE_CODE_PLUGIN_DIRS</c> didn't exist, so Remove deletes it once only Sanduhr's entry is left.</summary>
    [JsonPropertyName("createdKey")] public bool? CreatedKey { get; init; }

    /// <summary><c>mcpServers</c> didn't exist: Remove takes it out again once it is empty.</summary>
    [JsonPropertyName("createdParent")] public bool CreatedParent { get; init; }

    /// <summary>What sat between the braces of the object Sanduhr's member went into when it was empty.</summary>
    [JsonPropertyName("emptyInner")] public string? EmptyInner { get; init; }

    /// <summary>The config file written.</summary>
    [JsonPropertyName("file")] public required string File { get; init; }

    /// <summary>The Claude Code folder the user picked.</summary>
    [JsonPropertyName("folder")] public required string Folder { get; init; }

    /// <summary>The hooks: what Install made, so Remove takes out exactly that.</summary>
    [JsonPropertyName("hooks")] public HookReceipt? Hooks { get; init; }

    [JsonPropertyName("kind")] public required IntegrationKind Kind { get; init; }

    /// <summary>The statusline: replaced or combined.</summary>
    [JsonPropertyName("mode")] public StatuslineMode? Mode { get; init; }

    /// <summary>The meters mod: the stamp its plugin folders entry names, null when it follows the app's updates.</summary>
    [JsonPropertyName("pinned")] public string? Pinned { get; init; }

    /// <summary>The value Sanduhr replaced, byte for byte, as text (a statusline, a <c>sanduhr</c> entry, the plugin folders list).</summary>
    [JsonPropertyName("previous")] public string? Previous { get; init; }

    /// <summary>The meters mod's <c>enabledPlugins</c> switch.</summary>
    [JsonPropertyName("switched")] public SwitchReceipt? Switched { get; init; }
}

/// <summary>One <c>enabledPlugins</c> member a Mods page switch wrote, byte for byte.</summary>
public sealed record SwitchReceipt
{
    /// <summary>A missing <c>createdParent</c> reads as false.</summary>
    [JsonPropertyName("createdParent")] public bool CreatedParent { get; init; }

    /// <summary>What sat between the braces of the object the member went into when it was empty.</summary>
    [JsonPropertyName("emptyInner")] public string? EmptyInner { get; init; }

    /// <summary>The key, <c>&lt;name&gt;@inline</c>.</summary>
    [JsonPropertyName("key")] public required string Key { get; init; }

    /// <summary>The member's value before, as text; null when there was none.</summary>
    [JsonPropertyName("previous")] public string? Previous { get; init; }

    /// <summary>The value written: false switches the mod off, true overrides an off of the user's own.</summary>
    [JsonPropertyName("value")] public required bool Value { get; init; }
}

/// <summary>What the hooks' install made in <c>hooks</c>.</summary>
public sealed record HookReceipt
{
    /// <summary>Per event: what sat between the brackets of a list that was empty before Sanduhr's entry. Written in key order.</summary>
    [JsonPropertyName("arrayInners")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public SortedDictionary<string, string> ArrayInners { get; } = new(StringComparer.Ordinal);

    /// <summary>The event lists Sanduhr created (<c>Notification</c>, <c>Stop</c>).</summary>
    [JsonPropertyName("createdEvents")] public List<string> CreatedEvents { get; init; } = [];

    /// <summary>What sat between the braces of <c>hooks</c> when it was empty before Sanduhr's first list.</summary>
    [JsonPropertyName("hooksInner")] public string? HooksInner { get; init; }

    public bool Equals(HookReceipt? other)
        => other is not null
           && HooksInner == other.HooksInner
           && CreatedEvents.SequenceEqual(other.CreatedEvents)
           && ArrayInners.Count == other.ArrayInners.Count
           && ArrayInners.All(kv => other.ArrayInners.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(HooksInner, CreatedEvents.Count, ArrayInners.Count);
}

/// <summary>The one set of serializer options receipts are read and written with.</summary>
public static class ReceiptJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>One record as the bytes a save writes for it.</summary>
    public static byte[] Serialize<T>(T record) => JsonSerializer.SerializeToUtf8Bytes(record, Options);
}
