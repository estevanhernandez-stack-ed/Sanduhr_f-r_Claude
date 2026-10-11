using System.IO;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Runs <see cref="PackagedDataMigration"/> on a Store (MSIX) install, first thing in
/// <see cref="App.OnStartup"/>, before anything constructs <see cref="Paths"/>. Store builds
/// before 3.4.3 wrote their data into the package's LocalCache; 3.4.3 reads the real AppData,
/// so this brings it across once. Inert on Velopack and dev builds. Never throws.
/// </summary>
internal static class PackagedDataMigrationRunner
{
    public static void RunIfPackaged()
    {
        try
        {
            if (!Startup.IsPackaged())
                return;

            var familyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localCache = Path.Combine(localAppData, "Packages", familyName, "LocalCache");

            // Buffer the log lines and append them after the run, so the log file itself is
            // created at the real path only once the cached sanduhr.log has had its chance.
            var lines = new List<string>();
            new PackagedDataMigration(localCache, roamingAppData, localAppData, lines.Add).Run();
            AppendLog(lines);
        }
        catch (Exception ex)
        {
            AppendLog(new[] { "packaged-migration not run: " + ex.GetType().Name });
        }
    }

    private static void AppendLog(IReadOnlyCollection<string> lines)
    {
        if (lines.Count == 0)
            return;
        try
        {
            var stamp = DateTime.UtcNow.ToString("o");
            File.AppendAllLines(new Paths().LogFile, lines.Select(l => $"{stamp} {l}"));
        }
        catch
        {
            // Best-effort: a missing log line must never stop the widget starting.
        }
    }
}
