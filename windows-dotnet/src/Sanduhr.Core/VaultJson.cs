using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// Source-generated serializer metadata for the vault files. The widget and
/// <c>sanduhr-mcp</c> both read these through <see cref="VaultStore"/>; the
/// server ships as a trimmed single-file exe, where reflection-based
/// System.Text.Json is unavailable, so the vault models get their converters
/// at compile time. Options match the reflection defaults the store used
/// before (property names from the attributes, compact output).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(VaultSessionShard))]
[JsonSerializable(typeof(VaultRollupShard))]
[JsonSerializable(typeof(VaultCheckpointFile))]
[JsonSerializable(typeof(VaultRootMeta))]
internal sealed partial class VaultJsonContext : JsonSerializerContext
{
}
