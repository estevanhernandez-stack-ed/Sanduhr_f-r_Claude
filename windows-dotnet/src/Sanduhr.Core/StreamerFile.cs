using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One Claude Code folder as streamer.json lists it: its ref, its codename (null when it has none) and whether its real label shows.</summary>
public sealed record StreamerFolder(string Ref, string? Codename, bool Shown);

/// <summary>
/// <c>%USERPROFILE%\.sanduhr\streamer.json</c> (Settings v2, slice 7; spec, "Two locations, on purpose"). The statusline reads it: streamer
/// mode's state and each folder's codename, keyed by <see cref="FolderRef"/>. No real name goes in. The script's <c>Get-StreamerLabel</c>
/// computes the same ref (a run test pins the two together). App-side Core: not on the MCP link list.
/// </summary>
public static class StreamerFile
{
    public const string FileName = "streamer.json";
    public const int SchemaVersion = 1;
    /// <summary>The script reads no more than this; the widget never writes more.</summary>
    public const int MaxBytes = 65536;

    /// <summary><see cref="SnapshotContract.AccountRef"/> over the folder's canonical key (<see cref="StreamerMask.FolderKey"/>): 8 lowercase
    /// hex digits; empty for a path that can't be read.</summary>
    public static string FolderRef(string path)
    {
        var key = StreamerMask.FolderKey(path);
        return key.Length == 0 ? "" : SnapshotContract.AccountRef(key) ?? "";
    }

    /// <summary>The file's content. Off lists nothing. A shown folder goes under <c>shown</c> with no codename.</summary>
    public static JsonObject Build(bool on, IEnumerable<StreamerFolder> folders)
    {
        var map = new JsonObject();
        var shown = new JsonArray();
        if (on)
        {
            foreach (var f in folders)
            {
                if (!IsRef(f.Ref))
                    continue;
                if (f.Shown) shown.Add(f.Ref);
                else if (f.Codename is { Length: > 0 } code) map[f.Ref] = code;
            }
        }
        return new JsonObject { ["schema_version"] = SchemaVersion, ["on"] = on, ["folders"] = map, ["shown"] = shown };
    }

    /// <summary>Writes atomically, UTF-8 without a BOM. False when it can't.</summary>
    public static bool Write(string path, JsonObject content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsRef(string s) => s.Length == 8 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
