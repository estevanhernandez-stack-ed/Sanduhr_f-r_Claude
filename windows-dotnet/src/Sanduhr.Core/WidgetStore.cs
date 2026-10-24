using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>Where an installed pack came from (spec 3). A pack keeps its source: an id installed from one source is
/// never replaced by a pack from another.</summary>
public enum WidgetSource
{
    /// <summary>The signed <c>sanduhr-widgets</c> catalog.</summary>
    Catalog,
    /// <summary>Made with Claude (<c>propose_widget</c>, 3.8 item B), installed on Apply. Listed as "made here".</summary>
    Claude,
    /// <summary>The user's own (3.8 item C). Listed as "made here".</summary>
    Local,
}

/// <summary>One row of <c>widgets.json</c>.</summary>
public sealed record InstalledWidget(string Id, string Version, WidgetSource Source, DateTimeOffset InstalledAt, bool Enabled)
{
    /// <summary>Claude-made and the user's own packs are local-only, listed as "made here".</summary>
    public bool MadeHere => Source != WidgetSource.Catalog;
}

/// <summary>What Settings shows for an installed pack, read from its own <c>pack.json</c>.</summary>
public sealed record InstalledWidgetInfo(string Name, string Kind, string Description, string? PreviewPath);

/// <summary>One installed, enabled pack of a kind (<see cref="WidgetStore.InstalledOfKind"/>): its registry row, its own
/// <c>pack.json</c> info and its folder.</summary>
public sealed record InstalledPack(InstalledWidget Row, InstalledWidgetInfo Info, string Dir);

/// <summary><c>widgets.json</c>: the installed packs and the newest catalog <c>generated_at</c> seen (the rollback
/// marker, FORMAT 7 rule 2).</summary>
public sealed record WidgetRegistry(string? CatalogSeen, IReadOnlyList<InstalledWidget> Packs)
{
    public const int SchemaVersion = 1;

    public static WidgetRegistry Empty { get; } = new(null, []);

    public InstalledWidget? Find(string id) => Packs.FirstOrDefault(p => p.Id == id);

    public static string SourceName(WidgetSource source) => source switch
    {
        WidgetSource.Catalog => "catalog",
        WidgetSource.Claude => "claude",
        _ => "local",
    };

    public static WidgetSource? ParseSource(string? s) => s switch
    {
        "catalog" => WidgetSource.Catalog,
        "claude" => WidgetSource.Claude,
        "local" => WidgetSource.Local,
        _ => null,
    };

