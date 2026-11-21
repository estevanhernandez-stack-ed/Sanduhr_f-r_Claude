using System.IO;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The ready-made looks that ship inside the app (5.0 spec 5): the embedded <c>DeskLooks/&lt;id&gt;.json</c> resources, read into Core's
/// <see cref="DeskLookCatalog"/>. A file that does not parse or breaks a rule cannot ship (a test loads all six); at run time a bad
/// one is skipped, named in the debug log, and the gallery shows the others.
/// </summary>
internal static class DeskLookFiles
{
    private const string Prefix = "DeskLooks/";
    private const string Suffix = ".json";

    private static readonly Lazy<DeskLookCatalog> Loaded = new(Read);

    /// <summary>The looks, read once.</summary>
    public static DeskLookCatalog Catalog => Loaded.Value;

    /// <summary>Reads the embedded files now.</summary>
    internal static DeskLookCatalog Read()
    {
        var assembly = typeof(DeskLookFiles).Assembly;
        var files = new List<(string Name, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(Suffix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files.Add((resource[Prefix.Length..^Suffix.Length], reader.ReadToEnd()));
        }
        var catalog = DeskLookCatalog.Load(files);
        foreach (var skipped in catalog.Skipped)
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk look skipped: {skipped}");
        return catalog;
    }
}
