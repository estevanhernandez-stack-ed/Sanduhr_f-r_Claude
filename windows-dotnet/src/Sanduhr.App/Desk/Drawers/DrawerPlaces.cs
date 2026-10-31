namespace Sanduhr.App.Desk;

/// <summary>Where drawers work on this PC: the user's Desktop, the Public Desktop, and the OneDrive roots (for the sync line). Tests pass
/// a sandbox.</summary>
internal sealed record DrawerPlaces(string Desktop, string PublicDesktop, IReadOnlyList<string> Synced)
{
    public static DrawerPlaces ForThisUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable).OfType<string>().Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
}