    /// <summary>Reads <c>widgets.json</c>. Lenient: a file that is not JSON reads as empty, and a row that is not well
    /// formed is skipped (its folder stays until uninstalled or Reset).</summary>
    public static WidgetRegistry Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;
            string? seen = root.TryGetProperty("catalog_generated_at", out var s) && s.ValueKind == JsonValueKind.String
                && WidgetCatalog.ParseStamp(s.GetString()) is not null
                ? s.GetString()
                : null;
            var packs = new List<InstalledWidget>();
            if (root.TryGetProperty("packs", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in list.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    string? id = Str(row, "id"), version = Str(row, "version");
                    var source = ParseSource(Str(row, "source"));
                    var at = WidgetCatalog.ParseStamp(Str(row, "installed_at"));
                    bool enabled = !row.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
                    if (id is null || !WidgetStore.IsSafeId(id) || version is null || source is null || at is null) continue;
                    if (packs.Any(p => p.Id == id)) continue;
                    packs.Add(new InstalledWidget(id, version, source.Value, at.Value, enabled));
                }
            }
            return new WidgetRegistry(seen, [.. packs.OrderBy(p => p.Id, StringComparer.Ordinal)]);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema_version", SchemaVersion);
            if (CatalogSeen is not null) w.WriteString("catalog_generated_at", CatalogSeen);
            else w.WriteNull("catalog_generated_at");
            w.WriteStartArray("packs");
            foreach (var p in Packs.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("id", p.Id);
                w.WriteString("version", p.Version);
                w.WriteString("source", SourceName(p.Source));
                w.WriteString("installed_at", Stamp(p.InstalledAt));
                w.WriteBoolean("enabled", p.Enabled);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    public static string Stamp(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>The result of an install or uninstall: done, or the reason in a sentence for Settings.</summary>
public sealed record WidgetStoreResult(bool Ok, string? Error)
{
    public static WidgetStoreResult Done { get; } = new(true, null);
    public static WidgetStoreResult Failed(string why) => new(false, why);
}

/// <summary>
/// The installed packs on disk (spec 3): <c>%LOCALAPPDATA%\Sanduhr\widgets\&lt;id&gt;\</c>, one folder per pack, and
/// <c>widgets.json</c> beside them. App-private files, so the Store build's package virtualization is fine; nothing
/// outside the app reads them.
///
/// <see cref="Install"/> is atomic (FORMAT 7 rule 6): the pack's files are written to a fresh folder under
/// <c>.staging\</c>, pixel sheets are rasterized to PNG beside them (<c>.raster\&lt;sheet&gt;.png</c>), and only then is
/// the folder renamed into place and the row written. A failure at any step leaves the previous install (or nothing)
/// exactly as it was. Install takes files that already passed <see cref="PackValidator"/> (its manifest is the proof);
/// it re-checks every path against the staging folder anyway.
/// </summary>
public sealed class WidgetStore
{
    public const string RegistryName = "widgets.json";
    public const string StagingName = ".staging";

    /// <summary>The folder inside a pack's install folder holding the PNGs rasterized from its pixel sheets. A pack path
    /// can never start with a dot (FORMAT 3.5), so this can't collide with a pack file.</summary>
    public const string RasterName = ".raster";

    private readonly Func<PixelSheetImage, byte[]> _encodePng;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    /// <summary>Test seams: run after the pack is staged and before it is renamed into place, and after the rename and
    /// before the row is written. A throw there must leave the old install untouched.</summary>
    internal Action? BeforeSwap { get; set; }
    internal Action? BeforeRegistryWrite { get; set; }

    /// <param name="root">The widgets folder (<see cref="Paths.WidgetsDir"/>). Not created until something is installed.</param>
    /// <param name="encodePng">Encodes a rasterized pixel sheet as PNG (the app passes WPF's PngBitmapEncoder).</param>
    public WidgetStore(string root, Func<PixelSheetImage, byte[]> encodePng, Func<DateTimeOffset>? clock = null)
    {
        Root = Path.GetFullPath(root);
        _encodePng = encodePng;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Root { get; }
    public string RegistryFile => Path.Combine(Root, RegistryName);
    public string StagingDir => Path.Combine(Root, StagingName);

    /// <summary>A pack id that is safe as a folder name: FORMAT's id rule (lowercase kebab-case, 3 to 40 characters).</summary>
    public static bool IsSafeId(string? id) =>
        id is { Length: >= 3 and <= 40 } && id[0] is >= 'a' and <= 'z'
        && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !id.EndsWith('-') && !id.Contains("--");

    public string PackDir(string id)
    {
        if (!IsSafeId(id)) throw new ArgumentException("not a pack id", nameof(id));
        return Path.Combine(Root, id);
    }

    /// <summary>Where the PNG for a pixel sheet <paramref name="sheet"/> (a pack path ending <c>.json</c>) is written.</summary>
    public string RasterPath(string id, string sheet) =>
        Path.Combine(PackDir(id), RasterName, sheet.Replace('/', Path.DirectorySeparatorChar) + ".png");

    public WidgetRegistry Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(RegistryFile) ? WidgetRegistry.Parse(File.ReadAllText(RegistryFile, Encoding.UTF8)) : WidgetRegistry.Empty;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return WidgetRegistry.Empty;
            }
        }
    }

    /// <summary>The newest catalog <c>generated_at</c> seen, the rollback marker.</summary>
    public DateTimeOffset? NewestCatalogSeen() => WidgetCatalog.ParseStamp(Load().CatalogSeen);

    /// <summary>Records <paramref name="generatedAt"/> (a catalog's own string) when it is newer than the marker.</summary>
    public void RememberCatalog(string generatedAt)
    {
        var t = WidgetCatalog.ParseStamp(generatedAt);
        if (t is null) return;
        lock (_gate)
        {
            var reg = Load();
            var seen = WidgetCatalog.ParseStamp(reg.CatalogSeen);
            if (seen is not null && seen >= t) return;
            Save(reg with { CatalogSeen = generatedAt });
        }
    }

    /// <summary>
    /// The installed packs of one kind (<c>theme</c>, <c>palette</c>, <c>font</c>, ...) that are enabled, ordered by id: what the
    /// theme list, the Desk ink presets and the Desk fonts read. A pack whose folder is gone or whose <c>pack.json</c>
    /// can't be read is skipped. Claude-made <c>my-</c> packs are rows like any other, so they flow through unchanged.
    /// </summary>
    public IReadOnlyList<InstalledPack> InstalledOfKind(string kind)
    {
        var found = new List<InstalledPack>();
        foreach (var row in Load().Packs.Where(p => p.Enabled && IsSafeId(p.Id)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            string dir = PackDir(row.Id);
            if (Directory.Exists(dir) && ReadInfo(row.Id) is { } info && info.Kind == kind)
                found.Add(new InstalledPack(row, info, dir));
        }
        return found;
    }

    /// <summary>The installed pack's name, kind, description and preview from its own pack.json, or null.</summary>
    public InstalledWidgetInfo? ReadInfo(string id)
    {
        try
        {
            string dir = PackDir(id);
            string file = Path.Combine(dir, "pack.json");
            if (!File.Exists(file)) return null;
            var bytes = File.ReadAllBytes(file);
            if (!PackJson.TryParse(bytes, out var v, out _) || v is not PackObject o) return null;
            string? preview = o["preview"] is string pv && PackValidator.SafeRelPath(pv)
                ? Path.Combine(dir, pv.Replace('/', Path.DirectorySeparatorChar))
                : null;
            return new InstalledWidgetInfo(
                o["name"] as string ?? id,
                o["kind"] as string ?? "",
                o["description"] as string ?? "",
                preview is not null && File.Exists(preview) ? preview : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Installs a validated pack from <paramref name="source"/>. Refused when the id is installed from a different
    /// source. Replaces an install from the same source (an update) atomically. <paramref name="replaceMadeHere"/> lets a
    /// pack made here change hands between made-here sources (a Claude-made sprite the user gave their own art becomes
    /// <see cref="WidgetSource.Local"/>, spec 3.8 section 6); a catalog pack is never replaced that way.
    /// </summary>
    public WidgetStoreResult Install(IReadOnlyDictionary<string, byte[]> files, PackManifest manifest, WidgetSource source,
        bool replaceMadeHere = false)
    {
        if (files is null || manifest is null || !IsSafeId(manifest.Id))
            return WidgetStoreResult.Failed("The pack has no valid id.");
        string id = manifest.Id;
        lock (_gate)
        {
            var reg = Load();
            if (reg.Find(id) is { } existing && existing.Source != source
                && !(replaceMadeHere && existing.MadeHere && source != WidgetSource.Catalog))
                return WidgetStoreResult.Failed(existing.MadeHere
                    ? $"A widget made here is already installed as {id}. Uninstall it first."
                    : $"A catalog widget is already installed as {id}. Uninstall it first.");

            string staging = Path.Combine(StagingDir, id + "-" + Guid.NewGuid().ToString("N")[..12]);
            string target = PackDir(id);
            string? old = null;
            bool swapped = false;
            try
            {
                Directory.CreateDirectory(staging);
                string stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (var (rel, bytes) in files)
                {
                    string full = Inside(stagingRoot, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllBytes(full, bytes);
                }
                if (manifest.Kind == "sprite")
                {
                    foreach (var sheet in PixelSheetsOf(files))
                    {
                        var image = PixelSheet.Rasterize(files[sheet], out var why)
                            ?? throw new InvalidDataException("pixel sheet " + sheet + ": " + why);
                        string png = Inside(stagingRoot, RasterName + "/" + sheet + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                        File.WriteAllBytes(png, _encodePng(image));
                    }
                }

                BeforeSwap?.Invoke();
                if (Directory.Exists(target))
                {
                    old = Path.Combine(StagingDir, id + "-old-" + Guid.NewGuid().ToString("N")[..12]);
                    Directory.Move(target, old);
                }
                Directory.Move(staging, target);
                swapped = true;

                BeforeRegistryWrite?.Invoke();
                var row = new InstalledWidget(id, manifest.Version, source, _clock(), reg.Find(id)?.Enabled ?? true);
                Save(reg with { Packs = [.. reg.Packs.Where(p => p.Id != id), row] });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // Put the previous install back exactly as it was.
                if (swapped) TryDelete(target);
                if (old is not null && !Directory.Exists(target))
                {
                    try { Directory.Move(old, target); old = null; }
                    catch (Exception) { /* left in .staging; cleaned on the next open */ }
                }
                TryDelete(staging);
                return WidgetStoreResult.Failed("The widget couldn't be installed: " + Plain(e));
            }
            if (old is not null) TryDelete(old);
            return WidgetStoreResult.Done;
        }
    }

    /// <summary>
    /// An installed pack's own files read back into memory (relative POSIX path to bytes; the rasterized <c>.raster</c> folder
    /// is Sanduhr's, not the pack's, and is left out). Null when the pack is missing, a file can't be read, or it holds more
    /// than a pack may (<see cref="PackValidator.MaxPackBytes"/>, <see cref="PackValidator.MaxFiles"/>).
    /// </summary>
    public IReadOnlyDictionary<string, byte[]>? ReadFiles(string id)
    {
        try
        {
            string dir = PackDir(id);
            if (!Directory.Exists(dir)) return null;
            string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var full in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith(RasterName + "/", StringComparison.Ordinal)) continue;
                var info = new FileInfo(full);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
                total += info.Length;
                if (total > PackValidator.MaxPackBytes || files.Count > PackValidator.MaxFiles + 3) return null;
                files[rel] = File.ReadAllBytes(full);
            }
            return files;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Removes the pack's folder and its row. The folder is first renamed into <c>.staging\</c>, so a delete
    /// that stops halfway never leaves half a pack where Sanduhr reads packs.</summary>
    public WidgetStoreResult Uninstall(string id)
    {
        if (!IsSafeId(id))
            return WidgetStoreResult.Failed("That isn't an installed widget.");
        lock (_gate)
        {
            var reg = Load();
            string target = PackDir(id);
            if (reg.Find(id) is null && !Directory.Exists(target))
                return WidgetStoreResult.Failed("That widget isn't installed.");
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.CreateDirectory(StagingDir);
                    string gone = Path.Combine(StagingDir, id + "-gone-" + Guid.NewGuid().ToString("N")[..12]);
                    Directory.Move(target, gone);
                    TryDelete(gone);
                }
                Save(reg with { Packs = [.. reg.Packs.Where(p => p.Id != id)] });
                return WidgetStoreResult.Done;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return WidgetStoreResult.Failed("The widget couldn't be removed: " + Plain(e));
            }
        }
    }

    /// <summary>Deletes what an interrupted install or uninstall left in <c>.staging\</c>.</summary>
    public void CleanStaging()
    {
        lock (_gate)
        {
            if (Directory.Exists(StagingDir)) TryDelete(StagingDir);
        }
    }

    /// <summary>The pixel sheets (<c>.json</c> sheets) a validated sprite pack's <c>sprite.json</c> names.</summary>
    public static IReadOnlyList<string> PixelSheetsOf(IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue("sprite.json", out var bytes) || !PackJson.TryParse(bytes, out var v, out _) || v is not PackObject o)
            return [];
        var sheets = new List<string>();
        void Add(object? s)
        {
            if (s is string path && path.EndsWith(".json", StringComparison.Ordinal) && files.ContainsKey(path) && !sheets.Contains(path))
                sheets.Add(path);
        }
        Add(o["sheet"]);
        if (o["stages"] is List<object?> stages)
            foreach (var st in stages)
                if (st is PackObject so) Add(so["sheet"]);
        return sheets;
    }

    private void Save(WidgetRegistry reg)
    {
        Directory.CreateDirectory(Root);
        string tmp = RegistryFile + ".tmp";
        File.WriteAllText(tmp, reg.ToJson(), new UTF8Encoding(false));
        File.Move(tmp, RegistryFile, overwrite: true);
    }

    /// <summary>The full path of <paramref name="rel"/> under <paramref name="rootWithSeparator"/>; throws if it would
    /// land anywhere else.</summary>
    private static string Inside(string rootWithSeparator, string rel)
    {
        if (string.IsNullOrEmpty(rel) || WidgetZip.UnsafeName(rel) is not null)
            throw new InvalidDataException("unsafe path");
        string full = Path.GetFullPath(Path.Combine(rootWithSeparator, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("unsafe path");
        return full;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for CleanStaging or Reset.
        }
    }

    /// <summary>A short reason without a path in it.</summary>
    private static string Plain(Exception e) => e switch
    {
        InvalidDataException d => d.Message + ".",
        UnauthorizedAccessException => "access to the widgets folder was denied.",
        IOException => "the widgets folder couldn't be written.",
        _ => "something went wrong.",
    };
}
