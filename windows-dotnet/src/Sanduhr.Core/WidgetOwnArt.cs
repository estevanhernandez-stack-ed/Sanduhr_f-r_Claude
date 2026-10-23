using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What <see cref="WidgetOwnArt.Apply"/> made: the pack's new files and manifest, or the reasons in sentences for Settings.</summary>
public sealed record OwnArtResult(IReadOnlyDictionary<string, byte[]>? Files, PackManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool Ok => Files is not null && Manifest is not null;

    public static OwnArtResult Failed(params string[] errors) => new(null, null, errors);
}

/// <summary>
/// "Use my own art…" (spec 3.8 section 6): the user's own PNG sprite sheets put into a sprite pack made here, one per stage.
/// Each image is checked with the pack image rules first (<see cref="CheckImage"/>: a PNG, at most 2 MB and 4096 pixels on a
/// side, a whole number of the stage's cells and a cell for every frame the animations use), then copied into the pack as
/// <c>art/&lt;stage&gt;.png</c>, the sheet it replaces is dropped, <c>pack.json</c>'s files and patch version follow, and the whole
/// pack is validated again (<see cref="PackValidator"/>, strict: the chunk stream and CRCs included). The caller installs it as
/// <see cref="WidgetSource.Local"/>: a user's image makes the pack local-only, and nothing here sends it anywhere.
///
/// Pure: bytes in, bytes and sentences out. Claude never supplies these images; only the user's file picker does.
/// </summary>
public static class WidgetOwnArt
{
    public const string ArtFolder = "art";

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The stages of the pack's sprite (one, <see cref="CompanionSprite.SingleStageId"/>, for a single-sheet sprite), or null
    /// when it is not a sprite pack Sanduhr can read.</summary>
    public static CompanionSprite? Sprite(IReadOnlyDictionary<string, byte[]> files) =>
        files.TryGetValue("sprite.json", out var bytes) ? CompanionSprite.Parse(bytes) : null;

    /// <summary>The path a stage's own art is copied to.</summary>
    public static string ArtPath(string stageId) => $"{ArtFolder}/{stageId}.png";

    /// <summary>
    /// The pack image rules on one picked file, before it is copied: null when it may be used, else one sentence. The chunk
    /// stream and the CRCs are checked again with the whole pack.
    /// </summary>
    public static string? CheckImage(byte[] png, CompanionStage stage, int maxFrame)
    {
        if (png.Length > PackValidator.MaxImageBytes)
            return "That image is larger than 2 MB. A sprite sheet is at most 2 MB.";
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(PngSignature)
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return "That file isn't a PNG image. A sprite sheet is a PNG.";
        long w = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        long h = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (w == 0 || h == 0 || w > PackValidator.MaxImageSide || h > PackValidator.MaxImageSide)
            return $"That image is {w}x{h}. A sprite sheet is at most {PackValidator.MaxImageSide} pixels on a side.";
        if (w % stage.FrameWidth != 0 || h % stage.FrameHeight != 0)
            return $"That image is {w}x{h}, which isn't a whole number of {stage.FrameWidth}x{stage.FrameHeight} cells (the size of {stage.Name}'s frames).";
        long cells = w / stage.FrameWidth * (h / stage.FrameHeight);
        if (maxFrame >= cells)
            return $"That sheet has {cells} cell{(cells == 1 ? "" : "s")}, but the animations use frame {maxFrame}, so it needs at least {maxFrame + 1}.";
        return null;
    }

    /// <summary>
    /// The pack with <paramref name="art"/> (stage id to the user's PNG) in place of those stages' sheets, validated as a whole.
    /// <paramref name="packId"/> is the installed folder's name. Stages not in <paramref name="art"/> keep their sheets.
    /// </summary>
    public static OwnArtResult Apply(IReadOnlyDictionary<string, byte[]> files, string packId, IReadOnlyDictionary<string, byte[]> art)
    {
        if (art.Count == 0) return OwnArtResult.Failed("No image was chosen, so the widget is unchanged.");
        if (Sprite(files) is not { } sprite) return OwnArtResult.Failed("This widget isn't a sprite Sanduhr can read.");
        foreach (var (stageId, png) in art)
        {
            var stage = sprite.Stages.FirstOrDefault(s => s.Id == stageId);
            if (stage is null) return OwnArtResult.Failed($"The sprite has no stage {stageId}.");
            if (CheckImage(png, stage, sprite.MaxFrame) is { } why) return OwnArtResult.Failed(stage.Name + ": " + why);
        }

        JsonObject spriteDoc, manifest;
        try
        {
            spriteDoc = (JsonObject)JsonNode.Parse(files["sprite.json"])!;
            manifest = (JsonObject)JsonNode.Parse(files["pack.json"])!;
        }
        catch (Exception e) when (e is JsonException or InvalidCastException or KeyNotFoundException or NullReferenceException)
        {
            return OwnArtResult.Failed("This widget's files can't be read.");
        }

        var next = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        if (spriteDoc["stages"] is JsonArray stages)
        {
            foreach (var node in stages)
                if (node is JsonObject st && st["id"] is JsonValue iv && iv.TryGetValue<string>(out var sid) && art.TryGetValue(sid, out var png))
                {
                    st["sheet"] = ArtPath(sid);
                    next[ArtPath(sid)] = png;
                }
        }
        else if (art.TryGetValue(CompanionSprite.SingleStageId, out var single))
        {
            spriteDoc["sheet"] = ArtPath("sprite");
            next[ArtPath("sprite")] = single;
        }

        // The sheets sprite.json names now; a sheet no stage uses any more leaves the pack.
        var referenced = new HashSet<string>(StringComparer.Ordinal) { "sprite.json" };
        if (spriteDoc["sheet"] is JsonValue sv && sv.TryGetValue<string>(out var top)) referenced.Add(top);
        if (spriteDoc["stages"] is JsonArray st2)
            foreach (var node in st2)
                if (node is JsonObject so && so["sheet"] is JsonValue shv && shv.TryGetValue<string>(out var s)) referenced.Add(s);
        string? preview = manifest["preview"] is JsonValue pv && pv.TryGetValue<string>(out var p) ? p : null;
        foreach (var rel in next.Keys.ToList())
            if (rel is not ("pack.json" or "LICENSE") && rel != preview && !referenced.Contains(rel)) next.Remove(rel);

        var listed = new JsonArray();
        foreach (var rel in referenced.Order(StringComparer.Ordinal)) listed.Add((JsonNode?)rel);
        manifest["files"] = listed;
        if (manifest["version"] is JsonValue vv && vv.TryGetValue<string>(out var version)) manifest["version"] = BumpPatch(version);

        next["sprite.json"] = Utf8(spriteDoc.ToJsonString(Indented) + "\n");
        next["pack.json"] = Utf8(manifest.ToJsonString(Indented) + "\n");

        var verdict = PackValidator.Validate(next, folderName: packId, strict: true);
        return verdict.Ok && verdict.Manifest is { } m
            ? new OwnArtResult(next, m, [])
            : new OwnArtResult(null, null, verdict.Errors);
    }

    /// <summary>"1.0.0" to "1.0.1"; anything else stays as it was (the validator then says why).</summary>
    public static string BumpPatch(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3 && int.TryParse(parts[2], out var patch) && patch < int.MaxValue
            ? $"{parts[0]}.{parts[1]}.{patch + 1}"
            : version;
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}
