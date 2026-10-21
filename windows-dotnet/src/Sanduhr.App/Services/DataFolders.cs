using System.IO;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Where the private data really is, for About, Where settings live. On a Store (packaged) install Windows
/// redirects the app's NEW files under AppData into the package's <c>LocalCache</c>, so the folder to show is
/// <c>%LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache\Roaming\Sanduhr</c> (and <c>...\Local\Sanduhr\vault</c>) when
/// it exists; otherwise the plain AppData path. Pure over its inputs so it can be checked without a package.
/// </summary>
public static class DataFolders
{
    public readonly record struct Folders(string Settings, string Vault);

    /// <summary>The folders for this process: the package's LocalCache copies when packaged and present.</summary>
    public static Folders Resolve(Paths paths)
        => Resolve(paths, Startup.IsPackaged(), PackageFamilyName(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static Folders Resolve(Paths paths, bool packaged, string? familyName, string localAppData)
    {
        string settings = paths.AppDataDir;
        string vault = paths.VaultDir;
        if (!packaged || string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(localAppData))
            return new Folders(settings, vault);

        string cache = Path.Combine(localAppData, "Packages", familyName, "LocalCache");
        string cachedSettings = Path.Combine(cache, "Roaming", "Sanduhr");
        string cachedVault = Path.Combine(cache, "Local", "Sanduhr", "vault");
        return new Folders(
            Directory.Exists(cachedSettings) ? cachedSettings : settings,
            Directory.Exists(cachedVault) ? cachedVault : vault);
    }

    private static string? PackageFamilyName()
    {
        try { return Startup.IsPackaged() ? Windows.ApplicationModel.Package.Current.Id.FamilyName : null; }
        catch (Exception) { return null; }
    }
}
