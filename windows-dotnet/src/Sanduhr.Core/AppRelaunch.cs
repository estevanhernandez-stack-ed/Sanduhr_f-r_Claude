namespace Sanduhr.Core;

/// <summary>
/// How Sanduhr starts a new copy of itself for a restart. The Store build runs from WindowsApps, which
/// a process cannot launch by path, so it goes through its app user model id (the manifest's
/// <c>Application Id="Sanduhr"</c>); every other build starts its own executable, and a dev build's
/// <c>SANDUHR_DEV_PROFILE</c> rides along in the inherited environment.
/// </summary>
public static class AppRelaunch
{
    /// <summary>The manifest's application id.</summary>
    public const string PackagedAppId = "Sanduhr";

    /// <summary>The file and arguments to start, or null when there is nothing to start.</summary>
    public static (string FileName, string Arguments)? Target(bool packaged, string? packageFamilyName, string? processPath)
    {
        if (packaged)
        {
            if (string.IsNullOrWhiteSpace(packageFamilyName))
                return null;
            return ("explorer.exe", $"shell:AppsFolder\\{packageFamilyName}!{PackagedAppId}");
        }
        if (string.IsNullOrWhiteSpace(processPath))
            return null;
        return (processPath, "");
    }
}
