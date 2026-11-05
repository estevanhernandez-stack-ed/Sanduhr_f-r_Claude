namespace Sanduhr.Core;

/// <summary>
/// What an install from a file did: the line Settings shows, and whether anything changed. On success it also names the pack
/// (its id, kind, version, and its name as the line drew it) so Settings can make a sprite the companion and say so.
/// </summary>
public sealed record WidgetFileResult(bool Ok, string Message, string? PackId = null, string? Kind = null, string? ShownName = null,
    string? Version = null, bool Updated = false);

/// <summary>
/// "Install widget from file…" (#408): a pack zip from disk, made by the user or a pack maker such as SpriteForge. It passes
/// the same gates as a catalog download, in the same order: the catalog's zip reader (paths, sizes, file count, no links),
/// then the validator in strict mode, then the version floor. It installs as the user's own (<see cref="WidgetSource.Local"/>),
/// so it is listed as made here and never goes into the catalog. Nothing is sent anywhere.
/// <para>
/// Installing the same id again replaces the pack (an update), and so does a file over a Claude-made pack of that id. A catalog
/// pack of that id is never replaced: <see cref="WidgetStore.Install"/> refuses it with a sentence.
/// </para>
/// Pure apart from the store: bytes in, result out. The file dialog and reading the file are the caller's.
/// </summary>
public static class WidgetFileInstall
{
    /// <summary>The validator's errors shown in the status line; the rest are counted.</summary>
    public const int ErrorsShown = 3;

    /// <param name="showName">How the pack's name is drawn in the line: Settings passes the streamer mask, so a stream shows the
    /// same codename the Installed row does. Defaults to the name itself.</param>
    public static WidgetFileResult Install(byte[] zip, WidgetStore store, Version running, Func<string, string>? showName = null)
    {
        showName ??= static name => name;
        var files = WidgetZip.Read(zip, out var zipError);
        if (files is null)
            return new(false, SettingsNames.FileZipUnsafe(zipError));

        var verdict = PackValidator.Validate(files, folderName: null, strict: true);
        if (!verdict.Ok || verdict.Manifest is not { } manifest)
            return new(false, SettingsNames.FilePackRejected(Errors(verdict.Errors)));
        if (!WidgetCatalog.MeetsMinimum(manifest.SanduhrMin, running))
            return new(false, SettingsNames.FilePackTooNew(showName(manifest.Name), manifest.SanduhrMin));

        bool replacing = store.Load().Find(manifest.Id) is { MadeHere: true };
        var installed = store.Install(files, manifest, WidgetSource.Local, replaceMadeHere: true);
        if (!installed.Ok)
            return new(false, installed.Error ?? SettingsNames.WidgetInstallFailed);
        string shown = showName(manifest.Name);
        return new(true, replacing
            ? SettingsNames.FilePackUpdated(shown, manifest.Version)
            : SettingsNames.FilePackInstalled(shown), manifest.Id, manifest.Kind, shown, manifest.Version, replacing);
    }

    /// <summary>
    /// The line after a sprite from a file became the companion: on the Desk now, or what still stands between it and the Desk
    /// (the Desk is off, or its companion piece is hidden). The setting itself is the caller's.
    /// </summary>
    public static string CompanionLine(WidgetFileResult result, bool deskOn, bool piecePlaced)
    {
        string name = result.ShownName ?? "";
        if (!deskOn) return SettingsNames.FileCompanionDeskOff(name);
        if (!piecePlaced) return SettingsNames.FileCompanionHidden(name);
        return result.Updated
            ? SettingsNames.FileCompanionUpdatedOnDesk(name, result.Version ?? "")
            : SettingsNames.FileCompanionOnDesk(name);
    }

    /// <summary>The first <see cref="ErrorsShown"/> errors, and how many more there are.</summary>
    internal static string Errors(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0) return SettingsNames.OwnArtChecksFailed;
        var shown = string.Join("; ", errors.Take(ErrorsShown));
        int more = errors.Count - ErrorsShown;
        return more > 0 ? SettingsNames.FileMoreErrors(shown, more) : shown + ".";
    }
}
