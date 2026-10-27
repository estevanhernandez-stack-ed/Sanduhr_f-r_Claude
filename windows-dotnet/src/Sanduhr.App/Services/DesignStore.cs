using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The saved Desk designs on disk (spec, the agent-designed Desk, saved designs): one JSON document per design in
/// <see cref="Paths.DesignsDir"/>, named by its id (<see cref="SavedDesign.ToJson"/>). Ten are kept, the oldest unpinned going first
/// (<see cref="DeskDesignLibrary"/>); a Launcher image stays on disk while a saved design or the Launcher uses it. Reads skip a file
/// that is not a design. Writes go through a temp file. Never throws for an I/O failure; a failed write reports false.
/// </summary>
internal sealed class DesignStore
{
    private const long MaxFileBytes = 256 * 1024;

    private readonly Paths _paths;

    public DesignStore(Paths paths) => _paths = paths;

    /// <summary>The folder (not created until the first save).</summary>
    public string Dir => _paths.DesignsDir;   // streamer: notname - a file location used for IO, never drawn

    /// <summary>Every readable design, newest applied first.</summary>
    public IReadOnlyList<SavedDesign> List()
    {
        var found = new List<SavedDesign>();
        try
        {
            if (!Directory.Exists(Dir)) return found;
            foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
            {
                if (Read(file) is { } d && string.Equals(Path.GetFileNameWithoutExtension(file), d.Id, StringComparison.Ordinal)) found.Add(d);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // What was read so far stands.
        }
        return [.. found.OrderByDescending(d => d.AppliedAt).ThenBy(d => d.Id, StringComparer.Ordinal)];
    }

    public SavedDesign? Load(string? id) =>
        SavedDesign.IsValidId(id) ? Read(Path.Combine(Dir, id + ".json")) : null;

    /// <summary>Writes one design (replacing the one of the same id).</summary>
    public bool Save(SavedDesign design)
    {
        if (!SavedDesign.IsValidId(design.Id)) return false;
        try
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, design.Id + ".json");
            string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                File.WriteAllBytes(temp, new UTF8Encoding(false).GetBytes(design.ToJson().ToJsonString()));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { /* best effort */ }
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds a design (a design of the same id is replaced), prunes to ten (pinned ones stay), deletes the files of the ones that
    /// went and the Launcher images nothing uses any more. Returns the designs kept, newest first.
    /// </summary>
    public IReadOnlyList<SavedDesign> Add(SavedDesign design, LauncherModel currentLauncher)
    {
        var existing = List();
        var pruned = DeskDesignLibrary.Add(existing, design);
        Save(design);
        // The design just added is never the one that goes (a library of pinned designs would otherwise drop it at once, and
        // "Put my Desk back" would point at nothing); the library then holds one over ten until something is unpinned.
        var kept = pruned.Kept.Any(d => d.Id == design.Id) ? pruned.Kept : [.. pruned.Kept, design];
        var removed = pruned.Removed.Where(d => d.Id != design.Id).ToList();
        RemoveFiles(removed, kept, currentLauncher);
        return kept;
    }

    /// <summary>Deletes a design and the Launcher images only it used. Returns false when it was not there.</summary>
    public bool Delete(string id, LauncherModel currentLauncher)
    {
        var all = List();
        var gone = all.FirstOrDefault(d => d.Id == id);
        if (gone is null) return false;
        RemoveFiles([gone], all.Where(d => d.Id != id).ToList(), currentLauncher);
        return true;
    }

    /// <summary>The Launcher images some saved design uses, so removing a button does not delete a picture a design needs.</summary>
    public IReadOnlySet<string> UsedImages() => DeskDesigns.UsedImages(List());

    /// <summary>Whether a Launcher image is on disk.</summary>
    public bool ImageExists(string name) =>
        LauncherButton.IsImageName(name) && File.Exists(Path.Combine(_paths.LauncherDir, name));   // streamer: notname - a file location used for IO, never drawn

    private void RemoveFiles(IReadOnlyList<SavedDesign> removed, IReadOnlyList<SavedDesign> remaining, LauncherModel currentLauncher)
    {
        foreach (var d in removed)
        {
            try
            {
                string path = Path.Combine(Dir, d.Id + ".json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left behind: it is pruned again the next time a design is added.
            }
        }
        var images = DeskDesigns.ImagesToDelete(removed, remaining, currentLauncher);
        if (images.Count > 0) LauncherImages.Delete(_paths, images);
    }

    private static SavedDesign? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            return SavedDesign.Parse(JsonNode.Parse(File.ReadAllBytes(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
